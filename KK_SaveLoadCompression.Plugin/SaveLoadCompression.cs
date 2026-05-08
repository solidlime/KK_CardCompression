/*
 * KK_SaveLoadCompression - Harmony 2.x compatible build
 * Original by jim60105: https://github.com/jim60105/KK
 * Modified for Harmony 2.x / BepInEx 5.4.21+ compatibility
 */

using System;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Extension;
using HarmonyLib;
using SevenZip;
using Studio;
using UnityEngine;
using PngCompression;

namespace SaveLoadCompression
{
    [BepInProcess("CharaStudio")]
    [BepInProcess("Koikatu")]
    [BepInPlugin(GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SaveLoadCompression : BaseUnityPlugin
    {
        internal const string PLUGIN_NAME = "Save Load Compression";
        internal const string GUID = "com.jim60105.kk.saveloadcompression";
        internal const string PLUGIN_VERSION = "21.12.23.0";
        internal const string PLUGIN_RELEASE_VERSION = "1.4.0";
        public static ConfigEntry<DictionarySize> DictionarySize { get; private set; }
        public static ConfigEntry<bool> Enable { get; private set; }
        public static ConfigEntry<bool> Notice { get; private set; }
        public static ConfigEntry<bool> DeleteTheOri { get; private set; }
        public static ConfigEntry<bool> DisplayMessage { get; private set; }
        public static ConfigEntry<bool> SkipSaveCheck { get; private set; }
        public static ConfigEntry<bool> EnableOnCharaSaveing { get; private set; }
        public static ConfigEntry<bool> EnableOnCoordinateSaveing { get; private set; }
        public static ConfigEntry<bool> EnableOnStudioSceneSaveing { get; private set; }

        internal static new ManualLogSource Logger;
        internal static DirectoryInfo CacheDirectory;
        public void Awake()
        {
            Logger = base.Logger;
            Extension.Logger.logger = Logger;

            CleanCacheFolder();

            Enable = Config.Bind<bool>("Config", "Enable", false, "!!!NOTICE!!!");
            Notice = Config.Bind<bool>("Config", "I do realize that without this plugin, the save files will not be readable!!", false, "!!!NOTICE!!!");

            DeleteTheOri = Config.Bind<bool>("Settings", "Delete the original file", true, "The original saved file will be automatically overwritten.");
            DisplayMessage = Config.Bind<bool>("Settings", "Display compression message on screen", false);
            SkipSaveCheck = Config.Bind<bool>("Settings", "Skip bytes compare when saving", false, "!!!Use this at your own risk!!!!");

            EnableOnCharaSaveing = Config.Bind<bool>("Enable at Where", "Character", false, "Enable compress when saving characters.");
            EnableOnCoordinateSaveing = Config.Bind<bool>("Enable at Where", "Coordinate", false, "Enable compress when saving coordinates.");
            EnableOnStudioSceneSaveing = Config.Bind<bool>("Enable at Where", "Studio Scene", true, "Enable compress when saving scenes.");

            // Harmony 2.x compatible: use named parameters for Patch()
            Harmony harmonyInstance = Harmony.CreateAndPatchAll(typeof(Patches));
            harmonyInstance.Patch(
                original: typeof(SceneInfo).GetMethod(nameof(SceneInfo.Load), new[] { typeof(string), typeof(Version).MakeByRefType() }),
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.LoadPrefix))
            );
        }

        internal static string Progress = "";
        void OnGUI()
        {
            if (Progress.Length == 0) return;
            float margin = 20f;
            GUIStyle style = GUI.skin.GetStyle("box");
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 25;
            GUIContent content = new GUIContent(Progress, "Please wait until compression finish.");
            Vector2 v2 = style.CalcSize(content);
            GUI.Box(
                new Rect(
                    Screen.width - v2.x - margin,
                    Screen.height - v2.y - margin,
                    v2.x,
                    v2.y
                ),
                content,
                style
            );
        }

        void OnApplicationQuit() => CleanCacheFolder();
        void OnDestroy() => CleanCacheFolder();

        private void CleanCacheFolder()
        {
            CacheDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), GUID));
            foreach (FileInfo file in CacheDirectory.GetFiles()) file.Delete();
            foreach (DirectoryInfo subDirectory in CacheDirectory.GetDirectories()) subDirectory.Delete(true);
            Logger.LogDebug("Clean cache folder");
        }
    }

    class Patches
    {
        private static ManualLogSource Logger = SaveLoadCompression.Logger;

        #region Save
        //Studio Save
        [HarmonyPostfix, HarmonyPatch(typeof(SceneInfo), "Save", new Type[] { typeof(string) })]
        public static void SavePostfix(string _path)
        {
            if (SaveLoadCompression.EnableOnStudioSceneSaveing.Value)
                Save(_path, Token.StudioToken);
        }

        //Chara Save
        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileControl), "SaveCharaFile", new Type[] { typeof(string), typeof(byte), typeof(bool) })]
        public static void SaveCharaFilePostfix(ChaFileControl __instance, string filename, byte sex)
        {
            if(SaveLoadCompression.EnableOnCharaSaveing.Value)
                Save(filename, Token.CharaToken + "】" + Token.SexToken + sex);
        }

        //Coordinate Save
        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileCoordinate), "SaveFile", new Type[] { typeof(string) })]
        public static void SaveFilePostfix(string path)
        {
            if(SaveLoadCompression.EnableOnCoordinateSaveing.Value)
                Save(path, Token.CoordinateToken);
        }

        public static void Save(string path, string token)
        {
            string cleanedPath = path;
            while (cleanedPath.Contains("_compressed"))
            {
                cleanedPath = cleanedPath.Replace("_compressed", "");
            }

            string compressedPath = cleanedPath;
            if (!SaveLoadCompression.DeleteTheOri.Value)
            {
                compressedPath = cleanedPath.Substring(0, cleanedPath.Length - 4) + "_compressed.png";
            }

            string decompressCacheDirName = SaveLoadCompression.CacheDirectory.CreateSubdirectory("Decompressed").FullName;
            if (!SaveLoadCompression.Enable.Value || !SaveLoadCompression.Notice.Value)
            {
                File.Delete(Path.Combine(decompressCacheDirName, Path.GetFileName(path)));
                File.Delete(Path.Combine(decompressCacheDirName, Path.GetFileName(cleanedPath)));
                File.Delete(Path.Combine(decompressCacheDirName, Path.GetFileName(compressedPath)));
                return;
            }
            File.Copy(path, Path.Combine(decompressCacheDirName, Path.GetFileName(compressedPath)), true);

            if (cleanedPath != path)
            {
                File.Copy(path, cleanedPath, true);
                Logger.LogDebug($"Clean Path: {cleanedPath}");
            }

            byte[] pngData;
            byte[] unzipPngData;
            try
            {
                pngData = MakeWatermarkPic(ImageHelper.LoadPngBytes(path), token, true);
                unzipPngData = MakeWatermarkPic(ImageHelper.LoadPngBytes(path), token, false);
            }
            catch (TypeLoadException)
            {
                Logger.LogWarning("Unity types unavailable, skipping watermark");
                byte[] rawPng = ImageHelper.LoadPngBytes(path);
                pngData = rawPng;
                unzipPngData = rawPng;
            }

            Thread newThread = new Thread(saveThread);
            newThread.Start();

            void saveThread()
            {
                Logger.LogInfo("Start Compress");
                long newSize = 0;
                long originalSize = 0;
                float startTime = Time.time;
                string TempPath = Path.Combine(SaveLoadCompression.CacheDirectory.CreateSubdirectory("Compressed").FullName, Path.GetFileName(path));
                SaveLoadCompression.Progress = "";
                try
                {
                    originalSize = new FileInfo(path).Length;

                    newSize = new PngCompression.PngCompression().Save(
                        path,
                        TempPath,
                        token: token,
                        pngData: pngData,
                        compressProgress: (decimal progress) => SaveLoadCompression.Progress = $"Compressing: {progress:p2}",
                        doComapre: !SaveLoadCompression.SkipSaveCheck.Value,
                        compareProgress: (decimal progress) => SaveLoadCompression.Progress = $"Comparing: {progress:p2}");

                    if (newSize > 0)
                    {
                        LogLevel logLevel = SaveLoadCompression.DisplayMessage.Value ? (LogLevel.Message | LogLevel.Info) : LogLevel.Info;
                        Logger.LogInfo($"Compression test SUCCESS");
                        Logger.Log(logLevel, $"Compression finish in {Time.time - startTime:n2} seconds");
                        Logger.Log(logLevel, $"Size compress from {originalSize} bytes to {newSize} bytes");
                        Logger.Log(logLevel, $"Compress ratio: {Convert.ToDecimal(originalSize) / newSize:n3}/1, which means it is now {Convert.ToDecimal(newSize) / originalSize:p3} big.");

                        File.Copy(TempPath, compressedPath, true);
                        Logger.LogDebug($"Write to: {compressedPath}");

                        if (cleanedPath != compressedPath)
                        {
                            ChangePNG(cleanedPath, unzipPngData);
                            Logger.LogDebug($"Overwrite unzip watermark: {cleanedPath}");
                        }

                        if (path != compressedPath && path != cleanedPath)
                        {
                            File.Delete(path);
                            Logger.LogDebug($"Delete Original File: {path}");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Compression FAILED");
                    }
                }
                catch (Exception e)
                {
                    if (e is IOException && newSize > 0)
                    {
                        try
                        {
                            if (File.Exists(TempPath))
                            {
                                if (SaveLoadCompression.DeleteTheOri.Value)
                                {
                                    File.Copy(TempPath, path, true);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            File.Copy(TempPath, path.Substring(0, path.Length - 4) + "_compressed2.png");
                            Logger.LogError("Overwrite was FAILED twice. Fallback to use the '_compressed2' path.");
                        }
                    }
                    else
                    {
                        Logger.Log(LogLevel.Error | LogLevel.Message, $"An unknown error occurred. If your files are lost, please find them at %TEMP%/{SaveLoadCompression.GUID}");
                        throw;
                    }
                }
                finally
                {
                    SaveLoadCompression.Progress = "";
                    if (File.Exists(TempPath)) File.Delete(TempPath);
                }
            }
        }
        #endregion

        #region Load
        //Studio Load
        [HarmonyPriority(Priority.First)]
        public static void LoadPrefix(ref string _path)
            => Load(ref _path, Token.StudioToken);

        //Studio Import
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(SceneInfo), "Import", new Type[] { typeof(string) })]
        public static void ImportPrefix(ref string _path)
            => Load(ref _path, Token.StudioToken);

        //Chara Load
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(ChaFile), "LoadFile", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
        public static void LoadFilePrefix(ref string path)
            => Load(ref path, Token.CharaToken);

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(ChaFileControl), "LoadCharaFile", new Type[] { typeof(string), typeof(byte), typeof(bool), typeof(bool) })]
        public static void LoadCharaFilePrefix(ChaFileControl __instance, ref string filename, byte sex)
        {
            filename = __instance.ConvertCharaFilePath(filename, sex, true);
            Load(ref filename, Token.CharaToken);
        }

        //Coordinate Load
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(ChaFileCoordinate), "LoadFile", new Type[] { typeof(string) })]
        public static void LoadFilePrefix(ChaFileCoordinate __instance, ref string path)
            => Load(ref path, Token.CoordinateToken);

        public static void Load(ref string path, string token)
        {
            string fileName = Path.GetFileName(path);
            string tmpPath = Path.Combine(SaveLoadCompression.CacheDirectory.CreateSubdirectory("Decompressed").FullName, fileName);
            if (File.Exists(tmpPath))
            {
                path = tmpPath;
                Logger.LogDebug("Load from cache: " + path);
                return;
            }
            float startTime = Time.time;

            SaveLoadCompression.Progress = "";
            try
            {
                if (0 != new PngCompression.PngCompression().Load(path,
                                                        tmpPath,
                                                        token,
                                                        (decimal progress) => SaveLoadCompression.Progress = $"Decompressing: {progress:p2}"))
                {
                    path = tmpPath;
                    if (Time.time - startTime == 0)
                    {
                        Logger.LogDebug($"Decompressed: {fileName}");
                    }
                    else
                    {
                        Logger.LogDebug($"Decompressed: {fileName}, finish in {Time.time - startTime} seconds");
                    }
                }
                else
                {
                    File.Delete(tmpPath);
                }
            }
            catch (Exception)
            {
                Logger.Log(LogLevel.Error | LogLevel.Message, $"Decompressed failed: {fileName}");
                File.Delete(tmpPath);
                return;
            }
            finally
            {
                SaveLoadCompression.Progress = "";
            }
        }
        #endregion

        internal static byte[] MakeWatermarkPic(byte[] pngData, string token, bool zip)
        {
            Texture2D png = new Texture2D(1, 1);
            png.LoadImage(pngData);

            Texture2D watermark;
            if (zip)
            {
                watermark = UnityImageHelper.LoadDllResourceToTexture2D($"SaveLoadCompression.Resources.zip_watermark.png");
            }
            else
            {
                watermark = UnityImageHelper.LoadDllResourceToTexture2D($"SaveLoadCompression.Resources.unzip_watermark.png");
            }
            float scaleTimes = new PngCompression.PngCompression().GetScaleTimes(token);
            watermark = watermark.Scale(Convert.ToInt32(png.width * scaleTimes));
            png = png.OverwriteTexture(
                watermark,
                0,
                png.height - watermark.height
            );
            Extension.Logger.LogDebug($"Add Watermark: zip");
            return png.EncodeToPNG();
        }

        private static void ChangePNG(string path, byte[] pngData)
        {
            byte[] data;
            using (FileStream fileStreamReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                ImageHelper.SkipPng(fileStreamReader);
                data = ImageHelper.ReadToEnd(fileStreamReader);
            }
            string tmpPath = Path.GetTempFileName();
            using (FileStream fileStreamWriter = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter binaryWriter = new BinaryWriter(fileStreamWriter))
            {
                binaryWriter.Write(pngData);
                binaryWriter.Write(data);
            }
            File.Copy(tmpPath, path, true);
        }
    }
}
