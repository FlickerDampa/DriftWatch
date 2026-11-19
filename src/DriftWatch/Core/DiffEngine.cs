using System;
using System.Collections.Generic;
using System.Linq;

namespace DriftWatch.Core
{
    /// <summary>
    /// Compares two DeviceDossier reports section by section. Fields are matched by name and table rows by
    /// identity columns (a program's name, an account, a drive letter), so a program that moved up the list
    /// is not reported as removed and re-added. Every raw difference is then scored by <see cref="RiskRules"/>.
    /// </summary>
    public static class DiffEngine
    {
        private static readonly Dictionary<string, string[]> KeyColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "cpu", new[] { "CPU Model" } },
            { "ram", new[] { "Slot / Location", "Bank" } },
            { "gpu", new[] { "GPU Name" } },
            { "storage", new[] { "Serial Number" } },
            { "power", new[] { "Device ID", "Name" } },
            { "network", new[] { "Adapter" } },
            { "drives", new[] { "User Profile", "Drive Letter" } },
            { "security|antivirus", new[] { "Product Name" } },
            { "security|bitlocker", new[] { "Drive" } },
            { "security|administrative", new[] { "Account Name" } },
            { "software", new[] { "Software Name" } },
            { "extensions", new[] { "Browser", "User Profile", "Extension ID" } },
            { "monitors", new[] { "Serial Number", "Monitor", "Product Code" } }
        };

        // Columns and fields that change on every run and say nothing about the device itself.
        private static readonly HashSet<string> IgnoredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "software|No.", "cpu|Current Speed (MHz)", "power|Current Charge (%)", "power|Status"
        };

        private static readonly HashSet<string> IgnoredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "os|Last Boot"
        };

        public static Comparison Compare(Snapshot baseline, Snapshot current)
        {
            if (baseline == null) throw new ArgumentNullException("baseline");
            if (current == null) throw new ArgumentNullException("current");

            var notices = new List<string>();
            if (current.Generated < baseline.Generated)
            {
                var t = baseline;
                baseline = current;
                current = t;
                notices.Add("The reports were given newest first, so DriftWatch swapped them to compare older → newer.");
            }
            if (!string.Equals(Norm(baseline.Device), Norm(current.Device), StringComparison.OrdinalIgnoreCase))
                notices.Add("These reports come from different devices (" + baseline.Device + " and " + current.Device + "). Changes may reflect two different PCs rather than drift on one.");
            if (baseline.Administrator && !current.Administrator)
                notices.Add("The newer audit ran without administrator rights, so BitLocker, TPM and other users' details may be missing. Missing items are not necessarily removed.");
            else if (!baseline.Administrator && current.Administrator)
                notices.Add("The older audit ran without administrator rights, so some items now appearing may simply not have been readable before.");
            if (!string.Equals(baseline.Tool, current.Tool, StringComparison.OrdinalIgnoreCase) && baseline.Tool.Length > 0 && current.Tool.Length > 0)
                notices.Add("The audits were made by different DeviceDossier versions (" + baseline.Tool + " and " + current.Tool + "). Fields one version doesn't collect are skipped.");

            var changes = new List<Change>();
            foreach (var curSec in current.Sections)
            {
                var oldSec = baseline.Section(curSec.Id);
                if (oldSec == null)
                {
                    notices.Add("Section \"" + curSec.CleanTitle + "\" only appears in the newer audit, so it was not compared.");
                    continue;
                }
                CompareSectionState(oldSec, curSec, changes);
                CompareBlocks(oldSec, curSec, changes);
            }
            foreach (var oldSec in baseline.Sections)
            {
                if (current.Section(oldSec.Id) == null)
                    notices.Add("Section \"" + oldSec.CleanTitle + "\" is missing from the newer audit (it may have been skipped), so it was not compared.");
            }

            foreach (var c in changes) RiskRules.Classify(c, baseline, current);
            return new Comparison(baseline, current, changes, notices);
        }

        private static void CompareSectionState(SnapshotSection oldSec, SnapshotSection curSec, List<Change> changes)
        {
            if (string.Equals(oldSec.State, curSec.State, StringComparison.OrdinalIgnoreCase)) return;
            changes.Add(new Change
            {
                Kind = ChangeKind.Changed,
                SectionId = curSec.Id,
                SectionTitle = curSec.CleanTitle,
                Table = "",
                Item = "Section status",
                Field = "Section status",
                Before = oldSec.State + (oldSec.Note.Length > 0 ? " (" + oldSec.Note + ")" : ""),
                After = curSec.State + (curSec.Note.Length > 0 ? " (" + curSec.Note + ")" : "")
            });
        }

        private static void CompareBlocks(SnapshotSection oldSec, SnapshotSection curSec, List<Change> changes)
        {
            var oldTables = oldSec.Blocks.Where(b => b.Kind == BlockKind.Table).ToList();
            var curTables = curSec.Blocks.Where(b => b.Kind == BlockKind.Table).ToList();
            var oldFields = oldSec.Blocks.Where(b => b.Kind == BlockKind.Fields).ToList();
            var curFields = curSec.Blocks.Where(b => b.Kind == BlockKind.Fields).ToList();

            for (int i = 0; i < curFields.Count; i++)
            {
                var match = MatchBlock(oldFields, curFields[i], i);
                if (match != null) CompareFields(curSec, match, curFields[i], changes);
            }
            for (int i = 0; i < curTables.Count; i++)
            {
                var match = MatchBlock(oldTables, curTables[i], i);
                if (match != null) CompareTable(curSec, match, curTables[i], changes);
            }
        }

        // Blocks are matched by caption when a section has several (Security has four), otherwise by position,
        // because a caption such as "Known servers checked: ..." can change between runs.
        private static SnapshotBlock MatchBlock(List<SnapshotBlock> candidates, SnapshotBlock block, int index)
        {
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1 && index == 0) return candidates[0];
            var byCaption = candidates.FirstOrDefault(c => c.Caption.Length > 0 && string.Equals(c.Caption, block.Caption, StringComparison.OrdinalIgnoreCase));
            if (byCaption != null) return byCaption;
            return index < candidates.Count ? candidates[index] : null;
        }

        private static void CompareFields(SnapshotSection sec, SnapshotBlock oldBlock, SnapshotBlock curBlock, List<Change> changes)
        {
            foreach (var f in curBlock.Fields)
            {
                if (IgnoredFields.Contains(sec.Id + "|" + f.Key)) continue;
                string before = oldBlock.Field(f.Key);
                if (before == null) continue; // field not collected by the older version
                if (Same(before, f.Value)) continue;
                changes.Add(new Change
                {
                    Kind = ChangeKind.Changed,
                    SectionId = sec.Id,
                    SectionTitle = sec.CleanTitle,
                    Table = curBlock.Caption,
                    Item = f.Key,
                    Field = f.Key,
                    Before = before,
                    After = f.Value
                });
            }
        }

        private static void CompareTable(SnapshotSection sec, SnapshotBlock oldBlock, SnapshotBlock curBlock, List<Change> changes)
        {
            var keys = KeysFor(sec.Id, curBlock);
            var oldRows = Index(oldBlock, keys);
            var curRows = Index(curBlock, keys);
            string tableName = curBlock.Caption.Length > 0 && !curBlock.Caption.StartsWith("Known servers", StringComparison.OrdinalIgnoreCase) ? curBlock.Caption : "";

            foreach (var kv in curRows)
            {
                Dictionary<string, string> before;
                if (!oldRows.TryGetValue(kv.Key, out before))
                {
                    if (IsPlaceholderRow(kv.Value)) continue;
                    changes.Add(RowChange(ChangeKind.Added, sec, tableName, kv.Value, keys, curBlock));
                    continue;
                }
                foreach (var col in curBlock.Columns)
                {
                    if (IgnoredColumns.Contains(sec.Id + "|" + col) || keys.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;
                    string a, b;
                    before.TryGetValue(col, out a);
                    kv.Value.TryGetValue(col, out b);
                    if (a == null || Same(a, b)) continue;
                    var ch = RowChange(ChangeKind.Changed, sec, tableName, kv.Value, keys, curBlock);
                    ch.Field = col;
                    ch.Before = a;
                    ch.After = b ?? "";
                    changes.Add(ch);
                }
            }
            foreach (var kv in oldRows)
            {
                if (curRows.ContainsKey(kv.Key) || IsPlaceholderRow(kv.Value)) continue;
                changes.Add(RowChange(ChangeKind.Removed, sec, tableName, kv.Value, keys, oldBlock));
            }
        }

        private static Change RowChange(ChangeKind kind, SnapshotSection sec, string table, Dictionary<string, string> row, string[] keys, SnapshotBlock block)
        {
            var ch = new Change
            {
                Kind = kind,
                SectionId = sec.Id,
                SectionTitle = sec.CleanTitle,
                Table = table,
                Item = Describe(sec.Id, row, keys)
            };
            if (kind != ChangeKind.Changed)
            {
                string detail = string.Join(" · ", block.Columns
                    .Where(c => !IgnoredColumns.Contains(sec.Id + "|" + c) && !keys.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .Select(c => { string v; row.TryGetValue(c, out v); return TextUtil.IsBlank(v) ? null : c + ": " + v; })
                    .Where(v => v != null).Take(5).ToArray());
                if (kind == ChangeKind.Added) ch.After = detail; else ch.Before = detail;
            }
            ch.Row = row;
            return ch;
        }

        private static string Describe(string sectionId, Dictionary<string, string> row, string[] keys)
        {
            Func<string, string> get = c => { string v; return row.TryGetValue(c, out v) && !TextUtil.IsBlank(v) ? v : ""; };
            if (sectionId == "software")
            {
                string v = get("Version");
                return get("Software Name") + (v.Length > 0 ? " " + v : "");
            }
            if (sectionId == "extensions") return get("Extension Name") + " (" + get("Browser") + ", " + get("User Profile") + ")";
            if (sectionId == "drives") return get("Drive Letter") + " → " + (get("Full UNC Path").Length > 0 ? get("Full UNC Path") : get("Server IP")) + " (" + get("User Profile") + ")";
            if (sectionId == "storage") return get("Drive Model") + (get("Serial Number").Length > 0 ? " (serial " + get("Serial Number") + ")" : "");
            if (sectionId == "ram") return "RAM in " + get("Slot / Location") + (get("Capacity (GB)").Length > 0 ? " (" + get("Capacity (GB)") + " GB)" : "");
            if (sectionId == "monitors") return (get("Monitor").Length > 0 ? get("Monitor") : get("Manufacturer") + " " + get("Product Code")).Trim();
            var parts = keys.Select(get).Where(v => v.Length > 0).ToArray();
            return parts.Length > 0 ? string.Join(" · ", parts) : "(unnamed row)";
        }

        private static string[] KeysFor(string sectionId, SnapshotBlock block)
        {
            string[] keys = null;
            foreach (var kv in KeyColumns)
            {
                var parts = kv.Key.Split('|');
                if (!string.Equals(parts[0], sectionId, StringComparison.OrdinalIgnoreCase)) continue;
                if (parts.Length > 1 && !TextUtil.Contains(block.Caption, parts[1])) continue;
                keys = kv.Value;
                break;
            }
            if (keys == null) keys = block.Columns.Count > 0 ? new[] { block.Columns[0] } : new string[0];
            // Drop key columns the table doesn't have, or that are blank in every row (monitors without serials).
            if (sectionId == "monitors")
            {
                bool allSerials = block.Rows.Count > 0 && block.Rows.All(r => { string v; return r.TryGetValue("Serial Number", out v) && !TextUtil.IsBlank(v); });
                keys = allSerials ? new[] { "Serial Number" } : new[] { "Monitor", "Product Code" };
            }
            var usable = keys.Where(k => block.Columns.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (usable.Count == 0 && block.Columns.Count > 0) usable.Add(block.Columns[0]);
            return usable.ToArray();
        }

        private static Dictionary<string, Dictionary<string, string>> Index(SnapshotBlock block, string[] keys)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in block.Rows)
            {
                string key = string.Join("\u001f", keys.Select(k => { string v; return row.TryGetValue(k, out v) ? Norm(v) : ""; }).ToArray());
                string unique = key;
                int n = 2;
                while (result.ContainsKey(unique)) unique = key + "\u001f#" + n++;
                result[unique] = row;
            }
            return result;
        }

        // DeviceDossier writes a single "N/A" or "ERROR" row when a table could not be read.
        private static bool IsPlaceholderRow(Dictionary<string, string> row)
        {
            var values = row.Values.ToList();
            if (values.Count == 0) return true;
            return values.All(v => TextUtil.IsBlank(v) || v == "ERROR" || v.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) || v.IndexOf("collection failed", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Norm(string v)
        {
            if (v == null) return "";
            var t = string.Join(" ", v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return t == "N/A" ? "" : t;
        }
    }
}
