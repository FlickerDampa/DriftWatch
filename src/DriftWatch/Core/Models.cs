using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DriftWatch.Core
{
    /// <summary>How much attention a change needs. Ordered from most to least urgent.</summary>
    public enum Severity { Critical = 0, High = 1, Medium = 2, Low = 3, Info = 4 }

    public enum ChangeKind { Added, Removed, Changed }

    public static class SeverityText
    {
        public static readonly Severity[] All = { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info };

        public static string Name(Severity s)
        {
            return s.ToString();
        }

        public static bool TryParse(string text, out Severity severity)
        {
            foreach (var s in All)
            {
                if (string.Equals(s.ToString(), (text ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    severity = s;
                    return true;
                }
            }
            severity = Severity.Info;
            return false;
        }
    }

    /// <summary>One difference between two audits of the same device.</summary>
    public sealed class Change
    {
        public Change()
        {
            Severity = Severity.Info;
            Category = "System";
            Before = "";
            After = "";
            Field = "";
            Why = "";
        }

        public Severity Severity { get; set; }
        public string Category { get; set; }
        public ChangeKind Kind { get; set; }
        public string SectionId { get; set; }
        public string SectionTitle { get; set; }
        public string Table { get; set; }
        /// <summary>What changed, such as a program name, an account or a setting.</summary>
        public string Item { get; set; }
        /// <summary>For a changed table row, which column changed.</summary>
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        /// <summary>A short headline, such as "New local administrator".</summary>
        public string Title { get; set; }
        /// <summary>Why it matters and what to check.</summary>
        public string Why { get; set; }
        /// <summary>The full table row behind an added, removed or changed row, for rules that need other columns.</summary>
        public Dictionary<string, string> Row { get; set; }
        /// <summary>Marked as expected by the user (or approved by policy). Kept in the list but left out of the verdict.</summary>
        public bool Acknowledged { get; set; }
        /// <summary>Set when the policy changed this change's severity, explaining why.</summary>
        public string PolicyNote { get; set; }

        /// <summary>A stable fingerprint of this change on a device, used to remember acknowledgements.</summary>
        public string Signature(string device)
        {
            return string.Join("|", new[] { device ?? "", SectionId ?? "", Table ?? "", Item ?? "", Field ?? "", KindText, After ?? "" }).ToLowerInvariant();
        }

        public string KindText
        {
            get { return Kind == ChangeKind.Added ? "Added" : Kind == ChangeKind.Removed ? "Removed" : "Changed"; }
        }

        public string Summary
        {
            get
            {
                if (Kind == ChangeKind.Added) return "Added: " + Item;
                if (Kind == ChangeKind.Removed) return "Removed: " + Item;
                return Item + (Field.Length > 0 && Field != Item ? " · " + Field : "") + ": " + Display(Before) + " → " + Display(After);
            }
        }

        public static string Display(string v)
        {
            return string.IsNullOrEmpty(v) ? "(blank)" : v;
        }
    }

    /// <summary>The result of comparing a baseline audit with a later one.</summary>
    public sealed class Comparison
    {
        public Comparison(Snapshot baseline, Snapshot current, List<Change> changes, List<string> notices)
        {
            Baseline = baseline;
            Current = current;
            Changes = changes;
            Notices = notices;
            Resort();
        }

        /// <summary>Re-orders the changes after a policy has adjusted severities: most severe first, acknowledged last.</summary>
        public void Resort()
        {
            Changes = Changes.OrderBy(c => c.Acknowledged ? 1 : 0).ThenBy(c => (int)c.Severity).ThenBy(c => c.Category, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Item, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Changes that still need attention (not acknowledged).</summary>
        public IEnumerable<Change> Active { get { return Changes.Where(c => !c.Acknowledged); } }
        public int ActiveCount { get { return Changes.Count(c => !c.Acknowledged); } }
        public int AcknowledgedCount { get { return Changes.Count(c => c.Acknowledged); } }

        public Snapshot Baseline { get; private set; }
        public Snapshot Current { get; private set; }
        public List<Change> Changes { get; private set; }
        public Policy AppliedPolicy { get; set; }
        /// <summary>Things that affect how far the comparison can be trusted, such as a run without administrator rights.</summary>
        public List<string> Notices { get; private set; }

        public int Count(Severity s)
        {
            return Changes.Count(c => !c.Acknowledged && c.Severity == s);
        }

        public bool AnyAtOrAbove(Severity threshold)
        {
            return Changes.Any(c => !c.Acknowledged && c.Severity <= threshold);
        }

        public Severity? Highest
        {
            get { var a = Active.ToList(); return a.Count == 0 ? (Severity?)null : a.Min(c => c.Severity); }
        }

        public string Verdict
        {
            get
            {
                var h = Highest;
                if (h == null) return Changes.Count == 0 ? "No changes found" : "All changes acknowledged";
                if (h == Severity.Critical) return "Action needed";
                if (h == Severity.High) return "Review soon";
                if (h == Severity.Medium) return "Worth a look";
                return "Routine changes only";
            }
        }

        public string Span
        {
            get
            {
                var days = (Current.Generated - Baseline.Generated).TotalDays;
                if (Baseline.Generated == DateTime.MinValue || Current.Generated == DateTime.MinValue) return "";
                int d = (int)Math.Round(Math.Abs(days));
                return d == 0 ? "same day" : d == 1 ? "1 day apart" : d.ToString(CultureInfo.InvariantCulture) + " days apart";
            }
        }
    }

    public static class TextUtil
    {
        public static string TitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var words = s.ToLowerInvariant().Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length == 0) continue;
                string upper = w.ToUpperInvariant();
                if (upper == "CPU" || upper == "RAM" || upper == "GPU" || upper == "PSU" || upper == "(CPU)" || upper == "(PSU)" || upper == "MAC" || upper == "EDR" || upper == "AV")
                    words[i] = upper;
                else if (i > 0 && (w == "and" || w == "or" || w == "of"))
                    words[i] = w;
                else if (w[0] == '(' && w.Length > 1)
                    words[i] = "(" + char.ToUpperInvariant(w[1]) + w.Substring(2);
                else
                    words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
            }
            return string.Join(" ", words);
        }

        public static bool IsBlank(string v)
        {
            if (string.IsNullOrEmpty(v)) return true;
            string t = v.Trim();
            return t.Length == 0 || t == "N/A" || t == "-" || t == "Unknown";
        }

        public static bool Contains(string haystack, string needle)
        {
            return (haystack ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static double? Number(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var digits = new System.Text.StringBuilder();
            bool seen = false;
            foreach (char ch in v)
            {
                if (char.IsDigit(ch) || (ch == '.' && seen) || (ch == '-' && !seen && digits.Length == 0))
                {
                    digits.Append(ch);
                    if (char.IsDigit(ch)) seen = true;
                }
                else if (seen) break;
            }
            double d;
            if (seen && double.TryParse(digits.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }
    }
}
