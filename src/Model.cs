using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace CtlrToKey
{
    public class ActionDef
    {
        public string Action = "Keys";
        public string Keys = "";
        public string RunPath = "";
        public string RunArgs = "";
        public string Mode = "Tap";
        public string Macro = "";

        public ActionDef Clone()
        {
            return (ActionDef)MemberwiseClone();
        }

        public bool IsEmpty()
        {
            if (Action == "Keys") return string.IsNullOrEmpty(Keys);
            if (Action == "Run") return string.IsNullOrEmpty(RunPath);
            if (Action == "Macro") return string.IsNullOrEmpty((Macro ?? "").Trim());
            return false;
        }

        public string Text()
        {
            if (Action == "None") return "Disabled";
            if (Action == "Macro")
            {
                int n = MacroRunner.CountSteps(Macro);
                return n == 0 ? "None" : "Macro (" + n + " step" + (n == 1 ? "" : "s") + ")";
            }
            if (Action == "Run")
            {
                if (string.IsNullOrEmpty(RunPath)) return "None";
                string name = RunPath;
                try
                {
                    if (!RunPath.Contains("://")) name = Path.GetFileName(RunPath.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) name = RunPath;
                }
                catch (Exception) { }
                return "Run: " + name;
            }
            string k = KeyUtil.Pretty(Keys);
            return k.Length == 0 ? "None" : k;
        }

        public string ModeText()
        {
            if (Action != "Keys" || string.IsNullOrEmpty(Keys)) return "---";
            return Mode;
        }
    }

    public class Mapping
    {
        public List<string> Buttons = new List<string>();
        public string Action = "Keys";
        public string Keys = "";
        public string RunPath = "";
        public string RunArgs = "";
        public string Mode = "Tap";
        public string Macro = "";
        public string Comment = "";
        public ActionDef Long;

        [XmlIgnore] public bool Active;
        [XmlIgnore] public bool Pending;
        [XmlIgnore] public bool Waiting;
        [XmlIgnore] public long PressStart;
        [XmlIgnore] public long PendingUntil;
        [XmlIgnore] public List<string> HeldSet;
        [XmlIgnore] public long NextRepeat;
        [XmlIgnore] public ActionDef Cur;
        [XmlIgnore] public List<System.Windows.Forms.Keys> HeldKeys;
        [XmlIgnore] public int MacroBusy;

        [XmlIgnore]
        public ActionDef Main
        {
            get
            {
                ActionDef d = new ActionDef();
                d.Action = Action; d.Keys = Keys; d.RunPath = RunPath; d.RunArgs = RunArgs; d.Mode = Mode; d.Macro = Macro;
                return d;
            }
            set
            {
                Action = value.Action; Keys = value.Keys; RunPath = value.RunPath; RunArgs = value.RunArgs;
                Mode = value.Mode; Macro = value.Macro;
            }
        }

        public Mapping Clone()
        {
            Mapping m = new Mapping();
            m.Buttons = new List<string>(Buttons);
            m.Main = Main;
            m.Comment = Comment;
            m.Long = Long == null ? null : Long.Clone();
            return m;
        }

        public string TriggerText()
        {
            return string.Join(" + ", Buttons.ToArray());
        }

        public bool IsEmpty()
        {
            return Main.IsEmpty() && Action != "None" && Long == null && string.IsNullOrEmpty(Comment);
        }

        public string FunctionText()
        {
            string s = Main.Text();
            if (Long != null) s = (s == "None" ? "-" : s) + "   |   Hold: " + Long.Text();
            return s;
        }

        public string ModeText()
        {
            if (Long != null) return "Short/Long";
            return Main.ModeText();
        }
    }

    public class Profile
    {
        public string Name = "Profile 1";
        public string Device = "XInput:0";
        public bool Enabled = true;
        public int LongPressMs = 500;
        public List<Mapping> Mappings = new List<Mapping>();

        public Profile Clone(string newName)
        {
            Profile p = new Profile();
            p.Name = newName;
            p.Device = Device;
            p.Enabled = Enabled;
            p.LongPressMs = LongPressMs;
            p.Mappings = Mappings.Select(m => m.Clone()).ToList();
            return p;
        }
    }

    public class ProfileFile
    {
        public string App = "Ctlr To Key";
        public List<Profile> Profiles = new List<Profile>();

        public void Save(string path)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                new XmlSerializer(typeof(ProfileFile)).Serialize(ms, this);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        public static List<Profile> Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            Type[] types = { typeof(ProfileFile), typeof(AppConfig), typeof(Profile) };
            foreach (Type t in types)
            {
                try
                {
                    XmlSerializer xs = new XmlSerializer(t);
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        if (!xs.CanDeserialize(System.Xml.XmlReader.Create(ms))) continue;
                    }
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        object o = xs.Deserialize(ms);
                        if (o is ProfileFile) return ((ProfileFile)o).Profiles;
                        if (o is AppConfig) return ((AppConfig)o).Profiles;
                        if (o is Profile) return new List<Profile> { (Profile)o };
                    }
                }
                catch (Exception) { }
            }
            throw new InvalidDataException("This file is not a Ctlr To Key profile.");
        }
    }

    public class AppConfig
    {
        public List<Profile> Profiles = new List<Profile>();
        public string ActiveProfile = "";
        public string Theme = "Classic";
        public int Deadzone = 50;
        public int TriggerThreshold = 60;
        public int ComboWindow = 80;
        public int RepeatDelay = 400;
        public int RepeatInterval = 60;
        public int PollInterval = 10;
        public bool ScanCodes = false;
        public bool MinimizeToTray = true;
        public bool CloseToTray = false;
        public bool StartMinimized = false;
        public bool AlwaysOnTop = false;
        public bool HotkeyEnabled = true;
        public string Hotkey = "ControlKey+Menu+J";
        public bool ComboToggleEnabled = false;
        public string ToggleCombo = "Back+Start";
        public int ComboHoldMs = 800;
        public bool SoundOnToggle = true;
        public bool LockEnabled = false;
        public string LockPrograms = "";
        public int WinX = int.MinValue, WinY = int.MinValue, WinW = 0, WinH = 0;

        static string currentPath;

        public static string ExeDirPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CtlrToKey.xml"); }
        }

        public static string AppDataPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CtlrToKey", "CtlrToKey.xml");
            }
        }

        public static string CurrentPath { get { return currentPath ?? ExeDirPath; } }

        public List<string> ToggleComboButtons()
        {
            return (ToggleCombo ?? "").Split('+').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        }

        public static AppConfig Load()
        {
            string p = File.Exists(ExeDirPath) ? ExeDirPath : (File.Exists(AppDataPath) ? AppDataPath : ExeDirPath);
            currentPath = p;
            AppConfig c = null;
            if (File.Exists(p))
            {
                try
                {
                    using (FileStream fs = File.OpenRead(p))
                        c = (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(fs);
                }
                catch (Exception)
                {
                    try { File.Copy(p, p + ".bad", true); } catch (Exception) { }
                    c = null;
                }
            }
            if (c == null) c = new AppConfig();
            if (c.Profiles.Count == 0) c.Profiles.Add(new Profile());
            return c;
        }

        public void Save()
        {
            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            {
                new XmlSerializer(typeof(AppConfig)).Serialize(ms, this);
                data = ms.ToArray();
            }
            try
            {
                File.WriteAllBytes(CurrentPath, data);
            }
            catch (Exception)
            {
                currentPath = AppDataPath;
                Directory.CreateDirectory(Path.GetDirectoryName(currentPath));
                File.WriteAllBytes(currentPath, data);
            }
        }
    }
}
