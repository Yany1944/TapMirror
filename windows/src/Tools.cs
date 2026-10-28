using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace TapMirror
{
    /// <summary>Finding and running scrcpy/adb, downloading scrcpy, firewall and window helpers.</summary>
    static class Tools
    {
        public const string PhonePackage = "io.github.yany1944.tapmirror";

        // ---------- scrcpy and adb ----------

        /// <summary>Folder that holds scrcpy.exe, or null.</summary>
        public static string ScrcpyDir()
        {
            var candidates = new List<string>();
            if (Config.ScrcpyDir != "") candidates.Add(Config.ScrcpyDir);
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(exeDir);
            candidates.Add(Path.Combine(exeDir, "scrcpy"));
            if (Directory.Exists(Config.LocalData))
            {
                // downloaded copies: newest version first
                candidates.AddRange(Directory.GetDirectories(Config.LocalData, "scrcpy-win64-*")
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase));
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            candidates.AddRange(path.Split(';'));
            foreach (string d in candidates)
            {
                try
                {
                    if (d.Trim() != "" && File.Exists(Path.Combine(d.Trim(), "scrcpy.exe"))) return d.Trim();
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        public static string AdbPath()
        {
            if (Config.Adb != "" && File.Exists(Config.Adb)) return Config.Adb;
            string dir = ScrcpyDir();
            if (dir != null && File.Exists(Path.Combine(dir, "adb.exe"))) return Path.Combine(dir, "adb.exe");
            return "adb.exe"; // from PATH, if any
        }

        /// <summary>Runs a console program without a window and returns stdout + stderr.</summary>
        public static string Run(string exe, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var outp = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    return outp.Result + err.Result;
                }
            }
            catch (Exception e)
            {
                return "error: " + e.Message;
            }
        }

        public static string Adb(string args, int timeoutMs)
        {
            return Run(AdbPath(), args, timeoutMs);
        }

        /// <summary>Wireless debugging uses a random port; find it over mDNS by the phone's IP.</summary>
        public static int FindTlsPort(string ip, int timeoutMs)
        {
            var until = DateTime.Now.AddMilliseconds(timeoutMs);
            while (true)
            {
                foreach (string line in Adb("mdns services", 5000).Split('\n'))
                {
                    if (!line.Contains("_adb-tls-connect._tcp")) continue;
                    var m = Regex.Match(line, Regex.Escape(ip) + @":(\d+)");
                    if (m.Success) return int.Parse(m.Groups[1].Value);
                }
                if (DateTime.Now > until) return 0;
                Thread.Sleep(700);
            }
        }

        public static bool Connect(string serial, out string output)
        {
            output = "";
            Adb("disconnect " + serial, 3000);
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) Thread.Sleep(700);
                output = Adb("connect " + serial, 6000).Trim();
                if (output.Contains("connected to") && !output.Contains("failed")) return true;
            }
            return false;
        }

        public static bool AdbServerRunning()
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                    return c.ConnectAsync("127.0.0.1", 5037).Wait(300) && c.Connected;
            }
            catch { return false; }
        }

        /// <summary>
        /// Stops the adb server after a session if TapMirror started it and no device uses it.
        /// Waits first: the phone restarts adbd when the session ends, so USB devices vanish for a moment.
        /// </summary>
        public static void StopAdbServerIfOurs(bool wasRunning)
        {
            if (wasRunning) return;
            Thread.Sleep(4000);
            string devices = Adb("devices", 5000);
            if (devices.Split('\n').Count(l => l.Contains("\t")) == 0) Adb("kill-server", 5000);
        }

        // ---------- downloading scrcpy ----------

        /// <summary>Downloads the latest official scrcpy for Windows from GitHub, checks its SHA-256 and unpacks it.</summary>
        public static string DownloadScrcpy(Action<string> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.3
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "TapMirror";
                json = wc.DownloadString("https://api.github.com/repos/Genymobile/scrcpy/releases/latest");
            }
            var zipMatch = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/(scrcpy-win64-v[^\"/]*\\.zip))\"");
            if (!zipMatch.Success) throw new Exception("No Windows build in the latest scrcpy release.");
            string zipUrl = zipMatch.Groups[1].Value, zipName = zipMatch.Groups[2].Value;
            var sumsMatch = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/SHA256SUMS\\.txt)\"");

            Directory.CreateDirectory(Config.LocalData);
            string zipPath = Path.Combine(Path.GetTempPath(), zipName);
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "TapMirror";
                wc.DownloadProgressChanged += (s, e) => progress("Downloading " + zipName + ": " + e.ProgressPercentage + "%");
                wc.DownloadFileTaskAsync(zipUrl, zipPath).Wait();
                if (sumsMatch.Success)
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "TapMirror";
                    string sums = wc.DownloadString(sumsMatch.Groups[1].Value);
                    var line = sums.Split('\n').FirstOrDefault(l => l.Contains(zipName));
                    if (line == null) throw new Exception("No checksum for " + zipName + ".");
                    string expected = line.Trim().Split(' ')[0].ToLowerInvariant();
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var f = File.OpenRead(zipPath))
                        actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (actual != expected) throw new Exception("Checksum mismatch for " + zipName + ".");
                }
            }

            progress("Unpacking " + zipName);
            string tmp = Path.Combine(Config.LocalData, "unpack-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(zipPath, tmp);
            File.Delete(zipPath);
            string exe = Directory.GetFiles(tmp, "scrcpy.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null) throw new Exception("scrcpy.exe not found in " + zipName + ".");
            string target = Path.Combine(Config.LocalData, Path.GetFileNameWithoutExtension(zipName));
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(Path.GetDirectoryName(exe), target);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            return target;
        }

        // ---------- Windows Firewall ----------

        const string RuleName = "TapMirror";

        /// <summary>True if the firewall is on for the active network and has no TapMirror rule.</summary>
        public static bool FirewallMayBlock()
        {
            try
            {
                Type t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                object fw = Activator.CreateInstance(t);
                int profiles = (int)t.InvokeMember("CurrentProfileTypes", BindingFlags.GetProperty, null, fw, null);
                bool on = false;
                foreach (int bit in new[] { 1, 2, 4 })
                {
                    if ((profiles & bit) != 0 &&
                        (bool)t.InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, fw, new object[] { bit }))
                        on = true;
                }
                if (!on) return false;
                object rules = t.InvokeMember("Rules", BindingFlags.GetProperty, null, fw, null);
                try
                {
                    rules.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, rules, new object[] { RuleName });
                    return false;
                }
                catch { return true; }
            }
            catch { return false; }
        }

        /// <summary>Adds an inbound rule for the TapMirror UDP port (shows the UAC prompt).</summary>
        public static bool AddFirewallRule()
        {
            string exe = System.Windows.Forms.Application.ExecutablePath;
            string cmd = "/c netsh advfirewall firewall delete rule name=\"" + RuleName + "\" >nul & " +
                         "netsh advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow enable=yes " +
                         "profile=any protocol=UDP localport=" + Server.Port + " program=\"" + exe + "\"";
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; } // UAC cancelled
        }

        // ---------- windows ----------

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        public static IntPtr WindowOf(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return IntPtr.Zero;
                p.Refresh();
                return p.MainWindowHandle;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// Windows refuses SetForegroundWindow from background apps; attaching to the foreground
        /// thread's input lifts that, and a topmost flip makes the window visible either way.
        /// </summary>
        public static void BringToFront(IntPtr h)
        {
            ShowWindow(h, 9); // SW_RESTORE
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0003); // HWND_TOPMOST, keep size and position
            SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0003); // HWND_NOTOPMOST
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }
}
