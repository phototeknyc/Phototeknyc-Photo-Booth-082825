using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Photobooth.Services
{
    public enum FilterType
    {
        None,
        BlackAndWhite,
        Sepia,
        Vintage,
        Glamour,
        Cool,
        Warm,
        HighContrast,
        Soft,
        Vivid,
        Custom // For LUT-based filters
    }

    public class PhotoFilterService
    {
        private readonly string lutFolder;

        public PhotoFilterService()
        {
            // Set up LUT folder for custom filters
            lutFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Filters", "LUTs");
            if (!Directory.Exists(lutFolder))
            {
                Directory.CreateDirectory(lutFolder);
            }
        }

        public Bitmap ApplyFilter(Bitmap original, FilterType filterType, float intensity = 1.0f)
        {
            if (original == null) return null;
            
            // Clone the original to avoid modifying it
            Bitmap result = new Bitmap(original);
            
            switch (filterType)
            {
                case FilterType.None:
                    return result;
                    
                case FilterType.BlackAndWhite:
                    return ApplyBlackAndWhite(result, intensity);
                    
                case FilterType.Sepia:
                    return ApplySepia(result, intensity);
                    
                case FilterType.Vintage:
                    return ApplyVintage(result, intensity);
                    
                case FilterType.Glamour:
                    return ApplyGlamour(result, intensity);
                    
                case FilterType.Cool:
                    return ApplyCoolTone(result, intensity);
                    
                case FilterType.Warm:
                    return ApplyWarmTone(result, intensity);
                    
                case FilterType.HighContrast:
                    return ApplyHighContrast(result, intensity);
                    
                case FilterType.Soft:
                    return ApplySoftFocus(result, intensity);
                    
                case FilterType.Vivid:
                    return ApplyVivid(result, intensity);
                    
                case FilterType.Custom:
                    // For LUT-based filters
                    return result;
                    
                default:
                    return result;
            }
        }

        private Bitmap ApplyBlackAndWhite(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Create grayscale ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {.299f, .299f, .299f, 0, 0},
                        new float[] {.587f, .587f, .587f, 0, 0},
                        new float[] {.114f, .114f, .114f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplySepia(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Create sepia ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {.393f, .349f, .272f, 0, 0},
                        new float[] {.769f, .686f, .534f, 0, 0},
                        new float[] {.189f, .168f, .131f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplyVintage(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            // Apply a combination of effects for vintage look
            // 1. Reduce saturation
            // 2. Add warm tone
            // 3. Add vignette
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Vintage color matrix (reduced saturation + warm tone)
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {0.5f, 0.3f, 0.2f, 0, 0},
                        new float[] {0.4f, 0.5f, 0.3f, 0, 0},
                        new float[] {0.2f, 0.2f, 0.4f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0.1f, 0.05f, -0.1f, 0, 1} // Warm tint
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            // Add vignette
            result = AddVignette(result, 0.5f);

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplyGlamour(Bitmap source, float intensity)
        {
            // Glamour effect: soft focus + slight glow + enhanced skin tones
            Bitmap result = ApplySoftFocus(source, 0.3f);
            
            // Enhance warm tones (skin tones)
            using (Graphics g = Graphics.FromImage(result))
            {
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {1.1f, 0, 0, 0, 0},
                        new float[] {0, 1.05f, 0, 0, 0},
                        new float[] {0, 0, 0.95f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0.02f, 0.01f, 0, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                Bitmap temp = new Bitmap(result);
                g.Clear(Color.Transparent);
                g.DrawImage(temp, new Rectangle(0, 0, temp.Width, temp.Height),
                    0, 0, temp.Width, temp.Height, GraphicsUnit.Pixel, attributes);
                temp.Dispose();
            }

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplyCoolTone(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Cool tone color matrix (enhance blues, reduce reds)
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {0.9f, 0, 0, 0, 0},
                        new float[] {0, 1.0f, 0, 0, 0},
                        new float[] {0, 0, 1.2f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {-0.05f, 0, 0.1f, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplyWarmTone(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Warm tone color matrix (enhance reds/yellows, reduce blues)
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {1.2f, 0, 0, 0, 0},
                        new float[] {0, 1.1f, 0, 0, 0},
                        new float[] {0, 0, 0.8f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0.05f, 0.02f, -0.1f, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            if (intensity < 1.0f)
            {
                return BlendImages(source, result, intensity);
            }

            return result;
        }

        private Bitmap ApplyHighContrast(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            float contrast = 1.0f + (intensity * 0.5f); // Increase contrast by up to 50%
            
            using (Graphics g = Graphics.FromImage(result))
            {
                float t = (1.0f - contrast) / 2.0f;
                
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {contrast, 0, 0, 0, 0},
                        new float[] {0, contrast, 0, 0, 0},
                        new float[] {0, 0, contrast, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {t, t, t, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return result;
        }

        private Bitmap ApplySoftFocus(Bitmap source, float intensity)
        {
            // Create a blurred version
            Bitmap blurred = ApplyGaussianBlur(source, (int)(5 * intensity));
            
            // Blend original with blurred for soft focus effect
            return BlendImages(source, blurred, intensity * 0.5f);
        }

        private Bitmap ApplyVivid(Bitmap source, float intensity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            float saturation = 1.0f + (intensity * 0.5f); // Increase saturation by up to 50%
            
            using (Graphics g = Graphics.FromImage(result))
            {
                float sr = (1.0f - saturation) * 0.299f;
                float sg = (1.0f - saturation) * 0.587f;
                float sb = (1.0f - saturation) * 0.114f;
                
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {sr + saturation, sr, sr, 0, 0},
                        new float[] {sg, sg + saturation, sg, 0, 0},
                        new float[] {sb, sb, sb + saturation, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            return result;
        }

        private Bitmap ApplyGaussianBlur(Bitmap source, int radius)
        {
            if (radius < 1) return source;
            
            Bitmap result = new Bitmap(source);
            
            // Simple box blur as approximation of Gaussian blur
            // For production, consider using a proper Gaussian kernel
            Rectangle rect = new Rectangle(0, 0, source.Width, source.Height);
            BitmapData sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
            int bytes = Math.Abs(sourceData.Stride) * source.Height;
            byte[] sourceBytes = new byte[bytes];
            byte[] resultBytes = new byte[bytes];
            
            Marshal.Copy(sourceData.Scan0, sourceBytes, 0, bytes);
            
            // Apply simple box blur
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    int count = 0;
                    
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int px = Math.Min(Math.Max(x + kx, 0), source.Width - 1);
                            int py = Math.Min(Math.Max(y + ky, 0), source.Height - 1);
                            int idx = (py * sourceData.Stride) + (px * 4);
                            
                            b += sourceBytes[idx];
                            g += sourceBytes[idx + 1];
                            r += sourceBytes[idx + 2];
                            a += sourceBytes[idx + 3];
                            count++;
                        }
                    }
                    
                    int targetIdx = (y * sourceData.Stride) + (x * 4);
                    resultBytes[targetIdx] = (byte)(b / count);
                    resultBytes[targetIdx + 1] = (byte)(g / count);
                    resultBytes[targetIdx + 2] = (byte)(r / count);
                    resultBytes[targetIdx + 3] = (byte)(a / count);
                }
            }
            
            Marshal.Copy(resultBytes, 0, resultData.Scan0, bytes);
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
            
            return result;
        }

        private Bitmap AddVignette(Bitmap source, float strength)
        {
            Bitmap result = new Bitmap(source);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Create radial gradient for vignette
                Rectangle bounds = new Rectangle(0, 0, source.Width, source.Height);
                
                // Create a circular path for the gradient
                System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(bounds);
                
                using (var brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(source.Width / 2f, source.Height / 2f);
                    brush.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    brush.SurroundColors = new[] { Color.FromArgb((int)(255 * strength), 0, 0, 0) };
                    
                    g.FillRectangle(brush, bounds);
                }
            }
            
            return result;
        }

        private Bitmap BlendImages(Bitmap source, Bitmap overlay, float opacity)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(source, 0, 0);
                
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = opacity;
                
                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                
                g.DrawImage(overlay, new Rectangle(0, 0, overlay.Width, overlay.Height),
                    0, 0, overlay.Width, overlay.Height, GraphicsUnit.Pixel, attributes);
            }
            
            return result;
        }

        // LUT support for advanced filters
        public Bitmap ApplyLUT(Bitmap source, string lutPath)
        {
            if (!File.Exists(lutPath))
                return source;
            
            // Basic 3D LUT implementation
            // For production, consider using a proper LUT library
            // This is a simplified version
            
            Bitmap result = new Bitmap(source);
            
            // Load LUT file (assuming .cube format)
            // Parse and apply LUT transformation
            // This would require proper LUT parsing implementation
            
            return result;
        }

        // Helper method to apply filter to file
        public string ApplyFilterToFile(string inputPath, string outputPath, FilterType filterType, float intensity = 1.0f)
        {
            using (Bitmap source = new Bitmap(inputPath))
            {
                using (Bitmap filtered = ApplyFilter(source, filterType, intensity))
                {
                    filtered.Save(outputPath, ImageFormat.Jpeg);
                    return outputPath;
                }
            }
        }
    }

}