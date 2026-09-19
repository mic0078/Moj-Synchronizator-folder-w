// Logo "Echo Sync": okrąg ze strzałek synchronizacji + fale echa, na gradiencie fiolet → turkus
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MojSync
{
    public static class Logo
    {
        // size – rozmiar w px, rotation – obrót strzałek (animacja), busy – kolorystyka "w trakcie pracy"
        public static Bitmap Draw(int size, float rotation, bool busy)
        {
            return busy ? Draw(size, rotation, BusyTop, BusyBottom, BusyArrows)
                        : Draw(size, rotation, Color.FromArgb(124, 92, 255), Color.FromArgb(0, 198, 255), Color.White);
        }

        // kolory ikony "w trakcie synchronizacji"
        public static Color BusyTop = Color.FromArgb(255, 200, 0);      // żółty
        public static Color BusyBottom = Color.FromArgb(245, 158, 11);  // bursztyn
        public static Color BusyArrows = Color.FromArgb(60, 40, 0);     // ciemnobrązowe strzałki

        public static Bitmap Draw(int size, float rotation, Color c1, Color c2, Color fg)
        {
            // małe rozmiary rysujemy 4x większe i skalujemy – gładsze krawędzie
            int ss = size < 64 ? 4 : 1;
            int S = size * ss;
            var big = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(big))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float k = S / 256f;
                bool small = size <= 24;

                // tło: zaokrągłony kwadrat z gradientem
                float m = 6 * k, w = S - 2 * m, r = 58 * k;
                using (var path = RoundRect(m, m, w, w, r))
                {
                    using (var br = new LinearGradientBrush(new RectangleF(0, 0, S, S), c1, c2, 45f))
                        g.FillPath(br, path);

                    if (!small)
                    {
                        // delikatny połysk u góry
                        g.SetClip(path);
                        using (var gloss = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
                            g.FillEllipse(gloss, -40 * k, -150 * k, 336 * k, 260 * k);
                        g.ResetClip();
                    }
                }

                float cx = S / 2f, cy = S / 2f;

                // fale echa po bokach
                if (!small)
                {
                    DrawWaves(g, cx, cy, k, 0f, fg);
                    DrawWaves(g, cx, cy, k, 180f, fg);
                }

                // okrąg z dwóch strzałek
                float ringR = (small ? 74 : 58) * k;
                float penW = Math.Max(small ? 26 * k : 19 * k, 2f * ss);
                using (var pen = new Pen(fg, penW))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Flat;
                    for (int i = 0; i < 2; i++)
                    {
                        float start = rotation + 200 + i * 180;
                        const float sweep = 118f;
                        g.DrawArc(pen, cx - ringR, cy - ringR, 2 * ringR, 2 * ringR, start, sweep);
                        ArrowHead(g, cx, cy, ringR, start + sweep, penW * 1.25f, penW * 1.55f, fg);
                    }
                }

                // punkt w środku – źródło echa
                if (!small)
                {
                    float d = 15 * k;
                    using (var b = new SolidBrush(fg)) g.FillEllipse(b, cx - d, cy - d, 2 * d, 2 * d);
                }
            }

            if (ss == 1) return big;
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(big, 0, 0, size, size);
            }
            big.Dispose();
            return bmp;
        }

        static void DrawWaves(Graphics g, float cx, float cy, float k, float dir, Color fg)
        {
            float[] radii = { 92 * k, 114 * k };
            int[] alpha = { 170, 90 };
            for (int i = 0; i < 2; i++)
            {
                using (var pen = new Pen(Color.FromArgb(alpha[i], fg), 9 * k))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    float rr = radii[i];
                    g.DrawArc(pen, cx - rr, cy - rr, 2 * rr, 2 * rr, dir - 26, 52);
                }
            }
        }

        // grot strzałki na końcu łuku (kierunek zgodny z ruchem wskazówek zegara)
        static void ArrowHead(Graphics g, float cx, float cy, float r, float angleDeg, float halfW, float len, Color fg)
        {
            double a = angleDeg * Math.PI / 180.0;
            float px = cx + r * (float)Math.Cos(a), py = cy + r * (float)Math.Sin(a);
            float tx = -(float)Math.Sin(a), ty = (float)Math.Cos(a);   // styczna
            float nx = (float)Math.Cos(a), ny = (float)Math.Sin(a);   // promień
            var tip = new PointF(px + tx * len, py + ty * len);
            var b1 = new PointF(px + nx * halfW, py + ny * halfW);
            var b2 = new PointF(px - nx * halfW, py - ny * halfW);
            using (var b = new SolidBrush(fg)) g.FillPolygon(b, new[] { tip, b1, b2 });
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
            p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
            p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
