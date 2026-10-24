using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace QuickBar
{
    /// <summary>項目を起動する。</summary>
    internal static class Launcher
    {
        private const int ErrorCancelled = 1223; // UACダイアログでキャンセル

        /// <returns>起動できたら true。失敗時は error に理由（キャンセル時は null）。</returns>
        public static bool Launch(AppEntry entry, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(entry.Target);
                psi.UseShellExecute = true;
                if (!string.IsNullOrEmpty(entry.Args))
                    psi.Arguments = Environment.ExpandEnvironmentVariables(entry.Args);

                string workDir = string.IsNullOrEmpty(entry.WorkDir) ? null : Environment.ExpandEnvironmentVariables(entry.WorkDir);
                if (workDir == null && File.Exists(entry.Target)) workDir = Path.GetDirectoryName(entry.Target);
                if (workDir != null) psi.WorkingDirectory = workDir;

                if (entry.RunAs) psi.Verb = "runas";
                using (Process.Start(psi)) { }
                return true;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != ErrorCancelled) error = ex.Message;
                return false;
            }
            catch (Exception ex) // 不正なパスなど。原因に関わらずダイアログで知らせる
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>JSON の path を実際のパス / URL に解決する。</summary>
    internal static class PathResolver
    {
        private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\";

        // "https://..." "ms-settings:" "shell:startup" など（ドライブ文字 "C:" は除く）
        public static bool IsUri(string path)
        {
            int i = path.IndexOf(':');
            if (i < 2) return false;
            for (int k = 0; k < i; k++)
            {
                char c = path[k];
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.')) return false;
            }
            return true;
        }

        public static string Resolve(string raw, string baseDir)
        {
            string p = Environment.ExpandEnvironmentVariables(raw.Trim());
            if (IsUri(p)) return p;
            p = p.Replace('/', '\\');
            try
            {
                if (Path.IsPathRooted(p)) return p;
                if (p.IndexOf('\\') >= 0) return Path.GetFullPath(Path.Combine(baseDir, p)); // アプリフォルダ基準
                string found = FindExecutable(p, baseDir);
                return found ?? p;
            }
            catch (ArgumentException)
            {
                return p;
            }
            catch (NotSupportedException)
            {
                return p;
            }
        }

        // "notepad.exe" のような名前だけの指定を PATH と App Paths から探す（アイコン取得用）
        private static string FindExecutable(string name, string baseDir)
        {
            List<string> names = new List<string>();
            names.Add(name);
            if (Path.GetExtension(name).Length == 0) names.Add(name + ".exe");

            List<string> dirs = new List<string>();
            dirs.Add(baseDir);
            dirs.Add(Environment.SystemDirectory);
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));

            foreach (string dir in dirs)
            {
                string d = Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'));
                if (d.Length == 0) continue;
                foreach (string n in names)
                {
                    try
                    {
                        string candidate = Path.Combine(d, n);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }

            foreach (string n in names)
            {
                foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    string v = ReadAppPath(root, n);
                    if (v != null) return v;
                }
            }
            return null;
        }

        private static string ReadAppPath(RegistryKey root, string name)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(AppPathsKey + name))
                {
                    if (key == null) return null;
                    string v = key.GetValue(null) as string;
                    if (string.IsNullOrEmpty(v)) return null;
                    v = Environment.ExpandEnvironmentVariables(v.Trim().Trim('"'));
                    return File.Exists(v) ? v : null;
                }
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }
        }
    }
}
