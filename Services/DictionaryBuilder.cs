using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZstdSharp;

namespace KK_CardCompression.Services
{
    /// <summary>
    /// Zstd 辞書学習ツール。test/ ディレクトリの全PNGファイルから
    /// シーンデータを抽出し、辞書を構築する。
    /// 本番アプリには含めず、開発時の辞書生成用。
    /// </summary>
    public static class DictionaryBuilder
    {
        /// <summary>
        /// 指定ディレクトリ内の全PNGファイルからシーンデータを抽出し、
        /// Zstd辞書を学習してファイルに保存する。
        /// </summary>
        /// <param name="testDir">test/ ディレクトリのパス</param>
        /// <param name="outputPath">出力辞書ファイルパス</param>
        /// <param name="dictCapacity">辞書容量（デフォルト110KB）</param>
        /// <returns>学習に使用したサンプル数</returns>
        public static int TrainAndSave(string testDir, string outputPath, int dictCapacity = 112640)
        {
            var samples = CollectSamples(testDir);
            if (samples.Count == 0)
                throw new InvalidOperationException("学習データが見つかりません。PNGファイルが存在するか確認してください。");

            Console.WriteLine($"学習サンプル数: {samples.Count}");
            long totalBytes = samples.Sum(s => (long)s.Length);
            Console.WriteLine($"合計データ量: {totalBytes / (1024.0 * 1024.0):F1} MB");

            byte[] dict = DictBuilder.TrainFromBuffer(samples, dictCapacity);
            File.WriteAllBytes(outputPath, dict);

            Console.WriteLine($"辞書サイズ: {dict.Length} bytes → {outputPath}");
            return samples.Count;
        }

        /// <summary>
        /// 指定ディレクトリ内の全PNGファイルからPNG以降のデータを抽出する。
        /// キャラ・衣装・シーンの全種別を対象とする。
        /// </summary>
        private static List<byte[]> CollectSamples(string testDir)
        {
            var samples = new List<byte[]>();
            var pngFiles = Directory.GetFiles(testDir, "*.png", SearchOption.AllDirectories);

            Console.WriteLine($"PNGファイル検索中: {testDir} ({pngFiles.Length} 件)");

            foreach (var file in pngFiles)
            {
                try
                {
                    byte[] data = ExtractDataAfterPng(file);
                    if (data.Length >= 1024) // 最小1KB以上のデータのみ
                        samples.Add(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  スキップ: {Path.GetFileName(file)} ({ex.Message})");
                }
            }

            return samples;
        }

        /// <summary>
        /// PNGファイルからIEND以降のデータを抽出する。
        /// </summary>
        private static byte[] ExtractDataAfterPng(string pngPath)
        {
            using var fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8);

            // PNGをスキップ
            CompressionService.SkipPng(br);

            long dataStart = fs.Position;
            if (dataStart >= fs.Length)
                return Array.Empty<byte>();

            int remaining = (int)(fs.Length - dataStart);
            byte[] data = new byte[remaining];
            fs.Read(data, 0, remaining);
            return data;
        }
    }
}