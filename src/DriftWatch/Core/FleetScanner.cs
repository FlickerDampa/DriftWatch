using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DriftWatch.Core
{
    /// <summary>A device found in a folder of reports, with its audits oldest first.</summary>
    public sealed class DeviceHistory
    {
        public DeviceHistory(string device)
        {
            Device = device;
            Reports = new List<Snapshot>();
        }

        public string Device { get; private set; }
        public List<Snapshot> Reports { get; private set; }
        public Snapshot Latest { get { return Reports.Count > 0 ? Reports[Reports.Count - 1] : null; } }
        public Snapshot Previous { get { return Reports.Count > 1 ? Reports[Reports.Count - 2] : null; } }
    }

    public sealed class FleetResult
    {
        public FleetResult()
        {
            Devices = new List<DeviceHistory>();
            Comparisons = new List<Comparison>();
            Skipped = new List<string>();
        }

        public string Folder { get; set; }
        public List<DeviceHistory> Devices { get; private set; }
        /// <summary>One comparison per device that has at least two audits: its latest against the one before.</summary>
        public List<Comparison> Comparisons { get; private set; }
        /// <summary>Files that could not be read, with the reason.</summary>
        public List<string> Skipped { get; private set; }
    }

    /// <summary>Finds every DeviceDossier XML report in a folder (and its subfolders) and groups them by device.</summary>
    public static class FleetScanner
    {
        public static FleetResult Scan(string folder, bool recurse)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder not found: " + folder);
            var result = new FleetResult { Folder = Path.GetFullPath(folder) };
            var byDevice = new Dictionary<string, DeviceHistory>(StringComparer.OrdinalIgnoreCase);
            var files = Directory.GetFiles(folder, "*.xml", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                Snapshot snap;
                try
                {
                    snap = Snapshot.Load(file);
                }
                catch (Exception ex)
                {
                    if (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        result.Skipped.Add(Path.GetFileName(file) + ": " + ex.Message);
                        continue;
                    }
                    throw;
                }
                DeviceHistory history;
                if (!byDevice.TryGetValue(snap.Device, out history))
                {
                    history = new DeviceHistory(snap.Device);
                    byDevice[snap.Device] = history;
                }
                history.Reports.Add(snap);
            }
            foreach (var h in byDevice.Values.OrderBy(d => d.Device, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = h.Reports.OrderBy(r => r.Generated).ToList();
                h.Reports.Clear();
                h.Reports.AddRange(ordered);
                result.Devices.Add(h);
                if (h.Previous != null) result.Comparisons.Add(DiffEngine.Compare(h.Previous, h.Latest));
            }
            result.Comparisons.Sort((a, b) =>
            {
                int sa = a.Highest.HasValue ? (int)a.Highest.Value : 99, sb = b.Highest.HasValue ? (int)b.Highest.Value : 99;
                if (sa != sb) return sa.CompareTo(sb);
                return b.Changes.Count.CompareTo(a.Changes.Count);
            });
            return result;
        }
    }
}
