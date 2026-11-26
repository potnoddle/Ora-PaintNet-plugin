# Self-elevating code: check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "This script needs to run as Administrator to copy files into Program Files." -ForegroundColor Yellow
    Write-Host "Relaunching with Administrator privileges..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    Exit
}

# Define Paths
$ProjectDir = $PSScriptRoot
$PdnFileTypesDir = "C:\Program Files\paint.net\FileTypes"

# Candidate build output locations for Debug and Release
$CandidateReleaseDirs = @(
    (Join-Path $ProjectDir "bin\Release\net9.0-windows\win-x64"),
    (Join-Path $ProjectDir "bin\Release\net9.0-windows"),
    (Join-Path $ProjectDir "bin\Release")
)
$CandidateDebugDirs = @(
    (Join-Path $ProjectDir "bin\Debug\net9.0-windows\win-x64"),
    (Join-Path $ProjectDir "bin\Debug\net9.0-windows"),
    (Join-Path $ProjectDir "bin\Debug")
)

function Get-LatestDllDir($dirs) {
    $latestDir = $null
    $latestTime = [DateTime]::MinValue
    foreach ($dir in $dirs) {
        $dllPath = Join-Path $dir "OpenRasterFileType.dll"
        if (Test-Path $dllPath) {
            $time = (Get-Item $dllPath).LastWriteTime
            if ($time -gt $latestTime) {
                $latestTime = $time
                $latestDir = $dir
            }
        }
    }
    return $latestDir
}

$latestReleaseDir = Get-LatestDllDir $CandidateReleaseDirs
$latestDebugDir = Get-LatestDllDir $CandidateDebugDirs

# Choose the newest build if both exist
$SourceDir = $null
$config = $null

if ($latestReleaseDir -and $latestDebugDir) {
    $releaseTime = (Get-Item (Join-Path $latestReleaseDir "OpenRasterFileType.dll")).LastWriteTime
    $debugTime = (Get-Item (Join-Path $latestDebugDir "OpenRasterFileType.dll")).LastWriteTime
    if ($releaseTime -gt $debugTime) {
        $SourceDir = $latestReleaseDir
        $config = "Release"
    } else {
        $SourceDir = $latestDebugDir
        $config = "Debug"
    }
} elseif ($latestReleaseDir) {
    $SourceDir = $latestReleaseDir
    $config = "Release"
} elseif ($latestDebugDir) {
    $SourceDir = $latestDebugDir
    $config = "Debug"
} else {
    Write-Host "No compiled OpenRasterFileType.dll found in bin\Release\ or bin\Debug\" -ForegroundColor Red
    Write-Host "Please build the project first by running: dotnet build" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    Exit
}

# Ensure destination directory exists
if (-not (Test-Path $PdnFileTypesDir)) {
    # If base paint.net directory exists, attempt to create FileTypes folder
    $pdnBase = "C:\Program Files\paint.net"
    if (Test-Path $pdnBase) {
        try {
            New-Item -ItemType Directory -Path $PdnFileTypesDir -Force | Out-Null
        } catch { }
    }
}

if (-not (Test-Path $PdnFileTypesDir)) {
    Write-Host "Paint.NET FileTypes folder not found at: $PdnFileTypesDir" -ForegroundColor Red
    $PdnFileTypesDir = Read-Host "Please enter the path to your Paint.NET FileTypes folder"
    if (-not (Test-Path $PdnFileTypesDir)) {
        Write-Host "Invalid path. Exiting." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        Exit
    }
}

# Copy DLL and optional PDB
Write-Host "Copying $config build files from: $SourceDir" -ForegroundColor Cyan
Write-Host "To destination: $PdnFileTypesDir" -ForegroundColor Cyan

try {
    $targetDll = Join-Path $SourceDir "OpenRasterFileType.dll"
    Copy-Item -Path $targetDll -Destination $PdnFileTypesDir -Force -ErrorAction Stop
    Write-Host "Successfully copied OpenRasterFileType.dll to Paint.NET FileTypes!" -ForegroundColor Green

    $targetPdb = Join-Path $SourceDir "OpenRasterFileType.pdb"
    if (Test-Path $targetPdb) {
        Copy-Item -Path $targetPdb -Destination $PdnFileTypesDir -Force -ErrorAction SilentlyContinue
        Write-Host "Copied OpenRasterFileType.pdb for debugging." -ForegroundColor Gray
    }
    
    # Check if Paint.NET is currently running and warn the user
    $pdnProcess = Get-Process -Name "paint.net" -ErrorAction SilentlyContinue
    if ($pdnProcess) {
        Write-Host "Warning: Paint.NET is currently running. You will need to restart it for changes to take effect." -ForegroundColor Yellow
    }
} catch {
    Write-Host "Error copying file: $_" -ForegroundColor Red
}

Read-Host "Press Enter to exit"
