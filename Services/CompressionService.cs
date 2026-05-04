using System;
using System.IO;
using System.Text;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace KK_Archive.Services
{
    /// <summary>
    /// LZMA 圧縮レベル。numFastBytes のみ変わる。
    /// Dictionary サイズは常に 64 MiB なので KK_SaveLoadCompression との互換性は完全維持。
    /// </summary>
    public enum CompressionLevel
    {
        Fast    = 5,    // 最速（オリジナルプラグインと同じ）
        Normal  = 32,   // バランス
        Maximum = 128,  // 最高圧縮
    }

    internal static class KkToken
    {
        public const string StudioToken     = "【KStudio】";
        public const string CharaToken      = "【KoiKatuChara";
        public const string CoordinateToken = "【KoiKatuClothes】";
    }

    public static class CompressionService
    {
        // LZMA encoder settings — Fastest speed, VeryLarge (64 MiB) dictionary
        // matches the original plugin settings
        private static readonly CoderPropID[] s_propIDs =
        {
            CoderPropID.DictionarySize,
            CoderPropID.PosStateBits,
            CoderPropID.LitContextBits,
            CoderPropID.LitPosBits,
            CoderPropID.NumFastBytes,
            CoderPropID.MatchFinder,
            CoderPropID.EndMarker,
        };

        private static readonly object[] s_properties =
        {
            1 << 26,  // 64 MiB dictionary (VeryLarge)
            2,        // posStateBits
            3,        // litContextBits
            0,        // litPosBits
            5,        // numFastBytes (Fastest)
            "BT4",    // matchFinder
            true,     // endMarker
        };

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>
        /// ファイルが KK_SaveLoadCompression 形式で圧縮済みか判定する。
        /// PNG 末尾の直後にある圧縮マーカー (int32: 101 or Version "101.x") を確認する。
        /// </summary>
        public static bool IsCompressed(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
                SkipPng(br);
                if (fs.Position >= fs.Length) return false;
                return GuessCompressed(br);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// KK カードファイルを LZMA で圧縮する。
        /// 出力形式: [PNGデータ] [圧縮マーカー(101)] [トークン] [LZMAストリーム]
        /// </summary>
        public static void CompressFile(string inputPath, string outputPath,
                                        CompressionLevel level = CompressionLevel.Fast)
        {
            using var inFs  = new FileStream(inputPath,  FileMode.Open,   FileAccess.Read,  FileShare.ReadWrite);
            using var outFs = new FileStream(outputPath, FileMode.Create,  FileAccess.Write);
            using var br    = new BinaryReader(inFs,  Encoding.UTF8, leaveOpen: true);
            using var bw    = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true);

            byte[] pngData   = LoadPngBytes(br);
            long   extraStart = inFs.Position;

            // トークン種別を判定（カーソルはextraStartに戻る）
            string? token = GuessToken(br);
            if (token == null)
                throw new InvalidDataException("Koikatsuのファイル形式を認識できませんでした。");

            // 既に圧縮済みなら例外
            inFs.Seek(extraStart, SeekOrigin.Begin);
            if (GuessCompressed(br))
                throw new InvalidOperationException("このファイルは既に圧縮されています。");

            inFs.Seek(extraStart, SeekOrigin.Begin);

            // PNG を出力
            bw.Write(pngData);

            // 圧縮マーカー + トークン を出力
            if (token == KkToken.StudioToken)
                bw.Write(new Version(101, 0, 0, 0).ToString()); // BinaryWriter string
            else
                bw.Write(101); // int32
            bw.Write(token);

            // PNG 以降のデータを LZMA 圧縮して出力
            using var msCompressed = new MemoryStream();
            LzmaCompress(inFs, msCompressed, level);
            bw.Write(msCompressed.ToArray());
        }

        /// <summary>
        /// LZMA 圧縮済み KK カードファイルを元に戻す。
        /// </summary>
        public static void DecompressFile(string inputPath, string outputPath)
        {
            using var inFs  = new FileStream(inputPath,  FileMode.Open,   FileAccess.Read,  FileShare.ReadWrite);
            using var outFs = new FileStream(outputPath, FileMode.Create,  FileAccess.Write);
            using var br    = new BinaryReader(inFs,  Encoding.UTF8, leaveOpen: true);
            using var bw    = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true);

            byte[] pngData = LoadPngBytes(br);

            if (!GuessCompressed(br))
                throw new InvalidDataException("このファイルは圧縮されていません。");

            string? token = GuessToken(br);
            if (token == null)
                throw new InvalidDataException("Koikatsuのファイル形式を認識できませんでした。");

            // 圧縮マーカーをスキップ
            if (token == KkToken.StudioToken)
                br.ReadString(); // "101.0.0.0"
            else
                br.ReadInt32();  // 101

            // トークン文字列をスキップ
            br.ReadString();

            // PNG を出力してから LZMA 展開（展開結果が [100+token+rawData]）
            bw.Write(pngData);
            bw.Flush();

            LzmaDecompress(inFs, outFs);
        }

        // ---------------------------------------------------------------
        // PNG helpers
        // ---------------------------------------------------------------

        private static byte[] LoadPngBytes(BinaryReader br)
        {
            long start = br.BaseStream.Position;
            SkipPng(br);
            long end = br.BaseStream.Position;
            br.BaseStream.Seek(start, SeekOrigin.Begin);
            return br.ReadBytes((int)(end - start));
        }

        private static void SkipPng(BinaryReader br)
        {
            br.ReadBytes(8); // PNG magic
            while (true)
            {
                // PNG chunk length は big-endian
                byte[] lenBytes = br.ReadBytes(4);
                int    length   = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
                byte[] type     = br.ReadBytes(4);
                br.ReadBytes(length); // chunk data
                br.ReadBytes(4);      // CRC
                if (type[0] == 'I' && type[1] == 'E' && type[2] == 'N' && type[3] == 'D')
                    break;
            }
        }

        /// <summary>
        /// PNG 末尾直後から圧縮フラグを読む。読み位置は変化しない。
        /// </summary>
        private static bool GuessCompressed(BinaryReader br)
        {
            long pos = br.BaseStream.Position;
            try
            {
                int r = br.ReadInt32();
                if (r == 101) return true;
                if (r == 100) return false;

                // Studio: 先頭がバージョン文字列
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
                string st = br.ReadString();
                if (Version.TryParse(st, out var v)) return v.Major == 101;
            }
            catch { /* 不正なファイル */ }
            finally
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
            }
            return false;
        }

        /// <summary>
        /// PNG 末尾直後からトークン種別を判定する。読み位置は変化しない。
        /// </summary>
        private static string? GuessToken(BinaryReader br)
        {
            long pos = br.BaseStream.Position;
            try
            {
                int r = br.ReadInt32();
                if (r != 100 && r != 101)
                {
                    // Studio: 先頭はバージョン文字列
                    return KkToken.StudioToken;
                }

                string token = br.ReadString();
                if (token.Contains(KkToken.CharaToken))
                    return KkToken.CharaToken + "】sex1";
                if (token == KkToken.CoordinateToken)
                    return KkToken.CoordinateToken;

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
            }
        }

        // ---------------------------------------------------------------
        // LZMA helpers
        // ---------------------------------------------------------------

        private static void LzmaCompress(Stream input, Stream output,
                                          CompressionLevel level = CompressionLevel.Fast)
        {
            var props = new object[]
            {
                1 << 26,           // DictionarySize: 64 MiB (互換性のため固定)
                2,                 // PosStateBits
                3,                 // LitContextBits
                0,                 // LitPosBits
                (int)level,        // NumFastBytes: 圧縮レベルによって変わる
                "BT4",             // MatchFinder
                true,              // EndMarker
            };

            var encoder = new SevenZip.Compression.LZMA.Encoder();
            encoder.SetCoderProperties(s_propIDs, props);
            encoder.WriteCoderProperties(output);

            long remaining = input.Length - input.Position;
            for (int i = 0; i < 8; i++)
                output.WriteByte((byte)(remaining >> (8 * i)));

            encoder.Code(input, output, -1, -1, null);
        }

        private static void LzmaDecompress(Stream input, Stream output)
        {
            var    decoder = new SevenZip.Compression.LZMA.Decoder();
            byte[] props   = new byte[5];
            if (input.Read(props, 0, 5) != 5)
                throw new InvalidDataException("LZMAストリームが不正です（ヘッダ短すぎ）。");
            decoder.SetDecoderProperties(props);

            long fileLength = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = input.ReadByte();
                if (v < 0) throw new InvalidDataException("LZMAストリームが不正です（サイズ読み取り失敗）。");
                fileLength |= ((long)(byte)v) << (8 * i);
            }

            long compressedSize = input.Length - input.Position;
            decoder.Code(input, output, compressedSize, fileLength, null);
        }
    }
}
