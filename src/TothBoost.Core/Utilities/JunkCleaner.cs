using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TothBoost.Core.Utilities
{
    public class JunkCategoryInfo
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string PathPattern { get; init; }
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
        public double SizeMb => Math.Round(SizeBytes / (1024.0 * 1024.0), 2);
    }

    public class CleanResult
    {
        public int TotalFilesDeleted { get; set; }
        public long TotalBytesFreed { get; set; }
        public double TotalMbFreed => Math.Round(TotalBytesFreed / (1024.0 * 1024.0), 2);
        public List<string> Errors { get; } = new();
    }

    public static class JunkCleaner
    {
        public static List<JunkCategoryInfo> Scan()
        {
            var categories = GetDefaultCategories();
            foreach (var cat in categories)
            {
                if (string.IsNullOrEmpty(cat.PathPattern) || !Directory.Exists(cat.PathPattern))
                    continue;

                try
                {
                    var dirInfo = new DirectoryInfo(cat.PathPattern);
                    long bytes = 0;
                    int count = 0;
                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            bytes += file.Length;
                            count++;
                        }
                        catch { }
                    }
                    cat.SizeBytes = bytes;
                    cat.FileCount = count;
                }
                catch { }
            }
            return categories;
        }

        public static CleanResult Clean(IEnumerable<string>? categoryIds = null)
        {
            var result = new CleanResult();
            var categories = GetDefaultCategories();
            var targetIds = categoryIds != null ? new HashSet<string>(categoryIds) : null;

            foreach (var cat in categories)
            {
                if (targetIds != null && !targetIds.Contains(cat.Id))
                    continue;

                if (string.IsNullOrEmpty(cat.PathPattern) || !Directory.Exists(cat.PathPattern))
                    continue;

                try
                {
                    var dirInfo = new DirectoryInfo(cat.PathPattern);
                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long len = file.Length;
                            file.Delete();
                            result.TotalBytesFreed += len;
                            result.TotalFilesDeleted++;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Could not delete {file.Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error scanning {cat.Name}: {ex.Message}");
                }
            }

            return result;
        }

        public static List<JunkCategoryInfo> GetDefaultCategories()
        {
            var list = new List<JunkCategoryInfo>();

            string userTemp = Path.GetTempPath();
            list.Add(new JunkCategoryInfo
            {
                Id = "UserTemp",
                Name = "User Temporary Files",
                Description = "Temporary files created by user applications (%TEMP%).",
                PathPattern = userTemp
            });

            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (Directory.Exists(winTemp))
            {
                list.Add(new JunkCategoryInfo
                {
                    Id = "WinTemp",
                    Name = "System Temporary Files",
                    Description = "Windows system temporary folder (C:\\Windows\\Temp).",
                    PathPattern = winTemp
                });
            }

            string prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (Directory.Exists(prefetch))
            {
                list.Add(new JunkCategoryInfo
                {
                    Id = "Prefetch",
                    Name = "Windows Prefetch Cache",
                    Description = "Outdated application launch prefetch data.",
                    PathPattern = prefetch
                });
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string d3dCache = Path.Combine(localAppData, "D3DSCache");
            if (Directory.Exists(d3dCache))
            {
                list.Add(new JunkCategoryInfo
                {
                    Id = "D3DSCache",
                    Name = "DirectX Shader Cache",
                    Description = "Compiled DirectX shader cache files.",
                    PathPattern = d3dCache
                });
            }

            string winUpdate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            if (Directory.Exists(winUpdate))
            {
                list.Add(new JunkCategoryInfo
                {
                    Id = "WinUpdateDownload",
                    Name = "Windows Update Cache",
                    Description = "Downloaded installation files for Windows Updates.",
                    PathPattern = winUpdate
                });
            }

            return list;
        }
    }
}
