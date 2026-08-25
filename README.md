# 🚀 TothBoost — Windows 10/11 Performance Optimizer for Gaming

**TothBoost** is an advanced desktop application built with **C# / .NET 10 (WPF)** designed to optimize Windows performance for gaming and demanding workloads while maintaining absolute system stability and safety via automatic system restore points and baseline snapshots.

---

## 📌 Table of Contents
- [Key Features](#-key-features)
- [How It Works (Architecture & Safety)](#-how-it-works-architecture--safety)
- [Installation & Usage (End Users)](#-installation--usage-end-users)
- [System Requirements & Dependencies](#-system-requirements--dependencies)
- [Developer Guide](#-developer-guide)
  - [Prerequisites](#prerequisites)
  - [Project Structure](#project-structure)
  - [Build & Testing Commands](#build--testing-commands)
  - [Adding New Tweaks or Service Rules](#adding-new-tweaks-or-service-rules)
- [CI/CD Pipeline](#-cicd-pipeline)
- [License](#-license)

---

## ⚡ Key Features

- **⚡ Real-Time Process Manager**: Live CPU, RAM, Disk I/O (Read/Write) metrics using fast Win32 APIs. Asynchronous process termination and restarting with zero UI freezing.
- **🛡️ Service Safety Classification**: Clear separation between user services and protected Windows kernel services/drivers. Prevents accidental system crashes.
- **🚀 1-Click Gaming Optimization**: Disables non-essential background services automatically during gaming sessions.
- **📋 Scheduled Tasks & Startup Management**: Advanced scanner with non-destructive, reversible quarantine for background apps.
- **🛠️ OS Fine-Tuning Tweaks**: High Performance / Ultimate Performance power plans, visual effects reduction, transparency & animation toggles.
- **💾 Multi-Profile Manager**: Save and switch custom optimization profiles while keeping the `Default` baseline untouched.
- **⏪ Integrated System Restore Points**: Creates Windows Restore Points before system modifications.

---

## 🛡️ How It Works (Architecture & Safety)

TothBoost operates under **Safety-First Principles**:

1. **First-Run Baseline Safety Lock**: On first launch, TothBoost automatically creates a Windows Restore Point and saves the `Default` baseline profile containing the exact original state of all services, tasks, startup items, and OS tweaks.
2. **Knowledge Base Heuristics**: Using `knowledge.json` and rule engines in `TothBoost.Core`, services are categorized into:
   - 🔴 **Protected**: Critical kernel/driver components (locked against modification).
   - 🟢 **Recommended for Gaming**: Non-essential services safe to disable for gaming.
   - 🟡 **Caution / Optional**: Feature-dependent services (e.g. printing, Bluetooth).
3. **Reversible Quarantine**: Disabled startup items are never deleted; they are moved to TothBoost's isolation subkeys and quarantine directory (`%ProgramData%\TothBoost\Restoration`), allowing 1-click restoration.

---

## 📥 Installation & Usage (End Users)

No complex installer required. TothBoost is distributed as a single standalone executable (*Single-File Executable*).

1. Visit the **Releases** section on the GitHub repository.
2. Download the latest **`TothBoost.exe`** file.
3. Right-click `TothBoost.exe` and select **"Run as Administrator"** (required to manage Windows services and create restore points).
4. On first launch, TothBoost captures your baseline configuration. You can then apply the **Gaming Profile** or customize tweaks.

---

## 💻 System Requirements & Dependencies

### End Users:
- **Operating System**: Windows 10 (64-bit) or Windows 11 (64-bit).
- **Privileges**: Administrator Rights.
- **Dependencies**: **NONE**. The executable is 100% self-contained, with all libraries and .NET 10 runtime embedded inside `TothBoost.exe`.

---

## 👨💻 Developer Guide

If you wish to build, modify, or contribute to TothBoost:

### Prerequisites
- **SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download) installed.
- **IDE**: Visual Studio 2026 / Visual Studio Code (with C# Dev Kit extension) or JetBrains Rider.
- **Development OS**: Windows 10/11.

### Project Structure

```text
TothBoost/
├── src/
│   ├── TothBoost.Core/         # Core Library (Scanners, KnowledgeBase, Engine, Snapshots)
│   └── TothBoost.App/          # WPF UI (.NET 10, XAML, ViewModels, DWM Themes)
├── tests/
│   └── TothBoost.Core.Tests/   # Automated Unit Tests (xUnit)
├── .github/workflows/
│   └── ci-cd.yml               # GitHub Actions CI/CD Pipeline
├── publish/                    # Published Output Binaries
└── TothBoost.slnx              # .NET 10 Solution Format
```

### Build & Testing Commands

Open terminal in repository root:

1. **Restore Dependencies**:
   ```bash
   dotnet restore
   ```

2. **Build Solution**:
   ```bash
   dotnet build
   ```

3. **Run Application locally**:
   ```bash
   dotnet run --project src/TothBoost.App/TothBoost.App.csproj
   ```

4. **Execute Automated Unit Tests**:
   ```bash
   dotnet test
   ```

5. **Publish Standalone Single-File Executable (`TothBoost.exe`)**:
   ```bash
   dotnet publish src/TothBoost.App/TothBoost.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/
   ```

### Adding New Tweaks or Service Rules

- **Adding a new OS Tweak**:
  1. Add key enum entry to `TweakKind` in `TothBoost.Core/Models/Models.cs`.
  2. Define metadata and description in `TweakCatalog.cs`.
  3. Implement detection (`IsApplied`), application (`Apply`), and reversal (`Revert`) logic in `TweakManager.cs`.
- **Updating Service Safety Rules**:
  1. Modify `src/TothBoost.Core/Known/knowledge.json` specifying service name, category, risk level (`recommended`, `protected`, `warn`, `optional`), and explanation.

---

## 🔄 CI/CD Pipeline

The repository features active continuous integration and continuous delivery via **GitHub Actions** (`.github/workflows/ci-cd.yml`):
- On every *Push* or *Pull Request* to `main`, unit tests execute and `TothBoost.exe` is compiled as a build artifact.
- Pushing a release tag (e.g. `v1.0.0`) automatically publishes a **GitHub Release** with `TothBoost.exe` attached.

---

## 📜 License

Developed by Gabriel Toth. All rights reserved.
