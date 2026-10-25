#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the native TothBoost executable for Windows 7 SP1 / 8 / 8.1.

.DESCRIPTION
    Uses Rust 1.77.0, the last toolchain whose standard library still supports
    Windows 7, and forces PE subsystem version 6.01 in the linker. The result is
    audited by scripts/check-legacy-pe.ps1 before being copied to dist/legacy.

    Notes:
      * The legacy binary is 64-bit only (x86-64).
      * Windows 7 SP1 needs KB2533623 for the Rust standard library loader.
      * This is best-effort compatibility. Only a real Windows 7/8/8.1 machine
        can prove runtime behaviour.

.PARAMETER OutputDirectory
    Where the audited executable is copied. Defaults to dist/legacy.

.PARAMETER SkipAudit
    Build only, without running the PE compatibility audit.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'dist/legacy',
    [switch]$SkipAudit
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$crateDir = Join-Path $repoRoot 'src/tothboost-rs'
$toolchain = '1.77.0-x86_64-pc-windows-gnu'
$targetDir = Join-Path $crateDir 'target-win7'

$cargoBin = Join-Path $env:USERPROFILE '.cargo/bin'
$env:PATH = "$cargoBin;$env:PATH"

if (-not (Get-Command rustup -ErrorAction SilentlyContinue)) {
    throw 'rustup was not found. Install it from https://rustup.rs and retry.'
}

$installed = rustup toolchain list | Select-String -SimpleMatch $toolchain
if (-not $installed) {
    Write-Host "Installing Rust $toolchain (last release with Windows 7 std support)..."
    rustup toolchain install $toolchain --profile minimal
    if ($LASTEXITCODE -ne 0) { throw "Failed to install $toolchain" }
}

Push-Location $crateDir
try {
    # Cargo 1.77 cannot read lockfile version 4. Keep version 3 in the repo so
    # both the modern and the legacy toolchain can build from the same lockfile.
    $lockPath = Join-Path $crateDir 'Cargo.lock'
    if (Test-Path $lockPath) {
        $lock = Get-Content $lockPath -Raw
        if ($lock -match '(?m)^version = 4\s*$') {
            Write-Host 'Rewriting Cargo.lock from version 4 to version 3 for the legacy toolchain.'
            ($lock -replace '(?m)^version = 4\s*$', 'version = 3') | Set-Content $lockPath -NoNewline
        }
    }

    $env:RUSTFLAGS = '-C link-arg=-Wl,--major-subsystem-version=6 -C link-arg=-Wl,--minor-subsystem-version=1'

    Write-Host "Building TothBoost legacy native binary with $toolchain..."
    cargo "+$toolchain" build --release --locked --target-dir $targetDir
    if ($LASTEXITCODE -ne 0) { throw "Legacy build failed with exit code $LASTEXITCODE" }

    $exe = Join-Path $targetDir 'release/tothboost.exe'
    if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }

    if (-not $SkipAudit) {
        & (Join-Path $PSScriptRoot 'check-legacy-pe.ps1') -Path $exe
        if ($LASTEXITCODE -ne 0) { throw 'Legacy PE audit failed.' }
    }

    $outDir = Join-Path $repoRoot $OutputDirectory
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $destination = Join-Path $outDir 'TothBoost-Rust-Win7.exe'
    Copy-Item $exe $destination -Force

    $item = Get-Item $destination
    Write-Host ""
    Write-Host "Legacy binary: $($item.FullName)"
    Write-Host ("Size: {0:N0} bytes" -f $item.Length)
    Write-Host 'Targets: Windows 7 SP1, Windows 8, Windows 8.1 (x86-64)'
}
finally {
    Remove-Item Env:RUSTFLAGS -ErrorAction SilentlyContinue
    Pop-Location
}
