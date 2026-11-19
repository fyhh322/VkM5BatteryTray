using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class NProbe {
  [StructLayout(LayoutKind.Sequential)]
  struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }

  [DllImport("shell32.dll")]
  static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);

  public static void Main(){
    using (NotifyIcon ni = new NotifyIcon()){
      ni.Icon = VkM5Battery.IconFactory.Build(97, false);
      ni.Text = "VK M5 97%";
      ni.Visible = true;
      Application.DoEvents();
      System.Threading.Thread.Sleep(400);
      Application.DoEvents();

      // NotifyIcon keeps its window in a private field named "window".
      FieldInfo f = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
      object w = f == null ? null : f.GetValue(ni);
      Console.WriteLine("field window = " + (w == null ? "NULL" : w.GetType().FullName));
      if (w == null) return;
      PropertyInfo hp = w.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      IntPtr hwnd = (IntPtr)hp.GetValue(w, null);
      Console.WriteLine("hwnd = 0x" + hwnd.ToInt64().ToString("X"));

      // uID is assigned from a static counter; try 1..5
      for (uint id = 1; id <= 3; id++){
        NOTIFYICONIDENTIFIER nid = new NOTIFYICONIDENTIFIER();
        nid.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
        nid.hWnd = hwnd;
        nid.uID = id;
        nid.guidItem = Guid.Empty;
        RECT r;
        int hr = Shell_NotifyIconGetRect(ref nid, out r);
        Console.WriteLine("uID=" + id + " hr=0x" + hr.ToString("X8") +
          (hr == 0 ? "  rect " + r.L + "," + r.T + " " + (r.R-r.L) + "x" + (r.B-r.T) : ""));
      }
      Console.WriteLine("icon = " + ni.Icon.Width + "x" + ni.Icon.Height);
      ni.Visible = false;
    }
  }
}
