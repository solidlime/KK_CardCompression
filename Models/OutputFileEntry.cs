using System.IO;

namespace KK_Archive.Models
{
    public class OutputFileEntry : FileEntry
    {
        private int _progressPercent;
        private bool _isProcessingComplete;

        public long OriginalSizeBytes { get; set; }
        public long OutputSizeBytes   => File.Exists(FullPath) ? new FileInfo(FullPath).Length : 0;
        public string OriginalFormatted => FormatSz(OriginalSizeBytes);
        public string OutputFormatted   => FormatSz(OutputSizeBytes);

        public int ProgressPercent
        {
            get => _progressPercent;
            set
            {
                var clamped = value;
                if (clamped < 0) clamped = 0;
                if (clamped > 100) clamped = 100;
                if (_progressPercent == clamped) return;
                _progressPercent = clamped;
                OnPropertyChanged();
            }
        }

        public bool IsProcessingComplete
        {
            get => _isProcessingComplete;
            set
            {
                if (_isProcessingComplete == value) return;
                _isProcessingComplete = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompressionRatioText));
            }
        }

        public string CompressionRatioText
        {
            get
            {
                if (!IsProcessingComplete) return "—";
                if (OriginalSizeBytes <= 0) return "—";
                double ratio = (1.0 - (double)OutputSizeBytes / OriginalSizeBytes) * 100.0;
                return ratio >= 0
                    ? $"{ratio:F1}%削減"
                    : $"{System.Math.Abs(ratio):F1}%増加";
            }
        }

        public OutputFileEntry(string outputPath, long originalSizeBytes)
            : base(outputPath)
        {
            OriginalSizeBytes = originalSizeBytes;
        }

        public void RefreshComputed()
        {
            OnPropertyChanged(nameof(OutputSizeBytes));
            OnPropertyChanged(nameof(SizeBytes));
            OnPropertyChanged(nameof(SizeFormatted));
            OnPropertyChanged(nameof(OutputFormatted));
            OnPropertyChanged(nameof(CompressionRatioText));
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
