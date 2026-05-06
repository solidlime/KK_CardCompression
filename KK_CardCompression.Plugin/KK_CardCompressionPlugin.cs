using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Studio;
using ZstdSharp;

namespace KK_CardCompression
{
    /// <summary>
    /// BepInEx 5.4.23.3 プラグイン。
    /// Koikatsu が Zstd 圧縮ファイル（マーカー 102/103）を読み込む際に
    /// 自動的に解凍し、ゲームに透過的にデータを渡す。
    /// 
    /// KK_SaveLoadCompression.dll（LZMA マーカー 101）と共存可能。
    /// 解凍後のファイルは vanilla Koikatsu でそのまま読み込み可能。
    /// </summary>
    [BepInProcess("CharaStudio")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class KK_CardCompressionPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.kkcardcompression.zstd";
        public const string PLUGIN_NAME = "KK Card Compression (Zstd)";
        public const string PLUGIN_VERSION = "1.3.0";

        internal static ManualLogSource Log;
        internal static DirectoryInfo CacheDirectory;

        private void Awake()
        {
            Log = base.Logger;
            Log.LogInfo($"KK_CardCompression v{PLUGIN_VERSION} initializing...");

            // キャッシュディレクトリ初期化
            CacheDirectory = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), PLUGIN_GUID));
            CleanCacheFolder();

            // Zstd 辞書を事前ロード
            try
            {
                var dict = ZstdDecompressorHelper.LoadDictionary();
                Log.LogInfo($"Zstd dictionary loaded: {dict.Length} bytes");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to preload Zstd dictionary: {ex.Message}");
            }

            var harmony = new Harmony(PLUGIN_GUID);

            // Load patches — ゲームがファイルを読み込む前に解凍
            harmony.Patch(
                typeof(ChaFile).GetMethod("CheckData", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.CheckDataPrefix)));

            harmony.Patch(
                typeof(ChaFile).GetMethod("LoadFile", new[] { typeof(string), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadFilePrefix)));

            harmony.Patch(
                typeof(ChaFileControl).GetMethod("LoadCharaFile",
                    new[] { typeof(string), typeof(byte), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadCharaFilePrefix)));

            harmony.Patch(
                typeof(ChaFileControl).GetMethod("LoadCharaFileKoikatsu",
                    new[] { typeof(string), typeof(byte), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadCharaFileKoikatsuPrefix)));

            harmony.Patch(
                typeof(ChaFileCoordinate).GetMethod("LoadFile", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadCoordinateFilePrefix)));

            harmony.Patch(
                typeof(SceneInfo).GetMethod("Load", new[] { typeof(string), typeof(Version).MakeByRefType() }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadScenePrefix)));

            harmony.Patch(
                typeof(SceneInfo).GetMethod("Import", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.ImportScenePrefix)));

            Log.LogInfo($"KK_CardCompression v{PLUGIN_VERSION} loaded. Zstd decompression enabled.");
        }

        private void OnApplicationQuit() => CleanCacheFolder();
        private void OnDestroy() => CleanCacheFolder();

        private void CleanCacheFolder()
        {
            try
            {
                foreach (var file in CacheDirectory.GetFiles()) file.Delete();
                foreach (var dir in CacheDirectory.GetDirectories()) dir.Delete(true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Zstd 解凍ヘルパー。
    /// </summary>
    public static class ZstdDecompressorHelper
    {
        private static byte[] s_dictionary;
        private static readonly object s_lock = new object();

        public static byte[] LoadDictionary()
        {
            lock (s_lock)
            {
                if (s_dictionary != null && s_dictionary.Length > 0) return s_dictionary;

                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("kk_universal_dict.zstd", StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = assembly.GetManifestResourceStream(name);
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        s_dictionary = ms.ToArray();
                        return s_dictionary;
                    }
                }

                var pluginDir = Path.GetDirectoryName(assembly.Location);
                var dictPath = Path.Combine(pluginDir, "kk_universal_dict.zstd");
                if (File.Exists(dictPath))
                {
                    s_dictionary = File.ReadAllBytes(dictPath);
                    return s_dictionary;
                }

                KK_CardCompressionPlugin.Log.LogWarning("Zstd dictionary not found");
                s_dictionary = Array.Empty<byte>();
                return s_dictionary;
            }
        }

        public static byte[] Decompress(byte[] data)
        {
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(data).ToArray();
        }

        public static byte[] DecompressWithDictionary(byte[] data)
        {
            var dict = LoadDictionary();
            using var decompressor = new Decompressor();
            decompressor.LoadDictionary(dict);
            return decompressor.Unwrap(data).ToArray();
        }
    }

    /// <summary>
    /// フォーマットマーカー定数。
    /// </summary>
    public static class KkFormatMarker
    {
        public const int Raw = 100;
        public const int Lzma = 101;
        public const int ZstdNoDict = 102;
        public const int ZstdWithDict = 103;
    }

    /// <summary>
    /// Harmony パッチ。
    /// ゲームがファイルを読み込む際に Zstd 圧縮データを透過的に解凍する。
    /// 
    /// 圧縮ファイルの構造:
    ///   [PNG] [圧縮マーカー(102/103)] [トークン] [圧縮(元マーカー100 + トークン + ゲームデータ)]
    /// 
    /// 解凍後の構造:
    ///   [PNG] [元マーカー100] [トークン] [ゲームデータ]
    /// 
    /// 解凍データには既にマーカー・トークン・ゲームデータが含まれるため、
    /// PNG の後に解凍データをそのまま結合すればよい。
    /// </summary>
    public static class Patches
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// PNG 終端（IEND）までスキップし、PNG以降のデータ開始位置を返す。
        /// </summary>
        private static int SkipPng(byte[] data)
        {
            int pos = 8;
            while (pos + 12 <= data.Length)
            {
                int chunkLen = (data[pos] << 24) | (data[pos + 1] << 16)
                             | (data[pos + 2] << 8) | data[pos + 3];
                pos += 4;
                bool isIend = data[pos] == 73 && data[pos + 1] == 69
                            && data[pos + 2] == 78 && data[pos + 3] == 68;
                pos += 4;
                if (chunkLen < 0 || pos + chunkLen + 4 > data.Length) return -1;
                pos += chunkLen + 4;
                if (isIend) return pos;
            }
            return -1;
        }

        /// <summary>
        /// BinaryReader 形式の文字列を読み取る。
        /// BinaryWriter.Write(string) と互換: 7bit エンコード長 + UTF-8 バイト列。
        /// </summary>
        private static string ReadBinaryString(byte[] data, int offset, out int endOffset)
        {
            int pos = offset;
            int strlen = data[pos] & 0x7f;
            pos++;
            if (data[offset] >= 0x80)
            {
                strlen |= (data[pos] & 0x7f) << 7;
                pos++;
            }
            endOffset = pos + strlen;
            return Encoding.UTF8.GetString(data, pos, strlen);
        }

        /// <summary>
        /// ファイルが Zstd 圧縮なら解凍し、一時ファイルパスを返す。
        /// 未圧縮または LZMA 圧縮の場合は null を返す。
        /// </summary>
        private static string TryDecompress(string path)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(path);

                // PNG シグネチャチェック
                if (fileData.Length < 8) return null;
                for (int i = 0; i < 8; i++)
                {
                    if (fileData[i] != PngSignature[i]) return null;
                }

                int pngEnd = SkipPng(fileData);
                if (pngEnd < 0 || pngEnd >= fileData.Length) return null;

                // マーカー判定
                int firstInt = BitConverter.ToInt32(fileData, pngEnd);
                int marker;
                bool isStudio;

                if (firstInt == KkFormatMarker.Raw || firstInt == KkFormatMarker.Lzma
                    || firstInt == KkFormatMarker.ZstdNoDict || firstInt == KkFormatMarker.ZstdWithDict)
                {
                    marker = firstInt;
                    isStudio = false;
                }
                else
                {
                    try
                    {
                        string versionStr = ReadBinaryString(fileData, pngEnd, out _);
                        if (System.Version.TryParse(versionStr, out var v))
                            marker = v.Major;
                        else
                            return null;
                    }
                    catch { return null; }
                    isStudio = true;
                }

                // LZMA と未圧縮はスキップ（KK_SaveLoadCompression に任せる）
                if (marker == KkFormatMarker.Raw || marker == KkFormatMarker.Lzma)
                    return null;

                // Zstd のみ処理
                if (marker != KkFormatMarker.ZstdNoDict && marker != KkFormatMarker.ZstdWithDict)
                    return null;

                // マーカーとトークンを読み飛ばして圧縮データの開始位置を特定
                int pos;
                if (!isStudio)
                {
                    // キャラ/衣装: int32 マーカー + トークン文字列
                    pos = pngEnd + 4;
                    ReadBinaryString(fileData, pos, out pos); // トークンをスキップ
                }
                else
                {
                    // スタジオ: バージョン文字列 + トークン文字列
                    ReadBinaryString(fileData, pngEnd, out pos); // バージョンをスキップ
                    ReadBinaryString(fileData, pos, out pos);     // トークンをスキップ
                }

                // 圧縮データを解凍
                // 解凍データ = [元マーカー100] [トークン] [ゲームデータ]
                byte[] compressedData = new byte[fileData.Length - pos];
                Array.Copy(fileData, pos, compressedData, 0, compressedData.Length);

                byte[] decompressedData;
                if (marker == KkFormatMarker.ZstdWithDict)
                    decompressedData = ZstdDecompressorHelper.DecompressWithDictionary(compressedData);
                else
                    decompressedData = ZstdDecompressorHelper.Decompress(compressedData);

                // 元の形式に復元: [PNG] + [解凍データ(マーカー100 + トークン + ゲームデータ)]
                byte[] result = new byte[pngEnd + decompressedData.Length];
                Array.Copy(fileData, result, pngEnd);                    // PNG 部分
                Array.Copy(decompressedData, 0, result, pngEnd, decompressedData.Length); // 解凍データ

                // 一時ファイルに書き出し
                string tmpPath = Path.Combine(
                    KK_CardCompressionPlugin.CacheDirectory.FullName,
                    Path.GetFileName(path));

                File.WriteAllBytes(tmpPath, result);

                KK_CardCompressionPlugin.Log.LogInfo(
                    $"Decompressed Zstd{(marker == KkFormatMarker.ZstdWithDict ? "+dict" : "")}: " +
                    $"{fileData.Length} -> {result.Length} bytes");

                return tmpPath;
            }
            catch (Exception ex)
            {
                KK_CardCompressionPlugin.Log.LogWarning($"Zstd decompression failed for {path}: {ex.Message}");
                return null;
            }
        }

        #region Load Patches

        // CharaFile.CheckData
        public static void CheckDataPrefix(ref string path)
            => Load(ref path);

        // CharaFile.LoadFile
        public static void LoadFilePrefix(ref string path)
            => Load(ref path);

        // ChaFileControl.LoadCharaFile
        public static void LoadCharaFilePrefix(ChaFileControl __instance, ref string filename, byte sex)
        {
            filename = __instance.ConvertCharaFilePath(filename, sex);
            Load(ref filename);
        }

        // ChaFileControl.LoadCharaFileKoikatsu
        public static void LoadCharaFileKoikatsuPrefix(ref string filename)
            => Load(ref filename);

        // ChaFileCoordinate.LoadFile
        public static void LoadCoordinateFilePrefix(ref string path)
            => Load(ref path);

        // SceneInfo.Load
        public static void LoadScenePrefix(ref string _path)
            => Load(ref _path);

        // SceneInfo.Import
        public static void ImportScenePrefix(ref string _path)
            => Load(ref _path);

        public static void Load(ref string path)
        {
            string fileName = Path.GetFileName(path);
            string tmpPath = Path.Combine(
                KK_CardCompressionPlugin.CacheDirectory.FullName, fileName);

            // キャッシュチェック
            if (File.Exists(tmpPath))
            {
                path = tmpPath;
                KK_CardCompressionPlugin.Log.LogDebug($"Load from cache: {path}");
                return;
            }

            try
            {
                string result = TryDecompress(path);
                if (result != null)
                {
                    path = result;
                }
            }
            catch (Exception ex)
            {
                KK_CardCompressionPlugin.Log.LogError($"Decompression failed: {fileName}: {ex.Message}");
            }
        }

        #endregion
    }
}