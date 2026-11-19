using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconCheck
{
    private static Bitmap Flat(Icon ic, int S, Color bg)
    {
        Bitmap b = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(bg);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawIcon(ic, new Rectangle(0, 0, S, S));
        }
        return b;
    }

    public static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "icheck";
        Directory.CreateDirectory(dir);
        VkM5Battery.Native.EnableDpiAwareness();
        int want = VkM5Battery.Native.GetSystemMetrics(VkM5Battery.Native.SM_CXSMICON);
        Console.WriteLine("SM_CXSMICON=" + want);

        int[] lvs = new int[] { -1, 5, 20, 45, 80, 100 };
        Color dark = Color.FromArgb(32, 32, 32);
        Color light = Color.FromArgb(243, 243, 243);

        using (Bitmap sheet = new Bitmap(lvs.Length * (want + 12) + 8, want * 2 + 26))
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(96, 96, 96));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int i = 0; i < lvs.Length; i++)
            {
                using (Icon ic = VkM5Battery.IconFactory.Build(lvs[i], lvs[i] < 0))
                {
                    Console.WriteLine("lv=" + lvs[i] + " icon=" + ic.Width + "x" + ic.Height);
                    using (Bitmap b = Flat(ic, want, dark)) g.DrawImage(b, i * (want + 12) + 6, 6, want, want);
                    using (Bitmap b2 = Flat(ic, want, light)) g.DrawImage(b2, i * (want + 12) + 6, want + 14, want, want);
                }
            }
            sheet.Save(Path.Combine(dir, "tray20.png"));
            using (Bitmap big = new Bitmap(sheet.Width * 6, sheet.Height * 6))
            {
                using (Graphics gg = Graphics.FromImage(big))
                {
                    gg.InterpolationMode = InterpolationMode.NearestNeighbor;
                    gg.PixelOffsetMode = PixelOffsetMode.Half;
                    gg.DrawImage(sheet, 0, 0, sheet.Width * 6, sheet.Height * 6);
                }
                big.Save(Path.Combine(dir, "tray20_x6.png"));
            }
        }
        Console.WriteLine("ok");
    }
}
