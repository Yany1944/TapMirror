using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TapMirror
{
    /// <summary>Settings in %APPDATA%\TapMirror\TapMirror.ini (plain key=value lines).</summary>
    static class Config
    {
        public static readonly string AppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TapMirror");
        public static readonly string LocalData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TapMirror");
        static readonly string IniPath = Path.Combine(AppData, "TapMirror.ini");

        public static string PcId;
        public static string ScrcpyDir = "";
        public static string Adb = "";
        public static string ExtraArgs = "";
        public static bool FirstRunDone;
        /// <summary>Phone id → name, for phones allowed or blocked on this PC.</summary>
        public static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>();
        public static readonly Dictionary<string, string> Blocked = new Dictionary<string, string>();

        static readonly object fileLock = new object();

        public static void Load()
        {
            if (File.Exists(IniPath))
            {
                foreach (string raw in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.StartsWith("#") || line.StartsWith(";") || eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "pc.id") PcId = v;
                    else if (k == "scrcpy") ScrcpyDir = v;
                    else if (k == "adb") Adb = v;
                    else if (k == "args") ExtraArgs = v;
                    else if (k == "firstrun.done") FirstRunDone = v == "1";
                    else if (k.StartsWith("allow.")) Allowed[k.Substring(6)] = v;
                    else if (k.StartsWith("block.")) Blocked[k.Substring(6)] = v;
                }
            }
            if (string.IsNullOrEmpty(PcId))
            {
                byte[] b = new byte[8];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
                PcId = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                Save();
            }
        }

        public static void Save()
        {
            lock (fileLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# TapMirror settings. Edit while TapMirror is not running.");
                sb.AppendLine("# scrcpy = folder with scrcpy.exe (empty: found automatically)");
                sb.AppendLine("# adb    = path to adb.exe (empty: the one next to scrcpy)");
                sb.AppendLine("# args   = extra scrcpy options added to every session, e.g. --window-borderless");
                sb.AppendLine("pc.id=" + PcId);
                sb.AppendLine("scrcpy=" + ScrcpyDir);
                sb.AppendLine("adb=" + Adb);
                sb.AppendLine("args=" + ExtraArgs);
                sb.AppendLine("firstrun.done=" + (FirstRunDone ? "1" : "0"));
                foreach (var kv in Allowed) sb.AppendLine("allow." + kv.Key + "=" + kv.Value);
                foreach (var kv in Blocked) sb.AppendLine("block." + kv.Key + "=" + kv.Value);
                Directory.CreateDirectory(AppData);
                File.WriteAllText(IniPath, sb.ToString(), new UTF8Encoding(false));
            }
        }
    }

    static class Log
    {
        static readonly object fileLock = new object();
        public static readonly string FilePath = Path.Combine(Config.LocalData, "TapMirror.log");

        public static void Write(string msg)
        {
            try
            {
                lock (fileLock)
                {
                    Directory.CreateDirectory(Config.LocalData);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("MM-dd HH:mm:ss ") + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
