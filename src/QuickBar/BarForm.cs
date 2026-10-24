using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickBar
{
    /// <summary>
    /// タスクバーの上に出るランチャーバー。配置・アニメーション・マウス操作を担当する。
    /// 描画は BarForm.Paint.cs。
    /// </summary>
    internal sealed partial class BarForm : Form
    {
        private const int HideButton = -2;
        private const int EdgeLeft = 0;
        private const int EdgeTop = 1;
        private const int EdgeRight = 2;
        private const int EdgeBottom = 3;

        public event Action<AppEntry> ItemClicked;

        private readonly string _appDir;
        private readonly float _scale;
        private BarSettings _settings = new BarSettings();
        private List<AppEntry> _apps = new List<AppEntry>();

        // 配置
        private int _edge = EdgeBottom;
        private bool _horizontal = true;
        private int _thick = 48;
        private Rectangle _finalRect;
        private int _iconPx;
        private readonly List<Rectangle> _itemRects = new List<Rectangle>();
        private Rectangle _hideRect;

        // アニメーション
        private double _progress;      // 0=隠れている 1=表示
        private int _direction;        // +1 表示中 / -1 非表示中 / 0 停止
        private double _startProgress;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Timer _animTimer = new Timer();
        private int _offsetX;          // スライド表現用の描画オフセット
        private int _offsetY;

        private int _hover = -1;
        private int _pressed = -1;
        private readonly ToolTip _tip = new ToolTip();
        private Font _letterFont;

        public BarForm(string appDir)
        {
            _appDir = appDir;

            Text = "QuickBar";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _animTimer.Interval = 10;
            _animTimer.Tick += OnAnimTick;
            _tip.ShowAlways = true;
            _tip.UseAnimation = false;
            _tip.UseFading = false;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;

            CreateHandle(); // ウィンドウだけ先に作っておく（表示はしない）
        }

        public ContextMenuStrip BarMenu { get; set; }

        /// <summary>ツールチップに出すトリガーキーの名前</summary>
        public string TriggerName { get; set; }

        /// <summary>バーに入りきらず表示されていない項目の数（区切り線は数えない）</summary>
        public int HiddenCount { get; private set; }

        /// <summary>区切り線を除いた項目の数</summary>
        public int ItemCount { get; private set; }

        public bool IsOpen
        {
            get { return _direction > 0 || (_direction == 0 && _progress > 0); }
        }

        // フォーカスを奪わない・Alt+Tabに出さない・最前面
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // ---------------- 設定 ----------------

        public void SetConfig(BarSettings settings, List<AppEntry> apps)
        {
            DisposeIcons();
            _settings = settings;
            _apps = apps;
            _iconPx = 0; // アイコンを作り直させる
            Opacity = Math.Max(0.2, Math.Min(1.0, settings.Opacity));

            ComputeGeometry();
            if (_progress > 0) Apply();
        }

        // ---------------- 配置計算 ----------------

        public void RefreshGeometry()
        {
            ComputeGeometry();
            if (_progress > 0) Apply();
        }

        private void ComputeGeometry()
        {
            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            Rectangle taskbar;
            if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref abd) != IntPtr.Zero)
            {
                taskbar = Rectangle.FromLTRB(abd.rc.Left, abd.rc.Top, abd.rc.Right, abd.rc.Bottom);
                _edge = (int)abd.uEdge;
            }
            else
            {
                Screen screen = Screen.PrimaryScreen;
                int h = Math.Max((int)(48 * _scale), screen.Bounds.Bottom - screen.WorkingArea.Bottom);
                taskbar = new Rectangle(screen.Bounds.Left, screen.Bounds.Bottom - h, screen.Bounds.Width, h);
                _edge = EdgeBottom;
            }

            _horizontal = _edge == EdgeTop || _edge == EdgeBottom;
            _thick = _horizontal ? taskbar.Height : taskbar.Width;
            if (_thick < 16) _thick = (int)(48 * _scale);

            switch (_edge)
            {
                case EdgeTop: _finalRect = new Rectangle(taskbar.Left, taskbar.Bottom, taskbar.Width, _thick); break;
                case EdgeLeft: _finalRect = new Rectangle(taskbar.Right, taskbar.Top, _thick, taskbar.Height); break;
                case EdgeRight: _finalRect = new Rectangle(taskbar.Left - _thick, taskbar.Top, _thick, taskbar.Height); break;
                default: _finalRect = new Rectangle(taskbar.Left, taskbar.Top - _thick, taskbar.Width, _thick); break;
            }

            int px = Math.Max(8, (int)Math.Round(_settings.IconSize * _scale));
            if (px != _iconPx)
            {
                _iconPx = px;
                DisposeIcons();
                if (_letterFont != null) _letterFont.Dispose();
                _letterFont = new Font("Segoe UI", Math.Max(6f, px * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel);
            }
            LayoutItems();
            LoadVisibleIcons();
        }

        // 表示される項目のアイコンだけ読み込む（入りきらない分は読まない）
        private void LoadVisibleIcons()
        {
            for (int i = 0; i < _apps.Count && i < _itemRects.Count; i++)
            {
                AppEntry a = _apps[i];
                if (a.Separator || a.IconLoaded || _itemRects[i].IsEmpty) continue;
                a.Icon = IconLoader.Load(a, _iconPx, _appDir);
                a.IconLoaded = true;
            }
        }

        private void LayoutItems()
        {
            _itemRects.Clear();
            int len = _horizontal ? _finalRect.Width : _finalRect.Height;
            int cell = _thick;
            int sep = Math.Max(4, _thick / 4);
            int hideLen = Math.Max((int)(16 * _scale), _thick * 2 / 3);
            int avail = len - hideLen;

            int total = 0;
            foreach (AppEntry a in _apps) total += a.Separator ? sep : cell;

            int pos;
            string align = (_settings.Align ?? "").ToLowerInvariant();
            if (align == "left") pos = (int)(4 * _scale);
            else if (align == "right") pos = avail - total;
            else pos = (len - total) / 2;
            if (pos + total > avail) pos = avail - total;
            if (pos < 0) pos = 0;

            int hidden = 0, items = 0;
            foreach (AppEntry a in _apps)
            {
                int l = a.Separator ? sep : cell;
                bool fits = pos + l <= avail;
                _itemRects.Add(fits ? MapRect(pos, l) : Rectangle.Empty); // 入りきらない分は非表示
                if (!a.Separator)
                {
                    items++;
                    if (!fits) hidden++;
                }
                pos += l;
            }
            HiddenCount = hidden;
            ItemCount = items;
            _hideRect = MapRect(len - hideLen, hideLen);
        }

        private Rectangle MapRect(int pos, int length)
        {
            return _horizontal ? new Rectangle(pos, 0, length, _thick) : new Rectangle(0, pos, _thick, length);
        }

        // ---------------- 表示 / 非表示 ----------------

        public void ShowBar()
        {
            if (_progress <= 0) ComputeGeometry(); // タスクバーの位置・サイズ変更に追従
            StartAnimation(1);
        }

        public void HideBar()
        {
            _tip.Hide(this);
            _hover = -1;
            _pressed = -1;
            StartAnimation(-1);
        }

        private void StartAnimation(int direction)
        {
            _direction = direction;
            if (_settings.AnimationMs <= 0)
            {
                _animTimer.Stop();
                _progress = direction > 0 ? 1 : 0;
                _direction = 0;
                Apply();
                return;
            }
            _startProgress = _progress;
            _stopwatch.Reset();
            _stopwatch.Start();
            _animTimer.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            double p = _startProgress + _direction * _stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, _settings.AnimationMs);
            if (p >= 1 || p <= 0)
            {
                p = p >= 1 ? 1 : 0;
                _animTimer.Stop();
                _stopwatch.Stop();
                _direction = 0;
            }
            _progress = p;
            Apply();
        }

        // タスクバーの裏からせり出すように、ウィンドウの見える幅を変えて描画位置をずらす
        private void Apply()
        {
            double eased = 1 - Math.Pow(1 - _progress, 3); // ease-out
            int visible = (int)Math.Round(_thick * eased);
            if (visible <= 0)
            {
                if (Visible) Visible = false;
                return;
            }

            int x = _finalRect.X, y = _finalRect.Y, w = _finalRect.Width, h = _finalRect.Height;
            _offsetX = 0;
            _offsetY = 0;
            switch (_edge)
            {
                case EdgeTop: h = visible; _offsetY = visible - _thick; break;
                case EdgeLeft: w = visible; _offsetX = visible - _thick; break;
                case EdgeRight: x = _finalRect.Right - visible; w = visible; break;
                default: y = _finalRect.Bottom - visible; h = visible; break;
            }

            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
            if (!Visible) Visible = true;
            Invalidate();
            Update();
        }

        // ---------------- マウス ----------------

        private int HitTest(Point barPoint)
        {
            if (_hideRect.Contains(barPoint)) return HideButton;
            for (int i = 0; i < _itemRects.Count; i++)
            {
                if (!_apps[i].Separator && _itemRects[i].Contains(barPoint)) return i;
            }
            return -1;
        }

        private Point ToBar(Point clientPoint)
        {
            return new Point(clientPoint.X - _offsetX, clientPoint.Y - _offsetY);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(ToBar(e.Location));
            if (hit == _hover) return;
            _hover = hit;
            Invalidate();
            UpdateTip();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            _pressed = -1;
            _tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = HitTest(ToBar(e.Location));
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right && BarMenu != null)
            {
                _tip.Hide(this);
                BarMenu.Show(Cursor.Position);
                NativeMethods.SetForegroundWindow(BarMenu.Handle); // 外側クリックでメニューが閉じるように
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = HitTest(ToBar(e.Location));
            int pressed = _pressed;
            _pressed = -1;
            Invalidate();
            if (hit != pressed) return;

            if (hit == HideButton)
            {
                HideBar();
            }
            else if (hit >= 0)
            {
                Action<AppEntry> handler = ItemClicked;
                if (handler != null) handler(_apps[hit]);
            }
        }

        private void UpdateTip()
        {
            if (_hover == -1)
            {
                _tip.Hide(this);
                return;
            }
            string text = _hover == HideButton ? Strings.Format("tip.hide", TriggerName) : _apps[_hover].Name;
            if (string.IsNullOrEmpty(text))
            {
                _tip.Hide(this);
                return;
            }

            Rectangle r = _hover == HideButton ? _hideRect : _itemRects[_hover];
            r.Offset(_offsetX, _offsetY);
            Size ts = TextRenderer.MeasureText(text, SystemFonts.StatusFont);
            int tw = ts.Width + 12, th = ts.Height + 8, gap = (int)(6 * _scale);
            int x, y;
            switch (_edge)
            {
                case EdgeTop: x = r.X + (r.Width - tw) / 2; y = r.Bottom + gap; break;
                case EdgeLeft: x = r.Right + gap; y = r.Y + (r.Height - th) / 2; break;
                case EdgeRight: x = -tw - gap; y = r.Y + (r.Height - th) / 2; break;
                default: x = r.X + (r.Width - tw) / 2; y = -th - gap; break;
            }
            _tip.Show(text, this, x, y);
        }

        // ---------------- 後始末 ----------------

        private void DisposeIcons()
        {
            foreach (AppEntry a in _apps)
            {
                a.IconLoaded = false;
                if (a.Icon == null) continue;
                a.Icon.Dispose();
                a.Icon = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Dispose();
                _tip.Dispose();
                if (_letterFont != null) _letterFont.Dispose();
                DisposeIcons();
            }
            base.Dispose(disposing);
        }
    }
}
