using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BrightnessTray
{
    public enum ControlMethod
    {
        BuiltIn,
        DdcCi,
        Unsupported
    }

    public static class InputSource
    {
        public static readonly byte[] CommonInputs = new byte[]
        {
            0x01, // VGA 1
            0x03, // DVI 1
            0x04, // DVI 2
            0x0F, // DisplayPort 1
            0x10, // DisplayPort 2
            0x11, // HDMI 1
            0x12, // HDMI 2
            0x13, // HDMI 3
            0x1B  // USB-C
        };

        public static readonly byte[] DefaultInputs = new byte[]
        {
            0x0F, // DisplayPort 1
            0x10, // DisplayPort 2
            0x11, // HDMI 1
            0x12, // HDMI 2
            0x1B  // USB-C
        };

        public static string GetInputName(byte code)
        {
            switch (code)
            {
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
    }

    public class DisplayMonitor : IDisposable
    {
        private readonly object _ioLock = new object();
        private volatile bool _disposed = false;
        private int? _pendingBrightness;
        private bool _writeWorkerRunning = false;

        public string Id { get; set; }
        public string Name { get; set; }
        public string GdiDeviceName { get; set; }
        public string PnpInstanceId { get; set; }
        public ControlMethod Method { get; set; }
        public IntPtr PhysicalMonitorHandle { get; set; }
        public string WmiInstanceName { get; set; }
        public Rectangle Bounds { get; set; }
        public int MaxVcpBrightness { get; set; }

        public int Brightness { get; set; }
        public byte? CurrentInput { get; set; }
        public List<byte> CapabilitiesInputs { get; private set; }
        public bool CapabilitiesLoaded { get; private set; }
        public bool IsReadingCapabilities { get; set; }

        public DisplayMonitor()
        {
            MaxVcpBrightness = 100;
            CapabilitiesInputs = new List<byte>();
            Brightness = 50;
        }

        public string MethodTag
        {
            get
            {
                switch (Method)
                {
                    case ControlMethod.BuiltIn: return "Built-in";
                    case ControlMethod.DdcCi: return "DDC/CI";
                    default: return "Not supported";
                }
            }
        }

        public List<byte> GetEffectiveInputs(bool showAllCommon)
        {
            List<byte> list = new List<byte>();
            if (showAllCommon)
            {
                list.AddRange(InputSource.CommonInputs);
            }
            else if (CapabilitiesInputs.Count > 0)
            {
                list.AddRange(CapabilitiesInputs);
            }
            else
            {
                list.AddRange(InputSource.DefaultInputs);
            }

            if (CurrentInput.HasValue && !list.Contains(CurrentInput.Value))
            {
                list.Add(CurrentInput.Value);
            }

            return list;
        }

        public void SetBrightnessAsync(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            Brightness = percent;

            lock (_ioLock)
            {
                _pendingBrightness = percent;
                if (!_writeWorkerRunning && !_disposed)
                {
                    _writeWorkerRunning = true;
                    ThreadPool.QueueUserWorkItem(WriteBrightnessWorker);
                }
            }
        }

        private void WriteBrightnessWorker(object state)
        {
            while (!_disposed)
            {
                int target;
                lock (_ioLock)
                {
                    if (_pendingBrightness.HasValue)
                    {
                        target = _pendingBrightness.Value;
                        _pendingBrightness = null;
                    }
                    else
                    {
                        _writeWorkerRunning = false;
                        break;
                    }
                }

                WriteHardwareBrightness(target);
            }
        }

        private void WriteHardwareBrightness(int percent)
        {
            lock (_ioLock)
            {
                if (_disposed) return;

                if (Method == ControlMethod.DdcCi && PhysicalMonitorHandle != IntPtr.Zero)
                {
                    uint max = MaxVcpBrightness > 0 ? (uint)MaxVcpBrightness : 100;
                    uint val = (uint)Math.Round((double)percent * max / 100.0);
                    Native.SetVCPFeature(PhysicalMonitorHandle, 0x10, val);
                }
                else if (Method == ControlMethod.BuiltIn && !string.IsNullOrEmpty(WmiInstanceName))
                {
                    WmiBrightness.SetBrightness(WmiInstanceName, percent);
                }
            }
        }

        public void RefreshHardwareBrightness()
        {
            lock (_ioLock)
            {
                if (_disposed) return;

                if (Method == ControlMethod.DdcCi && PhysicalMonitorHandle != IntPtr.Zero)
                {
                    uint pvct, current, max;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (Native.GetVCPFeatureAndVCPFeatureReply(PhysicalMonitorHandle, 0x10, out pvct, out current, out max))
                        {
                            if (max > 0)
                            {
                                MaxVcpBrightness = (int)max;
                                Brightness = (int)Math.Round((double)current * 100.0 / max);
                            }
                            else
                            {
                                Brightness = (int)current;
                            }
                            break;
                        }
                        Thread.Sleep(40);
                    }

                    // Also refresh active input VCP 0x60
                    uint inPvct, inCur, inMax;
                    if (Native.GetVCPFeatureAndVCPFeatureReply(PhysicalMonitorHandle, 0x60, out inPvct, out inCur, out inMax))
                    {
                        CurrentInput = (byte)(inCur & 0xFF);
                    }
                }
                else if (Method == ControlMethod.BuiltIn && !string.IsNullOrEmpty(WmiInstanceName))
                {
                    int curr = WmiBrightness.GetBrightness(WmiInstanceName);
                    if (curr >= 0)
                    {
                        Brightness = curr;
                    }
                }
            }
        }

        public void RefreshCapabilities()
        {
            if (Method != ControlMethod.DdcCi || PhysicalMonitorHandle == IntPtr.Zero)
                return;

            lock (_ioLock)
            {
                if (_disposed) return;
                IsReadingCapabilities = true;

                // 1. Read current input VCP 0x60
                uint pvct, curInput, maxInput;
                if (Native.GetVCPFeatureAndVCPFeatureReply(PhysicalMonitorHandle, 0x60, out pvct, out curInput, out maxInput))
                {
                    CurrentInput = (byte)(curInput & 0xFF);
                }

                // 2. Fetch capabilities string
                uint capLen = 0;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (_disposed) break;
                    if (Native.GetCapabilitiesStringLength(PhysicalMonitorHandle, out capLen) && capLen > 0)
                    {
                        StringBuilder sb = new StringBuilder((int)capLen);
                        if (Native.CapabilitiesRequestAndCapabilitiesReply(PhysicalMonitorHandle, sb, capLen))
                        {
                            string capStr = sb.ToString();
                            ParseCapabilities(capStr);
                            CapabilitiesLoaded = true;
                            break;
                        }
                    }
                    Thread.Sleep(40);
                }

                IsReadingCapabilities = false;
            }
        }

        private void ParseCapabilities(string capStr)
        {
            if (string.IsNullOrEmpty(capStr)) return;

            Match m = Regex.Match(capStr, @"60\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string vals = m.Groups[1].Value.Trim();
                string[] tokens = vals.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> list = new List<byte>();
                foreach (string t in tokens)
                {
                    try
                    {
                        byte code = Convert.ToByte(t, 16);
                        if (!list.Contains(code))
                        {
                            list.Add(code);
                        }
                    }
                    catch { }
                }
                if (list.Count > 0)
                {
                    CapabilitiesInputs = list;
                }
            }
        }

        public void SetInputAsync(byte inputCode, Action<bool> onCompleted)
        {
            if (Method != ControlMethod.DdcCi || PhysicalMonitorHandle == IntPtr.Zero)
                return;

            CurrentInput = inputCode;

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool success = false;
                int err = 0;
                lock (_ioLock)
                {
                    if (!_disposed && PhysicalMonitorHandle != IntPtr.Zero)
                    {
                        uint code = (uint)inputCode;

                        // Try setting VCP 0x60 (single byte code)
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            if (Native.SetVCPFeature(PhysicalMonitorHandle, 0x60, code))
                            {
                                success = true;
                                break;
                            }
                            err = Marshal.GetLastWin32Error();
                            Thread.Sleep(50);
                        }

                        // If failed, attempt to refresh physical monitor handle
                        if (!success && !string.IsNullOrEmpty(GdiDeviceName))
                        {
                            IntPtr freshHandle = IntPtr.Zero;
                            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref Native.RECT rc, IntPtr data)
                            {
                                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                                if (Native.GetMonitorInfo(hMon, mi) && string.Equals(mi.szDevice, GdiDeviceName, StringComparison.OrdinalIgnoreCase))
                                {
                                    uint num = 0;
                                    if (Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out num) && num > 0)
                                    {
                                        Native.PHYSICAL_MONITOR[] phys = new Native.PHYSICAL_MONITOR[num];
                                        if (Native.GetPhysicalMonitorsFromHMONITOR(hMon, num, phys))
                                        {
                                            freshHandle = phys[0].hPhysicalMonitor;
                                            for (int j = 1; j < num; j++)
                                            {
                                                Native.DestroyPhysicalMonitor(phys[j].hPhysicalMonitor);
                                            }
                                        }
                                    }
                                    return false;
                                }
                                return true;
                            }, IntPtr.Zero);

                            if (freshHandle != IntPtr.Zero)
                            {
                                if (PhysicalMonitorHandle != IntPtr.Zero)
                                {
                                    Native.DestroyPhysicalMonitor(PhysicalMonitorHandle);
                                }
                                PhysicalMonitorHandle = freshHandle;

                                for (int attempt = 0; attempt < 2; attempt++)
                                {
                                    if (Native.SetVCPFeature(PhysicalMonitorHandle, 0x60, code))
                                    {
                                        success = true;
                                        break;
                                    }
                                    err = Marshal.GetLastWin32Error();
                                    Thread.Sleep(50);
                                }
                            }
                        }
                    }
                }

                // Log result to error.log
                try
                {
                    string logPath = Settings.GetErrorLogFilePath();
                    string msg = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] SetInput(0x{1:X2}) => success={2}, err={3}\r\n",
                        DateTime.Now, inputCode, success, err);
                    File.AppendAllText(logPath, msg);
                }
                catch { }

                if (onCompleted != null)
                {
                    onCompleted(success);
                }
            });
        }

        public void Dispose()
        {
            lock (_ioLock)
            {
                if (_disposed) return;
                _disposed = true;

                if (PhysicalMonitorHandle != IntPtr.Zero)
                {
                    Native.DestroyPhysicalMonitor(PhysicalMonitorHandle);
                    PhysicalMonitorHandle = IntPtr.Zero;
                }
            }
        }
    }

    public static class WmiBrightness
    {
        public static Dictionary<string, int> GetAllBrightness()
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementClass mc = new ManagementClass("root\\wmi", "WmiMonitorBrightness", null))
                {
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            string inst = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : null;
                            if (!string.IsNullOrEmpty(inst) && mo["CurrentBrightness"] != null)
                            {
                                int val = Convert.ToInt32(mo["CurrentBrightness"]);
                                result[inst] = val;
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        public static int GetBrightness(string instanceName)
        {
            try
            {
                using (ManagementClass mc = new ManagementClass("root\\wmi", "WmiMonitorBrightness", null))
                {
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            string inst = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : null;
                            if (string.Equals(inst, instanceName, StringComparison.OrdinalIgnoreCase) && mo["CurrentBrightness"] != null)
                            {
                                return Convert.ToInt32(mo["CurrentBrightness"]);
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        public static void SetBrightness(string instanceName, int percent)
        {
            try
            {
                using (ManagementClass mc = new ManagementClass("root\\wmi", "WmiMonitorBrightnessMethods", null))
                {
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            string inst = mo["InstanceName"] != null ? mo["InstanceName"].ToString() : null;
                            if (string.Equals(inst, instanceName, StringComparison.OrdinalIgnoreCase))
                            {
                                mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    public static class MonitorManager
    {
        private class DisplayConfigInfo
        {
            public string GdiDeviceName;
            public string FriendlyName;
            public string DevicePath;
            public string PnpInstanceId;
            public uint OutputTechnology;
            public bool IsInternal;
        }

        public static List<DisplayMonitor> Enumerate()
        {
            // 1. Query Display Config
            List<DisplayConfigInfo> configInfos = QueryConfig();

            // 2. Query WMI Brightness instances
            Dictionary<string, int> wmiInstances = WmiBrightness.GetAllBrightness();
            HashSet<string> claimedWmi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 3. Enum Desktop Monitors (user32 + dxva2)
            List<DisplayMonitor> monitors = new List<DisplayMonitor>();

            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdc, ref Native.RECT lprcMonitor, IntPtr dwData)
            {
                Native.MONITORINFOEX info = new Native.MONITORINFOEX();
                Native.GetMonitorInfo(hMonitor, info);
                string gdiName = info.szDevice;
                Rectangle bounds = new Rectangle(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.Width, info.rcMonitor.Height);

                // Find matching display config
                DisplayConfigInfo cfg = null;
                foreach (DisplayConfigInfo c in configInfos)
                {
                    if (string.Equals(c.GdiDeviceName, gdiName, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg = c;
                        break;
                    }
                }

                uint numPhysical = 0;
                Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out numPhysical);
                Native.PHYSICAL_MONITOR[] phys = null;
                if (numPhysical > 0)
                {
                    phys = new Native.PHYSICAL_MONITOR[numPhysical];
                    if (!Native.GetPhysicalMonitorsFromHMONITOR(hMonitor, numPhysical, phys))
                    {
                        phys = null;
                        numPhysical = 0;
                    }
                }

                if (phys != null && numPhysical > 0)
                {
                    for (int i = 0; i < numPhysical; i++)
                    {
                        DisplayMonitor dm = new DisplayMonitor();
                        dm.GdiDeviceName = gdiName;
                        dm.Bounds = bounds;
                        dm.PhysicalMonitorHandle = phys[i].hPhysicalMonitor;

                        string friendly = cfg != null ? cfg.FriendlyName : null;
                        string desc = phys[i].szPhysicalMonitorDescription;
                        bool isInternal = cfg != null && cfg.IsInternal;
                        string pnpId = cfg != null ? cfg.PnpInstanceId : null;
                        dm.PnpInstanceId = pnpId;

                        // Check WMI match
                        string matchedWmi = null;
                        if (!string.IsNullOrEmpty(pnpId))
                        {
                            foreach (string wmiInst in wmiInstances.Keys)
                            {
                                if (wmiInst.StartsWith(pnpId, StringComparison.OrdinalIgnoreCase) ||
                                    pnpId.StartsWith(wmiInst, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedWmi = wmiInst;
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(matchedWmi))
                        {
                            dm.Method = ControlMethod.BuiltIn;
                            dm.WmiInstanceName = matchedWmi;
                            claimedWmi.Add(matchedWmi);
                            dm.Brightness = wmiInstances[matchedWmi];
                        }
                        else
                        {
                            // Try DDC/CI read
                            uint pvct, cur, max;
                            bool ddcOk = false;
                            for (int att = 0; att < 3; att++)
                            {
                                if (Native.GetVCPFeatureAndVCPFeatureReply(dm.PhysicalMonitorHandle, 0x10, out pvct, out cur, out max))
                                {
                                    ddcOk = true;
                                    if (max > 0)
                                    {
                                        dm.MaxVcpBrightness = (int)max;
                                        dm.Brightness = (int)Math.Round((double)cur * 100.0 / max);
                                    }
                                    else
                                    {
                                        dm.Brightness = (int)cur;
                                    }
                                    break;
                                }
                                Thread.Sleep(40);
                            }

                            if (ddcOk)
                            {
                                dm.Method = ControlMethod.DdcCi;
                                uint inPvct, inCur, inMax;
                                if (Native.GetVCPFeatureAndVCPFeatureReply(dm.PhysicalMonitorHandle, 0x60, out inPvct, out inCur, out inMax))
                                {
                                    dm.CurrentInput = (byte)(inCur & 0xFF);
                                }
                            }
                            else if (isInternal)
                            {
                                // Check any unclaimed WMI
                                foreach (KeyValuePair<string, int> kvp in wmiInstances)
                                {
                                    if (!claimedWmi.Contains(kvp.Key))
                                    {
                                        dm.Method = ControlMethod.BuiltIn;
                                        dm.WmiInstanceName = kvp.Key;
                                        dm.Brightness = kvp.Value;
                                        claimedWmi.Add(kvp.Key);
                                        break;
                                    }
                                }
                                if (dm.Method != ControlMethod.BuiltIn)
                                {
                                    dm.Method = ControlMethod.Unsupported;
                                }
                            }
                            else
                            {
                                dm.Method = ControlMethod.Unsupported;
                            }
                        }

                        // Determine Name
                        if (isInternal && string.IsNullOrEmpty(friendly))
                        {
                            dm.Name = "Built-in Display";
                        }
                        else if (!string.IsNullOrEmpty(friendly))
                        {
                            dm.Name = friendly;
                        }
                        else if (!string.IsNullOrEmpty(desc) && !desc.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                        {
                            dm.Name = desc;
                        }
                        else
                        {
                            dm.Name = "Display " + (monitors.Count + 1);
                        }

                        dm.Id = string.Format("{0}_{1}_{2}", gdiName, i, dm.Name);
                        monitors.Add(dm);
                    }
                }
                else
                {
                    // No DDC physical monitors found. Could be internal laptop panel using WMI
                    DisplayMonitor dm = new DisplayMonitor();
                    dm.GdiDeviceName = gdiName;
                    dm.Bounds = bounds;

                    bool isInternal = cfg != null && cfg.IsInternal;
                    string pnpId = cfg != null ? cfg.PnpInstanceId : null;
                    dm.PnpInstanceId = pnpId;

                    string matchedWmi = null;
                    if (!string.IsNullOrEmpty(pnpId))
                    {
                        foreach (string wmiInst in wmiInstances.Keys)
                        {
                            if (wmiInst.StartsWith(pnpId, StringComparison.OrdinalIgnoreCase) ||
                                pnpId.StartsWith(wmiInst, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedWmi = wmiInst;
                                break;
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(matchedWmi) && (isInternal || wmiInstances.Count > 0))
                    {
                        foreach (KeyValuePair<string, int> kvp in wmiInstances)
                        {
                            if (!claimedWmi.Contains(kvp.Key))
                            {
                                matchedWmi = kvp.Key;
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(matchedWmi))
                    {
                        dm.Method = ControlMethod.BuiltIn;
                        dm.WmiInstanceName = matchedWmi;
                        claimedWmi.Add(matchedWmi);
                        dm.Brightness = wmiInstances[matchedWmi];
                    }
                    else
                    {
                        dm.Method = ControlMethod.Unsupported;
                    }

                    string friendly = cfg != null ? cfg.FriendlyName : null;
                    if (isInternal || dm.Method == ControlMethod.BuiltIn)
                    {
                        dm.Name = "Built-in Display";
                    }
                    else if (!string.IsNullOrEmpty(friendly))
                    {
                        dm.Name = friendly;
                    }
                    else
                    {
                        dm.Name = "Display " + (monitors.Count + 1);
                    }

                    dm.Id = string.Format("{0}_{1}", gdiName, dm.Name);
                    monitors.Add(dm);
                }

                return true;
            }, IntPtr.Zero);

            // Sort by left edge
            monitors.Sort(delegate(DisplayMonitor a, DisplayMonitor b)
            {
                return a.Bounds.Left.CompareTo(b.Bounds.Left);
            });

            // Number identical monitor names: Name (1), Name (2)...
            Dictionary<string, int> nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayMonitor m in monitors)
            {
                if (nameCounts.ContainsKey(m.Name))
                    nameCounts[m.Name]++;
                else
                    nameCounts[m.Name] = 1;
            }

            Dictionary<string, int> nameIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayMonitor m in monitors)
            {
                if (nameCounts[m.Name] > 1)
                {
                    int idx = 1;
                    if (nameIndices.ContainsKey(m.Name))
                    {
                        idx = nameIndices[m.Name] + 1;
                    }
                    nameIndices[m.Name] = idx;
                    m.Name = string.Format("{0} ({1})", m.Name, idx);
                }
            }

            return monitors;
        }

        private static List<DisplayConfigInfo> QueryConfig()
        {
            List<DisplayConfigInfo> list = new List<DisplayConfigInfo>();
            try
            {
                uint pathCount = 0;
                uint modeCount = 0;
                int err = Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref pathCount, null, ref modeCount, null, IntPtr.Zero);
                if (err != 0 || pathCount == 0) return list;

                Native.DISPLAYCONFIG_PATH_INFO[] paths = new Native.DISPLAYCONFIG_PATH_INFO[pathCount];
                Native.DISPLAYCONFIG_MODE_INFO[] modes = new Native.DISPLAYCONFIG_MODE_INFO[modeCount];
                err = Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (err != 0) return list;

                for (int i = 0; i < pathCount; i++)
                {
                    Native.DISPLAYCONFIG_PATH_INFO path = paths[i];

                    // Source name (GDI device name, e.g. \\.\DISPLAY1)
                    Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    sourceName.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    sourceName.header.size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;
                    string gdi = "";
                    if (Native.DisplayConfigGetDeviceInfo(ref sourceName) == 0)
                    {
                        gdi = sourceName.viewGdiDeviceName;
                    }

                    // Target name (Friendly EDID name + PnP path + output technology)
                    Native.DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    targetName.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;

                    string friendly = "";
                    string devicePath = "";
                    string pnpId = "";
                    uint outputTech = path.targetInfo.outputTechnology;
                    bool isInternal = false;

                    if (Native.DisplayConfigGetDeviceInfo(ref targetName) == 0)
                    {
                        friendly = targetName.monitorFriendlyDeviceName;
                        devicePath = targetName.monitorDevicePath;
                        outputTech = targetName.outputTechnology;

                        // Convert \\?\DISPLAY#BOE0812#4&...#{guid} to DISPLAY\BOE0812\4&...
                        if (!string.IsNullOrEmpty(devicePath))
                        {
                            string cleaned = devicePath.Replace(@"\\?\", "");
                            int lastHash = cleaned.LastIndexOf('#');
                            if (lastHash > 0)
                            {
                                cleaned = cleaned.Substring(0, lastHash);
                            }
                            pnpId = cleaned.Replace('#', '\\');
                        }
                    }

                    if (outputTech == Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL ||
                        outputTech == Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED ||
                        outputTech == Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED ||
                        outputTech == Native.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS)
                    {
                        isInternal = true;
                    }

                    DisplayConfigInfo info = new DisplayConfigInfo();
                    info.GdiDeviceName = gdi;
                    info.FriendlyName = friendly;
                    info.DevicePath = devicePath;
                    info.PnpInstanceId = pnpId;
                    info.OutputTechnology = outputTech;
                    info.IsInternal = isInternal;

                    list.Add(info);
                }
            }
            catch { }
            return list;
        }
    }
}
