using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickBar
{
    /// <summary>
    /// 常駐部分のつなぎ役。トレイアイコン・メニュー・長押し検出・設定・バーを結び付ける。
    /// </summary>
    internal sealed class AppController : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "QuickBar";
        private const string IconResourceName = "QuickBar.app.ico";

        private readonly string _appDir;
        private readonly ConfigStore _config;
        private readonly BarForm _bar;
        private readonly HoldKeyDetector _detector;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _reloadItem;
        private readonly ToolStripMenuItem _openDataItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly Icon _trayIcon;
        private BarSettings _settings = new BarSettings();
        private int _lastHiddenCount;

        public AppController()
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            Strings.Load(null, _appDir); // 設定を読むまでは既定の言語
            _config = new ConfigStore(Path.Combine(_appDir, "data"), _appDir);
            _config.EnsureSample();

            _bar = new BarForm(_appDir);
            _bar.ItemClicked += OnItemClicked;

            _detector = new HoldKeyDetector();
            _detector.LongPress += delegate { Toggle(); };
            if (!_detector.IsHooked)
                MessageBox.Show(Strings.Get("error.hook"), "QuickBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            _menu = new ContextMenuStrip();
            ToolStripMenuItem versionItem = new ToolStripMenuItem("QuickBar " + Application.ProductVersion);
            versionItem.Enabled = false; // バージョン表示のみ
            _menu.Items.Add(versionItem);
            _menu.Items.Add(new ToolStripSeparator());
            _toggleItem = AddMenuItem(delegate { Toggle(); });
            _reloadItem = AddMenuItem(delegate { Reload(true); });
            _openDataItem = AddMenuItem(delegate { OpenDataFolder(); });
            _menu.Items.Add(new ToolStripSeparator());
            _startupItem = AddMenuItem(delegate { SetStartup(!IsStartup()); });
            _menu.Items.Add(new ToolStripSeparator());
            _exitItem = AddMenuItem(delegate { ExitApp(); });
            _menu.Opening += delegate { _startupItem.Checked = IsStartup(); };
            _bar.BarMenu = _menu;

            _trayIcon = LoadAppIcon();
            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Toggle();
            };
            _tray.Visible = true;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            Reload(true);
        }

        private void Toggle()
        {
            if (_bar.IsOpen)
            {
                _bar.HideBar();
            }
            else
            {
                Reload(false); // JSONが変更されていれば読み直してから表示
                _bar.ShowBar();
            }
        }

        private void Reload(bool force)
        {
            if (!force && !_config.HasChanged()) return;

            ConfigResult result = _config.Load();
            _settings = result.Settings;

            string langError = Strings.Load(result.Settings.Language, _appDir);
            if (langError != null) result.Errors.Add(langError);

            TriggerKey trigger = TriggerKey.Parse(result.Settings.TriggerKey);
            if (trigger == null)
            {
                result.Errors.Add(Strings.Format("error.triggerKey", result.Settings.TriggerKey));
                trigger = TriggerKey.Default;
            }
            _detector.Trigger = trigger;
            _detector.HoldMs = result.Settings.HoldMs;
            _bar.TriggerName = trigger.DisplayName;
            ApplyTexts();

            _bar.SetConfig(result.Settings, result.Apps);

            if (result.Errors.Count > 0)
            {
                MessageBox.Show(
                    Strings.Format("error.configLoad", string.Join("\n\n", result.Errors.ToArray())),
                    "QuickBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _lastHiddenCount = 0; // 設定を読み直したら、入りきらなければ必ず知らせる
            WarnIfOverflow();
        }

        // バーに入りきらない項目があれば知らせる（同じ状態では繰り返さない）
        private void WarnIfOverflow()
        {
            int hidden = _bar.HiddenCount;
            bool increased = hidden > _lastHiddenCount;
            _lastHiddenCount = hidden;
            if (!increased) return;
            MessageBox.Show(Strings.Format("warn.overflow", hidden, _bar.ItemCount),
                            "QuickBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnItemClicked(AppEntry entry)
        {
            string error;
            if (Launcher.Launch(entry, out error))
            {
                if (_settings.HideOnLaunch) _bar.HideBar();
            }
            else if (error != null)
            {
                MessageBox.Show(Strings.Format("error.launch", entry.Target, error),
                                "QuickBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private ToolStripMenuItem AddMenuItem(EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem();
            item.Click += onClick;
            _menu.Items.Add(item);
            return item;
        }

        // 言語の切り替え時に、メニューとトレイの文言を入れ直す
        private void ApplyTexts()
        {
            _toggleItem.Text = Strings.Get("menu.toggle");
            _reloadItem.Text = Strings.Get("menu.reload");
            _openDataItem.Text = Strings.Get("menu.openData");
            _startupItem.Text = Strings.Get("menu.startup");
            _exitItem.Text = Strings.Get("menu.exit");

            string trayText = Strings.Format("tray.text", _detector.Trigger.DisplayName);
            _tray.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText; // NotifyIcon の上限
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            _bar.RefreshGeometry();
            WarnIfOverflow(); // 解像度や表示倍率の変更で入りきらなくなった場合
        }

        private void OpenDataFolder()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_config.DataDir);
                psi.UseShellExecute = true;
                using (Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "QuickBar");
            }
        }

        private static bool IsStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key != null && key.GetValue(RunValueName) != null;
            }
        }

        private static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) return;
                if (enabled) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunValueName, false);
            }
        }

        // exe に埋め込んだ app.ico からトレイ用の小さいサイズを取り出す
        private static Icon LoadAppIcon()
        {
            using (Stream stream = typeof(AppController).Assembly.GetManifestResourceStream(IconResourceName))
            {
                if (stream != null) return new Icon(stream, SystemInformation.SmallIconSize);
            }
            return (Icon)SystemIcons.Application.Clone();
        }

        private void ExitApp()
        {
            Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _detector.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _menu.Dispose();
                _bar.Dispose();
                _trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
