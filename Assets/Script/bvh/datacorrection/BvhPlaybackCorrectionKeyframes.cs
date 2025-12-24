using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Unified keyframe container for BVH playback correction
/// Stores frame mapping corrections (which frame to use at which time)
/// and transform corrections (position/rotation adjustments)
///
/// This single asset manages all correction keyframes used by:
/// - BvhPlaybackFrameMapper (frame timing corrections)
/// - BvhPlaybackTransformCorrector (position/rotation corrections)
/// </summary>
[CreateAssetMenu(menuName = "BVH/Playback Correction Keyframes", fileName = "BvhPlaybackCorrectionKeyframes")]
public class BvhPlaybackCorrectionKeyframes : ScriptableObject
{
    [Header("Keyframe Settings")]
    [SerializeField] private List<BvhKeyframe> keyframes = new List<BvhKeyframe>();

    [Header("Interpolation")]
    [SerializeField] private InterpolationType interpolationType = InterpolationType.Linear;

    [Header("Active")]
    [SerializeField] private bool isEnabled = true;

    // 最後に追加・更新されたキーフレームを追跡（キーフレーム更新機能用）
    private BvhKeyframe lastEditedKeyframe = null;

    // --------- Public API ---------

    /// <summary>
    /// 指定時刻でのアンカー相対位置を取得（キーフレーム間で補完）
    /// </summary>
    /// <param name="time">Timeline 上の時刻（秒）</param>
    /// <returns>親GameObject からの相対座標</returns>
    public Vector3 GetAnchorPositionAtTime(double time)
    {
        return InterpolateKeyframeValue(
            time,
            kf => kf.anchorPositionRelative
        );
    }

    /// <summary>
    /// 指定時刻でのアンカー相対回転を取得（キーフレーム間で補完）
    /// </summary>
    /// <param name="time">Timeline 上の時刻（秒）</param>
    /// <returns>親GameObject からの相対回転（オイラー角）</returns>
    public Vector3 GetAnchorRotationAtTime(double time)
    {
        return InterpolateKeyframeValue(
            time,
            kf => kf.anchorRotationRelative
        );
    }

    /// <summary>
    /// キーフレーム値の補完（位置・回転共通処理）
    /// </summary>
    private Vector3 InterpolateKeyframeValue(
        double time,
        System.Func<BvhKeyframe, Vector3> getValue)
    {
        if (!isEnabled || keyframes.Count == 0)
            return Vector3.zero;

        // キーフレームが1つだけの場合
        if (keyframes.Count == 1)
            return getValue(keyframes[0]);

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
            return getValue(nextKeyframe);
        if (prevKeyframe != null && nextKeyframe == null)
            return getValue(prevKeyframe);
        if (prevKeyframe == null && nextKeyframe == null)
            return Vector3.zero;

        // キーフレーム間での補完
        double timeDelta = nextKeyframe.timelineTime - prevKeyframe.timelineTime;
        if (timeDelta <= 0)
            return getValue(prevKeyframe);

        double t = (time - prevKeyframe.timelineTime) / timeDelta;
        t = Mathf.Clamp01((float)t);

        return InterpolatePosition(getValue(prevKeyframe),
                                   getValue(nextKeyframe), (float)t);
    }

    /// <summary>
    /// キーフレーム追加
    /// </summary>
    public void AddKeyframe(double time, int frameNumber, Vector3 positionRelative, Vector3 rotationRelative = default)
    {
        var newKeyframe = new BvhKeyframe(time, frameNumber, positionRelative, rotationRelative);
        keyframes.Add(newKeyframe);
        lastEditedKeyframe = newKeyframe;  // 最後編集フレームを記録
        SortKeyframes();

        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        #endif

        Debug.Log($"Keyframe added: time={time}s, frame={frameNumber}, pos={positionRelative}, rot={rotationRelative}");
    }

    /// <summary>
    /// キーフレーム削除
    /// </summary>
    public bool RemoveKeyframe(BvhKeyframe keyframe)
    {
        // 時刻とフレーム番号でキーフレームを特定（参照ではなく値で検索）
        var kf = keyframes.FirstOrDefault(k =>
            Mathf.Approximately((float)k.timelineTime, (float)keyframe.timelineTime) &&
            k.bvhFrameNumber == keyframe.bvhFrameNumber);

        if (kf != null)
        {
            keyframes.Remove(kf);
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
            Debug.Log($"Keyframe removed: time={keyframe.timelineTime}s");
            return true;
        }
        return false;
    }

    /// <summary>
    /// キーフレーム更新
    /// </summary>
    public bool UpdateKeyframe(BvhKeyframe keyframe, float time, int frameNumber, Vector3 positionRelative, Vector3 rotationRelative = default)
    {
        if (keyframe == null)
            return false;

        // 時刻とフレーム番号でキーフレームを特定（参照ではなく値で検索）
        var kf = keyframes.FirstOrDefault(k =>
            Mathf.Approximately((float)k.timelineTime, (float)keyframe.timelineTime) &&
            k.bvhFrameNumber == keyframe.bvhFrameNumber);

        if (kf != null)
        {
            kf.timelineTime = time;
            kf.bvhFrameNumber = frameNumber;
            kf.anchorPositionRelative = positionRelative;
            kf.anchorRotationRelative = rotationRelative;
            lastEditedKeyframe = kf;  // 最後編集フレームを記録
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
    /// 最後に編集されたキーフレームを取得
    /// Shift+Uでの更新用
    /// </summary>
    public BvhKeyframe GetLastEditedKeyframe() => lastEditedKeyframe;

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

    /// <summary>
    /// BVH ファイルのデフォルトフレームレートを取得
    /// Editor スクリプトで参考値計算用
    /// </summary>
    public float GetBvhFrameRate()
    {
        // BvhPlayableAsset 経由で BVH データを取得
        var pointCloudMgr = GameObject.FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (pointCloudMgr != null)
        {
            var config = pointCloudMgr.GetDatasetConfig();
            if (config != null)
            {
                var bvhPlayableAsset = Resources.FindObjectsOfTypeAll<BvhPlayableAsset>().FirstOrDefault();
                if (bvhPlayableAsset != null)
                {
                    var bvhData = bvhPlayableAsset.GetBvhData();
                    if (bvhData != null)
                    {
                        return bvhData.FrameRate;
                    }
                }
            }
        }

        // フォールバック: デフォルト 30fps
        return 30f;
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
