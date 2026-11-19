using System;
using System.Text;
using System.Threading;

namespace VkM5Battery
{
    internal static class SelfTest
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int n = args.Length > 0 ? int.Parse(args[0]) : 5;
            using (Reader r = new Reader())
            {
                for (int i = 0; i < n; i++)
                {
                    BatteryState st = r.Poll();
                    Console.WriteLine(
                        DateTime.Now.ToString("HH:mm:ss")
                        + " valid=" + st.Valid
                        + " mouse=" + st.MouseBattery + "%"
                        + " mouseOnline=" + st.MouseOnline
                        + " kb=" + st.KeyboardBattery + "%"
                        + " target=" + st.Target
                        + " canSend=" + st.CanSend
                        + " rfBoot=" + st.RfBoot);
                    Console.WriteLine("   raw " + st.RawHex);
                    if (i + 1 < n) Thread.Sleep(1000);
                }
            }
            Console.WriteLine("done");
        }
    }
}
