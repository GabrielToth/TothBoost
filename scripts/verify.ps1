#Requires -Version 5.1
<#
.SYNOPSIS
    Full local verification for TothBoost.

.DESCRIPTION
    Builds and tests both engines, publishes the .NET app, then launches each
    executable with --self-test to prove that it really starts with an
    Administrator token and opens its window. Exits non-zero on any failure.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/verify.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [switch]$SkipPublish,
    [switch]$SkipLegacy
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Resolve-Tool([string]$Name, [string]$Fallback) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if (Test-Path $Fallback) { return $Fallback }
    throw "$Name was not found. Install it or add it to PATH."
}

$dotnet = Resolve-Tool "dotnet" (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
$cargo = Resolve-Tool "cargo" (Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe")
$env:PATH = "$(Split-Path $dotnet);$(Split-Path $cargo);$env:PATH"

# Rust on a machine without Visual Studio needs the GNU toolchain. Select it
# automatically so the same script works on dev boxes and GitHub runners.
$rustToolchain = ""
& $cargo --version *> $null
if (-not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
    $rustup = Resolve-Tool "rustup" (Join-Path $env:USERPROFILE ".cargo\bin\rustup.exe")
    $toolchains = & $rustup toolchain list
    if ($toolchains -match "stable-x86_64-pc-windows-gnu") {
        $rustToolchain = "+stable-x86_64-pc-windows-gnu"
        Write-Host "No MSVC linker found; using Rust GNU toolchain." -ForegroundColor Yellow
    }
}

function Invoke-Cargo([string[]]$Arguments) {
    if ($rustToolchain) {
        & $cargo $rustToolchain @Arguments
    } else {
        & $cargo @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "cargo $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
}

Write-Step "Checking mandatory Administrator manifest"
$manifest = Get-Content "src/TothBoost.App/app.manifest" -Raw
if ($manifest -notmatch 'requestedExecutionLevel level="requireAdministrator"') {
    throw "app.manifest no longer requires Administrator."
}
Write-Host "OK: app.manifest requires Administrator." -ForegroundColor Green

Write-Step "Running .NET tests"
& $dotnet test "tests/TothBoost.Core.Tests/TothBoost.Core.Tests.csproj" -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

Write-Step "Running Rust tests"
Push-Location "src/tothboost-rs"
try {
    Invoke-Cargo @("test")
    Write-Step "Building Rust release binary"
    Invoke-Cargo @("build", "--release")
    $rustExe = Join-Path (Get-Location) "target\release\tothboost.exe"
} finally {
    Pop-Location
}

$legacyExe = $null
if (-not $SkipLegacy) {
    Write-Step "Building and auditing legacy Rust binary (Windows 7 SP1 / 8 / 8.1)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-legacy-win7.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Legacy Windows 7 build/audit failed." }
    $legacyExe = Join-Path $repoRoot "dist\legacy\TothBoost-Rust-Win7.exe"
    if (-not (Test-Path $legacyExe)) { throw "Legacy binary not found: $legacyExe" }
}

$dotnetDir = Join-Path $repoRoot "dist\verify\dotnet"
if (-not $SkipPublish) {    Write-Step "Publishing .NET app (self-contained, single file)"
    & $dotnet publish "src/TothBoost.App/TothBoost.App.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:Version=$Version `
        -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
        -o $dotnetDir -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

$dotnetExe = Join-Path $dotnetDir "TothBoost.exe"
if (-not (Test-Path $dotnetExe)) { throw "Published executable not found: $dotnetExe" }

function Invoke-SelfTest([string]$Exe, [string]$LogPath, [string[]]$ExtraArgs = @()) {
    Remove-Item $LogPath -ErrorAction SilentlyContinue
    $procArgs = @("--self-test") + $ExtraArgs
    # -Verb RunAs satisfies the requireAdministrator manifest and the native
    # auto-elevation path, and is silent when UAC is configured not to prompt.
    $proc = Start-Process -FilePath $Exe -ArgumentList $procArgs -Verb RunAs -Wait -PassThru
    for ($i = 0; $i -lt 40 -and -not (Test-Path $LogPath); $i++) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path $LogPath)) { throw "Self-test log not produced: $LogPath" }

    Get-Content $LogPath | ForEach-Object { Write-Host "    $_" }
    if (-not (Select-String -Path $LogPath -Pattern "RESULT=PASS" -Quiet)) {
        throw "Self-test FAILED for $Exe (exit $($proc.ExitCode))."
    }
    if (-not (Select-String -Path $LogPath -Pattern "ELEVATED=OK" -Quiet)) {
        throw "Executable did not run elevated: $Exe"
    }
    if (-not (Select-String -Path $LogPath -Pattern "WINDOW_VISIBLE=OK" -Quiet)) {
        throw "Executable did not open its window: $Exe"
    }
}

Write-Step "Verifying native app opens with Administrator rights"
$rustLog = Join-Path $env:TEMP "tothboost-rust-selftest.log"
Invoke-SelfTest $rustExe $rustLog

Write-Step "Verifying .NET app opens with Administrator rights"
$dotnetLog = Join-Path $env:TEMP "tothboost-dotnet-selftest.log"
Invoke-SelfTest $dotnetExe $dotnetLog @($dotnetLog)

Write-Step "ALL CHECKS PASSED"
Write-Host "Rust binary : $rustExe" -ForegroundColor Green
if ($legacyExe) {
    Write-Host "Legacy x64  : $legacyExe (Windows 7 SP1 / 8 / 8.1, build-audited)" -ForegroundColor Green
}
Write-Host ".NET binary : $dotnetExe" -ForegroundColor Green
exit 0
