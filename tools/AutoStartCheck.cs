using System;
using Microsoft.Win32;
using System.Text;

// Verifies the autostart entry the tray app maintains, without starting the GUI.
internal static class AutoStartCheck
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string RunValue = "VkM5BatteryTray";

    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
        {
            if (k == null) { Console.WriteLine("Run key missing"); return; }
            object v = k.GetValue(RunValue);
            Console.WriteLine(v == null ? "Run value: (absent)" : "Run value: " + v);
        }

        using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
        {
            if (a == null) Console.WriteLine("StartupApproved: (key absent)");
            else
            {
                byte[] b = a.GetValue(RunValue) as byte[];
                if (b == null) Console.WriteLine("StartupApproved: (no entry -> enabled)");
                else
                {
                    Console.WriteLine("StartupApproved: " + BitConverter.ToString(b)
                        + (b.Length > 0 && b[0] == 2 ? "  (disabled by user)"
                         : b.Length > 0 && b[0] == 3 ? "  (enabled by user)"
                         : b.Length > 0 && b[0] == 1 ? "  (disabled by policy)"
                         : "  (unknown)"));
                }
            }
        }
    }
}
