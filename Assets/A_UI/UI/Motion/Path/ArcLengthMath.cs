using System;

namespace UIPackage.Motion
{
    // 椭圆弧长用第二类椭圆积分；Carlson 对称积分的倍增算法不是折线/LUT 近似。
    internal static class ArcLengthMath
    {
        public static double Length(double a, double b, double start, double end)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (a == 0 && b == 0) return 0;
            if (a == b) return a * Math.Abs(end - start);
            if (a == 0) return b * Math.Abs(AbsCos(end) - AbsCos(start));
            if (b == 0) return a * Math.Abs(AbsCos(end - Math.PI / 2) - AbsCos(start - Math.PI / 2));
            // 把长轴归一到 cos 速度项，保证 m 在 [0,1) 内。
            if (a > b) { double tmp = a; a = b; b = tmp; start -= Math.PI / 2; end -= Math.PI / 2; }
            double m = 1 - (a / b) * (a / b);
            if (m >= 1) return b * Math.Abs(AbsCos(end) - AbsCos(start));
            return b * Math.Abs(E(end, m) - E(start, m));
        }
        private static double AbsCos(double angle)
        {
            double periods = Math.Floor((angle + Math.PI / 2) / Math.PI);
            return 2 * periods + Math.Sin(angle - periods * Math.PI);
        }
        private static double E(double angle, double m)
        {
            double periods = Math.Floor((angle + Math.PI / 2) / Math.PI);
            double phi = angle - periods * Math.PI;
            double s = Math.Sin(phi), c = Math.Cos(phi), y = 1 - m * s * s;
            double complete = RF(0, 1 - m, 1) - m * RD(0, 1 - m, 1) / 3;
            return 2 * periods * complete + s * RF(c * c, y, 1) - m * s * s * s * RD(c * c, y, 1) / 3;
        }
        private static double RF(double x, double y, double z)
        {
            double dx, dy, dz, average;
            do
            {
                double sx = Math.Sqrt(x), sy = Math.Sqrt(y), sz = Math.Sqrt(z);
                double lambda = sx * (sy + sz) + sy * sz;
                x = (x + lambda) / 4; y = (y + lambda) / 4; z = (z + lambda) / 4;
                average = (x + y + z) / 3;
                dx = (average - x) / average; dy = (average - y) / average; dz = (average - z) / average;
            } while (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) > .0025);
            double e2 = dx * dy - dz * dz, e3 = dx * dy * dz;
            return (1 + (e2 / 24 - .1 - 3 * e3 / 44) * e2 + e3 / 14) / Math.Sqrt(average);
        }
        private static double RD(double x, double y, double z)
        {
            double sum = 0, factor = 1, dx, dy, dz, average;
            do
            {
                double sx = Math.Sqrt(x), sy = Math.Sqrt(y), sz = Math.Sqrt(z);
                double lambda = sx * (sy + sz) + sy * sz;
                sum += factor / (sz * (z + lambda)); factor /= 4;
                x = (x + lambda) / 4; y = (y + lambda) / 4; z = (z + lambda) / 4;
                average = (x + y + 3 * z) / 5;
                dx = (average - x) / average; dy = (average - y) / average; dz = (average - z) / average;
            } while (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) > .0015);
            double ea = dx * dy, eb = dz * dz, ec = ea - eb, ed = ea - 6 * eb, ee = ed + 2 * ec;
            return 3 * sum + factor * (1 + ed * (-3.0 / 14 + 9.0 / 88 * ed - 9.0 / 52 * dz * ee)
                + dz * (ee / 6 + dz * (-9.0 / 22 * ec + dz * 3.0 / 26 * ea))) / (average * Math.Sqrt(average));
        }
    }
}
