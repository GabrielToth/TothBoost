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
    /// Lista a inicialização automática (registro + pastas de inicialização).
    /// Desabilitar NUNCA exclui: valores de registro são movidos para uma
    /// subchave de segurança do TothBoost e atalhos/arquivos são movidos para
    /// uma pasta protegida — tudo reversível e restaurado pelo snapshot.
    /// </summary>
    public class StartupScanner
    {
        public static string QuarantineRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TothBoost", "Restauração");

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

            AddRegistry(Registry.LocalMachine, RegistryKeys.RunLm, "Execução automática (HKLM)");
            AddRegistry(Registry.LocalMachine, RegistryKeys.RunLmWow, "Execução automática (HKLM Wow64)");
            AddRegistry(Registry.CurrentUser, RegistryKeys.RunCu, "Execução automática (usuário)");
            AddRegistry(Registry.LocalMachine, RegistryKeys.RunOnceLm, "Execução única (HKLM)");
            AddRegistry(Registry.CurrentUser, RegistryKeys.RunOnceCu, "Execução única (usuário)");

            ScanFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Pasta Inicialização (usuário)");
            ScanFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Pasta Inicialização (comum)");

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

        public static string QuarantineFiles => Path.Combine(QuarantineRoot, "arquivos");
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
                var location = meta?.Location ?? "Pasta Inicialização (quarentena)";

                if (!items.Any(i => string.Equals(i.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new StartupItem
                    {
                        Name = fileName,
                        Location = location,
                        Command = origPath,
                        IsFolder = true,
                        Existing = true,
                        DisabledByTothBoost = true
                    });
                }
            }
        }

        // ---------- Ações (nunca excluem) ----------

        public static void Disable(StartupItem item)
        {
            if (item.IsFolder) DisableFile(item);
            else DisableRegistry(item);
        }

        public static void Enable(StartupItem item)
        {
            if (item.IsFolder) EnableFile(item);
            else EnableRegistry(item);
        }

        private static void DisableRegistry(StartupItem item)
        {
            if (item.LocationKey is null) return;
            var hive = IsLm(item.LocationKey) ? Registry.LocalMachine : Registry.CurrentUser;
            using (var k = hive.OpenSubKey(item.LocationKey!, true))
            {
                if (k is null) return;
                using var q = hive.CreateSubKey(QuarantineKey(item.LocationKey), true);
                q.SetValue(item.Name, k.GetValue(item.Name)?.ToString() ?? "", RegistryValueKind.String);
                k.DeleteValue(item.Name, false);
            }
        }

        private static void EnableRegistry(StartupItem item)
        {
            if (item.LocationKey is null) return;
            var hive = IsLm(item.LocationKey) ? Registry.LocalMachine : Registry.CurrentUser;
            using var q = hive.OpenSubKey(QuarantineKey(item.LocationKey));
            if (q is null) return;
            using var k = hive.CreateSubKey(item.LocationKey!, true);
            k.SetValue(item.Name, q.GetValue(item.Name)?.ToString() ?? "", RegistryValueKind.String);
            q.DeleteValue(item.Name, false);
        }

        private static void DisableFile(StartupItem item)
        {
            if (string.IsNullOrEmpty(item.Command)) return;
            Directory.CreateDirectory(QuarantineFiles);
            var dest = Path.Combine(QuarantineFiles, Path.GetFileName(item.Command));

            var metas = LoadQuarantineFileMetas();
            metas.RemoveAll(m => string.Equals(m.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            metas.Add(new QuarantineFileMeta
            {
                Name = item.Name,
                OriginalPath = item.Command,
                Location = item.Location
            });
            SaveQuarantineFileMetas(metas);

            File.Move(item.Command, dest, true);
        }

        private static void EnableFile(StartupItem item)
        {
            var fileName = Path.GetFileName(item.Command);
            var metas = LoadQuarantineFileMetas();
            var meta = metas.FirstOrDefault(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));

            var src = Path.Combine(QuarantineFiles, fileName);
            if (!File.Exists(src)) return;

            var targetPath = meta?.OriginalPath ?? item.Command;
            if (string.IsNullOrEmpty(targetPath)) return;

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.Move(src, targetPath, true);

            metas.RemoveAll(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
            SaveQuarantineFileMetas(metas);
        }
    }
}