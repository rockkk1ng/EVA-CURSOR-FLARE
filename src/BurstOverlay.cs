using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EvaLocator
{
    // All windows and the timer live on the WinForms UI thread.
    internal sealed class BurstOverlay : IDisposable
    {
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly List<OverlayWindow> shades = new List<OverlayWindow>();
        private BeamStrip horizontal;
        private BeamStrip vertical;
        private Rectangle beamScreen;
        private int halfBand;
        private float scale;
        private Point tip;

        internal BurstOverlay()
        {
            timer.Interval = 15;
            timer.Tick += Tick;
        }

        internal bool IsActive { get { return timer.Enabled; } }

        internal void Start(Point cursorTip)
        {
            Stop();
            tip = cursorTip;
            try
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    OverlayWindow shade = new OverlayWindow("Shade", screen.Bounds, true);
                    shades.Add(shade);
                    shade.SetShade(BurstPainter.ShadeAlpha(0));
                    shade.Show();
                }
                PrepareBeams(Screen.FromPoint(tip).Bounds);
                PaintFrame(0);
                clock.Restart();
                timer.Start();
            }
            catch (Win32Exception)
            {
                // The desktop may disappear between the gesture and the first frame.
                Stop();
            }
            catch
            {
                Stop();
                throw;
            }
        }

        private void PrepareBeams(Rectangle screen)
        {
            if (horizontal != null) horizontal.Dispose();
            if (vertical != null) vertical.Dispose();
            beamScreen = screen;
            scale = OverlayNative.GetScale(tip);
            halfBand = BurstPainter.HalfBand(scale);
            horizontal = new BeamStrip(true, screen.Width, halfBand);
            vertical = new BeamStrip(false, screen.Height, halfBand);
        }

        private void Tick(object sender, EventArgs args)
        {
            double ms = clock.Elapsed.TotalMilliseconds;
            if (ms >= BurstPainter.Duration)
            {
                Stop();
                return;
            }
            Point position;
            if (OverlayNative.GetCursorPos(out position)) tip = position;
            try
            {
                Rectangle screen = Screen.FromPoint(tip).Bounds;
                if (screen != beamScreen) PrepareBeams(screen);
                PaintFrame(ms);
            }
            catch (Win32Exception)
            {
                // A session or display switch can invalidate a layered window mid-frame.
                Stop();
            }
        }

        private void PaintFrame(double ms)
        {
            byte shadeAlpha = BurstPainter.ShadeAlpha(ms);
            foreach (OverlayWindow shade in shades) shade.SetShade(shadeAlpha);
            vertical.Paint(beamScreen, tip, halfBand, scale, ms);
            horizontal.Paint(beamScreen, tip, halfBand, scale, ms);
        }

        internal void Stop()
        {
            timer.Stop();
            clock.Reset();
            if (horizontal != null) horizontal.Dispose();
            if (vertical != null) vertical.Dispose();
            horizontal = null;
            vertical = null;
            foreach (OverlayWindow shade in shades) shade.Dispose();
            shades.Clear();
        }

        public void Dispose()
        {
            Stop();
            timer.Dispose();
        }
    }

    internal static class BurstPainter
    {
        internal const double Duration = 620;
        internal static int HalfBand(float scale) { return (int)Math.Ceiling(52 * scale); }

        private static double Clamp(double value)
        {
            return Math.Max(0, Math.Min(value, 1));
        }

        internal static byte BeamAlpha(double ms)
        {
            double tail = Clamp((ms - 90) / (Duration - 90));
            return (byte)Math.Round(255 * Math.Pow(1 - tail, 1.35));
        }

        internal static byte ShadeAlpha(double ms)
        {
            double rise = 0.45 + 0.55 * Clamp(ms / 60);
            return (byte)Math.Round(BeamAlpha(ms) * rise * 0.23);
        }

        // The origin is a pixel coordinate on the long axis. Both beams share a pixel center.
        // SourceConstantAlpha on the layered windows applies BeamAlpha to the completed strip.
        internal static void DrawStrip(Graphics g, bool horizontal, int length, int halfBand,
                                       float origin, float scale, double ms)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (!horizontal)
                {
                    using (Matrix transpose = new Matrix(0, 1, 1, 0, 0, 0))
                        g.MultiplyTransform(transpose);
                }
                float x = origin + 0.5f;
                float y = halfBand + 0.5f;
                double advance = Clamp((ms - 22) / (horizontal ? 82 : 92));
                float reach = (float)(1 - Math.Pow(1 - advance, 3));
                float heat = (float)(1 - 0.27 * Clamp((ms - 100) / 360));
                if (reach > 0)
                {
                    DrawRay(g, x, y, -(x + 1) * reach, scale, heat);
                    DrawRay(g, x, y, (length - x + 1) * reach, scale, heat);
                }
                if (horizontal) DrawSpark(g, x, y, scale, ms);
            }
            finally { g.Restore(state); }
        }

        private static void DrawRay(Graphics g, float x, float y, float extent, float scale, float heat)
        {
            // Beyond the hot base, a narrow luminous filament remains visible to the edge.
            float distance = Math.Abs(extent);
            if (distance < 0.75f) return;
            const int sections = 14;
            PointF[] outline = new PointF[(sections + 1) * 2];
            float[] axial = new float[sections + 1];
            float[] taper = new float[sections + 1];
            for (int index = 0; index <= sections; ++index)
            {
                float fraction = (float)index / sections;
                fraction *= fraction; // More samples where the beam flares.
                axial[index] = x + extent * fraction;
                float d = fraction * distance / scale;
                taper[index] = (float)(0.63 * Math.Exp(-d / 28)
                             + 0.37 * Math.Exp(-d / 185));
            }
            for (int layer = 0; layer < RayLayers.Length; ++layer)
            {
                RayLayer pigment = RayLayers[layer];
                for (int index = 0; index <= sections; ++index)
                {
                    float width = scale * heat * (pigment.Edge + pigment.Base * taper[index]);
                    outline[index] = new PointF(axial[index], y - width);
                    outline[outline.Length - 1 - index] = new PointF(axial[index], y + width);
                }
                using (SolidBrush brush = new SolidBrush(pigment.Color)) g.FillPolygon(brush, outline);
            }
        }

        private struct RayLayer
        {
            internal float Base, Edge;
            internal Color Color;
            internal RayLayer(float hotBase, float edge, int alpha, int red, int green, int blue)
            {
                Base = hotBase;
                Edge = edge;
                Color = System.Drawing.Color.FromArgb(alpha, red, green, blue);
            }
        }

        private static readonly RayLayer[] RayLayers = {
            new RayLayer(19, 3.2f, 22, 255, 46, 5),
            new RayLayer(12, 2.1f, 45, 255, 65, 6),
            new RayLayer(7.3f, 1.4f, 125, 255, 85, 16),
            new RayLayer(4.3f, 0.86f, 217, 255, 152, 61),
            new RayLayer(2.35f, 0.56f, 250, 255, 246, 209),
            new RayLayer(0.95f, 0.25f, 255, 255, 255, 252)
        };

        private static void DrawSpark(Graphics g, float x, float y, float scale, double ms)
        {
            double swell = Clamp(ms / 55);
            double cool = Clamp((ms - 80) / 340);
            float radius = scale * (float)(9 + 36 * swell - 12 * cool);
            RectangleF bounds = new RectangleF(x - radius, y - radius, radius * 2, radius * 2);
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(bounds);
                using (PathGradientBrush halo = new PathGradientBrush(circle))
                {
                    halo.CenterPoint = new PointF(x, y);
                    halo.CenterColor = Color.FromArgb(250, 255, 250, 218);
                    halo.SurroundColors = new Color[] { Color.FromArgb(0, 255, 67, 0) };
                    ColorBlend blend = new ColorBlend(5);
                    blend.Positions = new float[] { 0f, 0.30f, 0.74f, 0.94f, 1f };
                    blend.Colors = new Color[] {
                        Color.FromArgb(0, 255, 67, 0), Color.FromArgb(9, 255, 77, 8),
                        Color.FromArgb(64, 255, 120, 26), Color.FromArgb(221, 255, 231, 170),
                        Color.FromArgb(255, 255, 255, 249)
                    };
                    halo.InterpolationColors = blend;
                    g.FillEllipse(halo, bounds);
                }
            }
            float core = scale * (float)(2.1 + 2.8 * swell - 1.6 * cool);
            g.FillEllipse(Brushes.White, x - core, y - core, core * 2, core * 2);
        }
    }

    // Only these narrow strips upload pixels each frame. The dimmer uses a constant-alpha HWND.
    internal sealed class BeamStrip : IDisposable
    {
        private readonly bool horizontal;
        private readonly int length;
        private readonly Size size;
        private readonly OverlayWindow window;
        private IntPtr dc, dib, previousBitmap;
        private Bitmap bitmap;
        private Graphics graphics;
        private bool shown;

        internal BeamStrip(bool horizontal, int length, int halfBand)
        {
            this.horizontal = horizontal;
            this.length = length;
            int band = halfBand * 2 + 1;
            size = horizontal ? new Size(length, band) : new Size(band, length);
            window = new OverlayWindow(horizontal ? "Horizontal" : "Vertical", new Rectangle(Point.Empty, size), false);
            try
            {
                dc = OverlayNative.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) throw new Win32Exception();
                OverlayNative.BitmapInfoHeader info = new OverlayNative.BitmapInfoHeader();
                info.Size = (uint)Marshal.SizeOf(typeof(OverlayNative.BitmapInfoHeader));
                info.Width = size.Width;
                info.Height = -size.Height;
                info.Planes = 1;
                info.BitCount = 32;
                IntPtr pixels;
                dib = OverlayNative.CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new Win32Exception();
                previousBitmap = OverlayNative.SelectObject(dc, dib);
                if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1)) throw new Win32Exception();
                bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, pixels);
                graphics = Graphics.FromImage(bitmap);
            }
            catch { Dispose(); throw; }
        }

        internal void Paint(Rectangle screen, Point tip, int halfBand, float scale, double ms)
        {
            Point destination = horizontal ? new Point(screen.Left, tip.Y - halfBand)
                                           : new Point(tip.X - halfBand, screen.Top);
            graphics.ResetClip();
            graphics.Clear(Color.Transparent);
            graphics.SetClip(new Rectangle(screen.Left - destination.X, screen.Top - destination.Y,
                                           screen.Width, screen.Height));
            float origin = horizontal ? tip.X - screen.Left : tip.Y - screen.Top;
            BurstPainter.DrawStrip(graphics, horizontal, length, halfBand, origin, scale, ms);
            graphics.Flush(FlushIntention.Sync);
            Size frameSize = size;
            Point source = Point.Empty;
            OverlayNative.BlendFunction blend = new OverlayNative.BlendFunction();
            blend.SourceConstantAlpha = BurstPainter.BeamAlpha(ms);
            blend.AlphaFormat = 1; // AC_SRC_ALPHA
            if (!OverlayNative.UpdateLayeredWindow(window.Handle, IntPtr.Zero, ref destination, ref frameSize,
                                                  dc, ref source, 0, ref blend, 2))
                throw new Win32Exception();
            if (!shown)
            {
                window.Show();
                shown = true;
            }
        }

        public void Dispose()
        {
            window.Dispose();
            if (graphics != null) graphics.Dispose();
            if (bitmap != null) bitmap.Dispose();
            if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                OverlayNative.SelectObject(dc, previousBitmap);
            if (dib != IntPtr.Zero) OverlayNative.DeleteObject(dib);
            if (dc != IntPtr.Zero) OverlayNative.DeleteDC(dc);
            graphics = null;
            bitmap = null;
            previousBitmap = dib = dc = IntPtr.Zero;
        }
    }

    internal sealed class OverlayWindow : NativeWindow, IDisposable
    {
        private readonly bool shade;
        private int lastAlpha = -1;

        internal OverlayWindow(string title, Rectangle bounds, bool shade)
        {
            this.shade = shade;
            CreateParams parameters = new CreateParams();
            parameters.Caption = "EvaLocator." + title;
            parameters.Style = unchecked((int)0x80000000); // WS_POPUP
            // WS_EX_TOPMOST | TRANSPARENT | TOOLWINDOW | LAYERED | NOACTIVATE
            parameters.ExStyle = 0x8 | 0x20 | 0x80 | 0x80000 | 0x08000000;
            parameters.X = bounds.X;
            parameters.Y = bounds.Y;
            parameters.Width = bounds.Width;
            parameters.Height = bounds.Height;
            CreateHandle(parameters);
        }

        internal void SetShade(byte alpha)
        {
            if (lastAlpha == alpha) return;
            if (!OverlayNative.SetLayeredWindowAttributes(Handle, 0, alpha, 2))
                throw new Win32Exception();
            lastAlpha = alpha;
        }

        internal void Show()
        {
            // Raise within the topmost group without transferring focus.
            if (!OverlayNative.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40))
                throw new Win32Exception();
            if (shade) OverlayNative.UpdateWindow(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (shade && m.Msg == 0x14) { m.Result = new IntPtr(1); return; }
            if (shade && m.Msg == 0x0f)
            {
                OverlayNative.PaintStruct paint;
                IntPtr dc = OverlayNative.BeginPaint(Handle, out paint);
                if (dc != IntPtr.Zero)
                    OverlayNative.FillRect(dc, ref paint.Rect, OverlayNative.GetStockObject(4)); // BLACK_BRUSH
                OverlayNative.EndPaint(Handle, ref paint);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }

    internal static class OverlayNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { internal int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PaintStruct
        {
            internal IntPtr Dc;
            internal int Erase;
            internal Rect Rect;
            internal int Restore, IncUpdate;
            // PAINTSTRUCT.rgbReserved[32] is opaque.
            internal int R0, R1, R2, R3, R4, R5, R6, R7;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ColorsUsed, ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BlendFunction
        {
            internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        internal static float GetScale(Point point)
        {
            uint x, y;
            int result = GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y);
            return result == 0 ? Math.Max(1, x / 96f) : 1;
        }

        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr window, int key, byte alpha, int flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr desktopDC, ref Point position, ref Size size, IntPtr sourceDC, ref Point source, int key, ref BlendFunction blend, int flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] internal static extern bool UpdateWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
        [DllImport("user32.dll")] internal static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
        [DllImport("gdi32.dll")] internal static extern IntPtr GetStockObject(int index);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    }
}
