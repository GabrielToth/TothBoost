using System.Xml.Linq;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Lista as tarefas agendadas do Windows (schtasks) e permite habilitar/desabilitar
    /// sem excluir nada.
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

                    var uri = (string?)root.Element("RegistrationInfo")?.Element("URI")
                              ?? (string?)root.Element("RegistrationInfo")?.Element("Uri");
                    var author = (string?)root.Element("RegistrationInfo")?.Element("Author");
                    var logon = (string?)root.Element("Principal")?.Element("LogonType");
                    var enabled = (bool?)root.Element("Settings")?.Element("Enabled") ?? true;

                    var path = uri ?? "\\Desconhecido";
                    var name = path.Split('\\').Last();

                    tasks.Add(new ScheduledTaskInfo
                    {
                        FullPath = path,
                        Name = string.IsNullOrEmpty(name) ? path : name,
                        Enabled = enabled,
                        Author = string.IsNullOrEmpty(author) ? null : author,
                        LogonDescription = logon
                    });
                }
                catch { /* bloco XML corrompido — ignorar */ }
            }
            return tasks.OrderBy(t => t.FullPath).ToList();
        }

        private static List<string> ParseXml(string raw)
        {
            var chunks = new List<string>();
            var start = raw.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
            while (start >= 0)
            {
                var next = raw.IndexOf("<?xml", start + 4, StringComparison.OrdinalIgnoreCase);
                var end = next >= 0 ? next : raw.Length;
                var chunk = raw.Substring(start, end - start);
                var taskEnd = chunk.LastIndexOf("</Task>", StringComparison.OrdinalIgnoreCase);
                if (taskEnd > 0) chunk = chunk.Substring(0, taskEnd + "</Task>".Length);
                chunks.Add(chunk);
                start = next;
            }
            return chunks;
        }

        private static string StripDeclaration(string xml)
        {
            var end = xml.IndexOf("?>", StringComparison.Ordinal);
            if (end >= 0 && xml.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                return xml.Substring(end + 2);
            return xml;
        }
    }

    public static class TaskActions
    {
        public static void SetEnabled(string fullPath, bool enabled)
        {
            var flag = enabled ? "/ENABLE" : "/DISABLE";
            var (exit, outStr) = Proc.Run("schtasks.exe", $"/Change /TN \"{fullPath}\" {flag}");
            if (exit != 0)
                throw new InvalidOperationException($"schtasks falhou: {outStr.Trim()}");
        }
    }
}