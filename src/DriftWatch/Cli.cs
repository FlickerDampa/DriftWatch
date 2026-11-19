using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DriftWatch.Core;
using DriftWatch.Export;

namespace DriftWatch
{
    /// <summary>
    /// Command-line mode for scripts and scheduled tasks. Exit codes:
    /// 0 = nothing at or above the --fail-on level, 1 = changes at or above it, 2 = invalid arguments, 3 = a report could not be read.
    /// </summary>
    public static class Cli
    {
        public const int Clean = 0, Found = 1, BadArgs = 2, Failed = 3;

        private sealed class Options
        {
            public string Baseline, Current, Folder, Html, Json, Csv, Markdown, PolicyFile, PostureFile;
            public int MinScore = -1;
            public bool Recurse, Quiet, Help, Version;
            public Severity FailOn = Severity.High;
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        public static int Run(string[] args)
        {
            TryAttachConsole();
            try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch (IOException) { }
            Options o;
            string error;
            if (!Parse(args, out o, out error))
            {
                Console.Error.WriteLine("DriftWatch: " + error);
                Console.Error.WriteLine("Run DriftWatch.exe --help for usage.");
                return BadArgs;
            }
            if (o.Help) { Console.WriteLine(Usage()); return Clean; }
            if (o.Version) { Console.WriteLine(AppInfo.Name + " " + AppInfo.Version); return Clean; }

            try
            {
                Policy policy = o.PolicyFile != null ? Policy.Load(o.PolicyFile) : new Policy();
                if (o.PostureFile != null) return RunPosture(o, policy);
                return o.Folder != null ? RunFleet(o, policy) : RunPair(o, policy);
            }
            catch (Exception ex)
            {
                if (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("DriftWatch: " + ex.Message);
                    return Failed;
                }
                throw;
            }
        }

        private static int RunPosture(Options o, Policy policy)
        {
            var snap = Snapshot.Load(o.PostureFile);
            var p = PostureEngine.Evaluate(snap, policy);
            if (o.Json != null) Write(o.Json, JsonReport.Posture(p) + "\n");
            if (!o.Quiet)
            {
                Console.WriteLine(snap.Device + ": security score " + p.Score + "/100, grade " + p.Grade + " (" + p.Passed + " passing, " + p.Warnings + " warnings, " + p.Failed + " failing)");
                foreach (var c in p.Checks.Where(x => x.Status == CheckStatus.Fail || x.Status == CheckStatus.Warn).OrderBy(x => x.Status == CheckStatus.Fail ? 0 : 1))
                    Console.WriteLine("  [" + (c.Status == CheckStatus.Fail ? "FAIL" : "WARN") + "] " + c.Title + ": " + c.Detail);
                if (o.Json != null) Console.WriteLine("JSON report: " + Path.GetFullPath(o.Json));
            }
            int min = o.MinScore >= 0 ? o.MinScore : (o.PolicyFile != null ? policy.MinimumScore : -1);
            return min >= 0 && p.Score < min ? Found : Clean;
        }

        private static void Write(string path, string text)
        {
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
        }

        private static int RunPair(Options o, Policy policy)
        {
            var cmp = DiffEngine.Compare(Snapshot.Load(o.Baseline), Snapshot.Load(o.Current));
            PolicyEngine.Apply(cmp, policy);
            var before = PostureEngine.Evaluate(cmp.Baseline, policy);
            var after = PostureEngine.Evaluate(cmp.Current, policy);
            if (o.Html != null) Write(o.Html, HtmlReport.Build(cmp, before, after));
            if (o.Json != null) JsonReport.Save(cmp, o.Json);
            if (o.Csv != null) Write(o.Csv, CsvReport.Changes(cmp));
            if (o.Markdown != null) Write(o.Markdown, MarkdownReport.Summary(cmp, before, after));
            if (!o.Quiet)
            {
                Console.WriteLine(cmp.Current.Device + ": " + cmp.Verdict + " (" + cmp.Changes.Count + " changes, " + Counts(cmp) + "), security score " + before.Score + " -> " + after.Score);
                foreach (var n in cmp.Notices) Console.WriteLine("  note: " + n);
                foreach (var ch in cmp.Changes.Where(c => c.Severity <= Severity.Medium))
                    Console.WriteLine("  [" + ch.Severity.ToString().ToUpperInvariant() + "] " + ch.Title);
                if (o.Html != null) Console.WriteLine("HTML report: " + Path.GetFullPath(o.Html));
                if (o.Json != null) Console.WriteLine("JSON report: " + Path.GetFullPath(o.Json));
                if (o.Csv != null) Console.WriteLine("CSV: " + Path.GetFullPath(o.Csv));
                if (o.Markdown != null) Console.WriteLine("Summary: " + Path.GetFullPath(o.Markdown));
            }
            bool lowScore = o.MinScore >= 0 && after.Score < o.MinScore;
            return cmp.AnyAtOrAbove(o.FailOn) || lowScore ? Found : Clean;
        }

        private static int RunFleet(Options o, Policy policy)
        {
            var ws = Workspace.FromFolder(o.Folder, o.Recurse, policy);
            var fleet = new FleetResult { Folder = ws.Folder };
            fleet.Devices.AddRange(ws.Devices);
            fleet.Skipped.AddRange(ws.Skipped);
            foreach (var d in ws.Devices) { var c = ws.Latest(d); if (c != null) fleet.Comparisons.Add(c); }
            fleet.Comparisons.Sort((a, b) =>
            {
                int sa = a.Highest.HasValue ? (int)a.Highest.Value : 99, sb = b.Highest.HasValue ? (int)b.Highest.Value : 99;
                return sa != sb ? sa.CompareTo(sb) : b.ActiveCount.CompareTo(a.ActiveCount);
            });
            if (o.Html != null) Write(o.Html, HtmlReport.BuildFleet(ws));
            if (o.Json != null) JsonReport.SaveFleet(fleet, o.Json, ws);
            if (o.Csv != null) Write(o.Csv, CsvReport.Devices(ws));
            if (!o.Quiet)
            {
                Console.WriteLine(fleet.Devices.Count + " devices, " + fleet.Comparisons.Count + " compared, average security score " + Math.Round(ws.AverageScore) + ".");
                foreach (var cmp in fleet.Comparisons)
                    Console.WriteLine("  " + cmp.Current.Device + ": " + cmp.Verdict + " (" + Counts(cmp) + "), score " + ws.Posture(cmp.Current).Score);
                foreach (var d in fleet.Devices.Where(d => d.Previous == null))
                    Console.WriteLine("  " + d.Device + ": one audit only");
                foreach (var s in fleet.Skipped) Console.WriteLine("  skipped " + s);
                if (o.Html != null) Console.WriteLine("HTML report: " + Path.GetFullPath(o.Html));
                if (o.Json != null) Console.WriteLine("JSON report: " + Path.GetFullPath(o.Json));
                if (o.Csv != null) Console.WriteLine("CSV: " + Path.GetFullPath(o.Csv));
            }
            bool lowScore = o.MinScore >= 0 && ws.Devices.Any(d => ws.Posture(d.Latest).Score < o.MinScore);
            return fleet.Comparisons.Any(c => c.AnyAtOrAbove(o.FailOn)) || lowScore ? Found : Clean;
        }

        private static string Counts(Comparison c)
        {
            var parts = SeverityText.All.Where(s => c.Count(s) > 0).Select(s => c.Count(s) + " " + s.ToString().ToLowerInvariant()).ToArray();
            return parts.Length == 0 ? "none" : string.Join(", ", parts);
        }

        private static bool Parse(string[] args, out Options o, out string error)
        {
            o = new Options();
            error = null;
            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string lower = a.ToLowerInvariant();
                Func<string> next = () => i + 1 < args.Length ? args[++i] : null;
                switch (lower)
                {
                    case "--help": case "-h": case "/?": o.Help = true; break;
                    case "--version": o.Version = true; break;
                    case "--quiet": case "--silent": o.Quiet = true; break;
                    case "--recurse": o.Recurse = true; break;
                    case "--baseline": case "--older": o.Baseline = next(); if (o.Baseline == null) { error = a + " needs a file path."; return false; } break;
                    case "--current": case "--newer": o.Current = next(); if (o.Current == null) { error = a + " needs a file path."; return false; } break;
                    case "--folder": o.Folder = next(); if (o.Folder == null) { error = "--folder needs a folder path."; return false; } break;
                    case "--html": o.Html = next(); if (o.Html == null) { error = "--html needs a file path."; return false; } break;
                    case "--json": o.Json = next(); if (o.Json == null) { error = "--json needs a file path."; return false; } break;
                    case "--csv": o.Csv = next(); if (o.Csv == null) { error = "--csv needs a file path."; return false; } break;
                    case "--markdown": case "--md": o.Markdown = next(); if (o.Markdown == null) { error = "--markdown needs a file path."; return false; } break;
                    case "--policy": o.PolicyFile = next(); if (o.PolicyFile == null) { error = "--policy needs a file path."; return false; } break;
                    case "--posture": o.PostureFile = next(); if (o.PostureFile == null) { error = "--posture needs a report file."; return false; } break;
                    case "--min-score":
                        int ms;
                        string msv = next();
                        if (msv == null || !int.TryParse(msv, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms < 0 || ms > 100) { error = "--min-score needs a number from 0 to 100."; return false; }
                        o.MinScore = ms;
                        break;
                    case "--fail-on":
                        string level = next();
                        Severity s;
                        if (level == null || !SeverityText.TryParse(level, out s)) { error = "--fail-on needs one of: critical, high, medium, low, info."; return false; }
                        o.FailOn = s;
                        break;
                    default:
                        if (a.StartsWith("--", StringComparison.Ordinal)) { error = "Unknown option " + a + "."; return false; }
                        positional.Add(a);
                        break;
                }
            }
            if (o.Help || o.Version) return true;
            if (o.PostureFile != null)
            {
                if (positional.Count > 0 || o.Folder != null) { error = "--posture takes one report on its own."; return false; }
                return true;
            }
            if (o.Baseline == null && positional.Count > 0) o.Baseline = positional[0];
            if (o.Current == null && positional.Count > 1) o.Current = positional[1];
            if (o.Folder != null && (o.Baseline != null || o.Current != null)) { error = "Use either --folder or two report files, not both."; return false; }
            if (o.Folder == null && (o.Baseline == null || o.Current == null)) { error = "Give two DeviceDossier XML reports to compare, or --folder with a folder of reports."; return false; }
            return true;
        }

        public static string Usage()
        {
            return AppInfo.Name + " " + AppInfo.Version + " - " + AppInfo.Tagline + @"

Compare two audits of one PC:
  DriftWatch.exe OLDER.xml NEWER.xml [--html report.html] [--json report.json] [--fail-on high]

Compare every PC in a folder of DeviceDossier reports (latest audit against the one before):
  DriftWatch.exe --folder ""\\server\it\Audits"" [--recurse] [--html fleet.html] [--json fleet.json]

Score one audit's security posture:
  DriftWatch.exe --posture REPORT.xml [--json posture.json] [--min-score 70]

Options:
  --baseline, --current   Name the older and newer report explicitly.
  --folder PATH           Scan a folder of DeviceDossier XML reports and group them by device.
  --recurse               Include subfolders when scanning.
  --html FILE             Save a self-contained HTML change report.
  --json FILE             Save the changes as JSON for scripts and ticketing tools.
  --csv FILE              Save the changes (or, with --folder, the device list) as CSV.
  --markdown FILE         Save a short summary for a ticket or email (two reports only).
  --policy FILE           Apply a DriftWatch policy (exported from the Policy page).
  --min-score N           Also use exit code 1 when a security score is below N.
  --fail-on LEVEL         critical, high (default), medium, low or info. Sets when exit code 1 is used.
  --quiet                 Print nothing; rely on the exit code and saved reports.
  --version, --help

Exit codes: 0 nothing at or above --fail-on (and no score below --min-score), 1 otherwise, 2 invalid arguments, 3 a report could not be read.
In PowerShell, pipe to Out-Null so the prompt waits, then check $LASTEXITCODE.";
        }

        private static void TryAttachConsole()
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT) AttachConsole(-1);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
