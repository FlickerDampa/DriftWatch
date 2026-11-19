using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DriftWatch.Core
{
    /// <summary>
    /// Your organisation's rules: banned, approved and required software, approved administrator accounts,
    /// thresholds for patch and audit age, and changes acknowledged as expected. Saved as JSON.
    /// </summary>
    public sealed class Policy
    {
        public Policy()
        {
            Name = "Default policy";
            BannedSoftware = new List<string>();
            ApprovedSoftware = new List<string>();
            RequiredSoftware = new List<string>();
            ApprovedAdmins = new List<string>();
            Acknowledged = new List<string>();
            MaxAdmins = 3;
            MaxPatchAgeDays = 45;
            MaxSignatureAgeDays = 7;
            StaleAuditDays = 30;
            MinimumScore = 70;
            AllowRemoteDesktop = false;
        }

        public string Name { get; set; }
        public List<string> BannedSoftware { get; private set; }
        public List<string> ApprovedSoftware { get; private set; }
        public List<string> RequiredSoftware { get; private set; }
        public List<string> ApprovedAdmins { get; private set; }
        public List<string> Acknowledged { get; private set; }
        public int MaxAdmins { get; set; }
        public int MaxPatchAgeDays { get; set; }
        public int MaxSignatureAgeDays { get; set; }
        public int StaleAuditDays { get; set; }
        public int MinimumScore { get; set; }
        public bool AllowRemoteDesktop { get; set; }

        /// <summary>The file this policy was loaded from or last saved to, if any.</summary>
        public string FilePath { get; set; }

        public static string DefaultPath
        {
            get { return Path.Combine(AppSettings.Folder, "policy.json"); }
        }

        /// <summary>True when the name matches any pattern. Patterns match anywhere in the name; * is a wildcard.</summary>
        public static bool Matches(string name, IEnumerable<string> patterns)
        {
            if (string.IsNullOrEmpty(name) || patterns == null) return false;
            string lower = name.ToLowerInvariant();
            foreach (var raw in patterns)
            {
                string p = (raw ?? "").Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p.IndexOf('*') < 0) { if (lower.Contains(p)) return true; continue; }
                var parts = p.Split('*');
                int pos = 0;
                bool ok = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    int at = lower.IndexOf(parts[i], pos, StringComparison.Ordinal);
                    if (at < 0 || (i == 0 && !p.StartsWith("*") && at != 0)) { ok = false; break; }
                    pos = at + parts[i].Length;
                }
                if (ok && !p.EndsWith("*") && parts[parts.Length - 1].Length > 0 && !lower.EndsWith(parts[parts.Length - 1])) ok = false;
                if (ok) return true;
            }
            return false;
        }

        public bool IsApprovedAdmin(string account)
        {
            if (ApprovedAdmins.Count == 0) return false;
            string name = account ?? "";
            int slash = name.LastIndexOf('\\');
            string shortName = slash >= 0 ? name.Substring(slash + 1) : name;
            foreach (var a in ApprovedAdmins)
            {
                if (string.Equals(a.Trim(), name, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Trim(), shortName, StringComparison.OrdinalIgnoreCase)) return true;
                if (a.Contains("*") && Matches(shortName, new[] { a })) return true;
            }
            return false;
        }

        public static Policy Load(string path)
        {
            var d = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (d == null) throw new FormatException("This policy file is not a JSON object.");
            var p = new Policy();
            p.Name = MiniJson.GetString(d, "name", p.Name);
            p.BannedSoftware.AddRange(MiniJson.GetList(d, "bannedSoftware"));
            p.ApprovedSoftware.AddRange(MiniJson.GetList(d, "approvedSoftware"));
            p.RequiredSoftware.AddRange(MiniJson.GetList(d, "requiredSoftware"));
            p.ApprovedAdmins.AddRange(MiniJson.GetList(d, "approvedAdmins"));
            p.Acknowledged.AddRange(MiniJson.GetList(d, "acknowledged"));
            p.MaxAdmins = Clamp(MiniJson.GetInt(d, "maxAdmins", p.MaxAdmins), 1, 50);
            p.MaxPatchAgeDays = Clamp(MiniJson.GetInt(d, "maxPatchAgeDays", p.MaxPatchAgeDays), 1, 3650);
            p.MaxSignatureAgeDays = Clamp(MiniJson.GetInt(d, "maxSignatureAgeDays", p.MaxSignatureAgeDays), 1, 365);
            p.StaleAuditDays = Clamp(MiniJson.GetInt(d, "staleAuditDays", p.StaleAuditDays), 1, 3650);
            p.MinimumScore = Clamp(MiniJson.GetInt(d, "minimumScore", p.MinimumScore), 0, 100);
            p.AllowRemoteDesktop = MiniJson.GetBool(d, "allowRemoteDesktop", p.AllowRemoteDesktop);
            p.FilePath = Path.GetFullPath(path);
            return p;
        }

        /// <summary>Loads the policy from the app's folder, or returns the defaults when there is none or it can't be read.</summary>
        public static Policy LoadDefault()
        {
            try
            {
                if (File.Exists(DefaultPath)) return Load(DefaultPath);
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)) throw;
            }
            return new Policy { FilePath = DefaultPath };
        }

        public void Save(string path)
        {
            var d = new Dictionary<string, object>();
            d["name"] = Name;
            d["bannedSoftware"] = BannedSoftware;
            d["approvedSoftware"] = ApprovedSoftware;
            d["requiredSoftware"] = RequiredSoftware;
            d["approvedAdmins"] = ApprovedAdmins;
            d["maxAdmins"] = MaxAdmins;
            d["maxPatchAgeDays"] = MaxPatchAgeDays;
            d["maxSignatureAgeDays"] = MaxSignatureAgeDays;
            d["staleAuditDays"] = StaleAuditDays;
            d["minimumScore"] = MinimumScore;
            d["allowRemoteDesktop"] = AllowRemoteDesktop;
            d["acknowledged"] = Acknowledged;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, MiniJson.Write(d) + "\n", new UTF8Encoding(false));
            FilePath = Path.GetFullPath(path);
        }

        public Policy Clone()
        {
            var p = new Policy
            {
                Name = Name, MaxAdmins = MaxAdmins, MaxPatchAgeDays = MaxPatchAgeDays, MaxSignatureAgeDays = MaxSignatureAgeDays,
                StaleAuditDays = StaleAuditDays, MinimumScore = MinimumScore, AllowRemoteDesktop = AllowRemoteDesktop, FilePath = FilePath
            };
            p.BannedSoftware.AddRange(BannedSoftware);
            p.ApprovedSoftware.AddRange(ApprovedSoftware);
            p.RequiredSoftware.AddRange(RequiredSoftware);
            p.ApprovedAdmins.AddRange(ApprovedAdmins);
            p.Acknowledged.AddRange(Acknowledged);
            return p;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }

    /// <summary>Adjusts a comparison's changes to match a policy, and applies acknowledgements.</summary>
    public static class PolicyEngine
    {
        public static void Apply(Comparison cmp, Policy policy)
        {
            if (cmp == null || policy == null) return;
            var acknowledged = new HashSet<string>(policy.Acknowledged, StringComparer.OrdinalIgnoreCase);
            string device = cmp.Current.Device;
            foreach (var c in cmp.Changes)
            {
                string section = (c.SectionId ?? "").ToLowerInvariant();
                string table = (c.Table ?? "").ToLowerInvariant();
                if (section == "software")
                {
                    string name = c.Item ?? "";
                    if (c.Kind == ChangeKind.Added && Policy.Matches(name, policy.BannedSoftware))
                    {
                        c.Severity = Severity.High;
                        c.Category = "Policy";
                        c.Title = "Banned software installed: " + name;
                        c.Why = "Your policy bans this software. Remove it or record an exception.";
                        c.PolicyNote = "Banned by policy";
                    }
                    else if (c.Kind == ChangeKind.Added && c.Severity <= Severity.Medium && Policy.Matches(name, policy.ApprovedSoftware))
                    {
                        c.Severity = Severity.Low;
                        c.PolicyNote = "Approved in your policy, so lowered to Low";
                    }
                    else if (c.Kind == ChangeKind.Removed && Policy.Matches(name, policy.RequiredSoftware))
                    {
                        c.Severity = Severity.High;
                        c.Category = "Policy";
                        c.Title = "Required software removed: " + name;
                        c.Why = "Your policy requires this software on every PC. Reinstall it.";
                        c.PolicyNote = "Required by policy";
                    }
                }
                else if (section == "security" && table.Contains("administrative") && c.Kind == ChangeKind.Added && policy.IsApprovedAdmin(c.Item))
                {
                    c.Severity = Severity.Low;
                    c.Title = "Approved administrator added: " + c.Item;
                    c.PolicyNote = "On your approved administrator list, so lowered to Low";
                }
                else if (section == "baseline" && string.Equals(c.Field, "Remote Desktop", StringComparison.OrdinalIgnoreCase) && policy.AllowRemoteDesktop && c.Severity == Severity.High)
                {
                    c.Severity = Severity.Low;
                    c.PolicyNote = "Your policy allows Remote Desktop, so lowered to Low";
                }
                if (acknowledged.Contains(c.Signature(device))) c.Acknowledged = true;
            }
            cmp.AppliedPolicy = policy;
            cmp.Resort();
        }
    }
}
