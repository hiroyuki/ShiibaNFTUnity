using UnityEngine;

/// <summary>
/// キーフレーム個別データ
/// タイムラインの特定時刻とそこでのBVHアンカー位置を記録
/// </summary>
[System.Serializable]
public class BvhKeyframe
{
    [Tooltip("Timeline上の時刻（秒）")]
    public float timelineTime;

    [Tooltip("対応するBVHフレーム番号")]
    public int bvhFrameNumber;

    [Tooltip("このキーフレームでのBVHルートの相対位置（親GameObjectからの相対座標）")]
    public Vector3 anchorPositionRelative;

    [Tooltip("メモ（オプション）")]
    public string note = "";

    public BvhKeyframe()
    {
        timelineTime = 0f;
        bvhFrameNumber = 0;
        anchorPositionRelative = Vector3.zero;
    }

    public BvhKeyframe(float time, int frame, Vector3 positionRelative)
    {
        timelineTime = time;
        bvhFrameNumber = frame;
        anchorPositionRelative = positionRelative;
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
