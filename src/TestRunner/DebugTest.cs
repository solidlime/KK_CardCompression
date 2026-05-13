using System;
using System.IO;
using KK_CardCompression.Services;

class DebugTest
{
    static void Main(string[] args)
    {
        string testFile = @"D:\VSCode\KK_CardCompression\test\chara\male\2024_0210_0219_54_635_0000.png";
        string outPath = @"D:\VSCode\KK_CardCompression\test\_debug\compressed.png";
        string verifyPath = @"D:\VSCode\KK_CardCompression\test\_debug\decompressed.png";

        Directory.CreateDirectory(@"D:\VSCode\KK_CardCompression\test\_debug");

        Console.WriteLine($"Original: {new FileInfo(testFile).Length} bytes");

        CompressionService.CompressFile(testFile, outPath, null);

        Console.WriteLine($"Compressed: {new FileInfo(outPath).Length} bytes");

        CompressionService.DecompressFile(outPath, verifyPath);

        Console.WriteLine($"Decompressed: {new FileInfo(verifyPath).Length} bytes");

        AnalyzeFile(testFile, "Original");
        AnalyzeFile(outPath, "Compressed");
        AnalyzeFile(verifyPath, "Decompressed");

        CompareGameData(testFile, verifyPath);
    }

    static void AnalyzeFile(string path, string label)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        long pngStart = fs.Position;
        CompressionService.SkipPng(br);
        long pngEnd = fs.Position;
        long pngSize = pngEnd - pngStart;

        Console.WriteLine($"\n{label}:");
        Console.WriteLine($"  PNG size: {pngSize} bytes");
        Console.WriteLine($"  Remaining: {fs.Length - fs.Position} bytes");

        if (fs.Position < fs.Length)
        {
            int marker = br.ReadInt32();
            fs.Seek(-4, SeekOrigin.Current);

            if (marker == KkFormatMarker.Raw || marker == KkFormatMarker.Lzma)
            {
                Console.WriteLine($"  Marker (int32): {marker}");
                br.ReadInt32();
                string token = br.ReadString();
                Console.WriteLine($"  Token: {token}");
            }
            else
            {
                string version = br.ReadString();
                Console.WriteLine($"  Version string: {version}");
                string token = br.ReadString();
                Console.WriteLine($"  Token: {token}");
            }
        }
    }

    static void CompareGameData(string originalPath, string decompressedPath)
    {
        Console.WriteLine("\n=== Game Data Comparison ===");

        using var fa = File.OpenRead(originalPath);
        using var fb = File.OpenRead(decompressedPath);

        CompressionService.SkipPng(new BinaryReader(fa));
        CompressionService.SkipPng(new BinaryReader(fb));

        long posA = fa.Position;
        long posB = fb.Position;
        long lenA = fa.Length - posA;
        long lenB = fb.Length - posB;

        Console.WriteLine($"Original game data: {lenA} bytes (offset {posA})");
        Console.WriteLine($"Decompressed game data: {lenB} bytes (offset {posB})");

        if (lenA != lenB)
        {
            Console.WriteLine($"SIZE MISMATCH: {lenA} vs {lenB}");
            return;
        }

        byte[] bufA = new byte[1];
        byte[] bufB = new byte[1];
        long offset = 0;
        bool found = false;

        while (offset < lenA)
        {
            fa.Read(bufA, 0, 1);
            fb.Read(bufB, 0, 1);
            if (bufA[0] != bufB[0])
            {
                Console.WriteLine($"First difference at offset {offset}: 0x{bufA[0]:X2} vs 0x{bufB[0]:X2}");
                found = true;
                break;
            }
            offset++;
        }

        if (!found)
        {
            Console.WriteLine("Game data: IDENTICAL");
        }
    }


}
