# MEMORY

## プロジェクト概要
KK Archive - コイカツ用キャラカード/シーンデータ/衣装カードの圧縮・解凍ツール。
`KK_SaveLoadCompression.dll` と互換性のある実装を目指す。

## 学習した知識・教訓
- コイカツカードファイルはPNG形式だが、内部に追加データが含まれる
- 圧縮アルゴリズムはLZMAを使用（7zの圧縮方式と同等）
- NuGetパッケージは `LZMA-SDK 19.0.0`（`SevenZip.Compression.LZMA` 名前空間）
- `using SevenZip;` と `using SevenZip.Compression.LZMA;` の両方が必要
- `Encoder`/`Decoder` は `System.Text` と衝突するため完全修飾名 `SevenZip.Compression.LZMA.Encoder` を使う

## KK ファイルフォーマット（PNG 末尾以降）
### キャラ / 衣装
```
[PNG bytes] [int32(LE): 100=未圧縮/101=圧縮済] [BinaryWriter string: token] [data or LZMA]
```
### スタジオ
```
[PNG bytes] [BinaryWriter string: "100.0.0.0"/"101.0.0.0"] [BinaryWriter string: "【KStudio】"] [data or LZMA]
```
### LZMA ストリーム構造
```
[5 bytes: LZMA props] [8 bytes: 元サイズ LE] [LZMA compressed bytes]
```
### 圧縮ファイルの LZMA 中身
`LZMA([version_100 + token + raw game data])` — 元データ全体が含まれる

## トークン種別
- キャラ: `【KoiKatuChara】sex0` (男) / `sex1` (女)
- 衣装: `【KoiKatuClothes】`
- スタジオ: `【KStudio】`

## 参考実装
- https://github.com/jim60105/KK/tree/KKSunshine/PngCompression/PngCompression.cs
- https://github.com/jim60105/KK/tree/KKSunshine/PngCompression/SevenZip.cs

## 安全設計
- 処理前に `.bak` バックアップ作成
- 出力ファイル存在チェック
- 失敗時は `.bak` からロールバック
