using System;
using UIKit;
using CoreImage;
using CoreGraphics;
using Foundation;
using AVFoundation;
using CoreFoundation;
using CoreVideo;
using CoreMedia;
using System.Runtime.CompilerServices;

namespace RectangleDetectionTest.iOS
{
	/// <summary>
	/// Shows a raw view of the video stream captured from the camera. Adds to smaller views
	/// which shows detected rectangles and perepsctive corrected versions of the image.
	/// </summary>
	public class RectDetectController : UIViewController, IAVCaptureVideoDataOutputSampleBufferDelegate
    {
		/// <summary>
		/// Defines with how many frames per second the detection is performed.
		/// </summary>
		const int DETECTION_FPS = 10;

		/// <summary>
		/// Defines the width of the preview windows that show detected rectangles.
		/// </summary>
		const float PREVIEW_VIEW_WIDTH = 160f;

		/// <summary>
		/// Defines the height of the preview windows that show detected rectangles.
		/// </summary>
		const float PREVIEW_VIEW_HEIGHT = 100f;

		public RectDetectController ()
		{
		}

		DispatchQueue sessionQueue;
		VideoFrameSamplerDelegate sampleBufferDelegate;
		AVCaptureVideoPreviewLayer videoLayer;

		NSMutableDictionary videoSettingsDict;

		public override void ViewDidLoad ()
		{
			base.ViewDidLoad ();

			this.View.BackgroundColor = UIColor.White;

			NSError error;


			// Create the session. The AVCaptureSession is the managing instance of the whole video handling.
			var captureSession = new AVCaptureSession ()
			{ 
				// Defines what quality we want to use for the images we grab. Photo gives highest resolutions.
				SessionPreset = AVCaptureSession.PresetPhoto
			};

			// Find a suitable AVCaptureDevice for video input.
			var device = AVCaptureDevice.GetDefaultDevice(AVMediaType.Video);
			if (device == null)
			{
				// This will not work on the iOS Simulator - there is no camera. :-)
				throw new InvalidProgramException ("Failed to get AVCaptureDevice for video input!");
			}

			// Create a device input with the device and add it to the session.
			var videoInput = AVCaptureDeviceInput.FromDevice (device, out error);
			if (videoInput == null)
			{
				throw new InvalidProgramException ("Failed to get AVCaptureDeviceInput from AVCaptureDevice!");
			}

			// Let session read from the input, this is our source.
			captureSession.AddInput (videoInput);

			// Create output for the video stream. This is the destination.
			var videoOutput = new AVCaptureVideoDataOutput () {
				AlwaysDiscardsLateVideoFrames = true
			};

			// Define the video format we want to use. Note that Xamarin exposes the CompressedVideoSetting and UncompressedVideoSetting 
			// properties on AVCaptureVideoDataOutput un Unified API, but I could not get these to work. The VideoSettings property is deprecated,
			// so I use the WeakVideoSettings instead which takes an NSDictionary as input.
			this.videoSettingsDict = new NSMutableDictionary ();
			this.videoSettingsDict.Add (CVPixelBuffer.PixelFormatTypeKey, NSNumber.FromUInt32((uint)CVPixelFormatType.CV32BGRA));
			videoOutput.WeakVideoSettings = this.videoSettingsDict;

			// Create a delegate to report back to us when an image has been captured.
			// We want to grab the camera stream and feed it through a AVCaptureVideoDataOutputSampleBufferDelegate
			// which allows us to get notified if a new image is availeble. An implementation of that delegate is VideoFrameSampleDelegate in this project.
			this.sampleBufferDelegate = new VideoFrameSamplerDelegate ();

			// Processing happens via Grand Central Dispatch (GCD), so we need to provide a queue.
			// This is pretty much like a system managed thread (see: http://zeroheroblog.com/ios/concurrency-in-ios-grand-central-dispatch-gcd-dispatch-queues).
			this.sessionQueue =  new DispatchQueue ("AVSessionQueue");

			// Assign the queue and the delegate to the output. Now all output will go through the delegate.
			videoOutput.SetSampleBufferDelegateQueue(this.sampleBufferDelegate, this.sessionQueue);

			// Add output to session.
			captureSession.AddOutput(videoOutput);

			// We also want to visualize the input stream. The raw stream can be fed into an AVCaptureVideoPreviewLayer, which is a subclass of CALayer.
			// A CALayer can be added to a UIView. We add that layer to the controller's main view.
			var layer = this.View.Layer;
			this.videoLayer = AVCaptureVideoPreviewLayer.FromSession (captureSession);
			this.videoLayer.Frame = layer.Bounds;
			layer.AddSublayer (this.videoLayer);

			// All setup! Start capturing!
			captureSession.StartRunning ();

			// Configure framerate. Kind of weird way of doing it but the only one that works.
			device.LockForConfiguration (out error);
			// CMTime constructor means: 1 = one second, DETECTION_FPS = how many samples per unit, which is 1 second in this case.
			device.ActiveVideoMinFrameDuration = new CMTime(1, DETECTION_FPS);
			device.ActiveVideoMaxFrameDuration = new CMTime(1, DETECTION_FPS);
			device.UnlockForConfiguration ();
		}
    }

    public class VideoFrameSamplerDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        public VideoFrameSamplerDelegate()
        {
        }

        /// <summary>
        /// Fires if a new image was captures.
        /// </summary>
        public EventHandler<ImageCaptureEventArgs> ImageCaptured;

        /// <summary>
        /// Trigger the ImageCaptured event.
        /// </summary>
        /// <param name="image">Image.</param>
        void OnImageCaptured(CGImage image)
        {
            var handler = this.ImageCaptured;
            if (handler != null)
            {
                var args = new ImageCaptureEventArgs();
                args.Image = image;
                args.CapturedAt = DateTime.Now;
                handler(this, args);
            }
        }

        /// <summary>
        /// Fires if an error occurs.
        /// </summary>
        public EventHandler<CaptureErrorEventArgs> CaptureError;

        /// <summary>
        /// Triggers the CaptureError event.
        /// </summary>
        /// <param name="errorMessage">Error message.</param>
        /// <param name="ex">Ex.</param>
        void OnCaptureError(string errorMessage, Exception ex)
        {
            var handler = this.CaptureError;
            if (handler != null)
            {
                try
                {
                    var args = new CaptureErrorEventArgs();
                    args.ErrorMessage = errorMessage;
                    args.Exception = ex;
                    handler(this, args);
                }
                catch (Exception fireEx)
                {
                    Console.WriteLine("Failed to fire CaptureError event: " + fireEx);
                }
            }
        }



        /// <summary>
        /// Gets called by the video session if a new image is available.
        /// </summary>
        /// <param name="captureOutput">Capture output.</param>
        /// <param name="sampleBuffer">Sample buffer.</param>
        /// <param name="connection">Connection.</param>
        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            System.Diagnostics.Debug.WriteLine("12345");
            try
            {
                // Convert the raw image data into a CGImage.
                using (CGImage sourceImage = GetImageFromSampleBuffer(sampleBuffer))
                {
                    this.OnImageCaptured(sourceImage);
                }

                // Make sure AVFoundation does not run out of buffers
                sampleBuffer.Dispose();

            }
            catch (Exception ex)
            {
                string errorMessage = string.Format("Failed to process image capture: {0}", ex);
                this.OnCaptureError(errorMessage, ex);
            }
        }

        /// <summary>
        /// Converts raw image data from a CMSampleBugger into a CGImage.
        /// </summary>
        /// <returns>The image from sample buffer.</returns>
        /// <param name="sampleBuffer">Sample buffer.</param>
        static CGImage GetImageFromSampleBuffer(CMSampleBuffer sampleBuffer)
        {
            // Get the CoreVideo image
            using (var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer)
            {
                pixelBuffer.Lock(CVPixelBufferLock.None);

                var baseAddress = pixelBuffer.BaseAddress;
                int bytesPerRow = (int)pixelBuffer.BytesPerRow;
                int width = (int)pixelBuffer.Width;
                int height = (int)pixelBuffer.Height;
                var flags = CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little;

                // Create a CGImage on the RGB colorspace from the configured parameter above
                using (var cs = CGColorSpace.CreateDeviceRGB())
                using (var context = new CGBitmapContext(baseAddress, width, height, 8, bytesPerRow, cs, (CGImageAlphaInfo)flags))
                {
                    var cgImage = context.ToImage();
                    pixelBuffer.Unlock(CVPixelBufferLock.None);
                    return cgImage;
                }
            }
        }
    }
}

