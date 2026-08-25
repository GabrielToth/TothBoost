using System.Diagnostics;
using System.Text;

#nullable disable

namespace TothBoost.Core.Utilities
{
    /// <summary>
    /// Executes Windows CLI tools (sc.exe, schtasks.exe, powercfg.exe)
    /// capturing stdout, stderr, and exit codes cleanly with encoding detection.
    /// </summary>
    public static class Proc
    {
        static Proc()
        {
            try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
            catch { }
        }

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
            if (p is null) return (-1, "Failed to start process.");

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
                return (1, so + "\n" + se + "\nTimeout exceeded executing " + file);
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

            var oem = Encoding.GetEncoding(437);
            return oem.GetString(data);
        }

        public static (int ExitCode, string Output) Sc(string command, string serviceName, string key = null, string value = null)
        {
            var sb = new StringBuilder();
            sb.Append(command).Append(' ').Append('"').Append(serviceName).Append('"');
            if (!string.IsNullOrEmpty(key))
            {
                sb.Append(' ').Append(key);
                if (value != null) sb.Append(value);
            }
            return Run("sc.exe", sb.ToString());
        }
    }

    public static class Elevation
    {
        public static bool IsElevated
        {
            get
            {
                try
                {
                    return new System.Security.Principal.WindowsPrincipal(
                        System.Security.Principal.WindowsIdentity.GetCurrent())
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
        public const string RunCu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string RunOnceLm = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        public const string RunOnceCu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        public const string RunMaxKwT = @"SOFTWARE\TothBoost\Quarantine";
    }

    public sealed class ActionItemReport
    {
        public string TargetName { get; init; } = "";
        public string Action { get; init; } = "";
        public bool Success { get; init; }
        public string Details { get; init; } = "";
    }

    public sealed class ActionReport
    {
        public List<ActionItemReport> Items { get; } = new();

        public bool HasErrors => Items.Any(i => !i.Success);
        public int SuccessCount => Items.Count(i => i.Success);
        public int ErrorCount => Items.Count(i => !i.Success);

        public void Add(string target, string action, bool success, string details = "")
            => Items.Add(new ActionItemReport { TargetName = target, Action = action, Success = success, Details = details });

        public void AddError(string target, string action, Exception ex)
            => Items.Add(new ActionItemReport { TargetName = target, Action = action, Success = false, Details = ex.Message });

        public void Merge(ActionReport other)
        {
            if (other?.Items != null) Items.AddRange(other.Items);
        }
    }
}
