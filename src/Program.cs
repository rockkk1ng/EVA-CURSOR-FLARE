using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("EVA 鼠标定位")]
[assembly: AssemblyDescription("摇晃鼠标，在指针尖端展开十字光爆")]
[assembly: AssemblyProduct("EVA Mouse Locator")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EvaLocator
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                bool firstInstance;
                using (var preview = new EventWaitHandle(false, EventResetMode.AutoReset,
                    @"Local\EvaMouseLocator.Preview.7D59CE22", out firstInstance))
                {
                    if (!firstInstance)
                    {
                        preview.Set();
                        return 0;
                    }

                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new LocatorContext(preview))
                        Application.Run(context);
                }
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("鼠标定位工具已停止。\r\n\r\n" + error.Message,
                    "EVA 鼠标定位", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal sealed class LocatorContext : ApplicationContext
    {
        private readonly BurstOverlay overlay;
        private readonly MouseShake gesture;
        private readonly Control dispatcher;
        private readonly Icon icon;
        private readonly ContextMenuStrip menu;
        private readonly NotifyIcon tray;
        private readonly RegisteredWaitHandle previewRequest;
        private bool closing;

        internal LocatorContext(EventWaitHandle preview)
        {
            try
            {
                overlay = new BurstOverlay();
                gesture = new MouseShake(ShowBurst);
                dispatcher = new Control();
                dispatcher.CreateControl();

                menu = new ContextMenuStrip();
                menu.Items.Add("预览光爆", null, delegate { ShowBurst(); });
                var enabled = new ToolStripMenuItem("摇晃触发");
                enabled.Checked = true;
                enabled.CheckOnClick = true;
                enabled.CheckedChanged += delegate
                {
                    gesture.Enabled = enabled.Checked;
                    if (!gesture.Enabled) overlay.Stop();
                    UpdateTooltip();
                };
                menu.Items.Add(enabled);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出", null, delegate { ExitThread(); });

                icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                tray = new NotifyIcon();
                tray.Icon = icon ?? SystemIcons.Application;
                tray.ContextMenuStrip = menu;
                tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) ShowBurst();
                };
                UpdateTooltip();
                tray.Visible = true;
                // A second launch requests a preview on the existing UI thread.
                previewRequest = ThreadPool.RegisterWaitForSingleObject(preview,
                    delegate { QueuePreview(); }, null, Timeout.Infinite, false);
                dispatcher.BeginInvoke(new MethodInvoker(ShowBurst));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void UpdateTooltip()
        {
            tray.Text = gesture.Enabled ? "EVA 鼠标定位 · 摇晃鼠标触发" : "EVA 鼠标定位 · 已暂停";
        }

        private void QueuePreview()
        {
            try { dispatcher.BeginInvoke(new MethodInvoker(ShowBurst)); }
            catch (InvalidOperationException) { /* The UI thread is exiting. */ }
        }

        private void ShowBurst()
        {
            if (!closing) overlay.Start(Cursor.Position);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing)
            {
                closing = true;
                if (previewRequest != null) previewRequest.Unregister(null);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose();
                if (gesture != null) gesture.Dispose();
                if (overlay != null) overlay.Dispose();
                if (dispatcher != null) dispatcher.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
