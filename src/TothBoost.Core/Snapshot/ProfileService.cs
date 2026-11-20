using System.Text.Json;
using TothBoost.Core.Models;

namespace TothBoost.Core.Snapshot
{
    /// <summary>
    /// TothBoost Profile Service: "Default" profile stores saved settings
    /// (state of services, scheduled tasks, startup items, and OS tweaks).
    /// </summary>
    public static class ProfileService
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TothBoost", "Profiles");

        private static readonly string MetaPath = Path.Combine(Root, "profiles.json");

        public sealed class ProfileMeta
        {
            public string Name { get; set; } = "Default";
            public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
            public string? HostName { get; set; }
            public bool IsDefault { get; set; }
            public DateTime? LastSavedUtc { get; set; }
        }

        private sealed class MetaFile
        {
            public string? Current { get; set; }
            public List<ProfileMeta> Profiles { get; set; } = new();
        }

        /// <summary>Ensures the Default and Extreme profiles exist (created on first launch).</summary>
        public static void EnsureDefault()
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                bool changed = false;
                if (!meta.Profiles.Any(p => p.IsDefault))
                {
                    Directory.CreateDirectory(Root);
                    meta.Profiles.Add(new ProfileMeta
                    {
                        Name = "Default",
                        CreatedUtc = DateTime.UtcNow,
                        HostName = Environment.MachineName,
                        IsDefault = true
                    });
                    meta.Current = "Default";
                    changed = true;
                }

                if (!meta.Profiles.Any(p => string.Equals(p.Name, "Extreme", StringComparison.OrdinalIgnoreCase)))
                {
                    Directory.CreateDirectory(Root);
                    meta.Profiles.Add(new ProfileMeta
                    {
                        Name = "Extreme",
                        CreatedUtc = DateTime.UtcNow,
                        HostName = Environment.MachineName,
                        IsDefault = false
                    });
                    changed = true;
                }

                if (changed)
                {
                    SaveMeta(meta);
                }
            }
        }

        public static string? CurrentName()
        {
            lock (Sync)
            {
                EnsureDefault();
                return LoadMeta().Current;
            }
        }

        public static List<ProfileMeta> List()
        {
            lock (Sync)
            {
                EnsureDefault();
                return LoadMeta().Profiles;
            }
        }

        public static bool Exists(string profileName)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                return meta.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static ProfileMeta Create(string name)
        {
            lock (Sync)
            {
                EnsureDefault();
                var meta = LoadMeta();
                var clean = Sanitize(name);
                var existing = meta.Profiles.FirstOrDefault(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;

                var item = new ProfileMeta
                {
                    Name = clean,
                    CreatedUtc = DateTime.UtcNow,
                    HostName = Environment.MachineName,
                    IsDefault = false
                };
                meta.Profiles.Add(item);
                meta.Current = clean;
                SaveMeta(meta);
                return item;
            }
        }

        public static ProfileCapture? LoadCapture(string profileName)
        {
            lock (Sync)
            {
                var file = Path.Combine(Root, Sanitize(profileName) + ".json");
                if (!File.Exists(file)) return null;
                try
                {
                    return JsonSerializer.Deserialize<ProfileCapture>(File.ReadAllText(file, System.Text.Encoding.UTF8), JsonOpts);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool CaptureExists(string profileName)
        {
            lock (Sync)
            {
                var file = Path.Combine(Root, Sanitize(profileName) + ".json");
                return File.Exists(file);
            }
        }

        public static void Select(string name)
        {
            lock (Sync)
            {
                EnsureDefault();
                var meta = LoadMeta();
                var clean = Sanitize(name);
                if (!meta.Profiles.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase))) return;

                meta.Current = clean;
                SaveMeta(meta);
            }
        }

        public static void MarkSaved(string profileName)
        {
            lock (Sync)
            {
                EnsureDefault();
                var meta = LoadMeta();
                var target = meta.Profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    target.LastSavedUtc = DateTime.UtcNow;
                    SaveMeta(meta);
                }
            }
        }

        public static void SaveCapture(string profileName, ProfileCapture capture)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                capture.Name = Sanitize(profileName);
                capture.CapturedAt = DateTime.Now;
                var json = JsonSerializer.Serialize(capture, JsonOpts);
                File.WriteAllText(Path.Combine(Root, capture.Name + ".json"), json);
            }
        }

        public static bool Delete(string name)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                var item = meta.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (item == null || item.IsDefault) return false;

                meta.Profiles.Remove(item);
                if (string.Equals(meta.Current, item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    meta.Current = "Default";
                }
                SaveMeta(meta);

                try
                {
                    var file = Path.Combine(Root, Sanitize(name) + ".json");
                    if (File.Exists(file)) File.Delete(file);
                }
                catch { }
                return true;
            }
        }

        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static MetaFile LoadMeta()
        {
            try
            {
                if (File.Exists(MetaPath))
                {
                    var json = File.ReadAllText(MetaPath);
                    return JsonSerializer.Deserialize<MetaFile>(json, JsonOpts) ?? new MetaFile();
                }
            }
            catch { }
            return new MetaFile();
        }

        private static void SaveMeta(MetaFile meta)
        {
            try
            {
                Directory.CreateDirectory(Root);
                var json = JsonSerializer.Serialize(meta, JsonOpts);
                File.WriteAllText(MetaPath, json);
            }
            catch { }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }

    public class ProfileCapture
    {
        public string Name { get; set; } = "";
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public List<ProfileServiceState> Services { get; set; } = new();
        public List<ProfileTaskState> Tasks { get; set; } = new();
        public List<ProfileStartupState> Startups { get; set; } = new();
        public List<ProfileTweakState> Tweaks { get; set; } = new();
    }

    public class ProfileServiceState
    {
        public string Name { get; set; } = "";
        public int StartKind { get; set; }
        public bool Running { get; set; }
    }

    public class ProfileTaskState
    {
        public string FullPath { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class ProfileStartupState
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public bool Exists { get; set; }
        public bool DisabledByTothBoost { get; set; }
    }

    public class ProfileTweakState
    {
        public string Id { get; set; } = "";
        public bool Applied { get; set; }
    }
}
