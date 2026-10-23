using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrightnessTray
{
    public class Settings
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "BrightnessTray";

        public bool Linked { get; set; }
        public bool ShowAllInputs { get; set; }
        public int ScrollStep { get; set; }

        private readonly Dictionary<string, HashSet<byte>> _connectedInputs = new Dictionary<string, HashSet<byte>>(StringComparer.OrdinalIgnoreCase);

        public Settings()
        {
            Linked = false;
            ShowAllInputs = false;
            ScrollStep = 5;
        }

        public bool IsInputConnected(string monitorId, byte inputCode, bool isActiveInput)
        {
            if (isActiveInput) return true;
            if (string.IsNullOrEmpty(monitorId)) return false;

            HashSet<byte> set;
            if (_connectedInputs.TryGetValue(monitorId, out set))
            {
                return set.Contains(inputCode);
            }
            return false;
        }

        public void SetInputConnected(string monitorId, byte inputCode, bool connected)
        {
            if (string.IsNullOrEmpty(monitorId)) return;

            HashSet<byte> set;
            if (!_connectedInputs.TryGetValue(monitorId, out set))
            {
                set = new HashSet<byte>();
                _connectedInputs[monitorId] = set;
            }

            if (connected)
            {
                set.Add(inputCode);
            }
            else
            {
                set.Remove(inputCode);
            }
            Save();
        }

        public void EnsureActiveInputConnected(string monitorId, byte? activeCode)
        {
            if (string.IsNullOrEmpty(monitorId) || !activeCode.HasValue) return;

            HashSet<byte> set;
            if (!_connectedInputs.TryGetValue(monitorId, out set))
            {
                set = new HashSet<byte>();
                _connectedInputs[monitorId] = set;
            }

            if (!set.Contains(activeCode.Value))
            {
                set.Add(activeCode.Value);
                Save();
            }
        }

        public static string GetSettingsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "BrightnessTray");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetSettingsDirectory(), "settings.ini");
        }

        public static string GetErrorLogFilePath()
        {
            return Path.Combine(GetSettingsDirectory(), "error.log");
        }

        public static Settings Load()
        {
            Settings settings = new Settings();
            string path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                            continue;

                        int idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            string key = line.Substring(0, idx).Trim();
                            string val = line.Substring(idx + 1).Trim();

                            if (string.Equals(key, "Linked", StringComparison.OrdinalIgnoreCase))
                            {
                                settings.Linked = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (string.Equals(key, "ShowAllInputs", StringComparison.OrdinalIgnoreCase))
                            {
                                settings.ShowAllInputs = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (string.Equals(key, "ScrollStep", StringComparison.OrdinalIgnoreCase))
                            {
                                int step;
                                if (int.TryParse(val, out step))
                                {
                                    if (step < 1) step = 1;
                                    if (step > 25) step = 25;
                                    settings.ScrollStep = step;
                                }
                            }
                            else if (key.StartsWith("ConnectedInputs_", StringComparison.OrdinalIgnoreCase))
                            {
                                string monId = key.Substring("ConnectedInputs_".Length);
                                string[] parts = val.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                HashSet<byte> set = new HashSet<byte>();
                                foreach (string p in parts)
                                {
                                    byte b;
                                    if (byte.TryParse(p, out b))
                                    {
                                        set.Add(b);
                                    }
                                    else if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try { set.Add(Convert.ToByte(p.Substring(2), 16)); } catch { }
                                    }
                                }
                                settings._connectedInputs[monId] = set;
                            }
                        }
                    }
                }
                catch
                {
                    // Fall back to defaults on read error
                }
            }
            else
            {
                // Create initial settings file with defaults
                settings.Save();
            }

            // Also check and normalize the Start with Windows registry key
            settings.NormalizeStartupRegistry();

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsFilePath();
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Linked={0}\r\nShowAllInputs={1}\r\nScrollStep={2}\r\n",
                    Linked ? "1" : "0",
                    ShowAllInputs ? "1" : "0",
                    ScrollStep
                );
                foreach (KeyValuePair<string, HashSet<byte>> kvp in _connectedInputs)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        List<string> codes = new List<string>();
                        foreach (byte b in kvp.Value)
                        {
                            codes.Add(b.ToString());
                        }
                        sb.AppendFormat("ConnectedInputs_{0}={1}\r\n", kvp.Key, string.Join(",", codes.ToArray()));
                    }
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                // Silently ignore write failures
            }
        }

        public bool StartWithWindows
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue(RunValueName);
                            return val != null;
                        }
                    }
                }
                catch { }
                return false;
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                    {
                        if (key != null)
                        {
                            if (value)
                            {
                                string exePath = Application.ExecutablePath;
                                string command = string.Format("\"{0}\" --startup", exePath);
                                key.SetValue(RunValueName, command, RegistryValueKind.String);
                            }
                            else
                            {
                                key.DeleteValue(RunValueName, false);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public void NormalizeStartupRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(RunValueName);
                        if (val != null)
                        {
                            string currentCmd = val.ToString();
                            string exePath = Application.ExecutablePath;
                            string desiredCmd = string.Format("\"{0}\" --startup", exePath);
                            if (!string.Equals(currentCmd, desiredCmd, StringComparison.OrdinalIgnoreCase))
                            {
                                key.SetValue(RunValueName, desiredCmd, RegistryValueKind.String);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
