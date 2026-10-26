#Requires -Version 5.1
<#
.SYNOPSIS
    Installs TothBoost for the current user and verifies it starts correctly.

.DESCRIPTION
    Publishes the .NET app and builds the native Rust engine, copies both to
    %LOCALAPPDATA%\Programs\TothBoost, creates a Start Menu shortcut (and
    optionally a Desktop shortcut), then runs the elevated self-test of the
    installed executable.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/install.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$DesktopShortcut,
    [switch]$WithDotNet,
    [switch]$DotNetShortcut,
    [switch]$SkipTests
)

if ($DotNetShortcut) { $WithDotNet = $true }

$ErrorActionPreference = "Stop"
trap {
    Write-Host "INSTALL ERROR: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
    exit 1
}
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Local installs must never downgrade the app. Default to the newest git tag;
# callers can still override with -Version explicitly.
if (-not $Version) {
    $tag = & git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $Version = $tag.TrimStart('v')
    } else {
        $Version = "1.0.0"
    }
}
Write-Host "Install version: $Version" -ForegroundColor Cyan

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    # Windows PowerShell 5.1 turns native stderr into a terminating error when
    # $ErrorActionPreference is Stop (cargo writes progress to stderr). Keep it
    # non-terminating and check the exit code explicitly.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $fallback = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $fallback) { $dotnet = $fallback } else { throw "dotnet was not found." }
}
$cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
if (-not $cargo) {
    $fallback = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
    if (Test-Path $fallback) { $cargo = $fallback } else { throw "cargo was not found." }
}
$env:PATH = "$(Split-Path $dotnet);$(Split-Path $cargo);$env:PATH"

$rustToolchain = ""
if (-not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
    $rustup = (Get-Command rustup -ErrorAction SilentlyContinue).Source
    if (-not $rustup) { $rustup = Join-Path $env:USERPROFILE ".cargo\bin\rustup.exe" }
    $toolchains = & $rustup toolchain list
    if ($toolchains -match "stable-x86_64-pc-windows-gnu") {
        $rustToolchain = "+stable-x86_64-pc-windows-gnu"
    }
}

if (-not $SkipTests) {
    Write-Step "Running full verification"
    $verifyArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "verify.ps1"),
        "-Version", $Version
    )
    $exit = Invoke-Native "powershell" $verifyArgs
    if ($exit -ne 0) { throw "Verification failed; installation aborted." }
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\TothBoost"
if (Test-Path $installDir) {
    Write-Step "Stopping any running TothBoost instances"
    Get-Process -Name "TothBoost", "TothBoost-Rust" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    $remaining = Get-Process -Name "TothBoost", "TothBoost-Rust" -ErrorAction SilentlyContinue
    if ($remaining) {
        throw "TothBoost is still running (elevated). Close it or run this script as Administrator, then retry."
    }
}

if ($WithDotNet) {
    Write-Step "Publishing .NET app to a clean staging directory"
    $staging = Join-Path $env:TEMP "tothboost-dotnet-$PID"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force $staging | Out-Null
    $publishArgs = @(
        "publish", "src/TothBoost.App/TothBoost.App.csproj",
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0",
        "-o", $staging, "-v", "minimal"
    )
    $exit = Invoke-Native $dotnet $publishArgs
    if ($exit -ne 0) { throw "dotnet publish failed (exit $exit)." }

    # Replace stale runtime files from older installs; mixing an old loose DLL
    # with a new single-file bundle can crash WPF with DllNotFoundException.
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Get-ChildItem $installDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne "TothBoost-Rust.exe" } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $staging "*") $installDir -Recurse -Force
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
} else {
    # The user-facing product is the native engine. Remove any heavy .NET
    # leftovers from previous installs so the install directory stays clean.
    Write-Step "Removing previous .NET WPF files (native-only install)"
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Get-ChildItem $installDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne "TothBoost-Rust.exe" } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Step "Building native Rust engine"
Push-Location "src/tothboost-rs"
try {
    $buildArgs = @("build", "--release")
    if ($rustToolchain) {
        $cargoArgs = @($rustToolchain) + $buildArgs
        $exit = Invoke-Native $cargo $cargoArgs
    } else {
        $exit = Invoke-Native $cargo $buildArgs
    }
    if ($exit -ne 0) { throw "cargo build failed (exit $exit)." }
    Copy-Item "target\release\tothboost.exe" (Join-Path $installDir "TothBoost-Rust.exe") -Force
} finally {
    Pop-Location
}

Write-Step "Creating shortcuts"
# The native Rust engine is the default product: ~680 KB, zero runtime, light
# native Win32 UI. The .NET WPF app is optional and gets its own shortcut.
$wsh = New-Object -ComObject WScript.Shell
$nativeExe = Join-Path $installDir "TothBoost-Rust.exe"
$dotnetExe = Join-Path $installDir "TothBoost.exe"

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TothBoost.lnk"
$lnk = $wsh.CreateShortcut($startMenu)
$lnk.TargetPath = $nativeExe
$lnk.WorkingDirectory = $installDir
$lnk.Description = "TothBoost - native Win32 performance optimizer (Administrator)"
$lnk.IconLocation = $nativeExe + ",0"
$lnk.Save()
Write-Host "Start Menu (default): $startMenu -> $nativeExe" -ForegroundColor Green

if ($WithDotNet -and $DotNetShortcut) {
    $dotnetLnkPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TothBoost (.NET UI).lnk"
    $dotnetLnk = $wsh.CreateShortcut($dotnetLnkPath)
    $dotnetLnk.TargetPath = $dotnetExe
    $dotnetLnk.WorkingDirectory = $installDir
    $dotnetLnk.Description = "TothBoost - .NET 10 WPF UI (Administrator, heavier)"
    $dotnetLnk.IconLocation = $dotnetExe + ",0"
    $dotnetLnk.Save()
    Write-Host "Start Menu (optional): $dotnetLnkPath -> $dotnetExe" -ForegroundColor Green
}

if ($DesktopShortcut) {
    $desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "TothBoost.lnk"
    $lnk = $wsh.CreateShortcut($desktop)
    $lnk.TargetPath = $nativeExe
    $lnk.WorkingDirectory = $installDir
    $lnk.Description = "TothBoost - native Win32 performance optimizer (Administrator)"
    $lnk.IconLocation = $nativeExe + ",0"
    $lnk.Save()
    Write-Host "Desktop  : $desktop -> $nativeExe" -ForegroundColor Green
}

Write-Step "Verifying the installed native app starts elevated and opens"
$nativeLog = Join-Path $env:TEMP "tothboost-rust-selftest.log"
Remove-Item $nativeLog -ErrorAction SilentlyContinue
$proc = Start-Process -FilePath $nativeExe -ArgumentList "--self-test" -Verb RunAs -Wait -PassThru
for ($i = 0; $i -lt 40 -and -not (Test-Path $nativeLog); $i++) { Start-Sleep -Milliseconds 250 }
if (-not (Test-Path $nativeLog)) { throw "Installed native app did not produce a self-test log." }
Get-Content $nativeLog | ForEach-Object { Write-Host "    $_" }
if (-not (Select-String -Path $nativeLog -Pattern "RESULT=PASS" -Quiet)) {
    throw "Installed native app self-test FAILED (exit $($proc.ExitCode))."
}

if ($WithDotNet) {
    Write-Step "Verifying the installed .NET app still starts"
    $dotnetLog = Join-Path $env:TEMP "tothboost-installed-selftest.log"
    Remove-Item $dotnetLog -ErrorAction SilentlyContinue
    $proc = Start-Process -FilePath $dotnetExe `
        -ArgumentList "--self-test", $dotnetLog -Verb RunAs -Wait -PassThru
    for ($i = 0; $i -lt 40 -and -not (Test-Path $dotnetLog); $i++) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path $dotnetLog)) { throw "Installed .NET app did not produce a self-test log." }
    Get-Content $dotnetLog | ForEach-Object { Write-Host "    $_" }
    if (-not (Select-String -Path $dotnetLog -Pattern "RESULT=PASS" -Quiet)) {
        throw "Installed .NET app self-test FAILED (exit $($proc.ExitCode))."
    }
} else {
    $stale = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TothBoost (.NET UI).lnk"
    Remove-Item $stale -ErrorAction SilentlyContinue
}

Write-Step "INSTALLED SUCCESSFULLY"
Write-Host "Location : $installDir" -ForegroundColor Green
Write-Host "Launch   : Start Menu -> TothBoost (native, always runs as Administrator)" -ForegroundColor Green
if ($WithDotNet) {
    Write-Host "Optional : $dotnetExe (.NET 10 WPF UI)" -ForegroundColor Green
}
exit 0
