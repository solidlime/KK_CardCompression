using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KK_Archive.Models;
using KK_Archive.Services;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs  = System.Windows.DragEventArgs;
using MessageBox     = System.Windows.MessageBox;
using Point          = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DataFormats    = System.Windows.DataFormats;
using DataObject     = System.Windows.DataObject;
using ListView       = System.Windows.Controls.ListView;

namespace KK_Archive
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FileEntry>       _inputFiles  = new();
        private readonly ObservableCollection<OutputFileEntry> _outputFiles = new();

        private string  _outputDirectory = string.Empty;
        private int     _successCount;
        private int     _failCount;
        private readonly Stopwatch _stopwatch = new();

        // For OLE drag initiation
        private Point   _dragStartPoint;
        private bool    _isDragging;

        public MainWindow()
        {
            InitializeComponent();
            LvInput.ItemsSource  = _inputFiles;
            LvOutput.ItemsSource = _outputFiles;
        }

        // ─────────────────────────────────────────────
        // Drop from Explorer → Window
        // ─────────────────────────────────────────────
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    AddFilesFromDirectory(path);
                else if (File.Exists(path) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    AddFile(path);
            }
            UpdateInputCount();
        }

        private void AddFile(string path)
        {
            if (_inputFiles.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            _inputFiles.Add(new FileEntry(path));
        }

        private void AddFilesFromDirectory(string dirPath)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dirPath, "*.png", SearchOption.AllDirectories))
                    AddFile(file);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダの読み込みに失敗しました: {ex.Message}");
            }
        }

        private void UpdateInputCount()
        {
            TxtInputCount.Text = _inputFiles.Count > 0 ? $"({_inputFiles.Count} 件)" : "";
            TxtDropHint.Visibility = _inputFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateOutputCount()
        {
            TxtOutputCount.Text = _outputFiles.Count > 0 ? $"({_outputFiles.Count} 件)" : "";
        }

        // ─────────────────────────────────────────────
        // OLE Drag FROM ListView to Explorer
        // ─────────────────────────────────────────────
        private void Lv_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Lv_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

            var pos   = e.GetPosition(null);
            var delta = pos - _dragStartPoint;

            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var lv = sender as ListView;
            if (lv?.SelectedItem is not FileEntry entry) return;

            // Collect all selected items
            var paths = lv.SelectedItems
                          .OfType<FileEntry>()
                          .Select(f => f.FullPath)
                          .Where(File.Exists)
                          .ToArray();

            if (paths.Length == 0) return;

            _isDragging = true;
            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(lv, data, DragDropEffects.Copy | DragDropEffects.Move);
            _isDragging = false;
        }

        // ─────────────────────────────────────────────
        // Double-click to open
        // ─────────────────────────────────────────────
        private void LvInput_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LvInput.SelectedItem is FileEntry entry)
                OpenFile(entry.FullPath);
        }

        private void LvOutput_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LvOutput.SelectedItem is OutputFileEntry entry)
                OpenFile(entry.FullPath);
        }

        private static void OpenFile(string path)
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        // ─────────────────────────────────────────────
        // Toolbar buttons
        // ─────────────────────────────────────────────
        private void BtnSelectOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _outputDirectory = dialog.SelectedPath;
                TxtOutputPath.Text = $"出力先: {_outputDirectory}";
            }
        }

        private async void BtnAuto_Click       (object sender, RoutedEventArgs e) => await ProcessFilesAsync(null);
        private async void BtnCompress_Click   (object sender, RoutedEventArgs e) => await ProcessFilesAsync(true);
        private async void BtnDecompress_Click (object sender, RoutedEventArgs e) => await ProcessFilesAsync(false);

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _inputFiles.Clear();
            _outputFiles.Clear();
            UpdateInputCount();
            UpdateOutputCount();
            ProgressBar.Value = 0;
            TxtStatus.Text = "クリアしました。";
            TxtStats.Text  = "";
        }

        private CompressionLevel GetSelectedLevel()
        {
            var tag = (CmbLevel.SelectedItem as ComboBoxItem)?.Tag as string ?? "Fast";
            return tag switch
            {
                "Normal"  => CompressionLevel.Normal,
                "Maximum" => CompressionLevel.Maximum,
                _         => CompressionLevel.Fast,
            };
        }

        // ─────────────────────────────────────────────
        // Core processing
        // ─────────────────────────────────────────────
        private async Task ProcessFilesAsync(bool? compress)
        {
            if (_inputFiles.Count == 0)      { MessageBox.Show("処理するファイルがありません。");   return; }
            if (string.IsNullOrEmpty(_outputDirectory)) { MessageBox.Show("出力先を選択してください。"); return; }

            _successCount = 0;
            _failCount    = 0;
            _stopwatch.Restart();
            ProgressBar.Value = 0;
            TxtStatus.Text    = "処理中...";

            var level    = GetSelectedLevel();
            var files    = _inputFiles.ToList();   // snapshot
            int total    = files.Count;

            for (int i = 0; i < total; i++)
            {
                var entry = files[i];
                bool doCompress = compress ?? !CompressionService.IsCompressed(entry.FullPath);

                long origSize = entry.SizeBytes;
                var  result   = await Task.Run(() => ProcessSingleFile(entry.FullPath, doCompress, level));

                if (result.Success)
                {
                    _successCount++;
                    Dispatcher.Invoke(() =>
                    {
                        _outputFiles.Add(new OutputFileEntry(result.OutputPath!, origSize));
                        UpdateOutputCount();
                    });
                }
                else _failCount++;

                ProgressBar.Value  = (i + 1) * 100.0 / total;
                TxtStatus.Text     = $"処理中... ({i + 1}/{total})";
            }

            _stopwatch.Stop();
            UpdateStats(files);
            TxtStatus.Text = $"処理完了！ 成功: {_successCount} / 失敗: {_failCount}";
        }

        private record ProcessResult(bool Success, string? OutputPath);

        private ProcessResult ProcessSingleFile(string inputPath, bool compress, CompressionLevel level)
        {
            string backupPath = inputPath + ".bak";
            try
            {
                File.Copy(inputPath, backupPath, overwrite: true);

                string relativePath = GetRelativePath(inputPath, _inputFiles.Select(f => f.FullPath).ToList());
                string outputPath   = Path.Combine(_outputDirectory, relativePath);

                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                if (compress) CompressionService.CompressFile(inputPath, outputPath, level);
                else          CompressionService.DecompressFile(inputPath, outputPath);

                if (!File.Exists(outputPath)) throw new Exception("出力ファイルが作成されませんでした。");

                if (File.Exists(backupPath)) File.Delete(backupPath);
                return new ProcessResult(true, outputPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(backupPath)) { File.Copy(backupPath, inputPath, true); File.Delete(backupPath); }
                Dispatcher.Invoke(() => MessageBox.Show($"処理失敗: {Path.GetFileName(inputPath)}\n{ex.Message}"));
                return new ProcessResult(false, null);
            }
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────
        private static string GetRelativePath(string fullPath, List<string> allFiles)
        {
            if (allFiles.Count == 0) return Path.GetFileName(fullPath);

            var dirs = allFiles
                .Select(p => Path.GetDirectoryName(p))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .Distinct()
                .ToList();

            if (dirs.Count == 0) return Path.GetFileName(fullPath);

            string commonDir = dirs[0];
            foreach (var dir in dirs.Skip(1))
            {
                while (commonDir.Length > 0 &&
                       !dir.StartsWith(commonDir, StringComparison.OrdinalIgnoreCase))
                    commonDir = Path.GetDirectoryName(commonDir) ?? "";
                if (commonDir.Length == 0) break;
            }

            if (string.IsNullOrEmpty(commonDir)) return fullPath;

            if (fullPath.StartsWith(commonDir, StringComparison.OrdinalIgnoreCase))
                return fullPath[commonDir.Length..].TrimStart(Path.DirectorySeparatorChar);

            return fullPath;
        }

        private void UpdateStats(List<FileEntry> files)
        {
            long before = files.Sum(f => f.SizeBytes);
            long after  = _outputFiles.Sum(f => f.SizeBytes);
            double rate = before > 0 ? (1 - (double)after / before) * 100 : 0;

            TxtStats.Text = $"元: {FormatSize(before)}  →  後: {FormatSize(after)}" +
                            $"  ({(rate >= 0 ? "-" : "+")}{Math.Abs(rate):F1}%)  " +
                            $"{_stopwatch.Elapsed.TotalSeconds:F2}秒";
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int   order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
            return $"{size:F1} {sizes[order]}";
        }
    }
}
