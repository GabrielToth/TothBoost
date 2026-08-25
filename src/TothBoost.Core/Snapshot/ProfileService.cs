using System.Text.Json;
using TothBoost.Core.Models;

namespace TothBoost.Core.Snapshot
{
    /// <summary>
    /// Perfis do TothBoost: o perfil "Default" guarda as configurações salvas do
    /// usuário atual (estado de serviços, tarefas, inicialização e ajustes).
    /// A estrutura já suporta vários perfis (criar/selecionar no futuro).
    /// </summary>
    public static class ProfileService
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TothBoost", "Profiles");

        private static readonly string MetaPath = Path.Combine(Root, "profiles.json");

        public sealed class ProfileMeta
        {
            public string? Name { get; set; }
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

        /// <summary>Garante que o perfil Default exista (criado na primeira abertura).</summary>
        public static void EnsureDefault()
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                if (meta.Profiles.Any(p => p.IsDefault)) return;

                Directory.CreateDirectory(Root);
                meta.Profiles.Add(new ProfileMeta
                {
                    Name = "Default",
                    CreatedUtc = DateTime.UtcNow,
                    HostName = Environment.MachineName,
                    IsDefault = true
                });
                meta.Current = "Default";
                SaveMeta(meta);
            }
        }

        public static string? CurrentName()
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                return meta.Current ?? meta.Profiles.FirstOrDefault()?.Name;
            }
        }

        public static List<ProfileMeta> List()
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                return meta.Profiles.ToList();
            }
        }

        /// <summary>Grava o estado atual do usuário no perfil indicado (Default.json etc.).</summary>
        public static void SaveCapture(string profileName, ProfileCapture capture)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                var file = Path.Combine(Root, Sanitize(profileName) + ".json");
                File.WriteAllText(file, JsonSerializer.Serialize(capture, JsonOpts), System.Text.Encoding.UTF8);
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
                return File.Exists(Path.Combine(Root, Sanitize(profileName) + ".json"));
            }
        }

        /// <summary>Cria um novo perfil e o torna o atual. Se já existir, apenas seleciona.</summary>
        public static void Create(string name)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                var existing = meta.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    meta.Current = existing.Name;
                    SaveMeta(meta);
                    return;
                }
                meta.Profiles.Add(new ProfileMeta
                {
                    Name = name,
                    CreatedUtc = DateTime.UtcNow,
                    HostName = Environment.MachineName,
                    IsDefault = false
                });
                meta.Current = name;
                SaveMeta(meta);
            }
        }

        public static bool Exists(string name)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                return meta.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static void Select(string name)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                var p = meta.Profiles.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is null) return;
                meta.Current = p.Name;
                SaveMeta(meta);
            }
        }

        public static void MarkSaved(string name)
        {
            lock (Sync)
            {
                var meta = LoadMeta();
                var p = meta.Profiles.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is null) return;
                p.LastSavedUtc = DateTime.UtcNow;
                SaveMeta(meta);
            }
        }

        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private static MetaFile LoadMeta()
        {
            try
            {
                if (!File.Exists(MetaPath)) return new MetaFile();
                return JsonSerializer.Deserialize<MetaFile>(File.ReadAllText(MetaPath)) ?? new MetaFile();
            }
            catch { return new MetaFile(); }
        }

        private static void SaveMeta(MetaFile meta)
        {
            File.WriteAllText(MetaPath, JsonSerializer.Serialize(meta, JsonOpts), System.Text.Encoding.UTF8);
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }

    /// <summary>Estado capturado de um perfil: serviços, tarefas, inicialização e ajustes.</summary>
    public class ProfileCapture
    {
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
        public List<ProfileServiceState> Services { get; set; } = new();
        public List<ProfileTaskState> Tasks { get; set; } = new();
        public List<ProfileStartupState> Startups { get; set; } = new();
        public List<ProfileTweakState> Tweaks { get; set; } = new();
    }

    public class ProfileServiceState
    {
        public string? Name { get; set; }
        public int StartKind { get; set; }
        public bool Protected { get; set; }
    }

    public class ProfileTaskState
    {
        public string? FullPath { get; set; }
        public string? Name { get; set; }
        public bool Enabled { get; set; }
    }

    public class ProfileStartupState
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public bool Exists { get; set; }
        public bool DisabledByTothBoost { get; set; }
    }

    public class ProfileTweakState
    {
        public string? Id { get; set; }
        public bool Applied { get; set; }
    }
}