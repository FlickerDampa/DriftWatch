using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DriftWatch.Core;

namespace DriftWatch.Export
{
    /// <summary>Machine-readable output for scripts, SIEMs and ticketing tools.</summary>
    public static class JsonReport
    {
        public static void Save(Comparison c, string path)
        {
            File.WriteAllText(path, Build(c), new UTF8Encoding(false));
        }

        public static void SaveFleet(FleetResult fleet, string path)
        {
            SaveFleet(fleet, path, null);
        }

        public static void SaveFleet(FleetResult fleet, string path, Workspace ws)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"tool\": ").Append(Q(AppInfo.Name + " " + AppInfo.Version))
              .Append(",\n  \"folder\": ").Append(Q(fleet.Folder))
              .Append(",\n  \"devices\": ").Append(fleet.Devices.Count.ToString(CultureInfo.InvariantCulture))
              .Append(",\n  \"skipped\": [").Append(string.Join(", ", fleet.Skipped.Select(Q).ToArray())).Append("]");
            if (ws != null)
                sb.Append(",\n  \"scores\": {").Append(string.Join(", ", ws.Devices.Select(d => Q(d.Device) + ": " + ws.Posture(d.Latest).Score.ToString(CultureInfo.InvariantCulture)).ToArray())).Append("}");
            sb
              .Append(",\n  \"comparisons\": [\n");
            sb.Append(string.Join(",\n", fleet.Comparisons.Select(c => Indent(Build(c), "    ")).ToArray()));
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string Build(Comparison c)
        {
            var sb = new StringBuilder();
            var h = c.Highest;
            sb.Append("{\n  \"tool\": ").Append(Q(AppInfo.Name + " " + AppInfo.Version))
              .Append(",\n  \"device\": ").Append(Q(c.Current.Device))
              .Append(",\n  \"verdict\": ").Append(Q(c.Verdict))
              .Append(",\n  \"highest\": ").Append(h.HasValue ? Q(h.Value.ToString()) : "null")
              .Append(",\n  \"baseline\": ").Append(Snap(c.Baseline))
              .Append(",\n  \"current\": ").Append(Snap(c.Current))
              .Append(",\n  \"counts\": {");
            sb.Append(string.Join(", ", SeverityText.All.Select(s => Q(s.ToString()) + ": " + c.Count(s).ToString(CultureInfo.InvariantCulture)).ToArray()));
            sb.Append("},\n  \"notices\": [").Append(string.Join(", ", c.Notices.Select(Q).ToArray())).Append("],\n  \"changes\": [");
            for (int i = 0; i < c.Changes.Count; i++)
            {
                var ch = c.Changes[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"severity\": ").Append(Q(ch.Severity.ToString()))
                  .Append(", \"category\": ").Append(Q(ch.Category))
                  .Append(", \"kind\": ").Append(Q(ch.KindText))
                  .Append(", \"section\": ").Append(Q(ch.SectionId))
                  .Append(", \"table\": ").Append(Q(ch.Table))
                  .Append(", \"item\": ").Append(Q(ch.Item))
                  .Append(", \"field\": ").Append(Q(ch.Field))
                  .Append(", \"before\": ").Append(Q(ch.Before))
                  .Append(", \"after\": ").Append(Q(ch.After))
                  .Append(", \"title\": ").Append(Q(ch.Title))
                  .Append(", \"why\": ").Append(Q(ch.Why))
                  .Append(", \"acknowledged\": ").Append(ch.Acknowledged ? "true" : "false")
                  .Append(", \"policyNote\": ").Append(string.IsNullOrEmpty(ch.PolicyNote) ? "null" : Q(ch.PolicyNote)).Append("}");
            }
            sb.Append(c.Changes.Count > 0 ? "\n  ]\n}" : "]\n}");
            return sb.ToString();
        }

        /// <summary>Fleet data from the app's workspace: every device with its score, failing checks and latest changes.</summary>
        public static string BuildFleet(Workspace ws)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"tool\": ").Append(Q(AppInfo.Name + " " + AppInfo.Version))
              .Append(",\n  \"folder\": ").Append(Q(ws.Folder ?? ""))
              .Append(",\n  \"generated\": ").Append(Q(DateTime.Now.ToString("s", CultureInfo.InvariantCulture)))
              .Append(",\n  \"averageScore\": ").Append(Math.Round(ws.AverageScore).ToString(CultureInfo.InvariantCulture))
              .Append(",\n  \"devices\": [");
            for (int i = 0; i < ws.Devices.Count; i++)
            {
                var d = ws.Devices[i];
                var p = ws.Posture(d.Latest);
                var c = ws.Latest(d);
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"device\": ").Append(Q(d.Device))
                  .Append(", \"audits\": ").Append(d.Reports.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"latestAudit\": ").Append(Q(d.Latest.Generated.ToString("s", CultureInfo.InvariantCulture)))
                  .Append(", \"stale\": ").Append(ws.IsStale(d) ? "true" : "false")
                  .Append(", \"posture\": ").Append(Posture(p))
                  .Append(", \"latestChanges\": ").Append(c == null ? "null" : Indent(Build(c), "    ").TrimStart()).Append("}");
            }
            sb.Append(ws.Devices.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
            return sb.ToString();
        }

        /// <summary>A single audit's security score and checks.</summary>
        public static string Posture(PostureResult p)
        {
            var sb = new StringBuilder();
            sb.Append("{\"score\": ").Append(p.Score.ToString(CultureInfo.InvariantCulture)).Append(", \"grade\": ").Append(Q(p.Grade))
              .Append(", \"device\": ").Append(Q(p.Snapshot.Device)).Append(", \"audit\": ").Append(Snap(p.Snapshot)).Append(", \"checks\": [");
            for (int i = 0; i < p.Checks.Count; i++)
            {
                var c = p.Checks[i];
                sb.Append(i == 0 ? "" : ", ").Append("{\"id\": ").Append(Q(c.Id)).Append(", \"title\": ").Append(Q(c.Title)).Append(", \"status\": ").Append(Q(c.StatusText))
                  .Append(", \"weight\": ").Append(c.Weight.ToString(CultureInfo.InvariantCulture)).Append(", \"detail\": ").Append(Q(c.Detail)).Append(", \"advice\": ").Append(Q(c.Advice)).Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Snap(Snapshot s)
        {
            return "{\"file\": " + Q(s.FileName) + ", \"generated\": " + Q(s.Generated == DateTime.MinValue ? "" : s.Generated.ToString("s", CultureInfo.InvariantCulture)) +
                   ", \"administrator\": " + (s.Administrator ? "true" : "false") + ", \"tool\": " + Q(s.Tool) + "}";
        }

        private static string Indent(string text, string pad)
        {
            return pad + text.Replace("\n", "\n" + pad);
        }

        public static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
