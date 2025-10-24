using UnityEngine;

/// <summary>
/// Component to load and play BVH motion capture data on a GameObject hierarchy
/// </summary>
public class BvhPlayer : MonoBehaviour
{
    [Header("BVH File Settings")]
    [Tooltip("Path to the BVH file to load")]
    public string bvhFilePath;

    [Tooltip("Load BVH file on start")]
    public bool loadOnStart = true;

    [Header("Playback Settings")]
    [Tooltip("Play animation automatically on start")]
    public bool autoPlay = false;

    [Tooltip("Loop the animation")]
    public bool loop = true;

    [Tooltip("Playback speed multiplier")]
    [Range(0.1f, 5f)]
    public float playbackSpeed = 1f;

    [Header("Debug")]
    [Tooltip("Show debug information")]
    public bool showDebug = false;

    private BvhData bvhData;
    private bool isPlaying = false;
    private float currentTime = 0f;

    void Start()
    {
        if (loadOnStart && !string.IsNullOrEmpty(bvhFilePath))
        {
            LoadBvhFile(bvhFilePath);

            if (autoPlay && bvhData != null)
            {
                Play();
            }
        }
    }

    void Update()
    {
        if (isPlaying && bvhData != null)
        {
            currentTime += Time.deltaTime * playbackSpeed;

            if (loop && currentTime >= bvhData.Duration)
            {
                currentTime = 0f;
            }
            else if (!loop && currentTime >= bvhData.Duration)
            {
                currentTime = bvhData.Duration;
                isPlaying = false;
            }

            ApplyMotion(currentTime);
        }
    }

    /// <summary>
    /// Load BVH file from path
    /// </summary>
    public bool LoadBvhFile(string filePath)
    {
        bvhData = BvhImporter.ImportFromBVH(filePath);

        if (bvhData != null)
        {
            Debug.Log($"BVH file loaded successfully:\n{bvhData.GetSummary()}");
            currentTime = 0f;
            return true;
        }
        else
        {
            Debug.LogError($"Failed to load BVH file: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Start playing the animation
    /// </summary>
    public void Play()
    {
        if (bvhData != null)
        {
            isPlaying = true;
        }
        else
        {
            Debug.LogWarning("No BVH data loaded. Call LoadBvhFile() first.");
        }
    }

    /// <summary>
    /// Pause the animation
    /// </summary>
    public void Pause()
    {
        isPlaying = false;
    }

    /// <summary>
    /// Stop and reset the animation
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        currentTime = 0f;
        if (bvhData != null)
        {
            ApplyMotion(0f);
        }
    }

    /// <summary>
    /// Seek to a specific time in the animation
    /// </summary>
    public void SeekToTime(float time)
    {
        if (bvhData != null)
        {
            currentTime = Mathf.Clamp(time, 0f, bvhData.Duration);
            ApplyMotion(currentTime);
        }
    }

    /// <summary>
    /// Seek to a specific frame
    /// </summary>
    public void SeekToFrame(int frameIndex)
    {
        if (bvhData != null)
        {
            frameIndex = Mathf.Clamp(frameIndex, 0, bvhData.FrameCount - 1);
            currentTime = frameIndex * bvhData.FrameTime;
            ApplyMotion(currentTime);
        }
    }

    /// <summary>
    /// Apply motion at the specified time
    /// </summary>
    private void ApplyMotion(float time)
    {
        if (bvhData == null) return;

        float[] frameData = bvhData.GetFrameAtTime(time);
        if (frameData != null)
        {
            int channelIndex = 0;
            ApplyJointMotion(bvhData.RootJoint, transform, frameData, ref channelIndex);
        }
    }

    /// <summary>
    /// Recursively apply motion to joint hierarchy
    /// </summary>
    private void ApplyJointMotion(BvhJoint joint, Transform targetTransform, float[] frameData, ref int channelIndex)
    {
        if (joint.IsEndSite) return;

        Vector3 position = joint.Offset;
        Vector3 rotation = Vector3.zero;

        // Read channel data
        foreach (string channel in joint.Channels)
        {
            if (channelIndex >= frameData.Length) break;

            float value = frameData[channelIndex];
            channelIndex++;

            switch (channel.ToUpper())
            {
                case "XPOSITION":
                    position.x = value;
                    break;
                case "YPOSITION":
                    position.y = value;
                    break;
                case "ZPOSITION":
                    position.z = value;
                    break;
                case "XROTATION":
                    rotation.x = value;
                    break;
                case "YROTATION":
                    rotation.y = value;
                    break;
                case "ZROTATION":
                    rotation.z = value;
                    break;
            }
        }

        targetTransform.localPosition = position;
        targetTransform.localRotation = Quaternion.Euler(rotation);

        // Apply to children
        foreach (var childJoint in joint.Children)
        {
            if (childJoint.IsEndSite) continue;

            Transform childTransform = targetTransform.Find(childJoint.Name);
            if (childTransform != null)
            {
                ApplyJointMotion(childJoint, childTransform, frameData, ref channelIndex);
            }
            else
            {
                // Create child transform if it doesn't exist
                GameObject childObj = new GameObject(childJoint.Name);
                childObj.transform.SetParent(targetTransform);
                childObj.transform.localPosition = childJoint.Offset;
                childObj.transform.localRotation = Quaternion.identity;

                ApplyJointMotion(childJoint, childObj.transform, frameData, ref channelIndex);
            }
        }
    }

    void OnGUI()
    {
        if (showDebug && bvhData != null)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Box($"BVH Player Debug\n" +
                         $"File: {System.IO.Path.GetFileName(bvhFilePath)}\n" +
                         $"Playing: {isPlaying}\n" +
                         $"Time: {currentTime:F2}s / {bvhData.Duration:F2}s\n" +
                         $"Frame: {Mathf.FloorToInt(currentTime / bvhData.FrameTime)} / {bvhData.FrameCount}\n" +
                         $"FPS: {bvhData.FrameRate:F2}");

            if (GUILayout.Button(isPlaying ? "Pause" : "Play"))
            {
                if (isPlaying) Pause();
                else Play();
            }

            if (GUILayout.Button("Stop"))
            {
                Stop();
            }

            GUILayout.EndArea();
        }
    }

    // Property accessors
    public bool IsPlaying => isPlaying;
    public float CurrentTime => currentTime;
    public BvhData Data => bvhData;
    public int CurrentFrame => bvhData != null ? Mathf.FloorToInt(currentTime / bvhData.FrameTime) : 0;
}
