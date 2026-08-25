using System.Xml.Linq;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Scans Windows Scheduled Tasks (schtasks) and allows enabling/disabling tasks.
    /// </summary>
    public class TaskScanner
    {
        public List<ScheduledTaskInfo> ScanAll()
        {
            var tasks = new List<ScheduledTaskInfo>();
            var (exit, outStr) = Proc.Run("schtasks.exe", "/query /xml");
            if (exit != 0) return tasks;

            foreach (var xml in ParseXml(outStr))
            {
                try
                {
                    var clean = StripDeclaration(xml);
                    var doc = XDocument.Parse(clean);
                    var root = doc.Root;
                    if (root is null) continue;

                    var items = root.Name.LocalName == "Task"
                        ? new[] { root }
                        : root.Elements().Where(e => e.Name.LocalName == "Task");

                    foreach (var taskEl in items)
                    {
                        var reg = Child(taskEl, "RegistrationInfo");
                        var uri = (string?)Child(reg, "URI");
                        if (string.IsNullOrEmpty(uri)) uri = (string?)Child(reg, "Uri");
                        var author = (string?)Child(reg, "Author");
                        var logon = (string?)Child(Child(taskEl, "Principal"), "LogonType");
                        var enabled = (bool?)Child(Child(taskEl, "Settings"), "Enabled") ?? true;

                        var path = string.IsNullOrEmpty(uri) ? "\\Unknown" : uri;
                        var name = path.Split('\\').Last();

                        tasks.Add(new ScheduledTaskInfo
                        {
                            FullPath = path,
                            Name = string.IsNullOrEmpty(name) ? path : name,
                            Enabled = enabled,
                            Author = author,
                            LogonDescription = logon
                        });
                    }
                }
                catch { }
            }
            return tasks.OrderBy(t => t.Name).ToList();
        }

        public List<ScheduledTaskInfo> ParseTasksXml(string xmlContent)
        {
            var tasks = new List<ScheduledTaskInfo>();
            try
            {
                var clean = StripDeclaration(xmlContent);
                var doc = XDocument.Parse(clean);
                var root = doc.Root;
                if (root is null) return tasks;

                var items = root.Name.LocalName == "Task"
                    ? new[] { root }
                    : root.Elements().Where(e => e.Name.LocalName == "Task");

                foreach (var taskEl in items)
                {
                    var reg = Child(taskEl, "RegistrationInfo");
                    var name = (string?)Child(taskEl, "TaskName") ?? (string?)Child(reg, "URI") ?? "Task";
                    var state = (string?)Child(taskEl, "State");
                    var enabled = !string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase);

                    tasks.Add(new ScheduledTaskInfo
                    {
                        FullPath = name,
                        Name = name,
                        Enabled = enabled
                    });
                }
            }
            catch { }
            return tasks;
        }

        private static string StripDeclaration(string xml)
        {
            var idx = xml.IndexOf("?>", StringComparison.Ordinal);
            return idx >= 0 ? xml[(idx + 2)..].TrimStart() : xml;
        }

        private static XElement? Child(XElement? el, string localName)
            => el?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<string> ParseXml(string raw)
        {
            const string marker = "<?xml";
            int pos = 0;
            while (true)
            {
                int start = raw.IndexOf(marker, pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                int end = raw.IndexOf(marker, start + marker.Length, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    yield return raw[start..];
                    break;
                }
                yield return raw[start..end];
                pos = end;
            }
        }

        public static void SetEnabled(string fullPath, bool enable)
        {
            var arg = (enable ? "/change /enable /tn \"" : "/change /disable /tn \"") + fullPath + "\"";
            var (exit, outStr) = Proc.Run("schtasks.exe", arg);
            if (exit != 0) throw new InvalidOperationException($"{fullPath}: {outStr.Trim()}");
        }
    }
}
