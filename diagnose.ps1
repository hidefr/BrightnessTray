# Brightness Tray Diagnostics
# Inspects connected displays, control methods (DDC/CI or WMI), brightness, and input sources

$script = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Management;

public class Diagnostics {
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
    public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MONITORINFOEX {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice = "";
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PHYSICAL_MONITOR {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, out uint pvct, out uint pdwCurrentValue, out uint pdwMaximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, out uint pdwCapabilitiesStringLengthInCharacters);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, StringBuilder pszASCIICapabilitiesString, uint dwCapabilitiesStringLengthInCharacters);

    public static string GetInputName(byte code) {
        switch (code) {
            case 0x01: return "VGA 1";
            case 0x02: return "VGA 2";
            case 0x03: return "DVI 1";
            case 0x04: return "DVI 2";
            case 0x05: return "Composite 1";
            case 0x06: return "Composite 2";
            case 0x07: return "S-Video 1";
            case 0x08: return "S-Video 2";
            case 0x09: return "Tuner 1";
            case 0x0A: return "Tuner 2";
            case 0x0B: return "Tuner 3";
            case 0x0C: return "Component 1";
            case 0x0D: return "Component 2";
            case 0x0E: return "Component 3";
            case 0x0F: return "DisplayPort 1";
            case 0x10: return "DisplayPort 2";
            case 0x11: return "HDMI 1";
            case 0x12: return "HDMI 2";
            case 0x13: return "HDMI 3";
            case 0x1B: return "USB-C";
            default: return string.Format("Input 0x{0:X2}", code);
        }
    }

    public static void Run() {
        Console.WriteLine("=================================================");
        Console.WriteLine("           Brightness Tray Diagnostics           ");
        Console.WriteLine("=================================================");
        Console.WriteLine();

        // 1. Check WMI for laptop panels
        Console.WriteLine("--- Checking WMI (Internal Displays) ---");
        try {
            using (ManagementClass mc = new ManagementClass("root\\wmi", "WmiMonitorBrightness", null)) {
                using (ManagementObjectCollection moc = mc.GetInstances()) {
                    int count = 0;
                    foreach (ManagementObject mo in moc) {
                        count++;
                        string inst = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : "(null)";
                        int curr = mo["CurrentBrightness"] != null ? Convert.ToInt32(mo["CurrentBrightness"]) : -1;
                        Console.WriteLine("  WMI Display {0}:", count);
                        Console.WriteLine("    Instance: {0}", inst);
                        Console.WriteLine("    Brightness: {0}%", curr);
                    }
                    if (count == 0) {
                        Console.WriteLine("  No WMI brightness monitors found (normal for desktop systems).");
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine("  WMI Query error: {0}", ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("--- Checking Desktop & DDC/CI Monitors ---");
        int monIndex = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) {
            monIndex++;
            MONITORINFOEX info = new MONITORINFOEX();
            GetMonitorInfo(hMonitor, info);
            Console.WriteLine("Display {0} ({1}):", monIndex, info.szDevice);
            Console.WriteLine("  Bounds: [{0}, {1}, {2}, {3}]", info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);

            uint numPhysical = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out numPhysical) || numPhysical == 0) {
                Console.WriteLine("  Control method: Not supported (No DDC/CI physical monitors found)");
                return true;
            }

            PHYSICAL_MONITOR[] phys = new PHYSICAL_MONITOR[numPhysical];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, numPhysical, phys)) {
                Console.WriteLine("  Failed to get physical monitor handles.");
                return true;
            }

            for (int i = 0; i < numPhysical; i++) {
                Console.WriteLine("  Physical Monitor [{0}]: {1}", i, phys[i].szPhysicalMonitorDescription);
                uint pvct = 0, current = 0, max = 0;
                bool ddcOk = false;
                for (int attempt = 0; attempt < 3; attempt++) {
                    if (GetVCPFeatureAndVCPFeatureReply(phys[i].hPhysicalMonitor, 0x10, out pvct, out current, out max)) {
                        ddcOk = true;
                        break;
                    }
                    System.Threading.Thread.Sleep(40);
                }

                if (ddcOk) {
                    Console.WriteLine("    Control Method: DDC/CI (Supported)");
                    int pct = max > 0 ? (int)Math.Round((double)current * 100.0 / max) : (int)current;
                    Console.WriteLine("    Brightness: {0}% (raw: {1}/{2})", pct, current, max);

                    // Check input VCP 0x60
                    uint curInput = 0, maxInput = 0;
                    if (GetVCPFeatureAndVCPFeatureReply(phys[i].hPhysicalMonitor, 0x60, out pvct, out curInput, out maxInput)) {
                        byte code = (byte)(curInput & 0xFF);
                        Console.WriteLine("    Current Input: 0x{0:X2} ({1})", code, GetInputName(code));
                    } else {
                        Console.WriteLine("    Current Input: (Unable to read VCP 0x60)");
                    }

                    // Query capabilities
                    uint capLen = 0;
                    bool capOk = false;
                    for (int capAtt = 0; capAtt < 3; capAtt++) {
                        if (GetCapabilitiesStringLength(phys[i].hPhysicalMonitor, out capLen) && capLen > 0) {
                            StringBuilder sb = new StringBuilder((int)capLen);
                            if (CapabilitiesRequestAndCapabilitiesReply(phys[i].hPhysicalMonitor, sb, capLen)) {
                                string capStr = sb.ToString();
                                Console.WriteLine("    Capabilities String Length: {0}", capStr.Length);
                                Console.WriteLine("    Raw Capabilities: {0}", capStr);

                                Match m = Regex.Match(capStr, @"60\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
                                if (m.Success) {
                                    string vals = m.Groups[1].Value.Trim();
                                    string[] tokens = vals.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                    List<string> parsedInputs = new List<string>();
                                    foreach (string t in tokens) {
                                        try {
                                            byte c = Convert.ToByte(t, 16);
                                            parsedInputs.Add(string.Format("0x{0:X2} ({1})", c, GetInputName(c)));
                                        } catch { }
                                    }
                                    Console.WriteLine("    Supported Inputs parsed: {0}", string.Join(", ", parsedInputs.ToArray()));
                                } else {
                                    Console.WriteLine("    Input feature 60() not found in capabilities string.");
                                }
                                capOk = true;
                                break;
                            }
                        }
                        System.Threading.Thread.Sleep(40);
                    }

                    if (!capOk) {
                        Console.WriteLine("    Capabilities string could not be retrieved.");
                    }
                } else {
                    Console.WriteLine("    Control Method: Not supported (DDC/CI luminance command failed)");
                }
            }

            DestroyPhysicalMonitors(numPhysical, phys);
            Console.WriteLine();
            return true;
        }, IntPtr.Zero);

        Console.WriteLine("Diagnostics complete.");
    }
}
'@

Add-Type -TypeDefinition $script -ReferencedAssemblies System.Management
[Diagnostics]::Run()
