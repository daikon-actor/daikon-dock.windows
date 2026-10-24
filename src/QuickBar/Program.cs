// QuickBar - Ctrl長押しでタスクバーの上にランチャーバーを表示する常駐アプリ
// Windows付属の csc.exe (C# 5) でビルドできるよう、C# 5 の範囲で書くこと。

using System;
using System.Threading;
using System.Windows.Forms;

namespace QuickBar
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "QuickBar_SingleInstance_7F3A", out created))
            {
                if (!created) return; // 二重起動しない

                try { NativeMethods.SetProcessDPIAware(); }
                catch (EntryPointNotFoundException) { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AppController());
                GC.KeepAlive(mutex);
            }
        }
    }
}
