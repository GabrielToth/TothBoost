using System.Text.Json;
using Microsoft.Win32;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Scanners
{
    public class QuarantineFileMeta
    {
        public string Name { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string Location { get; set; } = "";
    }

    /// <summary>
    /// Scans automatic startup items (Registry + Startup Folders).
    /// Disabling NEVER deletes items: registry values are moved to a safety subkey
    /// and shortcuts/files are moved to a quarantine folder — fully reversible.
    /// </summary>
    public class StartupScanner
    {
        public static string QuarantineRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TothBoost", "Restoration");

        public List<StartupItem> ScanAll()
        {
            var items = new List<StartupItem>();

            void AddRegistry(RegistryKey hive, string keyPath, string location)
            {
                try
                {
                    using var k = hive.OpenSubKey(keyPath);
                    if (k is null) return;
                    foreach (var vn in k.GetValueNames())
                    {
                        var v = k.GetValue(vn)?.ToString();
                        if (string.IsNullOrEmpty(v)) continue;
                        items.Add(new StartupItem
                        {
                            Name = vn,
                            Location = location,
                            LocationKey = keyPath,
                            Command = v,
                            DisabledByTothBoost = IsQuarantined(hive, keyPath, vn)
                        });
                    }
                }
                catch { }
            }

            AddRegistry(Registry.LocalMachine, RegistryKeys.RunLm, "Auto-Execution (HKLM)");
            AddRegistry(Registry.CurrentUser, RegistryKeys.RunCu, "Auto-Execution (HKCU)");
            AddRegistry(Registry.LocalMachine, RegistryKeys.RunOnceLm, "Run Once (HKLM)");
            AddRegistry(Registry.CurrentUser, RegistryKeys.RunOnceCu, "Run Once (HKCU)");

            ScanFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder");
            ScanFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All Users Startup Folder");

            ScanQuarantinedFiles(items);

            return items.OrderBy(i => i.Name).ToList();
        }

        private static void ScanFolder(List<StartupItem> items, string folder, string location)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var quarantined = IsFileQuarantined(f);
                items.Add(new StartupItem
                {
                    Name = Path.GetFileName(f),
                    Location = location,
                    Command = f,
                    IsFolder = true,
                    Existing = true,
                    DisabledByTothBoost = quarantined
                });
            }
        }

        public static bool IsQuarantined(RegistryKey hive, string key, string valueName)
        {
            try
            {
                using var q = hive.OpenSubKey(QuarantineKey(key));
                return q?.GetValue(valueName) is not null;
            }
            catch { return false; }
        }

        private static bool IsFileQuarantined(string filePath) =>
            File.Exists(Path.Combine(QuarantineFiles, Path.GetFileName(filePath)));

        public static string QuarantineFiles => Path.Combine(QuarantineRoot, "files");
        public static string QuarantineMetaFile => Path.Combine(QuarantineRoot, "quarantine_files.json");
        public static string QuarantineKey(string key) => RegistryKeys.RunMaxKwT + "\\" + Sanitize(key);
        private static string Sanitize(string key) => key.Replace('\\', '_').Replace('/', '_');
        private static bool IsLm(string key) => key.StartsWith("SOFTWARE", StringComparison.OrdinalIgnoreCase);

        private static List<QuarantineFileMeta> LoadQuarantineFileMetas()
        {
            try
            {
                if (File.Exists(QuarantineMetaFile))
                {
                    var json = File.ReadAllText(QuarantineMetaFile);
                    return JsonSerializer.Deserialize<List<QuarantineFileMeta>>(json) ?? new List<QuarantineFileMeta>();
                }
            }
            catch { }
            return new List<QuarantineFileMeta>();
        }

        private static void SaveQuarantineFileMetas(List<QuarantineFileMeta> list)
        {
            try
            {
                Directory.CreateDirectory(QuarantineRoot);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(QuarantineMetaFile, json);
            }
            catch { }
        }

        private static void ScanQuarantinedFiles(List<StartupItem> items)
        {
            if (!Directory.Exists(QuarantineFiles)) return;
            var metas = LoadQuarantineFileMetas();

            foreach (var f in Directory.EnumerateFiles(QuarantineFiles))
            {
                var fileName = Path.GetFileName(f);
                if (fileName.Equals("quarantine_files.json", StringComparison.OrdinalIgnoreCase)) continue;

                var meta = metas.FirstOrDefault(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
                var origPath = meta?.OriginalPath ?? f;
                var location = meta?.Location ?? "Startup Folder (Quarantine)";

                if (!items.Any(i => string.Equals(i.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new StartupItem
                    {
                        Name = fileName,
                        Location = location,
                        Command = origPath,
                        IsFolder = true,
                        Existing = false,
                        DisabledByTothBoost = true
                    });
                }
            }
        }

        public static void SetDisabled(StartupItem item, bool disable)
        {
            if (item.IsFolder) ToggleFileQuarantine(item, disable);
            else ToggleRegistryQuarantine(item, disable);
        }

        private static void ToggleRegistryQuarantine(StartupItem item, bool disable)
        {
            if (string.IsNullOrEmpty(item.LocationKey)) return;
            var hive = IsLm(item.LocationKey) ? Registry.LocalMachine : Registry.CurrentUser;
            var qKey = QuarantineKey(item.LocationKey);

            if (disable)
            {
                using var src = hive.OpenSubKey(item.LocationKey, writable: true);
                var val = src?.GetValue(item.Name);
                if (val is null) return;
                using var dst = hive.CreateSubKey(qKey);
                dst.SetValue(item.Name, val);
                src!.DeleteValue(item.Name, throwOnMissingValue: false);
            }
            else
            {
                using var dst = hive.OpenSubKey(qKey, writable: true);
                var val = dst?.GetValue(item.Name);
                if (val is null) return;
                using var src = hive.OpenSubKey(item.LocationKey, writable: true) ?? hive.CreateSubKey(item.LocationKey);
                src.SetValue(item.Name, val);
                dst!.DeleteValue(item.Name, throwOnMissingValue: false);
            }
        }

        private static void ToggleFileQuarantine(StartupItem item, bool disable)
        {
            if (string.IsNullOrEmpty(item.Command)) return;
            Directory.CreateDirectory(QuarantineFiles);
            var metas = LoadQuarantineFileMetas();

            var fileName = Path.GetFileName(item.Command);
            var qFile = Path.Combine(QuarantineFiles, fileName);

            if (disable)
            {
                if (File.Exists(item.Command))
                {
                    if (File.Exists(qFile)) File.Delete(qFile);
                    File.Move(item.Command, qFile);

                    metas.RemoveAll(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
                    metas.Add(new QuarantineFileMeta
                    {
                        Name = fileName,
                        OriginalPath = item.Command,
                        Location = item.Location
                    });
                    SaveQuarantineFileMetas(metas);
                }
            }
            else
            {
                var meta = metas.FirstOrDefault(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
                var targetPath = meta?.OriginalPath ?? item.Command;

                if (File.Exists(qFile))
                {
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                    if (File.Exists(targetPath)) File.Delete(targetPath);

                    File.Move(qFile, targetPath);
                    metas.RemoveAll(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
                    SaveQuarantineFileMetas(metas);
                }
            }
        }
    }
}
