using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace TothBoost.Core.Utilities
{
    public class NetworkActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public static class NetworkOptimizer
    {
        public static NetworkActionResult FlushDns()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                return new NetworkActionResult
                {
                    Success = proc?.ExitCode == 0,
                    Message = "DNS Resolver Cache successfully flushed."
                };
            }
            catch (Exception ex)
            {
                return new NetworkActionResult
                {
                    Success = false,
                    Message = $"Failed to flush DNS cache: {ex.Message}"
                };
            }
        }

        public static bool IsNetworkThrottlingDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                if (key != null)
                {
                    var val = key.GetValue("NetworkThrottlingIndex");
                    if (val is int intVal) return intVal == unchecked((int)0xFFFFFFFF);
                }
            }
            catch { }
            return false;
        }

        public static NetworkActionResult SetNetworkThrottlingDisabled(bool disable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                if (key != null)
                {
                    if (disable)
                    {
                        key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                        key.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                    }
                    return new NetworkActionResult
                    {
                        Success = true,
                        Message = disable ? "Network bandwidth throttling disabled & gaming CPU responsiveness set to 100%." : "Network throttling restored to default."
                    };
                }
            }
            catch (Exception ex)
            {
                return new NetworkActionResult
                {
                    Success = false,
                    Message = $"Failed to update network throttling registry setting: {ex.Message}"
                };
            }
            return new NetworkActionResult { Success = false, Message = "Registry key unreachable." };
        }
    }
}
