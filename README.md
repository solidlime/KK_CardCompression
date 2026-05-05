# KK CardCompression
Koikatsu のカード/シーン PNG を **Zstandard (Zstd)** で圧縮・解凍するデスクトップツールです。  
既存プラグイン `KK_SaveLoadCompression.dll` の圧縮強化版プラグイン **KK_CardCompression.dll** と連携して動作します。

## ⚙️ 動作環境

- **Windows 10/11 (64-bit)**
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0)** が必要です

> .NET 8 がインストールされていない場合、起動時にダウンロードページへの案内が表示されます。  
> 「.NET デスクトップランタイム 8.x.x (x64)」をダウンロードしてインストールしてください。

## ⚠️ 重要: プラグイン互換性

### KK_CardCompression.dll（新形式 / Zstd 圧縮）

- このツールで **新形式 (Zstd)** に圧縮したカードを Koikatsu で読み込むには **KK_CardCompression.dll** が必須です。
- インストール: Koikatsu の `BepInEx/plugins` フォルダに `KK_CardCompression.dll` を配置してください。

### KK_SaveLoadCompression.dll（旧形式 / LZMA 圧縮）との互換性

- **旧形式 (LZMA) で圧縮されたカードは引き続き読み込み可能**です。
- KK_CardCompression.dll は旧プラグインの上位互換です。
- 旧形式ファイルを新形式に変換することで、さらなる圧縮率向上とロード時間短縮が見込まれます。

> ⚠️ **KK_CardCompression.dll で保存したカードは、KK_CardCompression.dll がインストールされていない環境では読み込めません。**

## 圧縮アルゴリズム

| 形式 | アルゴリズム | 圧縮率目安 | 解凍速度 |
|------|------------|-----------|--------|
| 旧形式 (marker=101) | LZMA | ~12.9% 削減 | 低速 (~200 MB/s) |
| **新形式 (marker=102)** | **Zstd (+辞書)** | **~25〜30% 削減（予測）** | **高速 (~1.5 GB/s)** |

- Zstd は **解凍が LZMA の約 5〜10 倍速い** → ロード時間も改善
- 事前学習辞書により、カード特有の繰り返し構造（ボーン名・シェーダー名等）を効率的に圧縮

## 主な機能

### UI
- **入力/出力の 2 ペイン一覧** — 左パネルで圧縮前、右パネルで圧縮後を表示
- **同期スクロール** — 左右パネルが連動スクロール、同じ行が揃う
- **マウスオーバープレビュー** — ファイル名ホバーでサムネイル + サイズをPopup表示（入力・出力両方対応）
- **プレビュー切り替え** — ヘッダー右上のトグルで ON/OFF を制御

### 圧縮・解凍
- **出力サイズ列に個別進捗バーを表示** — 列幅に追従、各ファイルの圧縮進捗を可視化
- **ステータス部に全体進捗バーを表示** — 全体の圧縮進捗
- **圧縮完了後に圧縮率を表示** — 処理中は `—`、完了後に `xx.x%削減` または `xx.x%増加` を表示

### ファイル操作
- ファイル/フォルダのドラッグ & ドロップ
- 出力ファイルのダブルクリックで開く
- 出力ファイルを Explorer にドラッグ可能
- 入力一覧で Delete キー削除（実ファイルは削除しない）

### その他
- **PNG 再圧縮オプション** — チェックボックスで有効化、PNG の再圧縮により最大 +1.8% の圧縮率向上（衣装カード）
- 出力先フォルダを exe ルートの ini に保存し、次回起動時に復元
- 圧縮レベル、プレビュー設定を ini に保存

## 設定保存

実行ファイルと同じフォルダに `KK_CardCompression.ini` を作成します。

保存項目:
- LastOutputDirectory
- CompressionLevel
- RecompressPng
- PreviewEnabled

## 圧縮率表示

- 圧縮できた場合: `xx.x%削減`
- サイズが増えた場合: `xx.x%増加`

## ビルド

```bash
dotnet build -c Release
```

## 実行

```bash
dotnet run
```

## リリース

GitHub Releases で exe ファイルを配布しています。

## Git ワークフロー

### デフォルトブランチの統一（main）

このリポジトリは `main` をデフォルトブランチとして使用します。

```bash
# ローカル同期
git fetch origin --prune
git branch -r  # origin/master が表示されないことを確認
```
