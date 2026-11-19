// HidProbe.cs — VK M5 HID 探针
// 用途：枚举 VID_374A&PID_A322 的全部 HID 接口，导出报告描述符、能力、并被动监听/主动试探电量协议。
// 只读，不写设备配置。
//
// 编译：
//   csc /target:exe /platform:x64 /out:HidProbe.exe HidProbe.cs
// 用法：
//   HidProbe.exe dump                    列出所有 HID 接口 + 描述符
//   HidProbe.exe listen 15               被动监听 15 秒，打印所有输入报告
//   HidProbe.exe getfeat <rid> <len>     对每个接口读 Feature 报告
//   HidProbe.exe setfeat <hex>          对每个接口写 Feature 报告（试探查询用）

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Native
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const int FILE_SHARE_READ = 1;
    public const int FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public const uint IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0190;

    [StructLayout(LayoutKind.Sequential)]
    public struct GUID { public uint Data1; public ushort Data2; public ushort Data3; public byte b0, b1, b2, b3, b4, b5, b6, b7; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public GUID InterfaceClassGuid;
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

    // HIDP_CAPS 真实布局
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(ref GUID ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref GUID interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out GUID guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetProductString(IntPtr device, IntPtr buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetManufacturerString(IntPtr device, IntPtr buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetSerialNumberString(IntPtr device, IntPtr buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilities);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, int shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr hFile, byte[] buffer, int numberOfBytesToRead, out int numberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr hFile, byte[] buffer, int numberOfBytesToWrite, out int numberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);
}

class Dev
{
    public string Path;
    public ushort Vid, Pid, Ver;
    public string Product, Manufacturer, Serial;
    public int InLen, OutLen, FeatLen;
    public ushort Usage, UsagePage;
    public byte[] Descriptor = new byte[0];
}

static class Probe
{
    static List<Dev> Enumerate()
    {
        var list = new List<Dev>();
        Native.GUID guid;
        Native.HidD_GetHidGuid(out guid);
        IntPtr set = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == Native.INVALID_HANDLE_VALUE) return list;
        try
        {
            for (int idx = 0; ; idx++)
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, idx, ref did)) break;

                int need;
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(need);
                Dev d = new Dev();
                try
                {
                    Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, need, out need, IntPtr.Zero)) continue;
                    d.Path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (string.IsNullOrEmpty(d.Path)) continue;

                IntPtr h = Native.CreateFileW(d.Path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE_VALUE) continue;
                try
                {
                    var a = new Native.HIDD_ATTRIBUTES();
                    a.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                    if (Native.HidD_GetAttributes(h, ref a)) { d.Vid = a.VendorID; d.Pid = a.ProductID; d.Ver = a.VersionNumber; }

                    d.Product = GetStr(h, true);
                    d.Manufacturer = GetStr(h, false);
                    d.Serial = GetSerial(h);

                    IntPtr pp;
                    if (Native.HidD_GetPreparsedData(h, out pp) && pp != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr capsBuf = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.HIDP_CAPS)));
                            try
                            {
                                for (int i = 0; i < Marshal.SizeOf(typeof(Native.HIDP_CAPS)); i++) Marshal.WriteByte(capsBuf, i, 0);
                                if (Native.HidP_GetCaps(pp, capsBuf) == 0x00110000)
                                {
                                    var c = (Native.HIDP_CAPS)Marshal.PtrToStructure(capsBuf, typeof(Native.HIDP_CAPS));
                                    d.Usage = c.Usage; d.UsagePage = c.UsagePage;
                                    d.InLen = c.InputReportByteLength;
                                    d.OutLen = c.OutputReportByteLength;
                                    d.FeatLen = c.FeatureReportByteLength;
                                }
                            }
                            finally { Marshal.FreeHGlobal(capsBuf); }
                        }
                        finally { Native.HidD_FreePreparsedData(pp); }
                    }

                    // 报告描述符
                    IntPtr hRW = Native.CreateFileW(d.Path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                        IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hRW != Native.INVALID_HANDLE_VALUE)
                    {
                        try
                        {
                            byte[] rd = new byte[4096];
                            int got;
                            if (Native.DeviceIoControl(hRW, Native.IOCTL_HID_GET_REPORT_DESCRIPTOR, IntPtr.Zero, 0, rd, rd.Length, out got, IntPtr.Zero) && got > 0)
                            {
                                byte[] trimmed = new byte[got];
                                Array.Copy(rd, trimmed, got);
                                d.Descriptor = trimmed;
                            }
                        }
                        finally { Native.CloseHandle(hRW); }
                    }
                }
                finally { Native.CloseHandle(h); }

                if (d.Vid != 0 || d.Pid != 0) list.Add(d);
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    static string GetStr(IntPtr h, bool product)
    {
        IntPtr sb = Marshal.AllocHGlobal(1024);
        try
        {
            bool ok = product ? Native.HidD_GetProductString(h, sb, 1024) : Native.HidD_GetManufacturerString(h, sb, 1024);
            return ok ? Marshal.PtrToStringUni(sb) : null;
        }
        finally { Marshal.FreeHGlobal(sb); }
    }

    static string GetSerial(IntPtr h)
    {
        IntPtr sb = Marshal.AllocHGlobal(1024);
        try { return Native.HidD_GetSerialNumberString(h, sb, 1024) ? Marshal.PtrToStringUni(sb) : null; }
        finally { Marshal.FreeHGlobal(sb); }
    }

    static List<Dev> Filter(List<Dev> all, ushort vid, ushort pid)
    {
        var r = new List<Dev>();
        foreach (var d in all) if (d.Vid == vid && d.Pid == pid) r.Add(d);
        return r;
    }

    static string Hex(byte[] b, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n && i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    // 解析报告描述符，抽出 Report ID 与各 report 的 bit 长度（粗解析，够用）
    static void ParseDescriptor(byte[] d)
    {
        var ids = new SortedSet<int>();
        int i = 0;
        int reportId = 0;
        var sizes = new Dictionary<int, int>(); // reportId*16 + kind
        while (i < d.Length)
        {
            byte b = d[i];
            if (b == 0xFE) { i += 3 + (d.Length > i + 2 ? d[i + 2] : 0); continue; } // long item
            int size = b & 0x03; if (size == 3) size = 4;
            byte type = (byte)((b >> 2) & 0x03);
            byte tag = (byte)((b >> 4) & 0x0F);
            if (type == 1 && tag == 8 && size >= 1) // Report ID
            {
                int v = d[i + 1];
                if (size == 2) v |= d[i + 2] << 8;
                reportId = v; ids.Add(v);
            }
            if (type == 0 && tag == 7) // Input
            {
                int bits = 0;
                for (int k = 0; k < size; k++) bits |= d[i + 1 + k] << (8 * k);
                int key = reportId * 16 + 0;
                sizes[key] = (sizes.ContainsKey(key) ? sizes[key] : 0) + bits;
            }
            if (type == 0 && tag == 9) // Output
            {
                int bits = 0;
                for (int k = 0; k < size; k++) bits |= d[i + 1 + k] << (8 * k);
                int key = reportId * 16 + 1;
                sizes[key] = (sizes.ContainsKey(key) ? sizes[key] : 0) + bits;
            }
            if (type == 0 && tag == 11) // Feature
            {
                int bits = 0;
                for (int k = 0; k < size; k++) bits |= d[i + 1 + k] << (8 * k);
                int key = reportId * 16 + 2;
                sizes[key] = (sizes.ContainsKey(key) ? sizes[key] : 0) + bits;
            }
            i += 1 + size;
        }
        Console.WriteLine("    ReportIDs: " + string.Join(", ", ids));
        foreach (var kv in sizes)
        {
            string kind = kv.Key % 16 == 0 ? "IN " : (kv.Key % 16 == 1 ? "OUT" : "FEAT");
            Console.WriteLine(string.Format("      {0} rid=0x{1:X2} bits={2} bytes~{0,0}", kind, kv.Key / 16, kv.Value) + (8 * ((kv.Value + 7) / 8)).ToString());
        }
    }

    static void Dump(List<Dev> list)
    {
        foreach (var d in list)
        {
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine(string.Format("VID_{0:X4} PID_{1:X4} ver={2:X4}", d.Vid, d.Pid, d.Ver));
            Console.WriteLine("  Product     : " + d.Product);
            Console.WriteLine("  Manufacturer: " + d.Manufacturer);
            Console.WriteLine("  Serial      : " + d.Serial);
            Console.WriteLine("  UsagePage   : 0x" + d.UsagePage.ToString("X4") + "  Usage: 0x" + d.Usage.ToString("X4"));
            Console.WriteLine(string.Format("  InLen={0} OutLen={1} FeatLen={2}", d.InLen, d.OutLen, d.FeatLen));
            Console.WriteLine("  Path        : " + d.Path);
            Console.WriteLine("  Descriptor(" + d.Descriptor.Length + "): " + Hex(d.Descriptor, d.Descriptor.Length));
            ParseDescriptor(d.Descriptor);
        }
    }

    static void Listen(List<Dev> list, int seconds)
    {
        var threads = new List<Thread>();
        var stop = new ManualResetEventSlim(false);
        foreach (var d in list)
        {
            if (d.InLen <= 0) continue;
            var dev = d;
            var t = new Thread(delegate ()
            {
                IntPtr h = Native.CreateFileW(dev.Path, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                    IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE_VALUE)
                {
                    Console.WriteLine("[open-fail] " + dev.Path + " err=" + Marshal.GetLastWin32Error());
                    return;
                }
                Console.WriteLine("[listen] pid=" + dev.Pid.ToString("X4") + " up=0x" + dev.UsagePage.ToString("X4") + " len=" + dev.InLen);
                var buf = new byte[Math.Max(dev.InLen, 64) + 8];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!stop.IsSet && sw.Elapsed.TotalSeconds < seconds)
                {
                    int got;
                    if (Native.ReadFile(h, buf, buf.Length, out got, IntPtr.Zero))
                    {
                        Console.WriteLine(string.Format("  [{0,7:F2}s] pid={1:X4} up={2:X4} ", sw.Elapsed.TotalSeconds, dev.Pid, dev.UsagePage) + Hex(buf, got));
                    }
                    else
                    {
                        int e = Marshal.GetLastWin32Error();
                        if (e != 0) Console.WriteLine("  read err=" + e);
                        Thread.Sleep(20);
                    }
                }
                Native.CloseHandle(h);
            });
            t.IsBackground = true;
            t.Start();
            threads.Add(t);
        }
        Thread.Sleep(seconds * 1000);
        stop.Set();
        foreach (var t in threads) t.Join(1500);
    }

    static void GetFeature(List<Dev> list, int rid, int len)
    {
        foreach (var d in list)
        {
            if (d.FeatLen <= 0) continue;
            IntPtr h = Native.CreateFileW(d.Path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) { Console.WriteLine("[open-fail] " + d.Path); continue; }
            try
            {
                var buf = new byte[Math.Max(len, d.FeatLen)];
                if (rid >= 0) buf[0] = (byte)rid;
                bool ok = Native.HidD_GetFeature(h, buf, buf.Length);
                Console.WriteLine(string.Format("up=0x{0:X4} featLen={1} rid=0x{2:X2} -> {3} {4}", d.UsagePage, d.FeatLen, rid,
                    ok ? "OK" : ("fail " + Marshal.GetLastWin32Error()), ok ? Hex(buf, buf.Length) : ""));
            }
            finally { Native.CloseHandle(h); }
        }
    }

    static void SetFeature(List<Dev> list, byte[] payload)
    {
        foreach (var d in list)
        {
            if (d.FeatLen <= 0) continue;
            IntPtr h = Native.CreateFileW(d.Path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE_VALUE) { Console.WriteLine("[open-fail] " + d.Path); continue; }
            try
            {
                var buf = new byte[Math.Max(payload.Length, d.FeatLen)];
                Array.Copy(payload, buf, payload.Length);
                bool ok = Native.HidD_SetFeature(h, buf, buf.Length);
                Console.WriteLine(string.Format("up=0x{0:X4} featLen={1} SET {2} -> {3}", d.UsagePage, d.FeatLen,
                    Hex(payload, payload.Length), ok ? "OK" : ("fail " + Marshal.GetLastWin32Error())));
            }
            finally { Native.CloseHandle(h); }
        }
    }

    static byte[] ParseHex(string s)
    {
        s = s.Replace(" ", "").Replace("0x", "");
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
        ushort vid = 0x374A, pid = 0xA322;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--vid") vid = Convert.ToUInt16(args[i + 1], 16);
            if (args[i] == "--pid") pid = Convert.ToUInt16(args[i + 1], 16);
        }

        var all = Enumerate();
        var mine = Filter(all, vid, pid);
        Console.WriteLine("HID total=" + all.Count + "  matched VID_" + vid.ToString("X4") + " PID_" + pid.ToString("X4") + " = " + mine.Count);
        if (mine.Count == 0) { Console.WriteLine("no match; see dump of all:"); Dump(all); return 1; }

        switch (cmd)
        {
            case "dump":
                Dump(mine);
                break;
            case "listen":
                int sec = args.Length > 1 ? int.Parse(args[1]) : 15;
                Listen(mine, sec);
                break;
            case "getfeat":
                int rid = args.Length > 1 ? Convert.ToInt32(args[1], args[1].StartsWith("0x") ? 16 : 10) : -1;
                int len = args.Length > 2 ? Convert.ToInt32(args[2], args[2].StartsWith("0x") ? 16 : 10) : 64;
                GetFeature(mine, rid, len);
                break;
            case "setfeat":
                SetFeature(mine, ParseHex(args[1]));
                break;
            case "all":
                Dump(all);
                break;
            default:
                Console.WriteLine("unknown cmd");
                break;
        }
        return 0;
    }
}
