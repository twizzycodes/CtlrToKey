using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Ctlr To Key")]
[assembly: AssemblyProduct("Ctlr To Key")]
[assembly: AssemblyDescription("Map controller buttons and combos to keyboard shortcuts")]
[assembly: AssemblyVersion("7.3.0.0")]
[assembly: AssemblyFileVersion("7.3.0.0")]

namespace CtlrToKey
{
    static class Program
    {
        public static Icon AppIcon;
        public static float Scale = 1f;

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (Mutex mx = new Mutex(true, "CtlrToKey_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Ctlr To Key is already running.\nLook for its icon in the system tray.", "Ctlr To Key",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CtlrToKey.app.ico"))
                        AppIcon = new Icon(s);
                }
                catch (Exception)
                {
                    AppIcon = SystemIcons.Application;
                }
                bool min = args.Any(a => a.Equals("/min", StringComparison.OrdinalIgnoreCase) || a.Equals("-min", StringComparison.OrdinalIgnoreCase));
                Application.Run(new MainForm(min));
            }
        }
    }
}
