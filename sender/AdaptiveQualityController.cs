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
        public int TargetBitrate { get; private set; } = 2500000; // 2.5 Mbps
        public float TargetFPS { get; private set; } = 20f;       // 20 FPS
        public QualityPreset CurrentPreset { get; private set; } = QualityPreset.Medium;

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
        private const int MIN_BITRATE = 800000;  // 0.8 Mbps
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

            if (currentTime - _lastAdaptationTime < ADAPTATION_INTERVAL_MS)
            {
                return false;
            }

            _lastAdaptationTime = currentTime;

            var quality = _monitor.Quality;
            var currentFPS = _monitor.CurrentFPS;
            var packetLoss = _monitor.PacketLossPercent;
            var motionLevel = _monitor.CurrentMotionLevel; // ✅ קח motion level

            bool changed = false;

            // ✅ חדש: תגובה חכמה לפי Motion Level
            if (motionLevel >= MotionLevel.High && currentFPS < 20)
            {
                // תנועה גבוהה + FPS נמוך → הורד BITRATE, לא FPS!
                changed = ReduceBitrateKeepFPS();
                if (changed)
                {
                    LogAdaptation($"High motion ({motionLevel}) with low FPS ({currentFPS:F1})",
                                 "Reducing bitrate to improve FPS");
                }
            }
            else if (motionLevel <= MotionLevel.Low && currentFPS > 18 && TargetBitrate < MAX_BITRATE)
            {
                // תנועה נמוכה + FPS טוב → אפשר להוריד FPS ולהעלות איכות
                changed = TradeoffFPSForQuality();
                if (changed)
                {
                    LogAdaptation($"Low motion ({motionLevel}) with good FPS ({currentFPS:F1})",
                                 "Trading FPS for better quality");
                }
            }

            // המשך הלוגיקה הרגילה...
            switch (quality)
            {
                case StreamQuality.Excellent:
                    _excellentCount++;
                    _poorCount = 0;

                    if (_excellentCount >= EXCELLENT_THRESHOLD)
                    {
                        changed |= IncreaseQuality();
                        if (changed)
                        {
                            _excellentCount = 0;
                            LogAdaptation("Sustained excellent quality detected", "Increasing quality");
                        }
                    }
                    break;

                case StreamQuality.Good:
                case StreamQuality.Fair:
                    _excellentCount = 0;
                    _poorCount = 0;
                    break;

                case StreamQuality.Poor:
                    _poorCount++;
                    _excellentCount = 0;

                    if (_poorCount >= POOR_THRESHOLD)
                    {
                        // ✅ Poor quality - בדוק אם זה בגלל high motion
                        if (motionLevel >= MotionLevel.High)
                        {
                            // תנועה גבוהה - הורד bitrate בעיקר
                            changed |= ReduceBitrate(0.7); // 70% של הbitrate
                            if (TargetFPS > MIN_FPS)
                            {
                                changed |= ReduceFPS(0.9); // הורד FPS רק קצת (90%)
                            }
                        }
                        else
                        {
                            // תנועה נמוכה - הורד גם bitrate גם FPS
                            changed |= DecreaseQuality(moderate: false);
                        }

                        if (changed)
                        {
                            _poorCount = 0;
                            LogAdaptation("Sustained poor quality detected", "Reducing quality");
                        }
                    }
                    break;
            }

            // EMERGENCY HANDLING
            if (packetLoss > 10)
            {
                changed |= ReduceBitrate(0.6);
                LogAdaptation($"Critical packet loss: {packetLoss:F1}%", "Emergency bitrate reduction");
                _poorCount = 0;
            }
            else if (currentFPS < 8 && TargetFPS > MIN_FPS)
            {
                changed |= ReduceFPS(0.7);
                LogAdaptation($"Critical FPS drop: {currentFPS:F1}", "Emergency FPS reduction");
                _poorCount = 0;
            }

            if (changed)
            {
                UpdatePreset();
                QualityAdjusted?.Invoke(TargetBitrate, TargetFPS, CurrentPreset);
            }

            return changed;
        }

        /// <summary>
        /// Reduce bitrate aggressively while trying to keep FPS stable
        /// Used when high motion detected with low FPS
        /// </summary>
        private bool ReduceBitrateKeepFPS()
        {
            bool changed = false;

            // הורד bitrate בצורה אגרסיבית
            if (TargetBitrate > MIN_BITRATE)
            {
                TargetBitrate = Math.Max(MIN_BITRATE, (int)(TargetBitrate * 0.6)); // 60% של הbitrate
                changed = true;
            }

            // אל תוריד FPS! (או הורד רק מעט מאוד)

            return changed;
        }

        /// <summary>
        /// Trade FPS for better quality when motion is low
        /// </summary>
        private bool TradeoffFPSForQuality()
        {
            bool changed = false;

            // הורד FPS קצת
            if (TargetFPS > 15)
            {
                TargetFPS = Math.Max(15, TargetFPS * 0.85f); // 85% של ה-FPS
                changed = true;
            }

            // העלה bitrate
            if (TargetBitrate < MAX_BITRATE)
            {
                TargetBitrate = Math.Min(MAX_BITRATE, (int)(TargetBitrate * 1.2)); // 120% של הbitrate
                changed = true;
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
        /// Set bitrate directly (for motion-based adjustments)
        /// </summary>
        public void SetBitrate(int bitrate)
        {
            bitrate = Math.Max(MIN_BITRATE, Math.Min(MAX_BITRATE, bitrate));
            if (bitrate != TargetBitrate)
            {
                TargetBitrate = bitrate;
                UpdatePreset();
                LogAdaptation($"⚡ Bitrate set to {TargetBitrate / 1000000.0:F2} Mbps", "DATA");
            }
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

        //private void UpdateTargetFPS()
        //{
        //    var motion = _monitor.CurrentMotionLevel;

        //    // לוגיקה חכמה לניהול FPS לפי תנועה
        //    switch (motion)
        //    {
        //        case MotionLevel.VeryLow: // מסך סטטי (קריאת טקסט)
        //            TargetFPS = 5;       // אין סיבה לצלם 30 פעמים בשנייה כשכלום לא זז
        //            break;

        //        case MotionLevel.Low:     // תנועת עכבר קלה
        //            TargetFPS = 10;
        //            break;

        //        case MotionLevel.Medium:  // עבודה רגילה
        //            TargetFPS = 20;
        //            break;

        //        case MotionLevel.High:    // וידאו / גלילה
        //        case MotionLevel.VeryHigh:
        //            TargetFPS = 30;      // מקסימום חלקות
        //            break;

        //        default:
        //            TargetFPS = 15;      // ברירת מחדל
        //            break;
        //    }

        //    // כאן אתה יכול להוסיף לוג:
        //    // Console.WriteLine($"Motion: {motion}, Setting FPS to: {TargetFPS}");
        //}

        /// <summary>
        /// מתאים את איכות התמונה (דחיסת JPEG) לפי רמת התנועה במסך
        /// </summary>
        //private void UpdateQualityBasedOnMotion()
        //{
        //    var motion = _monitor.CurrentMotionLevel;

        //    // נשמור את הפריסט הקודם כדי לדעת אם צריך לדווח על שינוי
        //    var oldPreset = CurrentPreset;

        //    switch (motion)
        //    {
        //        case MotionLevel.VeryHigh: // משחקים / וידאו מלא
        //                                   // תנועה קיצונית: מורידים איכות דרסטית כדי למנוע תקיעות
        //            CurrentPreset = QualityPreset.Low;
        //            break;

        //        case MotionLevel.High:     // גלילה מהירה / יוטיוב
        //                                   // תנועה גבוהה: איכות בינונית-נמוכה
        //            CurrentPreset = QualityPreset.Medium;
        //            break;

        //        case MotionLevel.Medium:   // עבודה רגילה
        //                                   // תנועה סבירה: איכות גבוהה אבל לא מקסימלית
        //            CurrentPreset = QualityPreset.High;
        //            break;

        //        case MotionLevel.Low:      // הזזת עכבר
        //        case MotionLevel.VeryLow: // כמעט סטטי
        //                                   // אין תנועה: רוצים לראות טקסט חד וברור (Crisp)
        //            CurrentPreset = QualityPreset.Ultra;
        //            break;

        //        default:
        //            CurrentPreset = QualityPreset.High;
        //            break;
        //    }

        //    // אם האיכות השתנתה, נכתוב ללוג
        //    if (oldPreset != CurrentPreset)
        //    {
        //        Console.WriteLine($"[AdaptiveControl] Motion is {motion} -> Adjusted Quality to {CurrentPreset}");
        //    }
        //}

        //public void AdjustSettings()
        //{
        //    UpdateQualityBasedOnMotion();
        //    UpdateTargetFPS(); // (הפונקציה מהשלב הקודם שמשנה FPS)
        //}
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