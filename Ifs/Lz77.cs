namespace CPT50DTools.Ifs;

/// <summary>Konami LZ77 ("avslz") decompressor, ported from mon/kbinxml lz77.py.</summary>
public static class Lz77
{
    private const int Threshold = 3;

    public static byte[] Decompress(ReadOnlySpan<byte> input, int expectedSize = 0)
    {
        var buf = new byte[Math.Max(expectedSize, 1024)];
        int outLen = 0;
        int pos = 0;

        void Ensure(int extra)
        {
            if (outLen + extra > buf.Length)
                Array.Resize(ref buf, Math.Max(buf.Length * 2, outLen + extra));
        }

        while (pos < input.Length)
        {
            int flag = input[pos++];
            for (int i = 0; i < 8; i++)
            {
                if (((flag >> i) & 1) == 1)
                {
                    if (pos >= input.Length) goto done;
                    Ensure(1);
                    buf[outLen++] = input[pos++];
                }
                else
                {
                    if (pos + 1 >= input.Length) goto done;
                    int w = (input[pos] << 8) | input[pos + 1];
                    pos += 2;
                    int position = w >> 4;
                    int length = (w & 0x0F) + Threshold;
                    if (position == 0) goto done;

                    Ensure(length);
                    if (position > outLen)
                    {
                        int diff = Math.Min(position - outLen, length);
                        Array.Clear(buf, outLen, diff);
                        outLen += diff;
                        length -= diff;
                    }
                    for (int l = 0; l < length; l++)
                    {
                        buf[outLen] = buf[outLen - position];
                        outLen++;
                    }
                }
            }
        }
    done:
        if (outLen != buf.Length) Array.Resize(ref buf, outLen);
        return buf;
    }
}
