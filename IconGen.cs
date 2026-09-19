// Generator app.ico z Logo.cs (uruchamiany przez build.bat)
using System;
using System.Drawing;
using System.IO;

namespace MojSync
{
    static class IconGen
    {
        static int Main(string[] args)
        {
            string outFile = args.Length > 0 ? args[0] : "app.ico";
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bmp = Logo.Draw(sizes[i], 0, false))
                    images[i] = ToDib(bmp);

            using (var fs = File.Create(outFile))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    bw.Write(d); bw.Write(d); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(images[i].Length); bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) bw.Write(img);
            }

            // podgląd logo w dużym rozmiarze
            if (args.Length > 1)
                using (var prev = Logo.Draw(512, 0, false)) prev.Save(args[1]);
            return 0;
        }

        static byte[] ToDib(Bitmap bmp)
        {
            int sz = bmp.Width;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(sz); bw.Write(sz * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = sz - 1; y >= 0; y--)
                    for (int x = 0; x < sz; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                    }
                int maskRow = ((sz + 31) / 32) * 4;
                bw.Write(new byte[maskRow * sz]);
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
