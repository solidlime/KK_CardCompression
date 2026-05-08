using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace KK_CardCompression.Models
{
    public class FileEntry : INotifyPropertyChanged
    {
        public FileEntry(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }

        public string FileName    => Path.GetFileName(FullPath);
        public string FolderPath  => Path.GetDirectoryName(FullPath) ?? "";
        public long   SizeBytes   => File.Exists(FullPath) ? new FileInfo(FullPath).Length : 0;
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
