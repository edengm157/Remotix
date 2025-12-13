using System;

namespace sender
{
    /// <summary>
    /// Automatically adjusts encoding parameters based on performance metrics
    /// to maintain optimal stream quality under varying network conditions.
    /// SMART VERSION: Only reacts to significant quality changes (Excellent or Poor)
    /// </summary>
    public class AdaptiveQualityController
    {
        private readonly PerformanceMonitor _monitor;

        // Current encoding parameters
        public int TargetBitrate { get; private set; } = 5000000; // 5 Mbps default
        public float TargetFPS { get; private set; } = 15f;
        public QualityPreset CurrentPreset { get; private set; } = QualityPreset.High;

        // Adaptation settings - FAST RESPONSE with hysteresis protection
        private const int ADAPTATION_INTERVAL_MS = 1000; // Check every 1 second
        private long _lastAdaptationTime = 0;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();

        // Hysteresis: Require consecutive readings before changing
        private int _excellentCount = 0;
        private int _poorCount = 0;
        private const int EXCELLENT_THRESHOLD = 3; // Need 3 consecutive excellent readings (3 seconds)
        private const int POOR_THRESHOLD = 2; // Need 2 consecutive poor readings (2 seconds)

        // Bitrate bounds (in bps)
        private const int MIN_BITRATE = 1000000;  // 1 Mbps
        private const int MAX_BITRATE = 10000000; // 10 Mbps

        // FPS bounds
        private const float MIN_FPS = 10f;
        private const float MAX_FPS = 30f;

        public event Action<int, float, QualityPreset> QualityAdjusted;

        public AdaptiveQualityController(PerformanceMonitor monitor)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _stopwatch.Start();
        }

        /// <summary>
        /// Check if quality adjustment is needed and apply changes.
        /// SMART VERSION: Only reacts to sustained Excellent or Poor quality.
        /// </summary>
        public bool UpdateQuality()
        {
            long currentTime = _stopwatch.ElapsedMilliseconds;

            // Only adapt at specified intervals
            if (currentTime - _lastAdaptationTime < ADAPTATION_INTERVAL_MS)
            {
                return false;
            }

            _lastAdaptationTime = currentTime;

            var quality = _monitor.Quality;
            var currentFPS = _monitor.CurrentFPS;
            var packetLoss = _monitor.PacketLossPercent;

            bool changed = false;

            // SMART ADAPTATION: Only react to extremes
            switch (quality)
            {
                case StreamQuality.Excellent:
                    // Increment counter
                    _excellentCount++;
                    _poorCount = 0; // Reset poor counter

                    // Only increase quality after sustained excellence
                    if (_excellentCount >= EXCELLENT_THRESHOLD)
                    {
                        changed = IncreaseQuality();
                        if (changed)
                        {
                            _excellentCount = 0; // Reset after adjustment
                            LogAdaptation("Sustained excellent quality detected", "Increasing quality");
                        }
                    }
                    break;

                case StreamQuality.Good:
                case StreamQuality.Fair:
                    // STABLE ZONE - Do nothing, just reset counters
                    _excellentCount = 0;
                    _poorCount = 0;
                    // Quality is acceptable, no need to change
                    break;

                case StreamQuality.Poor:
                    // Increment counter
                    _poorCount++;
                    _excellentCount = 0; // Reset excellent counter

                    // React faster to poor quality (2 checks instead of 3)
                    if (_poorCount >= POOR_THRESHOLD)
                    {
                        changed = DecreaseQuality(moderate: false);
                        if (changed)
                        {
                            _poorCount = 0; // Reset after adjustment
                            LogAdaptation("Sustained poor quality detected", "Reducing quality aggressively");
                        }
                    }
                    break;
            }

            // EMERGENCY HANDLING: React immediately to critical issues
            // (These bypass the hysteresis counters)
            if (packetLoss > 10)
            {
                // Critical packet loss - immediate action
                changed |= ReduceBitrate(0.6); // Reduce to 60% immediately
                LogAdaptation($"Critical packet loss: {packetLoss:F1}%", "Emergency bitrate reduction");
                _poorCount = 0; // Reset to avoid double-reduction
            }
            else if (currentFPS < 8 && TargetFPS > MIN_FPS)
            {
                // Critical FPS drop - immediate action
                changed |= ReduceFPS(0.7); // Reduce to 70%
                LogAdaptation($"Critical FPS drop: {currentFPS:F1}", "Emergency FPS reduction");
                _poorCount = 0; // Reset to avoid double-reduction
            }

            if (changed)
            {
                UpdatePreset();
                QualityAdjusted?.Invoke(TargetBitrate, TargetFPS, CurrentPreset);
            }

            return changed;
        }

        /// <summary>
        /// Increase encoding quality (bitrate/FPS) - called only after sustained excellent quality
        /// </summary>
        private bool IncreaseQuality()
        {
            bool changed = false;

            // Conservative increase: 15% (slower growth)
            if (TargetBitrate < MAX_BITRATE)
            {
                TargetBitrate = Math.Min(MAX_BITRATE, (int)(TargetBitrate * 1.15));
                changed = true;
            }

            // Conservative FPS increase: 10%
            if (TargetFPS < MAX_FPS)
            {
                TargetFPS = Math.Min(MAX_FPS, TargetFPS * 1.1f);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Decrease encoding quality (bitrate/FPS) - called when sustained poor quality detected
        /// </summary>
        private bool DecreaseQuality(bool moderate)
        {
            bool changed = false;
            double reduction = moderate ? 0.85 : 0.65; // 15% or 35% reduction (more aggressive)

            // Reduce bitrate
            if (TargetBitrate > MIN_BITRATE)
            {
                TargetBitrate = Math.Max(MIN_BITRATE, (int)(TargetBitrate * reduction));
                changed = true;
            }

            // Reduce FPS
            if (TargetFPS > MIN_FPS)
            {
                TargetFPS = Math.Max(MIN_FPS, (float)(TargetFPS * reduction));
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Reduce bitrate by a specific factor
        /// </summary>
        private bool ReduceBitrate(double factor)
        {
            int newBitrate = Math.Max(MIN_BITRATE, (int)(TargetBitrate * factor));
            if (newBitrate != TargetBitrate)
            {
                TargetBitrate = newBitrate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reduce FPS by a specific factor
        /// </summary>
        private bool ReduceFPS(double factor)
        {
            float newFPS = Math.Max(MIN_FPS, (float)(TargetFPS * factor));
            if (Math.Abs(newFPS - TargetFPS) > 0.1f)
            {
                TargetFPS = newFPS;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update the current quality preset based on parameters
        /// </summary>
        private void UpdatePreset()
        {
            if (TargetBitrate >= 7000000 && TargetFPS >= 25)
            {
                CurrentPreset = QualityPreset.Ultra;
            }
            else if (TargetBitrate >= 5000000 && TargetFPS >= 20)
            {
                CurrentPreset = QualityPreset.High;
            }
            else if (TargetBitrate >= 3000000 && TargetFPS >= 15)
            {
                CurrentPreset = QualityPreset.Medium;
            }
            else if (TargetBitrate >= 1500000 && TargetFPS >= 12)
            {
                CurrentPreset = QualityPreset.Low;
            }
            else
            {
                CurrentPreset = QualityPreset.VeryLow;
            }
        }

        /// <summary>
        /// Manually set a quality preset
        /// </summary>
        public void SetPreset(QualityPreset preset)
        {
            CurrentPreset = preset;

            // Reset hysteresis counters when manually changing
            _excellentCount = 0;
            _poorCount = 0;

            switch (preset)
            {
                case QualityPreset.Ultra:
                    TargetBitrate = 8000000;
                    TargetFPS = 30f;
                    break;
                case QualityPreset.High:
                    TargetBitrate = 5000000;
                    TargetFPS = 20f;
                    break;
                case QualityPreset.Medium:
                    TargetBitrate = 3000000;
                    TargetFPS = 15f;
                    break;
                case QualityPreset.Low:
                    TargetBitrate = 1500000;
                    TargetFPS = 12f;
                    break;
                case QualityPreset.VeryLow:
                    TargetBitrate = 1000000;
                    TargetFPS = 10f;
                    break;
            }

            QualityAdjusted?.Invoke(TargetBitrate, TargetFPS, CurrentPreset);
        }

        /// <summary>
        /// Get a description of current settings
        /// </summary>
        public string GetSettingsDescription()
        {
            return $"Preset: {CurrentPreset} | Target: {TargetBitrate / 1000000.0:F1} Mbps @ {TargetFPS:F0} FPS";
        }

        /// <summary>
        /// Get current hysteresis state (for debugging)
        /// </summary>
        public string GetHysteresisState()
        {
            return $"Excellent: {_excellentCount}/{EXCELLENT_THRESHOLD} | Poor: {_poorCount}/{POOR_THRESHOLD}";
        }

        /// <summary>
        /// Log adaptation decision for debugging
        /// </summary>
        private void LogAdaptation(string reason, string action)
        {
            // This will appear in h264_sent.log via VideoEncoder
            System.Diagnostics.Debug.WriteLine($"[Adaptation] {reason} → {action}");
        }
    }

    /// <summary>
    /// Predefined quality presets
    /// </summary>
    public enum QualityPreset
    {
        VeryLow,
        Low,
        Medium,
        High,
        Ultra
    }
}