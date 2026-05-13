using System;
using System.IO;
using System.Text;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace KK_CardCompression.Services
{
    internal static class KkToken
    {
        public const string StudioToken     = "【KStudio】";
        public const string CharaToken      = "【KoiKatuChara";
        public const string CoordinateToken = "【KoiKatuClothes】";
    }

    public static class KkFormatMarker
    {
        public const int Raw  = 100;
        public const int Lzma = 101;
    }

    public static class CompressionService
    {
        // PNG signature for validation
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        // PNG chunk constants
        private const int PngChunkLengthBytes = 4;
        private const int PngCrcBytes = 4;

        // LZMA encoder parameters
        private const int LzmaDictionarySize = 1 << 26; // 64 MiB
        private const int LzmaPosStateBits = 2;
        private const int LzmaLitContextBits = 3;
        private const int LzmaLitPosBits = 0;
        private const int LzmaNumFastBytes = 5;
        private const string LzmaMatchFinder = "BT4";
        private const int LzmaAlgorithm = 2;

        // Progress reporting constants
        private const double ProgressReadFile = 0.02;
        private const double ProgressAfterPng = 0.12;
        private const double ProgressAfterCompress = 0.95;
        private const double ProgressAfterValidate = 0.96;

        // LZMA encoder settings — 64 MiB dictionary, matches KK_SaveLoadCompression
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

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>
        /// ファイルが圧縮済みか判定する。
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] IsCompressed check failed for '{System.IO.Path.GetFileName(filePath)}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// KK カードファイルを LZMA で圧縮する。
        /// 出力形式: [PNGデータ] [圧縮マーカー] [トークン] [LZMA圧縮ストリーム]
        /// </summary>
        public static void CompressFile(string inputPath, string outputPath,
                                        IProgress<double>? progress = null)
        {
            {
                using var inFs  = new FileStream(inputPath,  FileMode.Open,   FileAccess.Read,  FileShare.ReadWrite);
                using var br    = new BinaryReader(inFs,  Encoding.UTF8, leaveOpen: true);

                progress?.Report(0.01);

                byte[] pngData   = LoadPngBytes(br);
                long   extraStart = inFs.Position;

                progress?.Report(0.05);

                // トークン種別を判定（カーソルはextraStartに戻る）
                string? token = GuessToken(br);
                if (token == null)
                    throw new InvalidDataException("Koikatsuのファイル形式を認識できませんでした。");

                // 既に圧縮済みなら例外（出力ファイルを作成する前にチェック）
                inFs.Seek(extraStart, SeekOrigin.Begin);
                if (GuessCompressed(br))
                    throw new InvalidOperationException("このファイルは既に圧縮されています。");

                inFs.Seek(extraStart, SeekOrigin.Begin);

                using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var bw    = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true);

                // PNG を出力
                bw.Write(pngData);

                // 圧縮マーカー + トークンを出力（形式はトークン種別で異なる）
                if (token == KkToken.StudioToken)
                    bw.Write(new Version(101, 0, 0, 0).ToString());
                else
                    bw.Write(KkFormatMarker.Lzma);
                bw.Write(token);
                progress?.Report(ProgressAfterPng);

                // 残りデータ（元のマーカー + トークン + ゲームデータ）を読み取る
                byte[] extraData;
                using (var msExtra = new MemoryStream())
                {
                    inFs.CopyTo(msExtra);
                    extraData = msExtra.ToArray();
                }

                // LZMA 圧縮
                using var msCompressed = new MemoryStream();
                LzmaCompress(new MemoryStream(extraData), msCompressed, progress, ProgressAfterPng, ProgressAfterCompress);

                // 圧縮後の検証（compress → decompress → compare）
                ValidateCompressionRoundtrip(msCompressed, extraData);

                msCompressed.Seek(0, SeekOrigin.Begin);
                bw.Write(msCompressed.ToArray());
            } // streams flushed and closed here

            // Post-write verification: compressed output should be a valid PNG
            ValidatePngOutput(outputPath, "Compressed");

            progress?.Report(1.0);
        }

        /// <summary>
        /// 圧縮済み KK カードファイルを元に戻す。
        /// マーカー 101（LZMA）に対応。
        /// 出力形式: [PNGデータ] [元のマーカー100] [トークン] [ゲームデータ]
        /// </summary>
        public static void DecompressFile(string inputPath, string outputPath,
                                           IProgress<double>? progress = null)
        {
            byte[] pngData;
            // Phase 1: Read input and check compression BEFORE opening output stream
            using (var inFs  = new FileStream(inputPath,  FileMode.Open,   FileAccess.Read,  FileShare.ReadWrite))
            using (var br    = new BinaryReader(inFs,  Encoding.UTF8, leaveOpen: true))
            {
                progress?.Report(0.01);

                pngData = LoadPngBytes(br);
                progress?.Report(0.08);

                // マーカーを読み取る（int32 か Version 文字列か判定）
                long markerPos = inFs.Position;
                int peekMarker = br.ReadInt32();
                inFs.Seek(markerPos, SeekOrigin.Begin);

                if (peekMarker == KkFormatMarker.Raw || peekMarker == KkFormatMarker.Lzma)
                {
                    int marker = br.ReadInt32();
                    if (marker == KkFormatMarker.Raw)
                        throw new InvalidDataException("このファイルは圧縮されていません。");
                    if (marker != KkFormatMarker.Lzma)
                        throw new InvalidDataException("未知の圧縮マーカーです。");
                }
                else
                {
                    string versionStr = br.ReadString();
                    if (!Version.TryParse(versionStr, out var v))
                        throw new InvalidDataException("圧縮マーカーを認識できませんでした。");

                    if (v.Major == KkFormatMarker.Raw)
                        throw new InvalidDataException("このファイルは圧縮されていません。");
                    if (v.Major != KkFormatMarker.Lzma)
                        throw new InvalidDataException("未知の圧縮マーカーです。");
                }

                // トークンをスキップ
                br.ReadString();
                progress?.Report(ProgressAfterPng);

                // Phase 2: Now that compression is confirmed, open output stream
                using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var bw    = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true))
                {
                    // PNG を出力
                    bw.Write(pngData);
                    bw.Flush();

                    // LZMA 展開（LZMAデータの中には元のマーカー100+トークン+ゲームデータが含まれる）
                    LzmaDecompress(inFs, outFs, progress);
                }
            } // streams flushed and closed here

            // Post-decompression validation
            ValidatePngOutput(outputPath, "Decompressed");

            progress?.Report(1.0);
        }

        // ---------------------------------------------------------------
        // Post-output validation
        // ---------------------------------------------------------------

        /// <summary>
        /// 出力ファイルのPNGヘッダーを検証する。
        /// </summary>
        /// <param name="filePath">検証対象のファイルパス</param>
        /// <param name="label">エラーメッセージ用のラベル（例: "Decompressed", "Compressed"）</param>
        private static void ValidatePngOutput(string filePath, string label)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] header = new byte[PngSignature.Length];
                if (fs.Read(header, 0, PngSignature.Length) < PngSignature.Length)
                    throw new InvalidDataException($"{label} file is too short (missing PNG header).");
                for (int i = 0; i < PngSignature.Length; i++)
                {
                    if (header[i] != PngSignature[i])
                        throw new InvalidDataException($"{label} file has invalid PNG signature (corrupted or wrong format).");
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to validate {label.ToLowerInvariant()} file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// LZMA圧縮データを展開して元データと比較し、圧縮の正当性を検証する。
        /// 不一致の場合は InvalidDataException を投げる。
        /// </summary>
        private static void ValidateCompressionRoundtrip(MemoryStream compressed, byte[] originalData)
        {
            compressed.Seek(0, SeekOrigin.Begin);
            using var decompressed = new MemoryStream();
            LzmaDecompress(compressed, decompressed);
            decompressed.Seek(0, SeekOrigin.Begin);

            if (decompressed.Length != originalData.Length)
                throw new InvalidDataException("圧縮後の検証に失敗しました（サイズ不一致）。");

            if (!originalData.AsSpan().SequenceEqual(decompressed.ToArray().AsSpan()))
                throw new InvalidDataException("圧縮後の検証に失敗しました（データ不一致）。");

            compressed.Seek(0, SeekOrigin.Begin);
        }

        // ---------------------------------------------------------------
        // PNG helpers
        // ---------------------------------------------------------------

        internal static byte[] LoadPngBytes(BinaryReader br)
        {
            long start = br.BaseStream.Position;
            SkipPng(br);
            long end = br.BaseStream.Position;
            br.BaseStream.Seek(start, SeekOrigin.Begin);
            return br.ReadBytes((int)(end - start));
        }

        /// <summary>
        /// BinaryReader の現在位置から PNG チャンクを読み飛ばし、IEND までスキップする。
        /// 読み取り後、ストリーム位置は IEND チャンクの直後になる。
        /// </summary>
        public static void SkipPng(BinaryReader br)
        {
            br.ReadBytes(PngSignature.Length); // PNG magic
            while (true)
            {
                byte[] lenBytes = br.ReadBytes(PngChunkLengthBytes);
                int    length   = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
                byte[] type     = br.ReadBytes(4);
                br.ReadBytes(length); // chunk data
                br.ReadBytes(PngCrcBytes);      // CRC
                if (type[0] == 'I' && type[1] == 'E' && type[2] == 'N' && type[3] == 'D')
                    break;
            }
        }

        /// <summary>
        /// PNG 末尾直後から圧縮フラグを読む。読み位置は変化しない。
        /// </summary>
        internal static bool GuessCompressed(BinaryReader br)
        {
            long pos = br.BaseStream.Position;
            try
            {
                int r = br.ReadInt32();
                if (r == KkFormatMarker.Lzma) return true;
                if (r == KkFormatMarker.Raw) return false;

                // Studio: 先頭がバージョン文字列
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
                string st = br.ReadString();
                if (Version.TryParse(st, out var v))
                    return v.Major == KkFormatMarker.Lzma;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] GuessCompressed failed: {ex.Message}");
            }
            finally
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
            }
            return false;
        }

        /// <summary>
        /// PNG 末尾直後からトークン種別を判定する。読み位置は変化しない。
        /// </summary>
        internal static string? GuessToken(BinaryReader br)
        {
            long pos = br.BaseStream.Position;
            try
            {
                int r = br.ReadInt32();
                if (r != KkFormatMarker.Raw && r != KkFormatMarker.Lzma)
                {
                    // Studio: 先頭はバージョン文字列
                    return KkToken.StudioToken;
                }

                string token = br.ReadString();
                if (token.Contains(KkToken.CharaToken))
                    // Note: Assumes female (sex1); male cards (sex0) are not separately detected
                    return KkToken.CharaToken + "】sex1";
                if (token == KkToken.CoordinateToken)
                    return KkToken.CoordinateToken;

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] GuessToken failed: {ex.Message}");
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
                                         IProgress<double>? progress = null,
                                         double progressStart = 0.12, double progressEnd = 0.96)
        {
            var props = new object[]
            {
                LzmaDictionarySize,
                LzmaPosStateBits,
                LzmaLitContextBits,
                LzmaLitPosBits,
                LzmaNumFastBytes,
                LzmaMatchFinder,
                true,              // EndMarker
            };

            var encoder = new SevenZip.Compression.LZMA.Encoder();
            encoder.SetCoderProperties(s_propIDs, props);
            encoder.WriteCoderProperties(output);

            long remaining = input.Length - input.Position;
            for (int i = 0; i < 8; i++)
                output.WriteByte((byte)(remaining >> (8 * i)));

            ICodeProgress? codeProgress = null;
            if (progress != null)
                codeProgress = new LzmaCodeProgress(remaining, progress, progressStart, progressEnd);

            encoder.Code(input, output, -1, -1, codeProgress);
        }

        private static void LzmaDecompress(Stream input, Stream output,
                                           IProgress<double>? progress = null)
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
            ICodeProgress? codeProgress = null;
            if (progress != null)
                codeProgress = new LzmaCodeProgress(fileLength, progress, ProgressAfterPng, ProgressAfterValidate);

            decoder.Code(input, output, compressedSize, fileLength, codeProgress);
        }

        private sealed class LzmaCodeProgress : ICodeProgress
        {
            private readonly long _totalBytes;
            private readonly IProgress<double> _progress;
            private readonly double _start;
            private readonly double _end;

            public LzmaCodeProgress(long totalBytes, IProgress<double> progress, double start, double end)
            {
                _totalBytes = totalBytes <= 0 ? 1 : totalBytes;
                _progress = progress;
                _start = start;
                _end = end;
            }

            public void SetProgress(long inSize, long outSize)
            {
                if (inSize < 0) return;
                var ratio = (double)inSize / _totalBytes;
                if (ratio < 0) ratio = 0;
                if (ratio > 1) ratio = 1;
                var value = _start + ((_end - _start) * ratio);
                _progress.Report(value);
            }
        }
    }
}
