using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

class P {
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern SafeFileHandle CreateFileW(string p, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

  static SafeFileHandle Open(string p){
    SafeFileHandle h = CreateFileW(p, 0x80000000u|0x40000000u, 3u, IntPtr.Zero, 3u, 0, IntPtr.Zero);
    if (h.IsInvalid) throw new Exception("open fail " + Marshal.GetLastWin32Error() + " " + p);
    return h;
  }
  static string Hex(byte[] b){
    System.Text.StringBuilder sb=new System.Text.StringBuilder();
    foreach(byte x in b) sb.Append(x.ToString("X2")).Append(' ');
    return sb.ToString().Trim();
  }

  static void Main(string[] args){
    string path = args[0];
    int len = int.Parse(args[1]);
    SafeFileHandle h = Open(path);
    try {
      for(int mode=0; mode<3; mode++){
        byte[] w = new byte[len];
        if(mode==0){ w[0]=0x00; w[1]=0xF7; }
        else if(mode==1){ w[0]=0xF7; }
        else { w[0]=0x00; w[1]=0xF7; w[2]=0x00; }
        bool ok = HidD_SetFeature(h, w, len);
        Console.WriteLine("set mode=" + mode + " ok=" + ok + " err=" + Marshal.GetLastWin32Error() + " -> " + Hex(w));
        System.Threading.Thread.Sleep(30);
        for(int rid=0; rid<2; rid++){
          byte[] r = new byte[len];
          if(mode==1 || rid==1) r[0]=0xF7; else r[0]=0x00;
          bool g = HidD_GetFeature(h, r, len);
          Console.WriteLine("  get rid=" + r[0].ToString("X2") + " ok=" + g + " err=" + Marshal.GetLastWin32Error());
          Console.WriteLine("    " + Hex(r));
        }
      }
    } finally { h.Dispose(); }
  }
}
