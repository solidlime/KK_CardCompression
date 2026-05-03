# KNOWLEDGE - ドメイン知識・調査結果

## 業務・ドメイン知識
- コイカツ（Koikatsu）のキャラカード、コーデデータ、スタジオシーンデータはPNG形式だが、画像データの後に追加データが付与される
- 既存プラグイン `KK_SaveLoadCompression.dll` はLZMA圧縮を使用し、圧縮率は7zの「極致圧縮」に近い
- 圧縮済みファイルはプラグイン未導入の環境では読み取れない

## 調査・リサーチ結果
- 参考実装: https://github.com/solidlime/KK_Archive.git （Blazor WebAssembly版）
- 圧縮アルゴリズム: LZMA (SevenZip.Compression.LZMA) を使用
- 既存プラグインの仕様: `Save Load Compression/README.md` 参照

## 技術的な知見
- WPFでドラッグ＆ドロップを実装するには、`UIElement.Drop` イベントを使用
- LZMA圧縮のC#実装は `SevenZip.Compression` パッケージを使用するか、自前で実装する必要がある
- 非同期処理でUIをブロックしないように `async/await` を使用する

## 決定事項と理由
- デスクトップアプリとしてWPFを選択：既存プラグインがC#ベースのため、開発効率が高い
- LZMA圧縮ライブラリは `SevenZip.Compression.LZMA` を使用：既存実装との互換性を保つため
- 出力ディレクトリはユーザーが指定可能：柔軟性を持たせるため
