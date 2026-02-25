using System;

public static class DisplayNameCodec40
{
    // 最大12文字
    public const int MaxLength = 12;

    // 0-9 A-Z + 記号4つ（合計40文字）
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-!.?";
    static readonly int BaseN = Alphabet.Length; // 40

    static readonly ulong[] Pow = new ulong[MaxLength + 1];
    static readonly ulong[] Offset = new ulong[MaxLength + 1];

    // ASCII(0..127) -> index（未定義は-1）
    static readonly int[] CharToIndex = new int[128];

    static DisplayNameCodec40()
    {
        if (BaseN != 40) throw new InvalidOperationException("Alphabet length must be 40.");

        for (int i = 0; i < CharToIndex.Length; i++) CharToIndex[i] = -1;

        for (int i = 0; i < Alphabet.Length; i++)
        {
            char c = Alphabet[i];
            if (c >= 128) throw new InvalidOperationException("Alphabet must be ASCII (0-127).");
            if (CharToIndex[c] != -1) throw new InvalidOperationException("Alphabet contains duplicate characters.");
            CharToIndex[c] = i;
        }

        // Pow[i] = 40^i
        Pow[0] = 1;
        for (int i = 1; i <= MaxLength; i++)
        {
            checked { Pow[i] = Pow[i - 1] * (ulong)BaseN; }
        }

        // Offset[len] = Σ(40^k, k=1..len-1)
        Offset[1] = 0;
        for (int len = 2; len <= MaxLength; len++)
        {
            checked { Offset[len] = Offset[len - 1] + Pow[len - 1]; }
        }
    }

    /// <summary>
    /// 名前を (lo, hi) 2つのintにエンコード（長さも内包）
    /// </summary>
    public static (int lo, int hi) EncodeToTwoInts(string name)
    {
        ValidateName(name);

        // value = base40
        ulong value = 0;
        for (int i = 0; i < name.Length; i++)
        {
            int d = CharToIndex[name[i]];
            checked { value = value * (ulong)BaseN + (ulong)d; }
        }

        // code = offset[len] + value（これでlenを別保存しなくてOK）
        ulong code;
        checked { code = Offset[name.Length] + value; }

        // 64bit → 32bit×2
        uint loU = (uint)(code & 0xFFFFFFFFUL);
        uint hiU = (uint)(code >> 32);

        // EOS Ingestがintしか受けない想定：ビット列として詰める（負になってOK）
        return (unchecked((int)loU), unchecked((int)hiU));
    }

    /// <summary>
    /// (lo, hi) 2つのintから名前を復元（長さも内包から復元）
    /// </summary>
    public static string DecodeFromTwoInts(int lo, int hi)
    {
        uint loU = unchecked((uint)lo);
        uint hiU = unchecked((uint)hi);
        ulong code = ((ulong)hiU << 32) | loU;

        int len = FindLength(code);
        ulong value = code - Offset[len];

        char[] chars = new char[len];
        for (int i = len - 1; i >= 0; i--)
        {
            ulong rem = value % (ulong)BaseN;
            value /= (ulong)BaseN;
            chars[i] = Alphabet[(int)rem];
        }

        return new string(chars);
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name is null or empty.", nameof(name));
        if (name.Length > MaxLength) throw new ArgumentException($"name is too long. max={MaxLength}", nameof(name));

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c >= 128 || CharToIndex[c] < 0)
            {
                throw new ArgumentException($"name contains invalid character: '{c}'. allowed set: {Alphabet}", nameof(name));
            }
        }
    }

    static int FindLength(ulong code)
    {
        // Offset[len] <= code < Offset[len] + 40^len
        for (int len = 1; len <= MaxLength; len++)
        {
            ulong start = Offset[len];
            ulong endExclusive = checked(start + Pow[len]);
            if (code >= start && code < endExclusive) return len;
        }
        throw new ArgumentOutOfRangeException(nameof(code), "code is out of valid range for this codec.");
    }
}
