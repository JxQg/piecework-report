[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.1.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$stageDirectory = Join-Path $artifactsRoot "publish-stage-$Version"
$installerDirectory = Join-Path $artifactsRoot 'installer'
$webDirectory = Join-Path $stageDirectory 'web'
$buildDateUtc = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')

& (Join-Path $PSScriptRoot 'generate-icon.ps1')
if (-not $?) { throw 'Application icon generation failed.' }

if (-not $stageDirectory.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $stageDirectory"
}
if (Test-Path -LiteralPath $stageDirectory) {
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $webDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

$commonProperties = @(
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0", "-p:BuildDateUtc=$buildDateUtc"
)

dotnet restore (Join-Path $repositoryRoot 'PieceworkReport.sln')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
dotnet restore (Join-Path $repositoryRoot 'src\PieceworkReport.Launcher\PieceworkReport.Launcher.csproj') -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'Launcher win-x64 restore failed.' }
dotnet restore (Join-Path $repositoryRoot 'src\PieceworkReport.Web\PieceworkReport.Web.csproj') -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'Web win-x64 restore failed.' }
dotnet build (Join-Path $repositoryRoot 'PieceworkReport.sln') -c Release --no-restore `
    "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" "-p:BuildDateUtc=$buildDateUtc"
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
if (-not $SkipTests) {
    dotnet test (Join-Path $repositoryRoot 'PieceworkReport.sln') -c Release --no-restore --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
}

dotnet publish (Join-Path $repositoryRoot 'src\PieceworkReport.Launcher\PieceworkReport.Launcher.csproj') @commonProperties -o $stageDirectory --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
dotnet publish (Join-Path $repositoryRoot 'src\PieceworkReport.Web\PieceworkReport.Web.csproj') @commonProperties -o $webDirectory --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Web publish failed.' }

$developmentSettings = Join-Path $webDirectory 'appsettings.Development.json'
if (Test-Path -LiteralPath $developmentSettings) { Remove-Item -LiteralPath $developmentSettings -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '运行说明.txt') -Destination (Join-Path $stageDirectory '运行说明.txt') -Force
if (Test-Path -LiteralPath (Join-Path $stageDirectory 'data')) { throw 'Release staging must not contain formal data.' }
if (Test-Path -LiteralPath (Join-Path $webDirectory 'data')) { throw 'Web staging must not contain formal data.' }
$forbiddenPayload = Get-ChildItem -LiteralPath $stageDirectory -Recurse -Force -File | Where-Object {
    $_.Extension -in '.db', '.sqlite', '.sqlite3' -or
    $_.FullName -match '[\\/](data|data-demo)([\\/]|$)'
}
if ($forbiddenPayload) {
    throw "Release staging contains forbidden data payload: $($forbiddenPayload[0].FullName)"
}

$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $standardPaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $isccPath = $standardPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
} else {
    $isccPath = $iscc.Source
}
if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw 'Inno Setup 6 was not found. Install it before building the setup package.'
}

$isccOutput = @(& $isccPath "/DMyAppVersion=$Version" "/DSourceDir=$stageDirectory" "/DOutputDir=$installerDirectory" (Join-Path $PSScriptRoot 'PieceworkReport.iss') 2>&1)
$isccExitCode = $LASTEXITCODE
$isccOutput | ForEach-Object { Write-Host $_ }
if ($isccExitCode -ne 0) { throw 'Inno Setup compilation failed.' }
if ($isccOutput -match '^Warning:') { throw 'Inno Setup compilation produced warnings.' }

$installerPath = Join-Path $installerDirectory "PieceworkReport-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer was not produced: $installerPath" }
Write-Host "Installer: $installerPath"
