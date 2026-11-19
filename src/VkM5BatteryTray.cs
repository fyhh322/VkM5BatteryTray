using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VkM5Battery
{
    internal static class Native
    {
        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(IntPtr device, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, byte[] capabilities);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetFeature(IntPtr device, byte[] buffer, int bufferLength);

        [DllImport("hid.dll")]
        public static extern bool HidD_SetFeature(IntPtr device, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfoSet, IntPtr devInfo, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr devInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;

        // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2. Without this the
        // tray asks for a 16 px icon and stretches whatever single frame it
        // gets, which is what blurred the number on a 125% display.
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (Exception) { }
            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref ICONINFO info);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPel, IntPtr bits);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr wnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr wnd, IntPtr dc);

        public const uint DIB_RGB_COLORS = 0;
        public const int BI_RGB = 0;
    }

    internal sealed class HidIface
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort Version;
        public string Product;
        public ushort UsagePage;
        public ushort Usage;
        public int InputLen;
        public int OutputLen;
        public int FeatureLen;
    }

    internal static class HidScan
    {
        public static List<HidIface> Enumerate()
        {
            List<HidIface> list = new List<HidIface>();
            Guid guid;
            Native.HidD_GetHidGuid(out guid);

            IntPtr set = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == Native.INVALID_HANDLE) return list;

            try
            {
                for (int i = 0; ; i++)
                {
                    Native.SP_DEVICE_INTERFACE_DATA did = new Native.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;

                    int required = 0;
                    Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, required, out required, IntPtr.Zero))
                            continue;

                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        HidIface info = Inspect(path);
                        if (info != null) list.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }

            return list;
        }

        private static HidIface Inspect(string path)
        {
            IntPtr h = Native.CreateFileW(path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return null;

            try
            {
                HidIface info = new HidIface();
                info.Path = path;

                Native.HIDD_ATTRIBUTES attr = new Native.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                if (!Native.HidD_GetAttributes(h, ref attr)) return null;
                info.Vid = attr.VendorID;
                info.Pid = attr.ProductID;
                info.Version = attr.VersionNumber;

                StringBuilder sb = new StringBuilder(260);
                if (Native.HidD_GetProductString(h, sb, 520)) info.Product = sb.ToString();

                IntPtr pre = IntPtr.Zero;
                if (Native.HidD_GetPreparsedData(h, out pre) && pre != IntPtr.Zero)
                {
                    try
                    {
                        byte[] caps = new byte[256];
                        if (Native.HidP_GetCaps(pre, caps) >= 0)
                        {
                            // HIDP_CAPS: Usage@0, UsagePage@2, Input@4, Output@6, Feature@8
                            info.InputLen = BitConverter.ToInt16(caps, 4);
                            info.OutputLen = BitConverter.ToInt16(caps, 6);
                            info.FeatureLen = BitConverter.ToInt16(caps, 8);
                            info.UsagePage = BitConverter.ToUInt16(caps, 2);
                            info.Usage = BitConverter.ToUInt16(caps, 0);
                        }
                    }
                    finally { Native.HidD_FreePreparsedData(pre); }
                }
                return info;
            }
            finally { Native.CloseHandle(h); }
        }
    }

    internal sealed class BatteryState
    {
        public bool Valid;
        public int MouseBattery;
        public int KeyboardBattery;
        public bool MouseOnline;
        public bool KeyboardOnline;
        public int Target;
        public bool CanRead;
        public bool CanSend;
        public bool RfBoot;
        public string RawHex = "";
        public DateTime Stamp = DateTime.MinValue;
    }

    internal sealed class Reader : IDisposable
    {
        public const ushort Vid = 0x374A;
        public const ushort Pid = 0xA322;
        private const byte CmdStatus = 0xF7;

        private IntPtr handle = Native.INVALID_HANDLE;
        public string LastError = "";
        public int FeatureLen { get; private set; }

        public bool EnsureOpen()
        {
            if (handle != Native.INVALID_HANDLE) return true;

            List<HidIface> all = HidScan.Enumerate();
            foreach (HidIface d in all)
            {
                if (d.Vid != Vid || d.Pid != Pid) continue;
                if (d.FeatureLen < 2) continue;

                IntPtr h = Native.CreateFileW(d.Path,
                    Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE) continue;

                handle = h;
                FeatureLen = d.FeatureLen;
                LastError = "";
                return true;
            }

            LastError = "未找到 VK M5 接收器";
            return false;
        }

        public void Close()
        {
            if (handle != Native.INVALID_HANDLE)
            {
                Native.CloseHandle(handle);
                handle = Native.INVALID_HANDLE;
            }
        }

        public BatteryState Poll()
        {
            BatteryState st = new BatteryState();

            if (!EnsureOpen())
            {
                st.RawHex = LastError;
                st.Stamp = DateTime.Now;
                return st;
            }

            int len = FeatureLen;
            byte[] req = new byte[len];
            req[0] = 0x00;
            req[1] = CmdStatus;

            if (!Native.HidD_SetFeature(handle, req, len))
            {
                st.RawHex = "SetFeature 失败 err=" + Marshal.GetLastWin32Error();
                Close();
                st.Stamp = DateTime.Now;
                return st;
            }

            Thread.Sleep(12);

            byte[] resp = new byte[len];
            resp[0] = 0x00;
            if (!Native.HidD_GetFeature(handle, resp, len))
            {
                st.RawHex = "GetFeature 失败 err=" + Marshal.GetLastWin32Error();
                Close();
                st.Stamp = DateTime.Now;
                return st;
            }

            st.RawHex = Hex(resp, Math.Min(len, 24));
            st.Stamp = DateTime.Now;

            int canRead = resp[1];
            int kbBattery = resp[2];
            int msBattery = resp[3];

            st.CanRead = canRead != 0;
            st.KeyboardBattery = kbBattery > 100 ? 100 : kbBattery;
            st.MouseBattery = msBattery > 100 ? 100 : msBattery;
            st.KeyboardOnline = resp[4] == 0;
            st.MouseOnline = resp[5] == 0;
            st.CanSend = resp[6] != 0;
            st.Target = resp[7];
            st.RfBoot = resp[9] != 0;
            st.Valid = msBattery > 0 || kbBattery > 0;

            return st;
        }

        private static string Hex(byte[] b, int n)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(b[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public void Dispose() { Close(); }
    }

    internal static class IconFactory
    {
        private static readonly Color Ink = Color.FromArgb(22, 18, 34);

        public static Color Accent(int level, bool offline)
        {
            if (offline || level < 0) return Color.FromArgb(140, 140, 148);
            if (level <= 10) return Color.FromArgb(240, 64, 64);
            if (level <= 30) return Color.FromArgb(248, 158, 36);
            if (level <= 60) return Color.FromArgb(226, 206, 50);
            return Color.FromArgb(60, 212, 108);
        }

        // 16 px omits the battery shell: only the bold number survives at that
        // size (measured on a real tray). From 24 px up the battery silhouette
        // is added. The bitmap is rendered at the exact small-icon metric (16,
        // 20, 24, 32, 40, 48 or 64 px) so no shell scaling ever touches the
        // glyphs.
        private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 40, 48, 64 };

        public static Icon Build(int level, bool offline)
        {
            int size = Requested();
            try
            {
                using (Bitmap bmp = RenderBitmap(level, offline, size))
                    return FromBitmap(bmp);
            }
            catch (Exception ex)
            {
                Program.Log("图标生成失败: " + ex.Message);
                using (Bitmap bmp = RenderBitmap(level, offline, 32))
                    return FromBitmap(bmp);
            }
        }

        // The shell asks for an icon at the real small-icon metric, so render at
        // exactly that pixel size instead of letting it scale a 32 px bitmap.
        private static int Requested()
        {
            int v = 0;
            try { v = Native.GetSystemMetrics(Native.SM_CXSMICON); }
            catch (Exception) { }
            if (v < 16) v = 16;
            if (v > 64) v = 64;

            int best = Sizes[0];
            for (int i = 0; i < Sizes.Length; i++)
                if (Math.Abs(Sizes[i] - v) < Math.Abs(best - v)) best = Sizes[i];
            return best;
        }

        // Bitmap.GetHicon() drops the alpha channel and leaves a black box on
        // the tray, so build the HICON from a 32bpp DIB whose alpha we fill in
        // ourselves and pair with an all-zero mask.
        private static Icon FromBitmap(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;

            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.biWidth = w;
            bi.biHeight = -h;              // top-down rows
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = Native.BI_RGB;

            IntPtr dc = Native.GetDC(IntPtr.Zero);
            IntPtr bits;
            IntPtr hbmColor = Native.CreateDIBSection(dc, ref bi, Native.DIB_RGB_COLORS,
                                                      out bits, IntPtr.Zero, 0);
            if (hbmColor == IntPtr.Zero || bits == IntPtr.Zero)
            {
                if (hbmColor != IntPtr.Zero) Native.DeleteObject(hbmColor);
                Native.ReleaseDC(IntPtr.Zero, dc);
                throw new InvalidOperationException("CreateDIBSection 失败");
            }

            try
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        CopyRow(data.Scan0 + y * data.Stride, bits + y * w * 4, w * 4);
                }
                finally { bmp.UnlockBits(data); }

                IntPtr hbmMask = Native.CreateBitmap(w, h, 1, 1, IntPtr.Zero);
                if (hbmMask == IntPtr.Zero) throw new InvalidOperationException("CreateBitmap 失败");

                Native.ICONINFO ii = new Native.ICONINFO();
                ii.fIcon = 1;
                ii.hbmMask = hbmMask;
                ii.hbmColor = hbmColor;

                IntPtr hIcon = Native.CreateIconIndirect(ref ii);
                Native.DeleteObject(hbmMask);
                if (hIcon == IntPtr.Zero) throw new InvalidOperationException("CreateIconIndirect 失败");

                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                        return (Icon)tmp.Clone();
                }
                finally { Native.DestroyIcon(hIcon); }
            }
            finally
            {
                Native.DeleteObject(hbmColor);
                Native.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private static void CopyRow(IntPtr src, IntPtr dst, int bytes)
        {
            byte[] buf = new byte[bytes];
            Marshal.Copy(src, buf, 0, bytes);
            // GDI expects premultiplied alpha for icons.
            for (int i = 0; i < bytes; i += 4)
            {
                int a = buf[i + 3];
                if (a == 255) continue;
                if (a == 0) { buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0; continue; }
                buf[i] = (byte)(buf[i] * a / 255);
                buf[i + 1] = (byte)(buf[i + 1] * a / 255);
                buf[i + 2] = (byte)(buf[i + 2] * a / 255);
            }
            Marshal.Copy(buf, 0, dst, bytes);
        }

        private static Bitmap RenderBitmap(int level, bool offline, int S)
        {
            bool unknown = offline || level < 0;
            int pct = unknown ? 0 : Math.Max(0, Math.Min(100, level));

            Bitmap bmp = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (S >= 24)
                {
                    float k = S / 16f;
                    RectangleF body = new RectangleF(0.6f * k, 2.6f * k, 13.0f * k, 10.8f * k);
                    RectangleF cap = new RectangleF(13.9f * k, 5.6f * k, 1.7f * k, 4.8f * k);

                    using (SolidBrush cb = new SolidBrush(Ink)) g.FillRectangle(cb, cap);

                    using (GraphicsPath bp = Round(body, 1.9f * k))
                    {
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(80, 76, 94)))
                            g.FillPath(bg, bp);
                        using (Pen pen = new Pen(Ink, 1.5f * k))
                            g.DrawPath(pen, bp);
                    }

                    if (!unknown && pct > 0)
                    {
                        float ratio = pct / 100f;
                        RectangleF f = new RectangleF(
                            body.X + 1.5f * k, body.Y + 1.5f * k,
                            (body.Width - 3f * k) * ratio, body.Height - 3f * k);
                        if (f.Width > 0.6f * k)
                        {
                            using (GraphicsPath fp = Round(f, 1.1f * k))
                            using (SolidBrush fb = new SolidBrush(Accent(level, offline)))
                                g.FillPath(fb, fp);
                        }
                    }
                }

                string s = unknown ? "?" : pct.ToString();
                Color fill = unknown ? Color.FromArgb(232, 232, 240)
                                     : (S >= 24 ? Color.FromArgb(252, 252, 255) : Accent(level, offline));
                float size = S >= 24
                    ? (s.Length >= 3 ? S * 0.44f : (s.Length == 2 ? S * 0.54f : S * 0.60f))
                    : (s.Length >= 3 ? S * 0.70f : (s.Length == 2 ? S * 0.80f : S * 0.90f));

                // A three-digit reading is the widest case, so it is fitted to
                // the box instead of trusting the nominal point size to fit.
                DrawGlyphs(g, s, size, fill, new PointF(S / 2f, S / 2f),
                           S >= 24 ? S * 0.90f : S * 1.00f,
                           S >= 24 ? S * 0.74f : S * 0.90f);
            }
            return bmp;
        }

        // Centered by the real glyph bounds (not by font metrics) and stroked
        // with a dark halo so light taskbar themes keep the number legible.
        // The path is shrunk uniformly when it would exceed the given box.
        private static void DrawGlyphs(Graphics g, string s, float size, Color fill,
                                       PointF center, float maxWidth, float maxHeight)
        {
            using (Font f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(s, f.FontFamily, (int)FontStyle.Bold, size,
                               PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bb = path.GetBounds();

                float scale = 1f;
                if (bb.Width > maxWidth && bb.Width > 0f) scale = maxWidth / bb.Width;
                if (bb.Height * scale > maxHeight && bb.Height > 0f) scale = maxHeight / bb.Height;

                using (Matrix m = new Matrix())
                {
                    if (scale < 1f)
                    {
                        // Scale about the glyph origin first so the later
                        // centering translate stays exact.
                        m.Scale(scale, scale);
                        path.Transform(m);
                        m.Reset();
                        bb = path.GetBounds();
                        size *= scale;
                    }
                    m.Translate(center.X - bb.Width / 2f - bb.X,
                                center.Y - bb.Height / 2f - bb.Y);
                    path.Transform(m);
                }
                using (Pen halo = new Pen(Color.FromArgb(170, 0, 0, 0), Math.Max(1.1f, size * 0.13f)))
                {
                    halo.LineJoin = LineJoin.Round;
                    g.DrawPath(halo, path);
                }
                using (SolidBrush fb = new SolidBrush(fill)) g.FillPath(fb, path);
            }
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }
    }

    internal static class Program
    {
        private const string AppName = "VK M5 电量";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string RunValue = "VkM5BatteryTray";
        private const string PrefKey = @"Software\VkM5BatteryTray";
        private const string MutexName = "Local\\VkM5BatteryTray_SingleInstance";

        private static NotifyIcon tray;
        private static readonly Reader reader = new Reader();
        private static BatteryState last = new BatteryState();
        private static DateTime lastGood = DateTime.MinValue;
        private static System.Windows.Forms.Timer timer;
        private static Icon currentIcon;
        private static int iconLevel = int.MinValue;
        private static bool iconOffline;
        private static int intervalMs = 2000;
        private static string logPath;
        private static ToolStripMenuItem autoItem;
        private static ToolStripMenuItem pinItem;
        private static ContextMenuStrip menuStrip;
        private static bool busy;
        private static bool loggedFirst;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return;

                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--interval")
                    {
                        int v;
                        if (int.TryParse(args[i + 1], out v) && v >= 500 && v <= 60000) intervalMs = v;
                    }
                }

                logPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "vk-m5-tray.log");
                Log("启动，间隔 " + intervalMs + "ms");

                // Headless control of the startup entry, so a reinstall or a
                // repair script can set it without opening the tray menu.
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] != "--autostart") continue;
                    string v = args[i + 1].ToLowerInvariant();
                    SetAutoStart(v == "on" || v == "1" || v == "true");
                    return;
                }

                SyncAutoStartPath();

                tray = new NotifyIcon();
                tray.Text = AppName;
                BuildMenu();

                // Read once before the icon becomes visible: the shell snapshots
                // the first icon it sees, and that snapshot is what the tray
                // keeps showing until the reading changes.
                Refresh();
                tray.Visible = true;

                timer = new System.Windows.Forms.Timer();
                timer.Interval = intervalMs;
                timer.Tick += delegate { Refresh(); };
                timer.Start();

                Application.ApplicationExit += delegate { Cleanup(); };
                Application.Run();
            }
        }

        private static void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem details = new ToolStripMenuItem("显示详情");
            details.Click += delegate { ShowDetails(); };
            menu.Items.Add(details);

            ToolStripMenuItem now = new ToolStripMenuItem("立即刷新");
            now.Click += delegate { Refresh(); };
            menu.Items.Add(now);

            pinItem = new ToolStripMenuItem("常驻任务栏（不进溢出区）");
            pinItem.Click += delegate
            {
                SetPinned(!IsPinned());
                RefreshMenuState();
            };
            menu.Items.Add(pinItem);

            menu.Items.Add(new ToolStripSeparator());

            autoItem = new ToolStripMenuItem("开机自启");
            autoItem.Click += delegate
            {
                SetAutoStart(!IsAutoStart());
                RefreshMenuState();
            };
            menu.Items.Add(autoItem);

            // State is re-read every time the menu opens, so a change made by
            // --autostart, by task manager, or by the path-repair at startup is
            // reflected without restarting the tray.
            menu.Opening += delegate
            {
                RefreshMenuState();
                CheckAutoStartHealth();
            };
            menuStrip = menu;

            ToolStripMenuItem openLog = new ToolStripMenuItem("打开日志");
            openLog.Click += delegate
            {
                try { Process.Start("notepad.exe", logPath); } catch { }
            };
            menu.Items.Add(openLog);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Quit(); };
            menu.Items.Add(quit);

            RefreshMenuState();

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowDetails(); };
        }

        // Toggle items carry their state on the right edge, so the answer to
        // "is this on?" is readable without opening anything else.
        private static void RefreshMenuState()
        {
            if (autoItem != null)
            {
                bool on = IsAutoStart();
                autoItem.Checked = on;
                autoItem.ShortcutKeyDisplayString = on ? "开" : "关";
                autoItem.Text = "开机自启";
                autoItem.ForeColor = SystemColors.WindowText;
            }

            if (pinItem != null)
            {
                bool on = IsPinned();
                pinItem.Checked = on;
                pinItem.ShortcutKeyDisplayString = on ? "开" : "关";
            }
        }

        // An entry that exists but points at a moved executable still reads as
        // "on" while doing nothing at logon, so flag it in the menu itself.
        private static void CheckAutoStartHealth()
        {
            if (autoItem == null || !IsAutoStart()) return;

            string target = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k != null) target = k.GetValue(RunValue) as string;
                }
            }
            catch { return; }

            if (target == null) return;

            bool ok;
            try { ok = File.Exists(target.Trim().Trim('"')); }
            catch { ok = false; }

            if (ok) return;

            autoItem.Checked = false;
            autoItem.ForeColor = Color.FromArgb(200, 40, 40);
            autoItem.ShortcutKeyDisplayString = "路径失效";
            autoItem.Text = "开机自启";
            autoItem.ToolTipText = "注册表指向的 exe 已不存在，重新勾选可修复。\n当前：" + target;
        }

        private static void Refresh()
        {
            if (busy) return;
            busy = true;
            try
            {
                BatteryState st = reader.Poll();
                last = st;

                if (st.Valid)
                {
                    lastGood = DateTime.Now;
                    SetIcon(st.MouseBattery, false);
                    tray.Text = Text(st);

                    if (!loggedFirst)
                    {
                        loggedFirst = true;
                        Log("首次读取成功: 鼠标 " + st.MouseBattery + "%，图标 "
                            + (currentIcon == null ? "?" : currentIcon.Width + "x" + currentIcon.Height));
                    }
                }
                else if (lastGood == DateTime.MinValue || (DateTime.Now - lastGood).TotalSeconds > 12)
                {
                    SetIcon(-1, true);
                    tray.Text = "VK M5 - 未连接";
                }
                else
                {
                    tray.Text = Text(last);
                }
            }
            catch (Exception ex)
            {
                Log("刷新异常: " + ex.Message);
            }
            finally { busy = false; }
        }

        private static string Text(BatteryState st)
        {
            if (!st.Valid) return "VK M5 - 读取中";
            string s = "VK M5  " + st.MouseBattery + "%";
            if (!st.MouseOnline) s += "  (休眠)";
            if (st.KeyboardBattery > 0) s += "  键盘 " + st.KeyboardBattery + "%";
            if (s.Length > 63) s = s.Substring(0, 63);
            return s;
        }

        // Rebuilding a 32bpp DIB every two seconds is pointless churn and makes
        // the shell flicker, so the HICON is rebuilt only when the reading (or
        // the online state) actually changes.
        private static void SetIcon(int level, bool offline)
        {
            level = offline || level < 0 ? -1 : Math.Max(0, Math.Min(100, level));
            if (currentIcon != null && iconLevel == level && iconOffline == offline) return;

            Icon icon = IconFactory.Build(level, offline);
            Icon old = currentIcon;
            currentIcon = icon;
            iconLevel = level;
            iconOffline = offline;
            tray.Icon = icon;
            if (old != null) old.Dispose();
        }

        private static void ShowDetails()
        {
            BatteryState st = last;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("设备：VK M5   VID_374A / PID_A322");
            sb.AppendLine("通道：24G 接收器 Feature Report 0xF7");
            sb.AppendLine();

            if (st.Valid)
            {
                sb.AppendLine("鼠标电量： " + st.MouseBattery + "%");
                sb.AppendLine("鼠标状态： " + (st.MouseOnline ? "在线" : "休眠 / 离线"));
                sb.AppendLine("键盘电量： " + (st.KeyboardBattery > 0 ? st.KeyboardBattery + "%" : "无设备"));
                sb.AppendLine("当前目标： " + st.Target + "   (1=键盘 2=鼠标 3=both)");
                sb.AppendLine("可写：      " + (st.CanSend ? "是" : "否"));
                sb.AppendLine("RF Boot：   " + (st.RfBoot ? "是" : "否"));
            }
            else
            {
                sb.AppendLine("状态：暂未读到有效数据");
                if (!string.IsNullOrEmpty(reader.LastError)) sb.AppendLine(reader.LastError);
            }

            sb.AppendLine();
            sb.AppendLine("原始帧：");
            sb.AppendLine(st.RawHex == "" ? "(空)" : st.RawHex);
            sb.AppendLine();
            sb.AppendLine("最近成功读取：" + (lastGood == DateTime.MinValue ? "无" : lastGood.ToString("HH:mm:ss")));
            sb.AppendLine("刷新间隔： " + intervalMs + " ms");
            sb.AppendLine();
            sb.AppendLine("—— 自启 ——");
            sb.AppendLine(AutoStartReport());

            MessageBox.Show(sb.ToString(), AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // A startup entry that exists but points at a moved/deleted file fails
        // silently at logon, which looks exactly like "autostart does nothing",
        // so every diagnostic reports the stored path and whether it resolves.
        private static string AutoStartReport()
        {
            StringBuilder sb = new StringBuilder();
            string stored = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k != null) stored = k.GetValue(RunValue) as string;
                }
            }
            catch (Exception ex) { return "读取注册表失败：" + ex.Message; }

            if (stored == null)
            {
                sb.AppendLine("注册表项：未设置（当前不会自启）");
                sb.AppendLine("右键菜单勾选「开机自启」即可写入。");
                return sb.ToString();
            }

            sb.AppendLine("注册表项：" + stored);
            string target = stored.Trim().Trim('"');
            bool ok = false;
            try { ok = File.Exists(target); } catch { }
            sb.AppendLine("目标文件：" + (ok ? "存在" : "不存在！路径已失效"));
            if (!ok)
            {
                sb.AppendLine("下次启动本程序会自动改回当前路径：");
                sb.AppendLine(Application.ExecutablePath);
            }

            try
            {
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, false))
                {
                    byte[] b = a == null ? null : a.GetValue(RunValue) as byte[];
                    if (b != null && b.Length > 0)
                    {
                        if (b[0] == 2) sb.AppendLine("Explorer 记录：已被任务管理器禁用！");
                        else if (b[0] == 1) sb.AppendLine("Explorer 记录：被策略禁用");
                        else if (b[0] == 3) sb.AppendLine("Explorer 记录：已启用");
                    }
                }
            }
            catch { }

            sb.AppendLine("当前登录会话是否已运行：" + (Process.GetProcessesByName("VkM5BatteryTray").Length > 0 ? "是" : "否"));
            return sb.ToString();
        }

        private static bool IsAutoStart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (k == null) return false;
                return k.GetValue(RunValue) != null;
            }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValue, false);
                }

                // Explorer records "was this startup entry disabled by the user"
                // here. A leftover record keeps the entry off even after it is
                // written again, so drop it whenever the state is toggled.
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true))
                {
                    if (a != null) a.DeleteValue(RunValue, false);
                }

                // Remember the choice, so first-run registration never fights a
                // user who turned it off on purpose.
                using (RegistryKey p = Registry.CurrentUser.CreateSubKey(PrefKey))
                {
                    if (p != null) p.SetValue("AutoStart", on ? 1 : 0, RegistryValueKind.DWord);
                }

                Log("开机自启 = " + (on ? "开" : "关"));
            }
            catch (Exception ex) { Log("自启设置失败: " + ex.Message); }
        }

        // Keeps the startup entry correct across two kinds of drift: an entry
        // that points at an old path, and a fresh install with no entry at all
        // (registered once, unless the user explicitly turned it off before).
        private static void SyncAutoStartPath()
        {
            try
            {
                object pref = null;
                using (RegistryKey p = Registry.CurrentUser.OpenSubKey(PrefKey, false))
                {
                    if (p != null) pref = p.GetValue("AutoStart");
                }

                object stored = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k != null) stored = k.GetValue(RunValue);
                }

                string want = "\"" + Application.ExecutablePath + "\"";

                if (stored == null)
                {
                    if (pref is int && (int)pref == 0) return;   // user opted out
                    SetAutoStart(true);
                    Log("已注册开机自启: " + Application.ExecutablePath);
                    return;
                }

                if (string.Equals((string)stored, want, StringComparison.OrdinalIgnoreCase)) return;

                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    k.SetValue(RunValue, want);
                }
                Log("开机自启路径已更新: " + Application.ExecutablePath);
            }
            catch (Exception ex) { Log("自启路径同步失败: " + ex.Message); }
        }

        internal static void Log(string s)
        {
            try
            {
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        // Windows 11 keeps a per-icon setting under this key, one subkey per
        // icon, named by a hash of the executable path. IsPromoted=1 means the
        // icon sits in the always-visible tray instead of the "^" overflow.
        private const string IconSettingsKey = @"Control Panel\NotifyIconSettings";
        private const string IconSettingsPath = "HKEY_CURRENT_USER\\Control Panel\\NotifyIconSettings";

        private static string FindIconKey()
        {
            string exe = Application.ExecutablePath;
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(IconSettingsKey, false))
                {
                    if (root == null) return null;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using (RegistryKey sub = root.OpenSubKey(name, false))
                        {
                            if (sub == null) continue;
                            object v = sub.GetValue("ExecutablePath");
                            if (v is string && string.Equals((string)v, exe, StringComparison.OrdinalIgnoreCase))
                                return name;
                        }
                    }
                }
            }
            catch (Exception ex) { Log("读取托盘设置失败: " + ex.Message); }
            return null;
        }

        private static bool IsPinned()
        {
            string key = FindIconKey();
            if (key == null) return false;
            try
            {
                using (RegistryKey sub = Registry.CurrentUser.OpenSubKey(IconSettingsKey + "\\" + key, false))
                {
                    if (sub == null) return false;
                    object v = sub.GetValue("IsPromoted");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        private static void SetPinned(bool on)
        {
            string key = FindIconKey();
            if (key == null) { Log("尚未生成托盘设置项，先让图标出现一次"); return; }
            try
            {
                using (RegistryKey sub = Registry.CurrentUser.OpenSubKey(IconSettingsKey + "\\" + key, true))
                {
                    if (sub == null) return;
                    sub.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord);
                }
                Log("托盘固定 = " + (on ? "开" : "关") + "（可能需要重启 explorer 生效）");
            }
            catch (Exception ex) { Log("设置托盘固定失败: " + ex.Message); }
        }

        private static void Quit()
        {
            Cleanup();
            Application.Exit();
        }

        private static void Cleanup()
        {
            try
            {
                if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                if (reader != null) reader.Dispose();
                if (currentIcon != null) { currentIcon.Dispose(); currentIcon = null; }
            }
            catch { }
        }
    }
}
