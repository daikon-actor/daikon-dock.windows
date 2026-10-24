using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DaikonDock
{
    // BarForm の描画部分
    internal sealed partial class BarForm
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint で全面を塗るので何もしない（ちらつき防止）
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_settings.Background);
            g.TranslateTransform(_offsetX, _offsetY);

            DrawBorder(g);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int pad = Math.Max(2, _thick / 12);
            int radius = Math.Max(2, _thick / 9);

            for (int i = 0; i < _apps.Count && i < _itemRects.Count; i++)
            {
                Rectangle r = _itemRects[i];
                if (r.IsEmpty) continue;
                AppEntry a = _apps[i];

                if (a.Separator)
                {
                    DrawSeparator(g, r);
                    continue;
                }

                Rectangle inner = Rectangle.Inflate(r, -pad, -pad);
                if (i == _hover) FillRound(g, inner, radius, i == _pressed ? _settings.Pressed : _settings.Hover);

                Rectangle iconRect = new Rectangle(r.X + (r.Width - _iconPx) / 2, r.Y + (r.Height - _iconPx) / 2, _iconPx, _iconPx);
                if (i == _pressed && i == _hover)
                {
                    int shrink = Math.Max(1, _iconPx / 16); // 押し込み感
                    iconRect.Inflate(-shrink, -shrink);
                }
                if (a.Icon != null) g.DrawImage(a.Icon, iconRect);
                else DrawLetter(g, a, iconRect);
            }

            // 隠すボタン（右端 / 下端）
            Rectangle hideInner = Rectangle.Inflate(_hideRect, -pad, -pad);
            if (_hover == HideButton) FillRound(g, hideInner, radius, _pressed == HideButton ? _settings.Pressed : _settings.Hover);
            DrawChevron(g, _hideRect);
        }

        // タスクバーと反対側に細い境界線
        private void DrawBorder(Graphics g)
        {
            int w = _horizontal ? _finalRect.Width : _thick;
            int h = _horizontal ? _thick : _finalRect.Height;
            using (Pen pen = new Pen(_settings.Border, 1))
            {
                switch (_edge)
                {
                    case EdgeTop: g.DrawLine(pen, 0, h - 1, w, h - 1); break;
                    case EdgeLeft: g.DrawLine(pen, w - 1, 0, w - 1, h); break;
                    case EdgeRight: g.DrawLine(pen, 0, 0, 0, h); break;
                    default: g.DrawLine(pen, 0, 0, w, 0); break;
                }
            }
        }

        private void DrawSeparator(Graphics g, Rectangle r)
        {
            using (Pen pen = new Pen(_settings.Border, Math.Max(1f, _scale)))
            {
                if (_horizontal)
                    g.DrawLine(pen, r.X + r.Width / 2f, r.Y + _thick / 4f, r.X + r.Width / 2f, r.Bottom - _thick / 4f);
                else
                    g.DrawLine(pen, r.X + _thick / 4f, r.Y + r.Height / 2f, r.Right - _thick / 4f, r.Y + r.Height / 2f);
            }
        }

        // アイコンが取れないときは名前の頭文字
        private void DrawLetter(Graphics g, AppEntry a, Rectangle r)
        {
            string text = string.IsNullOrEmpty(a.Name) ? "?" : a.Name.Substring(0, 1).ToUpperInvariant();
            using (SolidBrush circle = new SolidBrush(ControlPaint.Light(_settings.Hover)))
            {
                g.FillEllipse(circle, r);
            }
            using (SolidBrush brush = new SolidBrush(_settings.Foreground))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(text, _letterFont, brush, r, sf);
            }
        }

        // タスクバーの方向を指す矢印
        private void DrawChevron(Graphics g, Rectangle r)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float s = Math.Max(3f, _iconPx * 0.28f);
            PointF[] pts;
            switch (_edge)
            {
                case EdgeTop: pts = new[] { new PointF(cx - s, cy + s / 2), new PointF(cx, cy - s / 2), new PointF(cx + s, cy + s / 2) }; break;
                case EdgeLeft: pts = new[] { new PointF(cx + s / 2, cy - s), new PointF(cx - s / 2, cy), new PointF(cx + s / 2, cy + s) }; break;
                case EdgeRight: pts = new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) }; break;
                default: pts = new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) }; break;
            }
            using (Pen pen = new Pen(_settings.Foreground, Math.Max(1.2f, 1.6f * _scale)))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, pts);
            }
        }

        private static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath path = RoundRect(r, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
