using System;

namespace ARPG.Core
{
    /// <summary>XZ 평면 좌표(x=X, y=Z). Unity Vector 에 의존하지 않는 코어 전용 값 타입.</summary>
    [Serializable]
    public struct Vec2
    {
        public double x;
        public double y;

        public Vec2(double x, double y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);
        public static readonly Vec2 Forward = new Vec2(0, 1);

        public double Length { get { return Math.Sqrt(x * x + y * y); } }
        public double SqrLength { get { return x * x + y * y; } }

        public Vec2 Normalized
        {
            get
            {
                double l = Length;
                return l > 1e-9 ? new Vec2(x / l, y / l) : Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.x + b.x, a.y + b.y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.x - b.x, a.y - b.y); }
        public static Vec2 operator -(Vec2 a) { return new Vec2(-a.x, -a.y); }
        public static Vec2 operator *(Vec2 a, double s) { return new Vec2(a.x * s, a.y * s); }
        public static Vec2 operator *(double s, Vec2 a) { return new Vec2(a.x * s, a.y * s); }

        public static double Dot(Vec2 a, Vec2 b) { return a.x * b.x + a.y * b.y; }
        public static double Cross(Vec2 a, Vec2 b) { return a.x * b.y - a.y * b.x; }
        public static double Distance(Vec2 a, Vec2 b) { return (a - b).Length; }

        /// <summary>왼쪽 수직 벡터(위에서 내려다볼 때 반시계 90도).</summary>
        public Vec2 PerpLeft { get { return new Vec2(-y, x); } }

        /// <summary>두 단위 벡터 사이의 부호 있는 각도(도). 양수=반시계.</summary>
        public static double SignedAngleDeg(Vec2 from, Vec2 to)
        {
            double a = Math.Atan2(Cross(from, to), Dot(from, to));
            return a * 180.0 / Math.PI;
        }

        public Vec2 Rotate(double deg)
        {
            double r = deg * Math.PI / 180.0;
            double c = Math.Cos(r), s = Math.Sin(r);
            return new Vec2(x * c - y * s, x * s + y * c);
        }

        /// <summary>현재 방향에서 목표 방향으로 최대 maxDeg 만큼 회전.</summary>
        public static Vec2 RotateTowards(Vec2 from, Vec2 to, double maxDeg)
        {
            if (to.SqrLength < 1e-12) return from;
            if (from.SqrLength < 1e-12) return to.Normalized;
            double ang = SignedAngleDeg(from, to);
            if (Math.Abs(ang) <= maxDeg) return to.Normalized;
            return from.Rotate(Math.Sign(ang) * maxDeg).Normalized;
        }

        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) { return new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }

        public override string ToString() { return "(" + x.ToString("0.00") + ", " + y.ToString("0.00") + ")"; }
    }
}
