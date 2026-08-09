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
        UltimatePower
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