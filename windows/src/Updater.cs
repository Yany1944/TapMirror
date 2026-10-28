using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TapMirror
{
    /// <summary>
    /// Checks GitHub for a newer release and replaces this exe with it.
    /// A running exe can't be overwritten but can be renamed, so the old one becomes TapMirror.exe.old
    /// and the next start deletes it.
    /// </summary>
    static class Updater
    {
        const string Api = "https://api.github.com/repos/Yany1944/TapMirror/releases/latest";

        /// <summary>Tag of a newer release found by the last check ("v0.1.2"), or null.</summary>
        public static volatile string NewTag;

        public static string NewVersion
        {
            get { string t = NewTag; return t == null ? null : t.TrimStart('v', 'V'); }
        }

        public static string ReleasePage
        {
            get { return Tray.Repo + "/releases/tag/" + NewTag; }
        }

        /// <summary>Asks GitHub for the latest release. Returns true if it is newer than this exe. Throws on network errors.</summary>
        public static bool Check()
        {
            string json = Get(Api);
            var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success) return false;
            Version latest = Parse(m.Groups[1].Value), current = Parse(Diag.Version);
            bool newer = latest != null && current != null && latest > current;
            NewTag = newer ? m.Groups[1].Value : null;
            return newer;
        }

        static Version Parse(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash); // "0.2.0-beta" counts as 0.2.0
            Version r;
            return Version.TryParse(v, out r) ? r : null;
        }

        /// <summary>
        /// Downloads the new exe, checks its SHA-256 against the release's SHA256SUMS.txt and swaps it in.
        /// Returns the path to start; the caller exits right after starting it.
        /// </summary>
        public static string Download(Action<string> progress)
        {
            string tag = NewTag;
            string baseUrl = Tray.Repo + "/releases/download/" + tag + "/";
            string exe = Application.ExecutablePath;
            string fresh = exe + ".new";

            progress("Downloading TapMirror " + NewVersion + "…");
            using (var wc = Client())
            {
                wc.DownloadProgressChanged += (s, e) => progress("Downloading TapMirror " + NewVersion + ": " + e.ProgressPercentage + "%");
                wc.DownloadFileTaskAsync(baseUrl + "TapMirror.exe", fresh).Wait();
            }
            string sums = Get(baseUrl + "SHA256SUMS.txt");
            var line = sums.Split('\n').FirstOrDefault(l => l.TrimEnd().EndsWith("TapMirror.exe"));
            if (line == null) { File.Delete(fresh); throw new Exception("The release has no checksum for TapMirror.exe."); }
            string expected = line.Trim().Split(' ')[0].ToLowerInvariant(), actual;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(fresh))
                actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (actual != expected) { File.Delete(fresh); throw new Exception("The downloaded file doesn't match its checksum."); }

            string old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
            Log.Write("updated the exe to " + NewVersion);
            return exe;
        }

        /// <summary>Called at startup: removes what the last update left behind.</summary>
        public static void CleanUp()
        {
            foreach (string f in new[] { Application.ExecutablePath + ".old", Application.ExecutablePath + ".new" })
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.3
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "TapMirror/" + Diag.Version;
            return wc;
        }

        static string Get(string url)
        {
            using (var wc = Client()) return wc.DownloadString(url);
        }

        public static void Restart(string exe)
        {
            Process.Start(new ProcessStartInfo(exe, "--after-update") { UseShellExecute = true });
        }
    }
}
