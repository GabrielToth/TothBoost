using System.Diagnostics;
using System.Text;

#nullable disable

namespace TothBoost.Core.Utilities
{
    /// <summary>
    /// Executa ferramentas de linha de comando do Windows (sc.exe, schtasks.exe,
    /// powercfg.exe) capturando saída e código de saída de forma confiável,
    /// com detecção de codificação (UTF-8/UTF-16).
    /// </summary>
    public static class Proc
    {
        public static (int ExitCode, string Output) Run(string file, string args, int timeoutMs = 120000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "Falha ao iniciar o processo.");

            var so = new StringBuilder();
            var se = new StringBuilder();
            using var oT = p.StandardOutput;
            using var eT = p.StandardError;
            var t1 = Task.Run(() => so.Append(Decode(oT.BaseStream)));
            var t2 = Task.Run(() => se.Append(Decode(eT.BaseStream)));

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); }
                catch { }
                return (1, so + "\n" + se + "\nTempo limite excedido executando " + file);
            }
            Task.WaitAll(t1, t2);
            return (p.ExitCode, (so.ToString() + "\n" + se.ToString()).Trim());
        }

        private static string Decode(System.IO.Stream stream)
        {
            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            return Encoding.UTF8.GetString(data);
        }

        public static (int Exit, string Output) Sc(string verb, string serviceName, string extra = null)
        {
            return Run("sc.exe", $"{verb} {quote(serviceName)}{(string.IsNullOrEmpty(extra) ? "" : " " + extra)}");
        }

        private static string quote(string v) => v.Contains(' ') ? $"\"{v}\"" : v;
    }

    /// <summary>Helpers de elevação.</summary>
    public static class Elevation
    {
        public static bool IsElevated
        {
            get
            {
                try
                {
                    using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                    return new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }
    }

    public static class RegistryKeys
    {
        public const string SvcRoot = @"SYSTEM\CurrentControlSet\Services";
        public const string RunLm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string RunLmWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        public const string RunCu = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunOnceLm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        public const string RunOnceCu = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        public const string RunMaxKwT = @"Software\TothBoost\Desabilitados";
    }
}