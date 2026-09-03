using System.Runtime.InteropServices;
using Microsoft.Win32;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Known
{
    public static class TweakCatalog
    {
        public static IReadOnlyList<TweakInfo> All() => new[]
        {
            new TweakInfo
            {
                Id = nameof(TweakKind.GameDvr),
                Name = "Disable Background Recording (Game DVR)",
                Description = "Prevents Xbox Game Bar from recording video/audio in background, freeing up CPU and GPU.",
                Category = "Gaming"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.VisualEffects),
                Name = "Visuals: Best Performance",
                Description = "Disables system animations, transparency, and visual effects (reduces GPU/CPU workload).",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Telemetry),
                Name = "Telemetry: Reduce Data Collection",
                Description = "Sets AllowTelemetry = 0 via local policies. Reversible at any time.",
                Category = "Privacy"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Hibernate),
                Name = "Disable Hibernation",
                Description = "Removes hibernation file (hiberfil.sys) and frees up disk space. Note: PC will not hibernate.",
                Category = "Power"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.UltimatePower),
                Name = "Power Plan: Ultimate Performance",
                Description = "Unlocks and activates Microsoft's 'Ultimate Performance' power plan for maximum FPS.",
                Category = "Power"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.HighPerformance),
                Name = "Power Plan: High Performance",
                Description = "Activates Windows classic 'High Performance' power plan (removes CPU throttling).",
                Category = "Power"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.BlackWallpaper),
                Name = "Background: Solid Black Wallpaper",
                Description = "Changes desktop wallpaper to solid black (ideal for OLED screens and less distraction). Reversible.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Transparency),
                Name = "Disable Transparency Effects",
                Description = "Turns off Windows transparency and blur effects (acrylic). Reversible.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Animations),
                Name = "Disable Windows Animations",
                Description = "Disables window, menu, tooltip, and UI animations for instantaneous system response. Reversible.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Thumbnails),
                Name = "Disable File Thumbnails",
                Description = "Explorer displays icons instead of thumbnail previews and taskbar stops rendering live previews. Reversible.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.NetworkThrottling),
                Name = "Disable Network Throttling for Gaming",
                Description = "Disables Windows network throttling index and reserves 100% CPU responsiveness for active games.",
                Category = "Gaming"
            }
        };

        public static TweakInfo Get(string id) => All().First(t => t.Id == id);
    }

    public static class TweakManager
    {
        private const string BackupKey = @"Software\TothBoost\Tweaks";

        public static bool IsApplied(TweakKind kind) => kind switch
        {
            TweakKind.GameDvr => GetDword(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") == 0,
            TweakKind.VisualEffects => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == 2,
            TweakKind.Telemetry => GetDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0,
            TweakKind.Hibernate => !HibernateEnabled(),
            TweakKind.UltimatePower => GetActiveScheme()?.StartsWith("e9a42b02", StringComparison.OrdinalIgnoreCase) == true,
            TweakKind.HighPerformance => GetActiveScheme()?.StartsWith("8c5e7fda", StringComparison.OrdinalIgnoreCase) == true,
            TweakKind.BlackWallpaper => string.Equals(GetWallpaper(), BlackWallpaperPath, StringComparison.OrdinalIgnoreCase),
            TweakKind.Transparency => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") == 0,
            TweakKind.Animations => GetAnimationFlagsApplied(),
            TweakKind.Thumbnails => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly") == 1,
            TweakKind.NetworkThrottling => NetworkOptimizer.IsNetworkThrottlingDisabled(),
            _ => false
        };

        public static ActionReport Apply(TweakKind kind)
        {
            var report = new ActionReport();
            var name = TweakCatalog.Get(kind.ToString()).Name;
            try
            {
                switch (kind)
                {
                    case TweakKind.GameDvr:
                        SetDword(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                        SetDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.VisualEffects:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.Telemetry:
                        SetDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.Hibernate:
                        Proc.Run("powercfg", "-h off");
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.UltimatePower:
                        Proc.Run("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                        Proc.Run("powercfg", "-setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.HighPerformance:
                        Proc.Run("powercfg", "-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.BlackWallpaper:
                        ApplyBlackWallpaper();
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.Transparency:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.Animations:
                        ApplyAnimations(false);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.Thumbnails:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 1);
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0);
                        report.Add(name, "applied", true);
                        break;
                    case TweakKind.NetworkThrottling:
                        var netRes = NetworkOptimizer.SetNetworkThrottlingDisabled(true);
                        report.Add(name, "applied", netRes.Success, netRes.Message);
                        break;
                }
            }
            catch (Exception ex)
            {
                report.AddError(name, "apply tweak", ex);
            }
            return report;
        }

        public static ActionReport Revert(TweakKind kind)
        {
            var report = new ActionReport();
            var name = TweakCatalog.Get(kind.ToString()).Name;
            try
            {
                switch (kind)
                {
                    case TweakKind.GameDvr:
                        SetDword(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                        DeleteValue(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.VisualEffects:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0);
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.Telemetry:
                        DeleteValue(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.Hibernate:
                        Proc.Run("powercfg", "-h on");
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.UltimatePower:
                    case TweakKind.HighPerformance:
                        Proc.Run("powercfg", "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.BlackWallpaper:
                        RevertBlackWallpaper();
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.Transparency:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.Animations:
                        ApplyAnimations(true);
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.Thumbnails:
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0);
                        SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1);
                        report.Add(name, "reverted", true);
                        break;
                    case TweakKind.NetworkThrottling:
                        var netRev = NetworkOptimizer.SetNetworkThrottlingDisabled(false);
                        report.Add(name, "reverted", netRev.Success, netRev.Message);
                        break;
                }
            }
            catch (Exception ex)
            {
                report.AddError(name, "revert tweak", ex);
            }
            return report;
        }

        private enum Hive { CurrentUser, LocalMachine }

        private static int? GetDword(Hive hive, string path, string valueName)
        {
            using var root = hive == Hive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using var k = root.OpenSubKey(path);
            return k?.GetValue(valueName) is int i ? i : null;
        }

        private static void SetDword(Hive hive, string path, string valueName, int val)
        {
            using var root = hive == Hive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using var k = root.CreateSubKey(path);
            k.SetValue(valueName, val, RegistryValueKind.DWord);
        }

        private static void DeleteValue(Hive hive, string path, string valueName)
        {
            using var root = hive == Hive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using var k = root.OpenSubKey(path, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
        }

        private static bool HibernateEnabled()
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return k?.GetValue("HibernateEnabled") is int i && i != 0;
        }

        private static string? GetActiveScheme()
        {
            var (exit, outStr) = Proc.Run("powercfg", "-getactivescheme");
            return exit == 0 ? outStr : null;
        }

        private static string BlackWallpaperPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TothBoost", "black.png");

        private static string? GetWallpaper()
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return k?.GetValue("WallPaper")?.ToString();
        }

        private static void ApplyBlackWallpaper()
        {
            var dir = Path.GetDirectoryName(BlackWallpaperPath)!;
            Directory.CreateDirectory(dir);
            if (!File.Exists(BlackWallpaperPath))
            {
                File.WriteAllBytes(BlackWallpaperPath, BlackPngBytes());
            }
            var orig = GetWallpaper();
            if (!string.Equals(orig, BlackWallpaperPath, StringComparison.OrdinalIgnoreCase))
            {
                using var bk = Registry.CurrentUser.CreateSubKey(BackupKey);
                bk.SetValue("Wallpaper", orig ?? "", RegistryValueKind.String);
            }
            SystemParametersInfo(0x0014, 0, BlackWallpaperPath, 0x01 | 0x02);
        }

        private static void RevertBlackWallpaper()
        {
            using var bk = Registry.CurrentUser.OpenSubKey(BackupKey);
            var orig = bk?.GetValue("Wallpaper")?.ToString();
            if (!string.IsNullOrEmpty(orig) && File.Exists(orig))
                SystemParametersInfo(0x0014, 0, orig, 0x01 | 0x02);
            else
                SystemParametersInfo(0x0014, 0, "", 0x01 | 0x02);
        }

        /// <summary>Embedded 1x1 black PNG (no external image library required).</summary>
        private static byte[] BlackPngBytes() => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk" +
            "YAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SystemParametersInfo(uint uAction, uint uParam, string lpvParam, uint fuWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uAction, uint uParam, ref ANIMATIONINFO lpvParam, uint fuWinIni);

        [StructLayout(LayoutKind.Sequential)]
        private struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        private static bool GetAnimationFlagsApplied()
        {
            try
            {
                var ani = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                if (!SystemParametersInfo(0x48, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ref ani, 0)) return false;
                return ani.iMinAnimate == 0;
            }
            catch { return false; }
        }

        private static void ApplyAnimations(bool enable)
        {
            try
            {
                var ani = new ANIMATIONINFO
                {
                    cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(),
                    iMinAnimate = enable ? 1 : 0
                };
                SystemParametersInfo(0x47, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ref ani, 0x01 | 0x02);
            }
            catch { }
        }
    }
}
