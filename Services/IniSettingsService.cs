using System;
using System.Collections.Generic;
using System.IO;

namespace KK_CardCompression.Services
{
    public sealed class AppSettings
    {
        public string LastOutputDirectory { get; set; } = string.Empty;
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Maximum;
        public bool PreviewEnabled { get; set; } = true;
    }

    public static class IniSettingsService
    {
        private static readonly string s_iniPath =
            Path.Combine(AppContext.BaseDirectory, "KK_CardCompression.ini");

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

                if (map.TryGetValue("CompressionLevel", out var lvl)
                    && Enum.TryParse<CompressionLevel>(lvl, ignoreCase: true, out var cl))
                    settings.CompressionLevel = cl;

                if (map.TryGetValue("PreviewEnabled", out var prev)
                    && bool.TryParse(prev, out var pe))
                    settings.PreviewEnabled = pe;
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
                $"CompressionLevel={settings.CompressionLevel}",
                $"PreviewEnabled={settings.PreviewEnabled}",
                string.Empty,
            });

            File.WriteAllText(s_iniPath, content);
        }
    }
}
