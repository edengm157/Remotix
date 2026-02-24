using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sender
{
    /// <summary>
    /// Detects pixel changes between frames by sampling random pixels.
    /// This helps determine how much motion/activity is in the screen capture.
    /// </summary>
    public class PixelChangeDetector
    {
        private byte[] _previousFrame;
        private int _width;
        private int _height;
        private int _stride;

        // Sampling configuration
        private const int SAMPLE_POINTS = 200; // Number of random pixels to check
        private List<SamplePoint> _samplePoints;
        private Random _random = new Random();

        // Statistics
        private double _currentChangePercent = 0;
        private int _totalSamples = 0;
        private int _changedSamples = 0;

        // Change thresholds (per color channel)
        private const int PIXEL_CHANGE_THRESHOLD = 15; // If any RGB component changes by more than this, it's "changed"

        public double CurrentChangePercent => _currentChangePercent;
        public MotionLevel CurrentMotionLevel { get; private set; } = MotionLevel.Unknown;

        public PixelChangeDetector()
        {
            _samplePoints = new List<SamplePoint>();
        }

        /// <summary>
        /// Initialize or reinitialize for a new frame size
        /// </summary>
        public void Initialize(int width, int height)
        {
            if (_width == width && _height == height && _samplePoints.Count > 0)
            {
                return; // Already initialized for this size
            }

            _width = width;
            _height = height;
            _stride = width * 4; // BGRA format

            _previousFrame = null;
            GenerateSamplePoints();
        }

        /// <summary>
        /// Generate random sample points across the screen
        /// </summary>
        private void GenerateSamplePoints()
        {
            _samplePoints.Clear();

            // Generate evenly distributed random points
            for (int i = 0; i < SAMPLE_POINTS; i++)
            {
                int x = _random.Next(0, _width);
                int y = _random.Next(0, _height);

                _samplePoints.Add(new SamplePoint { X = x, Y = y });
            }
        }

        /// <summary>
        /// Analyze a frame and compare to previous frame
        /// </summary>
        /// <param name="frameData">Current frame data in BGRA format</param>
        /// <param name="width">Frame width</param>
        /// <param name="height">Frame height</param>
        /// <returns>Percentage of pixels that changed (0-100)</returns>
        public double AnalyzeFrame(byte[] frameData, int width, int height)
        {
            // Initialize if needed
            if (_width != width || _height != height || _samplePoints.Count == 0)
            {
                Initialize(width, height);
            }

            // First frame - just store it
            if (_previousFrame == null)
            {
                _previousFrame = new byte[frameData.Length];
                Buffer.BlockCopy(frameData, 0, _previousFrame, 0, frameData.Length);
                _currentChangePercent = 0;
                CurrentMotionLevel = MotionLevel.Unknown;
                return 0;
            }

            // Compare sample points
            _changedSamples = 0;
            _totalSamples = _samplePoints.Count;

            foreach (var point in _samplePoints)
            {
                if (HasPixelChanged(frameData, _previousFrame, point.X, point.Y))
                {
                    _changedSamples++;
                }
            }

            // Calculate change percentage
            _currentChangePercent = (_changedSamples * 100.0) / _totalSamples;

            // Determine motion level
            UpdateMotionLevel();

            // Store current frame for next comparison
            Buffer.BlockCopy(frameData, 0, _previousFrame, 0, frameData.Length);

            return _currentChangePercent;
        }

        /// <summary>
        /// Check if a specific pixel has changed significantly
        /// </summary>
        private bool HasPixelChanged(byte[] current, byte[] previous, int x, int y)
        {
            int offset = (y * _stride) + (x * 4);

            // Ensure we're within bounds
            if (offset + 3 >= current.Length || offset + 3 >= previous.Length)
            {
                return false;
            }

            // Compare BGRA values
            int deltaB = Math.Abs(current[offset + 0] - previous[offset + 0]);
            int deltaG = Math.Abs(current[offset + 1] - previous[offset + 1]);
            int deltaR = Math.Abs(current[offset + 2] - previous[offset + 2]);

            // Consider changed if any channel exceeds threshold
            return deltaB > PIXEL_CHANGE_THRESHOLD ||
                   deltaG > PIXEL_CHANGE_THRESHOLD ||
                   deltaR > PIXEL_CHANGE_THRESHOLD;
        }

        /// <summary>
        /// Determine current motion level based on change percentage
        /// </summary>
        private void UpdateMotionLevel()
        {
            if (_currentChangePercent >= 50)
            {
                CurrentMotionLevel = MotionLevel.VeryHigh; // 50%+ pixels changed - extreme motion
            }
            else if (_currentChangePercent >= 30)
            {
                CurrentMotionLevel = MotionLevel.High; // 30-50% - high motion (gaming, video playback)
            }
            else if (_currentChangePercent >= 15)
            {
                CurrentMotionLevel = MotionLevel.Medium; // 15-30% - moderate motion (scrolling, typing)
            }
            else if (_currentChangePercent >= 5)
            {
                CurrentMotionLevel = MotionLevel.Low; // 5-15% - low motion (cursor movement)
            }
            else
            {
                CurrentMotionLevel = MotionLevel.VeryLow; // <5% - almost static
            }
        }

        /// <summary>
        /// Get a description of current motion state
        /// </summary>
        public string GetMotionDescription()
        {
            string emoji = CurrentMotionLevel switch
            {
                MotionLevel.VeryHigh => "🔥",
                MotionLevel.High => "⚡",
                MotionLevel.Medium => "🏃",
                MotionLevel.Low => "🚶",
                MotionLevel.VeryLow => "🧘",
                _ => "❓"
            };

            return $"{emoji} {CurrentMotionLevel}: {_currentChangePercent:F1}% pixels changed ({_changedSamples}/{_totalSamples} samples)";
        }

        /// <summary>
        /// Reset the detector (useful when stream restarts)
        /// </summary>
        public void Reset()
        {
            _previousFrame = null;
            _currentChangePercent = 0;
            _totalSamples = 0;
            _changedSamples = 0;
            CurrentMotionLevel = MotionLevel.Unknown;
        }

        private class SamplePoint
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }

    /// <summary>
    /// Levels of motion/activity detected in the screen
    /// </summary>
    public enum MotionLevel
    {
        Unknown,
        VeryLow,    // <5% change - almost static
        Low,        // 5-15% change - cursor movement, small changes
        Medium,     // 15-30% change - scrolling, typing, window movement
        High,       // 30-50% change - gaming, video playback, fast scrolling
        VeryHigh    // 50%+ change - extreme motion, full screen video changes
    }
}

