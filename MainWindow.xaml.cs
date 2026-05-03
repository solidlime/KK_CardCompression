using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KK_Archive.Services;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace KK_Archive
{
    public partial class MainWindow : Window
    {
        private string outputDirectory = string.Empty;
        private List<string> filesToProcess = new List<string>();
        private int successCount = 0;
        private int failCount = 0;
        private Stopwatch stopwatch = new Stopwatch();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_DragEnter(object sender, global::System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(global::System.Windows.DataFormats.FileDrop))
                e.Effects = global::System.Windows.DragDropEffects.Copy;
            else
                e.Effects = global::System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, global::System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(global::System.Windows.DataFormats.FileDrop))
            {
                string[] paths = (string[])e.Data.GetData(global::System.Windows.DataFormats.FileDrop);
                filesToProcess.Clear();
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                        AddFilesFromDirectory(path);
                    else if (File.Exists(path) && Path.GetExtension(path).ToLower() == ".png")
                        filesToProcess.Add(path);
                }
                TxtStatus.Text = $"{filesToProcess.Count} 件のファイルを追加しました。";
            }
        }

        private void AddFilesFromDirectory(string dirPath)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath, "*.png", SearchOption.AllDirectories))
                    filesToProcess.Add(file);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダの読み込みに失敗しました: {ex.Message}");
            }
        }

        private void BtnSelectOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                outputDirectory = dialog.SelectedPath;
                TxtOutputPath.Text = $"出力先: {outputDirectory}";
            }
        }

        private async void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            await ProcessFilesAuto();
        }

        private async void BtnCompress_Click(object sender, RoutedEventArgs e)
        {
            await ProcessFiles(true);
        }

        private async void BtnDecompress_Click(object sender, RoutedEventArgs e)
        {
            await ProcessFiles(false);
        }

        private async Task ProcessFilesAuto()
        {
            if (filesToProcess.Count == 0)
            {
                MessageBox.Show("処理するファイルがありません。");
                return;
            }
            if (string.IsNullOrEmpty(outputDirectory))
            {
                MessageBox.Show("出力先を選択してください。");
                return;
            }

            successCount = 0;
            failCount = 0;
            stopwatch.Restart();
            ProgressBar.Value = 0;
            TxtStatus.Text = "処理中...";

            int total = filesToProcess.Count;
            for (int i = 0; i < total; i++)
            {
                var file = filesToProcess[i];
                bool isCompressed = CompressionService.IsCompressed(file);
                bool success = await Task.Run(() => ProcessSingleFile(file, !isCompressed));
                if (success) successCount++;
                else failCount++;

                ProgressBar.Value = (i + 1) * 100.0 / total;
                TxtStatus.Text = $"処理中... ({i + 1}/{total})";
            }

            stopwatch.Stop();
            UpdateStats(total);
            TxtStatus.Text = "処理完了！";
        }

        private async Task ProcessFiles(bool compress)
        {
            if (filesToProcess.Count == 0)
            {
                MessageBox.Show("処理するファイルがありません。");
                return;
            }
            if (string.IsNullOrEmpty(outputDirectory))
            {
                MessageBox.Show("出力先を選択してください。");
                return;
            }

            successCount = 0;
            failCount = 0;
            stopwatch.Restart();
            ProgressBar.Value = 0;
            TxtStatus.Text = "処理中...";

            int total = filesToProcess.Count;
            for (int i = 0; i < total; i++)
            {
                var file = filesToProcess[i];
                bool success = await Task.Run(() => ProcessSingleFile(file, compress));
                if (success) successCount++;
                else failCount++;

                ProgressBar.Value = (i + 1) * 100.0 / total;
                TxtStatus.Text = $"処理中... ({i + 1}/{total})";
            }

            stopwatch.Stop();
            UpdateStats(total);
            TxtStatus.Text = "処理完了！";
        }

        private bool ProcessSingleFile(string inputPath, bool compress)
        {
            try
            {
                string backupPath = inputPath + ".bak";
                File.Copy(inputPath, backupPath, true);

                string relativePath = GetRelativePath(inputPath);
                string outputPath = Path.Combine(outputDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                if (compress)
                    CompressionService.CompressFile(inputPath, outputPath);
                else
                    CompressionService.DecompressFile(inputPath, outputPath);

                if (!File.Exists(outputPath))
                    throw new Exception("出力ファイルが作成されませんでした。");

                return true;
            }
            catch (Exception ex)
            {
                string backupPath = inputPath + ".bak";
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, inputPath, true);
                    File.Delete(backupPath);
                }
                Dispatcher.Invoke(() => MessageBox.Show($"処理失敗: {Path.GetFileName(inputPath)}\n{ex.Message}"));
                return false;
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (filesToProcess.Count == 0) return Path.GetFileName(fullPath);

            var dirs = filesToProcess.Select(p => Path.GetDirectoryName(p)).Distinct();
            string commonDir = "";
            foreach (var dir in dirs)
            {
                if (fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && dir.Length > commonDir.Length)
                    commonDir = dir;
            }

            return string.IsNullOrEmpty(commonDir) ? Path.GetFileName(fullPath) :
                fullPath.Substring(commonDir.Length).TrimStart(Path.DirectorySeparatorChar);
        }

        private void UpdateStats(int total)
        {
            var totalSizeBefore = filesToProcess.Sum(f => new FileInfo(f).Length);
            var totalSizeAfter = filesToProcess.Sum(f =>
            {
                string relativePath = GetRelativePath(f);
                string outputPath = Path.Combine(outputDirectory, relativePath);
                return File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            });

            double compressionRate = totalSizeBefore > 0 ? (1 - (double)totalSizeAfter / totalSizeBefore) * 100 : 0;
            TxtStats.Text = $"成功: {successCount} 件, 失敗: {failCount} 件\n" +
                            $"元サイズ: {FormatSize(totalSizeBefore)}, 処理後: {FormatSize(totalSizeAfter)}\n" +
                            $"圧縮率: {compressionRate:F2}%, 時間: {stopwatch.Elapsed.TotalSeconds:F2}秒";
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F2} {sizes[order]}";
        }
    }
}
