using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CutoutEqD.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
#if NET5_0_OR_GREATER
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#else
            try { SetProcessDPIAware(); } catch { /* older Windows / non-Windows: ignore */ }
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            if (args.Length > 0) form.Shown += (_, _) => form.OpenFromCommandLine(args[0]);
            Application.Run(form);
        }

#if !NET5_0_OR_GREATER
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
#endif
    }
}
