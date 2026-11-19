using System;
using System.Runtime.InteropServices;
using System.Text;

class D
{
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr d, ref ATT a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr d, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, byte[] c);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr h, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr di, ref Guid g, int i, ref DID d);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref DID d, IntPtr b, int sz, out int req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFileW(string p, uint a, uint sh, IntPtr sec, uint disp, uint fl, IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] struct ATT { public int Size; public ushort V; public ushort P; public ushort Ver; }
    [StructLayout(LayoutKind.Sequential)] struct DID { public int cbSize; public Guid g; public int flags; public IntPtr res; }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Guid hid; HidD_GetHidGuid(out hid);
        IntPtr set = SetupDiGetClassDevsW(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12);
        for (int i = 0; ; i++)
        {
            DID d = new DID(); d.cbSize = Marshal.SizeOf(typeof(DID));
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d)) break;
            int req = 0;
            SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            if (req <= 0) continue;
            IntPtr b = Marshal.AllocHGlobal(req);
            string path = "";
            try
            {
                Marshal.WriteInt32(b, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetailW(set, ref d, b, req, out req, IntPtr.Zero)) continue;
                path = Marshal.PtrToStringUni(new IntPtr(b.ToInt64() + 4));
            }
            finally { Marshal.FreeHGlobal(b); }

            IntPtr h = CreateFileW(path, 0xC0000000u, 3u, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) { Console.WriteLine("OPENFAIL " + path); continue; }
            try
            {
                ATT a = new ATT(); a.Size = Marshal.SizeOf(typeof(ATT));
                HidD_GetAttributes(h, ref a);
                if (a.V != 0x374A) { CloseHandle(h); continue; }

                IntPtr pre;
                if (!HidD_GetPreparsedData(h, out pre) || pre == IntPtr.Zero) continue;
                try
                {
                    byte[] c = new byte[256];
                    HidP_GetCaps(pre, c);
                    StringBuilder sb = new StringBuilder();
                    for (int k = 0; k < 24; k++) sb.Append("[" + k + "]=" + BitConverter.ToUInt16(c, k*2) + " ");
                    Console.WriteLine(path.Substring(path.IndexOf("mi_")));
                    Console.WriteLine("   " + sb);
                }
                finally { HidD_FreePreparsedData(pre); }
            }
            finally { CloseHandle(h); }
        }
        SetupDiDestroyDeviceInfoList(set);
    }
}
