using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrightnessTray
{
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(32, 32, 32);        // #202020
        public static readonly Color Border = Color.FromArgb(70, 70, 70);            // #464646
        public static readonly Color Separator = Color.FromArgb(58, 58, 58);         // #3A3A3A
        public static readonly Color TextPrimary = Color.FromArgb(240, 240, 240);    // #F0F0F0
        public static readonly Color TextSecondary = Color.FromArgb(160, 160, 160);  // #A0A0A0
        public static readonly Color SliderTrackUnfilled = Color.FromArgb(90, 90, 90); // #5A5A5A
        public static readonly Color ThumbOuter = Color.FromArgb(69, 69, 69);        // #454545
        public static readonly Color ButtonRest = Color.FromArgb(55, 55, 55);        // #373737
        public static readonly Color ButtonHover = Color.FromArgb(72, 72, 72);       // #484848

        public static Color GetAccentColor()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AccentColor");
                        if (val is int)
                        {
                            uint dw = (uint)(int)val;
                            // Registry value is 0xAABBGGRR
                            int r = (int)(dw & 0xFF);
                            int g = (int)((dw >> 8) & 0xFF);
                            int b = (int)((dw >> 16) & 0xFF);
                            return Color.FromArgb(r, g, b);
                        }
                    }
                }
            }
            catch { }

            // Fallback Windows blue #0078D7
            return Color.FromArgb(0, 120, 215);
        }

        public static bool IsTaskbarLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme");
                        if (val is int)
                        {
                            return ((int)val) == 1;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool IsColorLight(Color c)
        {
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance > 0.6;
        }

        public static GraphicsPath CreateRoundedRectangle(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2.0f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            if (d <= 0.0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            RectangleF arc = new RectangleF(rect.X, rect.Y, d, d);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon CreateTraySunIcon(int averageBrightness, bool lightTaskbar)
        {
            Size iconSize = SystemInformation.SmallIconSize;
            int w = iconSize.Width > 0 ? iconSize.Width : 16;
            int h = iconSize.Height > 0 ? iconSize.Height : 16;

            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    Color sunColor = lightTaskbar ? Color.FromArgb(30, 30, 30) : Color.White;
                    using (Pen pen = new Pen(sunColor, Math.Max(1.0f, w * 0.08f)))
                    using (SolidBrush brush = new SolidBrush(sunColor))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;

                        float cx = w / 2.0f;
                        float cy = h / 2.0f;
                        float coreRadius = w * 0.23f;
                        RectangleF coreRect = new RectangleF(cx - coreRadius, cy - coreRadius, coreRadius * 2.0f, coreRadius * 2.0f);

                        // 8 Rays
                        float r1 = w * 0.33f;
                        float r2 = w * 0.45f;
                        for (int i = 0; i < 8; i++)
                        {
                            double angle = i * Math.PI / 4.0;
                            float x1 = cx + (float)(r1 * Math.Cos(angle));
                            float y1 = cy + (float)(r1 * Math.Sin(angle));
                            float x2 = cx + (float)(r2 * Math.Cos(angle));
                            float y2 = cy + (float)(r2 * Math.Sin(angle));
                            g.DrawLine(pen, x1, y1, x2, y2);
                        }

                        // Core outline
                        g.DrawEllipse(pen, coreRect);

                        // Core fill quarter steps:
                        // 0-12% = empty, 13-37% = 1/4, 38-62% = 1/2, 63-87% = 3/4, 88-100% = full
                        int quarter;
                        if (averageBrightness < 13) quarter = 0;
                        else if (averageBrightness < 38) quarter = 1;
                        else if (averageBrightness < 63) quarter = 2;
                        else if (averageBrightness < 88) quarter = 3;
                        else quarter = 4;

                        if (quarter > 0)
                        {
                            GraphicsState state = g.Save();
                            using (GraphicsPath clipPath = new GraphicsPath())
                            {
                                clipPath.AddEllipse(coreRect);
                                g.SetClip(clipPath);

                                float fillHeight = (coreRect.Height * quarter) / 4.0f;
                                RectangleF fillRect = new RectangleF(
                                    coreRect.X,
                                    coreRect.Bottom - fillHeight,
                                    coreRect.Width,
                                    fillHeight
                                );
                                g.FillRectangle(brush, fillRect);
                            }
                            g.Restore(state);
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }
    }

    public class BrightnessSlider : Control
    {
        private int _value = 50;
        private bool _isDragging = false;
        private bool _isHovered = false;
        private bool _isDisabled = false;

        public event Action<int, bool> ValueChanged;

        public BrightnessSlider()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = 24;
            TabStop = true;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                }
            }
        }

        public bool IsDragging
        {
            get { return _isDragging; }
        }

        public bool IsDisabled
        {
            get { return _isDisabled; }
            set
            {
                if (_isDisabled != value)
                {
                    _isDisabled = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Width;
            int h = Height;
            if (w <= 0 || h <= 0) return;

            // Rounded track height ≈ 1/7 of control height, minimum 4 px
            float trackHeight = Math.Max(4.0f, h / 7.0f);
            float trackY = (h - trackHeight) / 2.0f;
            float thumbRadius = (h - 4.0f) / 2.0f;
            if (thumbRadius < 4.0f) thumbRadius = 4.0f;

            float trackLeft = thumbRadius;
            float trackRight = w - thumbRadius;
            float trackWidth = trackRight - trackLeft;
            if (trackWidth <= 0) trackWidth = 1.0f;

            RectangleF unfilledRect = new RectangleF(trackLeft, trackY, trackWidth, trackHeight);
            using (GraphicsPath trackPath = Theme.CreateRoundedRectangle(unfilledRect, trackHeight / 2.0f))
            using (SolidBrush trackBrush = new SolidBrush(Theme.SliderTrackUnfilled))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (_isDisabled) return;

            Color accent = Theme.GetAccentColor();
            float fillRatio = _value / 100.0f;
            float filledWidth = trackWidth * fillRatio;
            if (filledWidth > 0.0f)
            {
                RectangleF filledRect = new RectangleF(trackLeft, trackY, filledWidth, trackHeight);
                using (GraphicsPath filledPath = Theme.CreateRoundedRectangle(filledRect, trackHeight / 2.0f))
                using (SolidBrush accentBrush = new SolidBrush(accent))
                {
                    g.FillPath(accentBrush, filledPath);
                }
            }

            // Fluent-style Thumb:
            // #454545 outer disc with accent-coloured inner dot
            // Inner dot grows on hover (62%), shrinks while dragging (45%), rests at 52%
            float thumbX = trackLeft + filledWidth;
            float thumbY = h / 2.0f;

            float outerD = thumbRadius * 2.0f;
            RectangleF outerRect = new RectangleF(thumbX - thumbRadius, thumbY - thumbRadius, outerD, outerD);
            using (SolidBrush outerBrush = new SolidBrush(Theme.ThumbOuter))
            {
                g.FillEllipse(outerBrush, outerRect);
            }

            float dotRatio = _isDragging ? 0.45f : (_isHovered ? 0.62f : 0.52f);
            float innerD = outerD * dotRatio;
            float innerRadius = innerD / 2.0f;
            RectangleF innerRect = new RectangleF(thumbX - innerRadius, thumbY - innerRadius, innerD, innerD);
            using (SolidBrush innerBrush = new SolidBrush(accent))
            {
                g.FillEllipse(innerBrush, innerRect);
            }
        }

        private void UpdateValueFromMouse(int mouseX, bool dragging)
        {
            if (_isDisabled) return;

            float thumbRadius = (Height - 4.0f) / 2.0f;
            float trackLeft = thumbRadius;
            float trackRight = Width - thumbRadius;
            float trackWidth = trackRight - trackLeft;
            if (trackWidth <= 0) return;

            float ratio = (mouseX - trackLeft) / trackWidth;
            int newVal = (int)Math.Round(ratio * 100.0f);
            if (newVal < 0) newVal = 0;
            if (newVal > 100) newVal = 100;

            if (_value != newVal || dragging != _isDragging)
            {
                _value = newVal;
                Invalidate();
                if (ValueChanged != null)
                {
                    ValueChanged(_value, dragging);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_isDisabled) return;
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Capture = true;
                Focus();
                UpdateValueFromMouse(e.X, true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDisabled) return;
            if (_isDragging)
            {
                UpdateValueFromMouse(e.X, true);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDisabled) return;
            if (_isDragging)
            {
                _isDragging = false;
                Capture = false;
                UpdateValueFromMouse(e.X, false);
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_isDisabled) return;
            _isHovered = true;
            Focus();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_isDisabled) return;
            int step = 5;
            int delta = e.Delta > 0 ? step : -step;
            Value += delta;
            if (ValueChanged != null)
            {
                ValueChanged(_value, false);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_isDisabled)
            {
                int delta = 0;
                switch (keyData)
                {
                    case Keys.Left:
                    case Keys.Down:
                        delta = -1;
                        break;
                    case Keys.Right:
                    case Keys.Up:
                        delta = 1;
                        break;
                    case Keys.PageDown:
                        delta = -10;
                        break;
                    case Keys.PageUp:
                        delta = 10;
                        break;
                    case Keys.Home:
                        Value = 0;
                        if (ValueChanged != null) ValueChanged(_value, false);
                        return true;
                    case Keys.End:
                        Value = 100;
                        if (ValueChanged != null) ValueChanged(_value, false);
                        return true;
                }

                if (delta != 0)
                {
                    Value += delta;
                    if (ValueChanged != null) ValueChanged(_value, false);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public class PillButton : Control
    {
        private bool _isSelected = false;
        private bool _isHovered = false;
        private bool _isConnected = false;
        private bool _isDimmed = false;

        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Height = 26;
            Width = 90;
            Font = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            Cursor = Cursors.Hand;
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    Invalidate();
                }
            }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    Invalidate();
                }
            }
        }

        public bool IsDimmed
        {
            get { return _isDimmed; }
            set
            {
                if (_isDimmed != value)
                {
                    _isDimmed = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float w = Width - 1.0f;
            float h = Height - 1.0f;
            if (w <= 0 || h <= 0) return;

            float radius = h / 2.0f;
            RectangleF rect = new RectangleF(0.5f, 0.5f, w, h);

            Color accent = Theme.GetAccentColor();
            Color bg;
            Color fg;
            Color borderColor = Color.Transparent;

            if (_isSelected)
            {
                bg = accent;
                fg = Theme.IsColorLight(accent) ? Color.Black : Theme.TextPrimary;
            }
            else if (_isHovered)
            {
                bg = Theme.ButtonHover;
                fg = Theme.TextPrimary;
                borderColor = Color.FromArgb(90, 90, 90);
            }
            else if (_isConnected)
            {
                bg = Theme.ButtonRest;
                fg = Theme.TextPrimary;
                borderColor = Color.FromArgb(70, 70, 70);
            }
            else if (_isDimmed)
            {
                bg = Color.FromArgb(36, 36, 36);
                fg = Color.FromArgb(150, 150, 150);
                borderColor = Color.FromArgb(50, 50, 50);
            }
            else
            {
                bg = Theme.ButtonRest;
                fg = Theme.TextPrimary;
            }

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, radius))
            {
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }

                if (borderColor != Color.Transparent)
                {
                    using (Pen borderPen = new Pen(borderColor, 1.0f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                float textLeft = rect.Left;
                float textWidth = rect.Width;

                if (_isSelected || _isConnected)
                {
                    float dotSize = 6.0f;
                    float dotX = rect.Left + 10.0f;
                    float dotY = rect.Top + (rect.Height - dotSize) / 2.0f;
                    Color dotColor = _isSelected 
                        ? (Theme.IsColorLight(accent) ? Color.Black : Color.White) 
                        : Color.FromArgb(16, 185, 129); // Emerald green

                    using (SolidBrush dotBrush = new SolidBrush(dotColor))
                    {
                        g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                    }

                    textLeft = rect.Left + 14.0f;
                    textWidth = rect.Width - 16.0f;
                }

                RectangleF textRect = new RectangleF(textLeft, rect.Top, textWidth, rect.Height);
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush textBrush = new SolidBrush(fg))
                    {
                        g.DrawString(Text, Font, textBrush, textRect, sf);
                    }
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }
    }
}
