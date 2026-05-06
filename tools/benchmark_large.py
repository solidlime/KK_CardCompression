#!/usr/bin/env python3
"""大規模シーンファイルのベンチマーク"""
import struct, os, io, time, zstandard

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

filepath = r"D:\VSCode\KK_CardCompression\test\Studio\scene\2025_0825_0030_49_221.png"
print(f"=== ベンチマーク: {os.path.basename(filepath)} ===\n")

data = open(filepath, 'rb').read()
print(f"ファイルサイズ: {len(data)/1e6:.1f} MB")

iend = find_iend_end(data)
png_size = iend
extra = data[iend:]
print(f"外側PNG: {png_size/1e3:.0f} KB ({png_size/len(data)*100:.1f}%)")
print(f"ゲームデータ: {len(extra)/1e6:.1f} MB ({len(extra)/len(data)*100:.1f}%)")

# Parse studio format
br = io.BytesIO(extra)
version = read_binary_writer_string(br)
token = read_binary_writer_string(br)
game_data = extra[br.tell():]
print(f"バージョン: {version.decode('utf-8', errors='replace')}")
print(f"ゲームデータ本体: {len(game_data)/1e6:.1f} MB")

# Find embedded PNGs
png_sig = b'\x89PNG\r\n\x1a\n'
png_offsets = []
pos = 0
while True:
    idx = game_data.find(png_sig, pos)
    if idx < 0:
        break
    png_offsets.append(idx)
    pos = idx + 1

total_png = 0
png_sizes = []
for off in png_offsets:
    p = off + 8
    while p < len(game_data) - 12:
        length = struct.unpack('>I', game_data[p:p+4])[0]
        chunk_type = game_data[p+4:p+8]
        p += 12 + length
        if chunk_type == b'IEND':
            png_size = p - off
            total_png += png_size
            png_sizes.append(png_size)
            break

non_png_size = len(game_data) - total_png
print(f"\n埋め込みPNG: {len(png_offsets)}個, {total_png/1e6:.1f} MB ({total_png/len(game_data)*100:.1f}%)")
print(f"非PNGデータ: {non_png_size/1e3:.0f} KB ({non_png_size/len(game_data)*100:.1f}%)")

# Zstd compression benchmarks
print(f"\n=== Zstd圧縮ベンチマーク ===\n")

dict_path = r"D:\VSCode\KK_CardCompression\Resources\kk_universal_dict.zstd"
dict_data = open(dict_path, 'rb').read()
print(f"辞書サイズ: {len(dict_data)/1e3:.0f} KB")

levels = [
    (3, "Fast"),
    (6, "Default"),
    (14, "Better"),
    (19, "Best"),
]

# Skip Ultra(22) for large files - too slow

results = []

for level, name in levels:
    # Without dictionary
    t0 = time.time()
    cctx = zstandard.ZstdCompressor(level=level)
    compressed_no_dict = cctx.compress(game_data)
    t_compress = time.time() - t0
    
    # Decompression speed
    dctx = zstandard.ZstdDecompressor()
    t0 = time.time()
    decompressed = dctx.decompress(compressed_no_dict)
    t_decompress = time.time() - t0
    
    assert decompressed == game_data, "Decompression mismatch!"
    ratio = len(compressed_no_dict) / len(game_data) * 100
    results.append(("Zstd-" + name, "辞書なし", len(compressed_no_dict), ratio, t_compress, t_decompress))
    print(f"Zstd-{name:8s} 辞書なし: {len(compressed_no_dict)/1e6:8.2f} MB ({ratio:5.1f}%)  圧縮{t_compress:6.1f}s  解凍{t_decompress:5.2f}s")

    # With dictionary
    dict_obj = zstandard.ZstdCompressionDict(dict_data)
    cctx_dict = zstandard.ZstdCompressor(dict_data=dict_obj, level=level)
    t0 = time.time()
    compressed_dict = cctx_dict.compress(game_data)
    t_compress_d = time.time() - t0
    
    dctx_dict = zstandard.ZstdDecompressor(dict_data=dict_obj)
    t0 = time.time()
    decompressed_d = dctx_dict.decompress(compressed_dict)
    t_decompress_d = time.time() - t0
    
    assert decompressed_d == game_data, "Decompression mismatch (dict)!"
    ratio_d = len(compressed_dict) / len(game_data) * 100
    results.append(("Zstd-" + name, "辞書あり", len(compressed_dict), ratio_d, t_compress_d, t_decompress_d))
    print(f"Zstd-{name:8s} 辞書あり: {len(compressed_dict)/1e6:8.2f} MB ({ratio_d:5.1f}%)  圧縮{t_compress_d:6.1f}s  解凍{t_decompress_d:5.2f}s")

# PNG recompression simulation
print(f"\n=== PNG再圧縮シミュレーション ===\n")
from PIL import Image

total_orig_png = 0
total_recompressed_png = 0
count = 0

for off in png_offsets:
    p = off + 8
    while p < len(game_data) - 12:
        length = struct.unpack('>I', game_data[p:p+4])[0]
        chunk_type = game_data[p+4:p+8]
        p += 12 + length
        if chunk_type == b'IEND':
            png_data = game_data[off:p]
            break
    
    try:
        img = Image.open(io.BytesIO(png_data))
        buf = io.BytesIO()
        img.save(buf, format='PNG', compress_level=9, optimize=True)
        recompressed = buf.getvalue()
        total_orig_png += len(png_data)
        total_recompressed_png += min(len(recompressed), len(png_data))
        count += 1
        if count % 50 == 0:
            print(f"  処理中... {count}/{len(png_offsets)}")
    except Exception as e:
        total_orig_png += len(png_data)
        total_recompressed_png += len(png_data)

png_saving = total_orig_png - total_recompressed_png
png_ratio = total_recompressed_png / total_orig_png * 100
print(f"\nPNG再圧縮結果:")
print(f"  処理PNG数: {count}/{len(png_offsets)}")
print(f"  元PNG合計: {total_orig_png/1e6:.1f} MB")
print(f"  再圧縮後: {total_recompressed_png/1e6:.1f} MB ({png_ratio:.1f}%)")
print(f"  削減量: {png_saving/1e6:.1f} MB ({png_saving/total_orig_png*100:.1f}%)")

# Just print the summary table
print(f"{'モード':<25} {'圧縮後MB':>10} {'圧縮率':>8} {'圧縮時間':>10} {'解凍時間':>10}")
print("-" * 70)
for name, dict_type, size, ratio, t_comp, t_decomp in results:
    print(f"{name} {dict_type:<6s} {size/1e6:10.2f} {ratio:7.1f}% {t_comp:8.1f}s {t_decomp:8.2f}s")

# Estimate with PNG recompression
print(f"\nPNG再圧縮 + Zstd-Better推定:")
best_no_dict = [r for r in results if r[0] == "Zstd-Better" and r[1] == "辞書なし"][0]
best_dict = [r for r in results if r[0] == "Zstd-Better" and r[1] == "辞書あり"][0]

# PNG recompression reduces game_data by png_saving bytes
# The compressed size scales proportionally
scale_no_dict = best_no_dict[2] / len(game_data)
scale_dict = best_dict[2] / len(game_data)

estimated_no_dict = best_no_dict[2] * (total_recompressed_png + non_png_size) / len(game_data)
estimated_dict = best_dict[2] * (total_recompressed_png + non_png_size) / len(game_data)

total_file_no_dict = png_size + estimated_no_dict
total_file_dict = png_size + estimated_dict

print(f"  Zstd-Better 辞書なし: {total_file_no_dict/1e6:.1f} MB ({total_file_no_dict/len(data)*100:.1f}%)")
print(f"  Zstd-Better 辞書あり: {total_file_dict/1e6:.1f} MB ({total_file_dict/len(data)*100:.1f}%)")
print(f"  元ファイル: {len(data)/1e6:.1f} MB")