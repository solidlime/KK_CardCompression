#!/usr/bin/env python3
"""
Zstd 辞書学習スクリプト
test/ ディレクトリの全PNGファイルからシーンデータを抽出し、
Zstd辞書を学習して Resources/kk_universal_dict.zstd に保存する。

使い方:
    python tools/train_dictionary.py

要件:
    pip install zstandard
"""

import os
import struct
import sys
from pathlib import Path

import zstandard

# ── 設定 ──────────────────────────────────────────────
PROJECT_ROOT = Path(__file__).resolve().parent.parent
TEST_DIR = PROJECT_ROOT / "test"
OUTPUT_PATH = PROJECT_ROOT / "Resources" / "kk_universal_dict.zstd"
DICT_CAPACITY = 112640  # 110 KB
MIN_SAMPLE_SIZE = 1024  # 最小1KB以上のデータのみ
MAX_TOTAL_SIZE = 200 * 1024 * 1024  # 学習データ合計上限 200MB
MAX_SAMPLE_SIZE = 2 * 1024 * 1024  # 個別サンプル上限 2MB
# ──────────────────────────────────────────────────────


def find_iend_end(data: bytes) -> int:
    """PNGデータからIENDチャンクの終了位置を返す。"""
    # PNG署名: 89 50 4E 47 0D 0A 1A 0A
    png_sig = b"\x89PNG\r\n\x1a\n"
    if data[:8] != png_sig:
        return -1

    pos = 8  # PNG署名の後
    while pos < len(data):
        if pos + 8 > len(data):
            break
        length = struct.unpack(">I", data[pos : pos + 4])[0]
        chunk_type = data[pos + 4 : pos + 8]
        # チャンク: 長さ(4) + タイプ(4) + データ(length) + CRC(4)
        chunk_end = pos + 12 + length
        if chunk_type == b"IEND":
            return chunk_end
        pos = chunk_end

    return -1


def extract_data_after_png(filepath: Path) -> bytes:
    """PNGファイルからIEND以降のデータを抽出する。"""
    data = filepath.read_bytes()
    iend_end = find_iend_end(data)
    if iend_end < 0:
        return b""
    after_png = data[iend_end:]
    return after_png


def collect_samples(test_dir: Path) -> list[bytes]:
    """test/ ディレクトリ内の全PNGファイルからシーンデータを抽出する。
    合計サイズが上限を超えないよう、小さいサンプルを優先して選択する。
    """
    all_data = []  # (size, data)
    png_files = sorted(test_dir.rglob("*.png"))
    print(f"PNGファイル検索中: {test_dir} ({len(png_files)} 件)")

    skipped = 0
    for f in png_files:
        try:
            data = extract_data_after_png(f)
            if len(data) >= MIN_SAMPLE_SIZE and len(data) <= MAX_SAMPLE_SIZE:
                all_data.append((len(data), data))
            else:
                skipped += 1
        except Exception as ex:
            print(f"  スキップ: {f.name} ({ex})")
            skipped += 1

    # 小さいサンプルを優先（多様性を確保するため）
    all_data.sort(key=lambda x: x[0])

    samples = []
    total_bytes = 0
    for size, data in all_data:
        if total_bytes + size > MAX_TOTAL_SIZE:
            break
        samples.append(data)
        total_bytes += size

    print(f"有効サンプル数: {len(samples)} (上限内)")
    print(f"合計データ量: {total_bytes / (1024 * 1024):.1f} MB")
    if skipped:
        print(f"スキップ: {skipped} 件")
    return samples


def train_dictionary(samples: list[bytes], dict_capacity: int) -> bytes:
    """Zstd辞書を学習する。"""
    print(f"\n辞書学習開始 (容量: {dict_capacity} bytes, サンプル数: {len(samples)})...")
    dict_obj = zstandard.train_dictionary(dict_capacity, samples)
    dict_data = dict_obj.as_bytes()
    print(f"辞書サイズ: {len(dict_data)} bytes")
    return dict_data


def verify_dictionary(dict_data: bytes, samples: list[bytes]) -> None:
    """辞書を使って圧縮→展開のラウンドトリップテストを行う。"""
    print("\nラウンドトリップ検証中...")
    dict_obj = zstandard.ZstdCompressionDict(dict_data)
    cctx = zstandard.ZstdCompressor(dict_data=dict_obj)
    dctx = zstandard.ZstdDecompressor(dict_data=dict_obj)

    # 最初の5サンプルでテスト
    test_count = min(5, len(samples))
    for i in range(test_count):
        original = samples[i]
        compressed = cctx.compress(original)
        decompressed = dctx.decompress(compressed)
        assert decompressed == original, f"サンプル {i}: ラウンドトリップ失敗!"
        ratio = len(compressed) / len(original) * 100
        print(f"  サンプル {i}: {len(original):,} → {len(compressed):,} bytes ({ratio:.1f}%)")

    print("ラウンドトリップ検証: OK ✓")


def benchmark_comparison(dict_data: bytes, samples: list[bytes]) -> None:
    """辞書あり/なしの圧縮率を比較する。"""
    print("\n── ベンチマーク: 辞書なし vs 辞書あり ──")

    # テストサンプル（最大20件、各1MB以下）
    test_samples = [s for s in samples[:20] if len(s) <= 1_000_000][:10]
    if not test_samples:
        print("ベンチマーク対象なし")
        return

    dict_obj = zstandard.ZstdCompressionDict(dict_data)
    cctx_no_dict = zstandard.ZstdCompressor(level=3)
    cctx_with_dict = zstandard.ZstdCompressor(dict_data=dict_obj, level=3)

    total_orig = 0
    total_no_dict = 0
    total_with_dict = 0

    for i, sample in enumerate(test_samples):
        compressed_no = cctx_no_dict.compress(sample)
        compressed_wd = cctx_with_dict.compress(sample)
        total_orig += len(sample)
        total_no_dict += len(compressed_no)
        total_with_dict += len(compressed_wd)
        print(
            f"  [{i+1}] {len(sample):>10,} → "
            f"辞書なし: {len(compressed_no):>10,} ({len(compressed_no)/len(sample)*100:.1f}%)  "
            f"辞書あり: {len(compressed_wd):>10,} ({len(compressed_wd)/len(sample)*100:.1f}%)"
        )

    print(f"\n  合計:")
    print(f"    元サイズ:     {total_orig:>12,}")
    print(f"    辞書なし:     {total_no_dict:>12,} ({total_no_dict/total_orig*100:.1f}%)")
    print(f"    辞書あり:     {total_with_dict:>12,} ({total_with_dict/total_orig*100:.1f}%)")
    improvement = (total_no_dict - total_with_dict) / total_no_dict * 100
    print(f"    辞書による改善: {improvement:+.1f}%")


def main():
    print("=== Zstd 辞書学習ツール ===\n")

    # サンプル収集
    samples = collect_samples(TEST_DIR)
    if not samples:
        print("エラー: 有効なサンプルが見つかりませんでした。")
        sys.exit(1)

    # 辞書学習
    dict_data = train_dictionary(samples, DICT_CAPACITY)

    # ラウンドトリップ検証
    verify_dictionary(dict_data, samples)

    # ベンチマーク比較
    benchmark_comparison(dict_data, samples)

    # 保存
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_bytes(dict_data)
    print(f"\n辞書を保存しました: {OUTPUT_PATH}")
    print("完了!")


if __name__ == "__main__":
    main()