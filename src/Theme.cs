using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CtlrToKey
{
    public class Theme
    {
        public string Name;
        public Color Select, SelectText, On, Off;
        public Color Back, Panel, Field, Text, SubText, Border, Button, ButtonHover, Highlight, Header, TabInactive, Accent;
        public bool Flat, Dark;

        public static readonly string[] Names = { "Classic", "Dark" };

        public static Theme Get(string name)
        {
            Theme t = new Theme();
            if (name == "Dark")
            {
                t.Name = "Dark";
                t.Back = Color.FromArgb(32, 32, 32); t.Panel = Color.FromArgb(43, 43, 43); t.Field = Color.FromArgb(28, 28, 28);
                t.Text = Color.FromArgb(232, 232, 232); t.SubText = Color.FromArgb(150, 150, 150); t.Border = Color.FromArgb(72, 72, 72);
                t.Button = Color.FromArgb(55, 55, 55); t.ButtonHover = Color.FromArgb(72, 72, 72);
                t.Highlight = Color.FromArgb(28, 84, 140); t.Header = Color.FromArgb(45, 45, 45);
                t.TabInactive = Color.FromArgb(36, 36, 36); t.Accent = Color.FromArgb(96, 170, 255);
                t.Select = Color.FromArgb(40, 92, 150); t.SelectText = t.Text;
                t.On = Color.FromArgb(34, 150, 80); t.Off = Color.FromArgb(190, 55, 50);
                t.Flat = true; t.Dark = true;
            }
            else
            {
                t.Name = "Classic";
                t.Back = SystemColors.Control; t.Panel = SystemColors.Control; t.Field = SystemColors.Window;
                t.Text = SystemColors.ControlText; t.SubText = SystemColors.GrayText; t.Border = SystemColors.ControlDark;
                t.Button = SystemColors.Control; t.ButtonHover = SystemColors.ControlLight;
                t.Highlight = Color.FromArgb(255, 236, 160); t.Header = SystemColors.Control;
                t.TabInactive = Color.FromArgb(220, 220, 220); t.Accent = SystemColors.Highlight;
                t.Select = SystemColors.Highlight; t.SelectText = SystemColors.HighlightText;
                t.On = Color.FromArgb(28, 140, 70); t.Off = Color.FromArgb(200, 60, 55);
            }
            return t;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void TitleBar(Form f, bool dark)
        {
            if (!f.IsHandleCreated) return;
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch (Exception) { }
        }

        static void Scrollbars(Control c, bool dark)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); } catch (Exception) { }
        }

        public void Apply(Form f)
        {
            f.BackColor = Back;
            f.ForeColor = Text;
            TitleBar(f, Dark);
            foreach (Control c in f.Controls) ApplyTo(c);
        }

        public void ApplyTo(Control c)
        {
            string tag = c.Tag as string;
            if (c is Button)
            {
                Button b = (Button)c;
                if (tag == "tab") return;
                if (Flat)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    b.BackColor = Button;
                    b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = Highlight;
                }
                else
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.BackColor = SystemColors.Control;
                    b.ForeColor = SystemColors.ControlText;
                    b.UseVisualStyleBackColor = true;
                }
            }
            else if (c is ListView)
            {
                ListView lv = (ListView)c;
                lv.BackColor = Field;
                lv.ForeColor = Text;
                lv.OwnerDraw = Flat;
                Scrollbars(c, Dark);
                if (lv.IsHandleCreated)
                {
                    try
                    {
                        IntPtr hdr = SendMessage(lv.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
                        if (hdr != IntPtr.Zero) SetWindowTheme(hdr, Dark ? "DarkMode_ItemsView" : "ItemsView", null);
                    }
                    catch (Exception) { }
                }
                lv.Invalidate();
            }
            else if (c is ListBox || c is TextBoxBase || c is NumericUpDown)
            {
                c.BackColor = Field;
                c.ForeColor = Text;
                if (c is ListBox) Scrollbars(c, Dark);
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = Flat ? FlatStyle.Flat : FlatStyle.Standard;
                cb.BackColor = Field;
                cb.ForeColor = Text;
            }
            else if (c is ToolStrip)
            {
                ToolStrip ts = (ToolStrip)c;
                if (Flat) ts.Renderer = new ThemeRenderer(this);
                else ts.RenderMode = ToolStripRenderMode.System;
                ts.BackColor = Back;
                ts.ForeColor = Text;
            }
            else if (c is Label)
            {
                c.ForeColor = tag == "hint" ? SubText : Text;
            }
            else if (c is CheckBox || c is RadioButton || c is GroupBox)
            {
                c.ForeColor = Text;
            }
            else if (c is Panel)
            {
                if (tag == "panel") c.BackColor = Panel;
                else if (tag == "back") c.BackColor = Back;
                if (((Panel)c).AutoScroll) Scrollbars(c, Dark);
            }
            foreach (Control ch in c.Controls) ApplyTo(ch);
        }

        public void StyleMenu(ToolStrip ts)
        {
            if (Flat) ts.Renderer = new ThemeRenderer(this);
            else ts.RenderMode = ToolStripRenderMode.System;
            ts.BackColor = Flat ? Panel : SystemColors.Menu;
            ts.ForeColor = Text;
        }
    }

    class ThemeColors : ProfessionalColorTable
    {
        Theme t;
        public ThemeColors(Theme t) { this.t = t; UseSystemColors = false; }
        public override Color MenuStripGradientBegin { get { return t.Back; } }
        public override Color MenuStripGradientEnd { get { return t.Back; } }
        public override Color ToolStripDropDownBackground { get { return t.Panel; } }
        public override Color ImageMarginGradientBegin { get { return t.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return t.Panel; } }
        public override Color ImageMarginGradientEnd { get { return t.Panel; } }
        public override Color MenuItemSelected { get { return t.Highlight; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.Highlight; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.Highlight; } }
        public override Color MenuItemPressedGradientBegin { get { return t.Panel; } }
        public override Color MenuItemPressedGradientMiddle { get { return t.Panel; } }
        public override Color MenuItemPressedGradientEnd { get { return t.Panel; } }
        public override Color MenuItemBorder { get { return t.Highlight; } }
        public override Color MenuBorder { get { return t.Border; } }
        public override Color SeparatorDark { get { return t.Border; } }
        public override Color SeparatorLight { get { return t.Panel; } }
        public override Color StatusStripGradientBegin { get { return t.Back; } }
        public override Color StatusStripGradientEnd { get { return t.Back; } }
        public override Color CheckBackground { get { return t.Highlight; } }
        public override Color CheckSelectedBackground { get { return t.Highlight; } }
        public override Color CheckPressedBackground { get { return t.Highlight; } }
        public override Color ToolStripBorder { get { return t.Back; } }
    }

    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        Theme t;
        public ThemeRenderer(Theme t) : base(new ThemeColors(t)) { this.t = t; RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? t.Text : t.SubText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = t.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            using (SolidBrush b = new SolidBrush(t.Highlight)) e.Graphics.FillRectangle(b, r);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
