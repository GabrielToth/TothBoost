#Requires -Version 5.1
<#
.SYNOPSIS
    Audits a Windows PE binary for Windows 7 compatibility.

.DESCRIPTION
    Reads the PE headers directly (no SDK/toolchain required) and verifies:
      * the PE subsystem version is 6.01 or lower (Windows 7 is 6.01);
      * the import table does not reference APIs introduced after Windows 7.

    This is build-time evidence only. It cannot replace running the binary on
    a real Windows 7 machine.

.PARAMETER Path
    Path to the .exe file to audit.

.PARAMETER MaximumSubsystemVersion
    Highest accepted subsystem version. Defaults to 6.01.

.EXAMPLE
    ./scripts/check-legacy-pe.ps1 -Path src/tothboost-rs/target-win7/release/tothboost.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [version]$MaximumSubsystemVersion = [version]'6.01'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Binary not found: $Path"
}

$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)

function Read-U16([int]$offset) { [BitConverter]::ToUInt16($bytes, $offset) }
function Read-U32([int]$offset) { [BitConverter]::ToUInt32($bytes, $offset) }

if ((Read-U16 0) -ne 0x5A4D) { throw "Not a PE file (missing MZ header): $Path" }

$peOffset = Read-U32 0x3C
if ((Read-U32 $peOffset) -ne 0x00004550) { throw "Not a PE file (missing PE signature): $Path" }

$machine = Read-U16 ($peOffset + 4)
$sectionCount = Read-U16 ($peOffset + 6)
$optionalSize = Read-U16 ($peOffset + 20)
$optional = $peOffset + 24
$magic = Read-U16 $optional
$is64 = $magic -eq 0x20B

$majorSubsystem = Read-U16 ($optional + 48)
$minorSubsystem = Read-U16 ($optional + 50)
$subsystemValue = Read-U16 ($optional + 68)
$subsystem = switch ($subsystemValue) {
    2 { 'WINDOWS_GUI' }
    3 { 'WINDOWS_CUI' }
    default { "UNKNOWN($subsystemValue)" }
}

$actual = [version]"$majorSubsystem.$minorSubsystem"
$subsystemOk = $actual -le $MaximumSubsystemVersion

# Section table, used to translate RVAs into file offsets.
$sectionTable = $optional + $optionalSize
$sections = for ($i = 0; $i -lt $sectionCount; $i++) {
    $s = $sectionTable + (40 * $i)
    [pscustomobject]@{
        Name    = [Text.Encoding]::ASCII.GetString($bytes, $s, 8).TrimEnd([char]0)
        Virtual = Read-U32 ($s + 12)
        VSize   = Read-U32 ($s + 8)
        RawPtr  = Read-U32 ($s + 20)
        RawSize = Read-U32 ($s + 16)
    }
}

function Convert-RvaToOffset([uint32]$rva) {
    foreach ($s in $sections) {
        $span = [Math]::Max($s.VSize, $s.RawSize)
        if ($rva -ge $s.Virtual -and $rva -lt ($s.Virtual + $span)) {
            return [int]($s.RawPtr + ($rva - $s.Virtual))
        }
    }
    return -1
}

function Read-AsciiZ([int]$offset) {
    $end = $offset
    while ($end -lt $bytes.Length -and $bytes[$end] -ne 0) { $end++ }
    return [Text.Encoding]::ASCII.GetString($bytes, $offset, $end - $offset)
}

# Import directory is data directory index 1.
$dataDirectories = $optional + $(if ($is64) { 112 } else { 96 })
$importRva = Read-U32 ($dataDirectories + 8)

$imports = New-Object System.Collections.Generic.List[string]
$dlls = New-Object System.Collections.Generic.List[string]

if ($importRva -ne 0) {
    $descriptor = Convert-RvaToOffset $importRva
    while ($descriptor -ge 0) {
        $originalFirstThunk = Read-U32 $descriptor
        $nameRva = Read-U32 ($descriptor + 12)
        $firstThunk = Read-U32 ($descriptor + 16)
        if ($nameRva -eq 0 -and $firstThunk -eq 0) { break }

        $dllName = Read-AsciiZ (Convert-RvaToOffset $nameRva)
        $dlls.Add($dllName)

        $thunkRva = if ($originalFirstThunk -ne 0) { $originalFirstThunk } else { $firstThunk }
        $thunk = Convert-RvaToOffset $thunkRva
        while ($thunk -ge 0) {
            $entry = if ($is64) { [BitConverter]::ToUInt64($bytes, $thunk) } else { [uint64](Read-U32 $thunk) }
            if ($entry -eq 0) { break }

            $ordinalFlag = if ($is64) { 0x8000000000000000 } else { 0x80000000 }
            if (($entry -band $ordinalFlag) -eq 0) {
                # IMAGE_IMPORT_BY_NAME: 2-byte hint followed by the ASCII name.
                $imports.Add("$dllName!$(Read-AsciiZ ((Convert-RvaToOffset ([uint32]$entry)) + 2))")
            }

            $thunk += if ($is64) { 8 } else { 4 }
        }

        $descriptor += 20
    }
}

# APIs that only exist on Windows 8 / 8.1 / 10 / 11. Importing any of them makes
# the loader fail on Windows 7 before main() runs.
$postWindows7Apis = @(
    'GetSystemTimePreciseAsFileTime',
    'SetProcessDpiAwarenessContext',
    'GetDpiForWindow',
    'GetDpiForSystem',
    'SetThreadDescription',
    'GetThreadDescription',
    'WaitOnAddress',
    'WakeByAddressAll',
    'WakeByAddressSingle',
    'GetPackageFullName',
    'GetCurrentPackageFullName',
    'DiscardVirtualMemory',
    'OfferVirtualMemory',
    'ReclaimVirtualMemory',
    'PrefetchVirtualMemory',
    'CreateFile2',
    'GetSystemCpuSetInformation',
    'SetThreadSelectedCpuSets',
    'GetProcessMitigationPolicy',
    'SetProcessMitigationPolicy',
    'RegisterBadMemoryNotification',
    'GetThreadInformation',
    'SetThreadInformation',
    'CopyFile2'
)

$offenders = foreach ($import in $imports) {
    $leaf = $import.Split('!')[-1]
    if ($postWindows7Apis -contains $leaf) { $import }
}

Write-Host "PE AUDIT: $Path"
Write-Host ("  Machine            : 0x{0:X4} ({1})" -f $machine, $(if ($machine -eq 0x8664) { 'x86-64' } elseif ($machine -eq 0x14C) { 'x86' } else { 'other' }))
Write-Host ("  Subsystem          : {0} {1}.{2}" -f $subsystem, $majorSubsystem, $minorSubsystem)
Write-Host ("  Imported DLLs      : {0}" -f ($dlls -join ', '))
Write-Host ("  Imported functions : {0}" -f $imports.Count)

if ($offenders) {
    Write-Host "  Post-Windows-7 APIs:"
    foreach ($api in $offenders) { Write-Host "    - $api" }
} else {
    Write-Host "  Post-Windows-7 APIs: none"
}

$importsOk = -not $offenders
if (-not $subsystemOk) {
    Write-Host ("RESULT=FAIL subsystem {0} is newer than {1}" -f $actual, $MaximumSubsystemVersion)
} elseif (-not $importsOk) {
    Write-Host "RESULT=FAIL binary imports APIs unavailable on Windows 7"
} else {
    Write-Host "RESULT=PASS build-time Windows 7 compatibility checks"
}

if (-not ($subsystemOk -and $importsOk)) { exit 1 }
