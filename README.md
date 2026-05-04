# KK Archive

> コイカツ キャラカード・衣装カード・スタジオシーンの **圧縮・解凍ツール**

[![GitHub Release](https://img.shields.io/github/v/release/solidlime/KK_Archive?style=flat-square&color=00D4C8)](https://github.com/solidlime/KK_Archive/releases/latest)
[![.NET 8](https://img.shields.io/badge/.NET-8-blueviolet?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)

---

## 概要

KK_SaveLoadCompression プラグインと **完全互換** の LZMA 圧縮・解凍を行う Windows デスクトップアプリです。

- キャラカード / 衣装カード / スタジオシーンの .png ファイルをまとめて圧縮・解凍
- ドラッグ＆ドロップ対応（ファイル・フォルダ両対応）
- 2 ペイン UI：入力ファイル一覧 ↔ 出力ファイル一覧
- 圧縮レベル選択（速い / 標準 / 最大圧縮）
- 出力ファイルをダブルクリックで開く
- 出力ファイルをエクスプローラへドラッグ＆ドロップ

---

## ダウンロード

[**Releases ページ**](https://github.com/solidlime/KK_Archive/releases/latest) から KK_Archive.exe をダウンロードしてください。
インストール不要、.exe を直接実行できます。

---

## 使い方

1. KK_Archive.exe を起動
2. .png ファイルまたはフォルダをウィンドウにドロップ（左ペインに一覧表示）
3. 出力先フォルダを「📁 出力先選択」で指定
4. 処理方法を選択：
   - **✦ 自動判定** — 圧縮済みなら解凍、未圧縮なら圧縮
   - **▲ 圧縮** — 強制的に LZMA 圧縮
   - **▼ 解凍** — 強制的に解凍
5. 右ペインに出力ファイルが表示されます

### 圧縮レベル

| レベル | numFastBytes | 用途 |
|--------|-------------|------|
| ⚡ 速い (互換) | 5 | デフォルト。オリジナルプラグインと同じ設定 |
| ⚖ 標準 | 32 | バランス型（やや遅い、やや小さい） |
| ◆ 最大圧縮 | 128 | 最高圧縮率（時間がかかる） |

> **互換性について**: どのレベルでも辞書サイズ (64 MiB) は変わらないため、KK_SaveLoadCompression で読み込めます。LZMA プロパティはストリームの先頭 5 バイトに保存されているため、デコーダが自動的に適切な設定を選択します。

---

## フォーマット仕様

### キャラカード・衣装カード

\\\
[PNG データ] [int32: 100=非圧縮 / 101=圧縮済] [トークン(文字列)] [データ本体]
\\\

### スタジオシーン

\\\
[PNG データ] [バージョン文字列 "100.x.x.x"=非圧縮 / "101.x.x.x"=圧縮済] [データ本体]
\\\

圧縮済みの場合、データ本体は LZMA ストリーム（5 バイト LZMA プロパティ + 8 バイト長 + 圧縮データ）です。

---

## ビルド方法

\\\
git clone https://github.com/solidlime/KK_Archive.git
cd KK_Archive
dotnet build -c Release
\\\

### 自己完結型 EXE を生成

\\\
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
\\\

---

## リリース

 で始まるタグ (0.2.0 など) をプッシュすると GitHub Actions が自動で EXE をビルドしてリリースを作成します。

\\\
git tag v0.2.0
git push origin v0.2.0
\\\

---

## ライセンス

MIT License — 詳細は [LICENSE](LICENSE) を参照してください。
