using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TapMirror
{
    /// <summary>Entry point and the tray icon. Idle cost: one blocked UDP socket and a once-a-day update check.</summary>
    static class Tray
    {
        public const string Repo = "https://github.com/Yany1944/TapMirror";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static NotifyIcon icon;
        static SynchronizationContext ui;
        static System.Threading.Timer updateTimer;
        static bool updateBalloonShown; // the last balloon is the update offer, so a click on it means "update"
        static bool updating;
        static string restartWith; // set by a finished update: start this exe once we have exited

        [STAThread]
        static void Main(string[] args)
        {
            bool afterUpdate = args.Contains("--after-update");
            using (var single = new Mutex(false, @"Local\TapMirrorTray"))
            {
                bool owned;
                // after an update the old copy may still be closing
                try { owned = single.WaitOne(afterUpdate ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
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

                Updater.CleanUp();
                if (afterUpdate) Balloon("TapMirror was updated to " + Diag.Version + ".", ToolTipIcon.Info);
                if (!Config.FirstRunDone) ui.Post(_ => FirstRun(), null);
                icon.BalloonTipClicked += (s, e) => { if (updateBalloonShown) AskUpdate(); };
                if (args.Contains("--check-updates"))
                    new Thread(() => CheckUpdates(true)) { IsBackground = true }.Start();
                else if (Config.CheckUpdates)
                    updateTimer = new System.Threading.Timer(_ => CheckUpdates(false), null,
                        TimeSpan.FromSeconds(15), TimeSpan.FromDays(1));
                Application.Run();
                icon.Visible = false;
                single.ReleaseMutex();
            }
            // only now, with the port and the single-instance lock free
            if (restartWith != null) Updater.Restart(restartWith);
        }

        static void CheckUpdates(bool manual)
        {
            try
            {
                if (Updater.Check())
                {
                    Log.Write("update available: " + Updater.NewVersion);
                    ui.Post(_ =>
                    {
                        if (manual) { AskUpdate(); return; }
                        icon.ShowBalloonTip(10000, "TapMirror " + Updater.NewVersion + " is available",
                            "You have " + Diag.Version + ". Click here to update, or use the tray menu.", ToolTipIcon.Info);
                        updateBalloonShown = true;
                    }, null);
                }
                else if (manual)
                {
                    Balloon("You have the latest version (" + Diag.Version + ").", ToolTipIcon.Info);
                }
            }
            catch (Exception e)
            {
                Log.Write("update check failed: " + (e.InnerException ?? e).Message);
                if (manual) Balloon("Couldn't check for updates: " + (e.InnerException ?? e).Message, ToolTipIcon.Warning);
            }
        }

        static void AskUpdate()
        {
            updateBalloonShown = false;
            if (updating || Updater.NewTag == null) return;
            var answer = MessageBox.Show(
                "TapMirror " + Updater.NewVersion + " is available. You have " + Diag.Version + ".\n\n" +
                "Update now? TapMirror downloads it from GitHub, checks it and restarts. " +
                "If you're mirroring, the window stays open but the phone ends the session.\n\n" +
                "Yes: update.   No: later (tray menu → Update).   Cancel: see what's new.",
                "TapMirror", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { Process.Start(Updater.ReleasePage); return; }
            if (answer != DialogResult.Yes) return;
            updating = true;
            new Thread(() =>
            {
                try
                {
                    string exe = Updater.Download(msg => ui.Post(_ => icon.Text = Trim63(msg), null));
                    ui.Post(_ =>
                    {
                        restartWith = exe;
                        icon.Visible = false;
                        Application.Exit();
                    }, null);
                }
                catch (Exception e)
                {
                    updating = false;
                    Balloon("Couldn't update: " + (e.InnerException ?? e).Message +
                            " You can download it from the release page (tray menu → What's new).", ToolTipIcon.Error);
                    ui.Post(_ => icon.Text = Trim63("TapMirror: " + Server.StatusText()), null);
                }
            }) { IsBackground = true }.Start();
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
            ui.Post(_ => { updateBalloonShown = false; icon.ShowBalloonTip(6000, "TapMirror", text, kind); }, null);
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
            if (Updater.NewTag != null)
            {
                var upd = new ToolStripMenuItem("Update to " + Updater.NewVersion + "…", null, (s, e) => AskUpdate());
                upd.Font = new Font(upd.Font, FontStyle.Bold);
                m.Items.Add(upd);
                m.Items.Add("What's new in " + Updater.NewVersion, null, (s, e) => Process.Start(Updater.ReleasePage));
            }
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
            m.Items.Add("Copy info for a bug report", null, (s, e) => CopyReport());
            m.Items.Add("Report a bug", null, (s, e) => Process.Start(Repo + "/issues/new/choose"));
            m.Items.Add("Help (GitHub)", null, (s, e) => Process.Start(Repo));
            m.Items.Add("Check for updates", null, (s, e) => new Thread(() => CheckUpdates(true)) { IsBackground = true }.Start());
            m.Items.Add(new ToolStripMenuItem("TapMirror " + Diag.Version) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, (s, e) => { icon.Visible = false; Application.Exit(); });
        }

        static void CopyReport()
        {
            new Thread(() =>
            {
                string text = Diag.Collect();
                ui.Post(_ =>
                {
                    Clipboard.SetText(text);
                    updateBalloonShown = false;
                    icon.ShowBalloonTip(4000, "TapMirror", "Copied. Paste it into the bug report on GitHub.", ToolTipIcon.Info);
                }, null);
            }) { IsBackground = true }.Start();
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
