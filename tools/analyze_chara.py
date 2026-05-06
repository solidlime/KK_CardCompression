#!/usr/bin/env python3
"""Analyze character file structure to understand compression limits."""
import struct, os, math
from collections import Counter

def find_png_end(data, start=0):
    """Find the end of the PNG IEND chunk."""
    pos = start + 8  # Skip PNG signature
    while pos < len(data):
        if pos + 12 > len(data):
            return -1
        length = struct.unpack('>I', data[pos:pos+4])[0]
        chunk_type = data[pos+4:pos+8]
        pos += 12 + length
        if chunk_type == b'IEND':
            return pos
    return -1

def read_binary_string(data, offset):
    """Read a BinaryReader-style string (7-bit encoded length + UTF-8)."""
    b = data[offset]
    strlen = b & 0x7f
    idx = offset + 1
    if b & 0x80:
        b = data[idx]
        strlen |= (b & 0x7f) << 7
        idx += 1
        if b & 0x80:
            b = data[idx]
            strlen |= (b & 0x7f) << 14
            idx += 1
    s = data[idx:idx+strlen].decode('utf-8', errors='replace')
    return s, idx + strlen

def calc_entropy(data):
    """Calculate Shannon entropy of data."""
    if len(data) == 0:
        return 0
    freq = Counter(data)
    return -sum((c/len(data)) * math.log2(c/len(data)) for c in freq.values() if c > 0)

def count_embedded_pngs(data):
    """Count embedded PNGs and their total size."""
    png_sig = bytes([137, 80, 78, 71, 13, 10, 26, 10])
    count = 0
    total_size = 0
    pos = 0
    while True:
        idx = data.find(png_sig, pos)
        if idx < 0:
            break
        # Find IEND
        iend = data.find(b'IEND', idx)
        if iend < 0:
            break
        png_end = iend + 8  # IEND type + CRC
        total_size += png_end - idx
        count += 1
        pos = png_end
    return count, total_size

# Analyze character files
test_dir = os.path.dirname(os.path.abspath(__file__))
# Go up to test directory
test_dir = os.path.join(os.path.dirname(test_dir), 'test')

files_to_analyze = []
for root, dirs, fnames in os.walk(test_dir):
    for f in fnames:
        if f.endswith('.png'):
            fp = os.path.join(root, f)
            size = os.path.getsize(fp)
            if size > 5_000_000:  # > 5MB
                files_to_analyze.append((f, fp, size))

files_to_analyze.sort(key=lambda x: -x[2])

print("=== Koikatsu Character File Structure Analysis ===\n")

for f, fp, size in files_to_analyze[:5]:
    with open(fp, 'rb') as fh:
        data = fh.read()
    
    png_end = find_png_end(data)
    if png_end < 0:
        print(f"{f}: Not a valid PNG, skipping")
        continue
    
    png_size = png_end
    extra_size = len(data) - png_end
    
    print(f"--- {f} ({size/1e6:.1f} MB) ---")
    print(f"  PNG thumbnail: {png_size/1e3:.0f} KB ({png_size/size*100:.1f}%)")
    print(f"  Game data: {extra_size/1e3:.0f} KB ({extra_size/size*100:.1f}%)")
    
    # Check marker type
    first_int = struct.unpack('<i', data[png_end:png_end+4])[0]
    if first_int in (100, 101, 102, 103):
        print(f"  Marker: int32 = {first_int}")
        # Read token
        token, token_end = read_binary_string(data, png_end + 4)
        print(f"  Token: \"{token}\"")
        game_data_start = token_end
    else:
        # Version string format (studio)
        version, after_version = read_binary_string(data, png_end)
        print(f"  Version: \"{version}\"")
        token, token_end = read_binary_string(data, after_version)
        print(f"  Token: \"{token}\"")
        game_data_start = token_end
    
    game_data = data[game_data_start:]
    print(f"  Game data size: {len(game_data)/1e6:.1f} MB")
    
    # Entropy
    entropy = calc_entropy(game_data[:min(1_000_000, len(game_data))])
    print(f"  Game data entropy: {entropy:.3f} bits/byte")
    
    # Embedded PNGs
    png_count, png_total = count_embedded_pngs(game_data)
    if png_count > 0:
        print(f"  Embedded PNGs: {png_count} ({png_total/1e6:.1f} MB, {png_total/len(game_data)*100:.1f}%)")
        non_png = len(game_data) - png_total
        print(f"  Non-PNG data: {non_png/1e6:.1f} MB ({non_png/len(game_data)*100:.1f}%)")
        if non_png > 0:
            non_png_entropy = calc_entropy(game_data[:min(1_000_000, len(game_data))])
            print(f"  Non-PNG entropy (approx): {non_png_entropy:.3f} bits/byte")
    else:
        print(f"  No embedded PNGs in game data")
    
    print()