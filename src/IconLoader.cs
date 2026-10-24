using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DaikonDock
{
    /// <summary>
    /// 各項目のアイコンを取得し、表示サイズのビットマップにして返す。
    /// 読み込み時に一度だけ呼ばれ、描画時は作ったものを使い回す。
    /// </summary>
    internal static class IconLoader
    {
        private const int ShilExtraLarge = 2; // 48px
        private const int ShilJumbo = 4;      // 256px
        private const int IldTransparent = 1;
        private const uint FileAttributeNormal = 0x80;

        public static Bitmap Load(AppEntry entry, int px, string baseDir)
        {
            Bitmap src = null;
            try
            {
                if (!string.IsNullOrEmpty(entry.IconSpec)) src = FromSpec(entry.IconSpec, px, baseDir);
                if (src == null) src = FromShell(entry.Target, px);
            }
            catch (Exception)
            {
                // 壊れた画像などは頭文字表示にフォールバック
                src = null;
            }
            if (src == null) return null;
            try { return Fit(src, px); }
            finally { src.Dispose(); }
        }

        // "icon": "xxx.png" / "xxx.ico" / "xxx.exe" / "xxx.dll,3"
        private static Bitmap FromSpec(string spec, int px, string baseDir)
        {
            string path = Environment.ExpandEnvironmentVariables(spec.Trim()).Replace('/', '\\');
            int index = 0;
            bool hasIndex = false;
            int comma = path.LastIndexOf(',');
            if (comma > 0)
            {
                int n;
                if (int.TryParse(path.Substring(comma + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                {
                    index = n;
                    hasIndex = true;
                    path = path.Substring(0, comma).Trim();
                }
            }
            if (!Path.IsPathRooted(path)) path = Path.GetFullPath(Path.Combine(baseDir, path));
            if (!File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
            {
                byte[] bytes = File.ReadAllBytes(path); // ファイルをロックしない
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image img = Image.FromStream(ms))
                {
                    return new Bitmap(img);
                }
            }
            if (hasIndex || ext == ".ico")
            {
                IntPtr[] handles = new IntPtr[1];
                uint[] ids = new uint[1];
                uint got = NativeMethods.PrivateExtractIcons(path, index, px, px, handles, ids, 1, 0);
                if (got == 0 || got == 0xFFFFFFFF || handles[0] == IntPtr.Zero) return null;
                try
                {
                    using (Icon ic = Icon.FromHandle(handles[0])) return ic.ToBitmap();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handles[0]);
                }
            }
            return FromShell(path, px);
        }

        // エクスプローラーと同じアイコン（exe / lnk / フォルダ / ファイル / URL）
        private static Bitmap FromShell(string path, int px)
        {
            if (string.IsNullOrEmpty(path)) return null;
            uint attr = 0;
            uint flags = NativeMethods.SHGFI_SYSICONINDEX;
            string query = path;
            bool exists;
            try { exists = File.Exists(path) || Directory.Exists(path); }
            catch (ArgumentException) { exists = false; }

            if (!exists)
            {
                string lower = path.ToLowerInvariant();
                if (!lower.StartsWith("http://", StringComparison.Ordinal) &&
                    !lower.StartsWith("https://", StringComparison.Ordinal))
                    return null; // ms-settings: などは頭文字表示
                query = ".html"; // URLは既定ブラウザのアイコン
                attr = FileAttributeNormal;
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            }

            NativeMethods.SHFILEINFO info = new NativeMethods.SHFILEINFO();
            IntPtr r = NativeMethods.SHGetFileInfo(query, attr, ref info, (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)), flags);
            if (r == IntPtr.Zero) return null;

            int listId = px > 48 ? ShilJumbo : ShilExtraLarge;
            Guid iid = typeof(NativeMethods.IImageList).GUID;
            NativeMethods.IImageList list;
            if (NativeMethods.SHGetImageList(listId, ref iid, out list) != 0 || list == null) return null;

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                if (list.GetIcon(info.iIcon, IldTransparent, ref hIcon) != 0 || hIcon == IntPtr.Zero) return null;
                using (Icon ic = Icon.FromHandle(hIcon)) return ic.ToBitmap();
            }
            finally
            {
                if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
                Marshal.ReleaseComObject(list);
            }
        }

        // 表示サイズに高品質縮小（縦横比は維持して中央寄せ）
        private static Bitmap Fit(Bitmap src, int px)
        {
            Bitmap dst = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dst))
            using (ImageAttributes ia = new ImageAttributes())
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                double k = Math.Min((double)px / src.Width, (double)px / src.Height);
                int w = Math.Max(1, (int)Math.Round(src.Width * k));
                int h = Math.Max(1, (int)Math.Round(src.Height * k));
                g.DrawImage(src, new Rectangle((px - w) / 2, (px - h) / 2, w, h),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }
    }
}
