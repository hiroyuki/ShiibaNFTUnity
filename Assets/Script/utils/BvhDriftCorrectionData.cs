using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// BVH ドリフト補正データを管理する ScriptableObject
/// キーフレーム間での位置補完により、ドリフトを手動補正
/// </summary>
[CreateAssetMenu(menuName = "BVH/Drift Correction Data", fileName = "BvhDriftCorrectionData")]
public class BvhDriftCorrectionData : ScriptableObject
{
    [Header("Keyframe Settings")]
    [SerializeField] private List<BvhKeyframe> keyframes = new List<BvhKeyframe>();

    [Header("Interpolation")]
    [SerializeField] private InterpolationType interpolationType = InterpolationType.Linear;

    [Header("Active")]
    [SerializeField] private bool isEnabled = true;

    // --------- Public API ---------

    /// <summary>
    /// 指定時刻でのアンカー相対位置を取得（キーフレーム間で補完）
    /// </summary>
    /// <param name="time">Timeline 上の時刻（秒）</param>
    /// <returns>親GameObject からの相対座標</returns>
    public Vector3 GetAnchorPositionAtTime(float time)
    {
        if (!isEnabled || keyframes.Count == 0)
            return Vector3.zero;

        // キーフレームが1つだけの場合
        if (keyframes.Count == 1)
            return keyframes[0].anchorPositionRelative;

        // timeより前の最後のキーフレームと後のキーフレームを見つける
        BvhKeyframe prevKeyframe = null;
        BvhKeyframe nextKeyframe = null;

        var sortedKeyframes = keyframes.OrderBy(k => k.timelineTime).ToList();

        foreach (var kf in sortedKeyframes)
        {
            if (kf.timelineTime <= time)
                prevKeyframe = kf;
            else if (nextKeyframe == null)
                nextKeyframe = kf;
        }

        // 補完不可ケースの処理
        if (prevKeyframe == null && nextKeyframe != null)
            return nextKeyframe.anchorPositionRelative;
        if (prevKeyframe != null && nextKeyframe == null)
            return prevKeyframe.anchorPositionRelative;
        if (prevKeyframe == null && nextKeyframe == null)
            return Vector3.zero;

        // キーフレーム間での補完
        float timeDelta = nextKeyframe.timelineTime - prevKeyframe.timelineTime;
        if (timeDelta <= 0)
            return prevKeyframe.anchorPositionRelative;

        float t = (time - prevKeyframe.timelineTime) / timeDelta;
        t = Mathf.Clamp01(t);

        return InterpolatePosition(prevKeyframe.anchorPositionRelative,
                                   nextKeyframe.anchorPositionRelative, t);
    }

    /// <summary>
    /// キーフレーム追加
    /// </summary>
    public void AddKeyframe(float time, int frameNumber, Vector3 positionRelative)
    {
        var newKeyframe = new BvhKeyframe(time, frameNumber, positionRelative);
        keyframes.Add(newKeyframe);
        SortKeyframes();

        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        #endif

        Debug.Log($"Keyframe added: time={time}s, frame={frameNumber}, pos={positionRelative}");
    }

    /// <summary>
    /// キーフレーム削除（IDベース）
    /// </summary>
    public bool RemoveKeyframeById(int keyframeId)
    {
        var kf = keyframes.FirstOrDefault(k => k.GetKeyframeId() == keyframeId);
        if (kf != null)
        {
            keyframes.Remove(kf);
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
            Debug.Log($"Keyframe removed: id={keyframeId}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// キーフレーム更新（IDで検索して更新）
    /// </summary>
    public bool UpdateKeyframe(int keyframeId, float time, int frameNumber, Vector3 positionRelative)
    {
        var kf = keyframes.FirstOrDefault(k => k.GetKeyframeId() == keyframeId);
        if (kf != null)
        {
            kf.timelineTime = time;
            kf.bvhFrameNumber = frameNumber;
            kf.anchorPositionRelative = positionRelative;
            SortKeyframes();

            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
            return true;
        }
        return false;
    }

    /// <summary>
    /// 全キーフレーム取得（ソート済み）
    /// </summary>
    public List<BvhKeyframe> GetAllKeyframes()
    {
        return new List<BvhKeyframe>(keyframes.OrderBy(k => k.timelineTime).ToList());
    }

    /// <summary>
    /// キーフレーム数
    /// </summary>
    public int GetKeyframeCount() => keyframes.Count;

    /// <summary>
    /// ドリフト補正の有効状態
    /// </summary>
    public bool IsEnabled => isEnabled;

    /// <summary>
    /// ドリフト補正の有効/無効を切り替え
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
    }

    // --------- Private Methods ---------

    private void SortKeyframes()
    {
        keyframes = keyframes.OrderBy(k => k.timelineTime).ToList();
    }

    private Vector3 InterpolatePosition(Vector3 from, Vector3 to, float t)
    {
        switch (interpolationType)
        {
            case InterpolationType.Linear:
                return Vector3.Lerp(from, to, t);

            case InterpolationType.Spline:
                // TODO: Catmull-Rom や Hermite補完を実装
                return Vector3.Lerp(from, to, t);

            default:
                return Vector3.Lerp(from, to, t);
        }
    }
}

/// <summary>
/// 補完タイプ
/// </summary>
public enum InterpolationType
{
    Linear,
    Spline
}
