using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TapMirror
{
    /// <summary>One message of the UDP protocol: "TAPMIRROR/1 TYPE key=value ..." (see docs/protocol.md).</summary>
    class Msg
    {
        const string Magic = "TAPMIRROR/1";
        public string Type;
        public readonly Dictionary<string, string> Kv = new Dictionary<string, string>();
        public IPEndPoint From;
        public UdpClient Via;   // the socket it came in on; answers go out through it

        public string this[string key]
        {
            get { string v; return Kv.TryGetValue(key, out v) ? v : ""; }
        }

        public static Msg Parse(byte[] data, IPEndPoint from)
        {
            string[] parts = Encoding.UTF8.GetString(data).Trim().Split(' ');
            if (parts.Length < 2 || parts[0] != Magic) return null;
            var m = new Msg { Type = parts[1], From = from };
            for (int i = 2; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                // the phone uses Java's URLEncoder: '+' is a space
                try { m.Kv[parts[i].Substring(0, eq)] = Uri.UnescapeDataString(parts[i].Substring(eq + 1).Replace('+', ' ')); }
                catch (UriFormatException) { }
            }
            return m;
        }

        public static byte[] Build(string type, params string[] kv)
        {
            var sb = new StringBuilder(Magic).Append(' ').Append(type);
            for (int i = 0; i + 1 < kv.Length; i += 2)
                sb.Append(' ').Append(kv[i]).Append('=').Append(Uri.EscapeDataString(kv[i + 1] ?? ""));
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }

    class Session
    {
        public string PhoneId, Tap, Name;
        public IPEndPoint Phone;
        public volatile UdpClient Via;
        public int AdbPort;
        public Msg Request;
        public volatile Process Scrcpy;
        public volatile bool Running;   // scrcpy window is up
        public volatile bool Replaced;  // a newer request from the same phone took over
        public volatile bool StopRequested;
    }

    /// <summary>Listens for phones, asks the user to allow new ones, and runs scrcpy sessions.</summary>
    static class Server
    {
        public const int Port = 27199;

        // One socket for the whole port (broadcasts arrive there) plus one per local address:
        // a unicast answer must come from the address the phone asked, or a VPN on the phone
        // drops it. A PC with two addresses in one network (Ethernet and Wi-Fi) otherwise
        // answers from whichever one Windows routes through.
        static UdpClient anySocket;
        static readonly Dictionary<IPAddress, UdpClient> addrSockets = new Dictionary<IPAddress, UdpClient>();
        static readonly object socketLock = new object();
        static SynchronizationContext ui;
        // UI thread only
        static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        static readonly Dictionary<string, Msg> pendingApproval = new Dictionary<string, Msg>();

        public static event Action Changed;

        [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);

        public static void Start(SynchronizationContext uiContext)
        {
            ui = uiContext;
            anySocket = Open(IPAddress.Any);
            UpdateAddressSockets();
            NetworkChange.NetworkAddressChanged += (s, e) => UpdateAddressSockets();
            Log.Write("listening on UDP " + Port);
        }

        static UdpClient Open(IPAddress addr)
        {
            var sock = new UdpClient(new IPEndPoint(addr, Port));
            // Child processes inherit handles: without this the adb server that scrcpy starts
            // keeps the port open after TapMirror exits.
            SetHandleInformation(sock.Client.Handle, 1 /* HANDLE_FLAG_INHERIT */, 0);
            new Thread(() => Listen(sock)) { IsBackground = true, Name = "udp " + addr }.Start();
            return sock;
        }

        /// <summary>IPv4 addresses of the active physical adapters, for showing to the user.</summary>
        public static string LanAddresses()
        {
            var list = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string d = ni.Description + " " + ni.Name;
                if (d.Contains("Virtual") || d.Contains("VMware") || d.Contains("vEthernet") || d.Contains("Hyper-V")) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
            }
            return string.Join(", ", list);
        }

        /// <summary>Opens sockets for new local IPv4 addresses and closes those of vanished ones.</summary>
        static void UpdateAddressSockets()
        {
            var current = new HashSet<IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) current.Add(ua.Address);
            }
            lock (socketLock)
            {
                foreach (var addr in new List<IPAddress>(addrSockets.Keys))
                {
                    if (current.Contains(addr)) continue;
                    addrSockets[addr].Close();
                    addrSockets.Remove(addr);
                }
                foreach (var addr in current)
                {
                    if (addrSockets.ContainsKey(addr)) continue;
                    try { addrSockets[addr] = Open(addr); }
                    catch (SocketException e) { Log.Write("can't listen on " + addr + ": " + e.Message); }
                }
            }
        }

        public static string StatusText()
        {
            foreach (var s in sessions.Values)
                return (s.Running ? "Mirroring " : "Connecting ") + s.Name;
            return "Waiting for a phone";
        }

        static void Send(UdpClient via, IPEndPoint to, string type, params string[] kv)
        {
            byte[] data = Msg.Build(type, kv);
            try { via.Send(data, data.Length, to); } catch (Exception) { }
        }

        static void Listen(UdpClient sock)
        {
            while (true)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try { data = sock.Receive(ref from); }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { return; }
                Msg m = Msg.Parse(data, from);
                if (m == null) continue;
                m.Via = sock;
                if (m.Type == "HELLO")
                {
                    Log.Write("HELLO from " + from + " (" + m["name"] + ")");
                    Send(sock, from, "HI", "pc", Config.PcId, "name", Environment.MachineName);
                }
                else
                    ui.Post(_ => Handle(m), null);
            }
        }

        static Session ByTap(string tap)
        {
            foreach (var s in sessions.Values) if (s.Tap == tap) return s;
            return null;
        }

        static void Handle(Msg m)
        {
            switch (m.Type)
            {
                case "START": OnStart(m); break;
                case "PING":
                    {
                        Session s = ByTap(m["tap"]);
                        Send(m.Via, m.From, "PONG", "tap", m["tap"], "state", s == null ? "none" : s.Running ? "running" : "connecting");
                        break;
                    }
                case "STOP":
                    {
                        Session s = ByTap(m["tap"]);
                        if (s != null) { s.StopRequested = true; Close(s); }
                        // gave up while the approval question is open: the answer is still saved, but nothing starts
                        string waiting = null;
                        foreach (var kv in pendingApproval) if (kv.Value["tap"] == m["tap"]) waiting = kv.Key;
                        if (waiting != null) pendingApproval.Remove(waiting);
                        break;
                    }
            }
        }

        static void OnStart(Msg m)
        {
            string id = m["id"], tap = m["tap"];
            if (id == "" || tap == "") return;
            if (Config.Blocked.ContainsKey(id)) { Send(m.Via, m.From, "DENY", "tap", tap); return; }

            Session existing;
            if (sessions.TryGetValue(id, out existing) && existing.Tap == tap)
            {
                // a repeat of a request we already work on
                existing.Phone = m.From;
                existing.Via = m.Via;
                Send(m.Via, m.From, existing.Running ? "RUNNING" : "ACCEPT", "tap", tap);
                return;
            }

            if (!Config.Allowed.ContainsKey(id))
            {
                bool asking = pendingApproval.ContainsKey(id);
                pendingApproval[id] = m; // keep the newest request
                Send(m.Via, m.From, "WAIT", "tap", tap);
                if (!asking) AskApproval(id, m["name"], m.From.Address.ToString());
                return;
            }
            StartSession(m);
        }

        static void AskApproval(string id, string name, string ip)
        {
            Log.Write("asking to allow " + name + " (" + ip + ")");
            // its own thread, so the tray keeps answering the phone while the question is open
            var t = new Thread(() =>
            {
                var r = MessageBox.Show(
                    "The phone \"" + name + "\" (" + ip + ") wants to mirror its screen to this PC.\n\n" +
                    "Allow it? It won't be asked again.\n\n" +
                    "Yes: allow.   No: block this phone (you can unblock it in the tray menu).",
                    "TapMirror", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                ui.Post(_ => Approved(id, name, r == DialogResult.Yes), null);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static void Approved(string id, string name, bool yes)
        {
            Msg m;
            pendingApproval.TryGetValue(id, out m);
            pendingApproval.Remove(id);
            if (yes) { Config.Allowed[id] = name; Config.Blocked.Remove(id); }
            else { Config.Blocked[id] = name; Config.Allowed.Remove(id); }
            Config.Save();
            Log.Write((yes ? "allowed " : "blocked ") + name);
            if (m == null) return;
            if (yes) StartSession(m);
            else Send(m.Via, m.From, "DENY", "tap", m["tap"]);
        }

        static void StartSession(Msg m)
        {
            Session old;
            if (sessions.TryGetValue(m["id"], out old))
            {
                // the phone restarted adb for its new request, so the old session is dead or dying
                old.Replaced = true;
                Close(old);
            }
            int adbPort;
            int.TryParse(m["adb"], out adbPort);
            var s = new Session
            {
                PhoneId = m["id"], Tap = m["tap"], Name = m["name"] == "" ? "Phone" : m["name"],
                Phone = m.From, Via = m.Via, AdbPort = adbPort, Request = m,
            };
            sessions[s.PhoneId] = s;
            Send(s.Via, s.Phone, "ACCEPT", "tap", s.Tap);
            OnChanged();
            new Thread(() => Run(s)) { IsBackground = true, Name = "session" }.Start();
        }

        static void Close(Session s)
        {
            Process p = s.Scrcpy;
            if (p == null) return;
            try
            {
                if (!p.HasExited && !p.CloseMainWindow()) p.Kill();
            }
            catch { }
        }

        static void OnChanged()
        {
            var h = Changed;
            if (h != null) h();
        }

        // ---------- session thread ----------

        static void Run(Session s)
        {
            string ip = s.Phone.Address.ToString();
            string serial = null;
            string error = null;
            bool serverWasRunning = Tools.AdbServerRunning();
            Log.Write("session for " + s.Name + " " + ip + " adb port " + s.AdbPort);
            try
            {
                if (Tools.ScrcpyDir() == null) throw new Exception("scrcpy isn't installed on the PC (tray menu → scrcpy).");
                int port = s.AdbPort;
                if (port == 0)
                {
                    port = Tools.FindTlsPort(ip, 10000);
                    if (port == 0) throw new Exception("Wireless debugging didn't start on the phone. Open Developer options → " +
                        "Wireless debugging once on this Wi-Fi and allow it (\"Always allow on this network\").");
                }
                serial = ip + ":" + port;
                string output;
                if (s.Replaced || s.StopRequested) return;
                if (!Tools.Connect(serial, out output)) throw new Exception("adb couldn't connect: " + output);
                if (s.Replaced || s.StopRequested) return;
                error = RunScrcpy(s, serial);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                if (!s.Replaced)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Send(s.Via, s.Phone, "ENDED", "tap", s.Tap, "reason", error == null ? "closed" : "error", "msg", error ?? "");
                        Thread.Sleep(100);
                    }
                }
                if (error != null)
                {
                    Log.Write("session error: " + error);
                    Tray.Balloon("Mirroring " + s.Name + " failed: " + error, ToolTipIcon.Error);
                }
                if (serial != null) Tools.Adb("disconnect " + serial, 3000);
                Tools.StopAdbServerIfOurs(serverWasRunning);
                ui.Post(_ =>
                {
                    Session cur;
                    if (sessions.TryGetValue(s.PhoneId, out cur) && cur == s) sessions.Remove(s.PhoneId);
                    OnChanged();
                }, null);
            }
        }

        /// <summary>Runs scrcpy until it exits. Returns null on a normal close, or an error text.</summary>
        static string RunScrcpy(Session s, string serial)
        {
            string args = "-s " + serial + " " + PhoneOptions(s.Request, s.Name);
            var tail = new Queue<string>();
            Process p = StartScrcpy(args, tail);
            s.Scrcpy = p;
            var started = DateTime.Now;

            // A window opened by a background app lands behind the active one, so pull it to the front.
            // No window within 25 s means scrcpy is stuck (the phone went away while it was starting).
            IntPtr h = IntPtr.Zero;
            while (h == IntPtr.Zero && !p.WaitForExit(200) && (DateTime.Now - started).TotalSeconds < 25)
                h = Tools.WindowOf(p);
            if (h != IntPtr.Zero)
            {
                s.Running = true;
                Send(s.Via, s.Phone, "RUNNING", "tap", s.Tap);
                ui.Post(_ => { Tools.BringToFront(h); OnChanged(); }, null);
            }
            else if (!p.HasExited)
            {
                try { p.Kill(); } catch { }
            }
            p.WaitForExit();
            string output;
            lock (tail) output = string.Join("\n", tail.ToArray());
            Log.Write("scrcpy " + args + " exited " + p.ExitCode + "\n" + output);
            if (s.StopRequested || s.Replaced) return null;
            if (h == IntPtr.Zero) return LastError(output, "scrcpy didn't open a window");
            // closed by the user, or the phone stopped (that also ends scrcpy): both are normal ends
            return null;
        }

        static string LastError(string output, string fallback)
        {
            string[] lines = output.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (lines[i].StartsWith("ERROR:")) return lines[i].Substring(6).Trim();
            return fallback;
        }

        public static void StartUsb()
        {
            if (sessions.ContainsKey("usb")) return;
            var s = new Session { PhoneId = "usb", Name = "USB phone" };
            sessions["usb"] = s;
            OnChanged();
            new Thread(() =>
            {
                bool serverWasRunning = Tools.AdbServerRunning();
                try
                {
                    if (Tools.ScrcpyDir() == null) { Tray.Balloon("scrcpy isn't installed (tray menu → scrcpy).", ToolTipIcon.Warning); return; }
                    var tail = new Queue<string>();
                    Process p = StartScrcpy("-d", tail);
                    s.Scrcpy = p;
                    IntPtr h = IntPtr.Zero;
                    var started = DateTime.Now;
                    while (h == IntPtr.Zero && !p.WaitForExit(200) && (DateTime.Now - started).TotalSeconds < 25)
                        h = Tools.WindowOf(p);
                    if (h != IntPtr.Zero) { s.Running = true; ui.Post(_ => { Tools.BringToFront(h); OnChanged(); }, null); }
                    p.WaitForExit();
                    string output;
                    lock (tail) output = string.Join("\n", tail.ToArray());
                    if (h == IntPtr.Zero) Tray.Balloon("USB mirroring failed: " + LastError(output, "no phone found over USB"), ToolTipIcon.Warning);
                    Tools.StopAdbServerIfOurs(serverWasRunning);
                }
                finally { ui.Post(_ => { sessions.Remove("usb"); OnChanged(); }, null); }
            }) { IsBackground = true }.Start();
        }

        static Process StartScrcpy(string args, Queue<string> tail)
        {
            string dir = Tools.ScrcpyDir();
            var psi = new ProcessStartInfo(Path.Combine(dir, "scrcpy.exe"), (args + " " + Config.ExtraArgs).Trim())
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.EnvironmentVariables["ADB"] = Tools.AdbPath();
            DataReceivedEventHandler collect = (o, e) =>
            {
                if (e.Data == null) return;
                lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 20) tail.Dequeue(); }
            };
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += collect;
            p.ErrorDataReceived += collect;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        /// <summary>Turns the phone's settings into scrcpy options. Only known options with checked values pass.</summary>
        static string PhoneOptions(Msg m, string name)
        {
            var a = new List<string>();
            a.Add("--window-title \"" + name.Replace("\"", "") + "\"");
            int v;
            if (int.TryParse(m["size"], out v) && v >= 320 && v <= 8192) a.Add("--max-size " + v);
            if (int.TryParse(m["bitrate"], out v) && v >= 1 && v <= 200) a.Add("--video-bit-rate " + v + "M");
            if (int.TryParse(m["fps"], out v) && v >= 1 && v <= 240) a.Add("--max-fps " + v);
            string codec = m["codec"];
            if (codec == "h264" || codec == "h265" || codec == "av1") a.Add("--video-codec " + codec);
            if (m["video"] == "0") a.Add("--no-video");
            if (m["audio"] == "0") a.Add("--no-audio");
            if (m["screenoff"] == "1") a.Add("--turn-screen-off");
            if (m["awake"] == "1") a.Add("--stay-awake");
            return string.Join(" ", a);
        }
    }
}
