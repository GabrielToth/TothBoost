using Microsoft.Win32;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Snapshot
{
    /// <summary>
    /// Cria o ponto de restauração OBRIGATÓRIO do Windows na primeira execução
    /// do TothBoost, antes de qualquer varredura ou modificação, e controla o
    /// marcador de "primeiro uso concluído".
    /// </summary>
    public static class RestorePointService
    {
        private static string MarkerKey => @"SOFTWARE\TothBoost";

        /// <summary>Verdadeiro se o primeiro uso já foi concluído (ponto + perfil inicial).</summary>
        public static bool IsInitialized
        {
            get
            {
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(MarkerKey, false);
                    return k?.GetValue("Initialized") is int v && v == 1;
                }
                catch { return false; }
            }
        }

        public static void MarkInitialized()
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(MarkerKey);
                k?.SetValue("Initialized", 1, RegistryValueKind.DWord);
                k?.SetValue("InitializedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), RegistryValueKind.String);
            }
            catch { }
        }

        /// <summary>
        /// Cria o ponto de restauração via PowerShell (Checkpoint-Computer com
        /// fallback para o tipo padrão). Retorna (ok, mensagem).
        /// </summary>
        public static (bool Ok, string Message) CreateInitialPoint()
        {
            try
            {
                if (IsInitialized)
                    return (true, "Primeiro uso já concluído nesta máquina.");

                var ps = @"$ErrorActionPreference='Continue';" +
                         "try { Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue | Out-Null } catch { };" +
                         "try { Checkpoint-Computer -Description '" + Description + "' -RestorePointType 'APPLICATION_INSTALL' -ErrorAction Stop; 'TB_OK' } " +
                         "catch { try { Checkpoint-Computer -Description '" + Description + "' -ErrorAction Stop; 'TB_OK' } " +
                         "catch { 'TB_FAIL:' + $_.Exception.Message } }";

                var (exit, output) = Proc.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"& { " + ps + " }\"", 240000);

                if (exit != 0)
                    return (false, "PowerShell falhou (código " + exit + "): " + output.Trim());

                if (output.Contains("TB_OK"))
                    return (true, "Ponto de restauração criado com sucesso.");

                var firstLine = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("TB_FAIL:"))
                                ?? output.Trim();
                if (firstLine.Length > 400) firstLine = firstLine[..400];
                return (false, "Não foi possível criar o ponto de restauração (" + firstLine +
                                "). Verifique se a Proteção do Sistema está ativada.");
            }
            catch (Exception ex)
            {
                return (false, "Falha ao criar ponto de restauração: " + ex.Message);
            }
        }

        private static string Description => "TothBoost - ponto de seguranca inicial";

        /// <summary>Lista todos os pontos de restauração do Windows com data/hora exata.</summary>
        public static List<RestorePointInfo> ListPoints()
        {
            var list = new List<RestorePointInfo>();
            try
            {
                var ps = "$ErrorActionPreference='SilentlyContinue';" +
                         "$pts = @(Get-WmiObject -Class SystemRestore | Sort-Object CreationTime);" +
                         "$pts | ForEach-Object { Write-Output ('TB_PT|' + $_.SequenceNumber + '|' + $_.CreationTime.ToString('o') + '|' + $_.Description) }";
                var (exit, output) = Proc.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"& { " + ps + " }\"", 60000);
                if (exit != 0) return list;

                foreach (var line in output.Split('\n'))
                {
                    var l = line.Trim();
                    if (!l.StartsWith("TB_PT|")) continue;
                    var parts = l.Split('|');
                    if (parts.Length < 3) continue;
                    if (long.TryParse(parts[1], out var seq) &&
                        DateTime.TryParse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
                    {
                        var descParts = parts.Skip(3).ToArray();
                        list.Add(new RestorePointInfo
                        {
                            Sequence = seq,
                            When = when,
                            Description = descParts.Length > 0 && !string.IsNullOrWhiteSpace(descParts[0])
                                ? string.Join("|", descParts) : null
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>Exclui UM ponto de restauração específico via WMI. Por conta e risco do usuário.</summary>
        public static (bool Ok, string Message) DeletePoint(long sequence)
        {
            try
            {
                var ps = "$ErrorActionPreference='Stop';" +
                         "try { $cl = [wmiclass]'Root\\Default:SystemRestore'; $r = $cl.DeleteRestorePoint(" + sequence + "); 'TB_R:' + $r.ReturnValue }" +
                         "catch { 'TB_FAIL:' + $_.Exception.Message }";
                var (exit, output) = Proc.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"& { " + ps + " }\"", 60000);
                if (exit != 0)
                    return (false, "PowerShell falhou (código " + exit + "): " + output.Trim());
                if (output.Contains("TB_R:0"))
                    return (true, "Ponto de restauração excluído.");
                var fail = output.Split('\n').FirstOrDefault(l => l.StartsWith("TB_FAIL:"))?.Trim();
                return (false, fail ?? "A exclusão foi recusada pelo Windows.");
            }
            catch (Exception ex)
            {
                return (false, "Falha ao excluir: " + ex.Message);
            }
        }
    }
}