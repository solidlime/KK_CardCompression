using System;
using System.IO;
using System.Reflection;
using ZstdSharp;

namespace KK_CardCompression.Services
{
    /// <summary>
    /// Zstd 圧縮レベル。
    /// </summary>
    public enum ZstdLevel
    {
        Fast = 3,
        Default = 6,
        Better = 14,
        Best = 19,
        Ultra = 22,
    }

    /// <summary>
    /// KK カードファイルのフォーマットマーカー。
    /// 100 = 未圧縮, 101 = LZMA圧縮, 102 = Zstd圧縮(辞書なし), 103 = Zstd圧縮(辞書あり)
    /// </summary>
    public static class KkFormatMarker
    {
        public const int Raw = 100;
        public const int Lzma = 101;
        public const int ZstdNoDict = 102;
        public const int ZstdWithDict = 103;
    }

    /// <summary>
    /// Zstd 圧縮・解凍サービス。辞書あり/なし両対応。
    /// </summary>
    public static class ZstdCompressionService
    {
        private static byte[]? s_dictionary;

        /// <summary>
        /// 埋め込み辞書を取得する。初回呼び出し時にリソースから読み込む。
        /// </summary>
        public static byte[] Dictionary
        {
            get => s_dictionary ??= LoadEmbeddedDictionary();
            set => s_dictionary = value;
        }

        private static byte[] LoadEmbeddedDictionary()
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("kk_universal_dict.zstd", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = assembly.GetManifestResourceStream(name)!;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }

            // 辞書リソースが見つからない場合は空配列を返す（辞書なしモードで動作）
            return Array.Empty<byte>();
        }

        /// <summary>
        /// Zstd 圧縮（辞書なし）。
        /// </summary>
        public static byte[] Compress(byte[] data, ZstdLevel level = ZstdLevel.Better)
        {
            using var compressor = new Compressor((int)level);
            return compressor.Wrap(data).ToArray();
        }

        /// <summary>
        /// Zstd 圧縮（辞書あり）。
        /// </summary>
        public static byte[] CompressWithDictionary(byte[] data, ZstdLevel level = ZstdLevel.Better)
        {
            var dict = Dictionary;
            using var compressor = new Compressor((int)level);
            compressor.LoadDictionary(dict);
            return compressor.Wrap(data).ToArray();
        }

        /// <summary>
        /// Zstd 解凍。辞書の要否は圧縮時のマーカーで判定する。
        /// </summary>
        /// <param name="data">Zstd 圧縮済みデータ</param>
        /// <param name="useDictionary">辞書を使用するか（マーカー103の場合true）</param>
        public static byte[] Decompress(byte[] data, bool useDictionary)
        {
            if (useDictionary)
            {
                var dict = Dictionary;
                using var decompressor = new Decompressor();
                decompressor.LoadDictionary(dict);
                return decompressor.Unwrap(data).ToArray();
            }
            else
            {
                using var decompressor = new Decompressor();
                return decompressor.Unwrap(data).ToArray();
            }
        }

        /// <summary>
        /// ストリームから全バイトを読み取る。
        /// </summary>
        internal static byte[] ReadAllBytes(Stream stream)
        {
            if (stream is MemoryStream ms)
            {
                // MemoryStream.ToArray() は Position に関係なく全バッファを返すので注意
                // Position が 0 でない場合、GetBuffer() + Length を使う
                if (ms.TryGetBuffer(out var segment))
                    return segment.Array.AsSpan(0, (int)ms.Length).ToArray();
                return ms.ToArray();
            }

            long originalPosition = stream.Position;
            if (stream.CanSeek)
                stream.Position = 0;

            var buffer = new byte[stream.Length];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            if (stream.CanSeek)
                stream.Position = originalPosition;

            return buffer;
        }
    }
}