using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CtlrToKey
{
    static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Gamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct State
        {
            public uint dwPacketNumber;
            public Gamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint Get14(uint idx, out State s);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint Get910(uint idx, out State s);

        static int lib = 1;

        public static bool GetState(int idx, out State s)
        {
            s = new State();
            if (lib == 1)
            {
                try { return Get14((uint)idx, out s) == 0; } catch (Exception) { lib = 2; }
            }
            if (lib == 2)
            {
                try { return Get910((uint)idx, out s) == 0; } catch (Exception) { lib = 0; }
            }
            return false;
        }
    }

    static class WinMM
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos,
                dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
                wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")]
        public static extern uint joyGetPosEx(uint id, ref JOYINFOEX ji);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        public static extern uint joyGetDevCaps(UIntPtr id, ref JOYCAPS caps, uint cb);

        [DllImport("winmm.dll")]
        public static extern uint joyGetNumDevs();
    }

    public class DeviceInfo
    {
        public string Key;
        public string Name;
        public bool Connected;
        public override string ToString() { return Connected ? Name : Name + "   (not connected)"; }
    }

    public static class Devices
    {
        public static readonly string[] XButtons = {
            "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Back", "Start", "LS Click", "RS Click",
            "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right",
            "LStick Up", "LStick Down", "LStick Left", "LStick Right",
            "RStick Up", "RStick Down", "RStick Left", "RStick Right" };

        static Dictionary<int, WinMM.JOYCAPS> caps = new Dictionary<int, WinMM.JOYCAPS>();

        public static void Parse(string key, out string kind, out int idx)
        {
            kind = "XInput"; idx = 0;
            if (string.IsNullOrEmpty(key)) return;
            string[] p = key.Split(':');
            kind = p[0];
            if (p.Length > 1) int.TryParse(p[1], out idx);
        }

        static bool TryCaps(int id, out WinMM.JOYCAPS c)
        {
            if (caps.TryGetValue(id, out c)) return true;
            c = new WinMM.JOYCAPS();
            if (WinMM.joyGetDevCaps((UIntPtr)(uint)id, ref c, (uint)Marshal.SizeOf(typeof(WinMM.JOYCAPS))) != 0)
                return false;
            caps[id] = c;
            return true;
        }

        static bool PollJoy(int id, out WinMM.JOYINFOEX ji)
        {
            ji = new WinMM.JOYINFOEX();
            ji.dwSize = (uint)Marshal.SizeOf(typeof(WinMM.JOYINFOEX));
            ji.dwFlags = 0xFF | 0x200;
            return WinMM.joyGetPosEx((uint)id, ref ji) == 0;
        }

        public static List<DeviceInfo> List()
        {
            List<DeviceInfo> list = new List<DeviceInfo>();
            for (int i = 0; i < 4; i++)
            {
                XInput.State s;
                bool ok = XInput.GetState(i, out s);
                DeviceInfo d = new DeviceInfo();
                d.Key = "XInput:" + i;
                d.Name = "Xbox Controller " + (i + 1) + " (XInput)";
                d.Connected = ok;
                list.Add(d);
            }
            caps.Clear();
            int n = 0;
            try { n = (int)Math.Min(WinMM.joyGetNumDevs(), 16u); } catch (Exception) { }
            for (int i = 0; i < n; i++)
            {
                WinMM.JOYINFOEX ji;
                if (!PollJoy(i, out ji)) continue;
                WinMM.JOYCAPS c;
                string nm = (TryCaps(i, out c) && !string.IsNullOrEmpty(c.szPname)) ? c.szPname.Trim() : "Game Controller";
                DeviceInfo d = new DeviceInfo();
                d.Key = "WinMM:" + i;
                d.Name = "Joystick " + (i + 1) + ": " + nm + " (DirectInput)";
                d.Connected = true;
                list.Add(d);
            }
            return list;
        }

        public static string DisplayName(string key)
        {
            string kind; int idx;
            Parse(key, out kind, out idx);
            if (kind == "XInput") return "Xbox Controller " + (idx + 1);
            WinMM.JOYCAPS c;
            if (TryCaps(idx, out c) && !string.IsNullOrEmpty(c.szPname))
                return "Joystick " + (idx + 1) + ": " + c.szPname.Trim();
            return "Joystick " + (idx + 1);
        }

        public static string[] ButtonNames(string key)
        {
            string kind; int idx;
            Parse(key, out kind, out idx);
            if (kind == "XInput") return XButtons;
            List<string> l = new List<string> {
                "Stick1 Left", "Stick1 Right", "Stick1 Up", "Stick1 Down",
                "Stick2 Left", "Stick2 Right", "Stick2 Up", "Stick2 Down",
                "POV Up", "POV Right", "POV Down", "POV Left" };
            WinMM.JOYCAPS c;
            int nb = (TryCaps(idx, out c) && c.wNumButtons > 0) ? (int)c.wNumButtons : 16;
            nb = Math.Min(Math.Max(nb, 4), 32);
            for (int i = 1; i <= nb; i++) l.Add("Button " + i);
            return l.ToArray();
        }

        static double Norm(uint v, uint min, uint max)
        {
            if (max <= min) { min = 0; max = 65535; }
            return ((double)v - min) / (max - min) * 2.0 - 1.0;
        }

        public static HashSet<string> GetPressed(string key, AppConfig cfg)
        {
            string kind; int idx;
            Parse(key, out kind, out idx);
            HashSet<string> set = new HashSet<string>();

            if (kind == "XInput")
            {
                XInput.State s;
                if (!XInput.GetState(idx, out s)) return null;
                XInput.Gamepad g = s.Gamepad;
                int b = g.wButtons;
                if ((b & 0x1000) != 0) set.Add("A");
                if ((b & 0x2000) != 0) set.Add("B");
                if ((b & 0x4000) != 0) set.Add("X");
                if ((b & 0x8000) != 0) set.Add("Y");
                if ((b & 0x0100) != 0) set.Add("LB");
                if ((b & 0x0200) != 0) set.Add("RB");
                if ((b & 0x0020) != 0) set.Add("Back");
                if ((b & 0x0010) != 0) set.Add("Start");
                if ((b & 0x0040) != 0) set.Add("LS Click");
                if ((b & 0x0080) != 0) set.Add("RS Click");
                if ((b & 0x0001) != 0) set.Add("D-Pad Up");
                if ((b & 0x0002) != 0) set.Add("D-Pad Down");
                if ((b & 0x0004) != 0) set.Add("D-Pad Left");
                if ((b & 0x0008) != 0) set.Add("D-Pad Right");
                if (g.bLeftTrigger > cfg.TriggerThreshold) set.Add("LT");
                if (g.bRightTrigger > cfg.TriggerThreshold) set.Add("RT");
                double dz = cfg.Deadzone / 100.0 * 32767;
                if (g.sThumbLX < -dz) set.Add("LStick Left");
                if (g.sThumbLX > dz) set.Add("LStick Right");
                if (g.sThumbLY > dz) set.Add("LStick Up");
                if (g.sThumbLY < -dz) set.Add("LStick Down");
                if (g.sThumbRX < -dz) set.Add("RStick Left");
                if (g.sThumbRX > dz) set.Add("RStick Right");
                if (g.sThumbRY > dz) set.Add("RStick Up");
                if (g.sThumbRY < -dz) set.Add("RStick Down");
                return set;
            }

            WinMM.JOYINFOEX ji;
            if (!PollJoy(idx, out ji)) return null;
            WinMM.JOYCAPS c;
            bool hasCaps = TryCaps(idx, out c);
            double t = cfg.Deadzone / 100.0;
            double x = hasCaps ? Norm(ji.dwXpos, c.wXmin, c.wXmax) : Norm(ji.dwXpos, 0, 65535);
            double y = hasCaps ? Norm(ji.dwYpos, c.wYmin, c.wYmax) : Norm(ji.dwYpos, 0, 65535);
            if (x < -t) set.Add("Stick1 Left");
            if (x > t) set.Add("Stick1 Right");
            if (y < -t) set.Add("Stick1 Up");
            if (y > t) set.Add("Stick1 Down");
            if (hasCaps && (c.wCaps & 0x1) != 0)
            {
                double z = Norm(ji.dwZpos, c.wZmin, c.wZmax);
                if (z < -t) set.Add("Stick2 Left");
                if (z > t) set.Add("Stick2 Right");
            }
            if (hasCaps && (c.wCaps & 0x2) != 0)
            {
                double r = Norm(ji.dwRpos, c.wRmin, c.wRmax);
                if (r < -t) set.Add("Stick2 Up");
                if (r > t) set.Add("Stick2 Down");
            }
            uint pov = ji.dwPOV & 0xFFFF;
            if (pov != 0xFFFF && pov <= 36000)
            {
                if (pov > 27000 || pov < 9000) set.Add("POV Up");
                if (pov > 0 && pov < 18000) set.Add("POV Right");
                if (pov > 9000 && pov < 27000) set.Add("POV Down");
                if (pov > 18000 && pov < 36000) set.Add("POV Left");
            }
            for (int i = 0; i < 32; i++)
                if ((ji.dwButtons & (1u << i)) != 0) set.Add("Button " + (i + 1));
            return set;
        }
    }

    public static class KeyUtil
    {
        public static List<Keys> Parse(string s)
        {
            List<Keys> l = new List<Keys>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (string p in s.Split('+'))
            {
                Keys k;
                if (Enum.TryParse(p.Trim(), out k) && k != Keys.None && !l.Contains(k)) l.Add(k);
            }
            return l;
        }

        static Dictionary<string, Keys> nameMap;

        public static List<Keys> ParseFriendly(string s)
        {
            if (nameMap == null)
            {
                Dictionary<string, Keys> d = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);
                foreach (Keys k in Enum.GetValues(typeof(Keys)))
                {
                    if ((k & Keys.KeyCode) != k || k == Keys.None) continue;
                    Keys n = Normalize(k);
                    string pretty = Name(n);
                    if (!d.ContainsKey(pretty)) d[pretty] = n;
                    if (!d.ContainsKey(k.ToString())) d[k.ToString()] = n;
                }
                string[,] alias = { { "control", "ControlKey" }, { "windows", "LWin" }, { "escape", "Escape" }, { "del", "Delete" },
                    { "return", "Return" }, { "pgup", "Prior" }, { "pgdn", "Next" }, { "prtsc", "Snapshot" }, { "plus", "Oemplus" } };
                for (int i = 0; i < alias.GetLength(0); i++)
                    d[alias[i, 0]] = (Keys)Enum.Parse(typeof(Keys), alias[i, 1]);
                nameMap = d;
            }
            List<Keys> l = new List<Keys>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (string p in s.Split('+'))
            {
                string t = p.Trim();
                if (t.Length == 0) continue;
                Keys k;
                if (!nameMap.TryGetValue(t, out k)) return new List<Keys>();
                if (!l.Contains(k)) l.Add(k);
            }
            return l;
        }

        public static List<Keys> SpecialKeys()
        {
            List<Keys> l = new List<Keys> {
                Keys.LWin, Keys.ControlKey, Keys.ShiftKey, Keys.Menu, Keys.Tab, Keys.Escape, Keys.Return,
                Keys.Space, Keys.Back, Keys.Delete, Keys.Snapshot, Keys.Apps,
                Keys.VolumeUp, Keys.VolumeDown, Keys.VolumeMute,
                Keys.MediaPlayPause, Keys.MediaNextTrack, Keys.MediaPreviousTrack, Keys.MediaStop,
                Keys.BrowserBack, Keys.BrowserForward, Keys.BrowserHome, Keys.BrowserRefresh, Keys.LaunchMail, Keys.LaunchApplication2,
                Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.Prior, Keys.Next, Keys.Insert,
                Keys.Pause, Keys.Capital, Keys.NumLock, Keys.Scroll };
            for (Keys k = Keys.F1; k <= Keys.F24; k++) l.Add(k);
            return l;
        }

        public static string Join(IEnumerable<Keys> ks)
        {
            return string.Join("+", ks.Select(k => k.ToString()).ToArray());
        }

        public static Keys Normalize(Keys k)
        {
            switch (k)
            {
                case Keys.LControlKey: case Keys.RControlKey: return Keys.ControlKey;
                case Keys.LShiftKey: case Keys.RShiftKey: return Keys.ShiftKey;
                case Keys.LMenu: case Keys.RMenu: return Keys.Menu;
                case Keys.RWin: return Keys.LWin;
            }
            return k;
        }

        static int Rank(Keys k)
        {
            switch (Normalize(k))
            {
                case Keys.LWin: return 0;
                case Keys.ControlKey: return 1;
                case Keys.Menu: return 2;
                case Keys.ShiftKey: return 3;
            }
            return 9;
        }

        public static List<Keys> Order(List<Keys> l)
        {
            return l.Select((k, i) => new { k, i }).OrderBy(a => Rank(a.k)).ThenBy(a => a.i).Select(a => a.k).ToList();
        }

        public static string Name(Keys k)
        {
            switch (k)
            {
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Shift";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt";
                case Keys.LWin: case Keys.RWin: return "Win";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Back: return "Backspace";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Capital: return "CapsLock";
                case Keys.Snapshot: return "PrintScreen";
                case Keys.Scroll: return "ScrollLock";
                case Keys.Apps: return "Menu";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                case Keys.VolumeUp: return "Volume Up";
                case Keys.VolumeDown: return "Volume Down";
                case Keys.VolumeMute: return "Mute";
                case Keys.MediaPlayPause: return "Play/Pause";
                case Keys.MediaNextTrack: return "Next Track";
                case Keys.MediaPreviousTrack: return "Previous Track";
                case Keys.MediaStop: return "Media Stop";
                case Keys.BrowserBack: return "Browser Back";
                case Keys.BrowserForward: return "Browser Forward";
                case Keys.BrowserHome: return "Browser Home";
                case Keys.BrowserRefresh: return "Browser Refresh";
                case Keys.BrowserSearch: return "Browser Search";
                case Keys.LaunchMail: return "Launch Mail";
                case Keys.LaunchApplication2: return "Launch Calculator";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Multiply: return "Num *";
                case Keys.Divide: return "Num /";
                case Keys.Decimal: return "Num .";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
            return k.ToString();
        }

        public static string Pretty(List<Keys> l)
        {
            return string.Join(" + ", Order(l).Select(k => Name(k)).ToArray());
        }

        public static string Pretty(string s)
        {
            return Pretty(Parse(s));
        }
    }

    public static class KeySender
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);

        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End:
                case Keys.Prior: case Keys.Next: case Keys.Left: case Keys.Right:
                case Keys.Up: case Keys.Down: case Keys.NumLock: case Keys.Divide:
                case Keys.RControlKey: case Keys.RMenu: case Keys.LWin: case Keys.RWin:
                case Keys.Apps: case Keys.Snapshot:
                    return true;
            }
            return false;
        }

        static INPUT Make(Keys k, bool up, bool scan)
        {
            ushort vk = (ushort)k;
            ushort sc = (ushort)MapVirtualKey(vk, 0);
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (IsExtended(k)) flags |= KEYEVENTF_EXTENDEDKEY;
            bool media = vk >= 0xA6 && vk <= 0xB7;
            INPUT inp = new INPUT();
            inp.type = 1;
            if (scan && sc != 0 && !media)
            {
                inp.U.ki.wVk = 0;
                inp.U.ki.wScan = sc;
                flags |= KEYEVENTF_SCANCODE;
            }
            else
            {
                inp.U.ki.wVk = vk;
                inp.U.ki.wScan = sc;
            }
            inp.U.ki.dwFlags = flags;
            return inp;
        }

        static void Send(List<INPUT> l)
        {
            if (l.Count == 0) return;
            SendInput((uint)l.Count, l.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void Down(List<Keys> ks, bool scan)
        {
            Send(ks.Select(k => Make(k, false, scan)).ToList());
        }

        public static void Up(List<Keys> ks, bool scan)
        {
            List<Keys> r = new List<Keys>(ks); r.Reverse();
            Send(r.Select(k => Make(k, true, scan)).ToList());
        }

        public static void TypeText(string text)
        {
            foreach (char c in text ?? "")
            {
                if (c == '\r') continue;
                if (c == '\n') { Tap(new List<Keys> { Keys.Return }, false); continue; }
                INPUT d = new INPUT(); d.type = 1; d.U.ki.wScan = c; d.U.ki.dwFlags = 4;
                INPUT u = new INPUT(); u.type = 1; u.U.ki.wScan = c; u.U.ki.dwFlags = 4 | KEYEVENTF_KEYUP;
                Send(new List<INPUT> { d, u });
                System.Threading.Thread.Sleep(2);
            }
        }

        public static void Tap(List<Keys> ks, bool scan)
        {
            List<INPUT> l = ks.Select(k => Make(k, false, scan)).ToList();
            List<Keys> r = new List<Keys>(ks); r.Reverse();
            l.AddRange(r.Select(k => Make(k, true, scan)));
            Send(l);
        }
    }
}
