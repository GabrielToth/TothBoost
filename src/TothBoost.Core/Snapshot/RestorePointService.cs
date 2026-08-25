using Microsoft.Win32;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Snapshot
{
    /// <summary>
    /// Creates mandatory Windows System Restore Point on TothBoost first launch
    /// before any modification, and tracks first-run completion flag.
    /// </summary>
    public static class RestorePointService
    {
        private static string MarkerKey => @"SOFTWARE\TothBoost";

        /// <summary>True if initial setup (restore point + default baseline profile) has been completed.</summary>
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
        /// Creates System Restore Point via PowerShell Checkpoint-Computer.
        /// </summary>
        public static (bool Ok, string Message) CreateInitialPoint()
        {
            try
            {
                if (IsInitialized)
                    return (true, "First run already completed on this computer.");

                var ps = @"$ErrorActionPreference='Continue';" +
                         "try { Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue | Out-Null } catch { };" +
                         "try { Checkpoint-Computer -Description 'TothBoost - Initial Baseline' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; 'OK' } catch { 'FAIL: ' + $_.Exception.Message }";

                var (exit, outStr) = Proc.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"");
                if (outStr.Contains("OK"))
                    return (true, "System Restore Point 'TothBoost - Initial Baseline' created successfully.");

                var msg = string.IsNullOrWhiteSpace(outStr) ? "PowerShell did not return output." : outStr.Trim();
                return (false, "Could not create restore point automatically: " + msg);
            }
            catch (Exception ex)
            {
                return (false, "Error creating restore point: " + ex.Message);
            }
        }

        /// <summary>Creates a manual System Restore Point with custom description.</summary>
        public static (bool Ok, string Message) CreatePoint(string description)
        {
            try
            {
                var desc = string.IsNullOrWhiteSpace(description) ? "TothBoost - Safety Point" : "TothBoost - " + description.Trim();
                var ps = @"$ErrorActionPreference='Continue';" +
                         "try { Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue | Out-Null } catch { };" +
                         "try { Checkpoint-Computer -Description '" + desc.Replace("'", "''") + "' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; 'OK' } catch { 'FAIL: ' + $_.Exception.Message }";

                var (exit, outStr) = Proc.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"");
                if (outStr.Contains("OK"))
                    return (true, $"System Restore Point '{desc}' created successfully.");

                var msg = string.IsNullOrWhiteSpace(outStr) ? "PowerShell did not return output." : outStr.Trim();
                return (false, "Could not create restore point: " + msg);
            }
            catch (Exception ex)
            {
                return (false, "Error creating restore point: " + ex.Message);
            }
        }

        public sealed class RestorePointItem
        {
            public long SequenceNumber { get; set; }
            public string Description { get; set; } = "";
            public string EventType { get; set; } = "";
            public string RestorePointType { get; set; } = "";
            public string CreationTime { get; set; } = "";
        }

        public static List<RestorePointItem> ListPoints()
        {
            var list = new List<RestorePointItem>();
            try
            {
                var ps = "Get-ComputerRestorePoint | Select-Object SequenceNumber, Description, EventType, RestorePointType, CreationTime | ConvertTo-Json -Compress";
                var (exit, outStr) = Proc.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"");
                if (exit != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

                var trimmed = outStr.Trim();
                if (trimmed.StartsWith("["))
                {
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<RestorePointItem>>(trimmed);
                    if (items != null) list.AddRange(items);
                }
                else if (trimmed.StartsWith("{"))
                {
                    var item = System.Text.Json.JsonSerializer.Deserialize<RestorePointItem>(trimmed);
                    if (item != null) list.Add(item);
                }
            }
            catch { }
            return list.OrderByDescending(p => p.SequenceNumber).ToList();
        }

        public static (bool Ok, string Message) RestoreToPoint(long sequenceNumber)
        {
            try
            {
                var ps = $"Restore-Computer -RestorePoint {sequenceNumber} -Confirm:$false";
                var (exit, outStr) = Proc.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"");
                if (exit == 0)
                    return (true, $"System restoration to point #{sequenceNumber} initiated. Computer will restart.");

                return (false, "Could not initiate restoration: " + outStr.Trim());
            }
            catch (Exception ex)
            {
                return (false, "Error restoring system: " + ex.Message);
            }
        }

        public static (bool Ok, string Message) DeletePoint(long sequenceNumber)
        {
            try
            {
                var ps = $"Get-WmiObject -Class SystemRestore | Where-Object {{ $_.SequenceNumber -eq {sequenceNumber} }} | ForEach-Object {{ $_.Delete() }}";
                var (exit, outStr) = Proc.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"");
                return (true, $"Restore point #{sequenceNumber} deleted successfully.");
            }
            catch (Exception ex)
            {
                return (false, "Error deleting restore point: " + ex.Message);
            }
        }
    }
}
