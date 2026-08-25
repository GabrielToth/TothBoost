using System.Runtime.InteropServices;
using Microsoft.Win32;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Known
{
    public enum TweakKind
    {
        GameDvr,
        VisualEffects,
        Telemetry,
        Hibernate,
        UltimatePower,
        BlackWallpaper,
        Transparency,
        Animations,
        Thumbnails,
        HighPerformance
    }

    public static class TweakCatalog
    {
        public static IReadOnlyList<TweakInfo> All() => new[]
        {
            new TweakInfo
            {
                Id = nameof(TweakKind.GameDvr),
                Name = "Desativar gravação de fundo (Game DVR)",
                Description = "Impede que a Xbox Game Bar grave vídeo/áudio do jogo em segundo plano, liberando CPU e GPU.",
                Category = "Jogos"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.VisualEffects),
                Name = "Visual: melhor desempenho",
                Description = "Desativa animações, transparência e efeitos visuais do sistema (menos trabalho para a GPU).",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Telemetry),
                Name = "Telemetria: reduzir coleta de dados",
                Description = "Define AllowTelemetry = 0 via política local. Reversível a qualquer momento.",
                Category = "Privacidade"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Hibernate),
                Name = "Hibernação desligada",
                Description = "Remove o arquivo de hibernação (hiberfil.sys) e libera espaço em disco. Nota: o PC não hiberna mais.",
                Category = "Energia"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.UltimatePower),
                Name = "Plano de energia: Desempenho Máximo",
                Description = "Ativa o plano 'Desempenho Máximo' (Ultimate Performance), oculto no Painel de Controle por padrão.",
                Category = "Energia"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.HighPerformance),
                Name = "Plano de energia: Alto Desempenho",
                Description = "Ativa o plano clássico 'Alto desempenho' do Windows (sem limites de CPU/HD).",
                Category = "Energia"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.BlackWallpaper),
                Name = "Plano de fundo: cor preta",
                Description = "Troca o papel de parede por cor preta lisa (ideal para telas OLED e menos distração). Reversível.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Transparency),
                Name = "Remover transparência",
                Description = "Desliga os efeitos de transparência/blur do Windows (acrílico). Reversível.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Animations),
                Name = "Remover animações do Windows",
                Description = "Desativa animações de janelas, menus, tooltips e efeitos de interface para resposta mais rápida. Reversível.",
                Category = "Visual"
            },
            new TweakInfo
            {
                Id = nameof(TweakKind.Thumbnails),
                Name = "Remover miniaturas",
                Description = "Explorer mostra ícones em vez de miniaturas e a barra de tarefas não exibe pré-visualização. Reversível.",
                Category = "Visual"
            }
        };

        public static TweakInfo Get(string id) => All().First(t => t.Id == id);
    }

    public static class TweakManager
    {
        private const string BackupKey = @"Software\TothBoost\Ajustes";

        public static bool IsApplied(TweakKind kind) => kind switch
        {
            TweakKind.GameDvr => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameConfigStore", "GameDVR_Enabled") == 0,
            TweakKind.VisualEffects => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == 2,
            TweakKind.Telemetry => GetDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0,
            TweakKind.Hibernate => !HibernateEnabled(),
            TweakKind.UltimatePower => GetActiveScheme()?.StartsWith("e9a42b02", StringComparison.OrdinalIgnoreCase) == true,
            TweakKind.HighPerformance => GetActiveScheme()?.StartsWith("8c5e7fda", StringComparison.OrdinalIgnoreCase) == true,
            TweakKind.BlackWallpaper => string.Equals(GetWallpaper(), BlackWallpaperPath, StringComparison.OrdinalIgnoreCase),
            TweakKind.Transparency => GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") == 0,
            TweakKind.Animations => GetAnimationFlagsApplied(),
            TweakKind.Thumbnails =>
                GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly") == 1 &&
                GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn") == 0,
            _ => false
        };

        public static void Apply(TweakKind kind)
        {
            switch (kind)
            {
                case TweakKind.GameDvr:
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameConfigStore", "GameDVR_Enabled", 0);
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameConfigStore", "GameDVR_FSEBehaviorMode", 0);
                    break;
                case TweakKind.VisualEffects:
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
                    break;
                case TweakKind.Telemetry:
                    SetDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                    break;
                case TweakKind.Hibernate:
                    var (ex, outStr) = Proc.Run("powercfg.exe", "/h off");
                    if (ex != 0) throw new InvalidOperationException("Falha ao desligar hibernação: " + outStr.Trim());
                    break;
                case TweakKind.UltimatePower:
                    ApplyUltimatePower();
                    break;
                case TweakKind.HighPerformance:
                    ApplyHighPerformance();
                    break;
                case TweakKind.BlackWallpaper:
                    ApplyBlackWallpaper();
                    break;
                case TweakKind.Transparency:
                    var prevT = GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
                    if (prevT.HasValue) SetDword(Hive.CurrentUser, BackupKey, "PrevEnableTransparency", prevT.Value);
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
                    break;
                case TweakKind.Animations:
                    ApplyAnimations(false);
                    break;
                case TweakKind.Thumbnails:
                    var prevIcon = GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly");
                    var prevSn = GetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn");
                    if (prevIcon.HasValue) SetDword(Hive.CurrentUser, BackupKey, "PrevIconsOnly", prevIcon.Value);
                    if (prevSn.HasValue) SetDword(Hive.CurrentUser, BackupKey, "PrevTaskbarSn", prevSn.Value);
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 1);
                    SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn", 0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static void Revert(TweakKind kind)
        {
            switch (kind)
            {
                case TweakKind.GameDvr:
                    DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameConfigStore", "GameDVR_Enabled");
                    DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameConfigStore", "GameDVR_FSEBehaviorMode");
                    break;
                case TweakKind.VisualEffects:
                    DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
                    break;
                case TweakKind.Telemetry:
                    DeleteDword(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
                    break;
                case TweakKind.Hibernate:
                    Proc.Run("powercfg.exe", "/h on");
                    break;
                case TweakKind.UltimatePower:
                    RestoreUltimatePower();
                    break;
                case TweakKind.HighPerformance:
                    RestoreHighPerformance();
                    break;
                case TweakKind.BlackWallpaper:
                    RestoreWallpaper();
                    break;
                case TweakKind.Transparency:
                    var backT = GetDword(Hive.CurrentUser, BackupKey, "PrevEnableTransparency");
                    if (backT.HasValue) { SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", backT.Value); DeleteValue(Hive.CurrentUser, BackupKey, "PrevEnableTransparency"); }
                    else DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
                    break;
                case TweakKind.Animations:
                    ApplyAnimations(true);
                    break;
                case TweakKind.Thumbnails:
                    var backIcon = GetDword(Hive.CurrentUser, BackupKey, "PrevIconsOnly");
                    var backSn = GetDword(Hive.CurrentUser, BackupKey, "PrevTaskbarSn");
                    if (backIcon.HasValue) { SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", backIcon.Value); DeleteValue(Hive.CurrentUser, BackupKey, "PrevIconsOnly"); }
                    else DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly");
                    if (backSn.HasValue) { SetDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn", backSn.Value); DeleteValue(Hive.CurrentUser, BackupKey, "PrevTaskbarSn"); }
                    else DeleteDword(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarSn");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        // ---------- energia ----------

        private static void ApplyUltimatePower()
        {
            var current = GetActiveScheme();
            if (!string.IsNullOrEmpty(current))
                SetString(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid", current);

            var (ex1, o1) = Proc.Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
            if (ex1 != 0 && !o1.Contains("already", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("powercfg duplicate scheme falhou: " + o1.Trim());

            var (ex2, o2) = Proc.Run("powercfg.exe", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
            if (ex2 != 0 && !o2.Contains("not found", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("powercfg setactive falhou: " + o2.Trim());
        }

        private static void RestoreUltimatePower()
        {
            var prev = GetString(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid");
            if (string.IsNullOrEmpty(prev)) return;
            Proc.Run("powercfg.exe", $"/setactive {prev}");
            DeleteValue(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid");
        }

        // ---------- plano alto desempenho ----------

        private static void ApplyHighPerformance()
        {
            var current = GetActiveScheme();
            if (!string.IsNullOrEmpty(current))
                SetString(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid2", current);
            var (ex, o) = Proc.Run("powercfg.exe", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            if (ex != 0 && !o.Contains("not found", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("powercfg setactive falhou: " + o.Trim());
        }

        private static void RestoreHighPerformance()
        {
            var prev = GetString(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid2");
            if (string.IsNullOrEmpty(prev)) return;
            Proc.Run("powercfg.exe", $"/setactive {prev}");
            DeleteValue(Hive.CurrentUser, BackupKey, "PrevActivePowerSchemeGuid2");
        }

        // ---------- papel de parede preto ----------

        private const string BlackWallpaperId = "TOTHBOOST_BLACK";
        private static readonly string BlackWallpaperPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TothBoost", "black.bmp");

        private static void ApplyBlackWallpaper()
        {
            var current = GetWallpaper();
            if (!string.IsNullOrEmpty(current))
                SetString(Hive.CurrentUser, BackupKey, "PrevWallpaper", current);
            WriteBlackBitmap(BlackWallpaperPath);
            SetWallpaper(BlackWallpaperPath);
        }

        private static void RestoreWallpaper()
        {
            var prev = GetString(Hive.CurrentUser, BackupKey, "PrevWallpaper");
            DeleteValue(Hive.CurrentUser, BackupKey, "PrevWallpaper");
            SetWallpaper(string.IsNullOrEmpty(prev) ? "" : prev);
        }

        private static void WriteBlackBitmap(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // BMP 1x1, 24bpp, preto puro
            var data = new byte[]
            {
                0x42, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
                0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
                0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
                0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00
            };
            File.WriteAllBytes(path, data);
        }

        // ---------- animações (SPI) ----------

        private static readonly (uint Get, uint Set, string Name)[] AnimationSpis =
        {
            (0x1042, 0x1043, "ClientArea"),   // animação de redimensionar janelas
            (0x1015, 0x1016, "Tooltip"),      // animação de tooltip
            (0x1013, 0x1014, "TooltipFade"),
            (0x1003, 0x1004, "Combobox"),
            (0x1009, 0x100A, "ListboxSmooth"),
            (0x1001, 0x1002, "MenuAnim"),
            (0x1011, 0x1012, "MenuFade"),
            (0x1007, 0x1008, "GradientCaptions"),
            (0x101D, 0x101E, "SelectionFade"),
            (0x103E, 0x101F, "UiEffects"),
            (0x1000, 0x1000, "ActiveTracking"),
            (0x101A, 0x101A, "CursorShadow"),
            (0x48,   0x49,   "Minimize")     // ANIMATIONINFO (minimizar/maximizar)
        };

        private static bool GetAnimationFlagsApplied()
        {
            try
            {
                // critério: animação de minimizar desligada
                var ani = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                if (!SystemParametersInfo(0x48, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ref ani, 0)) return false;
                return ani.iMinAnimate == 0;
            }
            catch { return false; }
        }

        private static void ApplyAnimations(bool restore)
        {
            var prev = new List<uint>();
            try
            {
                foreach (var (get, set, name) in AnimationSpis)
                {
                    uint v = 1;
                    if (!restore)
                    {
                        try { SystemParametersInfo(get, 0, ref v, 0); } catch { }
                        prev.Add(v);
                    }
                    if (name == "Minimize")
                    {
                        var ani = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = (int)(restore ? v : 0) };
                        SystemParametersInfo(set, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ref ani, 0x03);
                    }
                    else
                    {
                        var val = restore ? v : 0u;
                        SystemParametersInfo(set, 0, ref val, 0x03);
                    }
                }
                if (!restore)
                    SetString(Hive.CurrentUser, BackupKey, "PrevAnimFlags", string.Join(",", prev));
                else
                    DeleteValue(Hive.CurrentUser, BackupKey, "PrevAnimFlags");
            }
            catch { }
        }

        // ---------- wallpaper via SPI ----------

        private static string? GetWallpaper()
        {
            var sb = new System.Text.StringBuilder(512);
            if (SystemParametersInfo(0x73, 512, sb, 0)) return sb.ToString();
            return null;
        }

        private static void SetWallpaper(string path)
        {
            SystemParametersInfo(0x14, 0, path, 0x03); // SPI_SETDESKWALLPAPER
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.Text.StringBuilder pvParam, uint fWinIni);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref ANIMATIONINFO pvParam, uint fWinIni);

        private struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        private static string? GetActiveScheme()
        {
            var (ex, o) = Proc.Run("powercfg.exe", "/getactivescheme");
            if (ex != 0) return null;
            var m = System.Text.RegularExpressions.Regex.Match(o, @"([0-9a-fA-F]{8}-[0-9a-fA-F-]{27,36})");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static bool HibernateEnabled()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
                return k?.GetValue("HibernateEnabled") is int i && i == 1;
            }
            catch { return false; }
        }

        // ---------- helpers de registro ----------

        private enum Hive { CurrentUser, LocalMachine }

        private static RegistryKey? Open(Hive hive, string path, bool writable)
        {
            try
            {
                var baseKey = hive == Hive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                return writable ? baseKey.CreateSubKey(path, true) : baseKey.OpenSubKey(path, false);
            }
            catch { return null; }
        }

        private static int? GetDword(Hive hive, string path, string name)
        {
            try
            {
                using var k = Open(hive, path, false);
                return k?.GetValue(name) is int i ? i : null;
            }
            catch { return null; }
        }

        private static void SetDword(Hive hive, string path, string name, int value)
        {
            using var k = Open(hive, path, true);
            k?.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static void DeleteDword(Hive hive, string path, string name)
        {
            try
            {
                using var k = Open(hive, path, true);
                k?.DeleteValue(name, false);
            }
            catch { }
        }

        private static string? GetString(Hive hive, string path, string name)
        {
            try
            {
                using var k = Open(hive, path, false);
                return k?.GetValue(name)?.ToString();
            }
            catch { return null; }
        }

        private static void SetString(Hive hive, string path, string name, string value)
        {
            using var k = Open(hive, path, true);
            k?.SetValue(name, value, RegistryValueKind.String);
        }

        private static void DeleteValue(Hive hive, string path, string name)
        {
            try
            {
                using var k = Open(hive, path, true);
                k?.DeleteValue(name, false);
            }
            catch { }
        }
    }
}