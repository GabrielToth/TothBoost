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
- [License](#-license)

---

## ⚡ Key Features

- **⚡ Native Win32 RAM Trimming**: Empties working set memory (`K32EmptyWorkingSet`) for inactive background processes with zero UI freezing.
- **🧹 System Junk Cleaner**: Automatically purges temporary caches (`%TEMP%`, `C:\Windows\Temp`, `Prefetch`, `D3DSCache`, `SoftwareDistribution`).
- **🌐 Network & Ping Optimization**: Flushes DNS cache, disables Windows Network Throttling (`NetworkThrottlingIndex = 0xFFFFFFFF`), and sets `SystemResponsiveness` to 0.
- **⚙️ Gaming Registry Tweaks**: One-click disabling of Windows GameDVR, background telemetry, and overlay overhead.
- **🛡️ Protected Process Guard**: Automatic safety rules preventing termination or trimming of kernel processes (`csrss.exe`, `lsass.exe`, `svchost.exe`).

---

## 🦀 Rust Native Engine (`tothboost-rs`)

For enthusiasts demanding absolute zero overhead and maximum gaming FPS:

- **Executable Size**: ~320 KB standalone executable.
- **Memory Footprint**: ~2–5 MB RAM (95% lower than standard UI frameworks).
- **Execution Speed**: Microsecond response times via pure Win32 bindings (`windows-rs`).
- **Dependencies**: Zero runtime required (no .NET runtime, no WebView2).

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

### Privilege requirements
- The .NET app embeds `requireAdministrator` in `app.manifest`, so Windows
  always elevates it before any UI is created.
- The native Rust app checks its own token and re-launches itself through UAC
  (`runas`), preserving command-line arguments.
- `--self-test [logPath]` is available in both apps for automated verification.
  It checks the administrator token, creates/shows the main window, writes a
  `RESULT=PASS` log, and exits without modifying the system.

---

## ⚙️ CI/CD Pipeline

GitHub Actions (`.github/workflows/ci-cd.yml`) runs on every push/PR to `main`,
on `v*` tags, and on manual dispatch:

1. **Test job** — restores and runs all .NET unit/UI tests (`dotnet test`) and
   all native tests (`cargo test`) on `windows-latest`.
2. **Release job** (push to `main`, tags, or manual run) — only runs if tests
   pass:
   - publishes the self-contained .NET app and builds the Rust release binary;
   - runs both `--self-test` smoke checks to confirm elevation + window opening;
   - packages `TothBoost-<version>-windows-x64.zip` and
     `TothBoost-Rust-<version>-windows-x64.zip`;
   - creates/updates GitHub Release `v<version>` and marks it as **latest**.

Versions on `main` are generated as `1.0.<run_number>` (and the assembly version
is stamped to match, so the in-app update checker sees the newest release).
Pushing a `v*` tag publishes that exact tag instead.

