using KK_CardCompression.Extension;
using KK_CardCompression.SevenZip;
using System;
using System.IO;
using System.IO.Compression;
namespace KK_CardCompression.PngCompression
{
    public delegate void ProgressCallback(decimal progress);
    public delegate void LongProgressCallback(long current, long total);

    public struct Token
    {
        public const string StudioToken = "【KStudio】";
        public const string CharaToken = "【KoiKatuChara";
        public const string SexToken = "sex";
        public const string CoordinateToken = "【KoiKatuClothes】";
    }

    public class PngCompression
    {
        public float GetScaleTimes(string token) => (token == Token.StudioToken) ? .14375f : .30423f;

        public long Save(string inputPath, string outputPath, string token = null, byte[] pngData = null, ProgressCallback compressProgress = null, bool doCompare = true, ProgressCallback compareProgress = null)
        {
            using (FileStream fileStreamReader = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream fileStreamWriter = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                return Save(fileStreamReader, fileStreamWriter, token: token, pngData: pngData, compressProgress: compressProgress, doCompare: doCompare, compareProgress: compareProgress);
            }
        }

        public long Save(Stream inputStream, Stream outputStream, string token = null, byte[] pngData = null, ProgressCallback compressProgress = null, bool doCompare = true, ProgressCallback compareProgress = null)
        {
            long dataSize = 0;

            using (BinaryReader binaryReader = new BinaryReader(inputStream))
            using (BinaryWriter binaryWriter = new BinaryWriter(outputStream))
            {
                if (null == pngData)
                {
                    pngData = ImageHelper.LoadPngBytes(binaryReader);
                }
                else
                {
                    ImageHelper.SkipPng(binaryReader);
                    Logger.LogDebug("Skip Png:" + inputStream.Position);
                }

                dataSize = inputStream.Length - inputStream.Position;

                LongProgressCallback _compressProgress = null;
                if (null != compressProgress && dataSize > 0)
                {
                    _compressProgress = (long inSize, long _) => compressProgress(Convert.ToDecimal(inSize) / dataSize);
                }

                binaryWriter.Write(pngData);

                if (null == token)
                {
                    token = GuessToken(binaryReader);
                }

                switch (token)
                {
                    case Token.StudioToken:
                        binaryWriter.Write(new Version(101, 0, 0, 0).ToString());
                        break;
                    case Token.CoordinateToken:
                        binaryWriter.Write(101);
                        break;
                    default:
                        if (token.IndexOf(Token.CharaToken) >= 0)
                        {
                            binaryWriter.Write(101);
                            break;
                        }
                        throw new IOException("PNG token does not match compression marker.");
                }

                binaryWriter.Write(token);

                using (MemoryStream msCompressed = new MemoryStream())
                {
                    long fileStreamPos = inputStream.Position;

                    LZMA.Compress(inputStream, msCompressed, LzmaSpeed.Fastest, DictionarySize.VeryLarge, _compressProgress);

                    Logger.LogDebug("Start compression test...");
                    if (doCompare)
                    {
                        using (MemoryStream msDecompressed = new MemoryStream())
                        {
                            msCompressed.Seek(0, SeekOrigin.Begin);
                            LZMA.Decompress(msCompressed, msDecompressed);
                            inputStream.Seek(fileStreamPos, SeekOrigin.Begin);
                            msDecompressed.Seek(0, SeekOrigin.Begin);
                            int bufferSize = 1 << 10;
                            byte[] originalBuffer = new byte[(int)bufferSize];
                            byte[] decompressedBuffer = new byte[(int)bufferSize];

                            if ((inputStream.Length - inputStream.Position) != msDecompressed.Length)
                            {
                                return 0;
                            }

                            for (long i = 0; i < msDecompressed.Length;)
                            {
                                if (null != compressProgress)
                                {
                                    compareProgress(Convert.ToDecimal(i) / msDecompressed.Length);
                                }

                                inputStream.Read(originalBuffer, 0, (int)bufferSize);
                                i += msDecompressed.Read(decompressedBuffer, 0, (int)bufferSize);
                                bool diff = false;
                                for (int j = 0; j < bufferSize; j++)
                                {
                                    if (originalBuffer[j] != decompressedBuffer[j]) { diff = true; break; }
                                }
                                if (diff)
                                {
                                    return 0;
                                }
                            }
                        }
                    }
                    binaryWriter.Write(msCompressed.ToArray());
                    return binaryWriter.BaseStream.Length;
                }
            }
        }

        public long Load(string inputPath, string outputPath, string token = null, ProgressCallback decompressProgress = null)
        {
            using (FileStream fileStreamReader = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream fileStreamWriter = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                return Load(fileStreamReader, fileStreamWriter, token: token, decompressProgress: decompressProgress);
            }
        }

        public long Load(Stream inputStream, Stream outputStream, string token = null, byte[] pngData = null, ProgressCallback decompressProgress = null)
        {
            long dataSize = 0;

            using (BinaryReader binaryReader = new BinaryReader(inputStream))
            using (BinaryWriter binaryWriter = new BinaryWriter(outputStream))
            {
                if (null == pngData)
                {
                    pngData = ImageHelper.LoadPngBytes(binaryReader);
                }
                else
                {
                    ImageHelper.SkipPng(binaryReader);
                    Logger.LogDebug("Skip Png:" + inputStream.Position);
                }

                if (!GuessCompressed(binaryReader))
                {
                    return 0;
                }

                try
                {
                    if (null == token)
                    {
                        token = GuessToken(binaryReader);
                        if (null == token)
                        {
                            throw new FileLoadException();
                        }
                    }
                    bool checkfail = false;

                    switch (token)
                    {
                        case Token.StudioToken:
                            try
                            {
                                checkfail = !new Version(binaryReader.ReadString()).Equals(new Version(101, 0, 0, 0));
                            }
                            catch
                            {
                                checkfail = true;
                            }
                            break;
                        case Token.CoordinateToken:
                        default:
                            checkfail = 101 != binaryReader.ReadInt32();
                            break;
                    }

                    if (checkfail)
                    {
                        throw new FileLoadException();
                    }
                }
                catch (FileLoadException e)
                {
                    Logger.LogError("Corrupted file");
                    throw e;
                }
                try
                {
                    binaryReader.ReadString();
                    Logger.LogDebug("Start Decompress...");
                    binaryWriter.Write(pngData);

                    dataSize = inputStream.Length - inputStream.Position;

                    LongProgressCallback _decompressProgress = null;
                    if (null != decompressProgress && dataSize > 0)
                    {
                        _decompressProgress = (long inSize, long _) => decompressProgress(Convert.ToDecimal(inSize) / dataSize);
                    }

                    LZMA.Decompress(inputStream, outputStream, _decompressProgress);
                }
                catch (Exception)
                {
                    Logger.LogError($"Decompression FAILED. The file was corrupted during compression or storage.");
                    Logger.LogError($"Do not disable the byte comparison setting next time to avoid this.");
                    throw;
                }
                return binaryWriter.BaseStream.Length;
            }
        }

        public string GuessToken(BinaryReader binaryReader)
        {
            long position = binaryReader.BaseStream.Position;
            try
            {
                int r = binaryReader.ReadInt32();
                if (r != 101 && r != 100)
                {
                    return Token.StudioToken;
                }
                string token = binaryReader.ReadString();
                if (token.IndexOf(Token.CharaToken) >= 0)
                {
                    return Token.CharaToken + "】" + Token.SexToken + 1;
                }
                else if (token == Token.CoordinateToken)
                {
                    return Token.CoordinateToken;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                binaryReader.BaseStream.Seek(position, SeekOrigin.Begin);
            }
            return null;
        }

        public bool GuessCompressed(BinaryReader binaryReader)
        {
            long position = binaryReader.BaseStream.Position;
            try
            {
                int r = binaryReader.ReadInt32();
                switch (r)
                {
                    case 101:
                        return true;
                    case 100:
                        return false;
                    default:
                        binaryReader.BaseStream.Seek(position, SeekOrigin.Begin);
                        string st = binaryReader.ReadString();
                        try
                        {
                            Version version = new Version(st);
                            return version.Major == 101;
                        }
                        catch
                        {
                            return false;
                        }
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                binaryReader.BaseStream.Seek(position, SeekOrigin.Begin);
            }
        }
    }
}
