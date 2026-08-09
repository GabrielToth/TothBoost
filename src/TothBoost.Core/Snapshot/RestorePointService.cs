using Microsoft.Win32;
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
    }
}