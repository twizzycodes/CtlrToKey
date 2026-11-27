using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CtlrToKey
{
    public class MainForm : Form
    {
        const string Version = "7.3";
        const int HotkeyId = 0x5C7A;
        const string GitHubUrl = "https://github.com/twizzycodes/CtlrToKey";

        AppConfig cfg;
        Engine engine;
        Theme theme;
        Timer timer, msgTimer;

        MenuStrip menu;
        ToolStripMenuItem miMaster, miTop, miUndo;
        List<ToolStripMenuItem> miThemes = new List<ToolStripMenuItem>();
        ListBox lstProfiles;
        Panel tabBar, content, pageMap, pageProf, pageOpt;
        Button[] tabs;
        Button btnPower;
        ComboBox cboDevice, cboTheme;
        ListView lv;
        ColumnHeader colBtn, colFn, colMode, colCmt;
        StatusStrip status;
        ToolStripStatusLabel stState, stDevice, stPressed, stMsg;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trMaster, trProfiles;
        Label lblProfName;
        CheckBox chkProfEnabled;
        NumericUpDown numLongPress;
        CheckBox chkAutoStart, chkTop, chkHotkey, chkCombo, chkLock;
        KeyCaptureBox hkBox;
        Label lblCombo;
        TextBox txtLock;

        bool loading, reallyExit, startHidden, trayTipShown;
        int currentTab;
        string lastLiveKey, lastStateKey;
        List<RowInfo> rows = new List<RowInfo>();
        List<UndoState> undo = new List<UndoState>();

        class RowInfo
        {
            public List<string> Buttons;
            public Mapping Map;
            public ListViewItem Item;
            public bool Lit;
        }

        class UndoState
        {
            public List<Profile> Profiles;
            public int Selected;
            public string Label;
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        Profile Sel
        {
            get
            {
                int i = lstProfiles.SelectedIndex;
                return i >= 0 && i < cfg.Profiles.Count ? cfg.Profiles[i] : null;
            }
        }

        public MainForm(bool startMin)
        {
            cfg = AppConfig.Load();
            engine = new Engine(cfg);
            engine.Message = ShowMsg;
            engine.ToggleRequested = delegate { ToggleMaster(true); };
            theme = Theme.Get(cfg.Theme);

            timer = new Timer { Interval = Math.Max(1, cfg.PollInterval) };
            timer.Tick += delegate { engine.Tick(); UpdateLive(); };
            msgTimer = new Timer { Interval = 6000 };
            msgTimer.Tick += delegate { msgTimer.Stop(); stMsg.Text = ""; };

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Ctlr To Key";
            Icon = Program.AppIcon;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(680, 440);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildMenu();
            BuildStatus();
            Panel body = BuildBody();
            Controls.Add(body);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
            ResumeLayout(false);
            PerformLayout();

            BuildTray();

            startHidden = startMin || cfg.StartMinimized;
            if (startHidden && !cfg.MinimizeToTray && !cfg.CloseToTray)
            {
                startHidden = false;
                WindowState = FormWindowState.Minimized;
            }
            TopMost = cfg.AlwaysOnTop;

            Profile active = cfg.Profiles.FirstOrDefault(p => p.Name == cfg.ActiveProfile) ?? cfg.Profiles[0];
            ReloadProfiles(active);
            ApplyTheme();
            SelectTab(0);
            UpdatePower();
            timer.Start();

            Load += OnLoadForm;
            Resize += OnResizeForm;
            FormClosing += OnClosingForm;
        }

        void BuildMenu()
        {
            menu = new MenuStrip { Padding = new Padding(4, 2, 0, 2) };

            ToolStripMenuItem mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add(new ToolStripMenuItem("New profile...", null, delegate { CreateProfile(); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("Import profiles...", null, delegate { ImportProfiles(); }));
            mFile.DropDownItems.Add(new ToolStripMenuItem("Export this profile...", null, delegate { ExportProfiles(false); }));
            mFile.DropDownItems.Add(new ToolStripMenuItem("Export all profiles...", null, delegate { ExportProfiles(true); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem miSave = new ToolStripMenuItem("Save", null, delegate { Save(); ShowMsg("Saved to " + AppConfig.CurrentPath); });
            miSave.ShortcutKeys = Keys.Control | Keys.S;
            mFile.DropDownItems.Add(miSave);
            mFile.DropDownItems.Add(new ToolStripMenuItem("Open settings folder", null, delegate { OpenSettingsFolder(); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));

            ToolStripMenuItem mEdit = new ToolStripMenuItem("&Edit");
            miUndo = new ToolStripMenuItem("Undo", null, delegate { Undo(); });
            miUndo.ShortcutKeys = Keys.Control | Keys.Z;
            mEdit.DropDownItems.Add(miUndo);
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(new ToolStripMenuItem("Edit button assignment...", null, delegate { EditSelected(); }));
            mEdit.DropDownItems.Add(new ToolStripMenuItem("Add combination...", null, delegate { AddCombo(); }));
            mEdit.DropDownItems.Add(new ToolStripMenuItem("Clear assignment", null, delegate { ClearSelected(); }));
            mEdit.DropDownItems.Add(new ToolStripMenuItem("Clear all assignments in this profile...", null, delegate { ClearAll(); }));
            mEdit.DropDownOpening += delegate { miUndo.Text = undo.Count > 0 ? "Undo " + undo[undo.Count - 1].Label : "Undo"; miUndo.Enabled = undo.Count > 0; };

            ToolStripMenuItem mSet = new ToolStripMenuItem("&Settings");
            ToolStripMenuItem mTheme = new ToolStripMenuItem("Theme");
            foreach (string n in Theme.Names)
            {
                string name = n;
                ToolStripMenuItem it = new ToolStripMenuItem(name, null, delegate { SetTheme(name); });
                miThemes.Add(it);
                mTheme.DropDownItems.Add(it);
            }
            mSet.DropDownItems.Add(mTheme);
            miTop = new ToolStripMenuItem("Always on top", null, delegate { SetTopMost(!cfg.AlwaysOnTop); });
            mSet.DropDownItems.Add(miTop);
            mSet.DropDownItems.Add(new ToolStripMenuItem("Refresh controllers", null, delegate { RefreshDevices(); ShowMsg("Controller list refreshed."); }));
            mSet.DropDownItems.Add(new ToolStripSeparator());
            mSet.DropDownItems.Add(new ToolStripMenuItem("Options...", null, delegate { SelectTab(2); }));

            ToolStripMenuItem mPow = new ToolStripMenuItem("&On/Off");
            miMaster = new ToolStripMenuItem("Turn everything OFF", null, delegate { ToggleMaster(false); });
            miMaster.ShortcutKeys = Keys.Control | Keys.P;
            mPow.DropDownItems.Add(miMaster);
            mPow.DropDownItems.Add(new ToolStripSeparator());
            mPow.DropDownItems.Add(new ToolStripMenuItem("Turn all profiles on", null, delegate { SetAllProfiles(true); }));
            mPow.DropDownItems.Add(new ToolStripMenuItem("Turn all profiles off", null, delegate { SetAllProfiles(false); }));
            mPow.DropDownItems.Add(new ToolStripMenuItem("Only this profile on", null, delegate { OnlyThisProfile(); }));

            ToolStripMenuItem mHelp = new ToolStripMenuItem("&Help");
            mHelp.DropDownItems.Add(new ToolStripMenuItem("How to use", null, delegate { ShowHelp(); }));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("Macro steps help", null, delegate { ShowMacroHelp(); }));
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add(new ToolStripMenuItem("GitHub", null, delegate { OpenUrl(GitHubUrl); }));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("About Ctlr To Key", null, delegate
            {
                MessageBox.Show(this, "Ctlr To Key v" + Version + "\nBy: Zerx/Twizzy\n\nTurn controller buttons and button combinations into keyboard shortcuts, macros, or app launchers.\n\n" +
                    GitHubUrl + "\n\nSettings file:\n" + AppConfig.CurrentPath,
                    "About Ctlr To Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add(new ToolStripLabel("By: Zerx/Twizzy"));

            menu.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mSet, mPow, mHelp });
        }

        void BuildStatus()
        {
            status = new StatusStrip { SizingGrip = true };
            stState = new ToolStripStatusLabel("ON") { Margin = new Padding(4, 3, 12, 2) };
            stDevice = new ToolStripStatusLabel("") { Margin = new Padding(0, 3, 12, 2) };
            stPressed = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            stMsg = new ToolStripStatusLabel("") { TextAlign = ContentAlignment.MiddleRight };
            status.Items.AddRange(new ToolStripItem[] { stState, stDevice, stPressed, stMsg });
        }

        Panel BuildBody()
        {
            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4) };

            Panel left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(0, 0, 6, 0) };
            lstProfiles = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = (int)(20 * Program.Scale)
            };
            lstProfiles.DrawItem += DrawProfile;
            lstProfiles.SelectedIndexChanged += delegate { if (!loading && Sel != null) ActivateProfile(Sel); };
            lstProfiles.MouseDown += delegate (object o, MouseEventArgs e)
            {
                int i = lstProfiles.IndexFromPoint(e.Location);
                if (i >= 0 && e.Button == MouseButtons.Right) lstProfiles.SelectedIndex = i;
            };
            lstProfiles.DoubleClick += delegate { RenameProfile(); };
            ContextMenuStrip pm = new ContextMenuStrip();
            ToolStripMenuItem pmToggle = new ToolStripMenuItem("Turn off", null, delegate { if (Sel != null) ToggleProfile(Sel); });
            pm.Items.Add(pmToggle);
            pm.Items.Add("Only this profile on", null, delegate { OnlyThisProfile(); });
            pm.Items.Add(new ToolStripSeparator());
            pm.Items.Add("Rename...", null, delegate { RenameProfile(); });
            pm.Items.Add("Copy...", null, delegate { CopyProfile(); });
            pm.Items.Add("Export...", null, delegate { ExportProfiles(false); });
            pm.Items.Add("Delete...", null, delegate { DeleteProfile(); });
            pm.Opening += delegate
            {
                theme.StyleMenu(pm);
                pmToggle.Text = Sel != null && Sel.Enabled ? "Turn off" : "Turn on";
            };
            lstProfiles.ContextMenuStrip = pm;
            lstProfiles.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space && Sel != null) { ToggleProfile(Sel); e.Handled = true; }
                else if (e.KeyCode == Keys.F2) { RenameProfile(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteProfile(); e.Handled = true; }
            };
            TableLayoutPanel pb = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 36, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 5, 0, 0) };
            for (int i = 0; i < 4; i++) pb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            pb.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pb.Controls.Add(MakeBtn("Create", delegate { CreateProfile(); }), 0, 0);
            pb.Controls.Add(MakeBtn("Rename", delegate { RenameProfile(); }), 1, 0);
            pb.Controls.Add(MakeBtn("Copy", delegate { CopyProfile(); }), 2, 0);
            pb.Controls.Add(MakeBtn("Delete", delegate { DeleteProfile(); }), 3, 0);
            left.Controls.Add(lstProfiles);
            left.Controls.Add(pb);

            Panel right = new Panel { Dock = DockStyle.Fill };
            tabBar = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 0, 0, 2) };
            tabs = new[] { MakeTab("[1] Controller", 0, 116), MakeTab("Profile", 115, 80), MakeTab("Options", 194, 80) };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                tabs[i].Click += delegate { SelectTab(idx); };
                tabBar.Controls.Add(tabs[i]);
            }
            btnPower = new Button { Dock = DockStyle.Right, Width = 80, TabStop = false };
            btnPower.Click += delegate { ToggleMaster(false); };
            new ToolTip().SetToolTip(btnPower, "Master switch: turns every profile on or off at once (Ctrl+P)");
            tabBar.Controls.Add(btnPower);

            content = new Panel { Dock = DockStyle.Fill, Tag = "panel", Padding = new Padding(8), BorderStyle = BorderStyle.FixedSingle };
            pageMap = BuildMapPage();
            pageProf = BuildProfilePage();
            pageOpt = BuildOptPage();
            content.Controls.Add(pageMap);
            content.Controls.Add(pageProf);
            content.Controls.Add(pageOpt);

            right.Controls.Add(content);
            right.Controls.Add(tabBar);

            body.Controls.Add(right);
            body.Controls.Add(left);
            return body;
        }

        Button MakeBtn(string text, EventHandler click)
        {
            Button b = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(1, 0, 1, 0), Padding = new Padding(0) };
            b.Click += click;
            return b;
        }

        Button MakeTab(string text, int x, int w)
        {
            Button b = new Button { Text = text, Location = new Point(x, 2), Size = new Size(w, 29), Tag = "tab", FlatStyle = FlatStyle.Flat, TabStop = false };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        Button FlowBtn(string text, EventHandler click)
        {
            Button b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 30), Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 6, 0) };
            b.Click += click;
            return b;
        }

        Panel BuildMapPage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill };

            TableLayoutPanel top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 0, 0, 6) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label lbl = new Label { Text = "Controller:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
            cboDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0) };
            cboDevice.SelectedIndexChanged += delegate { DeviceChanged(); };
            Button btnRefresh = new Button { Text = "Refresh", Anchor = AnchorStyles.Right, Size = new Size(80, 27), Margin = new Padding(0) };
            btnRefresh.Click += delegate { RefreshDevices(); ShowMsg("Controller list refreshed."); };
            top.Controls.Add(lbl, 0, 0);
            top.Controls.Add(cboDevice, 1, 0);
            top.Controls.Add(btnRefresh, 2, 0);

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle
            };
            colBtn = lv.Columns.Add("Button", 150);
            colFn = lv.Columns.Add("Function", 210);
            colMode = lv.Columns.Add("Mode", 70);
            colCmt = lv.Columns.Add("Comment", 120);
            lv.DrawColumnHeader += DrawHeader;
            lv.DrawItem += delegate (object o, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            lv.DrawSubItem += delegate (object o, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            lv.DoubleClick += delegate { EditSelected(); };
            lv.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) { ClearSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = true; }
            };
            lv.Resize += delegate { FitColumns(); };
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Edit button assignment...", null, delegate { EditSelected(); });
            cm.Items.Add("Clear assignment", null, delegate { ClearSelected(); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Add combination...", null, delegate { AddCombo(); });
            cm.Items.Add("Undo", null, delegate { Undo(); });
            cm.Opening += delegate { theme.StyleMenu(cm); };
            lv.ContextMenuStrip = cm;

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
            bottom.Controls.Add(FlowBtn("Edit button assignment", delegate { EditSelected(); }));
            bottom.Controls.Add(FlowBtn("Add combination...", delegate { AddCombo(); }));
            bottom.Controls.Add(FlowBtn("Clear", delegate { ClearSelected(); }));
            bottom.Controls.Add(FlowBtn("Undo", delegate { Undo(); }));

            p.Controls.Add(lv);
            p.Controls.Add(bottom);
            p.Controls.Add(top);
            return p;
        }

        Panel BuildProfilePage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
            int y = 2;
            GroupBox g = Group(p, "This profile", ref y, 4);
            lblProfName = new Label { Location = new Point(14, 26), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            g.Controls.Add(lblProfName);
            chkProfEnabled = new CheckBox { Text = "Profile is on (its buttons work)", Location = new Point(14, 56), AutoSize = true };
            chkProfEnabled.CheckedChanged += delegate { if (!loading && Sel != null && Sel.Enabled != chkProfEnabled.Checked) ToggleProfile(Sel); };
            g.Controls.Add(chkProfEnabled);
            numLongPress = Num(g, 2, "Long-press time (ms)", 100, 5000, 500, v => { if (Sel != null) Sel.LongPressMs = v; });
            Label h = new Label { Text = "Hold a button at least this long to use its \"Long press\" action.", Location = new Point(14, 118), Size = new Size(440, 20), Tag = "hint" };
            g.Controls.Add(h);

            GroupBox t = Group(p, "Tools", ref y, 2);
            AddBtn(t, 0, 0, "Undo last change", delegate { Undo(); });
            AddBtn(t, 0, 1, "Clear all assignments...", delegate { ClearAll(); });
            AddBtn(t, 1, 0, "Export this profile...", delegate { ExportProfiles(false); });
            AddBtn(t, 1, 1, "Import profiles...", delegate { ImportProfiles(); });

            Label hint = new Label
            {
                Text = "Several profiles can be on at the same time. Turn a profile on or off with the box above, by right-clicking it in the list, or from the On/Off menu. The ON/OFF button at the top turns everything off without changing which profiles are on.",
                Location = new Point(2, y + 2), Size = new Size(470, 50), Tag = "hint"
            };
            p.Controls.Add(hint);
            return p;
        }

        Panel BuildOptPage()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
            int y = 2;

            GroupBox gA = Group(p, "Appearance", ref y, 1);
            gA.Controls.Add(new Label { Text = "Theme", Location = new Point(14, 27), AutoSize = true });
            cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(250, 23), Width = 130 };
            cboTheme.Items.AddRange(Theme.Names);
            cboTheme.SelectedIndexChanged += delegate { if (!loading) SetTheme((string)cboTheme.SelectedItem); };
            gA.Controls.Add(cboTheme);

            GroupBox gT = Group(p, "Turning it on / off", ref y, 4);
            chkHotkey = Chk(gT, 0, "Keyboard hotkey (works anywhere)", cfg.HotkeyEnabled, v => { cfg.HotkeyEnabled = v; RegisterToggleHotkey(true); });
            hkBox = new KeyCaptureBox { Location = new Point(250, 24), Size = new Size(150, 23) };
            hkBox.SetKeys(KeyUtil.Parse(cfg.Hotkey));
            hkBox.Enter += delegate { UnregisterHotKey(Handle, HotkeyId); };
            hkBox.Leave += delegate
            {
                cfg.Hotkey = KeyUtil.Join(hkBox.Combo);
                Save();
                RegisterToggleHotkey(true);
            };
            Button hkClear = new Button { Text = "Clear", Location = new Point(406, 23), Size = new Size(52, 25) };
            hkClear.Click += delegate { hkBox.ClearKeys(); cfg.Hotkey = ""; Save(); RegisterToggleHotkey(false); };
            gT.Controls.Add(hkBox); gT.Controls.Add(hkClear);

            chkCombo = Chk(gT, 1, "Controller combo (hold)", cfg.ComboToggleEnabled, v => cfg.ComboToggleEnabled = v);
            lblCombo = new Label { Location = new Point(250, 57), Size = new Size(150, 20), Text = PrettyCombo() };
            Button bSet = new Button { Text = "Set...", Location = new Point(406, 53), Size = new Size(52, 25) };
            bSet.Click += delegate
            {
                string r = ComboPicker.Show(this, theme, cfg, Sel != null ? Sel.Device : "XInput:0", cfg.ToggleCombo);
                if (r == null) return;
                cfg.ToggleCombo = r;
                lblCombo.Text = PrettyCombo();
                loading = true; chkCombo.Checked = true; loading = false;
                cfg.ComboToggleEnabled = true;
                Save();
            };
            gT.Controls.Add(lblCombo); gT.Controls.Add(bSet);
            Num(gT, 2, "Combo hold time (ms)", 100, 5000, cfg.ComboHoldMs, v => cfg.ComboHoldMs = v);
            Chk(gT, 3, "Play a sound when turned on / off", cfg.SoundOnToggle, v => cfg.SoundOnToggle = v);

            GroupBox gL = Group(p, "Lock to a program", ref y, 3);
            chkLock = Chk(gL, 0, "Only work when one of these programs is in front:", cfg.LockEnabled, v => cfg.LockEnabled = v);
            txtLock = new TextBox { Location = new Point(14, 56), Size = new Size(328, 23), Text = cfg.LockPrograms };
            txtLock.TextChanged += delegate { if (!loading) { cfg.LockPrograms = txtLock.Text; Save(); } };
            Button bPick = new Button { Text = "Pick...", Location = new Point(350, 55), Size = new Size(108, 25) };
            bPick.Click += delegate
            {
                string n = ProgramPicker.Show(this, theme);
                if (n == null) return;
                List<string> cur = Engine.ParsePrograms(txtLock.Text);
                if (!cur.Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase))) cur.Add(n);
                txtLock.Text = string.Join(", ", cur.ToArray());
                chkLock.Checked = true;
            };
            gL.Controls.Add(txtLock); gL.Controls.Add(bPick);
            gL.Controls.Add(new Label { Text = "Separate several with commas, e.g.  chrome, notepad, obs64", Location = new Point(14, 87), Size = new Size(440, 20), Tag = "hint" });

            GroupBox gI = Group(p, "Controller input", ref y, 4);
            Num(gI, 0, "Stick deadzone (%)", 5, 95, cfg.Deadzone, v => cfg.Deadzone = v);
            Num(gI, 1, "Trigger (LT/RT) threshold (1-254)", 1, 254, cfg.TriggerThreshold, v => cfg.TriggerThreshold = v);
            Num(gI, 2, "Combination window (ms)", 0, 1000, cfg.ComboWindow, v => cfg.ComboWindow = v);
            Num(gI, 3, "Poll interval (ms)", 1, 100, cfg.PollInterval, v => { cfg.PollInterval = v; timer.Interval = v; });

            GroupBox gK = Group(p, "Key output", ref y, 3);
            Num(gK, 0, "Repeat start delay (ms)", 0, 5000, cfg.RepeatDelay, v => cfg.RepeatDelay = v);
            Num(gK, 1, "Repeat interval (ms)", 10, 5000, cfg.RepeatInterval, v => cfg.RepeatInterval = v);
            Chk(gK, 2, "Send keys as scan codes (works better in some games)", cfg.ScanCodes, v => cfg.ScanCodes = v);

            GroupBox gW = Group(p, "Window", ref y, 5);
            Chk(gW, 0, "Minimize to system tray", cfg.MinimizeToTray, v => cfg.MinimizeToTray = v);
            Chk(gW, 1, "Close button (X) hides to tray instead of exiting", cfg.CloseToTray, v => cfg.CloseToTray = v);
            Chk(gW, 2, "Start minimized", cfg.StartMinimized, v => cfg.StartMinimized = v);
            chkAutoStart = Chk(gW, 3, "Start with Windows", GetAutoStart(), v => SetAutoStart(v));
            chkTop = Chk(gW, 4, "Always on top", cfg.AlwaysOnTop, v => SetTopMost(v));

            GroupBox gB = Group(p, "Profiles, backup & undo", ref y, 2);
            AddBtn(gB, 0, 0, "Import profiles...", delegate { ImportProfiles(); });
            AddBtn(gB, 0, 1, "Export this profile...", delegate { ExportProfiles(false); });
            AddBtn(gB, 0, 2, "Export all profiles...", delegate { ExportProfiles(true); });
            AddBtn(gB, 1, 0, "Undo last change", delegate { Undo(); });
            AddBtn(gB, 1, 1, "Clear all assignments...", delegate { ClearAll(); });

            Label hint = new Label
            {
                Text = "Settings save automatically. Macros: Edit a button > choose \"Macro\" (Help > Macro steps help).",
                Location = new Point(2, y + 2), Size = new Size(470, 36), Tag = "hint"
            };
            p.Controls.Add(hint);
            return p;
        }

        string PrettyCombo()
        {
            List<string> b = cfg.ToggleComboButtons();
            return b.Count == 0 ? "(not set)" : string.Join(" + ", b.ToArray());
        }

        GroupBox Group(Panel p, string title, ref int y, int rows)
        {
            int h = 24 + rows * 30 + 8;
            GroupBox g = new GroupBox { Text = title, Location = new Point(2, y), Size = new Size(472, h) };
            p.Controls.Add(g);
            y += h + 8;
            return g;
        }

        void AddBtn(GroupBox g, int row, int col, string text, EventHandler click)
        {
            Button b = new Button { Text = text, Location = new Point(14 + col * 150, 22 + row * 30), Size = new Size(144, 27) };
            b.Click += click;
            g.Controls.Add(b);
        }

        NumericUpDown Num(GroupBox g, int row, string label, int min, int max, int val, Action<int> set)
        {
            int y = 24 + row * 30;
            Label l = new Label { Text = label, Location = new Point(14, y + 3), AutoSize = true };
            NumericUpDown n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Min(max, Math.Max(min, val)), Location = new Point(250, y), Width = 90 };
            n.ValueChanged += delegate { if (!loading) { set((int)n.Value); Save(); } };
            g.Controls.Add(l); g.Controls.Add(n);
            return n;
        }

        CheckBox Chk(GroupBox g, int row, string label, bool val, Action<bool> set)
        {
            int y = 24 + row * 30;
            CheckBox c = new CheckBox { Text = label, Checked = val, Location = new Point(14, y + 2), AutoSize = true };
            c.CheckedChanged += delegate { if (!loading) { set(c.Checked); Save(); } };
            g.Controls.Add(c);
            return c;
        }

        void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(new ToolStripMenuItem("Open Ctlr To Key", null, delegate { ShowFromTray(); }) { Font = new Font(Font, FontStyle.Bold) });
            trMaster = new ToolStripMenuItem("Turn everything OFF", null, delegate { ToggleMaster(false); });
            trayMenu.Items.Add(trMaster);
            trProfiles = new ToolStripMenuItem("Profiles");
            trayMenu.Items.Add(trProfiles);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));
            trayMenu.Opening += delegate
            {
                theme.StyleMenu(trayMenu);
                trMaster.Text = engine.Suspended ? "Turn everything ON" : "Turn everything OFF";
                trProfiles.DropDownItems.Clear();
                foreach (Profile pr in cfg.Profiles)
                {
                    Profile p = pr;
                    ToolStripMenuItem it = new ToolStripMenuItem(p.Name, null, delegate { ToggleProfile(p); });
                    it.Checked = p.Enabled;
                    trProfiles.DropDownItems.Add(it);
                }
                theme.StyleMenu(trProfiles.DropDown);
            };
            tray = new NotifyIcon { Icon = Program.AppIcon, Text = "Ctlr To Key", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseClick += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        }

        void SetTheme(string name)
        {
            cfg.Theme = name;
            theme = Theme.Get(name);
            ApplyTheme();
            Save();
        }

        void ApplyTheme()
        {
            loading = true;
            theme.Apply(this);
            theme.StyleMenu(menu);
            foreach (ToolStripMenuItem top in menu.Items) theme.StyleMenu(top.DropDown);
            status.BackColor = theme.Back;
            foreach (ToolStripMenuItem it in miThemes) it.Checked = it.Text == theme.Name;
            cboTheme.SelectedItem = theme.Name;
            loading = false;
            StyleTabs();
            UpdatePower();
            foreach (RowInfo r in rows) { r.Lit = false; r.Item.BackColor = theme.Field; r.Item.ForeColor = theme.Text; }
            lastLiveKey = null; lastStateKey = null;
            lv.Invalidate();
            lstProfiles.Invalidate();
            if (IsHandleCreated) BeginInvoke(new Action(FitColumns));
        }

        void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(theme.Header)) e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen pen = new Pen(theme.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            Rectangle r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        void DrawProfile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= cfg.Profiles.Count) return;
            Profile p = cfg.Profiles[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(sel ? theme.Select : theme.Field)) g.FillRectangle(b, e.Bounds);
            Color tc = sel ? theme.SelectText : (p.Enabled ? theme.Text : theme.SubText);
            int pad = (int)(3 * Program.Scale);
            Rectangle tr = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, e.Bounds.Width - pad * 2, e.Bounds.Height);
            TextRenderer.DrawText(g, p.Name + (p.Enabled ? "" : "   (off)"), lstProfiles.Font, tr, tc,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void StyleTabs()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                bool sel = i == currentTab;
                Button b = tabs[i];
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = sel ? theme.Panel : theme.TabInactive;
                b.ForeColor = sel ? theme.Text : theme.SubText;
                b.FlatAppearance.BorderColor = theme.Border;
                b.FlatAppearance.MouseOverBackColor = sel ? theme.Panel : theme.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = theme.Panel;
                b.Top = sel ? 0 : 3;
                b.Height = sel ? (int)(31 * Program.Scale) : (int)(28 * Program.Scale);
                if (sel) b.BringToFront();
            }
        }

        void SelectTab(int i)
        {
            currentTab = i;
            pageMap.Visible = i == 0;
            pageProf.Visible = i == 1;
            pageOpt.Visible = i == 2;
            StyleTabs();
        }

        void FitColumns()
        {
            int w = lv.ClientSize.Width - 1;
            if (lv.Items.Count > 0 && lv.Items.Count * lv.Items[0].Bounds.Height > lv.ClientSize.Height - 24 && lv.ClientSize.Width > lv.Width - 6)
                w -= SystemInformation.VerticalScrollBarWidth;
            if (w < 200) return;
            colBtn.Width = (int)(w * 0.24);
            colFn.Width = (int)(w * 0.43);
            colMode.Width = (int)(w * 0.13);
            colCmt.Width = w - colBtn.Width - colFn.Width - colMode.Width;
        }

        void ToggleMaster(bool fromShortcut)
        {
            engine.Suspended = !engine.Suspended;
            if (engine.Suspended) { engine.ReleaseAll(); engine.CancelMacros(); }
            UpdatePower();
            ShowMsg(engine.Suspended ? "Everything is OFF - controller buttons do nothing." : "Everything is ON.");
            if (fromShortcut)
            {
                if (cfg.SoundOnToggle)
                {
                    try { (engine.Suspended ? SystemSounds.Hand : SystemSounds.Asterisk).Play(); } catch (Exception) { }
                }
                if (!Visible)
                    tray.ShowBalloonTip(1200, "Ctlr To Key", engine.Suspended ? "OFF" : "ON", ToolTipIcon.None);
            }
        }

        void UpdatePower()
        {
            if (btnPower == null) return;
            bool on = !engine.Suspended;
            btnPower.Text = on ? "ON" : "OFF";
            miMaster.Text = on ? "Turn everything OFF" : "Turn everything ON";
            tray.Text = on ? "Ctlr To Key (ON)" : "Ctlr To Key (OFF)";
            lastStateKey = null;
            UpdateTitle();
        }

        void ToggleProfile(Profile p)
        {
            p.Enabled = !p.Enabled;
            lstProfiles.Invalidate();
            if (p == Sel) { loading = true; chkProfEnabled.Checked = p.Enabled; loading = false; }
            Save();
            lastStateKey = null;
            ShowMsg("Profile \"" + p.Name + "\" is now " + (p.Enabled ? "on." : "off."));
        }

        void SetAllProfiles(bool on)
        {
            foreach (Profile p in cfg.Profiles) p.Enabled = on;
            AfterProfileFlags(on ? "All profiles on." : "All profiles off.");
        }

        void OnlyThisProfile()
        {
            Profile s = Sel;
            if (s == null) return;
            foreach (Profile p in cfg.Profiles) p.Enabled = p == s;
            AfterProfileFlags("Only \"" + s.Name + "\" is on.");
        }

        void AfterProfileFlags(string msg)
        {
            lstProfiles.Invalidate();
            if (Sel != null) { loading = true; chkProfEnabled.Checked = Sel.Enabled; loading = false; }
            Save();
            lastStateKey = null;
            ShowMsg(msg);
        }

        void RegisterToggleHotkey(bool report)
        {
            if (!IsHandleCreated) return;
            UnregisterHotKey(Handle, HotkeyId);
            if (!cfg.HotkeyEnabled) return;
            List<Keys> ks = KeyUtil.Parse(cfg.Hotkey);
            if (ks.Count == 0) return;
            uint mods = 0x4000;
            Keys main = Keys.None;
            foreach (Keys k in ks)
            {
                switch (KeyUtil.Normalize(k))
                {
                    case Keys.ControlKey: mods |= 2; break;
                    case Keys.Menu: mods |= 1; break;
                    case Keys.ShiftKey: mods |= 4; break;
                    case Keys.LWin: mods |= 8; break;
                    default: main = k; break;
                }
            }
            if (main == Keys.None)
            {
                if (report) ShowMsg("The hotkey needs a normal key too, e.g. Ctrl + Alt + J.");
                return;
            }
            if (!RegisterHotKey(Handle, HotkeyId, mods, (uint)main))
            {
                if (report) ShowMsg("Hotkey " + KeyUtil.Pretty(ks) + " is already used by another program - pick another.");
            }
            else if (report) ShowMsg("Hotkey set: " + KeyUtil.Pretty(ks));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) { ToggleMaster(true); return; }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterToggleHotkey(false);
        }

        void Snapshot(string label)
        {
            UndoState u = new UndoState
            {
                Profiles = cfg.Profiles.Select(p => p.Clone(p.Name)).ToList(),
                Selected = Math.Max(0, lstProfiles.SelectedIndex),
                Label = label
            };
            undo.Add(u);
            if (undo.Count > 40) undo.RemoveAt(0);
        }

        void Undo()
        {
            if (undo.Count == 0) { ShowMsg("Nothing to undo."); return; }
            UndoState u = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            engine.ReleaseAll();
            cfg.Profiles = u.Profiles;
            ReloadProfiles(cfg.Profiles[Math.Min(u.Selected, cfg.Profiles.Count - 1)]);
            Save();
            ShowMsg("Undid: " + u.Label);
        }

        void ReloadProfiles(Profile select)
        {
            loading = true;
            lstProfiles.Items.Clear();
            foreach (Profile p in cfg.Profiles) lstProfiles.Items.Add(p.Name);
            int idx = Math.Max(0, cfg.Profiles.IndexOf(select));
            lstProfiles.SelectedIndex = idx;
            loading = false;
            ActivateProfile(cfg.Profiles[idx]);
        }

        void ActivateProfile(Profile p)
        {
            cfg.ActiveProfile = p.Name;
            engine.UiDevice = p.Device;
            RefreshDevices();
            BuildRows();
            UpdateTitle();
            loading = true;
            lblProfName.Text = p.Name;
            chkProfEnabled.Checked = p.Enabled;
            numLongPress.Value = Math.Min(numLongPress.Maximum, Math.Max(numLongPress.Minimum, p.LongPressMs));
            loading = false;
            lastStateKey = null;
            Save();
        }

        string UniqueName(string baseName)
        {
            if (!cfg.Profiles.Any(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
            for (int i = 2; ; i++)
            {
                string n = baseName + " " + i;
                if (!cfg.Profiles.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        bool NameTaken(string n, Profile except)
        {
            if (cfg.Profiles.Any(p => p != except && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
            {
                Dlg.Info(this, "A profile named \"" + n + "\" already exists.");
                return true;
            }
            return false;
        }

        void CreateProfile()
        {
            string n = InputBox.Show(this, theme, "Create profile", "Profile name:", UniqueName("Profile " + (cfg.Profiles.Count + 1)));
            if (n == null || NameTaken(n, null)) return;
            Snapshot("create profile");
            Profile p = new Profile { Name = n, Device = Sel != null ? Sel.Device : "XInput:0" };
            cfg.Profiles.Add(p);
            ReloadProfiles(p);
        }

        void RenameProfile()
        {
            Profile p = Sel;
            if (p == null) return;
            string n = InputBox.Show(this, theme, "Rename profile", "New name:", p.Name);
            if (n == null || n == p.Name || NameTaken(n, p)) return;
            Snapshot("rename profile");
            p.Name = n;
            ReloadProfiles(p);
        }

        void CopyProfile()
        {
            Profile p = Sel;
            if (p == null) return;
            string n = InputBox.Show(this, theme, "Copy profile", "Name for the copy:", UniqueName(p.Name + " (copy)"));
            if (n == null || NameTaken(n, null)) return;
            Snapshot("copy profile");
            Profile c = p.Clone(n);
            cfg.Profiles.Insert(cfg.Profiles.IndexOf(p) + 1, c);
            ReloadProfiles(c);
        }

        void DeleteProfile()
        {
            Profile p = Sel;
            if (p == null) return;
            if (cfg.Profiles.Count <= 1) { Dlg.Info(this, "You need at least one profile."); return; }
            if (MessageBox.Show(this, "Delete profile \"" + p.Name + "\"?\n(You can undo this with Ctrl+Z.)", "Delete profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Snapshot("delete profile");
            int i = cfg.Profiles.IndexOf(p);
            engine.ResetProfile(p);
            cfg.Profiles.Remove(p);
            ReloadProfiles(cfg.Profiles[Math.Min(i, cfg.Profiles.Count - 1)]);
        }

        void ClearAll()
        {
            Profile p = Sel;
            if (p == null) return;
            if (p.Mappings.Count == 0) { ShowMsg("This profile has no assignments."); return; }
            if (MessageBox.Show(this, "Remove all " + p.Mappings.Count + " button assignments from \"" + p.Name + "\"?\n(You can undo this with Ctrl+Z.)",
                "Clear all", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Snapshot("clear all");
            engine.ResetProfile(p);
            p.Mappings.Clear();
            Save();
            BuildRows();
            ShowMsg("All assignments cleared.");
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        void ExportProfiles(bool all)
        {
            if (!all && Sel == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = all ? "Export all profiles" : "Export profile";
                d.Filter = "Ctlr To Key profile (*.ctkprofile)|*.ctkprofile|XML file (*.xml)|*.xml";
                d.FileName = SafeFileName(all ? "Ctlr To Key profiles" : Sel.Name) + ".ctkprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ProfileFile f = new ProfileFile();
                    f.Profiles = (all ? cfg.Profiles : new List<Profile> { Sel }).Select(p => p.Clone(p.Name)).ToList();
                    f.Save(d.FileName);
                    ShowMsg("Exported " + f.Profiles.Count + " profile(s) to " + Path.GetFileName(d.FileName));
                }
                catch (Exception ex) { Dlg.Info(this, "Could not export: " + ex.Message); }
            }
        }

        void ImportProfiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Import profiles";
                d.Filter = "Ctlr To Key profiles (*.ctkprofile;*.xml)|*.ctkprofile;*.xml|All files|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<Profile> got = new List<Profile>();
                foreach (string file in d.FileNames)
                {
                    try { got.AddRange(ProfileFile.Load(file)); }
                    catch (Exception ex) { Dlg.Info(this, Path.GetFileName(file) + ": " + ex.Message); }
                }
                if (got.Count == 0) return;
                Snapshot("import");
                Profile last = null;
                foreach (Profile p in got)
                {
                    p.Name = UniqueName(string.IsNullOrEmpty(p.Name) ? "Imported" : p.Name);
                    cfg.Profiles.Add(p);
                    last = p;
                }
                ReloadProfiles(last);
                ShowMsg("Imported " + got.Count + " profile(s).");
            }
        }

        void RefreshDevices()
        {
            Profile p = Sel;
            if (p == null) return;
            loading = true;
            List<DeviceInfo> list = Devices.List();
            if (!list.Any(d => d.Key == p.Device))
                list.Add(new DeviceInfo { Key = p.Device, Name = Devices.DisplayName(p.Device), Connected = false });
            cboDevice.Items.Clear();
            foreach (DeviceInfo d in list) cboDevice.Items.Add(d);
            cboDevice.SelectedIndex = list.FindIndex(d => d.Key == p.Device);
            loading = false;
            engine.ForceReconnect();
            lastLiveKey = null;
        }

        void DeviceChanged()
        {
            if (loading) return;
            DeviceInfo d = cboDevice.SelectedItem as DeviceInfo;
            Profile p = Sel;
            if (d == null || p == null || d.Key == p.Device) return;
            Snapshot("change controller");
            engine.ResetProfile(p);
            p.Device = d.Key;
            engine.UiDevice = p.Device;
            BuildRows();
            Save();
        }

        void BuildRows()
        {
            Profile p = Sel;
            lv.BeginUpdate();
            lv.Items.Clear();
            rows.Clear();
            if (p != null)
            {
                string[] names = Devices.ButtonNames(p.Device);
                foreach (string n in names)
                {
                    Mapping m = p.Mappings.FirstOrDefault(x => x.Buttons.Count == 1 && x.Buttons[0] == n);
                    AddRow(new List<string> { n }, m);
                }
                foreach (Mapping m in p.Mappings)
                    if (m.Buttons.Count > 1 || (m.Buttons.Count == 1 && !names.Contains(m.Buttons[0])))
                        AddRow(m.Buttons, m);
            }
            lv.EndUpdate();
            FitColumns();
            lastLiveKey = null;
        }

        void AddRow(List<string> buttons, Mapping m)
        {
            ListViewItem it = new ListViewItem(string.Join(" + ", buttons.ToArray()));
            it.SubItems.Add(m == null ? "None" : m.FunctionText());
            it.SubItems.Add(m == null ? "---" : m.ModeText());
            it.SubItems.Add(m == null ? "" : m.Comment);
            it.BackColor = theme.Field;
            it.ForeColor = theme.Text;
            RowInfo r = new RowInfo { Buttons = buttons, Map = m, Item = it };
            it.Tag = r;
            rows.Add(r);
            lv.Items.Add(it);
        }

        RowInfo SelectedRow()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return lv.SelectedItems[0].Tag as RowInfo;
        }

        void EditSelected()
        {
            RowInfo r = SelectedRow();
            if (r == null) { ShowMsg("Select a button in the list first."); return; }
            Mapping work = r.Map != null ? r.Map.Clone() : new Mapping { Buttons = new List<string>(r.Buttons) };
            OpenEditor(work, r.Map, "Edit button assignment - " + string.Join(" + ", r.Buttons.ToArray()));
        }

        void AddCombo()
        {
            OpenEditor(new Mapping(), null, "Add combination");
        }

        static bool SameSet(List<string> a, List<string> b)
        {
            return a.Count == b.Count && a.All(b.Contains);
        }

        void OpenEditor(Mapping work, Mapping original, string title)
        {
            Profile p = Sel;
            if (p == null) return;
            engine.Paused = true;
            engine.ReleaseAll();
            try
            {
                using (EditForm f = new EditForm(work, p.Device, cfg, theme, title, p.LongPressMs))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    Mapping res = f.Result;
                    Snapshot("edit " + res.TriggerText());
                    int idx = original != null ? p.Mappings.IndexOf(original) : -1;
                    if (original != null) p.Mappings.Remove(original);
                    p.Mappings.RemoveAll(x => SameSet(x.Buttons, res.Buttons));
                    if (!res.IsEmpty())
                    {
                        if (idx >= 0 && idx <= p.Mappings.Count) p.Mappings.Insert(idx, res);
                        else p.Mappings.Add(res);
                    }
                    Save();
                    BuildRows();
                    RowInfo sel = rows.FirstOrDefault(x => SameSet(x.Buttons, res.Buttons));
                    if (sel != null) { sel.Item.Selected = true; sel.Item.Focused = true; sel.Item.EnsureVisible(); }
                    ShowMsg("Saved: " + res.TriggerText() + "  →  " + res.FunctionText());
                }
            }
            finally
            {
                engine.Paused = false;
            }
        }

        void ClearSelected()
        {
            RowInfo r = SelectedRow();
            if (r == null || r.Map == null) return;
            int index = lv.SelectedIndices[0];
            Snapshot("clear " + r.Map.TriggerText());
            engine.ReleaseAll();
            Sel.Mappings.Remove(r.Map);
            Save();
            BuildRows();
            if (lv.Items.Count > 0)
            {
                ListViewItem it = lv.Items[Math.Min(index, lv.Items.Count - 1)];
                it.Selected = true; it.Focused = true;
            }
            ShowMsg("Assignment cleared (Ctrl+Z to undo).");
        }

        void UpdateLive()
        {
            Profile p = Sel;
            if (p == null) return;

            int on = cfg.Profiles.Count(x => x.Enabled);
            string stateKey = engine.Suspended + "|" + engine.LockBlocked + "|" + on + "|" + cfg.Profiles.Count;
            if (stateKey != lastStateKey)
            {
                lastStateKey = stateKey;
                if (engine.Suspended) { stState.Text = "OFF"; stState.ForeColor = theme.Off; }
                else if (engine.LockBlocked) { stState.Text = "Paused - waiting for " + cfg.LockPrograms.Trim(); stState.ForeColor = Color.FromArgb(200, 130, 20); }
                else { stState.Text = "ON  (" + on + " of " + cfg.Profiles.Count + " profiles)"; stState.ForeColor = on > 0 ? theme.On : theme.SubText; }
            }

            bool conn = engine.IsConnected(p.Device);
            HashSet<string> pr = engine.PressedFor(p.Device);
            string key = conn ? string.Join(" + ", OrderButtons(pr, p.Device)) : "";
            if (key == lastLiveKey) return;
            lastLiveKey = key;

            stDevice.Text = Devices.DisplayName(p.Device) + (conn ? "  • connected" : "  • not connected");
            stDevice.ForeColor = conn ? theme.Text : theme.Off;
            stPressed.Text = conn ? "Pressed: " + (key.Length == 0 ? "-" : key) : "Plug in the controller or pick another one above.";

            foreach (RowInfo r in rows)
            {
                bool lit = conn && r.Buttons.Count > 0 && r.Buttons.All(pr.Contains);
                if (lit == r.Lit) continue;
                r.Lit = lit;
                r.Item.BackColor = lit ? theme.Highlight : theme.Field;
            }
        }

        static string[] OrderButtons(HashSet<string> pr, string device)
        {
            List<string> names = Devices.ButtonNames(device).ToList();
            return pr.OrderBy(b => { int i = names.IndexOf(b); return i < 0 ? 999 : i; }).ToArray();
        }

        void ShowMsg(string s)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(ShowMsg), s); } catch (Exception) { }
                return;
            }
            stMsg.Text = s;
            msgTimer.Stop();
            msgTimer.Start();
        }

        void UpdateTitle()
        {
            string n = Sel != null ? Sel.Name : "";
            Text = "Ctlr To Key v" + Version + " : " + n + (engine.Suspended ? "   [OFF]" : "");
        }

        void SetTopMost(bool on)
        {
            cfg.AlwaysOnTop = on;
            TopMost = on;
            miTop.Checked = on;
            loading = true;
            chkTop.Checked = on;
            loading = false;
            Save();
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("CtlrToKey") != null;
            }
            catch (Exception) { return false; }
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("CtlrToKey", "\"" + Application.ExecutablePath + "\" /min");
                    else k.DeleteValue("CtlrToKey", false);
                }
            }
            catch (Exception ex) { Dlg.Info(this, "Could not change the startup setting: " + ex.Message); }
        }

        void Save()
        {
            try { cfg.Save(); }
            catch (Exception ex) { ShowMsg("Could not save settings: " + ex.Message); }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Dlg.Info(this, "Could not open " + url + "\n" + ex.Message); }
        }

        void OpenSettingsFolder()
        {
            Save();
            try { Process.Start("explorer.exe", "/select,\"" + AppConfig.CurrentPath + "\""); } catch (Exception) { }
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "1.  Pick a profile on the left. Right-click it to turn it on or off\n" +
                "     (several profiles can be on at once).\n" +
                "2.  Pick its controller at the top.\n" +
                "     Press buttons - the matching row lights up.\n" +
                "3.  Double-click a button row to assign a shortcut, program, or macro.\n" +
                "4.  Combos (e.g. LB + A): \"Add combination...\" and tick several buttons.\n" +
                "5.  Long press: in the edit window, open the \"Long press\" tab to give a\n" +
                "     button a second action when you hold it.\n\n" +
                "The ON/OFF button (or Ctrl+P, or your hotkey / controller combo from Options)\n" +
                "turns everything off without changing which profiles are on.\n\n" +
                "Ctrl+Z undoes your last change.",
                "How to use Ctlr To Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowMacroHelp()
        {
            MessageBox.Show(this,
                "A macro is a list of steps, one per line:\n\n" +
                "   key Win + R            press a key or shortcut\n" +
                "   wait 500                 wait half a second (milliseconds)\n" +
                "   type notepad            type some text\n" +
                "   key Enter\n" +
                "   run https://google.com   open a program, file, or website\n" +
                "   down Shift / up Shift   hold or release a key\n\n" +
                "Lines starting with # are ignored. Use the + buttons in the editor to add steps.\n" +
                "Turning everything OFF stops a running macro.",
                "Macro steps", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        void OnLoadForm(object sender, EventArgs e)
        {
            if (cfg.WinW > 200 && cfg.WinH > 150)
            {
                Rectangle r = new Rectangle(cfg.WinX, cfg.WinY, cfg.WinW, cfg.WinH);
                if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(r.X + 40, r.Y + 10))))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = r;
                }
            }
            ApplyTheme();
            FitColumns();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.TitleBar(this, theme.Dark);
            FitColumns();
        }

        void OnResizeForm(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && cfg.MinimizeToTray)
            {
                Hide();
                if (!trayTipShown)
                {
                    trayTipShown = true;
                    tray.ShowBalloonTip(2500, "Ctlr To Key", "Still running in the tray. Click the icon to open.", ToolTipIcon.Info);
                }
            }
        }

        void OnClosingForm(object sender, FormClosingEventArgs e)
        {
            if (!reallyExit && cfg.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            timer.Stop();
            engine.CancelMacros();
            engine.ReleaseAll();
            if (IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
            if (Visible)
            {
                Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                cfg.WinX = b.X; cfg.WinY = b.Y; cfg.WinW = b.Width; cfg.WinH = b.Height;
            }
            Save();
            tray.Visible = false;
            tray.Dispose();
        }
    }
}
