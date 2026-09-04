using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TothBoost.Core.Utilities
{
    public class GitHubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }

    public class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public static class UpdateChecker
    {
        private const string GitHubRepo = "GabrielToth/TothBoost";

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = GetCurrentVersion()
            };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TothBoost-App");

                string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
                var release = await client.GetFromJsonAsync<GitHubReleaseInfo>(url);

                if (release != null && !string.IsNullOrEmpty(release.TagName))
                {
                    string latestTag = release.TagName.TrimStart('v', 'V');
                    result.LatestVersion = latestTag;
                    result.ReleaseNotes = release.Body;
                    result.ReleaseUrl = release.HtmlUrl;

                    if (Version.TryParse(result.CurrentVersion, out var currentVer) &&
                        Version.TryParse(latestTag, out var latestVer))
                    {
                        result.HasUpdate = latestVer > currentVer;
                    }
                    else
                    {
                        // Fallback string comparison
                        result.HasUpdate = !string.Equals(result.CurrentVersion, latestTag, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Could not check for updates: {ex.Message}";
            }

            return result;
        }

        public static string GetCurrentVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }
    }

    public static class ShortcutManager
    {
        public static void CreateStartMenuShortcut()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                string startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string shortcutPath = Path.Combine(startMenuPath, "TothBoost.lnk");

                // If shortcut doesn't exist, create it via PowerShell WScript.Shell
                if (!File.Exists(shortcutPath))
                {
                    string psCommand = $"$w = New-Object -ComObject WScript.Shell; $s = $w.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{exePath}'; $s.Description = 'TothBoost Windows Performance Optimizer'; $s.Save()";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
            }
            catch { }
        }
    }
}
