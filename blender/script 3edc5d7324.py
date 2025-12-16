import bpy
import numpy as np

# ====== 設定 ======
ply_path = "/Volumes/horristicSSD2T/Dropbox/projects/shiiba/mesh_exported/assets/Totori/PLY_Filtered_Motion/filtered_frame_0860.ply"

# ====== 既存オブジェクトの削除 ======
if "PointCloud" in bpy.data.objects:
    bpy.data.objects.remove(bpy.data.objects["PointCloud"], do_unlink=True)

# ====== PLYファイルの読み込み ======
# ヘッダーをスキップして、バイナリデータの開始位置を見つける
with open(ply_path, 'rb') as f:
    line = b''
    while line.strip() != b'end_header':
        line = f.readline()
    header_end = f.tell()

# バイナリデータの構造を定義
dt = np.dtype([
    ('x', '<f4'), ('y', '<f4'), ('z', '<f4'),
    ('red', 'u1'), ('green', 'u1'), ('blue', 'u1'),
    ('vx', '<f4'), ('vy', '<f4'), ('vz', '<f4')
])

# データ読み込み
with open(ply_path, 'rb') as f:
    f.seek(header_end)
    data = np.fromfile(f, dtype=dt)

n = len(data)
print(f"Loaded {n} points")
print(f"First point: x={data['x'][0]:.6f}, y={data['y'][0]:.6f}, z={data['z'][0]:.6f}")
print(f"First color: r={data['red'][0]}, g={data['green'][0]}, b={data['blue'][0]}")
print(f"First velocity: vx={data['vx'][0]:.6f}, vy={data['vy'][0]:.6f}, vz={data['vz'][0]:.6f}")

# ====== メッシュ作成 ======
mesh = bpy.data.meshes.new("PointCloudMesh")
obj = bpy.data.objects.new("PointCloud", mesh)
bpy.context.scene.collection.objects.link(obj)

# 頂点座標を設定
positions = np.stack([data['x'], data['y'], data['z']], axis=1)
mesh.from_pydata(positions.tolist(), [], [])
mesh.update()

# ====== カスタム属性の追加 ======
# 色属性 (RGB)
color_attr = mesh.attributes.new(name="color", type='FLOAT_COLOR', domain='POINT')
colors = np.stack([
    data['red'] / 255.0,
    data['green'] / 255.0,
    data['blue'] / 255.0,
    np.ones(n)  # Alpha
], axis=1).astype(np.float32)
color_attr.data.foreach_set("color", colors.ravel())

# 速度属性 (vx, vy, vz)
for name, arr in [('vx', data[  'vx']), ('vy', data['vy']), ('vz', data['vz'])]:
    attr = mesh.attributes.new(name=name, type='FLOAT', domain='POINT')
    attr.data.foreach_set("value", arr.astype(np.float32))

print("✅ 完了: PointCloudオブジェクトを作成しました")
print("   Spreadsheet Editorで 'PointCloud' を選択して、position/color/vx/vy/vz を確認してください")
