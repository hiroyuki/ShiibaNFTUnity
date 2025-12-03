using UnityEngine;

/// <summary>
/// キーフレーム個別データ
/// タイムラインの特定時刻とそこでのBVHアンカー位置を記録
/// </summary>
[System.Serializable]
public class BvhKeyframe
{
    [Tooltip("Timeline上の時刻（秒）")]
    public double timelineTime;

    [Tooltip("対応するBVHフレーム番号")]
    public int bvhFrameNumber;

    [Tooltip("このキーフレームでのBVHルートの相対位置（親GameObjectからの相対座標）")]
    public Vector3 anchorPositionRelative;

    [Tooltip("このキーフレームでのBVHルートの相対回転（親GameObjectからの相対回転・オイラー角）")]
    public Vector3 anchorRotationRelative;

    [Tooltip("メモ（オプション）")]
    public string note = "";

    public BvhKeyframe()
    {
        timelineTime = 0.0;
        bvhFrameNumber = 0;
        anchorPositionRelative = Vector3.zero;
        anchorRotationRelative = Vector3.zero;
    }

    public BvhKeyframe(double time, int frame, Vector3 positionRelative, Vector3 rotationRelative = default)
    {
        timelineTime = time;
        bvhFrameNumber = frame;
        anchorPositionRelative = positionRelative;
        anchorRotationRelative = rotationRelative;
    }

    /// <summary>
    /// キーフレームの妥当性をチェック
    /// </summary>
    public bool IsValid()
    {
        return timelineTime >= 0 && bvhFrameNumber >= 0;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Detects when keyframe position is modified in the Inspector
    /// Logs the new position value for debugging and monitoring
    /// </summary>
    private void OnValidate()
    {
        Debug.Log($"[BvhKeyframe Position Changed] Time: {timelineTime}s, Frame: {bvhFrameNumber}, Position: {anchorPositionRelative}");
    }
#endif
}
