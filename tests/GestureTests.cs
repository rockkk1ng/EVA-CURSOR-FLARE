using System;
using EvaLocator;

internal static class GestureTests
{
    private sealed class Trace
    {
        internal readonly ShakeDetector Detector = new ShakeDetector();
        internal uint Time = 5000;
        internal int Fires;

        internal void Move(double x, double y, int packets, uint interval)
        {
            for (int i = 0; i < packets; ++i)
            {
                Time = unchecked(Time + interval);
                if (Detector.Move(x, y, Time))
                    ++Fires;
            }
        }

        internal void Shake(double x, double y, int strokes, int packets, uint interval)
        {
            for (int i = 0; i < strokes; ++i)
            {
                int sign = (i % 2 == 0) ? 1 : -1;
                Move(sign * x, sign * y, packets, interval);
            }
        }
    }

    private static int checks;
    private static void Check(string name, bool ok)
    {
        if (!ok)
            throw new Exception(name);
        Console.WriteLine("PASS " + name);
        ++checks;
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Trace trace = new Trace();
            trace.Shake(4, 0, 4, 18, 4);
            Check("horizontal shake", trace.Fires == 1);

            trace = new Trace();
            trace.Shake(0, -5, 4, 16, 4);
            Check("vertical shake", trace.Fires == 1);

            trace = new Trace();
            trace.Shake(3.8, -2.9, 4, 16, 4);
            Check("diagonal shake", trace.Fires == 1);

            trace = new Trace();
            trace.Shake(115, -36, 4, 1, 64);
            Check("coalesced large packets", trace.Fires == 1);

            trace = new Trace();
            trace.Shake(0.8, 0, 4, 100, 1);
            Check("high report rate", trace.Fires == 1);

            trace = new Trace();
            trace.Move(24, 6, 180, 1);
            trace.Move(-24, -6, 15, 1);
            trace.Move(24, 6, 3, 1);
            Check("single flick and correction", trace.Fires == 0);

            trace = new Trace();
            trace.Shake(1, -0.2, 80, 14, 3);
            Check("small tremors", trace.Fires == 0);

            trace = new Trace();
            trace.Shake(4, 1, 14, 18, 20);
            Check("slow sweeps", trace.Fires == 0);

            trace = new Trace();
            for (int i = 0; i < 180; ++i)
            {
                double angle = i * Math.PI / 18.0;
                trace.Move(16 * Math.Cos(angle), 16 * Math.Sin(angle), 1, 8);
            }
            Check("continuous circular movement", trace.Fires == 0);

            trace = new Trace();
            trace.Shake(5, 1, 18, 16, 4);
            Check("long continuous shake triggers once", trace.Fires == 1);
            trace.Time += 900;
            trace.Shake(-5, 0, 4, 16, 4);
            Check("second shake after pause", trace.Fires == 2);

            trace = new Trace();
            trace.Shake(5, 1, 4, 16, 4);
            trace.Time += 200;
            trace.Shake(5, 1, 4, 16, 4);
            Check("brief pause respects cooldown", trace.Fires == 1);

            trace = new Trace();
            trace.Shake(5, 1, 3, 16, 4);
            trace.Detector.Reset();
            trace.Move(-5, -1, 16, 4);
            Check("reset drops partial gesture", trace.Fires == 0);

            trace = new Trace();
            trace.Time = uint.MaxValue - 120;
            trace.Shake(7, 3, 4, 12, 5);
            Check("input timestamp wraps", trace.Fires == 1);

            using (MouseShake mouse = new MouseShake(delegate { }))
            {
                Check("Windows raw input registration", mouse.Handle != IntPtr.Zero && mouse.Enabled);
                mouse.Enabled = false;
                Check("pause and resume", !mouse.Enabled);
                mouse.Enabled = true;
                Check("resume", mouse.Enabled);
            }
            Console.WriteLine("{0} checks passed", checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }
}
