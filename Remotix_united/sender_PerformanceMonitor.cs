using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace sender
{
    /// <summary>
    /// Monitors performance metrics like FPS, bitrate, latency, and packet loss.
    /// Used by both sender and receiver to track stream quality.
    /// </summary>
    public class PerformanceMonitor
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Queue<long> _frameTimes = new Queue<long>();
        private readonly Queue<FrameData> _frameHistory = new Queue<FrameData>();

        private const int FPS_SAMPLE_SIZE = 60; // Track last 60 frames for FPS calculation
        private const int BITRATE_WINDOW_MS = 1000; // Calculate bitrate over 1 second window

        private int _totalFrames = 0;
        private long _totalBytes = 0;
        private int _droppedFrames = 0;
        private long _lastFrameTime = 0;

        // Current metrics
        public double CurrentFPS { get; private set; }
        public double CurrentBitrateMbps { get; private set; }
        public double AverageLatencyMs { get; private set; }
        public int DroppedFrames => _droppedFrames;
        public int TotalFrames => _totalFrames;
        public double PacketLossPercent { get; private set; }

        // Performance quality assessment
        public StreamQuality Quality { get; private set; } = StreamQuality.Good;

        public PerformanceMonitor()
        {
            _stopwatch.Start();
        }

        public MotionLevel CurrentMotionLevel { get; set; } = MotionLevel.Unknown;

        /// <summary>
        /// Record a new frame for metrics calculation
        /// </summary>
        public void RecordFrame(int frameSize, long? latencyMs = null)
        {
            _totalFrames++;
            _totalBytes += frameSize;

            long currentTime = _stopwatch.ElapsedMilliseconds;

            // Track frame timing for FPS
            _frameTimes.Enqueue(currentTime);
            if (_frameTimes.Count > FPS_SAMPLE_SIZE)
            {
                _frameTimes.Dequeue();
            }

            // Track frame data for bitrate calculation
            var frameData = new FrameData
            {
                Timestamp = currentTime,
                Size = frameSize,
                Latency = latencyMs
            };
            _frameHistory.Enqueue(frameData);

            // Remove old frames outside the bitrate window
            while (_frameHistory.Count > 0 &&
                   currentTime - _frameHistory.Peek().Timestamp > BITRATE_WINDOW_MS)
            {
                _frameHistory.Dequeue();
            }

            // Calculate metrics
            CalculateFPS();
            CalculateBitrate();

            if (latencyMs.HasValue)
            {
                CalculateAverageLatency();
            }

            // Assess quality
            AssessQuality();

            _lastFrameTime = currentTime;
        }

        /// <summary>
        /// Record a dropped frame
        /// </summary>
        public void RecordDroppedFrame()
        {
            _droppedFrames++;
            CalculatePacketLoss();
            AssessQuality();
        }

        /// <summary>
        /// Calculate current FPS based on recent frames
        /// </summary>
        private void CalculateFPS()
        {
            if (_frameTimes.Count < 2)
            {
                CurrentFPS = 0;
                return;
            }

            long timeSpan = _frameTimes.Last() - _frameTimes.First();
            if (timeSpan > 0)
            {
                CurrentFPS = (_frameTimes.Count - 1) * 1000.0 / timeSpan;
            }
        }

        /// <summary>
        /// Calculate current bitrate in Mbps
        /// </summary>
        private void CalculateBitrate()
        {
            if (_frameHistory.Count == 0)
            {
                CurrentBitrateMbps = 0;
                return;
            }

            long totalBytesInWindow = _frameHistory.Sum(f => f.Size);
            long timeSpan = _stopwatch.ElapsedMilliseconds - _frameHistory.First().Timestamp;

            if (timeSpan > 0)
            {
                // Convert to Mbps: (bytes * 8 bits/byte) / (1,000,000 bits/Mb) / (timeSpan_ms / 1000)
                CurrentBitrateMbps = (totalBytesInWindow * 8.0) / (timeSpan * 1000.0);
            }
        }

        /// <summary>
        /// Calculate average latency from recent frames
        /// </summary>
        private void CalculateAverageLatency()
        {
            var latencies = _frameHistory
                .Where(f => f.Latency.HasValue)
                .Select(f => f.Latency.Value)
                .ToList();

            if (latencies.Count > 0)
            {
                AverageLatencyMs = latencies.Average();
            }
        }

        /// <summary>
        /// Calculate packet loss percentage
        /// </summary>
        private void CalculatePacketLoss()
        {
            int totalAttempts = _totalFrames + _droppedFrames;
            if (totalAttempts > 0)
            {
                PacketLossPercent = (_droppedFrames * 100.0) / totalAttempts;
            }
        }

        /// <summary>
        /// Assess overall stream quality based on metrics
        /// </summary>
        private void AssessQuality()
        {
            // ✅ סף FPS דינמי לפי תנועה
            double minAcceptableFPS = CurrentMotionLevel switch
            {
                MotionLevel.VeryHigh => 20,  // תנועה גבוהה מאוד → צריך 20+ FPS
                MotionLevel.High => 18,      // תנועה גבוהה → צריך 18+ FPS
                MotionLevel.Medium => 15,    // תנועה בינונית → 15+ FPS
                MotionLevel.Low => 12,       // תנועה נמוכה → 12+ FPS בסדר
                MotionLevel.VeryLow => 10,   // כמעט סטטי → 10+ FPS מספיק
                _ => 15
            };

            // Excellent: FPS טוב, low latency, no packet loss
            if (CurrentFPS >= minAcceptableFPS + 5 && PacketLossPercent < 1 && AverageLatencyMs < 100)
            {
                Quality = StreamQuality.Excellent;
            }
            // Good: FPS מעל המינימום
            else if (CurrentFPS >= minAcceptableFPS && PacketLossPercent < 3 && AverageLatencyMs < 200)
            {
                Quality = StreamQuality.Good;
            }
            // Fair: FPS קרוב למינימום
            else if (CurrentFPS >= minAcceptableFPS - 3 && PacketLossPercent < 5 && AverageLatencyMs < 300)
            {
                Quality = StreamQuality.Fair;
            }
            // Poor: FPS מתחת למינימום
            else
            {
                Quality = StreamQuality.Poor;
            }
        }

        /// <summary>
        /// Get expected minimum FPS based on motion level
        /// High motion = lower FPS is acceptable
        /// </summary>
        private double GetExpectedMinFPS()
        {
            return CurrentMotionLevel switch
            {
                MotionLevel.VeryHigh => 15,  // הרבה תנועה → 15 FPS זה OK
                MotionLevel.High => 18,      // תנועה גבוהה → 18 FPS זה OK
                MotionLevel.Medium => 20,    // תנועה בינונית → 20 FPS
                MotionLevel.Low => 23,       // תנועה נמוכה → 23 FPS
                MotionLevel.VeryLow => 25,   // כמעט סטטי → 25 FPS
                _ => 20                      // ברירת מחדל
            };
        }

        /// <summary>
        /// Get a formatted status string with all metrics
        /// </summary>
        public string GetStatusString()
        {
            return $"FPS: {CurrentFPS:F1} | Bitrate: {CurrentBitrateMbps:F2} Mbps | " +
                   $"Latency: {AverageLatencyMs:F0}ms | Dropped: {_droppedFrames}/{_totalFrames} | " +
                   $"Quality: {Quality}";
        }

        /// <summary>
        /// Get quality indicator emoji
        /// </summary>
        public string GetQualityIndicator()
        {
            return Quality switch
            {
                StreamQuality.Excellent => "🟢",
                StreamQuality.Good => "🟡",
                StreamQuality.Fair => "🟠",
                StreamQuality.Poor => "🔴",
                _ => "⚪"
            };
        }

        /// <summary>
        /// Reset all metrics
        /// </summary>
        public void Reset()
        {
            _frameTimes.Clear();
            _frameHistory.Clear();
            _totalFrames = 0;
            _totalBytes = 0;
            _droppedFrames = 0;
            _lastFrameTime = 0;
            CurrentFPS = 0;
            CurrentBitrateMbps = 0;
            AverageLatencyMs = 0;
            PacketLossPercent = 0;
            Quality = StreamQuality.Good;
            _stopwatch.Restart();
        }

        private class FrameData
        {
            public long Timestamp { get; set; }
            public int Size { get; set; }
            public long? Latency { get; set; }
        }
    }

    /// <summary>
    /// Stream quality levels
    /// </summary>
    public enum StreamQuality
    {
        Excellent,
        Good,
        Fair,
        Poor
    }
}


