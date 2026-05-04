using System.IO;

namespace KK_Archive.Models
{
    public class OutputFileEntry : FileEntry
    {
        public long   OriginalSizeBytes { get; set; }
        public long   OutputSizeBytes   => File.Exists(FullPath) ? new FileInfo(FullPath).Length : 0;
        public string OriginalFormatted => FormatSz(OriginalSizeBytes);
        public string OutputFormatted   => FormatSz(OutputSizeBytes);

        public string CompressionRatioText
        {
            get
            {
                if (OriginalSizeBytes <= 0) return "—";
                double ratio = (1.0 - (double)OutputSizeBytes / OriginalSizeBytes) * 100.0;
                return $"{ratio:+0.0;-0.0;0.0}%";
            }
        }

        public OutputFileEntry(string outputPath, long originalSizeBytes)
            : base(outputPath)
        {
            OriginalSizeBytes = originalSizeBytes;
        }

        private static string FormatSz(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
