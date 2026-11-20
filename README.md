# 🚀 TothBoost — Windows 10/11 Performance Optimizer for Gaming

**TothBoost** is a high-performance desktop application and zero-overhead native optimization engine designed to optimize Windows performance for gaming and demanding workloads while maintaining absolute system stability.

TothBoost is available both as a feature-rich **C# / .NET 10 (WPF)** WPF application and as a lightweight, ultra-optimized **Native Rust (`tothboost-rs`)** executable utilizing raw Win32 APIs for microsecond execution times and sub-5MB memory footprint.

---

## 📌 Table of Contents
- [Key Features](#-key-features)
- [Rust Native Engine (`tothboost-rs`)](#-rust-native-engine-tothboost-rs)
- [Installation & Usage](#-installation--usage)
- [Developer Guide](#-developer-guide)
  - [Prerequisites](#prerequisites)
  - [Build & Testing Commands](#build--testing-commands)
- [CI/CD Pipeline](#-cicd-pipeline)
- [Cross-platform Roadmap](docs/ROADMAP.md)
- [License](#-license)

---

## ⚡ Key Features

- **⚡ Native Win32 RAM Trimming**: Empties working set memory (`K32EmptyWorkingSet`) for inactive background processes with zero UI freezing.
- **🧹 System Junk Cleaner**: Automatically purges temporary caches (`%TEMP%`, `C:\Windows\Temp`, `Prefetch`, `D3DSCache`, `SoftwareDistribution`).
- **🌐 Network & Ping Optimization**: Flushes DNS cache, disables Windows Network Throttling (`NetworkThrottlingIndex = 0xFFFFFFFF`), and sets `SystemResponsiveness` to 0.
- **⚙️ Gaming Registry Tweaks**: One-click disabling of Windows GameDVR, background telemetry, and overlay overhead.
- **🛡️ Protected Process Guard**: Automatic safety rules preventing termination or trimming of kernel processes (`csrss.exe`, `lsass.exe`, `svchost.exe`).
- **🚀 Smart Startup Classification**: Evaluates startup entries and scheduled tasks into risk levels (`System (Critical)`, `System (Suggested)`, `Driver (Caution)`, `User (Suggested)`) with safe 1-click disabling of suggested entries.
- **🔥 Barebones "Extreme" Profile**: Auto-generated high-performance gaming profile that sets unneeded services and startup items to disabled, applies all tweaks, blocks telemetry/updater processes, while strictly protecting critical Windows system components.

---

## 🦀 Rust Native Engine (`tothboost-rs`)

For enthusiasts demanding absolute zero overhead and maximum gaming FPS:

- **Executable Size**: ~660 KB standalone executable.
- **Memory Footprint**: ~2–5 MB RAM (95% lower than standard UI frameworks).
- **Execution Speed**: Microsecond response times via pure Win32 bindings (`windows-rs`).
- **Dependencies**: Zero runtime required (no .NET runtime, no WebView2).
- **Five native tabs** (`SysTabControl32` + checkable `SysListView32`):
  Processes, Services, Startup, Tweaks/Performance and Profiles.
- **Persistent blocking**: kill + block a process and it is terminated on
  sight (2-second watchdog, skipped when the list is empty) and its matching
  startup entries are quarantined so it cannot return after a reboot. Stored in
  `%ProgramData%\TothBoost\blocked_processes.json`.
- **Persistent service disabling**: writes both the SCM configuration and the
  registry `Start` value, so disabled services stay disabled across reboots.
  Protected services require an explicit warning confirmation.
- **Reversible startup quarantine**: registry values and Startup-folder files
  are moved to `%ProgramData%\TothBoost\Restoration` and can be restored.
- **Profiles**: capture services, tweaks, startup entries and the blocklist;
  a `Default` baseline and an aggressive `Extreme` gaming profile are created
  automatically. User profiles and the Extreme profile can be customized and
  overwritten with confirmation, while `Default` remains protected as a safe
  restore point. Includes a dedicated "Apply Extreme" one-click action.

### Compatibility (Windows 10/11 vs. legacy)

- **Windows 10/11 x64**: official target for both apps, exercised in CI.
- **Windows 7 SP1 / 8 / 8.1 x64**: `TothBoost-Rust-Win7.exe`, built with Rust
  1.77.0 and PE subsystem 6.01. It is **best-effort**: the build and imports are
  audited automatically, but there is no Windows 7 machine in CI to run it.
- **Windows XP/Vista**: **not supported**. Modern Rust std and .NET 10 cannot
  target XP; a separate legacy codebase would be required.
- The .NET 10 WPF app requires Windows 10+ regardless of packaging options.

See [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) for the full matrix,
evidence and limitations.

```powershell
# Legacy build + PE import audit (Windows 7 SP1 / 8 / 8.1 x64)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-legacy-win7.ps1

# Audit any PE binary (subsystem version + post-Windows-7 imports)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-legacy-pe.ps1 `
  -Path src/tothboost-rs/target-win7/release/tothboost.exe
```

---

## 🚀 Building & Testing

### One-command verification (recommended)
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
```
This runs every unit/UI test, builds both release binaries, publishes the
self-contained .NET app, then launches each executable with `--self-test` to
prove that it **really starts elevated and opens its window**. Any failure
returns a non-zero exit code.

### Rust Native Version (`src/tothboost-rs`)
```powershell
cd src/tothboost-rs
cargo test
cargo build --release
```
On machines without the Visual Studio C++ build tools, use
`cargo +stable-x86_64-pc-windows-gnu test` / `build`. `scripts/verify.ps1`
selects the GNU toolchain automatically.

### .NET 10 WPF Version (`src/TothBoost.App`)
```powershell
dotnet test tests/TothBoost.Core.Tests/TothBoost.Core.Tests.csproj
dotnet build -c Release
```

### Grid behavior
- Both UIs sort by clicking a column header; clicking again reverses the order.
  The native list keeps the chosen sort while its 2-second enforcement/refresh
  cycle runs.
- The .NET WPF process grid sorts numerically for PID, RAM, CPU, GPU, disk
  read/write and alphabetically for text columns; the sort survives the
  2-second auto-refresh.
- .NET RAM shows one decimal (sub-1 MB processes are not rounded to `0`), CPU is
  a real per-process delta, and GPU uses the Windows `GPU Engine` performance
  counters (`—` when the counters are unavailable on the machine).
- The .NET "Columns ▾" button on each tab shows/hides columns. The choice is
  saved in `%ProgramData%\TothBoost\ui-layout.json` and restored on the next
  launch; "Show all columns" resets the layout.

### Privilege requirements
- The .NET app embeds `requireAdministrator` in `app.manifest`, so Windows
  always elevates it before any UI is created.
- The native Rust app checks its own token and re-launches itself through UAC
  (`runas`), preserving command-line arguments.
- `--self-test [logPath]` is available in both apps for automated verification.
  It checks the administrator token, creates/shows the main window, writes a
  `RESULT=PASS` log, and exits without modifying the system.

### Local installation (`scripts/install.ps1`)
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install.ps1
```
Builds and installs the lightweight native engine into
`%LOCALAPPDATA%\Programs\TothBoost`, **always overwriting the previously
installed version**, and points the Start Menu shortcut to it
(`TothBoost-Rust.exe`, ~680 KB, zero runtime). Any heavy .NET files left by an
older install are removed.

Use `-WithDotNet` to also publish the optional .NET 10 WPF app, and
`-DotNetShortcut` to create a separate `TothBoost (.NET UI)` shortcut. Without
`-Version`, the script reads the newest git tag, so a local install never
silently downgrades the app. The installed native app is self-tested at the end.

---

## ⚙️ CI/CD Pipeline

GitHub Actions (`.github/workflows/ci-cd.yml`) runs on every push/PR to `main`,
on `v*` tags, and on manual dispatch. **All build jobs run on Windows
(`windows-latest`) and produce downloadable Windows binaries on every run:**

1. **Test on Windows** — restores and runs all .NET unit/UI tests
   (`dotnet test`) and all native tests (`cargo test`).
2. **Build Windows binaries** — only runs if tests pass:
   - publishes the self-contained .NET app and builds the Rust release binary;
   - runs both `--self-test` smoke checks to confirm elevation + window opening;
   - packages `TothBoost-<version>-windows-x64.zip` and
     `TothBoost-Rust-<version>-windows-x64.zip`;
   - uploads them as workflow artifacts (available for 30 days);
   - on push to `main`, tags, or manual runs, additionally creates/updates
     GitHub Release `v<version>` and marks it as **latest**.

Versions on `main` are generated as `1.0.<run_number>` (and the assembly version
is stamped to match, so the in-app update checker sees the newest release).
Pushing a `v*` tag publishes that exact tag instead. Pull requests get the same
Windows build artifacts without publishing a release.

Linux and macOS are **not** part of CI yet; see the
[cross-platform roadmap](docs/ROADMAP.md) for the feasibility analysis, phased
plan and criteria to revisit them.

