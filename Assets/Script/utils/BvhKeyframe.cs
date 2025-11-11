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

    [Tooltip("キーフレーム識別用ID（自動生成、編集不可）")]
    [SerializeField] private int keyframeId;

    [Tooltip("メモ（オプション）")]
    public string note = "";

    public BvhKeyframe()
    {
        timelineTime = 0f;
        bvhFrameNumber = 0;
        anchorPositionRelative = Vector3.zero;
        keyframeId = System.Guid.NewGuid().GetHashCode();
    }

    public BvhKeyframe(float time, int frame, Vector3 positionRelative)
    {
        timelineTime = time;
        bvhFrameNumber = frame;
        anchorPositionRelative = positionRelative;
        keyframeId = System.Guid.NewGuid().GetHashCode();
    }

    public int GetKeyframeId() => keyframeId;

    /// <summary>
    /// キーフレームの妥当性をチェック
    /// </summary>
    public bool IsValid()
    {
        return timelineTime >= 0 && bvhFrameNumber >= 0;
    }
}
