using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace KK_CardCompression.Services
{
    /// <summary>
    /// シーンデータ内の埋め込みPNGを検出・再圧縮する前処理サービス。
    /// シーンバイナリの構造を完全に解析せず、PNGシグネチャをスキャンして
    /// 安全に再圧縮する。
    /// </summary>
    public static class ScenePreprocessor
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] IendChunkType = { 73, 69, 78, 68 }; // "IEND"

        /// <summary>
        /// シーンデータ内の全埋め込みPNGを再圧縮し、新しいバイト列を返す。
        /// 変更がなければ null を返す。
        /// </summary>
        /// <param name="sceneData">PNG以降のシーンデータ全体</param>
        /// <param name="dataStartOffset">シーンデータ内のオブジェクト領域開始オフセット（バージョン文字列以降）</param>
        public static byte[]? Process(byte[] sceneData, long dataStartOffset)
        {
            var regions = FindAllPngs(sceneData, (int)dataStartOffset);
            if (regions.Count == 0) return null;

            bool anyChanged = false;
            var recompressedData = new byte[regions.Count][];

            for (int i = 0; i < regions.Count; i++)
            {
                byte[] recompressed = RecompressPng(regions[i].Data);
                if (recompressed.Length < regions[i].Data.Length)
                {
                    recompressedData[i] = recompressed;
                    anyChanged = true;
                }
                else
                {
                    recompressedData[i] = regions[i].Data; // 元のまま
                }
            }

            if (!anyChanged) return null;

            // 新しいバイト列を構築
            long totalNewSize = 0;
            for (int i = 0; i < regions.Count; i++)
                totalNewSize += recompressedData[i].Length;

            long unchangedSize = sceneData.Length;
            foreach (var r in regions)
                unchangedSize -= r.OriginalSize;
            long newSize = unchangedSize + totalNewSize;

            var result = new byte[newSize];
            long srcPos = 0, dstPos = 0;

            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];

                // PNGの直前までコピー
                long copyLen = region.Offset - srcPos;
                if (copyLen > 0)
                {
                    Array.Copy(sceneData, srcPos, result, dstPos, copyLen);
                    dstPos += copyLen;
                }

                // 再圧縮後のPNGをコピー
                Array.Copy(recompressedData[i], 0, result, dstPos, recompressedData[i].Length);
                dstPos += recompressedData[i].Length;

                // 元のPNGの終わりまでスキップ
                srcPos = region.Offset + region.OriginalSize;
            }

            // 残りをコピー
            long tailLen = sceneData.Length - srcPos;
            if (tailLen > 0)
            {
                Array.Copy(sceneData, srcPos, result, dstPos, tailLen);
            }

            return result;
        }

        /// <summary>
        /// バイト列から全PNGを検出する。
        /// </summary>
        private static List<PngRegion> FindAllPngs(byte[] data, int startOffset)
        {
            var regions = new List<PngRegion>();
            int pos = startOffset;

            while (pos <= data.Length - PngSignature.Length)
            {
                // PNGシグネチャを検索
                if (!MatchAt(data, pos, PngSignature))
                {
                    pos++;
                    continue;
                }

                // IENDチャンクまで走査してPNG終端を特定
                int pngEnd = FindPngEnd(data, pos);
                if (pngEnd < 0)
                {
                    pos++;
                    continue;
                }

                int pngSize = pngEnd - pos;
                var pngData = new byte[pngSize];
                Array.Copy(data, pos, pngData, 0, pngSize);

                regions.Add(new PngRegion
                {
                    Offset = pos,
                    OriginalSize = pngSize,
                    Data = pngData,
                });

                pos = pngEnd;
            }

            return regions;
        }

        /// <summary>
        /// PNGシグネチャ位置からIENDチャンク終端までを走査し、PNG終端オフセットを返す。
        /// 見つからない場合は -1。
        /// </summary>
        private static int FindPngEnd(byte[] data, int pngStart)
        {
            // PNG構造: [8-byte signature] [chunk: 4-byte length + 4-byte type + data + 4-byte CRC]...
            int pos = pngStart + 8; // シグネチャ直後

            while (pos + 12 <= data.Length) // チャンク最低長: 4(len) + 4(type) + 0(data) + 4(crc) = 12
            {
                // チャンク長（ビッグエンディアン）
                int chunkLen = (data[pos] << 24) | (data[pos + 1] << 16)
                             | (data[pos + 2] << 8) | data[pos + 3];
                pos += 4; // length分進む

                // チャンクタイプ
                if (pos + 4 > data.Length) return -1;

                bool isIend = data[pos] == IendChunkType[0]
                           && data[pos + 1] == IendChunkType[1]
                           && data[pos + 2] == IendChunkType[2]
                           && data[pos + 3] == IendChunkType[3];

                pos += 4; // type分進む

                if (chunkLen < 0 || pos + chunkLen + 4 > data.Length)
                    return -1;

                pos += chunkLen + 4; // data + CRC分進む

                if (isIend)
                    return pos;
            }

            return -1;
        }

        private static bool MatchAt(byte[] data, int offset, byte[] pattern)
        {
            if (offset + pattern.Length > data.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (data[offset + i] != pattern[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// PNGをBestCompressionで再エンコードする。
        /// 再圧縮後が元より大きい場合は元のデータを返す。
        /// </summary>
        private static byte[] RecompressPng(byte[] originalPngData)
        {
            try
            {
                using var input = new MemoryStream(originalPngData);
                using var output = new MemoryStream();
                using var image = SixLabors.ImageSharp.Image.Load(input);
                image.SaveAsPng(output, new PngEncoder
                {
                    CompressionLevel = PngCompressionLevel.BestCompression,
                    FilterMethod = PngFilterMethod.Adaptive,
                });

                byte[] recompressed = output.ToArray();
                return recompressed.Length < originalPngData.Length ? recompressed : originalPngData;
            }
            catch
            {
                return originalPngData;
            }
        }

        private struct PngRegion
        {
            public long Offset;        // シーンデータ内のオフセット
            public int OriginalSize;    // 元のPNGサイズ
            public byte[] Data;         // PNGデータ（再圧縮後の場合あり）
        }
    }
}