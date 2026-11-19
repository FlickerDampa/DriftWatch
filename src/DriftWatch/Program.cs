using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DriftWatch.UI;

namespace DriftWatch
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // No arguments, "--open" or a single report (double-click / drag onto the exe) opens the window.
            // Anything else is command-line mode for scripts and scheduled tasks.
            bool open = args.Length > 0 && string.Equals(args[0], "--open", StringComparison.OrdinalIgnoreCase);
            bool singleFile = args.Length == 1 && File.Exists(args[0]) && args[0].EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
            if (args.Length > 0 && !open && !singleFile) return Cli.Run(args);

            Native.DpiAware();
            Dpi.Init();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var files = open ? args.Skip(1).ToArray() : args;
            var form = new MainForm(files);
            Application.Run(form);
            return 0;
        }

        private static bool reporting;

        /// <summary>Keeps the window open after an unexpected error and tells the user what happened.</summary>
        private static void ReportError(Exception ex)
        {
            try
            {
                string log = Path.Combine(DriftWatch.Core.AppSettings.Folder, "error.log");
                Directory.CreateDirectory(DriftWatch.Core.AppSettings.Folder);
                File.AppendAllText(log, DateTime.Now.ToString("s") + " " + DriftWatch.Core.AppInfo.Version + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
                if (reporting) return;
                reporting = true;
                MessageBox.Show("Something went wrong: " + ex.Message + Environment.NewLine + Environment.NewLine + "DriftWatch will keep running. Details were saved to " + log + ". Please include that file if you report the problem.",
                    "DriftWatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception) { }
            finally { reporting = false; }
        }
    }
}
