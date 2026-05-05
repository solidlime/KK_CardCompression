using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KK_Archive.Models;
using KK_Archive.Services;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListView = System.Windows.Controls.ListView;
using Point = System.Windows.Point;

namespace KK_Archive
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<FileEntry> _inputFiles = new();
        private readonly ObservableCollection<OutputFileEntry> _outputFiles = new();

        private readonly Stopwatch _stopwatch = new();
        private Point _dragStartPoint;
        private bool _isDragging;
        private bool _canStartDrag;
        private CancellationTokenSource? _cts;

        private string _outputDirectory = string.Empty;
        private bool _isSyncingScroll;
        private bool _isPreviewEnabled = true;

        public MainWindow()
        {
            InitializeComponent();

            LvInput.ItemsSource = _inputFiles;
            LvOutput.ItemsSource = _outputFiles;

            InitCompressionLevelCombo();
            UpdateInputCount();
            UpdateOutputCount();
            LoadSettings();
        }

        private void InitCompressionLevelCombo()
        {
            CmbCompLevel.Items.Add(new CompressionOption("Fast（最速・互換設定）",   CompressionLevel.Fast));
            CmbCompLevel.Items.Add(new CompressionOption("Normal（バランス）",        CompressionLevel.Normal));
            CmbCompLevel.Items.Add(new CompressionOption("Maximum（高圧縮）",         CompressionLevel.Maximum));
            CmbCompLevel.Items.Add(new CompressionOption("Ultra（最高圧縮）",         CompressionLevel.Ultra));
            CmbCompLevel.DisplayMemberPath  = "Label";
            CmbCompLevel.SelectedValuePath  = "Level";
            CmbCompLevel.SelectedValue      = CompressionLevel.Maximum;
        }

        private void LoadSettings()
        {
            var settings = IniSettingsService.Load();

            if (!string.IsNullOrWhiteSpace(settings.LastOutputDirectory))
            {
                _outputDirectory = settings.LastOutputDirectory;
                TxtOutputPath.Text = $"出力先: {_outputDirectory}";
            }

            SelectCompressionLevel(settings.CompressionLevel);
            ChkRecompressPng.IsChecked = settings.RecompressPng;
            _isPreviewEnabled = settings.PreviewEnabled;
            TglPreviewEnabled.IsChecked = _isPreviewEnabled;
        }

        private void SelectCompressionLevel(CompressionLevel level)
        {
            foreach (var item in CmbCompLevel.Items)
            {
                if (item is CompressionOption option && option.Level == level)
                {
                    CmbCompLevel.SelectedItem = option;
                    return;
                }
            }
            // Fallback to Maximum if not found
            CmbCompLevel.SelectedValue = CompressionLevel.Maximum;
        }

        private void SaveSettings()
        {
            if (CmbCompLevel == null) return; // Guard: called before InitializeComponent completes
            IniSettingsService.Save(new AppSettings
            {
                LastOutputDirectory = _outputDirectory,
                CompressionLevel    = GetSelectedCompressionLevel(),
                RecompressPng       = ChkRecompressPng.IsChecked == true,
                PreviewEnabled      = _isPreviewEnabled,
            });
        }

        private CompressionLevel GetSelectedCompressionLevel()
            => CmbCompLevel?.SelectedValue is CompressionLevel l ? l : CompressionLevel.Maximum;

        private void CmbCompLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => SaveSettings();

        private void ChkRecompressPng_Changed(object sender, RoutedEventArgs e)
            => SaveSettings();

        private void TglPreviewEnabled_Changed(object sender, RoutedEventArgs e)
        {
            _isPreviewEnabled = TglPreviewEnabled.IsChecked == true;
            if (!_isPreviewEnabled)
                PreviewPopup.IsOpen = false;
            SaveSettings();
        }

        // ── 同期スクロール ──────────────────────────────────────────
        private ScrollViewer? _inputScrollViewer;
        private ScrollViewer? _outputScrollViewer;

        private void LvInput_Loaded(object sender, RoutedEventArgs e)
        {
            _inputScrollViewer = GetScrollViewer(LvInput);
            if (_inputScrollViewer != null)
                _inputScrollViewer.ScrollChanged += OnInputScrollChanged;
        }

        private void LvOutput_Loaded(object sender, RoutedEventArgs e)
        {
            _outputScrollViewer = GetScrollViewer(LvOutput);
            if (_outputScrollViewer != null)
                _outputScrollViewer.ScrollChanged += OnOutputScrollChanged;
        }

        private void OnInputScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll || e.VerticalChange == 0) return;
            _isSyncingScroll = true;
            _outputScrollViewer?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        private void OnOutputScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll || e.VerticalChange == 0) return;
            _isSyncingScroll = true;
            _inputScrollViewer?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject obj)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is ScrollViewer sv) return sv;
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        // ── マウスオーバープレビュー ────────────────────────────────
        private void LvInput_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPreviewEnabled) return;

            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not System.Windows.Controls.ListViewItem)
                element = VisualTreeHelper.GetParent(element);

            if (element is System.Windows.Controls.ListViewItem item && item.DataContext is FileEntry entry)
            {
                UpdatePreview(entry);
                PreviewPopup.IsOpen = true;
            }
            else
            {
                PreviewPopup.IsOpen = false;
            }
        }

        private void LvInput_MouseLeave(object sender, MouseEventArgs e)
            => PreviewPopup.IsOpen = false;

        private void LvOutput_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPreviewEnabled) return;

            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not System.Windows.Controls.ListViewItem)
                element = VisualTreeHelper.GetParent(element);

            if (element is System.Windows.Controls.ListViewItem item && item.DataContext is OutputFileEntry entry)
            {
                UpdatePreview(entry);
                PreviewPopup.IsOpen = true;
            }
            else
            {
                PreviewPopup.IsOpen = false;
            }
        }

        private void LvOutput_MouseLeave(object sender, MouseEventArgs e)
            => PreviewPopup.IsOpen = false;

        private void UpdatePreview(FileEntry? entry)
        {
            if (entry == null)
            {
                ImgPreview.Source = null;
                TxtPreviewName.Text = string.Empty;
                TxtPreviewSize.Text = string.Empty;
                return;
            }

            TxtPreviewName.Text = entry.FileName;
            TxtPreviewSize.Text = entry.SizeFormatted;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(entry.FullPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();
                ImgPreview.Source = bmp;
            }
            catch
            {
                ImgPreview.Source = null;
            }
        }


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
            if (_isDragging) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            int skipped = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var (dirAdded, dirSkipped) = AddFilesFromDirectory(path);
                    added += dirAdded;
                    skipped += dirSkipped;
                }
                else if (File.Exists(path) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    if (ShouldAcceptInputDropPath(path) && AddFile(path))
                        added++;
                    else
                        skipped++;
                }
            }

            UpdateInputCount();
            if (added > 0 && skipped > 0)
            {
                TxtStatus.Text = $"入力追加: {added} 件 / 除外: {skipped} 件";
            }
            else if (added > 0)
            {
                TxtStatus.Text = $"入力追加: {added} 件";
            }
            else if (skipped > 0)
            {
                TxtStatus.Text = $"入力追加対象なし（除外: {skipped} 件）";
            }
            else
            {
                TxtStatus.Text = "入力追加対象なし";
            }
        }

        private bool AddFile(string path)
        {
            if (_inputFiles.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))) return false;
            _inputFiles.Add(new FileEntry(path));
            return true;
        }

        private (int Added, int Skipped) AddFilesFromDirectory(string dirPath)
        {
            int added = 0;
            int skipped = 0;

            try
            {
                foreach (var file in Directory.GetFiles(dirPath, "*.png", SearchOption.AllDirectories))
                {
                    if (ShouldAcceptInputDropPath(file) && AddFile(file))
                        added++;
                    else
                        skipped++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダの読み込みに失敗しました: {ex.Message}");
            }

            return (added, skipped);
        }

        private bool ShouldAcceptInputDropPath(string path)
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;

            var normalizedPath = Path.GetFullPath(path);

            if (_outputFiles.Any(f =>
                    Path.GetFullPath(f.FullPath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (!string.IsNullOrWhiteSpace(_outputDirectory) && IsPathUnderDirectory(normalizedPath, _outputDirectory))
                return false;

            return true;
        }

        private static bool IsPathUnderDirectory(string filePath, string directoryPath)
        {
            var normalizedFile = Path.GetFullPath(filePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedDir = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var dirPrefix = normalizedDir + Path.DirectorySeparatorChar;
            return normalizedFile.Equals(normalizedDir, StringComparison.OrdinalIgnoreCase)
                || normalizedFile.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateInputCount()
        {
            TxtInputCount.Text = _inputFiles.Count > 0 ? $"{_inputFiles.Count} 件" : "";
            TxtDropHint.Visibility = _inputFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateOutputCount()
        {
            TxtOutputCount.Text = _outputFiles.Count > 0 ? $"{_outputFiles.Count} 件" : "";
        }

        private void LvInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;

            var selected = LvInput.SelectedItems.OfType<FileEntry>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
                _inputFiles.Remove(item);

            UpdateInputCount();
            TxtStatus.Text = $"入力一覧から {selected.Count} 件を削除しました";
            e.Handled = true;
        }

        private void Lv_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _canStartDrag = GetParentOfType<GridViewColumnHeader>(e.OriginalSource as DependencyObject) == null
                         && GetParentOfType<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject) != null;
        }

        private void Lv_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging || !_canStartDrag) return;

            var currentPoint = e.GetPosition(null);
            var delta = currentPoint - _dragStartPoint;

            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (sender is not ListView lv || lv.SelectedItem is not FileEntry) return;

            var paths = lv.SelectedItems
                .OfType<FileEntry>()
                .Select(f => f.FullPath)
                .Where(File.Exists)
                .ToArray();

            if (paths.Length == 0) return;

            _isDragging = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, paths);
                DragDrop.DoDragDrop(lv, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
            }
        }

        private static T? GetParentOfType<T>(DependencyObject? obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T result) return result;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

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

        private void BtnSelectOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _outputDirectory = dialog.SelectedPath;
            TxtOutputPath.Text = $"出力先: {_outputDirectory}";
            SaveSettings();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private async void BtnAuto_Click(object sender, RoutedEventArgs e) => await ProcessFilesAsync(null);
        private async void BtnCompress_Click(object sender, RoutedEventArgs e) => await ProcessFilesAsync(true);
        private async void BtnDecompress_Click(object sender, RoutedEventArgs e) => await ProcessFilesAsync(false);

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            PreviewPopup.IsOpen = false;
            _inputFiles.Clear();
            _outputFiles.Clear();
            UpdateInputCount();
            UpdateOutputCount();

            ProgressBar.Value = 0;
            TxtStatus.Text = "クリアしました";
            TxtStats.Text = string.Empty;
        }

        private void SetProcessingState(bool processing)
        {
            BtnAuto.IsEnabled = !processing;
            BtnCompress.IsEnabled = !processing;
            BtnDecompress.IsEnabled = !processing;
            BtnClear.IsEnabled = !processing;
            BtnCancel.IsEnabled = processing;
        }

        private async Task ProcessFilesAsync(bool? compress)
        {
            if (_inputFiles.Count == 0)
            {
                MessageBox.Show("処理するファイルがありません。");
                return;
            }

            if (string.IsNullOrWhiteSpace(_outputDirectory))
            {
                MessageBox.Show("出力先を設定してください。");
                return;
            }

            _outputFiles.Clear();
            UpdateOutputCount();

            _stopwatch.Restart();
            ProgressBar.Value = 0;
            TxtStatus.Text = "処理開始";
            TxtStats.Text = string.Empty;

            SetProcessingState(true);
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var files = _inputFiles.ToList();

            // 処理開始前に UI スレッドで設定値を取得
            var compressionLevel = GetSelectedCompressionLevel();
            bool recompressPng   = ChkRecompressPng.IsChecked == true;

            int successCount = 0;
            int failCount = 0;
            int total = files.Count;

            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested) break;

                var source = files[i];
                bool doCompress = compress ?? !CompressionService.IsCompressed(source.FullPath);

                string relativePath = GetRelativePath(source.FullPath, files.Select(f => f.FullPath).ToList());
                string outputPath = Path.Combine(_outputDirectory, relativePath);

                var outEntry = new OutputFileEntry(outputPath, source.SizeBytes) { ProgressPercent = 0 };
                _outputFiles.Add(outEntry);
                UpdateOutputCount();

                var progress = new Progress<double>(p =>
                {
                    outEntry.ProgressPercent = (int)Math.Round(p * 100.0);
                });

                ProcessResult result;
                try
                {
                    result = await Task.Run(
                        () => ProcessSingleFile(source.FullPath, doCompress, compressionLevel, recompressPng, progress),
                        token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.Success)
                {
                    successCount++;
                    outEntry.ProgressPercent = 100;
                    outEntry.IsProcessingComplete = true;
                    outEntry.RefreshComputed();
                }
                else
                {
                    failCount++;
                    outEntry.ProgressPercent = 0;
                    outEntry.IsProcessingComplete = false;
                }

                ProgressBar.Value = (i + 1) * 100.0 / total;
                TxtStatus.Text = $"処理中... ({i + 1}/{total})";
            }

            _stopwatch.Stop();
            bool cancelled = token.IsCancellationRequested;
            SetProcessingState(false);

            UpdateStats(files, successCount, failCount);
            TxtStatus.Text = cancelled
                ? $"中止しました ({successCount}/{total} 完了)"
                : $"処理完了 ({successCount}/{total} 成功)";
        }

        private record ProcessResult(bool Success, string? OutputPath, string? ErrorMessage = null);

        private ProcessResult ProcessSingleFile(string inputPath, bool compress,
                                                 CompressionLevel compressionLevel, bool recompressPng,
                                                 IProgress<double> progress)
        {
            string backupPath = inputPath + ".bak";
            try
            {
                File.Copy(inputPath, backupPath, true);
                progress.Report(0.02);

                string relativePath = GetRelativePath(inputPath, _inputFiles.Select(f => f.FullPath).ToList());
                string outputPath = Path.Combine(_outputDirectory, relativePath);

                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                if (compress)
                    CompressionService.CompressFile(inputPath, outputPath, compressionLevel, recompressPng, progress);
                else
                    CompressionService.DecompressFile(inputPath, outputPath, progress);

                if (!File.Exists(outputPath))
                    throw new InvalidOperationException("出力ファイルが作成されませんでした。");

                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                progress.Report(1.0);
                return new ProcessResult(true, outputPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, inputPath, true);
                    File.Delete(backupPath);
                }

                Dispatcher.Invoke(() =>
                    MessageBox.Show($"処理失敗: {Path.GetFileName(inputPath)}\n{ex.Message}"));

                return new ProcessResult(false, null, ex.Message);
            }
        }

        private static string GetRelativePath(string fullPath, List<string> allFiles)
        {
            if (allFiles.Count == 0)
                return Path.GetFileName(fullPath);

            var dirs = allFiles
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d!)
                .Distinct()
                .ToList();

            if (dirs.Count == 0)
                return Path.GetFileName(fullPath);

            string commonDir = dirs[0];
            foreach (var dir in dirs.Skip(1))
            {
                while (commonDir.Length > 0 && !dir.StartsWith(commonDir, StringComparison.OrdinalIgnoreCase))
                    commonDir = Path.GetDirectoryName(commonDir) ?? string.Empty;

                if (commonDir.Length == 0)
                    break;
            }

            if (string.IsNullOrWhiteSpace(commonDir))
                return Path.GetFileName(fullPath);

            return fullPath.StartsWith(commonDir, StringComparison.OrdinalIgnoreCase)
                ? fullPath[commonDir.Length..].TrimStart(Path.DirectorySeparatorChar)
                : Path.GetFileName(fullPath);
        }

        private void UpdateStats(List<FileEntry> files, int successCount, int failCount)
        {
            long totalInput = files.Sum(f => f.SizeBytes);
            long totalOutput = _outputFiles.Where(f => f.ProgressPercent == 100).Sum(f => f.OutputSizeBytes);

            double reductionRate = totalInput > 0
                ? (1.0 - (double)totalOutput / totalInput) * 100.0
                : 0.0;

            string ratioText = reductionRate >= 0
                ? $"削減率 {reductionRate:F1}%"
                : $"増加率 {Math.Abs(reductionRate):F1}%";

            TxtStats.Text = $"成功 {successCount} / 失敗 {failCount} | {FormatSize(totalInput)} -> {FormatSize(totalOutput)} | {ratioText} | {_stopwatch.Elapsed.TotalSeconds:F2}s";
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double value = bytes;

            while (value >= 1024 && order < sizes.Length - 1)
            {
                order++;
                value /= 1024;
            }

            return $"{value:F1} {sizes[order]}";
        }

        private sealed record CompressionOption(string Label, CompressionLevel Level);
    }
}
