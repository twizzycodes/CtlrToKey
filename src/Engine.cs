using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CtlrToKey
{
    public class Engine
    {
        public AppConfig Cfg;
        public bool Suspended;
        public bool Paused;
        public bool LockBlocked;
        public string UiDevice;
        public Action<string> Message;
        public Action ToggleRequested;

        class PState
        {
            public HashSet<string> Prev = new HashSet<string>();
            public HashSet<string> Spent = new HashSet<string>();
            public Dictionary<string, long> PressTime = new Dictionary<string, long>();
        }

        Dictionary<Profile, PState> states = new Dictionary<Profile, PState>();
        Dictionary<string, HashSet<string>> polled = new Dictionary<string, HashSet<string>>();
        Dictionary<string, long> retry = new Dictionary<string, long>();
        Stopwatch sw = Stopwatch.StartNew();
        volatile int macroGen;
        long comboSince = -1;
        bool comboLatched;

        public Engine(AppConfig cfg) { Cfg = cfg; }

        public HashSet<string> PressedFor(string dev)
        {
            HashSet<string> s;
            return (dev != null && polled.TryGetValue(dev, out s) && s != null) ? s : new HashSet<string>();
        }

        public bool IsConnected(string dev)
        {
            HashSet<string> s;
            return dev != null && polled.TryGetValue(dev, out s) && s != null;
        }

        public void ForceReconnect() { retry.Clear(); }

        HashSet<string> Poll(string dev, long now, Dictionary<string, HashSet<string>> cur)
        {
            if (string.IsNullOrEmpty(dev)) return null;
            if (cur.ContainsKey(dev)) return cur[dev];
            HashSet<string> res = null;
            long r;
            bool wasConnected = polled.ContainsKey(dev) && polled[dev] != null;
            if (wasConnected || !retry.TryGetValue(dev, out r) || now >= r)
            {
                res = Devices.GetPressed(dev, Cfg);
                if (res == null) retry[dev] = now + 1000;
            }
            cur[dev] = res;
            return res;
        }

        public void Tick()
        {
            long now = sw.ElapsedMilliseconds;
            Dictionary<string, HashSet<string>> cur = new Dictionary<string, HashSet<string>>();
            Poll(UiDevice, now, cur);
            foreach (Profile p in Cfg.Profiles)
                if (p.Enabled) Poll(p.Device, now, cur);
            polled = cur;

            CheckToggleCombo(cur, now);

            LockBlocked = !Suspended && !LockAllows();
            bool run = !Suspended && !Paused && !LockBlocked;

            foreach (Profile gone in states.Keys.Where(k => !Cfg.Profiles.Contains(k)).ToList())
            {
                Release(gone);
                states.Remove(gone);
            }

            foreach (Profile p in Cfg.Profiles)
            {
                PState st;
                if (!states.TryGetValue(p, out st)) { st = new PState(); states[p] = st; }
                HashSet<string> pressed = null;
                if (p.Enabled) cur.TryGetValue(p.Device, out pressed);
                if (pressed == null) pressed = new HashSet<string>();

                foreach (string b in pressed)
                    if (!st.Prev.Contains(b)) st.PressTime[b] = now;
                st.Spent.RemoveWhere(b => !pressed.Contains(b));
                st.Prev = pressed;

                if (!run || !p.Enabled)
                {
                    Release(p);
                    st.Spent.UnionWith(pressed);
                    continue;
                }
                RunProfile(p, st, pressed, now);
            }
        }

        void CheckToggleCombo(Dictionary<string, HashSet<string>> cur, long now)
        {
            List<string> combo = Cfg.ComboToggleEnabled ? Cfg.ToggleComboButtons() : null;
            if (combo == null || combo.Count == 0) { comboSince = -1; comboLatched = false; return; }
            bool down = cur.Values.Any(s => s != null && combo.All(s.Contains));
            if (!down) { comboSince = -1; comboLatched = false; return; }
            foreach (PState st in states.Values) st.Spent.UnionWith(combo);
            if (comboSince < 0) comboSince = now;
            if (!comboLatched && now - comboSince >= Cfg.ComboHoldMs)
            {
                comboLatched = true;
                if (ToggleRequested != null) ToggleRequested();
            }
        }

        void RunProfile(Profile p, PState st, HashSet<string> pressed, long now)
        {
            List<Mapping> maps = p.Mappings
                .Where(m => m.Buttons.Count > 0 && (m.Action != "None" || m.Long != null))
                .OrderByDescending(m => m.Buttons.Count).ToList();
            List<string> toggle = Cfg.ComboToggleEnabled ? Cfg.ToggleComboButtons() : new List<string>();

            foreach (Mapping m in maps)
            {
                bool sat = m.Buttons.All(pressed.Contains);

                if (m.Active)
                {
                    if (!sat)
                    {
                        if (m.Waiting) { m.Waiting = false; Fire(m, m.Main, now, true); }
                        Deactivate(m);
                    }
                    else if (m.Waiting)
                    {
                        if (now - m.PressStart >= Math.Max(50, p.LongPressMs))
                        {
                            m.Waiting = false;
                            Fire(m, m.Long, now, false);
                        }
                    }
                    else if (m.Cur != null && now >= m.NextRepeat)
                    {
                        List<Keys> ks = KeyUtil.Order(KeyUtil.Parse(m.Cur.Keys));
                        if (ks.Count > 0) KeySender.Tap(ks, Cfg.ScanCodes);
                        m.NextRepeat = now + Math.Max(10, Cfg.RepeatInterval);
                    }
                    continue;
                }

                if (!sat)
                {
                    if (m.Pending) { m.Pending = false; Fire(m, m.Main, now, true); }
                    continue;
                }

                if (m.Buttons.Any(st.Spent.Contains)) { m.Pending = false; continue; }

                bool partOfBigger = maps.Any(o => o != m && o.Buttons.Count > m.Buttons.Count && m.Buttons.All(o.Buttons.Contains))
                    || (toggle.Count > m.Buttons.Count && m.Buttons.All(toggle.Contains));
                if (partOfBigger && Cfg.ComboWindow > 0)
                {
                    long newest = m.Buttons.Max(b => st.PressTime.ContainsKey(b) ? st.PressTime[b] : 0);
                    if (now - newest < Cfg.ComboWindow) { m.Pending = true; continue; }
                }

                m.Pending = false;
                m.Active = true;
                if (m.Long != null) { m.Waiting = true; m.PressStart = now; }
                else Fire(m, m.Main, now, false);
                st.Spent.UnionWith(m.Buttons);
                foreach (Mapping o in maps)
                    if (o != m && o.Pending && o.Buttons.Any(m.Buttons.Contains)) o.Pending = false;
            }
        }

        void Fire(Mapping m, ActionDef d, long now, bool tapOnly)
        {
            if (d == null) return;
            if (d.Action == "Run") { RunProgramAndReport(d.RunPath, d.RunArgs); return; }
            if (d.Action == "Macro") { StartMacro(m, d.Macro); return; }
            if (d.Action != "Keys") return;
            List<Keys> ks = KeyUtil.Order(KeyUtil.Parse(d.Keys));
            if (ks.Count == 0) return;
            if (!tapOnly && d.Mode == "Hold")
            {
                KeySender.Down(ks, Cfg.ScanCodes);
                m.HeldKeys = ks;
            }
            else
            {
                KeySender.Tap(ks, Cfg.ScanCodes);
                if (!tapOnly && d.Mode == "Repeat")
                {
                    m.Cur = d;
                    m.NextRepeat = now + Math.Max(0, Cfg.RepeatDelay);
                }
            }
        }

        void Deactivate(Mapping m)
        {
            m.Active = false;
            m.Waiting = false;
            m.Cur = null;
            if (m.HeldKeys != null)
            {
                KeySender.Up(m.HeldKeys, Cfg.ScanCodes);
                m.HeldKeys = null;
            }
        }

        void Release(Profile p)
        {
            foreach (Mapping m in p.Mappings)
            {
                if (m.Active) Deactivate(m);
                m.Pending = false;
            }
        }

        public void ReleaseAll()
        {
            foreach (Profile p in states.Keys.Union(Cfg.Profiles).ToList()) Release(p);
        }

        public void ResetProfile(Profile p)
        {
            Release(p);
            states.Remove(p);
        }

        public void CancelMacros() { macroGen++; }

        void StartMacro(Mapping m, string text)
        {
            if (Interlocked.CompareExchange(ref m.MacroBusy, 1, 0) != 0) return;
            int gen = macroGen;
            bool scan = Cfg.ScanCodes;
            Thread t = new Thread(() =>
            {
                try { MacroRunner.Run(text, scan, () => gen != macroGen, Say); }
                catch (Exception ex) { Say("Macro error: " + ex.Message); }
                finally { m.MacroBusy = 0; }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static string RunProgram(string runPath, string runArgs)
        {
            if (string.IsNullOrEmpty(runPath)) return null;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(runPath.Trim().Trim('"'));
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.Arguments = runArgs ?? "";
                psi.UseShellExecute = true;
                try { if (File.Exists(path)) psi.WorkingDirectory = Path.GetDirectoryName(path); } catch (Exception) { }
                Process.Start(psi);
                return null;
            }
            catch (Exception ex)
            {
                return "Could not run \"" + runPath + "\": " + ex.Message;
            }
        }

        void RunProgramAndReport(string path, string args)
        {
            string err = RunProgram(path, args);
            Say(err ?? "Launched " + path);
        }

        void Say(string s)
        {
            Action<string> h = Message;
            if (h != null) h(s);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        IntPtr lastFg = IntPtr.Zero;
        string lastFgName = "";
        string lockText;
        List<string> lockNames = new List<string>();

        public static List<string> ParsePrograms(string s)
        {
            return (s ?? "").Split(',', ';')
                .Select(x => x.Trim().Trim('"'))
                .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.Length - 4) : x)
                .Select(x => Path.GetFileName(x))
                .Where(x => x.Length > 0).ToList();
        }

        public string ForegroundProgram { get { return lastFgName; } }

        bool LockAllows()
        {
            if (!Cfg.LockEnabled) return true;
            if (lockText != Cfg.LockPrograms) { lockText = Cfg.LockPrograms; lockNames = ParsePrograms(lockText); }
            if (lockNames.Count == 0) return true;
            IntPtr h = GetForegroundWindow();
            if (h != lastFg)
            {
                lastFg = h;
                lastFgName = "";
                try
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != 0) lastFgName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch (Exception) { }
            }
            return lockNames.Any(n => string.Equals(n, lastFgName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class MacroRunner
    {
        public static int CountSteps(string text)
        {
            return Lines(text).Count();
        }

        static IEnumerable<string> Lines(string text)
        {
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string l = raw.TrimEnd('\r').Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                yield return l;
            }
        }

        public static void Run(string text, bool scan, Func<bool> cancelled, Action<string> say)
        {
            foreach (string line in Lines(text))
            {
                if (cancelled()) return;
                int sp = line.IndexOf(' ');
                string cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
                string arg = sp < 0 ? "" : line.Substring(sp + 1);
                switch (cmd)
                {
                    case "key":
                    case "down":
                    case "up":
                        {
                            List<Keys> ks = KeyUtil.ParseFriendly(arg);
                            if (ks.Count == 0) { say("Macro: unknown key \"" + arg.Trim() + "\""); break; }
                            ks = KeyUtil.Order(ks);
                            if (cmd == "key") KeySender.Tap(ks, scan);
                            else if (cmd == "down") KeySender.Down(ks, scan);
                            else KeySender.Up(ks, scan);
                            break;
                        }
                    case "wait":
                        {
                            int ms;
                            if (!int.TryParse(arg.Trim().Replace("ms", ""), out ms)) { say("Macro: bad wait \"" + arg + "\""); break; }
                            long end = Environment.TickCount + Math.Min(ms, 600000);
                            while (Environment.TickCount < end)
                            {
                                if (cancelled()) return;
                                Thread.Sleep(Math.Max(1, Math.Min(50, (int)(end - Environment.TickCount))));
                            }
                            break;
                        }
                    case "type":
                        KeySender.TypeText(arg);
                        break;
                    case "run":
                        {
                            string err = Engine.RunProgram(arg.Trim(), "");
                            if (err != null) say(err);
                            break;
                        }
                    default:
                        say("Macro: unknown step \"" + line + "\"");
                        break;
                }
                Thread.Sleep(25);
            }
        }
    }
}
