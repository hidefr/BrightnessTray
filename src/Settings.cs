using System;
using System.IO;
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

        public Settings()
        {
            Linked = false;
            ShowAllInputs = false;
            ScrollStep = 5;
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
                string content = string.Format(
                    "Linked={0}\r\nShowAllInputs={1}\r\nScrollStep={2}\r\n",
                    Linked ? "1" : "0",
                    ShowAllInputs ? "1" : "0",
                    ScrollStep
                );
                File.WriteAllText(path, content);
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
