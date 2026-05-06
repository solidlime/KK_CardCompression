# Zstd + 辞書圧縮 + シーンPNG再圧縮 — 実装計画

**Goal:** 圧縮エンジンを LZMA から Zstd（+事前学習辞書）に進化させ、シーンデータ内埋め込みPNGの再圧縮も追加し、圧縮率を大幅改善する。

**Architecture:** `ZstdCompressionService` で Zstd 圧縮・解凍を担当。`DictionaryBuilder` で `test/` 全ファイルから辞書を学習。`ScenePreprocessor` でシーンバイナリ内の埋め込みPNGを再圧縮。`CompressionService` が LZMA(旧) / Zstd(新) を統合しフォーマット自動判定。

**Tech:** C# .NET 8.0, ZstdSharp 0.8.1, SixLabors.ImageSharp 3.1.12

---

## ファイル構成

| ファイル | 責務 |
|---------|------|
| **`Services/ZstdCompressionService.cs`** (NEW) | Zstd 圧縮/解凍（辞書あり/なし）、フォーマットマーカー定義 |
| **`Services/DictionaryBuilder.cs`** (NEW) | 辞書学習（`test/` 全ファイル使用） |
| **`Services/ScenePreprocessor.cs`** (NEW) | シーンバイナリ解析、埋め込みPNG検出・再圧縮 |
| **`Resources/kk_universal_dict.zstd`** (NEW) | 学習済み辞書（EmbeddedResource） |
| **`Services/CompressionService.cs`** (MODIFY) | Zstd統合、マーカー自動判定（100/101/102/103）、SkipPng/RecompressPng を internal 化 |
| **`MainWindow.xaml`** (MODIFY) | 圧縮アルゴリズム選択UI追加 |
| **`MainWindow.xaml.cs`** (MODIFY) | Zstdレベル選択ロジック、設定保存 |
| **`Services/IniSettingsService.cs`** (MODIFY) | Algorithm 設定項目追加 |
| **`KK_CardCompression.csproj`** (MODIFY) | ZstdSharp パッケージ追加、辞書を EmbeddedResource に |

---

## 新フォーマット仕様（マーカー値）

```
[PNG] [マーカー] [トークン文字列] [圧縮データ]

マーカー値:
  100         → 未圧縮 (int32 / Version "100.0.0.0")
  101         → LZMA圧縮 (int32 / Version "101.0.0.0")  ← KK_SaveLoadCompression 互換
  102         → Zstd圧縮 辞書なし (int32 / Version "102.0.0.0")
  103         → Zstd圧縮 辞書あり (int32 / Version "103.0.0.0")

圧縮データの中身は共通:
  [元マーカー(100) + トークン + 生ゲームデータ]
  → 解凍後は必ず 100 マーカーの形式になる
```

**読み取り戦略（KK_CardCompression.dll）:**
- 100 → そのまま読む
- 101 → LZMA 解凍
- 102 → Zstd 解凍（辞書なし）
- 103 → Zstd 解凍（辞書あり）

---

## Phase 1: ZstdSharp 導入と ZstdCompressionService

### Task 1.1: パッケージ追加
- [ ] `KK_CardCompression.csproj` に `<PackageReference Include="ZstdSharp" Version="0.8.1" />` 追加
- [ ] `dotnet restore` 成功確認

### Task 1.2: ZstdCompressionService 実装

```csharp
// Services/ZstdCompressionService.cs
using System;
using System.IO;
using ZstdSharp;

namespace KK_CardCompression.Services
{
    public enum ZstdLevel
    {
        Fast = 3,
        Default = 6,
        Better = 14,
        Best = 19,
        Ultra = 22,
    }

    public static class KkFormatMarker
    {
        public const int Raw = 100;
        public const int Lzma = 101;
        public const int ZstdNoDict = 102;
        public const int ZstdWithDict = 103;
    }

    public static class ZstdCompressionService
    {
        private static byte[]? _embeddedDict;

        public static byte[] LoadEmbeddedDictionary()
        {
            if (_embeddedDict != null) return _embeddedDict;
            using var stream = typeof(ZstdCompressionService).Assembly
                .GetManifestResourceStream("KK_CardCompression.Resources.kk_universal_dict.zstd");
            if (stream == null) throw new InvalidOperationException("辞書リソースが見つかりません");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return _embeddedDict = ms.ToArray();
        }

        public static void SetDictionary(byte[] dict) => _embeddedDict = dict;

        public static void Compress(Stream input, Stream output, ZstdLevel level,
                                     bool useDictionary = false)
        {
            byte[] src = ReadToEnd(input);
            byte[] dst;
            if (useDictionary)
            {
                var dict = LoadEmbeddedDictionary();
                using var c = new Compressor(dict, (int)level);
                dst = c.Wrap(src);
            }
            else
            {
                using var c = new Compressor((int)level);
                dst = c.Wrap(src);
            }
            output.Write(dst);
        }

        public static void Decompress(Stream input, Stream output,
                                       int marker /* 101=旧LZMA, 102/103=Zstd */)
        {
            byte[] src = ReadToEnd(input);
            byte[] dst;
            if (marker == KkFormatMarker.ZstdWithDict)
            {
                var dict = LoadEmbeddedDictionary();
                using var d = new Decompressor(dict);
                dst = d.Unwrap(src);
            }
            else // 102 = ZstdNoDict
            {
                using var d = new Decompressor();
                dst = d.Unwrap(src);
            }
            output.Write(dst);
            output.Flush();
        }

        private static byte[] ReadToEnd(Stream s)
        {
            if (s is MemoryStream ms) return ms.ToArray();
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
```

### Task 1.3: CompressionService 変更 — マーカー列挙型

- [ ] `CompressionLevel` enum に隣接して `CompressionAlgorithm { Lzma, Zstd }` を定義
- [ ] `KkFormatMarker` 定数を CompressionService から参照

---

## Phase 2: 辞書学習（DictionaryBuilder）

### Task 2.1: 学習ツール実装

```csharp
// Services/DictionaryBuilder.cs
using ZstdSharp;

namespace KK_CardCompression.Services
{
    public static class DictionaryBuilder
    {
        /// <summary>
        /// test/ ディレクトリ内の全 .png ファイルから学習データを抽出し、
        /// Zstd 辞書を構築する。
        /// </summary>
        public static byte[] Train(string testDir, int dictCapacity = 110 * 1024)
        {
            var samples = new List<byte[]>();
            var pngFiles = Directory.GetFiles(testDir, "*.png",
                SearchOption.AllDirectories);

            Console.WriteLine($"学習ファイル数: {pngFiles.Length}");

            foreach (var file in pngFiles)
            {
                try
                {
                    byte[] data = ExtractDataAfterPng(file);
                    if (data.Length >= 1024)
                        samples.Add(data);
                }
                catch { /* skip corrupted files */ }
            }

            Console.WriteLine($"有効サンプル: {samples.Count}");
            long totalBytes = samples.Sum(s => (long)s.Length);
            Console.WriteLine($"合計データ量: {totalBytes / (1024*1024)} MB");

            return DictBuilder.TrainFromBuffer(samples, dictCapacity);
        }

        /// <summary>
        /// PNG の IEND 以降のデータを抽出（学習対象）。
        /// </summary>
        private static byte[] ExtractDataAfterPng(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8);
            CompressionService.SkipPng(br);
            int remaining = (int)(fs.Length - fs.Position);
            if (remaining <= 0) return Array.Empty<byte>();
            byte[] data = new byte[remaining];
            fs.Read(data, 0, remaining);
            return data;
        }
    }
}
```

### Task 2.2: 辞書学習の実行

- [ ] 一時的なコンソールプロジェクトまたはユニットテストで `DictionaryBuilder.Train("D:\VSCode\KK_CardCompression\test")` を実行
- [ ] 辞書ファイルを `Resources/kk_universal_dict.zstd` に保存
- [ ] csproj に `EmbeddedResource` として登録:
  ```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\kk_universal_dict.zstd" />
  </ItemGroup>
  ```

---

## Phase 3: シーンPNG再圧縮（ScenePreprocessor）

### Task 3.1: ScenePreprocessor 実装

シーンデータ内の埋め込みPNGをすべて検出し、`SixLabors.ImageSharp` で `BestCompression` 再エンコードする。再圧縮後が元より大きい場合は元のまま。

```csharp
// Services/ScenePreprocessor.cs
namespace KK_CardCompression.Services
{
    public ref struct PngRegion
    {
        public long Offset;
        public int OriginalSize;
        public byte[] Data;          // 再圧縮後（元より大きい場合は OriginalData）
        public int NewSize;
    }

    public static class ScenePreprocessor
    {
        private static readonly byte[] PngSig = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] Iend = { 73, 69, 78, 68 };

        /// <summary>
        /// シーンバイナリ全体から全埋め込みPNGを再圧縮し、
        /// 新しいバイト列を返す。変化がなければ null。
        /// </summary>
        public static byte[]? Process(byte[] raw, long dataOffset)
        {
            var regions = FindAllPngs(raw, dataOffset);
            if (regions.Count == 0) return null;

            bool changed = false;
            foreach (var r in regions)
            {
                r.Data = Recompress(r.Data);
                r.NewSize = r.Data.Length;
                if (r.NewSize < r.OriginalSize) changed = true;
                else { r.NewSize = r.OriginalSize; r.Data = raw[(int)r.Offset..((int)r.Offset + r.OriginalSize)]; }
            }
            if (!changed) return null;

            // 再構築
            long delta = regions.Sum(r => (long)(r.NewSize - r.OriginalSize));
            var result = new byte[raw.Length + delta];
            long src = 0, dst = 0;
            foreach (var r in regions)
            {
                long skip = r.Offset - src;
                Array.Copy(raw, src, result, dst, skip);
                dst += skip;
                Array.Copy(r.Data, 0, result, dst, r.NewSize);
                dst += r.NewSize;
                src = r.Offset + r.OriginalSize;
            }
            Array.Copy(raw, src, result, dst, raw.Length - src);
            return result;
        }

        private static List<PngRegion> FindAllPngs(byte[] data, long start)
        {
            var list = new List<PngRegion>();
            long pos = start;
            while (pos <= data.Length - PngSig.Length)
            {
                if (!Match(data, pos, PngSig)) { pos++; continue; }
                long end = FindIend(data, pos);
                if (end < 0) { pos++; continue; }
                int sz = (int)(end - pos);
                var png = new byte[sz];
                Array.Copy(data, pos, png, 0, sz);
                list.Add(new PngRegion { Offset = pos, OriginalSize = sz, Data = png, NewSize = sz });
                pos = end;
            }
            return list;
        }

        private static long FindIend(byte[] data, long pngStart)
        {
            long pos = pngStart + 8;
            while (pos + 12 <= data.Length)
            {
                int len = (data[pos - 4] << 24) | (data[pos - 3] << 16)
                        | (data[pos - 2] << 8) | data[pos - 1];
                if (len < 0 || pos + 4 + len + 4 > data.Length) return -1;
                if (Match(data, pos, Iend))
                    return pos + 4 + len + 4;
                pos += 4 + len + 4;
            }
            return -1;
        }

        private static bool Match(byte[] d, long p, byte[] pat)
        {
            for (int i = 0; i < pat.Length; i++)
                if (d[p + i] != pat[i]) return false;
            return true;
        }

        private static byte[] Recompress(byte[] original)
        {
            try
            {
                using var input = new MemoryStream(original);
                using var output = new MemoryStream();
                using var image = SixLabors.ImageSharp.Image.Load(input);
                image.SaveAsPng(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder
                {
                    CompressionLevel = SixLabors.ImageSharp.Formats.Png.PngCompressionLevel.BestCompression,
                    FilterMethod = SixLabors.ImageSharp.Formats.Png.PngFilterMethod.Adaptive,
                });
                byte[] r = output.ToArray();
                return r.Length < original.Length ? r : original;
            }
            catch { return original; }
        }
    }
}
```

### Task 3.2: CompressionService の内部メソッドを internal static 化

- [ ] `SkipPng(BinaryReader)` → `internal static void SkipPng(BinaryReader)`
- [ ] `RecompressPng(byte[])` → `internal static byte[] RecompressPng(byte[])`

---

## Phase 4: CompressionService 統合

### Task 4.1: CompressFile — Zstd 対応 + シーン前処理

圧縮フロー変更（疑似コード）:

```csharp
public static void CompressFile(string inputPath, string outputPath,
                                 CompressionAlgorithm algorithm,
                                 CompressionLevel lzmaLevel, ZstdLevel zstdLevel,
                                 bool recompressPng, IProgress<double>? progress)
{
    // 1. PNG読取 + トークン判定 + 圧縮済みチェック（既存ロジック流用）
    // 2. PNGバイト書き出し
    // 3. マーカー書出し:
    //    algorithm == Lzma → 101 / Version("101.0.0.0") (既存)
    //    algorithm == Zstd, StudioScene → 103 / Version("103.0.0.0") (辞書あり)
    //    algorithm == Zstd, それ以外    → 102
    // 4. トークン書出し
    // 5. 圧縮:
    //    algorithm == Lzma:
    //      LzmaCompress(inFs, outFs, lzmaLevel)  ← 既存
    //    algorithm == Zstd:
    //      a. PNG以降のデータをMemoryStreamに読取り
    //      b. StudioScene かつ recompressPng有効 → ScenePreprocessor.Process()
    //      c. ZstdCompressionService.Compress(processedData, outFs, zstdLevel, useDict)
}
```

### Task 4.2: DecompressFile — マーカー自動判定

```csharp
public static void DecompressFile(string inputPath, string outputPath, ...)
{
    // 1. PNG読取
    // 2. マーカー読取:
    //    int32 を読む → 100/101/102/103 判定
    //    100/101/102/103 以外 → Version 判定 (Studio用)
    // 3. トークン読取
    // 4. 展開:
    //    100 → そのままコピー
    //    101 → LzmaDecompress (既存)
    //    102 → ZstdCompressionService.Decompress(..., 102)
    //    103 → ZstdCompressionService.Decompress(..., 103)
}
```

### Task 4.3: IsCompressed — マーカー拡張

- [ ] 102, 103 も「圧縮済み」と判定するよう `IsCompressed` を更新

---

## Phase 5: GUI 統合

### Task 5.1: XAML — アルゴリズム選択追加

`MainWindow.xaml` に圧縮アルゴリズム選択用 ComboBox を追加:

```xml
<ComboBox x:Name="CmbAlgorithm" Grid.Column="1" Width="70"
          SelectionChanged="CmbAlgorithm_SelectionChanged">
    <ComboBoxItem Content="Zstd" Tag="Zstd" IsSelected="True"/>
    <ComboBoxItem Content="LZMA" Tag="Lzma"/>
</ComboBox>
```

圧縮レベル ComboBox は選択アルゴリズムに応じて動的切替:
- Zstd 時: `Fast(3) / Better(14) / Best(19) / Ultra(22)`
- LZMA 時: `Fast(5) / Normal(32) / Maximum(128) / Ultra(273)`

### Task 5.2: コードビハインド

- [ ] `CmbAlgorithm_SelectionChanged` で `CmbCompLevel.Items` を動的入替
- [ ] `GetSelectedAlgorithm()` / `GetSelectedZstdLevel()` プロパティ追加
- [ ] `SaveSettings()` に Algorithm を追加

### Task 5.3: IniSettings 更新

- [ ] `AppSettings` に `CompressionAlgorithm Algorithm` プロパティ追加
- [ ] `ZstdLevel ZstdCompressionLevel` プロパティ追加
- [ ] Ini 読み書きに Algorithm / ZstdLevel を追加

### Task 5.4: ProcessSingleFile 更新

- [ ] `ProcessSingleFile` が `CompressionAlgorithm` と `ZstdLevel` を受け取るよう変更
- [ ] Zstd/LZMA 分岐ロジック追加

---

## Phase 6: 検証

### Task 6.1: ビルド確認
- [ ] `dotnet build` 成功
- [ ] `dotnet build -c Release` 成功

### Task 6.2: 辞書学習実行
- [ ] `DictionaryBuilder.Train(testDir)` 実行
- [ ] 生成辞書ファイルを Resources/ に配置
- [ ] ビルドに埋め込まれていること確認

### Task 6.3: ラウンドトリップテスト

- [ ] キャラファイル → Zstd圧縮(102) → 解凍 → 元とバイナリ一致
- [ ] 衣装ファイル → Zstd圧縮(102) → 解凍 → 元とバイナリ一致
- [ ] シーンファイル → Zstd圧縮(103)+PNG再圧縮 → 解凍 → 元とバイナリ一致
- [ ] 旧 LZMA(101) ファイル → 解凍 → 元とバイナリ一致（後方互換）

### Task 6.4: ベンチマーク

- [ ] `2025_0825_0030_49_221.png` (372MB) で以下を計測:
  - LZMA Maximum（現行ベースライン）
  - Zstd Better 辞書なし
  - Zstd Better 辞書あり
  - Zstd Better 辞書あり + PNG再圧縮
- [ ] その他シーンファイル 5件で同様に計測
- [ ] キャラ/衣装ファイルでも LZMA vs Zstd 比較

---

## Phase 7: BepInEx プラグイン（KK_CardCompression.dll）— 別プロジェクト

**※ 本セッションでは GUI アプリ側のみ実装。BepInEx プラグインは別セッションで対応。**

要件:
- BepInEx v5.4.23.3+ で動作
- マーカー 100/101/102/103 すべてを読める
- 101 → LZMA 解凍（旧ファイル互換）
- 102/103 → Zstd 解凍（新ファイル）
- 辞書はプラグイン内部に EmbeddedResource として持つ
- シーンの場合、`ScenePreprocessor` 相当の処理は不要（解凍時にPNGは元のまま）

---

## リスク・注意点

| リスク | 対策 |
|--------|------|
| ZstdSharp 0.8.1 の Decompressor が辞書なしフレームを読めるか | 事前検証（bench の DLL で動作テスト） |
| 辞書サイズが 110KB 超 | 100KB に制限して試行、効果が薄ければ調整 |
| ScenePreprocessor の PNG 検出誤検知 | PNG sig (8bytes) + IEND 確認で偽陽性率はほぼゼロ |
| 大ファイル(372MB+)のメモリ消費 | MemoryStream 使用、ピーク時 ~1GB。.NET 8 の GC で問題ないはず |
| 学習データの偏り | test/ 全ファイル使用でカバー範囲最大化、不足なら追加 |
