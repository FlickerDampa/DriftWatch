using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DriftWatch.Core
{
    public enum CheckStatus { Pass, Warn, Fail, Unknown }

    /// <summary>One security control checked against a single audit.</summary>
    public sealed class PostureCheck
    {
        public string Id { get; set; }
        public string Group { get; set; }
        public string Title { get; set; }
        /// <summary>A short label for the compliance matrix, such as "RTP".</summary>
        public string Short { get; set; }
        public int Weight { get; set; }
        public CheckStatus Status { get; set; }
        /// <summary>What the audit shows.</summary>
        public string Detail { get; set; }
        /// <summary>What to do when the check doesn't pass.</summary>
        public string Advice { get; set; }

        public string StatusText
        {
            get { return Status == CheckStatus.Pass ? "Pass" : Status == CheckStatus.Warn ? "Warning" : Status == CheckStatus.Fail ? "Fail" : "Not reported"; }
        }
    }

    /// <summary>The security posture of one audit: a 0-100 score, a letter grade and every check behind it.</summary>
    public sealed class PostureResult
    {
        public PostureResult(Snapshot snapshot, List<PostureCheck> checks)
        {
            Snapshot = snapshot;
            Checks = checks;
            double total = 0, earned = 0;
            foreach (var c in checks)
            {
                if (c.Status == CheckStatus.Unknown) continue;
                total += c.Weight;
                if (c.Status == CheckStatus.Pass) earned += c.Weight;
                else if (c.Status == CheckStatus.Warn) earned += c.Weight * 0.5;
            }
            Score = total == 0 ? 0 : (int)Math.Round(earned * 100.0 / total);
            // A single critical failure caps the score, so a PC with antivirus off can never look healthy.
            if (checks.Any(c => c.Status == CheckStatus.Fail && c.Weight >= 12)) Score = Math.Min(Score, 69);
        }

        public Snapshot Snapshot { get; private set; }
        public List<PostureCheck> Checks { get; private set; }
        public int Score { get; private set; }
        public int Passed { get { return Checks.Count(c => c.Status == CheckStatus.Pass); } }
        public int Warnings { get { return Checks.Count(c => c.Status == CheckStatus.Warn); } }
        public int Failed { get { return Checks.Count(c => c.Status == CheckStatus.Fail); } }

        public string Grade { get { return GradeFor(Score); } }

        public PostureCheck Check(string id)
        {
            return Checks.FirstOrDefault(c => c.Id == id);
        }

        public static string GradeFor(int score)
        {
            return score >= 90 ? "A" : score >= 80 ? "B" : score >= 70 ? "C" : score >= 60 ? "D" : "F";
        }

        public static string Describe(int score)
        {
            return score >= 90 ? "Strong" : score >= 80 ? "Good" : score >= 70 ? "Fair" : score >= 60 ? "Weak" : "At risk";
        }
    }

    /// <summary>Scores one DeviceDossier audit against security best practice and the policy.</summary>
    public static class PostureEngine
    {
        public static readonly string[] Groups = { "Endpoint protection", "Encryption", "Network exposure", "Identity and access", "Updates", "Platform", "Software", "Audit" };

        public static PostureResult Evaluate(Snapshot s, Policy policy)
        {
            if (policy == null) policy = new Policy();
            var checks = new List<PostureCheck>();
            var security = s.Section("security");
            bool securityRead = security != null && security.State != "Error" && security.State != "Skipped";
            var baseline = s.Section("baseline");

            // Endpoint protection
            var av = Rows(security, "antivirus").Where(r => !Placeholder(Cell(r, "Product Name"))).ToList();
            var c = New(checks, "av", "Endpoint protection", "Antivirus or EDR installed", "AV", 14);
            if (!securityRead) Unknown(c, "The security section wasn't read in this audit.");
            else if (av.Count == 0) Fail(c, "No antivirus or EDR product is registered with Windows Security Center.", "Install and register an antivirus or EDR product. Microsoft Defender is built in.");
            else Pass(c, string.Join(", ", av.Select(r => Cell(r, "Product Name"))));

            c = New(checks, "rtp", "Endpoint protection", "Real-time protection on", "RTP", 12);
            var rtp = av.Select(r => Field(Cell(r, "Details"), "Real-time protection:")).Where(v => v.Length > 0).ToList();
            if (av.Count == 0) Unknown(c, "No antivirus product reported.");
            else if (rtp.Any(v => v.StartsWith("On", StringComparison.OrdinalIgnoreCase))) Pass(c, "Real-time protection is on.");
            else if (rtp.Count > 0) Fail(c, "Real-time protection is off.", "Turn real-time protection back on and find out who turned it off. Malware is only caught when it runs.");
            else Unknown(c, "The antivirus product doesn't report real-time protection.");

            c = New(checks, "sig", "Endpoint protection", "Malware definitions up to date", "SIG", 5);
            string sigText = Field(baseline, "Defender Signatures");
            DateTime sigDate;
            if (TryDateAfter(sigText, "updated", out sigDate) && s.Generated != DateTime.MinValue)
            {
                int age = Math.Max(0, (int)(s.Generated.Date - sigDate.Date).TotalDays);
                string detail = "Defender signatures were " + Days(age) + " old at audit time.";
                if (age <= policy.MaxSignatureAgeDays) Pass(c, detail);
                else if (age <= policy.MaxSignatureAgeDays * 3) Warn(c, detail, "Definitions should update daily. Check Windows Update can reach the internet.");
                else Fail(c, detail, "Definitions are badly out of date. Check Windows Update and Defender are working.");
            }
            else Unknown(c, sigText.Length > 0 ? sigText : "Signature date not reported.");

            // Encryption
            var bitlocker = Rows(security, "bitlocker").Where(r => !Placeholder(Cell(r, "Drive"))).ToList();
            var system = bitlocker.FirstOrDefault(r => Cell(r, "Drive").StartsWith("C", StringComparison.OrdinalIgnoreCase));
            c = New(checks, "blos", "Encryption", "System drive encrypted (BitLocker)", "BL-C", 12);
            if (!securityRead || bitlocker.Count == 0) Unknown(c, "BitLocker status wasn't reported. It needs an audit run as administrator.");
            else if (system == null) Unknown(c, "Drive C: isn't in the BitLocker list.");
            else if (On(Cell(system, "Protection Status"))) Pass(c, "C: is protected (" + Cell(system, "Volume Status") + ").");
            else Fail(c, "C: is not protected (" + Cell(system, "Protection Status") + ", " + Cell(system, "Volume Status") + ").", "Turn BitLocker on for C: and back up the recovery key. A lost or stolen laptop exposes every file otherwise.");

            c = New(checks, "bldata", "Encryption", "Other drives encrypted", "BL-D", 4);
            var others = bitlocker.Where(r => r != system).ToList();
            var unprotected = others.Where(r => !On(Cell(r, "Protection Status"))).Select(r => Cell(r, "Drive")).ToList();
            if (others.Count == 0) Unknown(c, "No other fixed drives reported.");
            else if (unprotected.Count == 0) Pass(c, "All " + others.Count + " other drive(s) are protected.");
            else Warn(c, "Not protected: " + string.Join(", ", unprotected) + ".", "Turn BitLocker on for data drives that hold work files.");

            // Network exposure
            c = New(checks, "fw", "Network exposure", "Firewall on for every network type", "FW", 10);
            var profiles = new[] { "Domain", "Private", "Public" };
            var fwValues = profiles.Select(p => Field(baseline, "Firewall - " + p + " Profile")).ToArray();
            if (fwValues.All(v => v.Length == 0)) Unknown(c, "Firewall status not reported.");
            else
            {
                var off = profiles.Where((p, i) => fwValues[i].Length > 0 && !On(fwValues[i])).ToList();
                if (off.Count == 0) Pass(c, "On for domain, private and public networks.");
                else if (off.Contains("Public")) Fail(c, "Off for: " + string.Join(", ", off) + ".", "Turn the firewall back on, especially for public networks such as café and hotel Wi-Fi.");
                else Warn(c, "Off for: " + string.Join(", ", off) + ".", "Turn the firewall back on unless another firewall replaces it.");
            }

            c = New(checks, "rdp", "Network exposure", "Remote Desktop off", "RDP", 5);
            string rdp = Field(baseline, "Remote Desktop");
            if (rdp.Length == 0) Unknown(c, "Remote Desktop status not reported.");
            else if (rdp.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (policy.AllowRemoteDesktop) Pass(c, "Enabled, which your policy allows.");
                else Warn(c, "Remote Desktop accepts connections.", "Turn it off if nobody needs it, or limit it to the office network or VPN with Network Level Authentication.");
            }
            else Pass(c, "Remote Desktop is off.");

            c = New(checks, "smb1", "Network exposure", "SMBv1 off", "SMB1", 6);
            string smb = Field(baseline, "SMBv1 Protocol");
            if (smb.Length == 0) Unknown(c, "SMBv1 status not reported.");
            else if (TextUtil.Contains(smb, "Enabled") && !TextUtil.Contains(smb, "Disabled")) Fail(c, "SMBv1 is turned on.", "Turn off SMBv1 (Windows Features). It is the protocol WannaCry used.");
            else Pass(c, "SMBv1 is off.");

            c = New(checks, "drives", "Network exposure", "Mapped drives point to known servers", "MAP", 4);
            var drives = s.Section("drives");
            var driveRows = Rows(drives, null).Where(r => !Placeholder(Cell(r, "Drive Letter"))).ToList();
            var unknownServers = driveRows.Where(r => Cell(r, "Matches Known Server List").StartsWith("No", StringComparison.OrdinalIgnoreCase)).ToList();
            if (drives == null) Unknown(c, "Mapped drives not reported.");
            else if (unknownServers.Count > 0) Fail(c, string.Join(", ", unknownServers.Select(r => Cell(r, "Drive Letter") + " → " + Cell(r, "Server IP"))) + " not on the known server list.", "Find out what these servers are. Unknown shares are a common way to move data off a PC.");
            else Pass(c, driveRows.Count == 0 ? "No mapped drives." : driveRows.Count + " mapped drive(s), all to known servers.");

            // Identity and access
            var admins = Rows(security, "administrative").Select(r => Cell(r, "Account Name")).Where(a => !Placeholder(a)).ToList();
            c = New(checks, "admins", "Identity and access", "Local administrators kept to a minimum", "ADM", 7);
            if (!securityRead || admins.Count == 0) Unknown(c, "Administrator accounts not reported.");
            else
            {
                var unapproved = policy.ApprovedAdmins.Count == 0 ? new List<string>() : admins.Where(a => !policy.IsApprovedAdmin(a)).ToList();
                string detail = admins.Count + " administrator account(s): " + string.Join(", ", admins.Select(ShortAccount)) + ".";
                if (unapproved.Count > 0) Fail(c, detail + " Not approved: " + string.Join(", ", unapproved.Select(ShortAccount)) + ".", "Remove administrator rights from accounts that aren't on your approved list.");
                else if (admins.Count > policy.MaxAdmins) Warn(c, detail, "Your policy allows " + policy.MaxAdmins + ". Daily work should use standard accounts.");
                else Pass(c, detail);
            }

            c = New(checks, "uac", "Identity and access", "User Account Control on", "UAC", 6);
            string uac = Field(baseline, "User Account Control");
            if (uac.Length == 0) Unknown(c, "UAC status not reported.");
            else if (On(uac)) Pass(c, "UAC is on.");
            else Fail(c, "UAC is off.", "Turn User Account Control back on so programs must ask before changing the system.");

            c = New(checks, "guest", "Identity and access", "Guest account off", "GST", 4);
            string guest = Field(baseline, "Guest Account");
            if (guest.Length == 0) Unknown(c, "Guest account status not reported.");
            else if (TextUtil.Contains(guest, "Enabled") && !TextUtil.Contains(guest, "Disabled")) Fail(c, "The Guest account is on.", "Turn the Guest account off. It allows sign-in without a password.");
            else Pass(c, "The Guest account is off.");

            // Updates
            c = New(checks, "patch", "Updates", "Security updates installed recently", "PAT", 9);
            string patchText = Field(Block(security, "patch"), "Latest Security Update");
            DateTime patchDate;
            if (TryDateAfter(patchText, "Installed:", out patchDate) && s.Generated != DateTime.MinValue)
            {
                int age = Math.Max(0, (int)(s.Generated.Date - patchDate.Date).TotalDays);
                string detail = "Latest security update was installed " + Days(age) + " before the audit.";
                if (age <= policy.MaxPatchAgeDays) Pass(c, detail);
                else if (age <= policy.MaxPatchAgeDays * 2) Warn(c, detail, "Install the latest cumulative update. Your policy allows " + policy.MaxPatchAgeDays + " days.");
                else Fail(c, detail, "This PC is well behind on security updates. Check Windows Update isn't paused or broken.");
            }
            else Unknown(c, patchText.Length > 0 ? patchText : "Security update history not reported.");

            c = New(checks, "restart", "Updates", "No restart pending", "RST", 2);
            string restart = Field(baseline, "Pending Restart");
            if (restart.Length == 0) Unknown(c, "Not reported.");
            else if (restart.StartsWith("Yes", StringComparison.OrdinalIgnoreCase)) Warn(c, "A restart is waiting to finish installing updates.", "Restart the PC so the installed updates take effect.");
            else Pass(c, "No restart pending.");

            // Platform
            c = New(checks, "secureboot", "Platform", "Secure Boot on", "SB", 6);
            string sb = Field(baseline, "Secure Boot");
            if (sb.Length == 0) Unknown(c, "Secure Boot status not reported.");
            else if (On(sb)) Pass(c, "Secure Boot is on.");
            else if (sb.StartsWith("Off", StringComparison.OrdinalIgnoreCase)) Fail(c, "Secure Boot is off.", "Turn Secure Boot on in the BIOS/UEFI settings so only trusted code loads at start-up.");
            else Warn(c, "Secure Boot: " + sb + ".", "Check whether the PC supports Secure Boot and turn it on.");

            c = New(checks, "tpm", "Platform", "TPM present and ready", "TPM", 3);
            string tpm = Field(baseline, "TPM");
            if (tpm.Length == 0) Unknown(c, "TPM status not reported.");
            else if (tpm.StartsWith("Present", StringComparison.OrdinalIgnoreCase) && TextUtil.Contains(tpm, "enabled") && !TextUtil.Contains(tpm, "not activated")) Pass(c, tpm + ".");
            else if (TextUtil.Contains(tpm, "Not present")) Fail(c, "No TPM found.", "Without a TPM, BitLocker and Windows Hello lose most of their protection. Enable it in the BIOS if the PC has one.");
            else Warn(c, tpm + ".", "Enable and activate the TPM in the BIOS.");

            c = New(checks, "activation", "Platform", "Windows activated", "ACT", 2);
            string act = Field(s.Section("os"), "Activation Status");
            if (act.Length == 0) Unknown(c, "Activation status not reported.");
            else if (act.StartsWith("Licensed", StringComparison.OrdinalIgnoreCase)) Pass(c, "Windows is licensed.");
            else Warn(c, "Activation status: " + act + ".", "Activate Windows with a valid licence. Unlicensed copies are a compliance risk.");

            // Software
            var software = Rows(s.Section("software"), null).Select(r => Cell(r, "Software Name")).Where(n => !Placeholder(n)).ToList();
            c = New(checks, "software", "Software", "No risky or banned software", "APP", 7);
            if (s.Section("software") == null) Unknown(c, "Installed software not reported.");
            else
            {
                var banned = software.Where(n => Policy.Matches(n, policy.BannedSoftware)).ToList();
                var risky = software.Where(n => !Policy.Matches(n, policy.ApprovedSoftware) && !banned.Contains(n))
                    .Select(n => new { Name = n, Risk = RiskRules.SoftwareRisk(n) })
                    .Where(x => x.Risk == "Remote access" || x.Risk == "Tunnelling" || x.Risk == "Unwanted").ToList();
                var tools = software.Where(n => !Policy.Matches(n, policy.ApprovedSoftware) && RiskRules.SoftwareRisk(n) == "Admin tool").ToList();
                if (banned.Count > 0 || risky.Count > 0)
                {
                    var names = banned.Select(n => n + " (banned)").Concat(risky.Select(x => x.Name + " (" + x.Risk.ToLowerInvariant() + ")"));
                    Fail(c, string.Join(", ", names) + ".", "Remove it, or add it to Approved software in the policy if IT uses it on purpose.");
                }
                else if (tools.Count > 0) Warn(c, "Admin or network tools: " + string.Join(", ", tools) + ".", "Fine for IT staff, but check this user needs them.");
                else Pass(c, software.Count + " programs, none flagged.");
            }

            c = New(checks, "required", "Software", "Required software installed", "REQ", 4);
            if (policy.RequiredSoftware.Count == 0) Unknown(c, "No required software in the policy.");
            else if (s.Section("software") == null) Unknown(c, "Installed software not reported.");
            else
            {
                var everything = software.Concat(av.Select(r => Cell(r, "Product Name"))).ToList();
                var missing = policy.RequiredSoftware.Where(p => !everything.Any(n => Policy.Matches(n, new[] { p }))).ToList();
                if (missing.Count == 0) Pass(c, "All " + policy.RequiredSoftware.Count + " required program(s) found.");
                else Fail(c, "Missing: " + string.Join(", ", missing) + ".", "Install the software your policy requires on every PC.");
            }

            // Audit quality
            c = New(checks, "coverage", "Audit", "Complete audit", "COV", 2);
            var failedSections = s.Sections.Where(x => x.State == "Error" || x.State == "Skipped").Select(x => x.CleanTitle).ToList();
            if (!s.Administrator) Warn(c, "DeviceDossier ran without administrator rights, so some checks couldn't be read.", "Run DeviceDossier as administrator for a full audit.");
            else if (failedSections.Count > 0) Warn(c, "Couldn't read: " + string.Join(", ", failedSections) + ".", "Re-run DeviceDossier as administrator.");
            else Pass(c, "All " + s.Sections.Count + " sections read as administrator.");

            return new PostureResult(s, checks);
        }


        private static PostureCheck New(List<PostureCheck> list, string id, string group, string title, string shortLabel, int weight)
        {
            var c = new PostureCheck { Id = id, Group = group, Title = title, Short = shortLabel, Weight = weight, Detail = "", Advice = "" };
            list.Add(c);
            return c;
        }

        private static void Pass(PostureCheck c, string detail) { c.Status = CheckStatus.Pass; c.Detail = detail; }
        private static void Warn(PostureCheck c, string detail, string advice) { c.Status = CheckStatus.Warn; c.Detail = detail; c.Advice = advice; }
        private static void Fail(PostureCheck c, string detail, string advice) { c.Status = CheckStatus.Fail; c.Detail = detail; c.Advice = advice; }
        private static void Unknown(PostureCheck c, string detail) { c.Status = CheckStatus.Unknown; c.Detail = detail; }

        private static SnapshotBlock Block(SnapshotSection section, string captionContains)
        {
            if (section == null) return null;
            foreach (var b in section.Blocks)
                if (captionContains == null || TextUtil.Contains(b.Caption, captionContains)) return b;
            return null;
        }

        private static IEnumerable<Dictionary<string, string>> Rows(SnapshotSection section, string captionContains)
        {
            if (section == null) yield break;
            foreach (var b in section.Blocks)
            {
                if (b.Kind != BlockKind.Table) continue;
                if (captionContains != null && !TextUtil.Contains(b.Caption, captionContains)) continue;
                foreach (var r in b.Rows) yield return r;
            }
        }

        private static string Field(SnapshotSection section, string name)
        {
            if (section == null) return "";
            foreach (var b in section.Blocks)
            {
                if (b.Kind != BlockKind.Fields) continue;
                var v = b.Field(name);
                if (v != null) return v.Trim();
            }
            return "";
        }

        private static string Field(SnapshotBlock block, string name)
        {
            if (block == null) return "";
            return (block.Field(name) ?? "").Trim();
        }

        /// <summary>Reads "Label: value" out of a "a: x | b: y" details string.</summary>
        private static string Field(string details, string label)
        {
            int i = (details ?? "").IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string rest = details.Substring(i + label.Length).TrimStart();
            int bar = rest.IndexOf('|');
            return (bar >= 0 ? rest.Substring(0, bar) : rest).Trim();
        }

        public static string Cell(Dictionary<string, string> row, string column)
        {
            string v;
            return row != null && row.TryGetValue(column, out v) ? (v ?? "").Trim() : "";
        }

        public static bool Placeholder(string v)
        {
            if (TextUtil.IsBlank(v)) return true;
            string t = v.Trim();
            return t.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("None", StringComparison.OrdinalIgnoreCase) || t.StartsWith("No ", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("Requires admin", StringComparison.OrdinalIgnoreCase);
        }

        private static bool On(string v)
        {
            return (v ?? "").TrimStart().StartsWith("On", StringComparison.OrdinalIgnoreCase);
        }

        private static string ShortAccount(string a)
        {
            int slash = a.LastIndexOf('\\');
            return slash >= 0 ? a.Substring(slash + 1) : a;
        }

        private static string Days(int d)
        {
            return d == 1 ? "1 day" : d.ToString(CultureInfo.InvariantCulture) + " days";
        }

        private static readonly string[] DateFormats = { "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy" };

        public static bool TryDateAfter(string text, string label, out DateTime date)
        {
            date = DateTime.MinValue;
            int i = (text ?? "").IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            string rest = text.Substring(i + label.Length).Trim().TrimStart(':').Trim();
            var token = new string(rest.TakeWhile(ch => char.IsDigit(ch) || ch == '-' || ch == '/' || ch == '.').ToArray()).TrimEnd('.');
            return DateTime.TryParseExact(token, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
