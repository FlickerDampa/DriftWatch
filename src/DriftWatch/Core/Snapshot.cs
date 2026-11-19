using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace DriftWatch.Core
{
    public enum BlockKind { Fields, Table }

    /// <summary>One block inside a report section: either name/value fields or a table.</summary>
    public sealed class SnapshotBlock
    {
        public SnapshotBlock(BlockKind kind, string caption)
        {
            Kind = kind;
            Caption = caption ?? "";
            Fields = new List<KeyValuePair<string, string>>();
            Columns = new List<string>();
            Rows = new List<Dictionary<string, string>>();
        }

        public BlockKind Kind { get; private set; }
        public string Caption { get; private set; }
        public List<KeyValuePair<string, string>> Fields { get; private set; }
        public List<string> Columns { get; private set; }
        public List<Dictionary<string, string>> Rows { get; private set; }

        public string Field(string name)
        {
            foreach (var f in Fields) if (string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase)) return f.Value;
            return null;
        }
    }

    public sealed class SnapshotSection
    {
        public SnapshotSection(string id, string number, string title, string state, string note)
        {
            Id = id ?? "";
            Number = number ?? "";
            Title = title ?? "";
            State = string.IsNullOrEmpty(state) ? "Ok" : state;
            Note = note ?? "";
            Blocks = new List<SnapshotBlock>();
        }

        public string Id { get; private set; }
        public string Number { get; private set; }
        public string Title { get; private set; }
        public string State { get; private set; }
        public string Note { get; private set; }
        public List<SnapshotBlock> Blocks { get; private set; }

        /// <summary>Section title without the live totals DeviceDossier appends, such as " - Total: 114 apps".</summary>
        public string CleanTitle
        {
            get
            {
                int dash = Title.IndexOf(" - ", StringComparison.Ordinal);
                string t = dash > 0 ? Title.Substring(0, dash) : Title;
                return TextUtil.TitleCase(t);
            }
        }
    }

    /// <summary>A DeviceDossier XML report loaded for comparison.</summary>
    public sealed class Snapshot
    {
        private Snapshot()
        {
            Sections = new List<SnapshotSection>();
        }

        public string Path { get; private set; }
        public string FileName { get { return System.IO.Path.GetFileName(Path ?? ""); } }
        public string Device { get; private set; }
        public DateTime Generated { get; private set; }
        public string Tool { get; private set; }
        public string RunAs { get; private set; }
        public bool Administrator { get; private set; }
        public bool Laptop { get; private set; }
        public List<SnapshotSection> Sections { get; private set; }

        public SnapshotSection Section(string id)
        {
            foreach (var s in Sections) if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public string Label
        {
            get { return Device + " · " + Generated.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture); }
        }

        public static Snapshot Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Report not found.", path);
            XDocument doc;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            try
            {
                using (var reader = XmlReader.Create(path, settings)) doc = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                throw new FormatException("This file is not valid XML: " + ex.Message, ex);
            }
            var snap = FromDocument(doc);
            snap.Path = System.IO.Path.GetFullPath(path);
            return snap;
        }

        public static Snapshot FromDocument(XDocument doc)
        {
            var root = doc.Root;
            if (root == null || (root.Name.LocalName != "DeviceDossierReport" && root.Name.LocalName != "SysAuditReport"))
                throw new FormatException("This file is not a DeviceDossier XML report. Save the audit as XML in DeviceDossier first.");

            var snap = new Snapshot
            {
                Device = Attr(root, "device"),
                Tool = Attr(root, "tool"),
                RunAs = Attr(root, "runAs"),
                Administrator = Attr(root, "administrator") == "true",
                Laptop = Attr(root, "laptop") == "true"
            };
            DateTime generated;
            if (DateTime.TryParse(Attr(root, "generated"), CultureInfo.InvariantCulture, DateTimeStyles.None, out generated)) snap.Generated = generated;

            foreach (var sec in root.Elements().Where(e => e.Name.LocalName == "Section"))
            {
                var section = new SnapshotSection(Attr(sec, "id"), Attr(sec, "number"), Attr(sec, "title"), Attr(sec, "state"), Attr(sec, "note"));
                foreach (var el in sec.Elements())
                {
                    string name = el.Name.LocalName;
                    if (name == "Fields")
                    {
                        var block = new SnapshotBlock(BlockKind.Fields, Attr(el, "caption"));
                        foreach (var f in el.Elements().Where(e => e.Name.LocalName == "Field"))
                            block.Fields.Add(new KeyValuePair<string, string>(Attr(f, "name"), Clean(f.Value)));
                        section.Blocks.Add(block);
                    }
                    else if (name == "Table")
                    {
                        var block = new SnapshotBlock(BlockKind.Table, Attr(el, "caption"));
                        var cols = el.Elements().FirstOrDefault(e => e.Name.LocalName == "Columns");
                        if (cols != null) foreach (var c in cols.Elements()) block.Columns.Add(Clean(c.Value));
                        foreach (var row in el.Elements().Where(e => e.Name.LocalName == "Row"))
                        {
                            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            int i = 0;
                            foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "Cell"))
                            {
                                string col = Attr(cell, "column");
                                if (string.IsNullOrEmpty(col)) col = i < block.Columns.Count ? block.Columns[i] : "Column " + (i + 1);
                                cells[col] = Clean(cell.Value);
                                i++;
                            }
                            block.Rows.Add(cells);
                        }
                        section.Blocks.Add(block);
                    }
                }
                snap.Sections.Add(section);
            }
            if (string.IsNullOrEmpty(snap.Device)) snap.Device = "Unknown device";
            return snap;
        }

        private static string Attr(XElement e, string name)
        {
            var a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        private static string Clean(string v)
        {
            return (v ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
