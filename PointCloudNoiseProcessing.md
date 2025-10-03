# ポイントクラウドの重複・ノイズ処理

## 概要

マルチカメラシステムで生成されるポイントクラウドを統合する際の重複除去とノイズフィルタリングの手法について説明します。

## 1. 問題の定義

### 1.1 重複点の問題

```
カメラA: (1.00, 2.00, 3.00) - 赤
カメラB: (1.02, 1.98, 3.01) - 青  ← 同じ物体の重複点
カメラC: (0.99, 2.01, 2.99) - 緑  ← 同じ物体の重複点
```

**視覚的な問題:**
- 同じ表面に複数の点が重なって「厚み」が発生
- 色が混在してちらつき
- 点密度が不均一

### 1.2 ノイズの種類

**深度ノイズ:**
```csharp
// 無効な深度値
if (depthValue == 0 || depthValue > maxDepth) 
    continue; // スキップ

// 異常値検出
if (Math.Abs(depthValue - neighborDepth) > threshold)
    continue; // 孤立点を除去
```

**飛び散りノイズ:**
```
正常: ●●●●●●●
ノイズ: ●●●  ●  ●●●●  (孤立点)
```

## 2. 空間ハッシュによる効率的な処理

### 2.1 基本概念

3D空間を立方体のセルに分割し、各セルをハッシュテーブルのキーとして使用：

```csharp
public class SpatialHash3D
{
    private float cellSize;
    private Dictionary<Vector3Int, List<Vector3>> hashTable;
    
    // 3D座標をハッシュキーに変換
    private Vector3Int GetHashKey(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.y / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }
}
```

### 2.2 セルサイズの選択

| 用途 | 推奨セルサイズ | 説明 |
|------|--------------|------|
| 重複除去 | 1-2cm | 細かい重複を検出 |
| ノイズフィルタ | 3-5cm | 適度な近傍範囲 |
| 衝突判定 | 10-20cm | 大まかな空間分割 |
| レンダリング最適化 | 適応的 | 点密度に応じて調整 |

### 2.3 最適化された実装

```csharp
public class OptimizedSpatialHash
{
    private Dictionary<long, List<Vector3>> hashTable;
    private float invCellSize; // 除算を避けるため事前計算
    
    // 高速ハッシュ関数
    private long GetHashKey(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x * invCellSize);
        int y = Mathf.FloorToInt(position.y * invCellSize);
        int z = Mathf.FloorToInt(position.z * invCellSize);
        
        // 3D座標を1つのlong値にパック
        return ((long)x << 42) | ((long)y << 21) | (long)z;
    }
}
```

## 3. 重複除去アルゴリズム

### 3.1 ボクセルグリッド法

```csharp
public class VoxelGrid
{
    private float voxelSize = 0.01f; // 1cm
    private Dictionary<Vector3Int, PointData> grid = new();
    
    public void AddPoint(Vector3 worldPos, Color32 color, string deviceName)
    {
        Vector3Int voxelKey = new Vector3Int(
            Mathf.FloorToInt(worldPos.x / voxelSize),
            Mathf.FloorToInt(worldPos.y / voxelSize),
            Mathf.FloorToInt(worldPos.z / voxelSize)
        );
        
        if (grid.ContainsKey(voxelKey))
        {
            // 既存点と統合（平均化、最も信頼性の高い色を選択など）
            grid[voxelKey] = MergePoints(grid[voxelKey], worldPos, color, deviceName);
        }
        else
        {
            grid[voxelKey] = new PointData(worldPos, color, deviceName);
        }
    }
}
```

### 3.2 信頼度ベース統合

```csharp
private PointData MergePoints(PointData existing, Vector3 newPos, Color32 newColor, string deviceName)
{
    // カメラからの距離で信頼度を計算
    float existingReliability = CalculateReliability(existing);
    float newReliability = CalculateReliability(newPos, deviceName);
    
    if (newReliability > existingReliability)
    {
        return new PointData(newPos, newColor, deviceName);
    }
    else
    {
        // 位置は平均、色は信頼性の高い方
        Vector3 avgPos = (existing.position + newPos) * 0.5f;
        return new PointData(avgPos, existing.color, existing.deviceName);
    }
}
```

## 4. ノイズフィルタリング

### 4.1 統計的異常値除去

```csharp
public void RemoveStatisticalOutliers(List<Vector3> points)
{
    for (int i = 0; i < points.Count; i++)
    {
        var neighbors = spatialHash.FindNearestNeighbors(points[i], k: 10);
        float avgDistance = neighbors.Average(n => Vector3.Distance(points[i], n));
        
        // 統計的に外れている点を除去
        if (avgDistance > meanDistance + 2 * standardDeviation)
        {
            points.RemoveAt(i--); // 異常値として除去
        }
    }
}
```

### 4.2 近傍検索の最適化

**計算量の比較:**

| 手法 | 前処理 | 1回のクエリ | 全点処理 | メモリ |
|------|--------|------------|----------|--------|
| ナイーブ | O(1) | O(N) | **O(N²)** | O(1) |
| 空間ハッシュ | O(N) | O(1)～O(k) | **O(N)** | O(N) |
| KD-Tree | O(N log N) | O(log N) | **O(N log N)** | O(N) |

**推奨：** ポイントクラウドには**空間ハッシュ**が最適

## 5. 時間的一貫性チェック

点対応がない場合の時間的フィルタリング手法：

### 5.1 空間領域ベースの一貫性チェック

```csharp
public bool IsTemporalAnomaly(Vector3 point)
{
    Vector3Int voxelKey = GetVoxelKey(point);
    
    // 周囲のボクセルに前フレームで点があったかチェック
    int neighboringVoxelsWithPoints = 0;
    for (int x = -1; x <= 1; x++)
    for (int y = -1; y <= 1; y++)
    for (int z = -1; z <= 1; z++)
    {
        Vector3Int neighborKey = voxelKey + new Vector3Int(x, y, z);
        if (previousFrame.HasPoints(neighborKey))
            neighboringVoxelsWithPoints++;
    }
    
    // 周囲に前フレームの点がほとんどない = 突然出現した異常点
    return neighboringVoxelsWithPoints < 3;
}
```

### 5.2 密度変化ベースの検出

```csharp
public bool IsSuspiciousDensityChange(Vector3 point)
{
    Vector3Int voxel = GetVoxelKey(point);
    
    int prevCount = previousDensity.GetValueOrDefault(voxel, 0);
    int currCount = currentDensity.GetValueOrDefault(voxel, 0);
    
    // 急激な密度増加 = ノイズの可能性
    float densityRatio = currCount / (float)Math.Max(prevCount, 1);
    return densityRatio > 5.0f; // 5倍以上の急増はノイズ
}
```

### 5.3 物理的制約チェック

```csharp
public bool IsPhysicallyPlausible(Vector3 point, List<Vector3> previousFramePoints)
{
    float maxMovement = Time.deltaTime * maxObjectSpeed; // 例：5m/s
    
    float minDistanceToPrevious = previousFramePoints
        .Where(p => Vector3.Distance(p, point) < maxMovement * 2)
        .Select(p => Vector3.Distance(p, point))
        .DefaultIfEmpty(float.MaxValue)
        .Min();
    
    return minDistanceToPrevious <= maxMovement;
}
```

## 6. 統合実装例

### 6.1 統合フィルタクラス

```csharp
public class RobustTemporalFilter
{
    private SpatialTemporalFilter spatialFilter = new();
    private DensityBasedTemporalFilter densityFilter = new();
    private StatisticalTemporalFilter statisticalFilter = new();
    
    public bool IsTemporalNoise(Vector3 point, List<Vector3> currentFrame, List<Vector3> previousFrame)
    {
        int suspiciousFlags = 0;
        
        // 複数の手法で判定
        if (spatialFilter.IsTemporalAnomaly(point)) suspiciousFlags++;
        if (densityFilter.IsSuspiciousDensityChange(point)) suspiciousFlags++;
        if (statisticalFilter.IsStatisticalOutlier(point)) suspiciousFlags++;
        
        // 複数の手法で異常判定された場合のみノイズとする
        return suspiciousFlags >= 2;
    }
}
```

### 6.2 マルチデバイス統合処理

```csharp
public class UnifiedPointCloudProcessor
{
    public void ProcessDevicesIntoUnifiedMesh(List<SensorDevice> devices, Mesh unifiedMesh)
    {
        List<Vector3> allVertices = new List<Vector3>();
        List<Color32> allColors = new List<Color32>();
        
        foreach(var device in devices)
        {
            // 1. 各デバイスのデータを解析
            var depthData = device.GetLatestDepthValues();
            var colorData = device.GetLatestColorData();
            
            // 2. グローバル座標系に変換
            var deviceVertices = TransformToGlobalCoordinates(depthData, device);
            var deviceColors = ProcessColors(colorData, device);
            
            // 3. 統合リストに追加
            allVertices.AddRange(deviceVertices);
            allColors.AddRange(deviceColors);
        }
        
        // 4. 重複除去とノイズフィルタリング
        var filteredData = ProcessNoiseAndDuplicates(allVertices, allColors);
        
        // 5. 1つのMeshに統合
        unifiedMesh.vertices = filteredData.vertices;
        unifiedMesh.colors32 = filteredData.colors;
        unifiedMesh.SetIndices(GenerateIndices(filteredData.vertices.Length), MeshTopology.Points, 0);
    }
}
```

## 7. パフォーマンス最適化

### 7.1 段階的処理

```csharp
public class OptimizedUnifiedProcessor
{
    private int frameCounter = 0;
    
    public void ProcessFrame(List<SensorDevice> devices, Mesh unifiedMesh)
    {
        frameCounter++;
        
        if (frameCounter % 3 == 1)
        {
            FastDeduplication(devices, unifiedMesh); // 毎フレーム
        }
        else if (frameCounter % 3 == 2)
        {
            DetailedNoiseRemoval(unifiedMesh); // 3フレームに1回
        }
        else
        {
            QualityEnhancement(unifiedMesh); // 3フレームに1回
        }
    }
}
```

### 7.2 並列処理

```csharp
public void ProcessTemporalFiltering(List<Vector3> points)
{
    var filteredFlags = new bool[points.Count];
    
    Parallel.For(0, points.Count, i =>
    {
        filteredFlags[i] = IsTemporalNoise(points[i]);
    });
    
    // フィルタされた点を除去
    for (int i = points.Count - 1; i >= 0; i--)
    {
        if (filteredFlags[i])
            points.RemoveAt(i);
    }
}
```

## 8. パラメータ調整

```csharp
[System.Serializable]
public class PointCloudFilterSettings
{
    [Header("重複除去")]
    public float voxelSize = 0.01f; // ボクセルサイズ
    public bool enableDeduplication = true;
    
    [Header("ノイズ除去")]
    public bool enableNoiseFilter = true;
    public int neighborCount = 10; // 近傍点数
    public float outlierThreshold = 2.0f; // 異常値閾値
    
    [Header("時間的フィルタ")]
    public bool enableTemporalFilter = true;
    public float maxObjectSpeed = 5.0f; // m/s
}
```

## 9. まとめ

- **空間ハッシュ**により O(N²) → O(N) の劇的な性能改善
- **複数の手法を組み合わせ**ることで堅牢なフィルタリング
- **リアルタイム処理**のための段階的・並列処理
- **パラメータ調整可能**な柔軟な実装

これらの手法により、高品質で一貫性のある統合ポイントクラウドの生成が可能になります。