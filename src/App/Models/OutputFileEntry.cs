using System.IO;

namespace KK_CardCompression.Models
{
    public class OutputFileEntry : FileEntry
    {
        private int _progressPercent;
        private bool _isProcessingComplete;
        private bool _isSkipped;

        public long OriginalSizeBytes { get; private set; }
        public long OutputSizeBytes   => File.Exists(FullPath) ? new FileInfo(FullPath).Length : 0;
        public string OriginalFormatted => FileSizeFormatter.Format(OriginalSizeBytes);
        public string OutputFormatted   => FileSizeFormatter.Format(OutputSizeBytes);

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

        public bool IsSkipped
        {
            get => _isSkipped;
            set
            {
                if (_isSkipped == value) return;
                _isSkipped = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompressionRatioText));
            }
        }

        public string CompressionRatioText
        {
            get
            {
                if (IsSkipped) return "スキップ";
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

    }
}
