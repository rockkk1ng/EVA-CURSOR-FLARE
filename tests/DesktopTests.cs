using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EvaLocator.Tests
{
    // Run on a private, never-displayed desktop. The test performs hit tests, creates
    // real HWNDs and checks activation without synthesizing pointer or keyboard input.
    internal static class DesktopTests
    {
        private const uint DesktopAccess = 0x01ff;
        private const int ExtendedStyle = -20;
        private const int RequiredStyles = 0x00080000 | 0x08000000 | 0x20 | 0x80 | 0x08;

        [MTAThread]
        public static int Main()
        {
            IntPtr desktop = CreateDesktop("EvaLocator.Tests." + Guid.NewGuid().ToString("N"),
                IntPtr.Zero, IntPtr.Zero, 0, DesktopAccess, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
            {
                Console.Error.WriteLine(new Win32Exception(Marshal.GetLastWin32Error()));
                return 1;
            }

            IntPtr previousDesktop = GetThreadDesktop(GetCurrentThreadId());
            try
            {
                if (!SetThreadDesktop(desktop))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                TestOverlay();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                if (SetThreadDesktop(previousDesktop))
                    CloseDesktop(desktop);
                // A remaining internal WinForms HWND can bind this thread until process exit.
                // Windows then closes the desktop together with the thread's handles.
            }
            return 0;
        }

        private static void TestOverlay()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Point tip = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            using (Form receiver = new Form())
            {
                receiver.FormBorderStyle = FormBorderStyle.None;
                receiver.StartPosition = FormStartPosition.Manual;
                receiver.Bounds = bounds;
                receiver.Show();
                SetActiveWindow(receiver.Handle);
                Application.DoEvents();
                IntPtr active = GetActiveWindow();
                Require(active == receiver.Handle, "Unable to establish a baseline active window.");
                Point offAxis = new Point(tip.X + 60, tip.Y + 60);
                Require(WindowFromPoint(tip) == receiver.Handle, "Baseline hit test failed.");
                IntPtr foreground = GetForegroundWindow();
                HashSet<IntPtr> baseline = ThreadWindows();
                List<IntPtr> overlays;

                using (BurstOverlay overlay = new BurstOverlay())
                {
                    Stopwatch lifetime = Stopwatch.StartNew();
                    overlay.Start(tip);
                    Require(overlay.IsActive, "Start must immediately activate the burst.");
                    PumpFor(60);
                    Require(overlay.IsActive, "The burst disappeared before its light could expand.");
                    overlays = new List<IntPtr>();
                    foreach (IntPtr window in ThreadWindows())
                    {
                        if (baseline.Contains(window) || !IsWindowVisible(window)) continue;
                        overlays.Add(window);
                        int style = GetWindowLong(window, ExtendedStyle);
                        Require((style & RequiredStyles) == RequiredStyles,
                            "Visible overlay is missing a layered, click-through, non-activating, tool or topmost flag.");
                    }
                    Require(overlays.Count >= 1, "No visible overlay windows during the active burst.");
                    Require(WindowFromPoint(tip) == receiver.Handle &&
                            WindowFromPoint(offAxis) == receiver.Handle,
                        "The underlying window lost mouse hit testing.");
                    Require(GetActiveWindow() == active && GetForegroundWindow() == foreground,
                        "The active or foreground window changed during the effect.");
                    Console.WriteLine("PASS - {0} visible click-through HWNDs; focus and hit targets preserved.",
                        overlays.Count);

                    while (overlay.IsActive && lifetime.ElapsedMilliseconds < 2300)
                        PumpFor(4);
                    Require(!overlay.IsActive && lifetime.ElapsedMilliseconds >= 380 &&
                        lifetime.ElapsedMilliseconds < 1600, "Unexpected burst duration: " + lifetime.Elapsed);
                    RequireAllHidden(overlays);
                    Console.WriteLine("PASS - automatic fade finished in {0} ms.", lifetime.ElapsedMilliseconds);

                    overlay.Start(tip);
                    overlay.Start(tip);
                    Require(overlay.IsActive, "Restart failed.");
                    overlay.Stop();
                    overlay.Stop();
                    Require(!overlay.IsActive, "Stop must deactivate the overlay.");
                    RequireAllHidden(overlays);
                    overlay.Start(tip);
                    Require(overlay.IsActive, "Cannot restart after Stop.");
                }
                RequireAllHidden(overlays);
                foreach (IntPtr window in ThreadWindows())
                    Require(baseline.Contains(window) || !IsWindowVisible(window),
                        "A visible window leaked after Dispose.");
                Console.WriteLine("PASS - repeated start/stop and disposal during animation.");
            }
        }

        private static void RequireAllHidden(List<IntPtr> windows)
        {
            foreach (IntPtr window in windows)
                Require(!IsWindowVisible(window), "A completed burst left an overlay visible.");
        }

        private static HashSet<IntPtr> ThreadWindows()
        {
            HashSet<IntPtr> windows = new HashSet<IntPtr>();
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr hwnd, IntPtr arg)
            {
                windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static void PumpFor(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                Thread.Sleep(1);
            } while (watch.ElapsedMilliseconds < milliseconds);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private delegate bool EnumWindow(IntPtr window, IntPtr arg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device,
            IntPtr mode, uint flags, uint access, IntPtr security);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll")]
        private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint id, EnumWindow callback, IntPtr arg);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
