using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrightnessTray
{
    public class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly Flyout _flyout;
        private readonly Settings _settings;

        private List<DisplayMonitor> _monitors = new List<DisplayMonitor>();
        private readonly object _monitorsLock = new object();

        private IntPtr _mouseHook = IntPtr.Zero;
        private Native.HookProc _mouseHookDelegate;
        private readonly System.Windows.Forms.Timer _trayRectTimer;
        private readonly System.Windows.Forms.Timer _debounceTimer;

        private Native.RECT _cachedTrayRect;
        private int _accumulatedWheelDelta = 0;
        private readonly SynchronizationContext _syncContext;

        private ToolStripMenuItem _menuOpen;
        private ToolStripMenuItem _menuRefresh;
        private ToolStripMenuItem _menuLink;
        private ToolStripMenuItem _menuShowAllInputs;
        private ToolStripMenuItem _menuScrollStep;
        private ToolStripMenuItem _menuTurnOffDisplays;
        private ToolStripMenuItem _menuStartWithWindows;
        private ToolStripMenuItem _menuExit;

        public TrayApp(bool startSilent)
        {
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _settings = Settings.Load();

            _flyout = new Flyout();

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            BuildContextMenu();

            _notifyIcon = new NotifyIcon();
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += NotifyIcon_MouseClick;

            UpdateTrayIconAndTooltip();

            // Setup 1s timer to track tray icon rectangle
            _trayRectTimer = new System.Windows.Forms.Timer();
            _trayRectTimer.Interval = 1000;
            _trayRectTimer.Tick += delegate { UpdateTrayRectangle(); };
            _trayRectTimer.Start();

            // Setup debounce timer (2000 ms) for display changes / power resume
            _debounceTimer = new System.Windows.Forms.Timer();
            _debounceTimer.Interval = 2000;
            _debounceTimer.Tick += delegate
            {
                _debounceTimer.Stop();
                ReloadMonitors(true);
            };

            // Setup low-level mouse hook for tray wheel scrolling
            InstallMouseHook();

            // System event listeners
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            // Initial monitor load
            ReloadMonitors(false);

            // Initial tray rect update
            UpdateTrayRectangle();

            if (!startSilent)
            {
                // Schedule opening the flyout as soon as initial load begins
                _syncContext.Post(delegate
                {
                    ShowFlyout();
                }, null);
            }
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            _menuOpen = new ToolStripMenuItem("Open brightness panel");
            _menuOpen.Font = new Font(_menuOpen.Font, FontStyle.Bold);
            _menuOpen.Click += delegate { ShowFlyout(); };
            _contextMenu.Items.Add(_menuOpen);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _menuRefresh = new ToolStripMenuItem("Refresh displays");
            _menuRefresh.Click += delegate { ReloadMonitors(true); };
            _contextMenu.Items.Add(_menuRefresh);

            _menuLink = new ToolStripMenuItem("Link all displays");
            _menuLink.CheckOnClick = true;
            _menuLink.Checked = _settings.Linked;
            _menuLink.Click += delegate
            {
                _settings.Linked = _menuLink.Checked;
                _settings.Save();
                _flyout.SyncValues();
            };
            _contextMenu.Items.Add(_menuLink);

            _menuShowAllInputs = new ToolStripMenuItem("Show all common inputs");
            _menuShowAllInputs.CheckOnClick = true;
            _menuShowAllInputs.Checked = _settings.ShowAllInputs;
            _menuShowAllInputs.Click += delegate
            {
                _settings.ShowAllInputs = _menuShowAllInputs.Checked;
                _settings.Save();
                RebuildFlyout();
            };
            _contextMenu.Items.Add(_menuShowAllInputs);

            _menuScrollStep = new ToolStripMenuItem("Scroll step");
            int[] steps = new int[] { 1, 2, 5, 10, 20 };
            foreach (int s in steps)
            {
                int currentStep = s;
                ToolStripMenuItem stepItem = new ToolStripMenuItem(string.Format("{0} %", currentStep));
                stepItem.Checked = (_settings.ScrollStep == currentStep);
                stepItem.Click += delegate
                {
                    _settings.ScrollStep = currentStep;
                    _settings.Save();
                    foreach (ToolStripItem it in _menuScrollStep.DropDownItems)
                    {
                        ToolStripMenuItem mi = it as ToolStripMenuItem;
                        if (mi != null)
                        {
                            mi.Checked = (mi == stepItem);
                        }
                    }
                };
                _menuScrollStep.DropDownItems.Add(stepItem);
            }
            _contextMenu.Items.Add(_menuScrollStep);

            _menuTurnOffDisplays = new ToolStripMenuItem("Turn off displays");
            _menuTurnOffDisplays.Click += delegate
            {
                // Delay 500 ms so the click doesn't wake the screen immediately
                System.Windows.Forms.Timer sleepTimer = new System.Windows.Forms.Timer();
                sleepTimer.Interval = 500;
                sleepTimer.Tick += delegate
                {
                    sleepTimer.Stop();
                    sleepTimer.Dispose();
                    Native.PostMessage(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_MONITORPOWER, (IntPtr)2);
                };
                sleepTimer.Start();
            };
            _contextMenu.Items.Add(_menuTurnOffDisplays);

            _menuStartWithWindows = new ToolStripMenuItem("Start with Windows");
            _menuStartWithWindows.CheckOnClick = true;
            _menuStartWithWindows.Checked = _settings.StartWithWindows;
            _menuStartWithWindows.Click += delegate
            {
                _settings.StartWithWindows = _menuStartWithWindows.Checked;
            };
            _contextMenu.Items.Add(_menuStartWithWindows);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _menuExit = new ToolStripMenuItem("Exit");
            _menuExit.Click += delegate { ExitApplication(); };
            _contextMenu.Items.Add(_menuExit);
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_flyout.Visible)
                {
                    _flyout.Hide();
                }
                else if (_flyout.WasJustClosed)
                {
                    // Ignore click if panel was just closed by blur (<300 ms)
                }
                else
                {
                    ShowFlyout();
                }
            }
        }

        public void ShowFlyout()
        {
            if (_flyout.InvokeRequired)
            {
                _flyout.BeginInvoke((Action)delegate { ShowFlyout(); });
                return;
            }

            UpdateTrayRectangle();
            _flyout.ShowFlyout(_cachedTrayRect);

            // In background, refresh current brightness & inputs from hardware
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DisplayMonitor> copy;
                lock (_monitorsLock)
                {
                    copy = new List<DisplayMonitor>(_monitors);
                }

                foreach (DisplayMonitor m in copy)
                {
                    m.RefreshHardwareBrightness();
                }

                _syncContext.Post(delegate
                {
                    _flyout.SyncValues();
                    UpdateTrayIconAndTooltip();
                }, null);
            });
        }

        private void ReloadMonitors(bool forceCapabilities)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DisplayMonitor> oldList;
                lock (_monitorsLock)
                {
                    oldList = _monitors;
                }

                List<DisplayMonitor> newList = MonitorManager.Enumerate();

                lock (_monitorsLock)
                {
                    _monitors = newList;
                }

                // Safely dispose old monitors in background
                ThreadPool.QueueUserWorkItem(delegate
                {
                    foreach (DisplayMonitor old in oldList)
                    {
                        old.Dispose();
                    }
                });

                _syncContext.Post(delegate
                {
                    RebuildFlyout();
                    UpdateTrayIconAndTooltip();
                }, null);

                // Now fetch capabilities for external monitors in background
                foreach (DisplayMonitor m in newList)
                {
                    if (m.Method == ControlMethod.DdcCi)
                    {
                        DisplayMonitor current = m;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            current.RefreshCapabilities();
                            _syncContext.Post(delegate
                            {
                                _flyout.SyncValues();
                            }, null);
                        });
                    }
                }
            });
        }

        private void RebuildFlyout()
        {
            List<DisplayMonitor> copy;
            lock (_monitorsLock)
            {
                copy = new List<DisplayMonitor>(_monitors);
            }

            _flyout.Build(
                copy,
                _settings,
                delegate { ReloadMonitors(true); },
                delegate(bool linked)
                {
                    _settings.Linked = linked;
                    _settings.Save();
                    if (_menuLink != null) _menuLink.Checked = linked;
                },
                delegate
                {
                    UpdateTrayIconAndTooltip();
                }
            );
        }

        private void UpdateTrayIconAndTooltip()
        {
            List<DisplayMonitor> copy;
            lock (_monitorsLock)
            {
                copy = new List<DisplayMonitor>(_monitors);
            }

            int sum = 0;
            int count = 0;
            List<string> lines = new List<string>();
            lines.Add("Brightness");

            foreach (DisplayMonitor m in copy)
            {
                if (m.Method != ControlMethod.Unsupported)
                {
                    sum += m.Brightness;
                    count++;
                    lines.Add(string.Format("{0}: {1}%", m.Name, m.Brightness));
                }
            }

            int avg = count > 0 ? (int)Math.Round((double)sum / count) : 50;
            bool light = Theme.IsTaskbarLight();

            Icon oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = Theme.CreateTraySunIcon(avg, light);
            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }

            string fullTooltip = string.Join("\n", lines.ToArray());
            if (fullTooltip.Length >= 64)
            {
                fullTooltip = fullTooltip.Substring(0, 60) + "...";
            }
            _notifyIcon.Text = fullTooltip;
        }

        private void UpdateTrayRectangle()
        {
            try
            {
                Native.NOTIFYICONIDENTIFIER nid = GetNotifyIconIdentifier();
                Native.RECT rc = new Native.RECT();
                int hr = Native.Shell_NotifyIconGetRect(ref nid, out rc);
                if (hr == 0 && rc.Width > 0 && rc.Height > 0)
                {
                    _cachedTrayRect = rc;
                }
            }
            catch { }
        }

        private Native.NOTIFYICONIDENTIFIER GetNotifyIconIdentifier()
        {
            Native.NOTIFYICONIDENTIFIER nid = new Native.NOTIFYICONIDENTIFIER();
            nid.cbSize = (uint)Marshal.SizeOf(typeof(Native.NOTIFYICONIDENTIFIER));

            try
            {
                FieldInfo idField = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo windowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);

                if (idField != null)
                {
                    nid.uID = (uint)(int)idField.GetValue(_notifyIcon);
                }
                if (windowField != null)
                {
                    NativeWindow nw = windowField.GetValue(_notifyIcon) as NativeWindow;
                    if (nw != null)
                    {
                        nid.hWnd = nw.Handle;
                    }
                }
            }
            catch { }

            return nid;
        }

        private void InstallMouseHook()
        {
            _mouseHookDelegate = MouseHookCallback;
            IntPtr hModule = Native.GetModuleHandle(null);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseHookDelegate, hModule, 0);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)Native.WM_MOUSEWHEEL)
            {
                Native.MSLLHOOKSTRUCT mouseStruct = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if (_cachedTrayRect.Width > 0 && _cachedTrayRect.Contains(mouseStruct.pt.x, mouseStruct.pt.y))
                {
                    short wheelDelta = (short)((mouseStruct.mouseData >> 16) & 0xffff);
                    _syncContext.Post(delegate
                    {
                        OnTrayMouseWheel(wheelDelta);
                    }, null);

                    // Swallow event so window under taskbar doesn't scroll
                    return (IntPtr)1;
                }
            }

            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void OnTrayMouseWheel(int rawDelta)
        {
            _accumulatedWheelDelta += rawDelta;
            int notches = _accumulatedWheelDelta / 120;
            if (notches == 0) return;

            _accumulatedWheelDelta %= 120;
            int step = _settings.ScrollStep * notches;

            List<DisplayMonitor> copy;
            lock (_monitorsLock)
            {
                copy = new List<DisplayMonitor>(_monitors);
            }

            if (copy.Count == 0) return;

            if (_settings.Linked)
            {
                int baseVal = copy[0].Brightness;
                int target = Math.Max(0, Math.Min(100, baseVal + step));
                foreach (DisplayMonitor m in copy)
                {
                    if (m.Method != ControlMethod.Unsupported)
                    {
                        m.SetBrightnessAsync(target);
                    }
                }
            }
            else
            {
                foreach (DisplayMonitor m in copy)
                {
                    if (m.Method != ControlMethod.Unsupported)
                    {
                        int target = Math.Max(0, Math.Min(100, m.Brightness + step));
                        m.SetBrightnessAsync(target);
                    }
                }
            }

            _flyout.SyncValues();
            UpdateTrayIconAndTooltip();
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            UpdateTrayIconAndTooltip();
        }

        private void ExitApplication()
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            if (_trayRectTimer != null)
            {
                _trayRectTimer.Stop();
                _trayRectTimer.Dispose();
            }

            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Dispose();
            }

            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                if (_notifyIcon.Icon != null)
                {
                    _notifyIcon.Icon.Dispose();
                }
                _notifyIcon.Dispose();
            }

            if (_flyout != null && !_flyout.IsDisposed)
            {
                _flyout.Dispose();
            }

            lock (_monitorsLock)
            {
                foreach (DisplayMonitor m in _monitors)
                {
                    m.Dispose();
                }
                _monitors.Clear();
            }

            ExitThread();
        }
    }

    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(43, 43, 43); } }
        public override Color MenuBorder { get { return Color.FromArgb(70, 70, 70); } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(60, 60, 60); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(60, 60, 60); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(60, 60, 60); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(75, 75, 75); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(75, 75, 75); } }
        public override Color SeparatorDark { get { return Color.FromArgb(65, 65, 65); } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(43, 43, 43); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(43, 43, 43); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(43, 43, 43); } }
        public override Color CheckBackground { get { return Color.FromArgb(70, 70, 70); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(85, 85, 85); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(90, 90, 90); } }
    }
}
