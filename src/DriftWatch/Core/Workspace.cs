using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DriftWatch.Core
{
    public sealed class SoftwareInstall
    {
        public DeviceHistory Device { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string InstallDate { get; set; }
    }

    /// <summary>One program across the fleet, from every device's latest audit.</summary>
    public sealed class SoftwareEntry
    {
        public SoftwareEntry()
        {
            Installs = new List<SoftwareInstall>();
        }

        public string Name { get; set; }
        public string Publisher { get; set; }
        public List<SoftwareInstall> Installs { get; private set; }
        /// <summary>"Banned", "Required", "Approved", "Remote access", "Tunnelling", "Unwanted", "Admin tool", "Security" or "".</summary>
        public string Flag { get; set; }

        public int DeviceCount { get { return Installs.Select(i => i.Device).Distinct().Count(); } }
        public List<string> Versions
        {
            get
            {
                var v = Installs.Select(i => i.Version ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                v.Sort((a, b) => RiskRules.CompareVersions(b, a));
                return v;
            }
        }
        public string LatestVersion { get { var v = Versions; return v.Count > 0 ? v[0] : ""; } }
        public bool Risky { get { return Flag == "Banned" || Flag == "Remote access" || Flag == "Tunnelling" || Flag == "Unwanted"; } }
    }

    /// <summary>
    /// Everything DriftWatch has loaded: the devices and their audits, plus cached posture scores and
    /// comparisons. All results honour the current policy.
    /// </summary>
    public sealed class Workspace
    {
        private readonly Dictionary<Snapshot, PostureResult> posture = new Dictionary<Snapshot, PostureResult>();
        private readonly Dictionary<string, Comparison> comparisons = new Dictionary<string, Comparison>(StringComparer.OrdinalIgnoreCase);
        private List<SoftwareEntry> inventory;

        public Workspace(Policy policy)
        {
            Policy = policy ?? new Policy();
            Devices = new List<DeviceHistory>();
            Skipped = new List<string>();
            Loaded = DateTime.Now;
        }

        public string Folder { get; private set; }
        public bool Recurse { get; private set; }
        public List<DeviceHistory> Devices { get; private set; }
        public List<string> Skipped { get; private set; }
        public Policy Policy { get; private set; }
        public DateTime Loaded { get; private set; }

        public bool IsEmpty { get { return Devices.Count == 0; } }
        public int ReportCount { get { return Devices.Sum(d => d.Reports.Count); } }
        public IEnumerable<Snapshot> AllReports { get { return Devices.SelectMany(d => d.Reports); } }

        public static Workspace FromFolder(string folder, bool recurse, Policy policy)
        {
            var fleet = FleetScanner.Scan(folder, recurse);
            var ws = new Workspace(policy) { Folder = fleet.Folder, Recurse = recurse };
            ws.Devices.AddRange(fleet.Devices);
            ws.Skipped.AddRange(fleet.Skipped);
            return ws;
        }

        /// <summary>Adds individual report files. Returns the reports that were loaded; problems go to <paramref name="errors"/>.</summary>
        public List<Snapshot> AddFiles(IEnumerable<string> paths, List<string> errors)
        {
            var added = new List<Snapshot>();
            foreach (var path in paths)
            {
                Snapshot existing = AllReports.FirstOrDefault(r => string.Equals(r.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
                if (existing != null) { added.Add(existing); continue; }
                try
                {
                    var snap = Snapshot.Load(path);
                    var dev = Devices.FirstOrDefault(d => string.Equals(d.Device, snap.Device, StringComparison.OrdinalIgnoreCase));
                    if (dev == null)
                    {
                        dev = new DeviceHistory(snap.Device);
                        Devices.Add(dev);
                        Devices.Sort((a, b) => string.Compare(a.Device, b.Device, StringComparison.OrdinalIgnoreCase));
                    }
                    dev.Reports.Add(snap);
                    dev.Reports.Sort((a, b) => a.Generated.CompareTo(b.Generated));
                    added.Add(snap);
                }
                catch (Exception ex)
                {
                    if (!(ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                    if (errors != null) errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            inventory = null;
            return added;
        }

        public DeviceHistory DeviceOf(Snapshot s)
        {
            return Devices.FirstOrDefault(d => d.Reports.Contains(s));
        }

        public DeviceHistory Find(string device)
        {
            return Devices.FirstOrDefault(d => string.Equals(d.Device, device, StringComparison.OrdinalIgnoreCase));
        }

        public PostureResult Posture(Snapshot s)
        {
            if (s == null) return null;
            PostureResult r;
            if (!posture.TryGetValue(s, out r))
            {
                r = PostureEngine.Evaluate(s, Policy);
                posture[s] = r;
            }
            return r;
        }

        public Comparison Compare(Snapshot older, Snapshot newer)
        {
            string key = (older.Path ?? older.GetHashCode().ToString(CultureInfo.InvariantCulture)) + "|" + (newer.Path ?? newer.GetHashCode().ToString(CultureInfo.InvariantCulture));
            Comparison c;
            if (!comparisons.TryGetValue(key, out c))
            {
                c = DiffEngine.Compare(older, newer);
                PolicyEngine.Apply(c, Policy);
                comparisons[key] = c;
            }
            return c;
        }

        /// <summary>The device's latest audit compared with the one before, or null with only one audit.</summary>
        public Comparison Latest(DeviceHistory d)
        {
            return d == null || d.Previous == null ? null : Compare(d.Previous, d.Latest);
        }

        public bool IsStale(DeviceHistory d)
        {
            return d.Latest != null && d.Latest.Generated != DateTime.MinValue && (DateTime.Now - d.Latest.Generated).TotalDays > Policy.StaleAuditDays;
        }

        public bool NeedsAttention(DeviceHistory d)
        {
            var c = Latest(d);
            var p = Posture(d.Latest);
            return (c != null && c.AnyAtOrAbove(Severity.High)) || (p != null && p.Score < Policy.MinimumScore);
        }

        public double AverageScore
        {
            get { return Devices.Count == 0 ? 0 : Devices.Average(d => (double)Posture(d.Latest).Score); }
        }

        /// <summary>Replaces the policy and clears cached results so everything is re-scored.</summary>
        public void SetPolicy(Policy p)
        {
            Policy = p ?? new Policy();
            Invalidate();
        }

        public void Invalidate()
        {
            posture.Clear();
            comparisons.Clear();
            inventory = null;
        }

        /// <summary>Marks a change as expected (or not) and remembers it in the policy.</summary>
        public void Acknowledge(Comparison cmp, Change change, bool value)
        {
            string sig = change.Signature(cmp.Current.Device);
            Policy.Acknowledged.RemoveAll(s => string.Equals(s, sig, StringComparison.OrdinalIgnoreCase));
            if (value) Policy.Acknowledged.Add(sig);
            change.Acknowledged = value;
            cmp.Resort();
        }

        public List<SoftwareEntry> Inventory
        {
            get
            {
                if (inventory == null) inventory = BuildInventory();
                return inventory;
            }
        }

        private static readonly Regex VersionSuffix = new Regex(@"\s+v?\d+(\.\d+)+\b.*$", RegexOptions.Compiled);

        public static string SoftwareKey(string name)
        {
            string n = VersionSuffix.Replace(name ?? "", "").Trim();
            return n.Length == 0 ? (name ?? "").Trim() : n;
        }

        private List<SoftwareEntry> BuildInventory()
        {
            var map = new Dictionary<string, SoftwareEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in Devices)
            {
                var s = d.Latest == null ? null : d.Latest.Section("software");
                if (s == null) continue;
                foreach (var b in s.Blocks.Where(x => x.Kind == BlockKind.Table))
                {
                    foreach (var r in b.Rows)
                    {
                        string name = PostureEngine.Cell(r, "Software Name");
                        if (PostureEngine.Placeholder(name)) continue;
                        string key = SoftwareKey(name);
                        SoftwareEntry e;
                        if (!map.TryGetValue(key, out e))
                        {
                            e = new SoftwareEntry { Name = key, Publisher = PostureEngine.Cell(r, "Publisher") };
                            map[key] = e;
                        }
                        if (e.Publisher.Length == 0) e.Publisher = PostureEngine.Cell(r, "Publisher");
                        e.Installs.Add(new SoftwareInstall { Device = d, Name = name, Version = PostureEngine.Cell(r, "Version"), Publisher = PostureEngine.Cell(r, "Publisher"), InstallDate = PostureEngine.Cell(r, "Install Date") });
                    }
                }
            }
            foreach (var e in map.Values)
            {
                if (Policy.Matches(e.Name, Policy.BannedSoftware)) e.Flag = "Banned";
                else if (Policy.Matches(e.Name, Policy.RequiredSoftware)) e.Flag = "Required";
                else if (Policy.Matches(e.Name, Policy.ApprovedSoftware)) e.Flag = "Approved";
                else e.Flag = RiskRules.SoftwareRisk(e.Name);
            }
            return map.Values.OrderByDescending(e => e.Risky).ThenByDescending(e => e.DeviceCount).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
