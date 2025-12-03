using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.InputSystem;

public class TimelineController : MonoBehaviour
{
    [Header("Timeline Control")]
    [SerializeField] private PlayableDirector timeline;
    [SerializeField] private bool findTimelineAutomatically = true;
    
    [Header("Input Settings")]
    [SerializeField] private Key playPauseKey = Key.Space;
    [SerializeField] private Key stopKey = Key.Escape;
    [SerializeField] private Key addKeyframeKey = Key.A;        // Shift+A
    [SerializeField] private Key resetTimelineKey = Key.Digit0; // 0 key

    [Header("BVH Drift Correction")]
    private BvhPlayableAsset bvhPlayableAsset;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    
    private bool wasPlayingLastFrame = false;
    
    void Start()
    {
        // Auto-find timeline if not assigned
        if (timeline == null && findTimelineAutomatically)
        {
            timeline = FindFirstObjectByType<PlayableDirector>();
            if (timeline != null && showDebugLogs)
            {
                Debug.Log($"TimelineController: Auto-found Timeline: {timeline.gameObject.name}");
            }
        }

        if (timeline == null)
        {
            Debug.LogWarning("TimelineController: No PlayableDirector found!");
        }

        // Auto-find BvhPlayableAsset from Timeline clips
        if (bvhPlayableAsset == null && timeline != null)
        {
            bvhPlayableAsset = FindBvhPlayableAssetInTimeline();
            if (bvhPlayableAsset != null && showDebugLogs)
            {
                Debug.Log($"TimelineController: Auto-found BvhPlayableAsset in Timeline");
            }
            else if (showDebugLogs)
            {
                Debug.LogWarning("TimelineController: No BvhPlayableAsset found in Timeline clips. Keyframe functions will not work.");
            }
        }
    }

    /// <summary>
    /// Search for BvhPlayableAsset in Timeline clips
    /// </summary>
    private BvhPlayableAsset FindBvhPlayableAssetInTimeline()
    {
        if (timeline == null || timeline.playableAsset == null)
        {
            return null;
        }

        TimelineAsset timelineAsset = timeline.playableAsset as TimelineAsset;
        if (timelineAsset == null)
        {
            return null;
        }

        // Search through all tracks and clips
        foreach (var track in timelineAsset.GetOutputTracks())
        {
            foreach (var clip in track.GetClips())
            {
                if (clip.asset is BvhPlayableAsset bvhAsset)
                {
                    return bvhAsset;
                }
            }
        }

        return null;
    }

    void Update()
    {
        if (timeline == null) return;
        
        HandleInput();
        UpdatePlaybackStatus();
    }
    
    void HandleInput()
    {
        if (Keyboard.current == null) return;

        // Space key: Play/Pause toggle
        if (Keyboard.current[playPauseKey].wasPressedThisFrame)
        {
            TogglePlayPause();
        }

        // Escape key: Stop
        if (Keyboard.current[stopKey].wasPressedThisFrame)
        {
            StopTimeline();
        }

        // Shift+A: Add drift correction keyframe
        if (Keyboard.current[addKeyframeKey].wasPressedThisFrame &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
        {
            AddDriftCorrectionKeyframe();
        }

        // 0 key: Reset timeline to frame 0
        if (Keyboard.current[resetTimelineKey].wasPressedThisFrame)
        {
            ResetTimeline();
        }
    }
    
    [ContextMenu("Toggle Play/Pause")]
    public void TogglePlayPause()
    {
        if (timeline == null) return;
        
        if (timeline.state == PlayState.Playing)
        {
            PauseTimeline();
        }
        else
        {
            PlayTimeline();
        }
    }
    
    [ContextMenu("Play Timeline")]
    public void PlayTimeline()
    {
        if (timeline == null) return;
        
        timeline.Play();
        if (showDebugLogs)
        {
            Debug.Log("Timeline: PLAY");
        }
    }
    
    [ContextMenu("Pause Timeline")]
    public void PauseTimeline()
    {
        if (timeline == null) return;
        
        timeline.Pause();
        if (showDebugLogs)
        {
            Debug.Log("Timeline: PAUSE");
        }
    }
    
    [ContextMenu("Stop Timeline")]
    public void StopTimeline()
    {
        if (timeline == null) return;

        timeline.Stop();
        if (showDebugLogs)
        {
            Debug.Log("Timeline: STOP");
        }
    }

    [ContextMenu("Reset Timeline to Frame 0")]
    public void ResetTimeline()
    {
        TimelineUtil.SeekToTime(0);
        if (showDebugLogs)
        {
            Debug.Log("Timeline: RESET to frame 0");
        }
    }

    /// <summary>
    /// Shift+Aで現在時刻にドリフト補正キーフレームを追加
    /// </summary>
    private void AddDriftCorrectionKeyframe()
    {
        if (timeline == null || bvhPlayableAsset == null)
        {
            Debug.LogWarning("TimelineController: Cannot add keyframe - timeline or BVH asset not assigned");
            return;
        }

        // ドリフト補正データを取得
        BvhPlaybackCorrectionKeyframes driftCorrectionData = bvhPlayableAsset.GetDriftCorrectionData();
        if (driftCorrectionData == null)
        {
            Debug.LogWarning("TimelineController: No drift correction data assigned to BVH asset");
            return;
        }

        // BvhPlayableBehaviourを取得
        BvhPlayableBehaviour bvhBehaviour = bvhPlayableAsset.GetBvhPlayableBehaviour();
        if (bvhBehaviour == null)
        {
            Debug.LogWarning("TimelineController: Cannot find BvhPlayableBehaviour");
            return;
        }

        // 現在の情報を取得
        float currentTime = (float)TimelineUtil.GetCurrentTimelineTime();
        int currentFrame = bvhBehaviour.GetCurrentFrame();

        // currentFrameが未初期化（-1）の場合は、時刻から計算
        if (currentFrame == -1)
        {
            BvhData bvhData = bvhPlayableAsset.GetBvhData();
            float bvhFrameRate = bvhData != null ? bvhData.FrameRate : 30f;
            currentFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
        }

        // 現在の補正値を取得（補間されたキーフレーム値）
        Vector3 currentCorrectionAtTime = driftCorrectionData.GetAnchorPositionAtTime(currentTime);

        // キーフレームを追加（補正値を保存）
        driftCorrectionData.AddKeyframe(currentTime, currentFrame, currentCorrectionAtTime);

        if (showDebugLogs)
        {
            Debug.Log($"TimelineController: Keyframe added at time={currentTime}s, frame={currentFrame}, pos={currentCorrectionAtTime}");
        }
    }

    void UpdatePlaybackStatus()
    {
        bool isPlayingNow = (timeline.state == PlayState.Playing);
        
        // Log state changes
        if (isPlayingNow != wasPlayingLastFrame && showDebugLogs)
        {
            Debug.Log($"Timeline state changed: {(isPlayingNow ? "PLAYING" : "PAUSED/STOPPED")}");
        }
        
        wasPlayingLastFrame = isPlayingNow;
    }
    
    // Public getters for other scripts
    public bool IsPlaying => timeline != null && timeline.state == PlayState.Playing;
    public double CurrentTime => timeline != null ? timeline.time : 0;
    public double Duration => timeline != null && timeline.playableAsset != null ? timeline.playableAsset.duration : 0;

    /// <summary>
    /// Set timeline duration based on total frame count and FPS (for PointCloud)
    /// BVH clips use their own duration based on BVH file data
    /// </summary>
    public void SetDuration(int totalFrameCount, int fps)
    {
        if (timeline == null || timeline.playableAsset == null)
        {
            Debug.LogError("TimelineController: Cannot set duration - timeline or playable asset is null");
            return;
        }

        if (fps <= 0)
        {
            Debug.LogError($"TimelineController: Invalid FPS ({fps}). Cannot set duration.");
            return;
        }

        // Calculate duration in seconds for PointCloud
        double pointCloudDurationInSeconds = (double)totalFrameCount / fps;

        // Cast to TimelineAsset
        TimelineAsset timelineAsset = timeline.playableAsset as TimelineAsset;
        if (timelineAsset == null)
        {
            Debug.LogError("TimelineController: PlayableAsset is not a TimelineAsset");
            return;
        }

        // Track the maximum duration across all clips
        double maxDuration = pointCloudDurationInSeconds;

        // Find clips and set their duration appropriately
        bool foundPointCloudClip = false;
        bool foundBvhClip = false;

        foreach (var track in timelineAsset.GetOutputTracks())
        {
            foreach (var clip in track.GetClips())
            {
                // PointCloud clips use the provided duration
                if (clip.asset is PointCloudPlayableAsset)
                {
                    clip.duration = pointCloudDurationInSeconds;
                    foundPointCloudClip = true;
                }
                // BVH clips use their own calculated duration from BVH file data
                else if (clip.asset is BvhPlayableAsset bvhAsset)
                {
                    double bvhDuration = bvhAsset.GetBvhDuration();
                    if (bvhDuration > 0)
                    {
                        clip.duration = bvhDuration;
                        foundBvhClip = true;

                        // Track maximum duration
                        if (bvhDuration > maxDuration)
                        {
                            maxDuration = bvhDuration;
                        }
                    }
                }

#if UNITY_EDITOR
                // Mark timeline dirty in editor
                UnityEditor.EditorUtility.SetDirty(timelineAsset);
#endif
            }
        }

        if (!foundPointCloudClip && showDebugLogs)
        {
            Debug.LogWarning("TimelineController: No PointCloudPlayableAsset clips found in timeline");
        }

        if (!foundBvhClip && showDebugLogs)
        {
            Debug.LogWarning("TimelineController: No BvhPlayableAsset clips found in timeline");
        }

        // Set timeline asset duration to the maximum clip duration
        timelineAsset.durationMode = TimelineAsset.DurationMode.FixedLength;
        timelineAsset.fixedDuration = maxDuration;
        Debug.Log($"TimelineController: Set Timeline total duration to {maxDuration}s");
    }
}