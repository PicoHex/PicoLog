param(
    [string]$ProjectPath = ".\samples\Pico.Logging.Sample\Pico.Logging.Sample.csproj",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = ".\artifacts\aot-sample"
)

function Get-CurrentArchitecture {
    [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
}

function Get-TargetArchitecture {
    param([string]$RuntimeIdentifier)
    
    if ($RuntimeIdentifier -match 'x64') {
        'X64'
    }
    elseif ($RuntimeIdentifier -match 'arm64') {
        'Arm64'
    }
    elseif ($RuntimeIdentifier -match 'x86') {
        'X86'
    }
    else {
        'Unknown'
    }
}

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

$currentArch = Get-CurrentArchitecture
$targetArch = Get-TargetArchitecture $RuntimeIdentifier
$canRun = $currentArch -eq $targetArch

if (-not (Test-Path $exePath)) {
    throw "Published sample executable was not found: $exePath"
}

if ($canRun -and (Test-Path $logPath)) {
    Remove-Item $logPath -Force
}

Push-Location $outputPath

try {
    if ($canRun) {
        & $exePath

        if ($LASTEXITCODE -ne 0) {
            throw "Sample exited with code $LASTEXITCODE"
        }
    }
    else {
        Write-Warning "Skipping execution of $RuntimeIdentifier ($targetArch) binary on current architecture ($currentArch). Only compilation validated."
    }
}
finally {
    Pop-Location
}

if ($canRun) {
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
}
else {
    Write-Warning "Skipping log verification for $RuntimeIdentifier ($targetArch) on current architecture ($currentArch)."
}

if ($canRun) {
    Write-Host "AOT sample validation succeeded."
    Write-Host "Executable: $exePath"
    Write-Host "Log file: $logPath"
}
else {
    Write-Host "AOT compilation validation succeeded (execution skipped due to architecture mismatch)."
    Write-Host "Executable: $exePath"
}