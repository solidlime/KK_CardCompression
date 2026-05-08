/*
 * KK_CardCompression - Harmony 2.x compatible build
 * Original by jim60105: https://github.com/jim60105/KK
 * Modified for Harmony 2.x / BepInEx 5.4.21+ compatibility
 */

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using KK_CardCompression.Extension;
using HarmonyLib;
using KK_CardCompression.SevenZip;
using Studio;
using UnityEngine;
using KK_CardCompression.PngCompression;

namespace KK_CardCompression
{
    [BepInProcess("CharaStudio")]
    [BepInProcess("Koikatu")]
    [BepInProcess("KoikatsuParty")]
    [BepInPlugin(GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PLUGIN_NAME = "KK Card Compression";
        internal const string GUID = "com.raurau.kk.cardcompression";
        internal const string PLUGIN_VERSION = "1.0.0";
        internal const string PLUGIN_RELEASE_VERSION = "1.0.0";
        public static ConfigEntry<bool> Enable { get; private set; }
        public static ConfigEntry<bool> DeleteOriginalFile { get; private set; }
        public static ConfigEntry<bool> DisplayMessage { get; private set; }
        public static ConfigEntry<bool> SkipSaveCheck { get; private set; }
        public static ConfigEntry<bool> EnableOnCharaSaving { get; private set; }
        public static ConfigEntry<bool> EnableOnCoordinateSaving { get; private set; }
        public static ConfigEntry<bool> EnableOnStudioSceneSaving { get; private set; }

        internal static new ManualLogSource Logger;
        internal static DirectoryInfo CacheDirectory;

        // Load embedded SevenZip.dll at startup (single-file DLL, no external deps)
        static Plugin()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name);
                if (name.Name != "SevenZip") return null;
                using (var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("KK_CardCompression.Embedded.SevenZip.dll"))
                {
                    if (stream == null) return null;
                    var data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);
                    return Assembly.Load(data);
                }
            };
        }

        public void Awake()
        {
            Logger = base.Logger;
            KK_CardCompression.Extension.Logger.logger = Logger;

            CleanCacheFolder();

            Enable = Config.Bind<bool>("Config", "Enable", true, "Enable compression on save. Requires restart or re-enable.");

            DeleteOriginalFile = Config.Bind<bool>("Settings", "Delete the original file", true, "The original saved file will be automatically overwritten.");
            DisplayMessage = Config.Bind<bool>("Settings", "Display compression message on screen", true);
            SkipSaveCheck = Config.Bind<bool>("Settings", "Skip bytes compare when saving", false, "!!!Use this at your own risk!!!!");
            EnableOnCharaSaving = Config.Bind<bool>("Enable at Where", "Character", true, "Enable compress when saving characters.");

            EnableOnCoordinateSaving = Config.Bind<bool>("Enable at Where", "Coordinate", true, "Enable compress when saving coordinates.");
            EnableOnStudioSceneSaving = Config.Bind<bool>("Enable at Where", "Studio Scene", true, "Enable compress when saving scenes.");

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
            if (style == null) return;
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
        private static ManualLogSource Logger = Plugin.Logger;

        #region Save
        //Studio Save
        [HarmonyPostfix, HarmonyPatch(typeof(SceneInfo), "Save", new Type[] { typeof(string) })]
        public static void SavePostfix(string _path)
        {
            if (Plugin.EnableOnStudioSceneSaving.Value)
                Save(_path, Token.StudioToken);
        }

        //Chara Save
        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileControl), "SaveCharaFile", new Type[] { typeof(string), typeof(byte), typeof(bool) })]
        public static void SaveCharaFilePostfix(ChaFileControl __instance, string filename, byte sex)
        {
            if(Plugin.EnableOnCharaSaving.Value)
                Save(__instance.ConvertCharaFilePath(filename, sex, true), Token.CharaToken + "】" + Token.SexToken + sex);
        }

        //Coordinate Save
        [HarmonyPostfix, HarmonyPatch(typeof(ChaFileCoordinate), "SaveFile", new Type[] { typeof(string) })]
        public static void SaveFilePostfix(string path)
        {
            if(Plugin.EnableOnCoordinateSaving.Value)
                Save(path, Token.CoordinateToken);
        }

        public static void Save(string path, string token)
        {
            Logger.LogDebug($"Save called: path={path}, token={token}");

            string cleanedPath = path;
            while (cleanedPath.Contains("_compressed"))
            {
                cleanedPath = cleanedPath.Replace("_compressed", "");
            }

            string compressedPath = cleanedPath;
            if (!Plugin.DeleteOriginalFile.Value)
            {
                compressedPath = cleanedPath.Substring(0, cleanedPath.Length - 4) + "_compressed.png";
            }

            Logger.LogDebug($"File exists: {File.Exists(cleanedPath)}");

            string decompressCacheDirName = Plugin.CacheDirectory.CreateSubdirectory("Decompressed").FullName;
            if (!Plugin.Enable.Value)
            {
                Logger.LogDebug("Save skipped: Plugin disabled (Enable=false)");
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

            byte[] pngData = ImageHelper.LoadPngBytes(path);

            Thread newThread = new Thread(() => ExecuteSaveThread(path, cleanedPath, compressedPath, pngData, token));
            newThread.Start();
        }

        private static void ExecuteSaveThread(string path, string cleanedPath, string compressedPath, byte[] pngData, string token)
        {
            Logger.LogInfo("Start Compress");
            long newSize = 0;
            long originalSize = 0;
            float startTime = Time.time;
            string tempPath = Path.Combine(Plugin.CacheDirectory.CreateSubdirectory("Compressed").FullName, Path.GetFileName(path));
            Logger.LogDebug($"Temp path: {tempPath}");
            Plugin.Progress = "";
            try
            {
                originalSize = new FileInfo(path).Length;
                Logger.LogDebug($"Original file size: {originalSize}");

                Logger.LogDebug($"PngCompression.Save starting: path={path}, temp={tempPath}, token={token}");
                newSize = new PngCompression.PngCompression().Save(
                    path,
                    tempPath,
                    token: token,
                    pngData: pngData,
                    compressProgress: (decimal progress) => Plugin.Progress = $"Compressing: {progress:p2}",
                    doCompare: !Plugin.SkipSaveCheck.Value,
                    compareProgress: (decimal progress) => Plugin.Progress = $"Comparing: {progress:p2}");
                Logger.LogDebug($"PngCompression.Save completed: newSize={newSize}");

                if (newSize > 0)
                {
                    LogLevel logLevel = Plugin.DisplayMessage.Value ? (LogLevel.Message | LogLevel.Info) : LogLevel.Info;
                    Logger.LogInfo($"Compression test SUCCESS");
                    Logger.Log(logLevel, $"Compression finish in {Time.time - startTime:n2} seconds");
                    Logger.Log(logLevel, $"Size compress from {originalSize} bytes to {newSize} bytes");
                    Logger.Log(logLevel, $"Compress ratio: {Convert.ToDecimal(originalSize) / newSize:n3}/1, which means it is now {Convert.ToDecimal(newSize) / originalSize:p3} big.");

                    File.Copy(tempPath, compressedPath, true);
                    Logger.LogDebug($"Write to: {compressedPath}");

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
                Logger.LogError($"ExecuteSaveThread exception: {e.GetType().Name} - {e.Message}");
                Logger.LogDebug($"ExecuteSaveThread stack trace: {e.StackTrace}");

                if (e is IOException && newSize > 0)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            if (Plugin.DeleteOriginalFile.Value)
                            {
                                File.Copy(tempPath, path, true);
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        Logger.LogError($"Fallback copy also failed: {ex2.GetType().Name} - {ex2.Message}");
                        if (path.Length < 5)
                        {
                            Logger.LogError("Path too short: " + path);
                        }
                        else
                        {
                            File.Copy(tempPath, path.Substring(0, path.Length - 4) + "_compressed2.png");
                            Logger.LogError("Overwrite was FAILED twice. Fallback to use the '_compressed2' path.");
                        }
                    }
                }
                else
                {
                    Logger.Log(LogLevel.Error | LogLevel.Message, $"An unknown error occurred. If your files are lost, please find them at %TEMP%/{Plugin.GUID}");
                    throw;
                }
            }
            finally
            {
                Plugin.Progress = "";
                if (File.Exists(tempPath)) File.Delete(tempPath);
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
            filename = __instance.ConvertCharaFilePath(filename, sex, false);
            Load(ref filename, Token.CharaToken);
        }

        //Coordinate Load
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(ChaFileCoordinate), "LoadFile", new Type[] { typeof(string) })]
        public static void LoadFilePrefix(ChaFileCoordinate __instance, ref string path)
            => Load(ref path, Token.CoordinateToken);

        public static void Load(ref string path, string token)
        {
            Logger.LogDebug($"Load called: path={path}, token={token}");
            Logger.LogDebug($"Source file exists: {File.Exists(path)}");

            if (!Plugin.Enable.Value)
            {
                Logger.LogDebug("Load skipped: Plugin disabled (Enable=false)");
                return;
            }

            string decompressCacheDirName = Plugin.CacheDirectory.CreateSubdirectory("Decompressed").FullName;
            string result = TryDecompressFile(path, decompressCacheDirName, token);
            if (result != null)
            {
                path = result;
            }
        }

        private static string TryDecompressFile(string loadPath, string decompressCacheDirName, string token)
        {
            string fileName = Path.GetFileName(loadPath);
            string tmpPath = Path.Combine(decompressCacheDirName, fileName);
            if (File.Exists(tmpPath))
            {
                Logger.LogDebug("Load from cache: " + tmpPath);
                return tmpPath;
            }
            float startTime = Time.time;

            Plugin.Progress = "";
            try
            {
                long loadResult = new PngCompression.PngCompression().Load(loadPath, tmpPath, token,
                    (decimal progress) => Plugin.Progress = $"Decompressing: {progress:p2}");
                Logger.LogDebug($"PngCompression.Load result: {loadResult}");

                if (0 != loadResult)
                {
                    long decompressedSize = new FileInfo(tmpPath).Length;
                    if (Time.time - startTime == 0)
                    {
                        Logger.LogDebug($"Decompressed: {fileName}, size={decompressedSize}");
                    }
                    else
                    {
                        Logger.LogDebug($"Decompressed: {fileName}, size={decompressedSize}, finish in {Time.time - startTime} seconds");
                    }
                    return tmpPath;
                }
                else
                {
                    File.Delete(tmpPath);
                }
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error | LogLevel.Message, $"Decompressed failed: {fileName}");
                Logger.LogError($"Load exception: {e.GetType().Name} - {e.Message}");
                Logger.LogDebug($"Load stack trace: {e.StackTrace}");
                File.Delete(tmpPath);
            }
            finally
            {
                Plugin.Progress = "";
            }
            return null;
        }
        #endregion
    }
}
