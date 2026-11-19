using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DriftWatch.Core
{
    /// <summary>Window preferences, kept in %APPDATA%\DriftWatch\settings.json.</summary>
    public sealed class AppSettings
    {
        public AppSettings()
        {
            Theme = "system";
            Recurse = true;
            Watch = true;
            Notify = true;
            RememberFolder = true;
            RecentFolders = new List<string>();
        }

        /// <summary>"system", "dark" or "light".</summary>
        public string Theme { get; set; }
        public string LastFolder { get; set; }
        public bool Recurse { get; set; }
        public bool Watch { get; set; }
        public bool Notify { get; set; }
        public bool RememberFolder { get; set; }
        public List<string> RecentFolders { get; private set; }

        /// <summary>Where settings and the policy live. DRIFTWATCH_HOME overrides it (used for portable setups and tests).</summary>
        public static string Folder
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("DRIFTWATCH_HOME");
                if (!string.IsNullOrEmpty(over)) return over;
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData)) appData = Path.GetTempPath();
                return Path.Combine(appData, "DriftWatch");
            }
        }

        private static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = MiniJson.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null) return s;
                s.Theme = MiniJson.GetString(d, "theme", s.Theme);
                s.LastFolder = MiniJson.GetString(d, "lastFolder", null);
                s.Recurse = MiniJson.GetBool(d, "recurse", s.Recurse);
                s.Watch = MiniJson.GetBool(d, "watch", s.Watch);
                s.Notify = MiniJson.GetBool(d, "notify", s.Notify);
                s.RememberFolder = MiniJson.GetBool(d, "rememberFolder", s.RememberFolder);
                s.RecentFolders.AddRange(MiniJson.GetList(d, "recentFolders"));
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)) throw;
            }
            return s;
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var d = new Dictionary<string, object>();
                d["theme"] = Theme;
                d["lastFolder"] = RememberFolder ? LastFolder : null;
                d["recurse"] = Recurse;
                d["watch"] = Watch;
                d["notify"] = Notify;
                d["rememberFolder"] = RememberFolder;
                d["recentFolders"] = RememberFolder ? RecentFolders : new List<string>();
                File.WriteAllText(FilePath, MiniJson.Write(d) + "\n", new UTF8Encoding(false));
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void AddRecent(string folder)
        {
            RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            RecentFolders.Insert(0, folder);
            while (RecentFolders.Count > 6) RecentFolders.RemoveAt(RecentFolders.Count - 1);
        }

        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
