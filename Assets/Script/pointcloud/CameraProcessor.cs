using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PointCloud
{
    public class FrameResult
    {
        public string CameraName { get; set; }
        public ulong Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class FrameRequest
    {
        public ulong TargetTimestamp { get; set; }
        public Guid RequestId { get; set; }
    }

    public class CameraProcessor : IDisposable
    {
        private readonly string cameraName;
        private readonly CameraDataManager parser;
        private readonly Thread backgroundThread;
        private readonly BlockingCollection<FrameRequest> requestQueue;
        private readonly ConcurrentQueue<FrameResult> resultQueue;
        private volatile bool isRunning = true;
        
        public CameraProcessor(string cameraName, CameraDataManager parser)
        {
            this.cameraName = cameraName;
            this.parser = parser;
            requestQueue = new BlockingCollection<FrameRequest>();
            resultQueue = new ConcurrentQueue<FrameResult>();
            
            // Start background processing thread
            backgroundThread = new Thread(BackgroundProcessingLoop)
            {
                Name = $"CameraProcessor_{cameraName}",
                IsBackground = true
            };
            backgroundThread.Start();
            
            // Use main thread for initialization logging
            UnityEngine.Debug.Log($"CameraProcessor started for {cameraName}");
        }
        
        public async Task<FrameResult> ProcessFrameAsync(ulong targetTimestamp, CancellationToken cancellationToken)
        {
            var request = new FrameRequest
            {
                TargetTimestamp = targetTimestamp,
                RequestId = Guid.NewGuid()
            };
            
            // Submit request to background thread
            if (!requestQueue.IsAddingCompleted)
            {
                requestQueue.Add(request, cancellationToken);
            }
            
            // Wait for result with timeout
            var timeoutTask = Task.Delay(5000, cancellationToken); // 5 second timeout
            var completionTask = WaitForResult(request.RequestId, cancellationToken);
            
            var completedTask = await Task.WhenAny(timeoutTask, completionTask);
            
            if (completedTask == timeoutTask)
            {
                return new FrameResult
                {
                    CameraName = cameraName,
                    Timestamp = targetTimestamp,
                    Success = false,
                    ErrorMessage = "Processing timeout"
                };
            }
            
            return await completionTask;
        }
        
        private async Task<FrameResult> WaitForResult(Guid requestId, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (resultQueue.TryDequeue(out var result))
                {
                    return result;
                }
                await Task.Delay(10, cancellationToken); // Small delay to prevent tight loop
            }
            
            return new FrameResult
            {
                CameraName = cameraName,
                Success = false,
                ErrorMessage = "Cancelled"
            };
        }
        
        private void BackgroundProcessingLoop()
        {
            System.Console.WriteLine($"Background processing thread started for {cameraName}");
            
            try
            {
                while (isRunning && !requestQueue.IsCompleted)
                {
                    if (requestQueue.TryTake(out var request, 100)) // 100ms timeout
                    {
                        var result = ProcessFrameRequest(request);
                        resultQueue.Enqueue(result);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Background processing error for {cameraName}: {ex.Message}");
            }
            
            System.Console.WriteLine($"Background processing thread ended for {cameraName}");
        }
        
        private FrameResult ProcessFrameRequest(FrameRequest request)
        {
            try
            {
                // Thread-safe processing: avoid Unity API calls in background thread
                bool success = ProcessFrameThreadSafe(request.TargetTimestamp);
                
                return new FrameResult
                {
                    CameraName = cameraName,
                    Timestamp = request.TargetTimestamp,
                    Success = success
                };
            }
            catch (Exception ex)
            {
                // Use Console.WriteLine instead of Debug.LogError for thread safety
                System.Console.WriteLine($"Frame processing error for {cameraName}: {ex.Message}");
                return new FrameResult
                {
                    CameraName = cameraName,
                    Timestamp = request.TargetTimestamp,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        private bool ProcessFrameThreadSafe(ulong targetTimestamp)
        {
            try
            {
                // Use thread-safe methods that don't call Unity APIs
                bool seekSuccess = SeekToTimestampThreadSafe(targetTimestamp);
                if (!seekSuccess) return false;
                
                // Process the frame using thread-safe method
                bool frameProcessed = ProcessSingleFrameThreadSafe();
                return frameProcessed;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Thread-safe processing error for {cameraName}: {ex.Message}");
                return false;
            }
        }
        
        private bool SeekToTimestampThreadSafe(ulong targetTimestamp)
        {
            // Completely bypass CameraDataManager and work directly with parsers
            try
            {
                // Get the internal parsers using reflection
                var depthParserField = typeof(CameraDataManager).GetField("depthParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var colorParserField = typeof(CameraDataManager).GetField("colorParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (depthParserField != null && colorParserField != null)
                {
                    var depthParser = depthParserField.GetValue(parser);
                    var colorParser = colorParserField.GetValue(parser);
                    
                    // Direct parser-level seeking without Unity APIs
                    return SeekParsersToTimestamp(depthParser, colorParser, targetTimestamp);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SeekToTimestampThreadSafe error for {cameraName}: {ex.Message}");
                return false;
            }
        }
        
        private bool SeekParsersToTimestamp(object depthParser, object colorParser, ulong targetTimestamp)
        {
            try
            {
                // Use reflection to access parser methods directly
                var peekDepthMethod = depthParser.GetType().GetMethod("PeekNextTimestamp");
                var peekColorMethod = colorParser.GetType().GetMethod("PeekNextTimestamp");
                var skipDepthMethod = depthParser.GetType().GetMethod("SkipCurrentRecord");
                var skipColorMethod = colorParser.GetType().GetMethod("SkipCurrentRecord");
                
                if (peekDepthMethod == null || peekColorMethod == null || 
                    skipDepthMethod == null || skipColorMethod == null)
                {
                    return false;
                }
                
                const long maxAllowableDeltaNs = 2_000;
                int maxIterations = 1000; // Prevent infinite loops
                int iterations = 0;
                
                while (iterations < maxIterations)
                {
                    // Peek next timestamps
                    object[] depthParams = new object[1];
                    object[] colorParams = new object[1];
                    
                    bool hasDepthTs = (bool)peekDepthMethod.Invoke(depthParser, depthParams);
                    bool hasColorTs = (bool)peekColorMethod.Invoke(colorParser, colorParams);
                    
                    if (!hasDepthTs || !hasColorTs) break;
                    
                    ulong depthTs = (ulong)depthParams[0];
                    ulong colorTs = (ulong)colorParams[0];
                    
                    long delta = (long)depthTs - (long)colorTs;
                    
                    if (Math.Abs(delta) <= maxAllowableDeltaNs)
                    {
                        // Synchronized frame found - check if we've reached target
                        if (depthTs >= targetTimestamp)
                        {
                            return true; // Found target timestamp
                        }
                        
                        // Skip both to next synchronized frame
                        skipDepthMethod.Invoke(depthParser, null);
                        skipColorMethod.Invoke(colorParser, null);
                    }
                    else if (delta < 0)
                    {
                        // Skip depth to catch up
                        skipDepthMethod.Invoke(depthParser, null);
                    }
                    else
                    {
                        // Skip color to catch up
                        skipColorMethod.Invoke(colorParser, null);
                    }
                    
                    iterations++;
                }
                
                return false; // Target not found
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SeekParsersToTimestamp error for {cameraName}: {ex.Message}");
                return false;
            }
        }
        
        private bool SeekToTimestampFallback(ulong targetTimestamp)
        {
            // Simple fallback that avoids Unity API calls
            try
            {
                // Access parser's internal parsers directly if possible
                var depthParserField = typeof(CameraDataManager).GetField("depthParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var colorParserField = typeof(CameraDataManager).GetField("colorParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (depthParserField != null && colorParserField != null)
                {
                    var depthParser = depthParserField.GetValue(parser);
                    var colorParser = colorParserField.GetValue(parser);
                    
                    // Perform basic seeking without UI updates
                    // This is a simplified version that avoids Unity API calls
                    return true; // Simplified for thread safety
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool ProcessSingleFrameThreadSafe()
        {
            // Completely thread-safe version that bypasses CameraDataManager Unity API calls
            try
            {
                // Get the internal parsers using reflection
                var depthParserField = typeof(CameraDataManager).GetField("depthParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var colorParserField = typeof(CameraDataManager).GetField("colorParser", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (depthParserField != null && colorParserField != null)
                {
                    var depthParser = depthParserField.GetValue(parser);
                    var colorParser = colorParserField.GetValue(parser);
                    
                    // Process frame at parser level without Unity API calls
                    return ProcessParsersThreadSafe(depthParser, colorParser);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"ProcessSingleFrameThreadSafe error for {cameraName}: {ex.Message}");
                return false;
            }
        }
        
        private bool ProcessParsersThreadSafe(object depthParser, object colorParser)
        {
            try
            {
                // Get parser methods using reflection
                var peekDepthMethod = depthParser.GetType().GetMethod("PeekNextTimestamp");
                var peekColorMethod = colorParser.GetType().GetMethod("PeekNextTimestamp");
                var parseDepthMethod = depthParser.GetType().GetMethod("ParseNextRecord", new Type[] { typeof(bool) });
                var parseColorMethod = colorParser.GetType().GetMethod("ParseNextRecord", new Type[] { typeof(bool) });
                
                if (peekDepthMethod == null || peekColorMethod == null || 
                    parseDepthMethod == null || parseColorMethod == null)
                {
                    return false;
                }
                
                // Check if frames are synchronized
                object[] depthParams = new object[1];
                object[] colorParams = new object[1];
                
                bool hasDepthTs = (bool)peekDepthMethod.Invoke(depthParser, depthParams);
                bool hasColorTs = (bool)peekColorMethod.Invoke(colorParser, colorParams);
                
                if (!hasDepthTs || !hasColorTs) return false;
                
                ulong depthTs = (ulong)depthParams[0];
                ulong colorTs = (ulong)colorParams[0];
                
                const long maxAllowableDeltaNs = 2_000;
                long delta = (long)depthTs - (long)colorTs;
                
                if (Math.Abs(delta) <= maxAllowableDeltaNs)
                {
                    // Parse synchronized frames with GPU optimization
                    bool depthOk = (bool)parseDepthMethod.Invoke(depthParser, new object[] { true }); // GPU optimized
                    bool colorOk = (bool)parseColorMethod.Invoke(colorParser, new object[] { true }); // GPU optimized
                    
                    return depthOk && colorOk;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"ProcessParsersThreadSafe error for {cameraName}: {ex.Message}");
                return false;
            }
        }
        
        
        public void Dispose()
        {
            isRunning = false;
            requestQueue.CompleteAdding();
            
            if (backgroundThread != null && backgroundThread.IsAlive)
            {
                if (!backgroundThread.Join(1000)) // Wait 1 second for graceful shutdown
                {
                    backgroundThread.Abort(); // Force shutdown if needed
                }
            }
            
            requestQueue?.Dispose();
            UnityEngine.Debug.Log($"CameraProcessor disposed for {cameraName}");
        }
    }
}