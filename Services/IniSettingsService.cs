using System;
using System.Collections.Generic;
using System.IO;

namespace KK_Archive.Services
{
    public sealed class AppSettings
    {
        public string LastOutputDirectory { get; set; } = string.Empty;
        public string CompressionLevelTag { get; set; } = "Fast";
    }

    public static class IniSettingsService
    {
        private static readonly string s_iniPath =
            Path.Combine(AppContext.BaseDirectory, "KK_Archive.ini");

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(s_iniPath)) return settings;

            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(s_iniPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[")) continue;

                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;

                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim();
                    map[key] = val;
                }

                if (map.TryGetValue("LastOutputDirectory", out var output))
                    settings.LastOutputDirectory = output;

                if (map.TryGetValue("CompressionLevel", out var level) &&
                    (level == "Fast" || level == "Normal" || level == "Maximum"))
                    settings.CompressionLevelTag = level;
            }
            catch
            {
                return new AppSettings();
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            var content = string.Join(Environment.NewLine, new[]
            {
                "[Settings]",
                $"LastOutputDirectory={settings.LastOutputDirectory}",
                $"CompressionLevel={settings.CompressionLevelTag}",
                string.Empty,
            });

            File.WriteAllText(s_iniPath, content);
        }
    }
}
