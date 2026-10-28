using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TapMirror
{
    /// <summary>Entry point and the tray icon. Idle cost: one blocked UDP socket, no timers, no polling.</summary>
    static class Tray
    {
        public const string Repo = "https://github.com/Yany1944/TapMirror";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static NotifyIcon icon;
        static SynchronizationContext ui;

        [STAThread]
        static void Main()
        {
            bool fresh;
            using (new Mutex(true, @"Local\TapMirrorTray", out fresh))
            {
                if (!fresh)
                {
                    MessageBox.Show("TapMirror is already running. Look for its icon in the tray.", "TapMirror");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // log stray errors instead of showing the .NET crash dialog
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Write("error: " + e.Exception);
                ui = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(ui);
                Config.Load();

                icon = new NotifyIcon
                {
                    Icon = AppIcon(SystemInformation.SmallIconSize),
                    Text = "TapMirror",
                    Visible = true,
                    ContextMenuStrip = new ContextMenuStrip(),
                };
                icon.ContextMenuStrip.Opening += (s, e) => BuildMenu();
                icon.DoubleClick += (s, e) => Server.StartUsb();
                BuildMenu();

                try
                {
                    Server.Start(ui);
                }
                catch (SocketException)
                {
                    MessageBox.Show("UDP port " + Server.Port + " is already in use, so phones can't reach TapMirror.\n" +
                                    "Is another copy (or an older version) running?", "TapMirror",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    icon.Visible = false;
                    return;
                }
                Server.Changed += () => icon.Text = Trim63("TapMirror: " + Server.StatusText());
                icon.Text = Trim63("TapMirror: " + Server.StatusText());

                if (!Config.FirstRunDone) ui.Post(_ => FirstRun(), null);
                Application.Run();
                icon.Visible = false;
            }
        }

        public static Icon AppIcon(Size size)
        {
            using (Stream s = typeof(Tray).Assembly.GetManifestResourceStream("TapMirror.ico"))
                return s != null ? new Icon(s, size) : SystemIcons.Application;
        }

        static string Trim63(string s)
        {
            return s.Length > 63 ? s.Substring(0, 63) : s;
        }

        public static void Balloon(string text, ToolTipIcon kind)
        {
            Log.Write(text);
            ui.Post(_ => icon.ShowBalloonTip(6000, "TapMirror", text, kind), null);
        }

        static void FirstRun()
        {
            Config.FirstRunDone = true;
            Config.Save();
            SetAutostart(true);
            if (Tools.ScrcpyDir() == null &&
                MessageBox.Show("TapMirror uses scrcpy (free and open source) to show the phone screen.\n\n" +
                                "Download the latest official scrcpy for Windows from GitHub now (about 10 MB)?",
                                "TapMirror", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                DownloadScrcpy();
            }
            if (Tools.FirewallMayBlock() &&
                MessageBox.Show("Windows Firewall may block phones from finding this PC.\n\n" +
                                "Add a rule that lets TapMirror receive on UDP port " + Server.Port + "? " +
                                "(Windows will ask for administrator rights.)",
                                "TapMirror", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Tools.AddFirewallRule();
            }
            Balloon("TapMirror runs here in the tray and starts with Windows. Install the TapMirror app on your phone and tap its tile.",
                    ToolTipIcon.Info);
        }

        static void BuildMenu()
        {
            var m = icon.ContextMenuStrip;
            m.Items.Clear();
            m.Items.Add(new ToolStripMenuItem(Server.StatusText()) { Enabled = false });
            // for typing into the phone app when searching can't work (some VPNs)
            string addrs = Server.LanAddresses();
            if (addrs != "") m.Items.Add(new ToolStripMenuItem("This PC: " + addrs) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Mirror over USB", null, (s, e) => Server.StartUsb());
            m.Items.Add("Set up a phone without root…", null, (s, e) => SetupForm.ShowOnce());

            var phones = new ToolStripMenuItem("Phones");
            foreach (var kv in Config.Allowed)
            {
                string id = kv.Key, name = kv.Value;
                phones.DropDownItems.Add("Remove \"" + name + "\" (ask again next time)", null, (s, e) =>
                {
                    Config.Allowed.Remove(id);
                    Config.Save();
                });
            }
            foreach (var kv in Config.Blocked)
            {
                string id = kv.Key, name = kv.Value;
                phones.DropDownItems.Add("Unblock \"" + name + "\"", null, (s, e) =>
                {
                    Config.Blocked.Remove(id);
                    Config.Save();
                });
            }
            if (phones.DropDownItems.Count == 0) phones.DropDownItems.Add(new ToolStripMenuItem("No phones yet") { Enabled = false });
            m.Items.Add(phones);

            var scrcpy = new ToolStripMenuItem("scrcpy");
            string dir = Tools.ScrcpyDir();
            scrcpy.DropDownItems.Add(new ToolStripMenuItem(dir ?? "Not installed") { Enabled = false });
            scrcpy.DropDownItems.Add("Download latest from GitHub", null, (s, e) => DownloadScrcpy());
            scrcpy.DropDownItems.Add("Choose scrcpy folder…", null, (s, e) => ChooseScrcpy());
            m.Items.Add(scrcpy);

            if (Tools.FirewallMayBlock())
                m.Items.Add("Allow through Windows Firewall", null, (s, e) => Tools.AddFirewallRule());

            var auto = new ToolStripMenuItem("Start with Windows") { Checked = IsAutostart() };
            auto.Click += (s, e) => SetAutostart(!IsAutostart());
            m.Items.Add(auto);
            m.Items.Add("Open log", null, (s, e) => { if (File.Exists(Log.FilePath)) Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); });
            m.Items.Add("Help (GitHub)", null, (s, e) => Process.Start(Repo));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, (s, e) => { icon.Visible = false; Application.Exit(); });
        }

        static void DownloadScrcpy()
        {
            Balloon("Downloading scrcpy…", ToolTipIcon.Info);
            new Thread(() =>
            {
                try
                {
                    int lastShown = -1;
                    string dir = Tools.DownloadScrcpy(msg =>
                    {
                        // tooltip only: balloons for every percent would be noise
                        int pct;
                        int at = msg.LastIndexOf(' ');
                        if (int.TryParse(msg.Substring(at + 1).TrimEnd('%'), out pct) && pct / 10 == lastShown) return;
                        lastShown = pct / 10;
                        ui.Post(_ => icon.Text = Trim63(msg), null);
                    });
                    Config.ScrcpyDir = dir;
                    Config.Save();
                    Balloon("scrcpy is ready: " + dir, ToolTipIcon.Info);
                }
                catch (Exception e)
                {
                    Balloon("Couldn't download scrcpy: " + (e.InnerException ?? e).Message, ToolTipIcon.Error);
                }
                finally
                {
                    ui.Post(_ => icon.Text = Trim63("TapMirror: " + Server.StatusText()), null);
                }
            }) { IsBackground = true }.Start();
        }

        static void ChooseScrcpy()
        {
            using (var d = new FolderBrowserDialog { Description = "Folder that contains scrcpy.exe" })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(d.SelectedPath, "scrcpy.exe")))
                {
                    MessageBox.Show("There is no scrcpy.exe in that folder.", "TapMirror");
                    return;
                }
                Config.ScrcpyDir = d.SelectedPath;
                Config.Save();
            }
        }

        static bool IsAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("TapMirror") != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("TapMirror", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("TapMirror", false);
            }
        }
    }
}
