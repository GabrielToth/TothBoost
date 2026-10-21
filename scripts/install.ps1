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
    [string]$Version = "1.0.0",
    [switch]$DesktopShortcut,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
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
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "verify.ps1") -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "Verification failed; installation aborted." }
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\TothBoost"
if (Test-Path $installDir) {
    Write-Step "Stopping any running TothBoost instances"
    Get-Process -Name "TothBoost", "TothBoost-Rust" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

Write-Step "Publishing .NET app to $installDir"
& $dotnet publish "src/TothBoost.App/TothBoost.App.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
    -o $installDir -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Step "Building native Rust engine"
Push-Location "src/tothboost-rs"
try {
    if ($rustToolchain) { & $cargo $rustToolchain build --release } else { & $cargo build --release }
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed." }
    Copy-Item "target\release\tothboost.exe" (Join-Path $installDir "TothBoost-Rust.exe") -Force
} finally {
    Pop-Location
}

Write-Step "Creating shortcuts"
$wsh = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TothBoost.lnk"
$lnk = $wsh.CreateShortcut($startMenu)
$lnk.TargetPath = Join-Path $installDir "TothBoost.exe"
$lnk.WorkingDirectory = $installDir
$lnk.Description = "TothBoost - Windows Gaming Performance Optimizer (Administrator)"
$lnk.IconLocation = (Join-Path $installDir "TothBoost.exe") + ",0"
$lnk.Save()
Write-Host "Start Menu: $startMenu" -ForegroundColor Green

if ($DesktopShortcut) {
    $desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "TothBoost.lnk"
    $lnk = $wsh.CreateShortcut($desktop)
    $lnk.TargetPath = Join-Path $installDir "TothBoost.exe"
    $lnk.WorkingDirectory = $installDir
    $lnk.Description = "TothBoost - Windows Gaming Performance Optimizer (Administrator)"
    $lnk.IconLocation = (Join-Path $installDir "TothBoost.exe") + ",0"
    $lnk.Save()
    Write-Host "Desktop  : $desktop" -ForegroundColor Green
}

Write-Step "Verifying the installed app starts elevated and opens"
$log = Join-Path $env:TEMP "tothboost-installed-selftest.log"
Remove-Item $log -ErrorAction SilentlyContinue
$proc = Start-Process -FilePath (Join-Path $installDir "TothBoost.exe") `
    -ArgumentList "--self-test", $log -Verb RunAs -Wait -PassThru
for ($i = 0; $i -lt 40 -and -not (Test-Path $log); $i++) { Start-Sleep -Milliseconds 250 }
if (-not (Test-Path $log)) { throw "Installed app did not produce a self-test log." }
Get-Content $log | ForEach-Object { Write-Host "    $_" }
if (-not (Select-String -Path $log -Pattern "RESULT=PASS" -Quiet)) {
    throw "Installed app self-test FAILED (exit $($proc.ExitCode))."
}

Write-Step "INSTALLED SUCCESSFULLY"
Write-Host "Location: $installDir" -ForegroundColor Green
Write-Host "Launch  : Start Menu -> TothBoost (always runs as Administrator)" -ForegroundColor Green
exit 0
