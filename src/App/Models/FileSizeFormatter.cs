namespace KK_CardCompression.Models
{
    /// <summary>
    /// ファイルサイズを人間が読みやすい形式に整形する。
    /// </summary>
    public static class FileSizeFormatter
    {
        public static string Format(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
