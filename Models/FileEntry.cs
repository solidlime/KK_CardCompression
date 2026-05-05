using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace KK_CardCompression.Models
{
    public class FileEntry : INotifyPropertyChanged
    {
        private string _fullPath;

        public FileEntry(string fullPath)
        {
            _fullPath = fullPath;
        }

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); OnPropertyChanged(nameof(FolderPath)); OnPropertyChanged(nameof(SizeFormatted)); }
        }

        public string FileName    => Path.GetFileName(_fullPath);
        public string FolderPath  => Path.GetDirectoryName(_fullPath) ?? "";
        public long   SizeBytes   => File.Exists(_fullPath) ? new FileInfo(_fullPath).Length : 0;
        public string SizeFormatted => FormatSize(SizeBytes);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
