using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DriftWatch.Core;

namespace DriftWatch.Export
{
    /// <summary>
    /// Writes a self-contained HTML change report: one file, no internet needed, readable on any device.
    /// It follows the reader's light or dark preference and prints cleanly.
    /// </summary>
    public static class HtmlReport
    {
        public static void Save(Comparison c, string path)
        {
            File.WriteAllText(path, Build(c), new UTF8Encoding(false));
        }

        public static void SaveFleet(FleetResult fleet, string path)
        {
            File.WriteAllText(path, BuildFleet(fleet), new UTF8Encoding(false));
        }

        public static string Build(Comparison c)
        {
            return Build(c, null, null);
        }

        /// <summary>A change report, with the security score of both audits when given.</summary>
        public static string Build(Comparison c, PostureResult before, PostureResult after)
        {
            var sb = new StringBuilder();
            Head(sb, "DriftWatch - " + c.Current.Device);
            sb.Append("<header class=\"top\"><div class=\"brand\">").Append(Logo()).Append("<span>DriftWatch</span></div><div class=\"meta\">Change report · generated ")
              .Append(E(DateTime.Now.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture))).Append("</div></header>");
            sb.Append("<main>");
            DeviceSummary(sb, c, true);
            if (after != null) PostureSection(sb, before, after);
            ChangeList(sb, c, "");
            sb.Append("</main>");
            Foot(sb);
            return sb.ToString();
        }

        /// <summary>A fleet report from the app's workspace: policy applied, with each PC's security score.</summary>
        public static string BuildFleet(Workspace ws)
        {
            var fleet = new FleetResult { Folder = ws.Folder ?? "" };
            fleet.Devices.AddRange(ws.Devices);
            fleet.Skipped.AddRange(ws.Skipped);
            foreach (var d in ws.Devices) { var c = ws.Latest(d); if (c != null) fleet.Comparisons.Add(c); }
            fleet.Comparisons.Sort((a, b) =>
            {
                int sa = a.Highest.HasValue ? (int)a.Highest.Value : 99, sb2 = b.Highest.HasValue ? (int)b.Highest.Value : 99;
                return sa != sb2 ? sa.CompareTo(sb2) : b.ActiveCount.CompareTo(a.ActiveCount);
            });
            var scores = ws.Devices.ToDictionary(d => d.Device, d => ws.Posture(d.Latest).Score, StringComparer.OrdinalIgnoreCase);
            return BuildFleet(fleet, scores);
        }

        public static string BuildFleet(FleetResult fleet)
        {
            return BuildFleet(fleet, null);
        }

        private static string BuildFleet(FleetResult fleet, Dictionary<string, int> scores)
        {
            var sb = new StringBuilder();
            Head(sb, "DriftWatch - Fleet report");
            sb.Append("<header class=\"top\"><div class=\"brand\">").Append(Logo()).Append("<span>DriftWatch</span></div><div class=\"meta\">Fleet report · generated ")
              .Append(E(DateTime.Now.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture))).Append("</div></header><main>");

            int devices = fleet.Devices.Count, compared = fleet.Comparisons.Count;
            int attention = fleet.Comparisons.Count(x => x.AnyAtOrAbove(Severity.High));
            sb.Append("<section class=\"hero\"><h1>").Append(devices).Append(devices == 1 ? " device" : " devices").Append("</h1><p class=\"sub\">")
              .Append(E(fleet.Folder)).Append("</p><div class=\"tiles\">");
            Tile(sb, "Compared", compared.ToString(CultureInfo.InvariantCulture), "devices with two or more audits", "");
            Tile(sb, "Need attention", attention.ToString(CultureInfo.InvariantCulture), "critical or high changes", attention > 0 ? "critical" : "");
            Tile(sb, "Single audit", (devices - compared).ToString(CultureInfo.InvariantCulture), "need a second audit to compare", "");
            Tile(sb, "Changes", fleet.Comparisons.Sum(x => x.Changes.Count).ToString(CultureInfo.InvariantCulture), "across all devices", "");
            sb.Append("</div></section>");

            if (fleet.Skipped.Count > 0)
            {
                sb.Append("<div class=\"notice\"><b>Some files were skipped</b><ul>");
                foreach (var s in fleet.Skipped) sb.Append("<li>").Append(E(s)).Append("</li>");
                sb.Append("</ul></div>");
            }

            sb.Append("<section class=\"card\"><h2>Devices</h2><table class=\"fleet\"><thead><tr><th>Device</th>").Append(scores != null ? "<th class=\"num\">Score</th>" : "").Append("<th>Status</th><th>Compared</th>");
            foreach (var s in SeverityText.All) sb.Append("<th class=\"num\">").Append(s).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var cmp in fleet.Comparisons)
            {
                string anchor = Anchor(cmp.Current.Device);
                var h = cmp.Highest;
                sb.Append("<tr><td><a href=\"#").Append(anchor).Append("\">").Append(E(cmp.Current.Device)).Append("</a></td>").Append(ScoreCell(scores, cmp.Current.Device)).Append("<td><span class=\"pill ")
                  .Append(h.HasValue ? h.Value.ToString().ToLowerInvariant() : "none").Append("\">").Append(E(cmp.Verdict)).Append("</span></td><td>")
                  .Append(E(Short(cmp.Baseline.Generated))).Append(" → ").Append(E(Short(cmp.Current.Generated))).Append("</td>");
                foreach (var s in SeverityText.All)
                {
                    int n = cmp.Count(s);
                    sb.Append("<td class=\"num").Append(n > 0 ? " has " + s.ToString().ToLowerInvariant() : "").Append("\">").Append(n == 0 ? "–" : n.ToString(CultureInfo.InvariantCulture)).Append("</td>");
                }
                sb.Append("</tr>");
            }
            foreach (var d in fleet.Devices.Where(d => d.Previous == null))
            {
                sb.Append("<tr class=\"single\"><td>").Append(E(d.Device)).Append("</td>").Append(ScoreCell(scores, d.Device)).Append("<td><span class=\"pill none\">One audit only</span></td><td>")
                  .Append(E(Short(d.Latest.Generated))).Append("</td><td colspan=\"5\" class=\"muted\">Run DeviceDossier again later to start tracking changes.</td></tr>");
            }
            sb.Append("</tbody></table></section>");

            foreach (var cmp in fleet.Comparisons.Where(x => x.Changes.Count > 0))
            {
                sb.Append("<section class=\"device\" id=\"").Append(Anchor(cmp.Current.Device)).Append("\">");
                DeviceSummary(sb, cmp, false);
                ChangeList(sb, cmp, Anchor(cmp.Current.Device));
                sb.Append("</section>");
            }
            sb.Append("</main>");
            Foot(sb);
            return sb.ToString();
        }

        private static void DeviceSummary(StringBuilder sb, Comparison c, bool big)
        {
            var h = c.Highest;
            string level = h.HasValue ? h.Value.ToString().ToLowerInvariant() : "none";
            sb.Append("<section class=\"hero ").Append(level).Append("\"><div class=\"verdict ").Append(level).Append("\">").Append(E(c.Verdict)).Append("</div>");
            sb.Append(big ? "<h1>" : "<h2 class=\"devname\">").Append(E(c.Current.Device)).Append(big ? "</h1>" : "</h2>");
            sb.Append("<p class=\"sub\">").Append(E(Long(c.Baseline.Generated))).Append(" → ").Append(E(Long(c.Current.Generated)));
            if (c.Span.Length > 0) sb.Append(" · ").Append(E(c.Span));
            sb.Append(" · ").Append(c.Changes.Count).Append(c.Changes.Count == 1 ? " change" : " changes").Append("</p>");
            sb.Append("<div class=\"tiles\">");
            foreach (var s in SeverityText.All)
                Tile(sb, s.ToString(), c.Count(s).ToString(CultureInfo.InvariantCulture), Hint(s), c.Count(s) > 0 ? s.ToString().ToLowerInvariant() : "");
            sb.Append("</div>");
            sb.Append("<div class=\"files\"><div><span>Older audit</span>").Append(E(c.Baseline.FileName.Length > 0 ? c.Baseline.FileName : c.Baseline.Label)).Append(Admin(c.Baseline))
              .Append("</div><div><span>Newer audit</span>").Append(E(c.Current.FileName.Length > 0 ? c.Current.FileName : c.Current.Label)).Append(Admin(c.Current)).Append("</div></div>");
            if (c.Notices.Count > 0)
            {
                sb.Append("<div class=\"notice\"><b>Before you read the results</b><ul>");
                foreach (var n in c.Notices) sb.Append("<li>").Append(E(n)).Append("</li>");
                sb.Append("</ul></div>");
            }
            sb.Append("</section>");
        }

        private static void ChangeList(StringBuilder sb, Comparison c, string scope)
        {
            if (c.Changes.Count == 0)
            {
                sb.Append("<section class=\"card empty\"><h2>No changes</h2><p>Every compared section matches between the two audits.</p></section>");
                return;
            }
            string id = scope.Length > 0 ? scope + "-" : "";
            sb.Append("<section class=\"card\" data-scope=\"").Append(id).Append("\"><div class=\"toolbar\"><div class=\"chips\">");
            sb.Append("<button class=\"chip on\" data-sev=\"all\">All <b>").Append(c.Changes.Count).Append("</b></button>");
            foreach (var s in SeverityText.All)
            {
                int n = c.Count(s);
                if (n == 0) continue;
                sb.Append("<button class=\"chip ").Append(s.ToString().ToLowerInvariant()).Append("\" data-sev=\"").Append(s.ToString().ToLowerInvariant()).Append("\">").Append(s).Append(" <b>").Append(n).Append("</b></button>");
            }
            sb.Append("</div><input class=\"search\" type=\"search\" placeholder=\"Search changes\" aria-label=\"Search changes\"></div>");

            foreach (var group in c.Changes.GroupBy(x => x.Severity))
            {
                string sev = group.Key.ToString().ToLowerInvariant();
                sb.Append("<h3 class=\"group ").Append(sev).Append("\" data-sev=\"").Append(sev).Append("\">").Append(group.Key).Append(" <span>").Append(group.Count()).Append("</span></h3><ol class=\"changes\">");
                foreach (var ch in group)
                {
                    sb.Append("<li class=\"change\" data-sev=\"").Append(sev).Append("\"><div class=\"row1\"><span class=\"pill ").Append(sev).Append("\">").Append(group.Key).Append("</span><span class=\"cat\">")
                      .Append(E(ch.Category)).Append("</span><span class=\"where\">").Append(E(ch.SectionTitle + (ch.Table.Length > 0 ? " · " + ch.Table : ""))).Append("</span></div>");
                    sb.Append("<div class=\"title\">").Append(E(ch.Title)).Append(ch.Acknowledged ? " <span class=\"ack\">acknowledged</span>" : "").Append("</div>");
                    if (!string.IsNullOrEmpty(ch.PolicyNote)) sb.Append("<div class=\"policy\">").Append(E(ch.PolicyNote)).Append("</div>");
                    if (ch.Kind == ChangeKind.Changed)
                        sb.Append("<div class=\"diff\"><span class=\"before\">").Append(E(Change.Display(ch.Before))).Append("</span><span class=\"arrow\">→</span><span class=\"after\">").Append(E(Change.Display(ch.After))).Append("</span></div>");
                    else if (ch.Kind == ChangeKind.Added && ch.After.Length > 0)
                        sb.Append("<div class=\"diff\"><span class=\"after\">+ ").Append(E(ch.After)).Append("</span></div>");
                    else if (ch.Kind == ChangeKind.Removed && ch.Before.Length > 0)
                        sb.Append("<div class=\"diff\"><span class=\"before\">− ").Append(E(ch.Before)).Append("</span></div>");
                    if (ch.Why.Length > 0) sb.Append("<p class=\"why\">").Append(E(ch.Why)).Append("</p>");
                    sb.Append("</li>");
                }
                sb.Append("</ol>");
            }
            sb.Append("<p class=\"nomatch\" hidden>No changes match your search.</p></section>");
        }

        private static string ScoreCell(Dictionary<string, int> scores, string device)
        {
            int v;
            if (scores == null) return "";
            if (!scores.TryGetValue(device, out v)) return "<td class=\"num\">–</td>";
            string cls = v >= 85 ? "ok" : v >= 70 ? "fair" : v >= 55 ? "weak" : "bad";
            return "<td class=\"num\"><span class=\"score " + cls + "\">" + v + "</span></td>";
        }

        private static void PostureSection(StringBuilder sb, PostureResult before, PostureResult after)
        {
            string cls = after.Score >= 85 ? "ok" : after.Score >= 70 ? "fair" : after.Score >= 55 ? "weak" : "bad";
            sb.Append("<section class=\"card\"><h2>Security score</h2><p class=\"scoreline\"><span class=\"score big ").Append(cls).Append("\">").Append(after.Score).Append("</span> / 100 · grade ")
              .Append(after.Grade).Append(" · ").Append(E(PostureResult.Describe(after.Score)));
            if (before != null && before.Score != after.Score)
                sb.Append(" <span class=\"delta ").Append(after.Score > before.Score ? "up" : "down").Append("\">").Append(after.Score > before.Score ? "▲ +" : "▼ −").Append(Math.Abs(after.Score - before.Score)).Append(" since the older audit (").Append(before.Score).Append(")</span>");
            sb.Append("</p>");
            var issues = after.Checks.Where(x => x.Status == CheckStatus.Fail || x.Status == CheckStatus.Warn).OrderBy(x => x.Status == CheckStatus.Fail ? 0 : 1).ThenByDescending(x => x.Weight).ToList();
            if (issues.Count == 0) sb.Append("<p class=\"muted\">Every reported check passes.</p>");
            else
            {
                sb.Append("<ul class=\"checks\">");
                foreach (var ch in issues)
                {
                    string st = ch.Status == CheckStatus.Fail ? "critical" : "medium";
                    sb.Append("<li><span class=\"pill ").Append(st).Append("\">").Append(ch.Status == CheckStatus.Fail ? "Fail" : "Warning").Append("</span> <b>").Append(E(ch.Title)).Append("</b><div class=\"why\">")
                      .Append(E(ch.Detail)).Append(ch.Advice.Length > 0 ? " " + E(ch.Advice) : "").Append("</div></li>");
                }
                sb.Append("</ul>");
            }
            sb.Append("<p class=\"muted small\">").Append(after.Passed).Append(" passing · ").Append(after.Warnings).Append(" warnings · ").Append(after.Failed).Append(" failing · ")
              .Append(after.Checks.Count(x => x.Status == CheckStatus.Unknown)).Append(" not reported</p></section>");
        }

        private static void Tile(StringBuilder sb, string label, string value, string hint, string cls)
        {
            sb.Append("<div class=\"tile ").Append(cls).Append("\"><span class=\"lbl\">").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong><span class=\"hint\">").Append(E(hint)).Append("</span></div>");
        }

        private static string Hint(Severity s)
        {
            switch (s)
            {
                case Severity.Critical: return "act now";
                case Severity.High: return "review soon";
                case Severity.Medium: return "worth checking";
                case Severity.Low: return "routine";
                default: return "for the record";
            }
        }

        private static string Admin(Snapshot s)
        {
            return s.Administrator ? "<em>administrator</em>" : "<em class=\"warn\">limited mode</em>";
        }

        private static string Short(DateTime d)
        {
            return d == DateTime.MinValue ? "unknown date" : d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string Long(DateTime d)
        {
            return d == DateTime.MinValue ? "unknown date" : d.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }

        private static string Anchor(string device)
        {
            var sb = new StringBuilder("d-");
            foreach (char ch in device.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
            return sb.ToString();
        }

        private static string E(string s)
        {
            return WebUtility.HtmlEncode(s ?? "");
        }

        private static string Logo()
        {
            return "<svg viewBox=\"0 0 32 32\" width=\"26\" height=\"26\" aria-hidden=\"true\"><defs><linearGradient id=\"dwg\" x1=\"0\" x2=\"1\"><stop offset=\"0\" stop-color=\"#22d3ee\"/><stop offset=\"1\" stop-color=\"#8b7cf6\"/></linearGradient></defs><rect width=\"32\" height=\"32\" rx=\"8\" fill=\"#0e1d3d\"/><path d=\"M5 19c3.5-5 5.5-5 8 0s5 4.5 9-1\" fill=\"none\" stroke=\"url(#dwg)\" stroke-width=\"3\" stroke-linecap=\"round\"/><circle cx=\"25.5\" cy=\"10.5\" r=\"2.6\" fill=\"#fbbf24\"/></svg>";
        }

        private static void Head(StringBuilder sb, string title)
        {
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"light dark\"><meta name=\"generator\" content=\"")
              .Append(AppInfo.Name).Append(' ').Append(AppInfo.Version).Append("\"><title>").Append(E(title)).Append("</title><style>").Append(Css).Append("</style></head><body>");
        }

        private static void Foot(StringBuilder sb)
        {
            sb.Append("<footer>Made with ").Append(AppInfo.Name).Append(' ').Append(AppInfo.Version).Append(" from DeviceDossier reports. This report may contain sensitive details. Store it securely.</footer><script>").Append(Js).Append("</script></body></html>");
        }

        private const string Css = @":root{--bg:#f5f7fb;--card:#fff;--ink:#121726;--soft:#3d465c;--muted:#6b7590;--line:#e2e6ef;--crit:#d92d4b;--high:#e8590c;--med:#d69e00;--low:#2f80ed;--info:#7a8499;--ok:#12a670;--acc:#4f7cff}
@media (prefers-color-scheme:dark){:root{--bg:#070a12;--card:#0e1320;--ink:#eef2ff;--soft:#c3cbe0;--muted:#8f9ab3;--line:#1e2638;--crit:#ff5b74;--high:#ff8a4c;--med:#ffcb47;--low:#5ea2ff;--info:#8f9ab3;--ok:#4ff0b4;--acc:#6f95ff}}
*{box-sizing:border-box;margin:0}body{background:var(--bg);color:var(--ink);font:15px/1.55 'Segoe UI',system-ui,-apple-system,sans-serif}
.top{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;padding:14px max(16px,calc(50% - 560px));border-bottom:1px solid var(--line);background:var(--card)}
.brand{display:flex;align-items:center;gap:10px;font-weight:700;font-size:1.05rem}.meta{color:var(--muted);font-size:.85rem}
main{width:min(1120px,100% - 32px);margin:24px auto 40px;display:flex;flex-direction:column;gap:18px}
.hero,.card{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:24px}
.hero.critical{border-top:4px solid var(--crit)}.hero.high{border-top:4px solid var(--high)}.hero.medium{border-top:4px solid var(--med)}.hero.low,.hero.info{border-top:4px solid var(--low)}.hero.none{border-top:4px solid var(--ok)}
h1{font-size:1.9rem;letter-spacing:-.02em}h2{font-size:1.25rem}.devname{font-size:1.5rem}.sub{color:var(--muted);margin-top:4px}
.verdict{display:inline-block;font-size:.78rem;font-weight:700;letter-spacing:.06em;text-transform:uppercase;padding:4px 10px;border-radius:999px;margin-bottom:10px;color:#fff;background:var(--ok)}
.verdict.critical{background:var(--crit)}.verdict.high{background:var(--high)}.verdict.medium{background:var(--med);color:#1a1300}.verdict.low,.verdict.info{background:var(--low)}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-top:18px}
.tile{border:1px solid var(--line);border-radius:12px;padding:12px 14px}.tile .lbl{font-size:.78rem;color:var(--muted);font-weight:600;text-transform:uppercase;letter-spacing:.05em}
.tile strong{display:block;font-size:1.7rem;line-height:1.2}.tile .hint{font-size:.78rem;color:var(--muted)}
.tile.critical strong{color:var(--crit)}.tile.high strong{color:var(--high)}.tile.medium strong{color:var(--med)}.tile.low strong{color:var(--low)}.tile.info strong{color:var(--info)}
.files{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:14px;font-size:.84rem}.files div{border:1px dashed var(--line);border-radius:10px;padding:8px 12px;overflow-wrap:anywhere}
.files span{display:block;color:var(--muted);font-size:.72rem;text-transform:uppercase;letter-spacing:.05em}.files em{font-style:normal;color:var(--ok);margin-left:8px;font-size:.78rem}.files em.warn{color:var(--high)}
.notice{margin-top:14px;border-left:3px solid var(--med);background:color-mix(in srgb,var(--med) 10%,transparent);padding:10px 14px;border-radius:8px;font-size:.9rem}.notice ul{margin:6px 0 0 18px}
.toolbar{display:flex;flex-wrap:wrap;justify-content:space-between;gap:10px;margin-bottom:8px}.chips{display:flex;flex-wrap:wrap;gap:6px}
.chip{font:inherit;font-size:.84rem;padding:6px 12px;border-radius:999px;border:1px solid var(--line);background:transparent;color:var(--soft);cursor:pointer}.chip b{margin-left:4px}
.chip.on{background:var(--ink);color:var(--card);border-color:var(--ink)}
.search{font:inherit;padding:7px 12px;border-radius:10px;border:1px solid var(--line);background:var(--bg);color:var(--ink);min-width:220px}
.group{margin:20px 0 8px;font-size:.82rem;text-transform:uppercase;letter-spacing:.07em;color:var(--muted)}.group span{font-weight:400}
.group.critical{color:var(--crit)}.group.high{color:var(--high)}.group.medium{color:var(--med)}.group.low{color:var(--low)}
.changes{list-style:none;padding:0;display:flex;flex-direction:column;gap:8px}
.change{border:1px solid var(--line);border-radius:12px;padding:12px 14px}.change[data-sev=critical]{border-left:4px solid var(--crit)}.change[data-sev=high]{border-left:4px solid var(--high)}.change[data-sev=medium]{border-left:4px solid var(--med)}.change[data-sev=low]{border-left:4px solid var(--low)}
.row1{display:flex;flex-wrap:wrap;align-items:center;gap:8px;font-size:.78rem;color:var(--muted)}.cat{font-weight:600}
.pill{display:inline-block;font-size:.7rem;font-weight:700;letter-spacing:.05em;text-transform:uppercase;padding:2px 8px;border-radius:999px;color:#fff;background:var(--info)}
.pill.critical{background:var(--crit)}.pill.high{background:var(--high)}.pill.medium{background:var(--med);color:#1a1300}.pill.low{background:var(--low)}.pill.none{background:var(--ok)}
.title{font-weight:650;margin-top:4px}.diff{display:flex;flex-wrap:wrap;gap:6px;align-items:center;margin-top:6px;font-family:Consolas,'Cascadia Mono',monospace;font-size:.82rem}
.before{background:color-mix(in srgb,var(--crit) 12%,transparent);padding:2px 6px;border-radius:5px;overflow-wrap:anywhere}.after{background:color-mix(in srgb,var(--ok) 14%,transparent);padding:2px 6px;border-radius:5px;overflow-wrap:anywhere}.arrow{color:var(--muted)}
.why{color:var(--soft);font-size:.9rem;margin-top:6px}.empty p{color:var(--muted);margin-top:6px}.muted{color:var(--muted)}
table.fleet{width:100%;border-collapse:collapse;margin-top:10px;font-size:.9rem}.fleet th,.fleet td{text-align:left;padding:9px 10px;border-bottom:1px solid var(--line)}.fleet th{font-size:.74rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted)}
.fleet .num{text-align:center}.fleet td.has{font-weight:700}.fleet td.critical{color:var(--crit)}.fleet td.high{color:var(--high)}.fleet td.medium{color:var(--med)}.fleet a{color:var(--acc)}
.device{display:flex;flex-direction:column;gap:12px;scroll-margin-top:16px}
.score{font-weight:700}.score.ok{color:var(--ok)}.score.fair{color:var(--acc)}.score.weak{color:var(--med)}.score.bad{color:var(--crit)}.score.big{font-size:2rem;vertical-align:middle}
.scoreline{margin-top:6px;color:var(--soft)}.delta.up{color:var(--ok);font-weight:600}.delta.down{color:var(--crit);font-weight:600}
.checks{list-style:none;padding:0;margin-top:12px;display:flex;flex-direction:column;gap:8px}.checks li{border:1px solid var(--line);border-radius:12px;padding:10px 14px}.small{font-size:.82rem;margin-top:10px}
.ack{font-size:.72rem;font-weight:600;color:var(--muted);border:1px solid var(--line);border-radius:999px;padding:1px 8px;margin-left:6px}.policy{font-size:.8rem;color:var(--acc);margin-top:2px}
footer{text-align:center;color:var(--muted);font-size:.8rem;padding:0 16px 30px}
@media (max-width:640px){.files{grid-template-columns:1fr}.search{width:100%}.fleet{display:block;overflow-x:auto}}
@media print{.toolbar{display:none}.hero,.card{break-inside:avoid-page}body{background:#fff}}";

        private const string Js = @"document.querySelectorAll('.card[data-scope]').forEach(function(card){
var sev='all',q='';var chips=card.querySelectorAll('.chip'),items=card.querySelectorAll('.change'),groups=card.querySelectorAll('.group'),none=card.querySelector('.nomatch'),box=card.querySelector('.search');
function apply(){var shown=0;items.forEach(function(li){var ok=(sev==='all'||li.dataset.sev===sev)&&(!q||li.textContent.toLowerCase().indexOf(q)>=0);li.hidden=!ok;if(ok)shown++});
groups.forEach(function(g){var any=false;var n=g.nextElementSibling;n.querySelectorAll('.change').forEach(function(li){if(!li.hidden)any=true});g.hidden=!any;n.hidden=!any});if(none)none.hidden=shown>0}
chips.forEach(function(b){b.addEventListener('click',function(){sev=b.dataset.sev;chips.forEach(function(x){x.classList.toggle('on',x===b)});apply()})});
if(box)box.addEventListener('input',function(){q=box.value.trim().toLowerCase();apply()});});";
    }
}
