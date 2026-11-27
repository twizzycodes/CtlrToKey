using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CtlrToKey
{
    public class KeyCaptureBox : TextBox
    {
        public List<Keys> Combo = new List<Keys>();
        HashSet<Keys> down = new HashSet<Keys>();
        public event EventHandler ComboChanged;

        public KeyCaptureBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
            UpdateText();
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        public void SetKeys(List<Keys> ks) { Combo = new List<Keys>(ks); UpdateText(); }

        public void AddKey(Keys k)
        {
            k = KeyUtil.Normalize(k);
            if (!Combo.Contains(k)) Combo.Add(k);
            Changed();
        }

        public void ClearKeys() { Combo.Clear(); down.Clear(); Changed(); }

        void Changed()
        {
            UpdateText();
            if (ComboChanged != null) ComboChanged(this, EventArgs.Empty);
        }

        public void UpdateText()
        {
            Text = Combo.Count == 0 ? "(none) - click here, then press keys" : KeyUtil.Pretty(Combo);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Keys k = KeyUtil.Normalize(e.KeyCode);
            if (k != Keys.None)
            {
                if (down.Count == 0) Combo.Clear();
                down.Add(k);
                if (!Combo.Contains(k)) Combo.Add(k);
                Changed();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            Keys k = KeyUtil.Normalize(e.KeyCode);
            if (!down.Contains(k) && k != Keys.None)
            {
                if (down.Count == 0) Combo.Clear();
                if (!Combo.Contains(k)) Combo.Add(k);
                Changed();
            }
            down.Remove(k);
            e.Handled = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }

        protected override void OnLostFocus(EventArgs e) { down.Clear(); base.OnLostFocus(e); }
    }

    class KeyItem
    {
        public Keys Key;
        public KeyItem(Keys k) { Key = k; }
        public override string ToString() { return KeyUtil.Name(Key); }
    }

    static class Dlg
    {
        public static Form NewDialog(string title, int w, int h)
        {
            Form f = new Form();
            f.SuspendLayout();
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Font = new Font("Segoe UI", 9F);
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false; f.ShowIcon = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(w, h);
            return f;
        }

        public static void Finish(Form f, Theme theme)
        {
            f.ResumeLayout(false);
            f.PerformLayout();
            theme.Apply(f);
            f.Load += delegate { theme.Apply(f); };
        }

        public static void Info(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "Ctlr To Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public class ActionEditor : Panel
    {
        RadioButton rbKeys, rbRun, rbMacro, rbNone;
        KeyCaptureBox kcb;
        ComboBox cboAdd, cboMode;
        TextBox txtPath, txtArgs, txtMacro;
        Button btnClear, btnAdd, btnBrowse, bKey, bWait, bText, bRun;
        Theme theme;
        static readonly string[] Modes = { "Tap", "Hold", "Repeat" };

        public ActionEditor(ActionDef d, Theme theme, bool longPress)
        {
            this.theme = theme;
            Size = new Size(360, 392);

            rbKeys = new RadioButton { Text = "Press key / shortcut", Location = new Point(0, 2), AutoSize = true };
            kcb = new KeyCaptureBox { Location = new Point(20, 28), Size = new Size(226, 23) };
            kcb.SetKeys(KeyUtil.Parse(d.Keys));
            btnClear = new Button { Text = "Clear", Location = new Point(254, 26), Size = new Size(96, 27) };
            btnClear.Click += delegate { kcb.ClearKeys(); };
            Label lAdd = new Label { Text = "Add key:", Location = new Point(20, 64), AutoSize = true };
            cboAdd = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(86, 60), Size = new Size(160, 23), MaxDropDownItems = 16 };
            foreach (Keys k in KeyUtil.SpecialKeys()) cboAdd.Items.Add(new KeyItem(k));
            cboAdd.SelectedIndex = 0;
            btnAdd = new Button { Text = "Add", Location = new Point(254, 58), Size = new Size(96, 27) };
            btnAdd.Click += delegate { KeyItem ki = cboAdd.SelectedItem as KeyItem; if (ki != null) kcb.AddKey(ki.Key); };
            Label lMode = new Label { Text = "Mode:", Location = new Point(20, 96), AutoSize = true };
            cboMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(86, 92), Size = new Size(264, 23) };
            cboMode.Items.AddRange(new object[] { "Tap - press once", "Hold - keys held while button held", "Repeat - auto-fire while held" });
            int mi = Array.IndexOf(Modes, d.Mode);
            cboMode.SelectedIndex = mi < 0 ? 0 : mi;

            rbRun = new RadioButton { Text = "Run a program / open a file, folder or website", Location = new Point(0, 128), AutoSize = true };
            txtPath = new TextBox { Location = new Point(20, 154), Size = new Size(226, 23), Text = d.RunPath };
            btnBrowse = new Button { Text = "Browse...", Location = new Point(254, 152), Size = new Size(96, 27) };
            btnBrowse.Click += delegate { Browse(); };
            Label lArgs = new Label { Text = "Arguments:", Location = new Point(20, 188), AutoSize = true };
            txtArgs = new TextBox { Location = new Point(100, 184), Size = new Size(250, 23), Text = d.RunArgs };

            rbMacro = new RadioButton { Text = "Macro - several steps in a row", Location = new Point(0, 218), AutoSize = true };
            txtMacro = new TextBox
            {
                Location = new Point(20, 242), Size = new Size(330, 84), Multiline = true, ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true, WordWrap = false, Font = new Font("Consolas", 9.5F),
                Text = (d.Macro ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n")
            };
            bKey = SmallBtn("+ Key...", 20); bWait = SmallBtn("+ Wait", 104); bText = SmallBtn("+ Text...", 188); bRun = SmallBtn("+ Run...", 272);
            bKey.Click += delegate
            {
                List<Keys> ks = KeyPrompt.Show(FindForm(), theme);
                if (ks != null && ks.Count > 0) AddStep("key " + KeyUtil.Pretty(ks));
            };
            bWait.Click += delegate
            {
                string v = InputBox.Show(FindForm(), theme, "Add wait", "Wait how many milliseconds? (1000 = 1 second)", "500");
                int ms;
                if (v != null && int.TryParse(v, out ms)) AddStep("wait " + ms);
            };
            bText.Click += delegate
            {
                string v = InputBox.Show(FindForm(), theme, "Add text", "Text to type:", "");
                if (v != null) AddStep("type " + v);
            };
            bRun.Click += delegate
            {
                string v = InputBox.Show(FindForm(), theme, "Add run step", "Program, file or website to open (e.g. notepad, https://...):", "");
                if (v != null) AddStep("run " + v);
            };

            rbNone = new RadioButton { Text = "Do nothing", Location = new Point(0, 366), AutoSize = true };

            Controls.AddRange(new Control[] { rbKeys, kcb, btnClear, lAdd, cboAdd, btnAdd, lMode, cboMode,
                rbRun, txtPath, btnBrowse, lArgs, txtArgs, rbMacro, txtMacro, bKey, bWait, bText, bRun, rbNone });

            if (d.Action == "Run") rbRun.Checked = true;
            else if (d.Action == "Macro") rbMacro.Checked = true;
            else if (d.Action == "None") rbNone.Checked = true;
            else rbKeys.Checked = true;
            foreach (RadioButton rb in new[] { rbKeys, rbRun, rbMacro, rbNone })
                rb.CheckedChanged += delegate { UpdateEnabled(); };
            kcb.Enter += delegate { rbKeys.Checked = true; };
            UpdateEnabled();
        }

        Button SmallBtn(string text, int x)
        {
            return new Button { Text = text, Location = new Point(x, 332), Size = new Size(78, 27) };
        }

        void AddStep(string line)
        {
            rbMacro.Checked = true;
            string t = txtMacro.Text.TrimEnd('\r', '\n');
            txtMacro.Text = (t.Length == 0 ? "" : t + "\r\n") + line;
            txtMacro.SelectionStart = txtMacro.Text.Length;
            txtMacro.ScrollToCaret();
        }

        void UpdateEnabled()
        {
            bool k = rbKeys.Checked, r = rbRun.Checked, m = rbMacro.Checked;
            btnClear.Enabled = cboAdd.Enabled = btnAdd.Enabled = cboMode.Enabled = k;
            txtPath.Enabled = txtArgs.Enabled = btnBrowse.Enabled = r;
            txtMacro.Enabled = m;
        }

        void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a program or file";
                d.Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*";
                d.DereferenceLinks = false;
                if (d.ShowDialog(FindForm()) == DialogResult.OK) { txtPath.Text = d.FileName; rbRun.Checked = true; }
            }
        }

        public ActionDef GetDef()
        {
            ActionDef d = new ActionDef();
            d.Action = rbRun.Checked ? "Run" : rbMacro.Checked ? "Macro" : rbNone.Checked ? "None" : "Keys";
            d.Keys = KeyUtil.Join(kcb.Combo);
            d.Mode = Modes[Math.Max(0, cboMode.SelectedIndex)];
            d.RunPath = txtPath.Text.Trim();
            d.RunArgs = txtArgs.Text.Trim();
            d.Macro = txtMacro.Text.Replace("\r\n", "\n").Trim('\n');
            return d;
        }

        public string Check()
        {
            ActionDef d = GetDef();
            if (d.Action == "Run" && d.RunPath.Length == 0) return "Enter a program, file, folder or website to open.";
            if (d.Action == "Macro")
            {
                if (MacroRunner.CountSteps(d.Macro) == 0) return "Add at least one macro step (use the + buttons).";
                foreach (string raw in d.Macro.Split('\n'))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int sp = l.IndexOf(' ');
                    string cmd = (sp < 0 ? l : l.Substring(0, sp)).ToLowerInvariant();
                    string arg = sp < 0 ? "" : l.Substring(sp + 1);
                    if ((cmd == "key" || cmd == "down" || cmd == "up") && KeyUtil.ParseFriendly(arg).Count == 0)
                        return "Macro step \"" + l + "\": unknown key.";
                    if (cmd == "wait") { int ms; if (!int.TryParse(arg.Trim(), out ms)) return "Macro step \"" + l + "\": wait needs a number of milliseconds."; }
                    if (cmd != "key" && cmd != "down" && cmd != "up" && cmd != "wait" && cmd != "type" && cmd != "run")
                        return "Macro step \"" + l + "\" is not understood.\nUse: key, wait, type, run, down, up.";
                }
            }
            return null;
        }
    }

    public class EditForm : Form
    {
        public Mapping Result;

        string device;
        AppConfig cfg;
        Theme theme;
        CheckedListBox clb;
        Button btnRecord, tabShort, tabLong;
        Label lblLive;
        Timer timer;
        bool recording;
        HashSet<string> rec = new HashSet<string>();
        ActionEditor edShort, edLong;
        Panel pageShort, pageLong;
        CheckBox chkLong;
        TextBox txtComment;
        int page;

        public EditForm(Mapping m, string device, AppConfig cfg, Theme theme, string title, int longMs)
        {
            this.device = device; this.cfg = cfg; this.theme = theme;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 596);

            GroupBox gT = new GroupBox { Text = "Controller button(s)", Location = new Point(12, 10), Size = new Size(226, 498) };
            clb = new CheckedListBox { Location = new Point(10, 22), Size = new Size(206, 366), CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            foreach (string n in Devices.ButtonNames(device)) clb.Items.Add(n);
            foreach (string b in m.Buttons) if (!clb.Items.Contains(b)) clb.Items.Add(b);
            for (int i = 0; i < clb.Items.Count; i++)
                if (m.Buttons.Contains((string)clb.Items[i])) clb.SetItemChecked(i, true);
            btnRecord = new Button { Text = "Record from controller", Location = new Point(10, 396), Size = new Size(206, 30) };
            btnRecord.Click += delegate { rec.Clear(); recording = true; btnRecord.Text = "Press button(s) now..."; };
            lblLive = new Label { Text = "Pressed: -", Location = new Point(10, 434), Size = new Size(206, 22), Tag = "hint" };
            Label hintT = new Label { Text = "Tick 2+ buttons for a combination.", Location = new Point(10, 460), Size = new Size(206, 30), Tag = "hint" };
            gT.Controls.AddRange(new Control[] { clb, btnRecord, lblLive, hintT });

            tabShort = new Button { Text = "Normal press", Location = new Point(250, 10), Size = new Size(130, 29), Tag = "tab", TabStop = false };
            tabLong = new Button { Text = "Long press (hold)", Location = new Point(379, 10), Size = new Size(140, 29), Tag = "tab", TabStop = false };
            tabShort.Click += delegate { SelectPage(0); };
            tabLong.Click += delegate { SelectPage(1); };
            Panel box = new Panel { Location = new Point(250, 38), Size = new Size(378, 470), BorderStyle = BorderStyle.FixedSingle, Tag = "panel" };

            pageShort = new Panel { Dock = DockStyle.Fill };
            edShort = new ActionEditor(m.Main, theme, false) { Location = new Point(9, 10) };
            pageShort.Controls.Add(edShort);

            pageLong = new Panel { Dock = DockStyle.Fill, Visible = false };
            chkLong = new CheckBox { Text = "Do something else when held for " + longMs + " ms or more", Location = new Point(9, 8), AutoSize = true, Checked = m.Long != null };
            Label hintL = new Label { Text = "With this on, a quick tap always fires the Normal press action once. Change the hold time on the Profile tab.", Location = new Point(9, 30), Size = new Size(356, 32), Tag = "hint" };
            edLong = new ActionEditor(m.Long ?? new ActionDef(), theme, true) { Location = new Point(9, 66) };
            chkLong.CheckedChanged += delegate { edLong.Enabled = chkLong.Checked; UpdateTabText(); };
            edLong.Enabled = chkLong.Checked;
            pageLong.Controls.AddRange(new Control[] { chkLong, hintL, edLong });
            box.Controls.Add(pageShort);
            box.Controls.Add(pageLong);

            Label lC = new Label { Text = "Comment:", Location = new Point(12, 524), AutoSize = true };
            txtComment = new TextBox { Location = new Point(80, 520), Size = new Size(548, 23), Text = m.Comment };

            Button ok = new Button { Text = "OK", Location = new Point(444, 556), Size = new Size(88, 30) };
            Button cancel = new Button { Text = "Cancel", Location = new Point(540, 556), Size = new Size(88, 30), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { Accept(); };
            CancelButton = cancel;

            Controls.AddRange(new Control[] { gT, tabShort, tabLong, box, lC, txtComment, ok, cancel });
            ResumeLayout(false);
            PerformLayout();

            theme.Apply(this);
            UpdateTabText();
            SelectPage(0);

            timer = new Timer { Interval = 15 };
            timer.Tick += delegate { Poll(); };
            timer.Start();
            Load += delegate { theme.Apply(this); StyleTabs(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }

        void UpdateTabText()
        {
            tabLong.Text = chkLong.Checked ? "Long press  ✓" : "Long press (hold)";
        }

        void SelectPage(int i)
        {
            page = i;
            pageShort.Visible = i == 0;
            pageLong.Visible = i == 1;
            StyleTabs();
        }

        void StyleTabs()
        {
            Button[] tabs = { tabShort, tabLong };
            for (int i = 0; i < 2; i++)
            {
                bool sel = i == page;
                Button b = tabs[i];
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = sel ? theme.Panel : theme.TabInactive;
                b.ForeColor = sel ? theme.Text : theme.SubText;
                b.FlatAppearance.BorderColor = theme.Border;
                b.FlatAppearance.MouseOverBackColor = sel ? theme.Panel : theme.ButtonHover;
                if (sel) b.BringToFront();
            }
        }

        void Poll()
        {
            HashSet<string> pr = Devices.GetPressed(device, cfg);
            if (pr == null) { lblLive.Text = "Controller not connected"; return; }
            lblLive.Text = "Pressed: " + (pr.Count == 0 ? "-" : string.Join(" + ", pr.ToArray()));
            if (!recording) return;
            if (pr.Count > 0) rec.UnionWith(pr);
            else if (rec.Count > 0)
            {
                recording = false;
                btnRecord.Text = "Record from controller";
                for (int i = 0; i < clb.Items.Count; i++)
                    clb.SetItemChecked(i, rec.Contains((string)clb.Items[i]));
            }
        }

        void Accept()
        {
            List<string> btns = clb.CheckedItems.Cast<string>().ToList();
            if (btns.Count == 0) { Dlg.Info(this, "Pick at least one controller button (tick it in the list or use Record)."); return; }
            string err = edShort.Check();
            if (err != null) { SelectPage(0); Dlg.Info(this, err); return; }
            if (chkLong.Checked)
            {
                err = edLong.Check();
                if (err == null && edLong.GetDef().IsEmpty()) err = "Set the long-press action, or untick \"Do something else when held\".";
                if (err != null) { SelectPage(1); Dlg.Info(this, err); return; }
            }
            Mapping r = new Mapping();
            r.Buttons = btns;
            r.Main = edShort.GetDef();
            r.Long = chkLong.Checked ? edLong.GetDef() : null;
            r.Comment = txtComment.Text.Trim();
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public static class InputBox
    {
        public static string Show(IWin32Window owner, Theme theme, string title, string prompt, string value)
        {
            using (Form f = Dlg.NewDialog(title, 380, 116))
            {
                Label l = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true };
                TextBox t = new TextBox { Text = value, Location = new Point(12, 36), Size = new Size(356, 23) };
                Button ok = new Button { Text = "OK", Location = new Point(186, 76), Size = new Size(88, 28), DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Cancel", Location = new Point(280, 76), Size = new Size(88, 28), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Dlg.Finish(f, theme);
                f.Shown += delegate { t.SelectAll(); t.Focus(); };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string s = t.Text.Trim();
                return s.Length == 0 ? null : s;
            }
        }
    }

    public static class KeyPrompt
    {
        public static List<Keys> Show(IWin32Window owner, Theme theme)
        {
            using (Form f = Dlg.NewDialog("Add key step", 380, 150))
            {
                Label l = new Label { Text = "Click the box and press the key or shortcut:", Location = new Point(12, 12), AutoSize = true };
                KeyCaptureBox k = new KeyCaptureBox { Location = new Point(12, 36), Size = new Size(356, 23) };
                Label la = new Label { Text = "Or add:", Location = new Point(12, 74), AutoSize = true };
                ComboBox c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(66, 70), Size = new Size(200, 23), MaxDropDownItems = 16 };
                foreach (Keys key in KeyUtil.SpecialKeys()) c.Items.Add(new KeyItem(key));
                c.SelectedIndex = 0;
                Button add = new Button { Text = "Add", Location = new Point(274, 68), Size = new Size(94, 27) };
                add.Click += delegate { KeyItem ki = c.SelectedItem as KeyItem; if (ki != null) k.AddKey(ki.Key); };
                Button ok = new Button { Text = "OK", Location = new Point(186, 110), Size = new Size(88, 28), DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Cancel", Location = new Point(280, 110), Size = new Size(88, 28), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, k, la, c, add, ok, cancel });
                f.CancelButton = cancel;
                Dlg.Finish(f, theme);
                f.Shown += delegate { k.Focus(); };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return k.Combo;
            }
        }
    }

    public static class ComboPicker
    {
        public static string Show(IWin32Window owner, Theme theme, AppConfig cfg, string device, string current)
        {
            using (Form f = Dlg.NewDialog("Controller on/off combo", 280, 450))
            {
                List<string> cur = current.Split('+').Select(s => s.Trim()).ToList();
                Label l = new Label { Text = "Tick the buttons to hold together:", Location = new Point(12, 10), AutoSize = true };
                CheckedListBox clb = new CheckedListBox { Location = new Point(12, 32), Size = new Size(256, 290), CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
                foreach (string n in Devices.ButtonNames(device).Concat(Devices.XButtons).Concat(cur).Distinct())
                    if (n.Length > 0) clb.Items.Add(n, cur.Contains(n));
                Button rec = new Button { Text = "Record from controller", Location = new Point(12, 330), Size = new Size(256, 30) };
                Label live = new Label { Text = "", Location = new Point(12, 366), Size = new Size(256, 22), Tag = "hint" };
                Button ok = new Button { Text = "OK", Location = new Point(86, 410), Size = new Size(88, 28), DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Cancel", Location = new Point(180, 410), Size = new Size(88, 28), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, clb, rec, live, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Dlg.Finish(f, theme);

                bool recording = false;
                HashSet<string> got = new HashSet<string>();
                rec.Click += delegate { got.Clear(); recording = true; rec.Text = "Hold the buttons, then let go..."; };
                Timer t = new Timer { Interval = 15 };
                t.Tick += delegate
                {
                    HashSet<string> pr = Devices.GetPressed(device, cfg);
                    if (pr == null) { live.Text = "Controller not connected"; return; }
                    live.Text = "Pressed: " + (pr.Count == 0 ? "-" : string.Join(" + ", pr.ToArray()));
                    if (!recording) return;
                    if (pr.Count > 0) got.UnionWith(pr);
                    else if (got.Count > 0)
                    {
                        recording = false;
                        rec.Text = "Record from controller";
                        for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, got.Contains((string)clb.Items[i]));
                    }
                };
                t.Start();
                DialogResult res = f.ShowDialog(owner);
                t.Stop(); t.Dispose();
                if (res != DialogResult.OK) return null;
                List<string> sel = clb.CheckedItems.Cast<string>().ToList();
                if (sel.Count < 2) { Dlg.Info(owner, "Pick at least 2 buttons, so it can't be pressed by accident."); return null; }
                return string.Join("+", sel.ToArray());
            }
        }
    }

    public static class ProgramPicker
    {
        class Item
        {
            public string Name, Title;
            public override string ToString() { return Name + "    -    " + Title; }
        }

        public static string Show(IWin32Window owner, Theme theme)
        {
            List<Item> items = new List<Item>();
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    items.Add(new Item { Name = p.ProcessName, Title = p.MainWindowTitle });
                }
                catch (Exception) { }
            }
            items = items.GroupBy(i => i.Name.ToLowerInvariant()).Select(g => g.First()).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

            using (Form f = Dlg.NewDialog("Pick a program", 440, 360))
            {
                Label l = new Label { Text = "Open programs (open the one you want first if it's not listed):", Location = new Point(12, 10), AutoSize = true };
                ListBox lb = new ListBox { Location = new Point(12, 32), Size = new Size(416, 280), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
                foreach (Item i in items) lb.Items.Add(i);
                Button ok = new Button { Text = "OK", Location = new Point(246, 322), Size = new Size(88, 28), DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Cancel", Location = new Point(340, 322), Size = new Size(88, 28), DialogResult = DialogResult.Cancel };
                lb.DoubleClick += delegate { if (lb.SelectedItem != null) { f.DialogResult = DialogResult.OK; f.Close(); } };
                f.Controls.AddRange(new Control[] { l, lb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Dlg.Finish(f, theme);
                if (f.ShowDialog(owner) != DialogResult.OK || lb.SelectedItem == null) return null;
                return ((Item)lb.SelectedItem).Name;
            }
        }
    }
}
