using UnityEngine;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Synchronizes BVH_Character transform with DatasetConfig changes in real-time,
/// even when Timeline is paused in Edit mode.
///
/// This component listens to DatasetConfig.OnBvhTransformChanged events and
/// immediately updates the BVH_Character's position, rotation, and scale.
/// </summary>
public class BvhTransformSync : MonoBehaviour
{
    [SerializeField] private PlayableDirector timeline;
    [SerializeField] private string bvhCharacterName = "BVH_Character";

    private Transform bvhCharacterTransform;
    private BvhPlayableAsset bvhAsset;

    private void OnEnable()
    {
        // Subscribe to DatasetConfig changes
        DatasetConfig.OnBvhTransformChanged += OnTransformChanged;
    }

    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        DatasetConfig.OnBvhTransformChanged -= OnTransformChanged;
    }

    private void Start()
    {
        // Find BVH_Character in scene
        FindBvhCharacter();

        // Find BvhPlayableAsset in Timeline
        FindBvhAsset();
    }

    private void FindBvhCharacter()
    {
        GameObject bvhCharacterGO = GameObject.Find(bvhCharacterName);
        if (bvhCharacterGO != null)
        {
            bvhCharacterTransform = bvhCharacterGO.transform;
            Debug.Log($"[BvhTransformSync] Found BVH_Character: {bvhCharacterName}");
        }
        else
        {
            Debug.LogWarning($"[BvhTransformSync] BVH_Character not found: {bvhCharacterName}");
        }
    }

    private void FindBvhAsset()
    {
        if (timeline == null)
        {
            Debug.LogWarning("[BvhTransformSync] Timeline PlayableDirector is not assigned!");
            return;
        }

        // Search through Timeline tracks for BvhPlayableAsset
        var playableAsset = timeline.playableAsset;
        if (playableAsset is UnityEngine.Timeline.TimelineAsset timelineAsset)
        {
            foreach (var track in timelineAsset.GetOutputTracks())
            {
                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is BvhPlayableAsset asset)
                    {
                        bvhAsset = asset;
                        Debug.Log("[BvhTransformSync] Found BvhPlayableAsset in Timeline");
                        return;
                    }
                }
            }
        }

        Debug.LogWarning("[BvhTransformSync] BvhPlayableAsset not found in Timeline!");
    }

    private void OnTransformChanged()
    {
        Debug.Log("[BvhTransformSync] Detected BVH transform change, updating...");
        // Only update if we have valid references
        if (bvhCharacterTransform == null)
        {
            FindBvhCharacter();
            if (bvhCharacterTransform == null) return;
        }

        // Get current DatasetConfig
        var config = DatasetConfig.GetInstance();
        if (config == null)
        {
            Debug.LogWarning("[BvhTransformSync] DatasetConfig not found!");
            return;
        }

        // Get drift correction data if available
        BvhPlaybackCorrectionKeyframes driftCorrectionData = null;
        if (bvhAsset != null)
        {
            driftCorrectionData = bvhAsset.GetDriftCorrectionData();
        }

        // Get current Timeline time
        double timelineTime = timeline != null ? timeline.time : 0.0;

        // Calculate corrected position and rotation using the same logic as BvhPlayableBehaviour
        Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(
            timelineTime,
            driftCorrectionData,
            config.BvhPositionOffset
        );

        Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(
            timelineTime,
            driftCorrectionData,
            config.BvhRotationOffset
        );

        // Apply transform
        bvhCharacterTransform.SetLocalPositionAndRotation(correctedPos, correctedRot);
        bvhCharacterTransform.localScale = config.BvhScale;

        Debug.Log($"[BvhTransformSync] Updated BVH_Character: pos={correctedPos}, rot={correctedRot.eulerAngles}, scale={config.BvhScale}");

#if UNITY_EDITOR
        // Mark scene as dirty so changes are visible in Edit mode
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(bvhCharacterTransform.gameObject);
        }
#endif
    }
}
