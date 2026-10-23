using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BrightnessTray
{
    public class Flyout : Form
    {
        private DateTime _lastHideTime = DateTime.MinValue;
        private bool _isMenuOpen = false;
        private List<DisplayMonitor> _monitors = new List<DisplayMonitor>();
        private Settings _settings;
        private Action _onRefreshClicked;
        private Action<bool> _onLinkChanged;
        private Action _onValuesChanged;

        private Panel _contentPanel;
        private CheckBox _linkCheckBox;
        private readonly Dictionary<string, BrightnessSlider> _sliders = new Dictionary<string, BrightnessSlider>();
        private readonly Dictionary<string, Label> _valueLabels = new Dictionary<string, Label>();
        private readonly Dictionary<string, Label> _inputCaptions = new Dictionary<string, Label>();
        private readonly Dictionary<string, List<PillButton>> _inputButtons = new Dictionary<string, List<PillButton>>();
        private readonly Dictionary<string, bool> _otherExpanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private float _dpiScale = 1.0f;

        public Flyout()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            using (Graphics g = CreateGraphics())
            {
                _dpiScale = g.DpiX / 96.0f;
                if (_dpiScale < 0.5f) _dpiScale = 1.0f;
            }

            Width = S(380);

            _contentPanel = new Panel();
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.BackColor = Theme.Background;
            _contentPanel.AutoScroll = false;
            Controls.Add(_contentPanel);
        }

        public int S(int px)
        {
            return (int)Math.Round(px * _dpiScale);
        }

        public bool WasJustClosed
        {
            get
            {
                return (DateTime.UtcNow - _lastHideTime).TotalMilliseconds < 300;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmStyling();
        }

        private void ApplyDwmStyling()
        {
            try
            {
                int dark = 1;
                // Windows 10 20H1+ and Windows 11
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                // Fallback for earlier builds
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

                // Windows 11 rounded corners
                int round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            if (_isMenuOpen) return;
            base.OnDeactivate(e);
            _lastHideTime = DateTime.UtcNow;
            Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                _lastHideTime = DateTime.UtcNow;
                Hide();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 1 px #464646 hairline border
            using (Pen borderPen = new Pen(Theme.Border, 1.0f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        public void PositionNearTray(Native.RECT trayRect)
        {
            Screen screen = null;
            if (trayRect.Width > 0 && trayRect.Height > 0)
            {
                Point center = new Point(trayRect.left + trayRect.Width / 2, trayRect.top + trayRect.Height / 2);
                screen = Screen.FromPoint(center);
            }
            if (screen == null)
            {
                screen = Screen.PrimaryScreen;
            }

            Rectangle work = screen.WorkingArea;
            Rectangle bounds = screen.Bounds;
            int margin = S(12);

            int x = 0;
            int y = 0;

            // Detect taskbar position
            bool isBottom = work.Bottom < bounds.Bottom;
            bool isTop = work.Top > bounds.Top;
            bool isLeft = work.Left > bounds.Left;
            bool isRight = work.Right < bounds.Right;

            if (isTop)
            {
                x = (trayRect.left + trayRect.Width / 2) - (Width / 2);
                y = work.Top + margin;
            }
            else if (isLeft)
            {
                x = work.Left + margin;
                y = (trayRect.top + trayRect.Height / 2) - (Height / 2);
            }
            else if (isRight)
            {
                x = work.Right - Width - margin;
                y = (trayRect.top + trayRect.Height / 2) - (Height / 2);
            }
            else // Default to bottom taskbar
            {
                if (trayRect.Width > 0)
                {
                    x = (trayRect.left + trayRect.Width / 2) - (Width / 2);
                }
                else
                {
                    x = work.Right - Width - margin;
                }
                y = work.Bottom - Height - margin;
            }

            // Clamp inside screen work area
            if (x < work.Left + margin) x = work.Left + margin;
            if (x + Width > work.Right - margin) x = work.Right - Width - margin;
            if (y < work.Top + margin) y = work.Top + margin;
            if (y + Height > work.Bottom - margin) y = work.Bottom - Height - margin;

            Location = new Point(x, y);
        }

        public void ShowFlyout(Native.RECT trayRect)
        {
            PositionNearTray(trayRect);
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        public void Build(
            List<DisplayMonitor> monitors,
            Settings settings,
            Action onRefreshClicked,
            Action<bool> onLinkChanged,
            Action onValuesChanged)
        {
            _monitors = monitors != null ? monitors : new List<DisplayMonitor>();
            _settings = settings;
            _onRefreshClicked = onRefreshClicked;
            _onLinkChanged = onLinkChanged;
            _onValuesChanged = onValuesChanged;

            _contentPanel.SuspendLayout();
            _contentPanel.Controls.Clear();
            _sliders.Clear();
            _valueLabels.Clear();
            _inputCaptions.Clear();
            _inputButtons.Clear();

            int pad = S(16);
            int curY = S(16);
            int contentWidth = Width - (pad * 2);

            // Title: "Brightness" (11.5 pt Semibold)
            Label lblTitle = new Label();
            lblTitle.Text = "Brightness";
            lblTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.TextPrimary;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(pad, curY);
            _contentPanel.Controls.Add(lblTitle);
            curY += lblTitle.PreferredHeight + S(14);

            // Display list
            for (int i = 0; i < _monitors.Count; i++)
            {
                DisplayMonitor mon = _monitors[i];

                if (i > 0)
                {
                    // Separator line between monitors
                    Panel sep = new Panel();
                    sep.BackColor = Theme.Separator;
                    sep.Height = 1;
                    sep.Width = contentWidth;
                    sep.Location = new Point(pad, curY);
                    _contentPanel.Controls.Add(sep);
                    curY += S(14);
                }

                // Row: Display name (left) + Method tag (right)
                Label lblName = new Label();
                lblName.Text = mon.Name;
                lblName.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                lblName.ForeColor = Theme.TextPrimary;
                lblName.AutoSize = true;
                lblName.Location = new Point(pad, curY);

                Label lblTag = new Label();
                lblTag.Text = mon.MethodTag;
                lblTag.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                lblTag.ForeColor = Theme.TextSecondary;
                lblTag.AutoSize = true;
                lblTag.Location = new Point(pad + contentWidth - lblTag.PreferredWidth, curY + S(2));

                _contentPanel.Controls.Add(lblName);
                _contentPanel.Controls.Add(lblTag);
                curY += Math.Max(lblName.PreferredHeight, lblTag.PreferredHeight) + S(10);

                // Row: Slider + Value Label
                int valLabelWidth = S(32);
                int sliderWidth = contentWidth - valLabelWidth - S(10);

                BrightnessSlider slider = new BrightnessSlider();
                slider.Width = sliderWidth;
                slider.Height = S(24);
                slider.Location = new Point(pad, curY);
                slider.Value = mon.Brightness;
                slider.IsDisabled = (mon.Method == ControlMethod.Unsupported);

                Label lblVal = new Label();
                lblVal.Text = mon.Brightness.ToString();
                lblVal.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                lblVal.ForeColor = Theme.TextPrimary;
                lblVal.TextAlign = ContentAlignment.MiddleRight;
                lblVal.Width = valLabelWidth;
                lblVal.Height = S(24);
                lblVal.Location = new Point(pad + sliderWidth + S(10), curY);

                _sliders[mon.Id] = slider;
                _valueLabels[mon.Id] = lblVal;

                DisplayMonitor currentMon = mon;
                slider.ValueChanged += delegate(int val, bool isDragging)
                {
                    lblVal.Text = val.ToString();
                    if (_settings.Linked)
                    {
                        foreach (DisplayMonitor other in _monitors)
                        {
                            other.SetBrightnessAsync(val);
                            BrightnessSlider otherSlider;
                            if (_sliders.TryGetValue(other.Id, out otherSlider))
                            {
                                otherSlider.Value = val;
                            }
                            Label otherVal;
                            if (_valueLabels.TryGetValue(other.Id, out otherVal))
                            {
                                otherVal.Text = val.ToString();
                            }
                        }
                    }
                    else
                    {
                        currentMon.SetBrightnessAsync(val);
                    }

                    if (_onValuesChanged != null)
                    {
                        _onValuesChanged();
                    }
                };

                _contentPanel.Controls.Add(slider);
                _contentPanel.Controls.Add(lblVal);
                curY += slider.Height + S(10);

                // Unsupported display help text
                if (mon.Method == ControlMethod.Unsupported)
                {
                    Label lblHelp = new Label();
                    lblHelp.Text = "DDC/CI must be enabled in the monitor's on-screen menu, or a dock or DisplayLink adapter may be blocking it.";
                    lblHelp.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                    lblHelp.ForeColor = Theme.TextSecondary;
                    lblHelp.Width = contentWidth;
                    lblHelp.Height = S(34);
                    lblHelp.Location = new Point(pad, curY);
                    _contentPanel.Controls.Add(lblHelp);
                    curY += lblHelp.Height + S(8);
                }
                else if (mon.Method == ControlMethod.DdcCi)
                {
                    // Ensure active input is marked as connected
                    if (mon.CurrentInput.HasValue)
                    {
                        _settings.EnsureActiveInputConnected(mon.Id, mon.CurrentInput.Value);
                    }

                    // Input source caption
                    Label lblInputCaption = new Label();
                    lblInputCaption.Text = mon.IsReadingCapabilities ? "Input source (reading monitor…)" : "Input source";
                    lblInputCaption.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                    lblInputCaption.ForeColor = Theme.TextSecondary;
                    lblInputCaption.AutoSize = true;
                    lblInputCaption.Location = new Point(pad, curY);
                    _contentPanel.Controls.Add(lblInputCaption);
                    _inputCaptions[mon.Id] = lblInputCaption;
                    curY += lblInputCaption.PreferredHeight + S(8);

                    List<byte> allInputs = mon.GetEffectiveInputs(_settings.ShowAllInputs);
                    List<byte> connectedInputs = new List<byte>();
                    List<byte> otherInputs = new List<byte>();

                    foreach (byte code in allInputs)
                    {
                        bool isCurrent = mon.CurrentInput.HasValue && mon.CurrentInput.Value == code;
                        if (_settings.IsInputConnected(mon.Id, code, isCurrent))
                        {
                            connectedInputs.Add(code);
                        }
                        else
                        {
                            otherInputs.Add(code);
                        }
                    }

                    // If no connected inputs identified yet, default to first or active
                    if (connectedInputs.Count == 0 && allInputs.Count > 0)
                    {
                        connectedInputs.Add(allInputs[0]);
                        otherInputs.Remove(allInputs[0]);
                    }

                    List<PillButton> monitorButtons = new List<PillButton>();

                    // 1. Render Connected Inputs (shown first, highlighted with green/accent indicator dot)
                    List<PillButton> connectedButtons = new List<PillButton>();
                    foreach (byte code in connectedInputs)
                    {
                        PillButton btn = CreateInputPillButton(mon, code, true);
                        connectedButtons.Add(btn);
                        monitorButtons.Add(btn);
                    }

                    curY = LayoutPillButtons(connectedButtons, pad, curY, contentWidth, S(6), S(6));

                    // 2. Render Other Inputs section (collapsible so user won't click unconnected ports by mistake)
                    if (otherInputs.Count > 0)
                    {
                        bool isExpanded = _otherExpanded.ContainsKey(mon.Id) && _otherExpanded[mon.Id];

                        Label lblToggle = new Label();
                        lblToggle.Text = isExpanded
                            ? "▾ Hide other ports"
                            : string.Format("▸ Other ports ({0})", otherInputs.Count);
                        lblToggle.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                        lblToggle.ForeColor = Color.FromArgb(145, 145, 145);
                        lblToggle.AutoSize = true;
                        lblToggle.Cursor = Cursors.Hand;
                        lblToggle.Location = new Point(pad, curY + S(2));

                        string monId = mon.Id;
                        lblToggle.Click += delegate
                        {
                            _otherExpanded[monId] = !isExpanded;
                            Rebuild();
                        };
                        lblToggle.MouseEnter += delegate
                        {
                            lblToggle.ForeColor = Color.FromArgb(210, 210, 210);
                        };
                        lblToggle.MouseLeave += delegate
                        {
                            lblToggle.ForeColor = Color.FromArgb(145, 145, 145);
                        };

                        _contentPanel.Controls.Add(lblToggle);
                        curY += lblToggle.PreferredHeight + S(6);

                        if (isExpanded)
                        {
                            List<PillButton> otherButtons = new List<PillButton>();
                            foreach (byte code in otherInputs)
                            {
                                PillButton btn = CreateInputPillButton(mon, code, false);
                                otherButtons.Add(btn);
                                monitorButtons.Add(btn);
                            }
                            curY = LayoutPillButtons(otherButtons, pad, curY, contentWidth, S(6), S(6));
                        }
                    }

                    _inputButtons[mon.Id] = monitorButtons;
                    curY += S(4);
                }
            }

            // Separator above footer
            Panel footerSep = new Panel();
            footerSep.BackColor = Theme.Separator;
            footerSep.Height = 1;
            footerSep.Width = contentWidth;
            footerSep.Location = new Point(pad, curY);
            _contentPanel.Controls.Add(footerSep);
            curY += S(12);

            // Footer: "Link all displays" Checkbox + "Refresh" PillButton
            _linkCheckBox = new CheckBox();
            _linkCheckBox.Text = "Link all displays";
            _linkCheckBox.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            _linkCheckBox.ForeColor = Theme.TextPrimary;
            _linkCheckBox.AutoSize = true;
            _linkCheckBox.Checked = _settings.Linked;
            _linkCheckBox.Location = new Point(pad, curY + S(3));
            _linkCheckBox.CheckedChanged += delegate
            {
                _settings.Linked = _linkCheckBox.Checked;
                if (_onLinkChanged != null)
                {
                    _onLinkChanged(_linkCheckBox.Checked);
                }
            };

            PillButton btnRefresh = new PillButton();
            btnRefresh.Text = "Refresh";
            btnRefresh.Height = S(26);
            btnRefresh.Width = S(75);
            btnRefresh.Location = new Point(pad + contentWidth - btnRefresh.Width, curY);
            btnRefresh.Click += delegate
            {
                if (_onRefreshClicked != null)
                {
                    _onRefreshClicked();
                }
            };

            _contentPanel.Controls.Add(_linkCheckBox);
            _contentPanel.Controls.Add(btnRefresh);
            curY += Math.Max(_linkCheckBox.PreferredSize.Height, btnRefresh.Height) + S(16);

            _contentPanel.ResumeLayout(true);
            Height = curY;
            Invalidate();
        }

        public void SyncValues()
        {
            if (_linkCheckBox != null && _settings != null)
            {
                _linkCheckBox.Checked = _settings.Linked;
            }

            foreach (DisplayMonitor mon in _monitors)
            {
                BrightnessSlider slider;
                if (_sliders.TryGetValue(mon.Id, out slider))
                {
                    if (!slider.IsDragging)
                    {
                        slider.Value = mon.Brightness;
                    }
                }

                Label lblVal;
                if (_valueLabels.TryGetValue(mon.Id, out lblVal))
                {
                    lblVal.Text = mon.Brightness.ToString();
                }

                Label lblCap;
                if (_inputCaptions.TryGetValue(mon.Id, out lblCap))
                {
                    lblCap.Text = mon.IsReadingCapabilities ? "Input source (reading monitor…)" : "Input source";
                }

                List<PillButton> buttons;
                if (_inputButtons.TryGetValue(mon.Id, out buttons))
                {
                    foreach (PillButton btn in buttons)
                    {
                        if (btn.Tag is byte)
                        {
                            byte code = (byte)btn.Tag;
                            btn.IsSelected = (mon.CurrentInput.HasValue && mon.CurrentInput.Value == code);
                            btn.IsConnected = _settings.IsInputConnected(mon.Id, code, btn.IsSelected);
                            btn.IsDimmed = !btn.IsConnected && !btn.IsSelected;
                        }
                    }
                }
            }
        }

        private int LayoutPillButtons(List<PillButton> buttons, int startX, int startY, int maxWidth, int spacingX, int spacingY)
        {
            int curX = startX;
            int curY = startY;
            int rowHeight = S(26);

            for (int i = 0; i < buttons.Count; i++)
            {
                PillButton btn = buttons[i];
                if (curX + btn.Width > startX + maxWidth && curX > startX)
                {
                    curX = startX;
                    curY += rowHeight + spacingY;
                }
                btn.Location = new Point(curX, curY);
                _contentPanel.Controls.Add(btn);
                curX += btn.Width + spacingX;
            }

            if (buttons.Count > 0)
            {
                return curY + rowHeight + spacingY;
            }
            return startY;
        }

        private PillButton CreateInputPillButton(DisplayMonitor mon, byte code, bool isConnected)
        {
            PillButton btn = new PillButton();
            btn.Text = InputSource.GetInputName(code);
            btn.Tag = code;
            btn.Height = S(26);
            btn.IsSelected = (mon.CurrentInput.HasValue && mon.CurrentInput.Value == code);
            btn.IsConnected = isConnected;
            btn.IsDimmed = !isConnected && !btn.IsSelected;

            using (Graphics g = CreateGraphics())
            {
                SizeF sz = g.MeasureString(btn.Text, btn.Font);
                int extra = (btn.IsSelected || btn.IsConnected) ? S(34) : S(22);
                btn.Width = Math.Max(S(70), (int)Math.Ceiling(sz.Width) + extra);
            }

            byte currentCode = code;
            string monId = mon.Id;

            btn.Click += delegate
            {
                mon.CurrentInput = currentCode;
                _settings.SetInputConnected(monId, currentCode, true);
                SyncValues();

                mon.SetInputAsync(currentCode, delegate
                {
                    BeginInvoke((Action)delegate
                    {
                        Rebuild();
                    });
                });
            };

            ContextMenuStrip btnMenu = new ContextMenuStrip();
            btnMenu.Renderer = new DarkMenuRenderer();
            btnMenu.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btnMenu.ForeColor = Theme.TextPrimary;
            btnMenu.Opened += delegate { _isMenuOpen = true; };
            btnMenu.Closed += delegate { _isMenuOpen = false; };

            if (isConnected)
            {
                ToolStripMenuItem miDisconnect = new ToolStripMenuItem("Mark as not connected");
                miDisconnect.Enabled = !btn.IsSelected;
                miDisconnect.Click += delegate
                {
                    _settings.SetInputConnected(monId, currentCode, false);
                    Rebuild();
                };
                btnMenu.Items.Add(miDisconnect);
            }
            else
            {
                ToolStripMenuItem miConnect = new ToolStripMenuItem("Mark as connected");
                miConnect.Click += delegate
                {
                    _settings.SetInputConnected(monId, currentCode, true);
                    Rebuild();
                };
                btnMenu.Items.Add(miConnect);
            }

            btn.ContextMenuStrip = btnMenu;
            return btn;
        }

        public void Rebuild()
        {
            Build(_monitors, _settings, _onRefreshClicked, _onLinkChanged, _onValuesChanged);
        }
    }
}
