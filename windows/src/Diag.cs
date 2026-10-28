using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace TapMirror
{
    /// <summary>What a bug report needs from the PC: versions, setup and the end of the log.</summary>
    static class Diag
    {
        public static string Version
        {
            get
            {
                var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(Diag).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return a != null ? a.InformationalVersion : "?";
            }
        }

        public static string Collect()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TapMirror PC: " + Version);
            sb.AppendLine("Windows: " + WindowsVersion());
            string dir = Tools.ScrcpyDir();
            sb.AppendLine("scrcpy: " + (dir == null ? "not found" :
                FirstLine(Tools.Run(Path.Combine(dir, "scrcpy.exe"), "--version", 5000)) + " (" + dir + ")"));
            // line 2 is the real version ("Version 37.0.1-..."); line 1 is the protocol version
            string[] adb = Tools.Adb("version", 5000).Split('\n');
            sb.AppendLine("adb: " + (adb.Length > 1 ? adb[1] : adb[0]).Trim() + " (" + Tools.AdbPath() + ")");
            sb.AppendLine("Firewall may block: " + (Tools.FirewallMayBlock() ? "yes" : "no"));
            sb.AppendLine("This PC: " + Server.LanAddresses());
            sb.AppendLine("Phones allowed: " + Config.Allowed.Count + ", blocked: " + Config.Blocked.Count);
            sb.AppendLine();
            sb.AppendLine("Log:");
            try
            {
                var lines = File.ReadAllLines(Log.FilePath);
                foreach (string l in lines.Skip(Math.Max(0, lines.Length - 60))) sb.AppendLine(l);
            }
            catch (Exception e) { sb.AppendLine("(no log: " + e.Message + ")"); }
            return sb.ToString();
        }

        static string FirstLine(string s)
        {
            return s.Split('\n')[0].Trim();
        }

        static string WindowsVersion()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string build = (string)k.GetValue("CurrentBuild", "0");
                    int b;
                    int.TryParse(build, out b);
                    // ProductName says "Windows 10" on Windows 11 too; the build number tells them apart
                    return "Windows " + (b >= 22000 ? "11" : "10") + " " + k.GetValue("EditionID", "") + " " +
                           k.GetValue("DisplayVersion", "") + " (build " + build + "." + k.GetValue("UBR", 0) + ")";
                }
            }
            catch { return Environment.OSVersion.ToString(); }
        }
    }
}
