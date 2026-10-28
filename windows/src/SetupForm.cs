using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TapMirror
{
    /// <summary>
    /// No-root setup: grants the phone app WRITE_SECURE_SETTINGS (once), over USB or Wireless debugging pairing.
    /// With it the app can switch Wireless debugging on and off by itself.
    /// </summary>
    class SetupForm : Form
    {
        static SetupForm open;

        readonly TextBox logBox, addrBox, codeBox;
        readonly Button usbButton, pairButton;

        public static void ShowOnce()
        {
            if (open != null) { open.Activate(); return; }
            open = new SetupForm();
            open.FormClosed += (s, e) => open = null;
            open.Show();
        }

        SetupForm()
        {
            Text = "TapMirror: set up a phone without root";
            Icon = Tray.AppIcon(new Size(32, 32));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 600);

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(14),
                AutoScroll = true,
            };
            Controls.Add(layout);

            layout.Controls.Add(Text_(
                "Phones with root need nothing here. Without root, the TapMirror app needs one permission " +
                "(WRITE_SECURE_SETTINGS) to switch Wireless debugging on and off. Only a computer can grant it, " +
                "once. Install the TapMirror app on the phone first, and turn on Developer options " +
                "(Settings → About phone → tap Build number 7 times)."));

            layout.Controls.Add(Header("Option A: with a USB cable"));
            layout.Controls.Add(Text_(
                "1. On the phone: Developer options → turn on USB debugging.\n" +
                "2. Connect the cable and tap Allow on the phone.\n" +
                "3. Click the button."));
            usbButton = new Button { Text = "Grant over USB", AutoSize = true };
            usbButton.Click += (s, e) => Work(GrantUsb);
            layout.Controls.Add(usbButton);

            layout.Controls.Add(Header("Option B: without a cable"));
            layout.Controls.Add(Text_(
                "1. Phone and PC on the same Wi-Fi.\n" +
                "2. On the phone: Developer options → Wireless debugging → turn on → \"Pair device with pairing code\".\n" +
                "3. Type the IP address & port and the code the phone shows, then click the button."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(new Label { Text = "IP address & port", AutoSize = true, Anchor = AnchorStyles.Left });
            addrBox = new TextBox { Width = 170 };
            row.Controls.Add(addrBox);
            row.Controls.Add(new Label { Text = "Code", AutoSize = true, Anchor = AnchorStyles.Left });
            codeBox = new TextBox { Width = 90 };
            row.Controls.Add(codeBox);
            layout.Controls.Add(row);
            pairButton = new Button { Text = "Pair and grant", AutoSize = true };
            pairButton.Click += (s, e) => Work(Pair);
            layout.Controls.Add(pairButton);

            layout.Controls.Add(Header("Result"));
            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 520,
                Height = 130,
            };
            layout.Controls.Add(logBox);
        }

        static Label Header(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold),
                Margin = new Padding(0, 14, 0, 4),
            };
        }

        static Label Text_(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 2, 0, 6) };
        }

        void Say(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Say), line); return; }
            logBox.AppendText(line + Environment.NewLine);
        }

        void Work(Action job)
        {
            usbButton.Enabled = pairButton.Enabled = false;
            logBox.Clear();
            new Thread(() =>
            {
                try { job(); }
                catch (Exception e) { Say("Error: " + e.Message); }
                finally { BeginInvoke(new Action(() => usbButton.Enabled = pairButton.Enabled = true)); }
            }) { IsBackground = true }.Start();
        }

        void GrantUsb()
        {
            Say("Looking for a phone on USB…");
            string list = Tools.Adb("devices", 15000);
            var lines = list.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("\t")).ToList();
            // USB serials have no ':' (network ones are ip:port or mDNS names)
            var usb = lines.Where(l => !l.Split('\t')[0].Contains(":") && !l.Contains("._adb")).ToList();
            if (usb.Count == 0)
            {
                Say("No phone found on USB. Check the cable, USB debugging, and the Allow prompt on the phone.");
                return;
            }
            foreach (string l in usb)
            {
                string serial = l.Split('\t')[0], state = l.Split('\t')[1];
                if (state != "device")
                {
                    Say(serial + ": " + state + ". Unlock the phone and tap Allow on the USB debugging prompt, then try again.");
                    continue;
                }
                Grant(serial);
            }
        }

        void Pair()
        {
            string addr = (string)Invoke(new Func<string>(() => addrBox.Text.Trim()));
            string code = (string)Invoke(new Func<string>(() => codeBox.Text.Trim()));
            if (!Regex.IsMatch(addr, @"^[\d.]+:\d+$") || !Regex.IsMatch(code, @"^\d{6}$"))
            {
                Say("Type the address as shown on the phone (like 192.168.1.20:37099) and the 6-digit code.");
                return;
            }
            Say("Pairing with " + addr + "…");
            string o = Tools.Adb("pair " + addr + " " + code, 30000).Trim();
            Say(o);
            if (!o.Contains("Successfully paired"))
            {
                Say("Pairing failed. Make sure the pairing dialog is still open on the phone and try again.");
                return;
            }
            string ip = addr.Split(':')[0];
            Say("Looking for the phone's debugging port…");
            int port = Tools.FindTlsPort(ip, 15000);
            if (port == 0)
            {
                Say("Paired, but the phone wasn't found over mDNS. On the phone, look at Wireless debugging → " +
                    "\"IP address & Port\", then run:  adb connect <that address>  and  adb shell pm grant " +
                    Tools.PhonePackage + " android.permission.WRITE_SECURE_SETTINGS");
                return;
            }
            string serial = ip + ":" + port;
            string output;
            if (!Tools.Connect(serial, out output))
            {
                Say("Couldn't connect to " + serial + ": " + output);
                return;
            }
            Grant(serial);
            Tools.Adb("disconnect " + serial, 3000);
        }

        void Grant(string serial)
        {
            string o = Tools.Adb("-s " + serial + " shell pm grant " + Tools.PhonePackage +
                                 " android.permission.WRITE_SECURE_SETTINGS", 15000).Trim();
            if (o == "")
            {
                string model = Tools.Adb("-s " + serial + " shell getprop ro.product.model", 5000).Trim();
                Say("Done: " + model + " can now switch Wireless debugging by itself. You can unplug it. " +
                    "Open the TapMirror app on the phone to finish the checklist.");
            }
            else if (o.Contains("Unknown package") || o.Contains("not installed"))
            {
                Say(serial + ": the TapMirror app isn't installed on this phone yet. Install it and try again.");
            }
            else
            {
                Say(serial + ": " + o);
            }
        }
    }
}
