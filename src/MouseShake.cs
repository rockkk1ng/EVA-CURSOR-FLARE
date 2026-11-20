using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EvaLocator
{
    // A single gesture must contain three decisive reversals on roughly the same axis.
    // All times are unsigned WM_INPUT message times, including across the 49-day wrap.
    internal sealed class ShakeDetector
    {
        private const double DirectionSample = 12;
        private const double ReverseDistance = 36;
        private const uint MaxWindowMs = 420;
        private const uint PauseMs = 190;
        private const uint CooldownMs = 800;

        private double sampleX, sampleY, axisX, axisY, position, extreme;
        private int direction, reversalCount;
        private uint previousMotion, previousReversal, olderReversal, lastTrigger;
        private bool triggeredSinceBreak, hasTriggered;

        internal void Reset()
        {
            ResetTrack();
            triggeredSinceBreak = hasTriggered = false;
        }

        private void ResetTrack()
        {
            sampleX = sampleY = position = extreme = 0;
            reversalCount = direction = 0;
        }

        internal bool Move(double dx, double dy, uint time)
        {
            if (dx == 0 && dy == 0)
                return false;

            if (unchecked(time - previousMotion) > PauseMs)
            {
                ResetTrack();
                triggeredSinceBreak = false;
            }
            previousMotion = time;

            // Small hand tremors must not establish a new direction. Integrating first
            // also makes the result independent of the device's packet frequency.
            sampleX += dx;
            sampleY += dy;
            double squaredLength = sampleX * sampleX + sampleY * sampleY;
            if (squaredLength < DirectionSample * DirectionSample)
                return false;
            double projection = sampleX * axisX + sampleY * axisY;
            if (direction == 0 || projection * projection < 0.5 * squaredLength)
            {
                double length = Math.Sqrt(squaredLength);
                axisX = sampleX / length;
                axisY = sampleY / length;
                position = extreme = length;
                direction = 1;
                reversalCount = 0;
            }
            else
                position += projection;
            sampleX = sampleY = 0;

            double retreat = direction * (extreme - position);
            if (retreat <= 0)
            {
                extreme = position;
                return false;
            }
            if (retreat < ReverseDistance)
                return false;

            extreme = position;
            direction = -direction;
            if (unchecked(time - previousReversal) > MaxWindowMs)
            {
                reversalCount = 0;
                triggeredSinceBreak = false;
            }
            bool fastSequence = reversalCount >= 2 && unchecked(time - olderReversal) <= MaxWindowMs;
            olderReversal = previousReversal;
            previousReversal = time;
            reversalCount = Math.Min(2, reversalCount + 1);
            if (!fastSequence || triggeredSinceBreak ||
                (hasTriggered && unchecked(time - lastTrigger) < CooldownMs))
                return false;

            triggeredSinceBreak = hasTriggered = true;
            lastTrigger = time;
            return true;
        }
    }

    internal sealed class MouseShake : NativeWindow, IDisposable
    {
        private const int WmInput = 0x00ff;
        private const uint RidInput = 0x10000003;
        private const uint RidevInputSink = 0x100;
        private const uint RidevRemove = 1;
        private const ushort MoveAbsolute = 1;
        private const ushort VirtualDesktop = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RawDevice
        {
            public ushort Page, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawHeader
        {
            public uint Type, Size;
            public IntPtr Device, WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MousePacket
        {
            public RawHeader Header;
            public ushort Flags, Padding;
            public uint Buttons, RawButtons;
            public int X, Y;
            public uint Extra;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(ref RawDevice device, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr handle, uint command, out MousePacket packet,
                                                   ref uint bytes, uint headerSize);

        [DllImport("user32.dll")]
        private static extern uint GetMessageTime();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private static readonly uint HeaderBytes = (uint)Marshal.SizeOf(typeof(RawHeader));
        private static readonly uint PacketBytes = (uint)Marshal.SizeOf(typeof(MousePacket));
        private static readonly uint DeviceBytes = (uint)Marshal.SizeOf(typeof(RawDevice));
        private readonly Action onShake;
        private readonly ShakeDetector detector = new ShakeDetector();
        private bool enabled = true, hasSource;
        private IntPtr source;
        private ushort coordinateMode;
        private int lastX, lastY;

        internal MouseShake(Action onShake)
        {
            if (onShake == null)
                throw new ArgumentNullException("onShake");
            this.onShake = onShake;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3), Caption = "EvaLocator.Input" });
            RawDevice device = new RawDevice { Page = 1, Usage = 2, Flags = RidevInputSink, Target = Handle };
            if (!RegisterRawInputDevices(ref device, 1, DeviceBytes))
            {
                int error = Marshal.GetLastWin32Error();
                DestroyHandle();
                throw new Win32Exception(error);
            }
        }

        internal bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value)
                    return;
                enabled = value;
                hasSource = false;
                detector.Reset();
            }
        }

        protected override void WndProc(ref Message message)
        {
            bool fire = message.Msg == WmInput && enabled && ReadMotion(message.LParam);
            // DefWindowProc performs the required cleanup for foreground WM_INPUT.
            base.WndProc(ref message);
            if (fire)
                onShake();
        }

        private bool ReadMotion(IntPtr handle)
        {
            MousePacket packet;
            uint size = PacketBytes;
            if (GetRawInputData(handle, RidInput, out packet, ref size, HeaderBytes) != PacketBytes ||
                packet.Header.Type != 0)
                return false;

            ushort mode = (ushort)(packet.Flags & (MoveAbsolute | VirtualDesktop));
            bool absolute = (mode & MoveAbsolute) != 0;
            if (!absolute && packet.X == 0 && packet.Y == 0)
                return false;

            bool newSource = !hasSource || packet.Header.Device != source || mode != coordinateMode;
            if (newSource)
            {
                detector.Reset();
                source = packet.Header.Device;
                coordinateMode = mode;
                hasSource = true;
            }
            if (!absolute)
                return detector.Move(packet.X, packet.Y, GetMessageTime());

            // Absolute devices send 16-bit normalized coordinates on either the
            // primary screen or the virtual desktop. Differencing avoids origin bias.
            bool virtualDesktop = (mode & VirtualDesktop) != 0;
            int width = GetSystemMetrics(virtualDesktop ? 78 : 0);
            int height = GetSystemMetrics(virtualDesktop ? 79 : 1);
            double dx = (packet.X - lastX) * (width / 65535.0);
            double dy = (packet.Y - lastY) * (height / 65535.0);
            lastX = packet.X;
            lastY = packet.Y;
            return !newSource && detector.Move(dx, dy, GetMessageTime());
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
                return;
            enabled = false;
            RawDevice device = new RawDevice { Page = 1, Usage = 2, Flags = RidevRemove };
            RegisterRawInputDevices(ref device, 1, DeviceBytes);
            DestroyHandle();
        }
    }
}
