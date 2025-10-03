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
    /// Set timeline duration based on total frame count and FPS
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

        // Calculate duration in seconds
        double durationInSeconds = (double)totalFrameCount / fps;

        // Cast to TimelineAsset
        TimelineAsset timelineAsset = timeline.playableAsset as TimelineAsset;
        if (timelineAsset == null)
        {
            Debug.LogError("TimelineController: PlayableAsset is not a TimelineAsset");
            return;
        }

        // Find PointCloudPlayableAsset clips and set their duration
        bool foundClip = false;
        foreach (var track in timelineAsset.GetOutputTracks())
        {
            foreach (var clip in track.GetClips())
            {
                if (clip.asset is PointCloudPlayableAsset)
                {
                    clip.duration = durationInSeconds;
                    foundClip = true;

#if UNITY_EDITOR
                    // Mark timeline dirty in editor
                    UnityEditor.EditorUtility.SetDirty(timelineAsset);
#endif
                }
            }
        }

        if (!foundClip && showDebugLogs)
        {
            Debug.LogWarning("TimelineController: No PointCloudPlayableAsset clips found in timeline");
        }

        // Also set timeline asset duration
        timelineAsset.durationMode = TimelineAsset.DurationMode.FixedLength;
        timelineAsset.fixedDuration = durationInSeconds;
    }
}