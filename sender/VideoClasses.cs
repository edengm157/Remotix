using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sender
{
    internal class VideoEncoder
    {
        private FrameCupturer cupturer;
        private static Byte[] encodedFrame; // need to checl id this variable will be already with something in it or not while the complieration. if not add '?' after the []
        private int bitrate;
        private int framerate;
        private string resolution;
        private bool usInitialized;
        // constructor
        public VideoEncoder(FrameCupturer cupturer, int bitrate, int framerate, string resolution)
        {
            this.cupturer = cupturer;
            this.bitrate = bitrate;
            this.framerate = framerate;
            this.resolution = resolution;
            this.usInitialized = false;
        }
        public FrameCupturer Cupturer { get { return cupturer; } set { cupturer = value; } }             // getters and setters
        public Byte[] EncodedFrame { get { return encodedFrame; } set { encodedFrame = value; } }       // getters and setters
        public int Bitrate { get { return bitrate; } set { bitrate = value; } }                        // getters and setters 
        public int Framerate { get { return framerate; } set { framerate = value; } }                 // getters and setters
        public string Resolution { get { return resolution; } set { resolution = value; } }          // getters and setters
        public bool UsInitialized { get { return usInitialized; } set { usInitialized = value; } }  // getters and setters

        public override string ToString() { return "VideoEncoder: " + cupturer.ToString() + "\n" + bitrate + "kbps\n" + framerate + "fps\n" + resolution + "Initialized: " + usInitialized; } // toString method
    }

    internal class FrameCupturer
    {
        protected string sourceWindow;
        protected Frame currentFrame;
        // constructor
        public FrameCupturer(Frame currentFrame, string sourceWindow)
        {
            this.currentFrame = currentFrame;
            this.sourceWindow = sourceWindow;
        }

        public Frame CurrentFrame { get { return currentFrame; } set { currentFrame = value; } }                          // getters and setters
        public string SourceWindow { get { return sourceWindow; } set { sourceWindow = value; } }                        // getters and setters

        public override string ToString() { return "FrameCupturer: " + sourceWindow + " " + currentFrame.ToString(); } // toString method
    }

    internal class Frame
    {
        protected int timestamp;
        protected double width;
        protected double height;
        // constructor
        public Frame(int timestamp, double width, double height)
        {
            this.timestamp = timestamp;
            this.width = width;
            this.height = height;
        }

        public int Timestamp { get { return timestamp; } set { timestamp = value; } }                        // getters and setters
        public double Width { get { return width; } set { width = value; } }                                // getters and setters
        public double Height { get { return height; } set { height = value; } }                            // getters and setters

        public override string ToString() { return "Frame: " + timestamp + " " + width + "x" + height; } // toString method
    }

}
