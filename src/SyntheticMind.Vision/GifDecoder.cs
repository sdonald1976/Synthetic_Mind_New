namespace SyntheticMind.Vision;

/// <summary>
/// A minimal GIF decoder — owned rather than rented. We used ImageSharp for this and it became a
/// liability twice (a CVE, then a paid-license wall). A GIF is a bounded, well-documented format:
/// LZW-compressed indexed frames over a color table. Decoding it ourselves removes the dependency,
/// the license, and the vulnerability in one move — the "own the interesting part" rule (README),
/// applied when the boring part stopped being cheap.
///
/// Handles the common cases: GIF87a/89a, global/local color tables, interlacing, transparency, and
/// frame disposal 0–3. Produces grayscale frames, which is all the retina needs.
/// </summary>
public static class GifDecoder
{
    public static (List<byte[]> Frames, int Width, int Height) DecodeGrayscale(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
            throw new InvalidDataException($"'{path}' is not a GIF file.");

        var p = 6;
        int width = data[p] | (data[p + 1] << 8); p += 2;
        int height = data[p] | (data[p + 1] << 8); p += 2;
        var packed = data[p++];
        var bgIndex = data[p++];
        p++; // pixel aspect ratio

        byte[]? globalTable = null;
        if ((packed & 0x80) != 0)
        {
            var size = 2 << (packed & 7);
            globalTable = new byte[size * 3];
            Array.Copy(data, p, globalTable, 0, globalTable.Length);
            p += globalTable.Length;
        }

        var frames = new List<byte[]>();
        var canvas = new byte[width * height];
        var bgGray = globalTable is not null ? Gray(globalTable, bgIndex) : (byte)0;

        var disposal = 0;
        var transparentIndex = -1;

        while (p < data.Length)
        {
            var block = data[p++];
            if (block == 0x3B) break;             // trailer

            if (block == 0x21)                     // extension
            {
                var label = data[p++];
                if (label == 0xF9)                 // graphic control extension
                {
                    var size = data[p++];          // always 4
                    var flags = data[p];
                    disposal = (flags >> 2) & 7;
                    transparentIndex = (flags & 1) != 0 ? data[p + 3] : -1;
                    p += size;
                    p++;                           // block terminator
                }
                else
                {
                    p = SkipSubBlocks(data, p);
                }
            }
            else if (block == 0x2C)                // image descriptor
            {
                int left = data[p] | (data[p + 1] << 8); p += 2;
                int top = data[p] | (data[p + 1] << 8); p += 2;
                int iw = data[p] | (data[p + 1] << 8); p += 2;
                int ih = data[p] | (data[p + 1] << 8); p += 2;
                var ipacked = data[p++];
                var interlaced = (ipacked & 0x40) != 0;

                var table = globalTable;
                if ((ipacked & 0x80) != 0)
                {
                    var size = 2 << (ipacked & 7);
                    table = new byte[size * 3];
                    Array.Copy(data, p, table, 0, table.Length);
                    p += table.Length;
                }

                var minCodeSize = data[p++];
                var indices = LzwDecode(data, ref p, minCodeSize, iw * ih);

                var restore = disposal == 3 ? (byte[])canvas.Clone() : null;

                Compose(canvas, width, indices, left, top, iw, ih, interlaced, table!, transparentIndex);
                frames.Add((byte[])canvas.Clone());

                if (disposal == 2)
                    for (var y = 0; y < ih; y++)
                        for (var x = 0; x < iw; x++)
                            canvas[(top + y) * width + left + x] = bgGray;
                else if (disposal == 3 && restore is not null)
                    canvas = restore;
            }
            else
            {
                break;   // unknown block — stop rather than misread
            }
        }

        if (frames.Count == 0) throw new InvalidDataException($"'{path}' decoded to zero frames.");
        return (frames, width, height);
    }

    private static void Compose(byte[] canvas, int canvasWidth, byte[] indices,
        int left, int top, int iw, int ih, bool interlaced, byte[] table, int transparentIndex)
    {
        // Interlaced GIFs store rows in 4 passes; map the decoded row order back to real rows.
        var rowOrder = interlaced ? InterlacedRowOrder(ih) : null;
        for (var row = 0; row < ih; row++)
        {
            var destRow = rowOrder is null ? row : rowOrder[row];
            for (var x = 0; x < iw; x++)
            {
                var index = indices[row * iw + x];
                if (index == transparentIndex) continue;   // keep whatever was underneath
                canvas[(top + destRow) * canvasWidth + left + x] = Gray(table, index);
            }
        }
    }

    private static int[] InterlacedRowOrder(int height)
    {
        var order = new int[height];
        var k = 0;
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            for (var y = start; y < height; y += step) order[k++] = y;
        return order;
    }

    private static byte Gray(byte[] table, int index)
    {
        var o = index * 3;
        if (o + 2 >= table.Length) return 0;
        return (byte)(0.299f * table[o] + 0.587f * table[o + 1] + 0.114f * table[o + 2]);
    }

    private static int SkipSubBlocks(byte[] data, int p)
    {
        while (p < data.Length && data[p] != 0) p += data[p] + 1;
        return p + 1;   // step past the 0 terminator
    }

    /// <summary>GIF-variant LZW: concatenate the sub-blocks, then decode the variable-width bit stream.</summary>
    private static byte[] LzwDecode(byte[] data, ref int p, int minCodeSize, int pixelCount)
    {
        // Gather the LZW sub-blocks into one buffer.
        var lzw = new List<byte>();
        while (p < data.Length && data[p] != 0)
        {
            var len = data[p++];
            for (var i = 0; i < len; i++) lzw.Add(data[p++]);
        }
        p++; // terminator

        var output = new byte[pixelCount];
        var outPos = 0;

        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var codeSize = minCodeSize + 1;

        var dict = new List<byte[]>();
        void ResetDict()
        {
            dict.Clear();
            for (var i = 0; i < clearCode; i++) dict.Add([(byte)i]);
            dict.Add([]);   // clear
            dict.Add([]);   // end
            codeSize = minCodeSize + 1;
        }
        ResetDict();

        var bitPos = 0;
        int ReadCode()
        {
            var code = 0;
            for (var i = 0; i < codeSize; i++)
            {
                var byteIndex = bitPos >> 3;
                if (byteIndex >= lzw.Count) return endCode;
                var bit = (lzw[byteIndex] >> (bitPos & 7)) & 1;
                code |= bit << i;
                bitPos++;
            }
            return code;
        }

        var previous = -1;
        while (outPos < pixelCount)
        {
            var code = ReadCode();
            if (code == clearCode) { ResetDict(); previous = -1; continue; }
            if (code == endCode) break;

            byte[] entry;
            if (code < dict.Count) entry = dict[code];
            else if (previous >= 0) { var prev = dict[previous]; entry = [.. prev, prev[0]]; }
            else break;

            foreach (var b in entry) { if (outPos < pixelCount) output[outPos++] = b; }

            if (previous >= 0)
            {
                var prev = dict[previous];
                dict.Add([.. prev, entry[0]]);
                if (dict.Count == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            previous = code;
        }

        return output;
    }
}
