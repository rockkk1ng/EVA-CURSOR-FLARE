using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace EvaLocator.Tests
{
    // Render the production strips over a known desktop color, without reading the user's screen.
    internal static class FrameTests
    {
        private const int Width = 800, Height = 450, X = 501, Y = 266;
        private static readonly Color Desktop = Color.FromArgb(88, 111, 134);

        private static Bitmap Render(double ms)
        {
            var frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            int half = BurstPainter.HalfBand(1);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Desktop);
                using (var shade = new SolidBrush(Color.FromArgb(BurstPainter.ShadeAlpha(ms), 0, 0, 0)))
                    g.FillRectangle(shade, 0, 0, Width, Height);
                using (var opacity = new ImageAttributes())
                {
                    var matrix = new ColorMatrix();
                    matrix.Matrix33 = BurstPainter.BeamAlpha(ms) / 255f;
                    opacity.SetColorMatrix(matrix);
                    foreach (bool horizontal in new bool[] { false, true })
                    {
                        int length = horizontal ? Width : Height;
                        int w = horizontal ? length : 2 * half + 1;
                        int h = horizontal ? 2 * half + 1 : length;
                        using (var strip = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
                        {
                            using (var pen = Graphics.FromImage(strip))
                                BurstPainter.DrawStrip(pen, horizontal, length, half, horizontal ? X : Y, 1, ms);
                            var destination = new Rectangle(horizontal ? 0 : X - half, horizontal ? Y - half : 0, w, h);
                            g.DrawImage(strip, destination, 0, 0, w, h, GraphicsUnit.Pixel, opacity);
                        }
                    }
                }
            }
            return frame;
        }

        private static void Require(bool yes, string message)
        {
            if (!yes) throw new InvalidOperationException(message);
        }

        public static int Main(string[] args)
        {
            using (Bitmap ignition = Render(0), peak = Render(115), fading = Render(300),
                          tail = Render(560), finished = Render(BurstPainter.Duration))
            {
                Require(ignition.GetPixel(X, Y).R > 235, "Missing hotspot on the first frame.");
                Require(ignition.GetPixel(1, Y) == ignition.GetPixel(1, 1), "Beams appear too soon.");
                Color shade = peak.GetPixel(1, 1);
                Require(shade.R < Desktop.R && shade.R > Desktop.R * 0.65, "Unexpected desktop shade.");
                foreach (Point p in new[] { new Point(0, Y), new Point(Width - 1, Y),
                                            new Point(X, 0), new Point(X, Height - 1) })
                    Require(peak.GetPixel(p.X, p.Y).R > shade.R + 80, "A beam missed the screen edge.");
                Color core = peak.GetPixel(X, Y);
                Require(core.R > 235 && core.G > 235 && core.B > 225, "The peak must have a white core.");
                Require(peak.GetPixel(X + 15, Y + 4).R > peak.GetPixel(1, Y + 4).R + 20, "Missing center taper.");
                Require(core.R > fading.GetPixel(X, Y).R && fading.GetPixel(X, Y).R > tail.GetPixel(X, Y).R,
                    "Beam intensity should decay.");
                Require(finished.GetPixel(X, Y).ToArgb() == Desktop.ToArgb(), "Effect failed to clear.");
                if (args.Length == 1) peak.Save(Path.GetFullPath(args[0]), ImageFormat.Png);
            }
            Console.WriteLine("PASS - ignition, four screen edges, taper, core, dimming and fade.");
            return 0;
        }
    }
}
