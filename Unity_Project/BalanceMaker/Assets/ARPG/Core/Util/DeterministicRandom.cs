using System;

namespace ARPG.Core
{
    public interface IRandomSource
    {
        /// <summary>[0, 1) 균등 난수</summary>
        double NextUnit();
    }

    /// <summary>
    /// xoshiro256** 기반 결정적 난수. 시드는 SplitMix64 로 확장한다.
    /// 프레임 시간·스레드 순서에서 시드를 만들지 않는다(구현계획 8.1).
    /// </summary>
    public sealed class DeterministicRandom : IRandomSource
    {
        private ulong _s0, _s1, _s2, _s3;
        public long DrawCount { get; private set; }

        public DeterministicRandom(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);
            _s2 = SplitMix64(ref x);
            _s3 = SplitMix64(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 0x9E3779B97F4A7C15UL;
        }

        public static ulong SplitMix64(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>회차별 시드: masterSeed 와 battleIndex 만으로 결정.</summary>
        public static ulong SeedForBattle(ulong masterSeed, int battleIndex)
        {
            ulong x = masterSeed ^ (0xD1B54A32D192ED03UL * (ulong)(battleIndex + 1));
            return SplitMix64(ref x);
        }

        private static ulong Rotl(ulong x, int k) { return (x << k) | (x >> (64 - k)); }

        public ulong NextULong()
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            DrawCount++;
            return result;
        }

        public double NextUnit()
        {
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>[min, max] 구간. Excel RANDBETWEEN 의 연속 버전.</summary>
        public double Range(double min, double max)
        {
            return min + (max - min) * NextUnit();
        }
    }

    public static class StableHash
    {
        /// <summary>FNV-1a 64bit. 플랫폼/런타임과 무관한 문자열 해시(결정성 비교용).</summary>
        public static ulong Fnv1a64(string s, ulong hash = 14695981039346656037UL)
        {
            if (s == null) return hash;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Mix(ulong hash, long v)
        {
            ulong u = unchecked((ulong)v);
            for (int i = 0; i < 8; i++)
            {
                hash ^= (u >> (i * 8)) & 0xFF;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Mix(ulong hash, double v)
        {
            return Mix(hash, BitConverter.DoubleToInt64Bits(v));
        }

        public static string Hex(ulong h) { return h.ToString("x16"); }
    }
}
