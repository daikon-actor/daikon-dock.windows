using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickBar
{
    /// <summary>
    /// 長押しで表示を切り替えるキー（設定の triggerKey）。修飾キーだけを対象にする。
    /// 文字キーなどは長押しで文字が連続入力されてしまうため選べない。
    /// </summary>
    internal sealed class TriggerKey
    {
        private const int SideBoth = 0;
        private const int SideLeft = 1;
        private const int SideRight = 2;

        public static readonly TriggerKey Default = new TriggerKey("ctrl", "Ctrl", SideBoth, new[] { 0x11, 0xA2, 0xA3 }, false);

        public readonly string Id;
        public readonly int[] KeyCodes;   // フックに来る仮想キーコード（状態の確認にも使う）
        public readonly bool NeedsMask;   // 離したときにメニュー / スタートが開くキー（Alt / Win）
        private readonly string _baseName;
        private readonly int _side;

        private TriggerKey(string id, string baseName, int side, int[] keyCodes, bool needsMask)
        {
            Id = id;
            _baseName = baseName;
            _side = side;
            KeyCodes = keyCodes;
            NeedsMask = needsMask;
        }

        /// <summary>"ctrl" "lctrl" "rshift" "win" など。対応していない名前なら null。</summary>
        public static TriggerKey Parse(string id)
        {
            switch ((id ?? "").Trim().ToLowerInvariant())
            {
                case "ctrl": return Default;
                case "lctrl": return new TriggerKey("lctrl", "Ctrl", SideLeft, new[] { 0xA2 }, false);
                case "rctrl": return new TriggerKey("rctrl", "Ctrl", SideRight, new[] { 0xA3 }, false);
                case "shift": return new TriggerKey("shift", "Shift", SideBoth, new[] { 0x10, 0xA0, 0xA1 }, false);
                case "lshift": return new TriggerKey("lshift", "Shift", SideLeft, new[] { 0xA0 }, false);
                case "rshift": return new TriggerKey("rshift", "Shift", SideRight, new[] { 0xA1 }, false);
                case "alt": return new TriggerKey("alt", "Alt", SideBoth, new[] { 0x12, 0xA4, 0xA5 }, true);
                case "lalt": return new TriggerKey("lalt", "Alt", SideLeft, new[] { 0xA4 }, true);
                case "ralt": return new TriggerKey("ralt", "Alt", SideRight, new[] { 0xA5 }, true);
                case "win": return new TriggerKey("win", "Win", SideBoth, new[] { 0x5B, 0x5C }, true);
                case "lwin": return new TriggerKey("lwin", "Win", SideLeft, new[] { 0x5B }, true);
                case "rwin": return new TriggerKey("rwin", "Win", SideRight, new[] { 0x5C }, true);
                default: return null;
            }
        }

        /// <summary>画面に出す名前（"Ctrl"、"Left Ctrl" / "左Ctrl" など）。</summary>
        public string DisplayName
        {
            get
            {
                if (_side == SideLeft) return Strings.Format("key.left", _baseName);
                if (_side == SideRight) return Strings.Format("key.right", _baseName);
                return _baseName;
            }
        }

        public bool Matches(int vk)
        {
            return Array.IndexOf(KeyCodes, vk) >= 0;
        }

        public bool IsPhysicallyDown()
        {
            foreach (int vk in KeyCodes)
            {
                if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// トリガーキーの長押しを検出する。低レベルキーボードフックによるイベント駆動なので待機中の負荷はほぼ0。
    /// </summary>
    internal sealed class HoldKeyDetector : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int VK_MASK = 0xE8;          // 未割り当てのキー。Alt / Win を「単独で押した」扱いにしないために送る
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public event EventHandler LongPress;

        private readonly NativeMethods.HookProc _keyboardProc; // GCされないようフィールドで保持
        private readonly NativeMethods.HookProc _mouseProc;
        private readonly Timer _timer = new Timer();
        private TriggerKey _trigger = TriggerKey.Default;
        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;
        private bool _keyDown;
        private bool _cancelled;

        public HoldKeyDetector()
        {
            _keyboardProc = KeyboardCallback;
            _mouseProc = MouseCallback;
            _timer.Interval = 600;
            _timer.Tick += OnTick;
            _keyboardHook = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, NativeMethods.GetModuleHandle(null), 0);
        }

        /// <summary>キーボードフックを設定できたか。</summary>
        public bool IsHooked
        {
            get { return _keyboardHook != IntPtr.Zero; }
        }

        public int HoldMs
        {
            set { _timer.Interval = Math.Max(150, value); }
        }

        public TriggerKey Trigger
        {
            get { return _trigger; }
            set
            {
                if (value == null || value.Id == _trigger.Id) return;
                _trigger = value;
                Reset();
            }
        }

        private void Reset()
        {
            _keyDown = false;
            _cancelled = false;
            _timer.Stop();
            UnhookMouse();
        }

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                bool down = msg == 0x100 || msg == 0x104; // WM_KEYDOWN / WM_SYSKEYDOWN
                bool up = msg == 0x101 || msg == 0x105;   // WM_KEYUP / WM_SYSKEYUP

                if (vk == VK_MASK)
                {
                    // 自分で送ったマスク用のキーは無視
                }
                else if (_trigger.Matches(vk))
                {
                    // キーリピートは無視。フック内では非同期キー状態がまだ更新前なので
                    // 「押されていない」なら新規押下と判断できる（取りこぼし時の復帰用）
                    if (down && (!_keyDown || !_trigger.IsPhysicallyDown()))
                    {
                        _keyDown = true;
                        _cancelled = false;
                        _timer.Stop();
                        _timer.Start();
                        HookMouse();
                    }
                    else if (up)
                    {
                        _keyDown = false;
                        _timer.Stop();
                        UnhookMouse();
                    }
                }
                else if (down && _keyDown)
                {
                    // Ctrl+C などのショートカット中は発動しない
                    _cancelled = true;
                    _timer.Stop();
                    UnhookMouse();
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int m = wParam.ToInt32();
                // Ctrl+クリック（複数選択）や Ctrl+ホイール（拡大）中は発動しない
                if (m == 0x201 || m == 0x204 || m == 0x207 || m == 0x20A || m == 0x20B || m == 0x20E)
                {
                    _cancelled = true;
                    _timer.Stop();
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void OnTick(object sender, EventArgs e)
        {
            _timer.Stop();
            UnhookMouse();
            if (!_trigger.IsPhysicallyDown())
            {
                _keyDown = false;
                return;
            }
            if (_keyDown && !_cancelled)
            {
                _cancelled = true; // 押しっぱなしで連続発動しない

                // Alt / Win は、そのまま離すとメニューバーやスタートメニューが開くので、
                // 別のキーを押したことにして打ち消す
                if (_trigger.NeedsMask)
                {
                    NativeMethods.keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
                    NativeMethods.keybd_event(VK_MASK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }

                EventHandler handler = LongPress;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        // マウスフックはトリガーキーを押している間だけ設定する（普段は負荷ゼロ）
        private void HookMouse()
        {
            if (_mouseHook == IntPtr.Zero)
                _mouseHook = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, NativeMethods.GetModuleHandle(null), 0);
        }

        private void UnhookMouse()
        {
            if (_mouseHook == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            UnhookMouse();
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }
    }
}
