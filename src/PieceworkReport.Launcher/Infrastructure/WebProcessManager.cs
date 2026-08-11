using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace PieceworkReport.Launcher.Infrastructure;

public enum WebServiceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    PortConflict,
    Faulted
}

public sealed class WebProcessManager(LauncherPaths paths, int port) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private Process? _process;
    private WindowsJobObject? _job;
    private string? _launcherToken;
    private bool _expectedStop;

    public event EventHandler? StateChanged;
    public WebServiceState State { get; private set; } = WebServiceState.Stopped;
    public string? LastError { get; private set; }
    public int Port { get; private set; } = port;
    public bool IsRunning => State == WebServiceState.Running && _process is { HasExited: false };

    public void UpdatePort(int port)
    {
        if (IsRunning) throw new InvalidOperationException("服务运行时不能直接修改端口。");
        Port = port;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }) return State == WebServiceState.Running;
            LastError = null;
            if (!PortProbe.IsAvailable(Port))
            {
                SetState(WebServiceState.PortConflict, $"端口 {Port} 已被其他程序占用。");
                return false;
            }
            var executable = paths.FindWebExecutable();
            if (!File.Exists(executable))
            {
                SetState(WebServiceState.Faulted, $"未找到 Web 程序：{executable}");
                return false;
            }

            SetState(WebServiceState.Starting);
            _launcherToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _expectedStop = false;
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--DataDirectory");
            startInfo.ArgumentList.Add(paths.DataDirectory);
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add($"http://0.0.0.0:{Port}");
            startInfo.Environment["PIECEWORK_LAUNCHER_TOKEN"] = _launcherToken;
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, args) => WriteLog("INFO", args.Data);
            _process.ErrorDataReceived += (_, args) => WriteLog("ERROR", args.Data);
            _process.Exited += OnProcessExited;
            if (!_process.Start()) throw new InvalidOperationException("Web 程序未能启动。");
            _job = new WindowsJobObject();
            _job.Assign(_process);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var ready = await WaitUntilReadyAsync(_process, cancellationToken);
            if (!ready)
            {
                if (!_process.HasExited) _process.Kill(true);
                SetState(WebServiceState.Faulted, LastError ?? "Web 服务启动超时，请查看启动器日志。");
                return false;
            }
            SetState(WebServiceState.Running);
            return true;
        }
        catch (Exception exception)
        {
            WriteLog("ERROR", exception.ToString());
            SetState(WebServiceState.Faulted, exception.Message);
            return false;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is null || _process.HasExited)
            {
                CleanupProcess();
                SetState(WebServiceState.Stopped);
                return;
            }
            SetState(WebServiceState.Stopping);
            _expectedStop = true;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/internal/launcher/shutdown");
                request.Headers.Add("X-Launcher-Token", _launcherToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) WriteLog("WARN", $"Graceful shutdown returned {(int)response.StatusCode}.");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                WriteLog("WARN", $"Graceful shutdown request failed: {exception.Message}");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await _process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync(cancellationToken);
                WriteLog("WARN", "Web service required forced termination after shutdown timeout.");
            }
            CleanupProcess();
            SetState(WebServiceState.Stopped);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> WaitUntilReadyAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                LastError = $"Web 程序已退出，退出代码 {process.ExitCode}。";
                return false;
            }
            try
            {
                var status = await _httpClient.GetFromJsonAsync<HealthStatus>($"http://127.0.0.1:{Port}/health/ready", timeout.Token);
                if (status?.Status == "ready") return true;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }
            try { await Task.Delay(250, timeout.Token); }
            catch (OperationCanceledException) { break; }
        }
        return false;
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (_expectedStop) return;
        var exitCode = _process?.ExitCode;
        WriteLog("ERROR", $"Web service exited unexpectedly with code {exitCode?.ToString() ?? "unknown"}.");
        SetState(WebServiceState.Faulted, $"Web 服务异常退出，退出代码 {exitCode?.ToString() ?? "未知"}。不会自动重启。请查看日志。 ");
    }

    private void SetState(WebServiceState state, string? error = null)
    {
        State = state;
        if (error is not null) LastError = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WriteLog(string level, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            File.AppendAllText(Path.Combine(paths.LogDirectory, $"web-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private void CleanupProcess()
    {
        if (_process is not null)
        {
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
        }
        _job?.Dispose();
        _job = null;
        _launcherToken = null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally
        {
            _httpClient.Dispose();
            _gate.Dispose();
            _job?.Dispose();
        }
    }

    private sealed record HealthStatus(string Status);
}
