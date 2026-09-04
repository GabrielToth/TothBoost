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

### Rust Native Version (`src/tothboost-rs`)
```powershell
cd src/tothboost-rs
cargo test
cargo build --release
```

### .NET 10 WPF Version (`src/TothBoost.App`)
```powershell
dotnet test tests/TothBoost.Core.Tests/TothBoost.Core.Tests.csproj
dotnet build -c Release
```

---

## ⚙️ CI/CD Pipeline

Automated builds and tests are executed on Windows Runners via GitHub Actions (`.github/workflows/ci-cd.yml`):
- Executes all Rust unit tests (`cargo test`).
- Compiles the release binary (`cargo build --release`).
- Automatically attaches `tothboost.exe` as a release build artifact.
