# MEMORY

## プロジェクト概要
KK_CardCompression - コイカツ用キャラカード/シーンデータ/衣装カードの圧縮・解凍ツール。
`KK_SaveLoadCompression.dll` と互換性のある実装。ブランド名: KKCC。

## 学習した知識・教訓
- コイカツカードファイルはPNG形式だが、内部に追加データが含まれる
- NuGetパッケージ: `LZMA-SDK 19.0.0`, `SixLabors.ImageSharp 3.1.12`, `ZstdSharp.Port 0.8.8`
- `SevenZip.Compression.LZMA` 名前空間の衝突に注意
- `.sync-conflict-*` ファイルと `test/` ディレクトリは csproj で除外必須
- `GenerateAssemblyInfo=false` 使用時、obj/ 内の自動生成属性と競合する場合は `rm -Recurse -Force obj` で解決
- **BepInEx プラグインの解凍ロジック**: 解凍データには既にマーカー(100)+トークン+ゲームデータが含まれるため、PNG + 解凍データをそのまま結合すればよい。マーカー・トークンを別途書き込むと二重書き込みになる（重大バグ、修正済み）
- **net48 プロジェクト**: `#nullable enable` 未対応のため `byte[]?` 等の nullable 注釈は CS8632 警告になる。`<NoWarn>CS8632</NoWarn>` で抑制するか、`?` を削除する

## KK ファイルフォーマット（PNG 末尾以降）
### キャラ / 衣装
```
[PNG bytes] [int32(LE): 100=未圧縮/101=LZMA/102=Zstd辞書なし/103=Zstd辞書あり] [BinaryWriter string: token] [data or compressed]
```
### スタジオ
```
[PNG bytes] [BinaryWriter string: "100.0.0.0"/"101.0.0.0"/"102.0.0.0"/"103.0.0.0"] [BinaryWriter string: "【KStudio】"] [data or compressed]
```
### 圧縮時の構造
```
[PNG] [圧縮マーカー(101/102/103)] [トークン] [圧縮(元マーカー100 + トークン + ゲームデータ)]
```
### 解凍後の構造
```
[PNG] + 解凍データ（マーカー100 + トークン + ゲームデータが既に含まれている）
```

## データ構造分析結果（重要）
- **キャラデータ**: 99.6%が埋め込みPNG、非PNGは0.4%のみ
- **シーンデータ**: 95-97%が埋め込みPNG、非PNGは3-5%
- **PNGのShannon entropy**: 7.95-7.99 bits/byte（ほぼ理論限界）
- **非PNGのShannon entropy**: 5.5-5.9 bits/byte（Zstdで4-8%に圧縮可能）
- **結論**: 汎用圧縮（Zstd/LZMA）だけではPNGをほぼ圧縮不可能。PNG再圧縮が唯一の有効な無劣化手法

## ベンチマーク結果（2026-05-06）
### ラウンドトリップテスト: 全12テスト PASS
### 圧縮率比較（キャラファイル）
| 設定 | 圧縮率 | 速度 |
|------|--------|------|
| LZMA-Max | 94.9% | 2052ms |
| Zstd-Better | 95.5% | 554ms |
| Zstd-Best | 94.7% | 1969ms |

### 圧縮率比較（シーンファイル）
| 設定 | 圧縮率 | 速度 |
|------|--------|------|
| LZMA-Max | 63.3% | 1432ms |
| Zstd-Better | 65.2% | 498ms |
| Zstd-Best | 64.9% | 622ms |
| Zstd+辞書 | 65.2% | 500ms |
| Zstd+辞書+PNG再圧縮 | 58.4% | 12963ms |

### PNG再圧縮効果（キャラファイル）
- Pillow BestCompression: 43.0MB → 38.0MB（11.7%削減）
- 透過ピクセル52.6%のテクスチャ: 2217KB → 1825KB（17.7%削減）
- **RGBAアルファチャンネルは完全に保持される（ロスレス再エンコード）**

## Zstd辞書
- 辞書ファイル: `Resources/kk_universal_dict.zstd` (112,640 bytes)
- EmbeddedResource としてビルドに組み込み
- 学習データ: 489サンプル（test/ディレクトリ）
- 効果: 小さいファイルで35.8%改善、大きなファイルで0.7-0.9%改善

## トークン種別
- キャラ: `【KoiKatuChara】sex0` (男) / `sex1` (女)
- 衣装: `【KoiKatuClothes】`
- スタジオ: `【KStudio】`

## 安全設計
- 処理前に `.bak` バックアップ作成
- 出力ファイル存在チェック
- 失敗時は `.bak` からロールバック
- PNG再圧縮: 再圧縮後が元より大きい場合は元データを使用

## BepInEx プラグイン
- プロジェクト: `KK_CardCompression.Plugin/`
- ターゲット: net48, BepInEx 5.4.21+
- 依存: IllusionLibs.KoikatsuSunshine パッケージ群, ZstdSharp.Port 0.8.8
- Harmony パッチ対象: ChaFile.CheckData, ChaFile.LoadFile, ChaFileControl.LoadCharaFile, ChaFileControl.LoadCharaFileKoikatsu, ChaFileCoordinate.LoadFile, SceneInfo.Load, SceneInfo.Import
- Zstd マーカー 102/103 のみ処理、101 は KK_SaveLoadCompression.dll に委譲
- 辞書は埋め込みリソース + フォールバック（ファイルシステム）の二重ロード
- キャッシュ: `%TEMP%\com.kkcardcompression.zstd\` に一時ファイル、終了時に削除

## 現在の実装状況
- ✅ Zstd圧縮/解凍（辞書あり/なし両対応）
- ✅ LZMA圧縮/解凍（後方互換）
- ✅ マーカー100-103全対応
- ✅ GUI統合（アルゴリズム選択ComboBox）
- ✅ 設定保存/読み込み（アルゴリズム・Zstdレベル）
- ✅ 辞書学習ツール（tools/train_dictionary.py）
- ✅ 埋め込みPNG再圧縮（全ファイル種別対応）
- ✅ ラウンドトリップテスト全PASS
- ✅ BepInEx プラグイン（KK_CardCompression.Plugin）
- ✅ プラグイン解凍ロジック修正（二重書き込みバグ修正済み）
- 🔲 大規模ベンチマーク（568MBシーンファイル）