using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    public class FlipbookService
    {
        private readonly string _tempFolder;
        private readonly string _outputFolder;
        private readonly string _ffmpegPath;
        private const int TOTAL_FRAMES = 28;
        private const int FRAMES_PER_STRIP = 2; // 2 frames per 2x6 strip
        private const int VIDEO_DURATION_SECONDS = 4;
        
        // Print dimensions for 2x6 strip (in pixels at 300 DPI)
        private const int STRIP_WIDTH = 1800; // 6 inches * 300 DPI
        private const int STRIP_HEIGHT = 600;  // 2 inches * 300 DPI
        private const int FRAME_WIDTH = 900;   // Half of strip width
        private const int FRAME_HEIGHT = 600;  // Full height
        
        public FlipbookService()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), "Flipbook", Guid.NewGuid().ToString());
            
            // Use the configured photo location from settings, or default to MyPictures
            string photoLocation = Properties.Settings.Default.PhotoLocation;
            if (string.IsNullOrWhiteSpace(photoLocation))
            {
                photoLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
            _outputFolder = photoLocation;
            
            _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            
            Directory.CreateDirectory(_tempFolder);
            Directory.CreateDirectory(_outputFolder);
            
            // Log.Debug($"FlipbookService: Initialized with output folder: {_outputFolder}");
            
            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"ffmpeg.exe not found at {_ffmpegPath}");
            }
        }
        
        public async Task<FlipbookResult> CreateFlipbookFromVideo(string videoPath)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Create a session folder for this flipbook
                var sessionFolder = Path.Combine(_outputFolder, $"Flipbook_{timestamp}");
                Directory.CreateDirectory(sessionFolder);
                // Log.Debug($"FlipbookService: Created session folder: {sessionFolder}");
                
                var result = new FlipbookResult
                {
                    SessionId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now
                };
                
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Processing video from: {videoPath}");
                
                // Step 1: Extract frames from video
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Step 1 - Extracting frames...");
                var frames = await ExtractFramesFromVideo(videoPath);
                result.ExtractedFrames = frames;
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Extracted {frames.Count} frames");
                
                // Step 2: Create flipbook strips (14 strips for 28 frames)
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Step 2 - Creating flipbook strips in {sessionFolder}");
                var strips = await CreateFlipbookStrips(frames, sessionFolder);
                result.FlipbookStrips = strips;
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Created {strips.Count} strips in folder: {sessionFolder}");
                
                // Step 3: Create MP4 from frames using existing VideoGenerationService
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Step 3 - Creating MP4 using VideoGenerationService");
                var mp4Path = Path.Combine(sessionFolder, "flipbook.mp4");
                var mp4Result = VideoGenerationService.GenerateLoopingMP4(frames, mp4Path, 140); // ~7fps (1000ms/7 = ~140ms per frame)
                if (!string.IsNullOrEmpty(mp4Result))
                {
                    result.Mp4Path = mp4Result;
                    System.Diagnostics.Debug.WriteLine($"FlipbookService: Created MP4 at {mp4Result}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"FlipbookService: MP4 creation failed");
                }
                
                // Skip GIF creation - not needed
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Skipping GIF creation (not needed)");
                
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creating flipbook: {ex.Message}", ex);
            }
        }
        
        private async Task<List<string>> ExtractFramesFromVideo(string videoPath)
        {
            return await Task.Run(() =>
            {
                var frames = new List<string>();
                var framesFolder = Path.Combine(_tempFolder, "frames");
                Directory.CreateDirectory(framesFolder);
                
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Extracting frames to: {framesFolder}");
                
                // Calculate frame rate to extract exactly 28 frames from the video
                // ffmpeg command to extract 28 frames evenly distributed
                var outputPattern = Path.Combine(framesFolder, "frame_%03d.jpg");
                
                var arguments = $"-i \"{videoPath}\" -vf \"select='not(mod(n\\,{VIDEO_DURATION_SECONDS * 30 / TOTAL_FRAMES}))',setpts=N/FRAME_RATE/TB\" -frames:v {TOTAL_FRAMES} -q:v 2 \"{outputPattern}\"";
                
                System.Diagnostics.Debug.WriteLine($"FlipbookService: FFmpeg command: {_ffmpegPath} {arguments}");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                
                // Capture error output
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"FlipbookService: FFmpeg failed with exit code {process.ExitCode}");
                    System.Diagnostics.Debug.WriteLine($"FlipbookService: FFmpeg error: {errorOutput}");
                    throw new Exception($"FFmpeg failed with exit code {process.ExitCode}: {errorOutput}");
                }
                
                // Get all extracted frames
                for (int i = 1; i <= TOTAL_FRAMES; i++)
                {
                    var framePath = Path.Combine(framesFolder, $"frame_{i:D3}.jpg");
                    if (File.Exists(framePath))
                    {
                        frames.Add(framePath);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"FlipbookService: Found {frames.Count} frames in {framesFolder}");
                
                if (frames.Count != TOTAL_FRAMES)
                {
                    System.Diagnostics.Debug.WriteLine($"FlipbookService: Warning - Expected {TOTAL_FRAMES} frames, but extracted {frames.Count}");
                    // Don't throw here, work with what we have
                    // throw new Exception($"Expected {TOTAL_FRAMES} frames, but extracted {frames.Count}");
                }
                
                return frames;
            });
        }
        
        private async Task<List<string>> CreateFlipbookStrips(List<string> frames, string sessionFolder)
        {
            return await Task.Run(() =>
            {
                var strips = new List<string>();
                
                // Create 14 strips with proper flipbook pairing (frames 6 apart)
                // Strip 1: frames 1 & 7, Strip 2: frames 2 & 8, etc.
                for (int stripIndex = 0; stripIndex < 14; stripIndex++)
                {
                    var leftFrameIndex = stripIndex;           // 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13
                    var rightFrameIndex = stripIndex + 14;     // 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27
                    
                    if (leftFrameIndex < frames.Count && rightFrameIndex < frames.Count)
                    {
                        // Save to session folder with descriptive name
                        var stripPath = Path.Combine(sessionFolder, $"strip_{stripIndex + 1:D2}.jpg");
                        CreateStrip(
                            frames[leftFrameIndex], 
                            frames[rightFrameIndex], 
                            leftFrameIndex + 1,    // Frame number (1-based)
                            rightFrameIndex + 1,   // Frame number (1-based)
                            stripPath
                        );
                        strips.Add(stripPath);
                        // Log.Debug($"FlipbookService: Created strip {stripIndex + 1} at: {stripPath}");
                    }
                }
                
                // Log.Debug($"FlipbookService: Created {strips.Count} flipbook strips in: {sessionFolder}");
                return strips;
            });
        }
        
        private void CreateStrip(string oddFramePath, string evenFramePath, int oddNumber, int evenNumber, string outputPath)
        {
            using (var strip = new Bitmap(STRIP_WIDTH, STRIP_HEIGHT))
            using (var g = Graphics.FromImage(strip))
            {
                g.FillRectangle(Brushes.White, 0, 0, STRIP_WIDTH, STRIP_HEIGHT);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                
                // Define spine area in the middle (100 pixels wide for binding and numbers)
                const int SPINE_WIDTH = 100;
                const int FRAME_AREA_WIDTH = (STRIP_WIDTH - SPINE_WIDTH) / 2; // Each frame area
                
                // Load frames
                using (var oddFrame = Image.FromFile(oddFramePath))
                using (var evenFrame = Image.FromFile(evenFramePath))
                {
                    // Left frame area (odd frame) - fill the entire left side minus spine
                    var oddDestRect = new Rectangle(0, 0, FRAME_AREA_WIDTH, STRIP_HEIGHT);
                    
                    // Draw odd frame - HORIZONTALLY FLIPPED and stretched to fill
                    g.TranslateTransform(FRAME_AREA_WIDTH / 2, STRIP_HEIGHT / 2);
                    g.ScaleTransform(-1, 1); // Horizontal flip
                    DrawImageStretched(g, oddFrame, -FRAME_AREA_WIDTH / 2, -STRIP_HEIGHT / 2, FRAME_AREA_WIDTH, STRIP_HEIGHT);
                    g.ResetTransform();
                    
                    // Right frame area (even frame) - fill the entire right side minus spine
                    var evenDestRect = new Rectangle(FRAME_AREA_WIDTH + SPINE_WIDTH, 0, FRAME_AREA_WIDTH, STRIP_HEIGHT);
                    
                    // Draw even frame - HORIZONTALLY FLIPPED, then ROTATED 180 degrees, and stretched to fill
                    g.TranslateTransform(FRAME_AREA_WIDTH + SPINE_WIDTH + FRAME_AREA_WIDTH / 2, STRIP_HEIGHT / 2);
                    g.ScaleTransform(-1, 1); // Horizontal flip first
                    g.RotateTransform(180);    // Then rotate 180 degrees
                    DrawImageStretched(g, evenFrame, -FRAME_AREA_WIDTH / 2, -STRIP_HEIGHT / 2, FRAME_AREA_WIDTH, STRIP_HEIGHT);
                    g.ResetTransform();
                    
                    // Draw spine area in the middle with frame numbers
                    var spineRect = new Rectangle(FRAME_AREA_WIDTH, 0, SPINE_WIDTH, STRIP_HEIGHT);
                    using (var spineBrush = new SolidBrush(Color.FromArgb(240, 240, 240))) // Light gray spine
                    {
                        g.FillRectangle(spineBrush, spineRect);
                    }
                    
                    // Add frame numbers in the spine (smaller font)
                    using (var font = new Font("Arial", 14, FontStyle.Bold))
                    using (var numberBrush = new SolidBrush(Color.Black))
                    {
                        var format = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        
                        // Draw odd number (left side) - normal orientation, closer to left frame
                        var oddNumberRect = new Rectangle(FRAME_AREA_WIDTH + 5, STRIP_HEIGHT / 2 - 20, 40, 40);
                        g.DrawString(oddNumber.ToString(), font, numberBrush, oddNumberRect, format);
                        
                        // Draw even number (right side) - rotated 180 degrees, closer to right frame
                        g.TranslateTransform(FRAME_AREA_WIDTH + SPINE_WIDTH - 25, STRIP_HEIGHT / 2);
                        g.RotateTransform(180);
                        var evenNumberRect = new Rectangle(-20, -20, 40, 40);
                        g.DrawString(evenNumber.ToString(), font, numberBrush, evenNumberRect, format);
                        g.ResetTransform();
                    }
                    
                    // Add dotted cut/fold lines at spine edges
                    using (var pen = new Pen(Color.Gray, 1))
                    {
                        pen.DashStyle = DashStyle.Dot;
                        // Left edge of spine
                        g.DrawLine(pen, FRAME_AREA_WIDTH, 0, FRAME_AREA_WIDTH, STRIP_HEIGHT);
                        // Right edge of spine
                        g.DrawLine(pen, FRAME_AREA_WIDTH + SPINE_WIDTH, 0, FRAME_AREA_WIDTH + SPINE_WIDTH, STRIP_HEIGHT);
                    }
                }
                
                // Save with high quality
                var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                var jpegCodec = ImageCodecInfo.GetImageDecoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
                strip.Save(outputPath, jpegCodec, encoderParameters);
            }
        }
        
        private void DrawImageStretched(Graphics g, Image image, int x, int y, int width, int height)
        {
            // Draw image stretched to fill the entire area (may distort aspect ratio)
            g.DrawImage(image, x, y, width, height);
        }
        
        private Rectangle CalculateFitRectangle(Image image, Rectangle bounds)
        {
            var imageAspect = (float)image.Width / image.Height;
            var boundsAspect = (float)bounds.Width / bounds.Height;
            
            int width, height;
            if (imageAspect > boundsAspect)
            {
                // Image is wider - fit to width
                width = bounds.Width;
                height = (int)(bounds.Width / imageAspect);
            }
            else
            {
                // Image is taller - fit to height
                height = bounds.Height;
                width = (int)(bounds.Height * imageAspect);
            }
            
            int x = bounds.X + (bounds.Width - width) / 2;
            int y = bounds.Y + (bounds.Height - height) / 2;
            
            return new Rectangle(x, y, width, height);
        }
        
        
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch { }
        }
    }
    
    public class FlipbookResult
    {
        public string SessionId { get; set; }
        public DateTime Timestamp { get; set; }
        public List<string> ExtractedFrames { get; set; }
        public List<string> FlipbookStrips { get; set; }
        public string Mp4Path { get; set; }
        public string GifPath { get; set; }
    }
}