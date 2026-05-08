using System;
using System.IO;
using System.Diagnostics;
using KK_CardCompression.Services;

class TestRunner
{
    static int success = 0, fail = 0, skipped = 0;
    static long totalOriginal = 0, totalCompressed = 0;

    static void Main(string[] args)
    {
        string testDir = args.Length > 0 ? args[0] : @"D:\VSCode\KK_CardCompression\test";
        string outputDir = Path.Combine(testDir, "_output");
        string verifyDir = Path.Combine(testDir, "_verify");

        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        if (Directory.Exists(verifyDir)) Directory.Delete(verifyDir, true);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(verifyDir);

        Console.WriteLine("=== KK Card Compression Test ===");
        Console.WriteLine($"Algorithm: LZMA (Fastest / VerySmall Dictionary)");
        Console.WriteLine();

        var files = Directory.GetFiles(testDir, "*.png", SearchOption.AllDirectories);
        Console.WriteLine($"Found {files.Length} files\n");

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(testDir, file);
            var outPath = Path.Combine(outputDir, relative);
            var verifyPath = Path.Combine(verifyDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(verifyPath)!);

            long originalSize = new FileInfo(file).Length;
            totalOriginal += originalSize;

            Console.Write($"[{Path.GetDirectoryName(relative)}] {Path.GetFileName(file)}");
            Console.Write($" ({FormatSize(originalSize)})");

            try
            {
                var sw = Stopwatch.StartNew();
                CompressionService.CompressFile(file, outPath, null);
                sw.Stop();

                long compressedSize = new FileInfo(outPath).Length;
                totalCompressed += compressedSize;
                double ratio = (1.0 - (double)compressedSize / originalSize) * 100.0;

                Console.Write($" -> {FormatSize(compressedSize)} ({ratio:F1}% reduced, {sw.ElapsedMilliseconds}ms)");

                // Decompress
                CompressionService.DecompressFile(outPath, verifyPath);

                // Verify game data matches
                bool match = VerifyGameData(file, verifyPath);
                if (match)
                {
                    Console.WriteLine(" [OK - game data verified]");
                    success++;
                }
                else
                {
                    Console.WriteLine(" [FAIL - game data mismatch!]");
                    fail++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [ERROR: {ex.Message}]");
                fail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Success: {success}, Failed: {fail}, Skipped: {skipped}");
        Console.WriteLine($"Total: {FormatSize(totalOriginal)} -> {FormatSize(totalCompressed)}");
        double totalRatio = (1.0 - (double)totalCompressed / totalOriginal) * 100.0;
        Console.WriteLine($"Overall reduction: {totalRatio:F1}%");

        Environment.Exit(fail > 0 ? 1 : 0);
    }

    static bool VerifyGameData(string originalPath, string decompressedPath)
    {
        using var fa = File.OpenRead(originalPath);
        using var fb = File.OpenRead(decompressedPath);

        SkipPng(fa);
        SkipPng(fb);

        long remainingA = fa.Length - fa.Position;
        long remainingB = fb.Length - fb.Position;

        if (remainingA != remainingB)
        {
            Console.WriteLine($" (game data size: {remainingA} vs {remainingB})");
            return false;
        }

        byte[] bufA = new byte[81920];
        byte[] bufB = new byte[81920];
        int readA, readB;

        while ((readA = fa.Read(bufA, 0, bufA.Length)) > 0)
        {
            readB = 0;
            while (readB < readA)
            {
                int r = fb.Read(bufB, readB, readA - readB);
                if (r == 0) return false;
                readB += r;
            }
            for (int i = 0; i < readA; i++)
                if (bufA[i] != bufB[i]) return false;
        }
        return true;
    }

    static void SkipPng(Stream st)
    {
        byte[] sig = new byte[8];
        st.Read(sig, 0, 8);

        while (true)
        {
            byte[] lenBytes = new byte[4];
            st.Read(lenBytes, 0, 4);
            int length = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
            byte[] type = new byte[4];
            st.Read(type, 0, 4);
            st.Seek(length, SeekOrigin.Current);
            st.Seek(4, SeekOrigin.Current);
            if (type[0] == 'I' && type[1] == 'E' && type[2] == 'N' && type[3] == 'D')
                break;
        }
    }

    static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double value = bytes;
        while (value >= 1024 && order < sizes.Length - 1)
        {
            order++;
            value /= 1024;
        }
        return $"{value:F1} {sizes[order]}";
    }
}
