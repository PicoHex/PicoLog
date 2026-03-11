param(
    [string]$ProjectPath = ".\samples\Pico.Logging.Sample\Pico.Logging.Sample.csproj",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = ".\artifacts\aot-sample"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot (Join-Path $OutputRoot $RuntimeIdentifier)))

if (-not (Test-Path $projectFullPath)) {
    throw "Sample project not found: $projectFullPath"
}

if (Test-Path $outputPath) {
    Remove-Item $outputPath -Recurse -Force
}

dotnet publish $projectFullPath -c $Configuration -r $RuntimeIdentifier -p:PublishAOT=true -o $outputPath

$exeName = if ($IsWindows -or $env:OS -eq "Windows_NT") {
    "Pico.Logging.Sample.exe"
}
else {
    "Pico.Logging.Sample"
}

$exePath = Join-Path $outputPath $exeName
$logPath = Join-Path $outputPath "logs\test.log"

if (-not (Test-Path $exePath)) {
    throw "Published sample executable was not found: $exePath"
}

if (Test-Path $logPath) {
    Remove-Item $logPath -Force
}

Push-Location $outputPath

try {
    & $exePath

    if ($LASTEXITCODE -ne 0) {
        throw "Sample exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $logPath)) {
    throw "Sample log file was not produced: $logPath"
}

$contents = Get-Content $logPath -Raw
$requiredMarkers = @(
    "23. Export completed",
    "24. Application shutting down...",
    "25. Press any key to exit..."
)

foreach ($marker in $requiredMarkers) {
    if (-not $contents.Contains($marker)) {
        throw "Published sample log is missing expected marker: $marker"
    }
}

Write-Host "AOT sample validation succeeded."
Write-Host "Executable: $exePath"
Write-Host "Log file: $logPath"