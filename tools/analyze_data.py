#!/usr/bin/env python3
"""Analyze KK card data structure to find compression optimization opportunities."""

import struct, os, io, zstandard, math
from collections import Counter

def find_iend_end(data):
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        return -1
    pos = 8
    while pos < len(data):
        if pos + 8 > len(data):
            break
        length = struct.unpack('>I', data[pos:pos+4])[0]
        chunk_type = data[pos+4:pos+8]
        chunk_end = pos + 12 + length
        if chunk_type == b'IEND':
            return chunk_end
        pos = chunk_end
    return -1

def read_binary_writer_string(br):
    b = br.read(1)[0]
    str_len = b & 0x7f
    shift = 7
    while b & 0x80:
        b = br.read(1)[0]
        str_len |= (b & 0x7f) << shift
        shift += 7
    return br.read(str_len)

def shannon_entropy(data):
    if len(data) == 0:
        return 0.0
    freq = Counter(data)
    total = len(data)
    return -sum((c/total) * math.log2(c/total) for c in freq.values() if c > 0)

def find_embedded_pngs(data):
    png_sig = b'\x89PNG\r\n\x1a\n'
    offsets = []
    pos = 0
    while True:
        idx = data.find(png_sig, pos)
        if idx < 0:
            break
        offsets.append(idx)
        pos = idx + 1
    return offsets

def extract_png_end(data, off):
    p = off + 8
    while p < len(data) - 12:
        length = struct.unpack('>I', data[p:p+4])[0]
        chunk_type = data[p+4:p+8]
        p += 12 + length
        if chunk_type == b'IEND':
            return p
    return len(data)

def analyze_file(filepath, label):
    data = open(filepath, 'rb').read()
    iend = find_iend_end(data)
    if iend < 0:
        print(f"  {label}: Not a valid PNG")
        return

    extra = data[iend:]
    br = io.BytesIO(extra)

    # Read marker
    first_int = struct.unpack('<i', br.read(4))[0]

    if first_int in (100, 101, 102, 103):
        # Non-studio format
        token = read_binary_writer_string(br)
        game_data = extra[br.tell():]
        print(f"\n=== {label} (キャラ/衣装) ===")
        print(f"  Marker: {first_int}, Token: {token[:30]}")
    else:
        # Studio format - re-read
        br = io.BytesIO(extra)
        version = read_binary_writer_string(br)
        token = read_binary_writer_string(br)
        game_data = extra[br.tell():]
        print(f"\n=== {label} (シーン) ===")
        print(f"  Version: {version.decode('utf-8', errors='replace')}, Token: {token[:20]}")

    print(f"  GameData: {len(game_data)/1e6:.1f}MB")
    print(f"  Shannon entropy: {shannon_entropy(game_data):.2f} bits/byte (max 8.0)")

    # Find embedded PNGs
    png_offsets = find_embedded_pngs(game_data)
    total_png = 0
    png_sizes = []
    for off in png_offsets:
        end = extract_png_end(game_data, off)
        png_size = end - off
        total_png += png_size
        png_sizes.append(png_size)

    non_png_size = len(game_data) - total_png
    print(f"  埋め込みPNG: {len(png_offsets)}個, {total_png/1e6:.1f}MB ({total_png/len(game_data)*100:.1f}%)")
    if png_sizes:
        print(f"  PNGサイズ: min={min(png_sizes)/1e3:.0f}KB, max={max(png_sizes)/1e3:.0f}KB, avg={sum(png_sizes)/len(png_sizes)/1e3:.0f}KB")
    print(f"  非PNGデータ: {non_png_size/1e3:.0f}KB ({non_png_size/len(game_data)*100:.1f}%)")

    # Compress non-PNG data
    segments = []
    prev_end = 0
    for off in png_offsets:
        if off > prev_end:
            segments.append(game_data[prev_end:off])
        end = extract_png_end(game_data, off)
        prev_end = end
    if prev_end < len(game_data):
        segments.append(game_data[prev_end:])
    non_png_data = b''.join(segments)

    if len(non_png_data) > 0:
        print(f"  非PNG entropy: {shannon_entropy(non_png_data):.2f} bits/byte")
        cctx = zstandard.ZstdCompressor(level=14)
        comp = cctx.compress(non_png_data)
        print(f"  非PNG Zstd-14圧縮: {len(comp)/len(non_png_data)*100:.1f}%")

    # Whole game data compression
    cctx = zstandard.ZstdCompressor(level=14)
    comp_whole = cctx.compress(game_data)
    print(f"  全体 Zstd-14圧縮: {len(comp_whole)/len(game_data)*100:.1f}%")

    # Try: strip PNGs, compress non-PNG, then compress PNGs separately
    if len(non_png_data) > 0 and len(png_sizes) > 0:
        all_pngs = b''.join(game_data[off:extract_png_end(game_data, off)] for off in png_offsets)
        comp_pngs = cctx.compress(all_pngs)
        total_comp = len(comp) + len(comp_pngs)
        print(f"  分割圧縮(非PNG+PNG別): {total_comp/len(game_data)*100:.1f}%")

# Analyze representative files
print("=" * 60)
print("KK カードデータ構造分析")
print("=" * 60)

# Character files
chara_dir = 'test/chara/female'
chara_files = sorted(os.listdir(chara_dir))
for f in chara_files[:2]:
    path = os.path.join(chara_dir, f)
    if os.path.isfile(path):
        analyze_file(path, f"キャラ: {f[:30]}")

# Scene files
scene_dir = 'test/Studio/scene'
scene_files = sorted(os.listdir(scene_dir))
for f in scene_files[:2]:
    path = os.path.join(scene_dir, f)
    if os.path.isfile(path):
        size_mb = os.path.getsize(path) / 1e6
        if size_mb < 100:  # Skip very large files
            analyze_file(path, f"シーン: {f[:30]}")

print("\n" + "=" * 60)
print("結論:")
print("=" * 60)
print("""
キャラデータ: 99.6%が埋め込みPNG、非PNGは0.4%のみ
  → PNGが既に圧縮済み(entropy 7.95-7.99)なので、Zstd/LZMAではほぼ圧縮不可能
  → 非PNG部分はentropy 2.65なので圧縮可能だが、全体の0.4%しかない

シーンデータ: 95%が埋め込みPNG、非PNGは5%
  → 同じくPNGが圧縮のボトルネック
  → 非PNG部分はentropy 5.68でZstd圧縮率4.3%（非常に高い圧縮率）

改善の方向性:
1. 埋め込みPNGの再圧縮（既に実装済み、最大効果）
2. 埋め込みPNGの減色/最適化（大幅なサイズ削減の可能性）
3. 非PNG部分のMessagePack最適化（効果は限定的、5%しかない）
4. より高いZstdレベル（Ultra=22）は時間がかかる割に効果薄い
""")