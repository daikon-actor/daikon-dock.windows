using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DaikonDock
{
    /// <summary>
    /// 画面に出す文言。lang\{言語}.json から読む。
    /// キーごとに「exe 隣の lang\{言語}.json → 埋め込みの {言語} → exe 隣の lang\en.json → 埋め込みの en → キー名」の順で探す。
    /// </summary>
    internal static class Strings
    {
        // build.bat で選んだ言語が既定値になる（/define:DAIKONDOCK_LANG_JA）。設定の "language" で上書きできる。
#if DAIKONDOCK_LANG_JA
        public const string DefaultLanguage = "ja";
#else
        public const string DefaultLanguage = "en";
#endif
        private const string FallbackLanguage = "en";
        private const string ResourcePrefix = "DaikonDock.lang.";

        private static readonly List<IDictionary<string, string>> _tables = new List<IDictionary<string, string>>();

        /// <summary>言語を切り替える。</summary>
        /// <param name="language">"en" / "ja" など。null や空なら既定の言語。</param>
        /// <param name="appDir">exe のあるフォルダ</param>
        /// <returns>外部の言語ファイルが壊れていたときのエラー（無ければ null）</returns>
        public static string Load(string language, string appDir)
        {
            string lang = string.IsNullOrEmpty(language) ? DefaultLanguage : language.Trim().ToLowerInvariant();
            List<string> errors = new List<string>();

            _tables.Clear();
            AddExternal(appDir, lang, errors);
            AddEmbedded(lang);
            if (lang != FallbackLanguage)
            {
                AddExternal(appDir, FallbackLanguage, errors);
                AddEmbedded(FallbackLanguage);
            }
            return errors.Count > 0 ? string.Join("\n\n", errors.ToArray()) : null;
        }

        public static string Get(string key)
        {
            foreach (IDictionary<string, string> table in _tables)
            {
                string value;
                if (table.TryGetValue(key, out value)) return value;
            }
            return key;
        }

        public static string Format(string key, params object[] args)
        {
            string text = Get(key);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, args);
            }
            catch (FormatException)
            {
                return text; // 言語ファイルの { } の書き間違いで落ちないように
            }
        }

        private static void AddExternal(string appDir, string lang, List<string> errors)
        {
            string path = Path.Combine(Path.Combine(appDir, "lang"), lang + ".json");
            if (!File.Exists(path)) return;
            try
            {
                _tables.Add(Parse(File.ReadAllText(path, Encoding.UTF8)));
            }
            catch (Exception ex)
            {
                errors.Add("lang\\" + lang + ".json\n  " + ex.Message);
            }
        }

        private static void AddEmbedded(string lang)
        {
            using (Stream stream = typeof(Strings).Assembly.GetManifestResourceStream(ResourcePrefix + lang + ".json"))
            {
                if (stream == null) return;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    _tables.Add(Parse(reader.ReadToEnd()));
                }
            }
        }

        private static IDictionary<string, string> Parse(string json)
        {
            Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            IDictionary<string, object> root = serializer.DeserializeObject(json) as IDictionary<string, object>;
            if (root == null) return table;
            foreach (KeyValuePair<string, object> kv in root)
            {
                string value = kv.Value as string;
                if (value != null) table[kv.Key] = value;
            }
            return table;
        }
    }
}
