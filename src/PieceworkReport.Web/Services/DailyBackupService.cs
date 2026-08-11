using PieceworkReport.Core.Services;

namespace PieceworkReport.Web.Services;

public sealed class DailyBackupService(DatabaseBackupService backupService, ILogger<DailyBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await backupService.CreateBackupAsync("daily", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic database backup failed.");
            }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
