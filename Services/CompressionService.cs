using System;
using System.IO;

namespace KK_Archive.Services
{
    public static class CompressionService
    {
        /// <summary>
        /// ファイルが圧縮済みか判定（簡易版：ファイル名で判定）
        /// </summary>
        public static bool IsCompressed(string filePath)
        {
            // 仮実装：ファイル名で判定
            return filePath.EndsWith("_compressed.png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 圧縮（仮実装：ファイルをコピー）
        /// </summary>
        public static void CompressFile(string inputPath, string outputPath)
        {
            // TODO: 実装LZMA圧縮
            File.Copy(inputPath, outputPath, true);
        }

        /// <summary>
        /// 解凍（仮実装：ファイルをコピー）
        /// </summary>
        public static void DecompressFile(string inputPath, string outputPath)
        {
            // TODO: 実装LZMA解凍
            File.Copy(inputPath, outputPath, true);
        }
    }
}
