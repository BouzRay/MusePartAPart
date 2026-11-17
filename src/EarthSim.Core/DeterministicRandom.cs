namespace EarthSim.Core;

/// <summary>
/// 決定論的な乱数ストリーム。SplitMix64 ベース。
/// 同じ Seed から作れば、プラットフォームや実行回数によらず同じ列を返す。
/// 用途別（世界生成／住人生成／イベント）にストリームを分けて使うこと。
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    /// <summary>文字列 Seed と用途名から、用途別ストリームを作る。</summary>
    public static DeterministicRandom FromSeed(string seed, string stream)
    {
        // FNV-1a ハッシュで文字列を 64bit に潰す（プラットフォーム非依存）
        ulong hash = 14695981039346656037UL;
        foreach (char c in seed + "|" + stream)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        // ストリーム名違いで相関が出ないよう、よく混ぜる
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        return new DeterministicRandom(hash);
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 以上 maxExclusive 未満の整数。</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>minInclusive 以上 maxExclusive 未満の整数。</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>0.0 以上 1.0 未満。</summary>
    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
    }

    /// <summary>0〜100 のパラメータ用。</summary>
    public int NextPercent()
    {
        return NextInt(101);
    }

    /// <summary>確率 p（0.0〜1.0）で true。</summary>
    public bool Chance(double p)
    {
        return NextDouble() < p;
    }
}
