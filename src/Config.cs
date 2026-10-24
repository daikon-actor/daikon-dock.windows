using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DaikonDock
{
    /// <summary>バーに並べる1項目（アプリ / 区切り線）。</summary>
    internal sealed class AppEntry
    {
        public string Name;
        public string RawPath;   // JSONに書かれた path
        public string Target;    // 解決済みのパス / URL
        public string Args;
        public string WorkDir;
        public string IconSpec;
        public bool RunAs;
        public bool Separator;
        public Bitmap Icon;      // 表示サイズで作ったキャッシュ
        public bool IconLoaded;  // 取得を試したか（失敗したものを毎回読み直さない）
    }

    /// <summary>JSON の settings。未指定の項目は既定値。</summary>
    internal sealed class BarSettings
    {
        public string TriggerKey = "ctrl";
        public int HoldMs = 600;
        public int AnimationMs = 180;
        public int IconSize = 24;
        public bool HideOnLaunch = true;
        public string Align = "center";
        public string Language;  // null なら build.bat で選んだ言語
        public double Opacity = 1.0;
        public Color Background = Color.FromArgb(32, 32, 32);
        public Color Hover = Color.FromArgb(58, 58, 58);
        public Color Pressed = Color.FromArgb(46, 46, 46);
        public Color Border = Color.FromArgb(64, 64, 64);
        public Color Foreground = Color.White;
    }

    internal sealed class ConfigResult
    {
        public BarSettings Settings = new BarSettings();
        public List<AppEntry> Apps = new List<AppEntry>();
        public List<string> Errors = new List<string>();
    }

    /// <summary>data フォルダ直下の *.json（*.schema.json を除く）をファイル名順に読み込む。</summary>
    internal sealed class ConfigStore
    {
        private readonly string _dataDir;
        private readonly string _appDir;
        private string _signature;

        public ConfigStore(string dataDir, string appDir)
        {
            _dataDir = dataDir;
            _appDir = appDir;
        }

        public string DataDir
        {
            get { return _dataDir; }
        }

        // 設定ファイルが1つも無ければサンプルを置く
        public void EnsureSample()
        {
            try
            {
                if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
                if (ConfigFiles().Length == 0)
                    File.WriteAllText(Path.Combine(_dataDir, "apps.json"), BuildSampleJson(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // data フォルダに設定が1つも無いときの最小サンプル（名前は現在の言語）
        private static string BuildSampleJson()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n  \"$schema\": \"./apps.schema.json\",\r\n  \"apps\": [\r\n");
            AppendSample(sb, js, "sample.notepad", "notepad.exe", true);
            AppendSample(sb, js, "sample.calc", "calc.exe", true);
            AppendSample(sb, js, "sample.explorer", "explorer.exe", false);
            sb.Append("  ]\r\n}\r\n");
            return sb.ToString();
        }

        private static void AppendSample(StringBuilder sb, JavaScriptSerializer js, string nameKey, string path, bool comma)
        {
            sb.Append("    {\r\n      \"name\": ").Append(js.Serialize(Strings.Get(nameKey)))
              .Append(",\r\n      \"path\": ").Append(js.Serialize(path))
              .Append("\r\n    }").Append(comma ? "," : "").Append("\r\n");
        }

        public bool HasChanged()
        {
            return ComputeSignature() != _signature;
        }

        public ConfigResult Load()
        {
            _signature = ComputeSignature();
            ConfigResult result = new ConfigResult();
            foreach (string file in ConfigFiles())
            {
                try
                {
                    ConfigParser.ParseFile(file, result.Settings, result.Apps, _appDir);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(Path.GetFileName(file) + "\n  " + ex.Message);
                }
            }
            return result;
        }

        private string[] ConfigFiles()
        {
            if (!Directory.Exists(_dataDir)) return new string[0];
            List<string> files = new List<string>();
            foreach (string f in Directory.GetFiles(_dataDir, "*.json"))
            {
                if (!f.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) files.Add(f);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files.ToArray();
        }

        // ファイル名と更新日時から作る変更検知用の文字列
        private string ComputeSignature()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string f in ConfigFiles())
                    sb.Append(f).Append('|').Append(File.GetLastWriteTimeUtc(f).Ticks).Append(';');
                return sb.ToString();
            }
            catch (IOException)
            {
                return "";
            }
            catch (UnauthorizedAccessException)
            {
                return "";
            }
        }
    }

    internal static class ConfigParser
    {
        public static void ParseFile(string file, BarSettings settings, List<AppEntry> list, string baseDir)
        {
            string json = File.ReadAllText(file, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object root = serializer.DeserializeObject(json);

            object appsObj;
            IDictionary<string, object> dict = root as IDictionary<string, object>;
            if (dict != null)
            {
                IDictionary<string, object> sd = Get(dict, "settings") as IDictionary<string, object>;
                if (sd != null) ApplySettings(settings, sd);
                appsObj = Get(dict, "apps");
            }
            else
            {
                appsObj = root; // ルートが配列でもOK
            }

            IEnumerable items = appsObj as IEnumerable;
            if (items == null || appsObj is string || appsObj is IDictionary) return;

            foreach (object o in items)
            {
                IDictionary<string, object> ad = o as IDictionary<string, object>;
                if (ad == null) continue;
                AppEntry a = ParseEntry(ad, baseDir);
                if (a != null) list.Add(a);
            }
        }

        private static AppEntry ParseEntry(IDictionary<string, object> ad, string baseDir)
        {
            AppEntry a = new AppEntry();
            a.Separator = GetBool(ad, "separator", false);
            if (a.Separator) return a;

            a.RawPath = GetStr(ad, "path");
            if (string.IsNullOrEmpty(a.RawPath)) return null;
            a.Name = GetStr(ad, "name");
            a.Args = GetStr(ad, "args");
            a.WorkDir = GetStr(ad, "workdir");
            a.IconSpec = GetStr(ad, "icon");
            a.RunAs = GetBool(ad, "admin", false);
            a.Target = PathResolver.Resolve(a.RawPath, baseDir);
            if (string.IsNullOrEmpty(a.Name))
            {
                try { a.Name = Path.GetFileNameWithoutExtension(a.Target); }
                catch (ArgumentException) { a.Name = a.RawPath; }
            }
            return a;
        }

        private static void ApplySettings(BarSettings s, IDictionary<string, object> d)
        {
            s.TriggerKey = GetStr(d, "triggerKey") ?? s.TriggerKey;
            s.HoldMs = GetInt(d, "holdMs", s.HoldMs);
            s.AnimationMs = GetInt(d, "animationMs", s.AnimationMs);
            s.IconSize = Math.Max(8, Math.Min(128, GetInt(d, "iconSize", s.IconSize)));
            s.HideOnLaunch = GetBool(d, "hideOnLaunch", s.HideOnLaunch);
            s.Align = GetStr(d, "align") ?? s.Align;
            s.Language = GetStr(d, "language") ?? s.Language;
            s.Opacity = GetDouble(d, "opacity", s.Opacity);
            s.Background = GetColor(d, "background", s.Background);
            s.Hover = GetColor(d, "hover", s.Hover);
            s.Pressed = GetColor(d, "pressed", s.Pressed);
            s.Border = GetColor(d, "border", s.Border);
            s.Foreground = GetColor(d, "foreground", s.Foreground);
        }

        // キーの大文字小文字は区別しない
        private static object Get(IDictionary<string, object> d, string key)
        {
            foreach (KeyValuePair<string, object> kv in d)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            return null;
        }

        private static string GetStr(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static int GetInt(IDictionary<string, object> d, string key, int def)
        {
            object v = Get(d, key);
            if (v == null) return def;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (FormatException) { return def; }
            catch (InvalidCastException) { return def; }
            catch (OverflowException) { return def; }
        }

        private static double GetDouble(IDictionary<string, object> d, string key, double def)
        {
            object v = Get(d, key);
            if (v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (FormatException) { return def; }
            catch (InvalidCastException) { return def; }
        }

        private static bool GetBool(IDictionary<string, object> d, string key, bool def)
        {
            object v = Get(d, key);
            if (v is bool) return (bool)v;
            if (v == null) return def;
            bool b;
            return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out b) ? b : def;
        }

        private static Color GetColor(IDictionary<string, object> d, string key, Color def)
        {
            string v = GetStr(d, key);
            if (string.IsNullOrEmpty(v)) return def;
            try { return ColorTranslator.FromHtml(v.Trim()); }
            catch (Exception) { return def; } // FromHtml は不正値で Exception を投げる
        }
    }
}
