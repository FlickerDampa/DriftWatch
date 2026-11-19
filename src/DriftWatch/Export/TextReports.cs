using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DriftWatch.Core;

namespace DriftWatch.Export
{
    /// <summary>CSV exports that open cleanly in Excel (UTF-8 with a byte order mark is added when saving).</summary>
    public static class CsvReport
    {
        public static string Changes(Comparison c)
        {
            var sb = new StringBuilder();
            Row(sb, "Device", "Severity", "Category", "Type", "Title", "Section", "Table", "Item", "Field", "Before", "After", "Why it matters", "Acknowledged", "Policy note", "Older audit", "Newer audit");
            foreach (var ch in c.Changes)
                Row(sb, c.Current.Device, ch.Severity.ToString(), ch.Category, ch.KindText, ch.Title, ch.SectionTitle, ch.Table, ch.Item, ch.Field, ch.Before, ch.After, ch.Why,
                    ch.Acknowledged ? "Yes" : "No", ch.PolicyNote ?? "", Iso(c.Baseline.Generated), Iso(c.Current.Generated));
            return sb.ToString();
        }

        public static string Devices(Workspace ws)
        {
            var sb = new StringBuilder();
            Row(sb, "Device", "Model", "Operating system", "Score", "Grade", "Failing checks", "Warnings", "Latest audit", "Audits", "Overdue", "Verdict", "Critical", "High", "Medium", "Low", "Info", "Serial number", "Assigned to");
            foreach (var d in ws.Devices)
            {
                var p = ws.Posture(d.Latest);
                var c = ws.Latest(d);
                var s = d.Latest;
                Row(sb, d.Device, Field(s, "device", "Model Name"), Field(s, "os", "OS Name"), p.Score.ToString(CultureInfo.InvariantCulture), p.Grade,
                    string.Join("; ", p.Checks.Where(x => x.Status == CheckStatus.Fail).Select(x => x.Title)), p.Warnings.ToString(CultureInfo.InvariantCulture),
                    Iso(s.Generated), d.Reports.Count.ToString(CultureInfo.InvariantCulture), ws.IsStale(d) ? "Yes" : "No", c == null ? "One audit only" : c.Verdict,
                    N(c, Severity.Critical), N(c, Severity.High), N(c, Severity.Medium), N(c, Severity.Low), N(c, Severity.Info),
                    Field(s, "device", "BIOS Serial Number"), Field(s, "asset", "Assigned To"));
            }
            return sb.ToString();
        }

        public static string Software(Workspace ws)
        {
            var sb = new StringBuilder();
            Row(sb, "Program", "Publisher", "Flag", "PCs", "Versions", "Latest version", "Installed on");
            foreach (var e in ws.Inventory)
                Row(sb, e.Name, e.Publisher, e.Flag ?? "", e.DeviceCount.ToString(CultureInfo.InvariantCulture), string.Join("; ", e.Versions), e.LatestVersion,
                    string.Join("; ", e.Installs.Select(i => i.Device.Device + (i.Version.Length > 0 ? " (" + i.Version + ")" : ""))));
            return sb.ToString();
        }

        public static string Posture(Workspace ws)
        {
            var sb = new StringBuilder();
            if (ws.Devices.Count == 0) return "";
            var template = ws.Posture(ws.Devices[0].Latest).Checks;
            var header = new List<string> { "Device", "Latest audit", "Score", "Grade" };
            header.AddRange(template.Select(c => c.Title));
            Row(sb, header.ToArray());
            foreach (var d in ws.Devices)
            {
                var p = ws.Posture(d.Latest);
                var row = new List<string> { d.Device, Iso(d.Latest.Generated), p.Score.ToString(CultureInfo.InvariantCulture), p.Grade };
                row.AddRange(template.Select(t => { var c = p.Check(t.Id); return c == null ? "" : c.StatusText; }));
                Row(sb, row.ToArray());
            }
            return sb.ToString();
        }

        private static string N(Comparison c, Severity s)
        {
            return c == null ? "" : c.Count(s).ToString(CultureInfo.InvariantCulture);
        }

        public static string Field(Snapshot s, string section, string field)
        {
            var sec = s.Section(section);
            if (sec == null) return "";
            foreach (var b in sec.Blocks)
            {
                if (b.Kind != BlockKind.Fields) continue;
                var v = b.Field(field);
                if (v != null) return v;
            }
            return "";
        }

        private static string Iso(DateTime d)
        {
            return d == DateTime.MinValue ? "" : d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static void Row(StringBuilder sb, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string v = cells[i] ?? "";
                // Stop spreadsheet apps treating text as a formula.
                if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0) v = "'" + v;
                if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
                sb.Append(v);
            }
            sb.Append("\r\n");
        }
    }

    /// <summary>Plain-text summaries for tickets, chat and email.</summary>
    public static class MarkdownReport
    {
        public static string Summary(Comparison c, PostureResult before, PostureResult after)
        {
            var sb = new StringBuilder();
            sb.Append("## DriftWatch: ").Append(c.Current.Device).Append(": ").Append(c.Verdict).Append("\n\n");
            sb.Append("Compared ").Append(Date(c.Baseline.Generated)).Append(" with ").Append(Date(c.Current.Generated));
            if (c.Span.Length > 0) sb.Append(" (").Append(c.Span).Append(")");
            sb.Append(".\n\n");
            if (after != null)
            {
                sb.Append("Security score: **").Append(after.Score).Append("/100** (grade ").Append(after.Grade).Append(")");
                if (before != null && before.Score != after.Score) sb.Append(", was ").Append(before.Score);
                sb.Append(".\n\n");
            }
            var counts = SeverityText.All.Where(s => c.Count(s) > 0).Select(s => c.Count(s) + " " + s.ToString().ToLowerInvariant()).ToArray();
            sb.Append("Open changes: ").Append(counts.Length == 0 ? "none" : string.Join(", ", counts));
            if (c.AcknowledgedCount > 0) sb.Append(" (").Append(c.AcknowledgedCount).Append(" acknowledged)");
            sb.Append(".\n\n");
            var important = c.Active.Where(x => x.Severity <= Severity.Medium).ToList();
            if (important.Count > 0)
            {
                sb.Append("### Needs review\n\n");
                foreach (var ch in important)
                {
                    sb.Append("- **").Append(ch.Severity).Append("** ").Append(ch.Title);
                    if (ch.Kind == ChangeKind.Changed) sb.Append(" (`").Append(Core.Change.Display(ch.Before)).Append("` → `").Append(Core.Change.Display(ch.After)).Append("`)");
                    sb.Append("\n");
                    if (ch.Why.Length > 0) sb.Append("  ").Append(ch.Why).Append("\n");
                }
                sb.Append("\n");
            }
            int routine = c.Active.Count(x => x.Severity > Severity.Medium);
            if (routine > 0) sb.Append(routine).Append(routine == 1 ? " routine change" : " routine changes").Append(" not listed.\n\n");
            foreach (var n in c.Notices) sb.Append("> Note: ").Append(n).Append("\n");
            sb.Append("\n_From ").Append(c.Baseline.FileName).Append(" and ").Append(c.Current.FileName).Append(" with ").Append(AppInfo.Name).Append(' ').Append(AppInfo.Version).Append("._\n");
            return sb.ToString();
        }

        public static string Change(Change ch, Comparison c)
        {
            var sb = new StringBuilder();
            sb.Append("[").Append(ch.Severity).Append("] ").Append(ch.Title);
            if (c != null) sb.Append(" on ").Append(c.Current.Device);
            sb.Append("\n");
            if (ch.Kind == ChangeKind.Changed) sb.Append("Before: ").Append(Core.Change.Display(ch.Before)).Append("\nAfter: ").Append(Core.Change.Display(ch.After)).Append("\n");
            if (c != null) sb.Append("Audits: ").Append(Date(c.Baseline.Generated)).Append(" → ").Append(Date(c.Current.Generated)).Append("\n");
            if (ch.Why.Length > 0) sb.Append(ch.Why).Append("\n");
            return sb.ToString();
        }

        private static string Date(DateTime d)
        {
            return d == DateTime.MinValue ? "unknown date" : d.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
