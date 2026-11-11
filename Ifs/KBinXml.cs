using System.Buffers.Binary;
using System.Text;

namespace CPT50DTools.Ifs;

/// <summary>A node of a decoded Konami binary XML (kbin) document.</summary>
public sealed class KNode
{
    public string Name { get; set; } = "";
    /// <summary>kbin type name, e.g. "void", "str", "3s32", "4u16".</summary>
    public string Type { get; set; } = "void";
    /// <summary>Numeric values (for numeric types), as longs/doubles.</summary>
    public object[] Values { get; set; } = Array.Empty<object>();
    /// <summary>String value (for "str" nodes).</summary>
    public string? Text { get; set; }
    public byte[]? Binary { get; set; }
    public Dictionary<string, string> Attributes { get; } = new();
    public List<KNode> Children { get; } = new();
    public KNode? Parent { get; set; }

    public KNode? Child(string name) => Children.FirstOrDefault(c => c.Name == name);

    public IEnumerable<KNode> Descendants(string name)
    {
        foreach (var c in Children)
        {
            if (c.Name == name) yield return c;
            foreach (var d in c.Descendants(name)) yield return d;
        }
    }

    public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;

    public int[] IntValues() => Values.Select(v => Convert.ToInt32(v)).ToArray();

    public override string ToString() => $"<{Name} type={Type}>";
}

/// <summary>
/// Decoder for Konami binary XML ("kbin"), ported from mon/kbinxml (used by mon/ifstools).
/// Only decoding is implemented.
/// </summary>
public static class KBinXml
{
    private const byte Signature = 0xA0;
    private const byte SigCompressed = 0x42;
    private const byte SigUncompressed = 0x45;

    private const int NodeVoid = 1;
    private const int NodeBin = 10;
    private const int NodeStr = 11;
    private const int NodeAttr = 46;
    private const int NodeEnd = 190;      // 0xBE (0xFE with array bit)
    private const int SectionEnd = 191;   // 0xBF (0xFF with array bit)

    private const string SixbitChars = "0123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";

    // id -> (name, element type char, count)
    private static readonly Dictionary<int, (string Name, char Elem, int Count)> Formats = BuildFormats();

    private static Dictionary<int, (string, char, int)> BuildFormats()
    {
        var f = new Dictionary<int, (string, char, int)>
        {
            [2] = ("s8", 'b', 1), [3] = ("u8", 'B', 1),
            [4] = ("s16", 'h', 1), [5] = ("u16", 'H', 1),
            [6] = ("s32", 'i', 1), [7] = ("u32", 'I', 1),
            [8] = ("s64", 'q', 1), [9] = ("u64", 'Q', 1),
            [12] = ("ip4", 'B', 4), [13] = ("time", 'I', 1),
            [14] = ("float", 'f', 1), [15] = ("double", 'd', 1),
            [48] = ("vs8", 'b', 16), [49] = ("vu8", 'B', 16),
            [50] = ("vs16", 'h', 8), [51] = ("vu16", 'H', 8),
            [52] = ("bool", 'b', 1), [53] = ("2b", 'b', 2),
            [54] = ("3b", 'b', 3), [55] = ("4b", 'b', 4), [56] = ("vb", 'b', 16),
        };
        // 2x/3x/4x vector types: ids 16..45
        string[] baseNames = { "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64", "f", "d" };
        char[] baseElems = { 'b', 'B', 'h', 'H', 'i', 'I', 'q', 'Q', 'f', 'd' };
        int id = 16;
        for (int count = 2; count <= 4; count++)
            for (int i = 0; i < baseNames.Length; i++)
                f[id++] = ($"{count}{baseNames[i]}", baseElems[i], count);
        return f;
    }

    private static int ElemSize(char c) => c switch
    {
        'b' or 'B' => 1,
        'h' or 'H' => 2,
        'i' or 'I' or 'f' => 4,
        _ => 8,
    };

    public static bool IsKBin(ReadOnlySpan<byte> data) =>
        data.Length > 8 && data[0] == Signature && (data[1] == SigCompressed || data[1] == SigUncompressed);

    private static Encoding GetEncoding(byte flag)
    {
        try
        {
            return (flag & 0xE0) switch
            {
                0x20 => Encoding.ASCII,
                0x40 => Encoding.Latin1,
                0x60 => Encoding.GetEncoding("EUC-JP"),
                0x80 => Encoding.GetEncoding("shift_jis"),
                0xA0 => Encoding.UTF8,
                _ => Encoding.GetEncoding(932),
            };
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public static KNode Decode(byte[] data)
    {
        if (!IsKBin(data)) throw new InvalidDataException("Not a kbin document");
        bool compressed = data[1] == SigCompressed;
        var enc = GetEncoding(data[2]);

        int nodeLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
        int nodePos = 8;
        int nodeEnd = 8 + nodeLen;
        int dataLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(nodeEnd));
        int dataStart = nodeEnd + 4;

        var reader = new DataReader(data, dataStart, dataLen);

        var root = new KNode { Name = "__root__" };
        var current = root;

        while (nodePos < nodeEnd)
        {
            int nodeType = data[nodePos++];
            bool isArray = (nodeType & 0x40) != 0;
            nodeType &= ~0x40;

            if (nodeType == NodeEnd)
            {
                if (current.Parent != null) current = current.Parent;
                continue;
            }
            if (nodeType == SectionEnd) break;

            string name = ReadName(data, ref nodePos, compressed, enc);

            if (nodeType == NodeAttr)
            {
                var bytes = reader.GrabAuto();
                current.Attributes[name] = DecodeString(bytes, enc);
                continue;
            }

            var node = new KNode { Name = name, Parent = current };
            current.Children.Add(node);
            current = node;

            switch (nodeType)
            {
                case NodeVoid:
                    node.Type = "void";
                    break;
                case NodeBin:
                    node.Type = "bin";
                    node.Binary = reader.GrabAuto();
                    break;
                case NodeStr:
                    node.Type = "str";
                    node.Text = DecodeString(reader.GrabAuto(), enc);
                    break;
                default:
                    if (!Formats.TryGetValue(nodeType, out var fmt))
                        throw new InvalidDataException($"Unknown kbin node type {nodeType}");
                    node.Type = fmt.Name;
                    int es = ElemSize(fmt.Elem);
                    if (isArray)
                    {
                        var raw = reader.GrabAuto();
                        node.Values = ParseValues(raw, fmt.Elem, raw.Length / es);
                    }
                    else
                    {
                        var raw = reader.GrabAligned(es * fmt.Count);
                        node.Values = ParseValues(raw, fmt.Elem, fmt.Count);
                    }
                    break;
            }
        }

        return root.Children.Count == 1 ? Detach(root.Children[0]) : root;
    }

    private static KNode Detach(KNode n)
    {
        n.Parent = null;
        return n;
    }

    private static string DecodeString(byte[] bytes, Encoding enc)
    {
        int len = bytes.Length;
        while (len > 0 && bytes[len - 1] == 0) len--;
        return enc.GetString(bytes, 0, len);
    }

    private static string ReadName(byte[] data, ref int pos, bool compressed, Encoding enc)
    {
        int len = data[pos++];
        if (compressed)
        {
            int byteLen = (len * 6 + 7) / 8;
            var sb = new StringBuilder(len);
            int bitPos = 0;
            for (int i = 0; i < len; i++)
            {
                int v = 0;
                for (int b = 0; b < 6; b++)
                {
                    int byteIdx = pos + (bitPos >> 3);
                    int bit = (data[byteIdx] >> (7 - (bitPos & 7))) & 1;
                    v = (v << 1) | bit;
                    bitPos++;
                }
                sb.Append(SixbitChars[v]);
            }
            pos += byteLen;
            return sb.ToString();
        }
        else
        {
            int realLen = (len & ~0x40) + 1;
            string s = enc.GetString(data, pos, realLen);
            pos += realLen;
            return s;
        }
    }

    private static object[] ParseValues(byte[] raw, char elem, int count)
    {
        var result = new object[count];
        int es = ElemSize(elem);
        var span = raw.AsSpan();
        for (int i = 0; i < count; i++)
        {
            var s = span.Slice(i * es, es);
            result[i] = elem switch
            {
                'b' => (long)(sbyte)s[0],
                'B' => (long)s[0],
                'h' => (long)BinaryPrimitives.ReadInt16BigEndian(s),
                'H' => (long)BinaryPrimitives.ReadUInt16BigEndian(s),
                'i' => (long)BinaryPrimitives.ReadInt32BigEndian(s),
                'I' => (long)BinaryPrimitives.ReadUInt32BigEndian(s),
                'q' => BinaryPrimitives.ReadInt64BigEndian(s),
                'Q' => (long)BinaryPrimitives.ReadUInt64BigEndian(s),
                'f' => (double)BinaryPrimitives.ReadSingleBigEndian(s),
                'd' => BinaryPrimitives.ReadDoubleBigEndian(s),
                _ => 0L,
            };
        }
        return result;
    }

    /// <summary>
    /// The kbin data section uses three cursors over the same buffer: a 4-byte aligned cursor,
    /// a byte cursor (for 1-byte values) and a word cursor (for 2-byte values).
    /// </summary>
    private sealed class DataReader
    {
        private readonly byte[] _data;
        private readonly int _start;
        private int _dword, _byte, _word;

        public DataReader(byte[] data, int start, int length)
        {
            _data = data;
            _start = start;
        }

        private static int Align4(int v) => (v + 3) & ~3;

        private byte[] Get(int offset, int count)
        {
            var r = new byte[count];
            Buffer.BlockCopy(_data, _start + offset, r, 0, count);
            return r;
        }

        public byte[] GrabAuto()
        {
            int size = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_start + _dword));
            _dword += 4;
            var r = Get(_dword, size);
            _dword = Align4(_dword + size);
            return r;
        }

        public byte[] GrabAligned(int size)
        {
            if (_byte % 4 == 0) _byte = _dword;
            if (_word % 4 == 0) _word = _dword;

            byte[] r;
            if (size == 1)
            {
                r = Get(_byte, 1);
                _byte += 1;
            }
            else if (size == 2)
            {
                r = Get(_word, 2);
                _word += 2;
            }
            else
            {
                r = Get(_dword, size);
                _dword = Align4(_dword + size);
            }

            int trailing = Math.Max(_byte, _word);
            if (_dword < trailing) _dword = Align4(trailing);
            return r;
        }
    }
}
