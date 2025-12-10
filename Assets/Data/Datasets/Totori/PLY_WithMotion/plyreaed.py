import sys
from plyfile import PlyData
import numpy as np

if len(sys.argv) < 2:
    print("Usage: python read_ply.py <filename> [num_lines]")
    sys.exit(1)

filename = sys.argv[1]
num_line = int(sys.argv[2]) if len(sys.argv) > 2 else 5

print(f"Reading: {filename}")

ply = PlyData.read(filename)

# 頂点データの構造確認
print("\n=== Vertex data type ===")
print(ply['vertex'].data.dtype)

# 最初の num_line 点を表示
print(f"\n=== First {num_line} vertices ===")
for v in ply['vertex'].data[:num_line]:
    print(v)
