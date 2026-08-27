using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

const string FamilyName = "FusionPixel12ZhHans";

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: FontBuilder <source.bdf> <translations.json> <output.ttf>");
    return 2;
}

var wanted = CollectCharacters(args[1]);
var parsed = ReadBdf(args[0], wanted);
var missing = wanted.Where(codepoint => !parsed.Glyphs.ContainsKey(codepoint)).ToArray();
if (missing.Length > 0)
{
    Console.Error.WriteLine(
        $"Warning: {missing.Length} requested glyphs were absent from the BDF: " +
        string.Join(", ", missing.Select(value => $"U+{value:X4}"))
    );
}

var font = TrueTypeBuilder.Build(FamilyName, parsed.NotDef, parsed.Glyphs);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllBytes(args[2], font);
Console.WriteLine($"Wrote {parsed.Glyphs.Count + 1} glyphs ({font.Length:N0} bytes) to {args[2]}");
return 0;

static HashSet<int> CollectCharacters(string translationPath)
{
    var result = new HashSet<int>();
    using var document = JsonDocument.Parse(File.ReadAllText(translationPath));
    foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
    {
        AddString(entry.GetProperty("original").GetString());
        AddString(entry.GetProperty("translation").GetString());
    }

    for (var codepoint = 0x20; codepoint <= 0x7E; codepoint++)
    {
        result.Add(codepoint);
    }
    for (var codepoint = 0x3041; codepoint <= 0x3096; codepoint++)
    {
        result.Add(codepoint);
    }
    for (var codepoint = 0x3099; codepoint <= 0x30FF; codepoint++)
    {
        result.Add(codepoint);
    }
    result.Add(0x3000);

    return result;

    void AddString(string? value)
    {
        if (value == null)
        {
            return;
        }
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsControl(rune))
            {
                result.Add(rune.Value);
            }
        }
    }
}

static BdfFont ReadBdf(string path, HashSet<int> wanted)
{
    using var reader = new StreamReader(path, Encoding.ASCII, true, 1 << 20);
    var glyphs = new Dictionary<int, BdfGlyph>();
    BdfGlyph? notDef = null;
    string? line;

    while ((line = reader.ReadLine()) != null)
    {
        if (!line.StartsWith("STARTCHAR ", StringComparison.Ordinal))
        {
            continue;
        }

        var glyph = new BdfGlyph();
        var bitmap = new List<string>();
        while ((line = reader.ReadLine()) != null && line != "ENDCHAR")
        {
            if (line.StartsWith("ENCODING ", StringComparison.Ordinal))
            {
                glyph.Codepoint = int.Parse(line.AsSpan(9));
            }
            else if (line.StartsWith("DWIDTH ", StringComparison.Ordinal))
            {
                glyph.Advance = int.Parse(line.AsSpan(7, line.IndexOf(' ', 7) - 7));
            }
            else if (line.StartsWith("BBX ", StringComparison.Ordinal))
            {
                var values = line.AsSpan(4).ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                glyph.Width = int.Parse(values[0]);
                glyph.Height = int.Parse(values[1]);
                glyph.XOffset = int.Parse(values[2]);
                glyph.YOffset = int.Parse(values[3]);
            }
            else if (line == "BITMAP")
            {
                while ((line = reader.ReadLine()) != null && line != "ENDCHAR")
                {
                    bitmap.Add(line);
                }
                break;
            }
        }

        if (glyph.Codepoint == -1 || wanted.Contains(glyph.Codepoint))
        {
            glyph.Rows = bitmap.ToArray();
            if (glyph.Codepoint == -1)
            {
                notDef = glyph;
            }
            else
            {
                glyphs[glyph.Codepoint] = glyph;
            }
        }
    }

    if (notDef == null)
    {
        throw new InvalidDataException("The BDF has no .notdef glyph.");
    }
    return new BdfFont(notDef, glyphs);
}

sealed record BdfFont(BdfGlyph NotDef, Dictionary<int, BdfGlyph> Glyphs);

sealed class BdfGlyph
{
    internal int Codepoint = int.MinValue;
    internal int Advance;
    internal int Width;
    internal int Height;
    internal int XOffset;
    internal int YOffset;
    internal string[] Rows = Array.Empty<string>();

    internal List<Rect> GetRuns()
    {
        var result = new List<Rect>();
        for (var row = 0; row < Rows.Length && row < Height; row++)
        {
            var bytes = Convert.FromHexString(Rows[row]);
            var x = 0;
            while (x < Width)
            {
                if (!IsSet(bytes, x))
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < Width && IsSet(bytes, x))
                {
                    x++;
                }

                var yTop = YOffset + Height - row;
                result.Add(new Rect(XOffset + start, yTop - 1, XOffset + x, yTop));
            }
        }
        return result;

        static bool IsSet(byte[] bytes, int x)
        {
            return (bytes[x / 8] & (0x80 >> (x % 8))) != 0;
        }
    }
}

readonly record struct Rect(int XMin, int YMin, int XMax, int YMax);

static class TrueTypeBuilder
{
    private const uint ChecksumMagic = 0xB1B0AFBA;
    private const int UnitsPerPixel = 64;

    internal static byte[] Build(string familyName, BdfGlyph notDef, Dictionary<int, BdfGlyph> sourceGlyphs)
    {
        var glyphs = new List<BdfGlyph> { notDef };
        var glyphIds = new Dictionary<int, ushort>();
        foreach (var pair in sourceGlyphs.OrderBy(pair => pair.Key))
        {
            glyphIds[pair.Key] = checked((ushort)glyphs.Count);
            glyphs.Add(pair.Value);
        }

        var glyf = BuildGlyf(glyphs, out var loca, out var metrics, out var bounds, out var maxima);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["OS/2"] = BuildOs2(sourceGlyphs.Keys, metrics, bounds),
            ["cmap"] = BuildCmap(glyphIds),
            ["glyf"] = glyf,
            ["head"] = BuildHead(bounds),
            ["hhea"] = BuildHhea(metrics, bounds),
            ["hmtx"] = BuildHmtx(metrics),
            ["loca"] = BuildLoca(loca),
            ["maxp"] = BuildMaxp(glyphs.Count, maxima),
            ["name"] = BuildName(familyName),
            ["post"] = BuildPost()
        };

        return Assemble(tables);
    }

    private static byte[] BuildGlyf(
        List<BdfGlyph> glyphs,
        out uint[] loca,
        out List<GlyphMetric> metrics,
        out FontBounds fontBounds,
        out GlyphMaxima maxima)
    {
        using var output = new MemoryStream();
        loca = new uint[glyphs.Count + 1];
        metrics = new List<GlyphMetric>(glyphs.Count);
        fontBounds = new FontBounds(short.MaxValue, short.MaxValue, short.MinValue, short.MinValue);
        maxima = new GlyphMaxima();

        for (var glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
        {
            loca[glyphIndex] = checked((uint)output.Position);
            var glyph = glyphs[glyphIndex];
            var runs = glyph.GetRuns();
            var advance = checked((ushort)Math.Max(1, glyph.Advance * UnitsPerPixel));
            if (runs.Count == 0)
            {
                metrics.Add(new GlyphMetric(advance, 0, 0, 0, 0, 0));
                continue;
            }

            var xMin = checked((short)(runs.Min(rect => rect.XMin) * UnitsPerPixel));
            var yMin = checked((short)(runs.Min(rect => rect.YMin) * UnitsPerPixel));
            var xMax = checked((short)(runs.Max(rect => rect.XMax) * UnitsPerPixel));
            var yMax = checked((short)(runs.Max(rect => rect.YMax) * UnitsPerPixel));
            metrics.Add(new GlyphMetric(advance, xMin, xMin, yMin, xMax, yMax));
            fontBounds = fontBounds.Include(xMin, yMin, xMax, yMax);
            maxima.MaxContours = Math.Max(maxima.MaxContours, runs.Count);
            maxima.MaxPoints = Math.Max(maxima.MaxPoints, runs.Count * 4);

            using var glyphStream = new MemoryStream();
            var writer = new BeWriter(glyphStream);
            writer.I16(checked((short)runs.Count));
            writer.I16(xMin);
            writer.I16(yMin);
            writer.I16(xMax);
            writer.I16(yMax);
            for (var contour = 0; contour < runs.Count; contour++)
            {
                writer.U16(checked((ushort)(contour * 4 + 3)));
            }
            writer.U16(0);

            var points = new List<(short X, short Y)>(runs.Count * 4);
            foreach (var run in runs)
            {
                var left = checked((short)(run.XMin * UnitsPerPixel));
                var right = checked((short)(run.XMax * UnitsPerPixel));
                var bottom = checked((short)(run.YMin * UnitsPerPixel));
                var top = checked((short)(run.YMax * UnitsPerPixel));
                points.Add((left, bottom));
                points.Add((left, top));
                points.Add((right, top));
                points.Add((right, bottom));
            }
            foreach (var _ in points)
            {
                writer.U8(0x01);
            }

            short previous = 0;
            foreach (var point in points)
            {
                writer.I16(checked((short)(point.X - previous)));
                previous = point.X;
            }
            previous = 0;
            foreach (var point in points)
            {
                writer.I16(checked((short)(point.Y - previous)));
                previous = point.Y;
            }

            var data = glyphStream.ToArray();
            output.Write(data);
            while ((output.Position & 3) != 0)
            {
                output.WriteByte(0);
            }
        }

        loca[glyphs.Count] = checked((uint)output.Position);
        return output.ToArray();
    }

    private static byte[] BuildCmap(Dictionary<int, ushort> glyphIds)
    {
        var entries = glyphIds.Where(pair => pair.Key <= 0xFFFE).OrderBy(pair => pair.Key).ToArray();
        var segmentCount = entries.Length + 1;
        var subtableLength = checked((ushort)(16 + segmentCount * 8 + entries.Length * 2));
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U16(0);
        writer.U16(2);
        writer.U16(0); writer.U16(3); writer.U32(20);
        writer.U16(3); writer.U16(1); writer.U32(20);

        writer.U16(4);
        writer.U16(subtableLength);
        writer.U16(0);
        writer.U16(checked((ushort)(segmentCount * 2)));
        var power = HighestPowerOfTwo(segmentCount);
        writer.U16(checked((ushort)(power * 2)));
        writer.U16(checked((ushort)Math.Log2(power)));
        writer.U16(checked((ushort)(segmentCount * 2 - power * 2)));
        foreach (var pair in entries) writer.U16(checked((ushort)pair.Key));
        writer.U16(0xFFFF);
        writer.U16(0);
        foreach (var pair in entries) writer.U16(checked((ushort)pair.Key));
        writer.U16(0xFFFF);
        foreach (var _ in entries) writer.I16(0);
        writer.I16(1);
        foreach (var _ in entries) writer.U16(checked((ushort)(segmentCount * 2)));
        writer.U16(0);
        foreach (var pair in entries) writer.U16(pair.Value);
        return stream.ToArray();
    }

    private static byte[] BuildHead(FontBounds bounds)
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U32(0x00010000);
        writer.U32(0x00010000);
        writer.U32(0);
        writer.U32(0x5F0F3CF5);
        writer.U16(0x000B);
        writer.U16(1024);
        var sourceRelease = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var macSeconds = checked((ulong)(sourceRelease.ToUnixTimeSeconds() + 2082844800L));
        writer.U64(macSeconds);
        writer.U64(macSeconds);
        writer.I16(bounds.XMin); writer.I16(bounds.YMin); writer.I16(bounds.XMax); writer.I16(bounds.YMax);
        writer.U16(0);
        writer.U16(8);
        writer.I16(2);
        writer.I16(1);
        writer.I16(0);
        return stream.ToArray();
    }

    private static byte[] BuildHhea(List<GlyphMetric> metrics, FontBounds bounds)
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U32(0x00010000);
        writer.I16(13 * UnitsPerPixel);
        writer.I16(-3 * UnitsPerPixel);
        writer.I16(0);
        writer.U16(metrics.Max(metric => metric.Advance));
        writer.I16(metrics.Min(metric => metric.LeftSideBearing));
        writer.I16(metrics.Min(metric => checked((short)(metric.Advance - metric.LeftSideBearing - metric.Width))));
        writer.I16(metrics.Max(metric => checked((short)(metric.LeftSideBearing + metric.Width))));
        writer.I16(1); writer.I16(0); writer.I16(0);
        writer.I16(0); writer.I16(0); writer.I16(0); writer.I16(0);
        writer.I16(0);
        writer.U16(checked((ushort)metrics.Count));
        return stream.ToArray();
    }

    private static byte[] BuildHmtx(List<GlyphMetric> metrics)
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        foreach (var metric in metrics)
        {
            writer.U16(metric.Advance);
            writer.I16(metric.LeftSideBearing);
        }
        return stream.ToArray();
    }

    private static byte[] BuildLoca(uint[] loca)
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        foreach (var offset in loca) writer.U32(offset);
        return stream.ToArray();
    }

    private static byte[] BuildMaxp(int glyphCount, GlyphMaxima maxima)
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U32(0x00010000);
        writer.U16(checked((ushort)glyphCount));
        writer.U16(checked((ushort)maxima.MaxPoints));
        writer.U16(checked((ushort)maxima.MaxContours));
        writer.U16(0); writer.U16(0);
        writer.U16(2);
        writer.U16(0); writer.U16(0); writer.U16(0); writer.U16(0); writer.U16(0); writer.U16(0);
        writer.U16(0); writer.U16(0);
        return stream.ToArray();
    }

    private static byte[] BuildName(string familyName)
    {
        var names = new (ushort Id, string Value)[]
        {
            (1, familyName),
            (2, "Regular"),
            (3, familyName + " Regular 1.0"),
            (4, familyName + " Regular"),
            (5, "Version 1.0"),
            (6, "FusionPixel12PropZhHans")
        };
        var encoded = names.Select(name => Encoding.BigEndianUnicode.GetBytes(name.Value)).ToArray();
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U16(0);
        writer.U16(checked((ushort)names.Length));
        writer.U16(checked((ushort)(6 + names.Length * 12)));
        var offset = 0;
        for (var index = 0; index < names.Length; index++)
        {
            writer.U16(3); writer.U16(1); writer.U16(0x0409); writer.U16(names[index].Id);
            writer.U16(checked((ushort)encoded[index].Length));
            writer.U16(checked((ushort)offset));
            offset += encoded[index].Length;
        }
        foreach (var bytes in encoded) stream.Write(bytes);
        return stream.ToArray();
    }

    private static byte[] BuildOs2(IEnumerable<int> codepoints, List<GlyphMetric> metrics, FontBounds bounds)
    {
        var values = codepoints.Where(value => value <= 0xFFFF).ToArray();
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U16(0);
        writer.I16(checked((short)metrics.Average(metric => metric.Advance)));
        writer.U16(400); writer.U16(5); writer.U16(0);
        writer.I16(650); writer.I16(699); writer.I16(0); writer.I16(140);
        writer.I16(650); writer.I16(699); writer.I16(0); writer.I16(300);
        writer.I16(64); writer.I16(320); writer.I16(0);
        for (var index = 0; index < 10; index++) writer.U8(0);
        writer.U32(0); writer.U32(0); writer.U32(0); writer.U32(0);
        writer.Tag("ECHO");
        writer.U16(0x0040);
        writer.U16(checked((ushort)values.Min()));
        writer.U16(checked((ushort)values.Max()));
        writer.I16(13 * UnitsPerPixel);
        writer.I16(-3 * UnitsPerPixel);
        writer.I16(0);
        writer.U16(checked((ushort)Math.Max(0, (int)bounds.YMax)));
        writer.U16(checked((ushort)Math.Max(0, -(int)bounds.YMin)));
        return stream.ToArray();
    }

    private static byte[] BuildPost()
    {
        using var stream = new MemoryStream();
        var writer = new BeWriter(stream);
        writer.U32(0x00030000);
        writer.U32(0);
        writer.I16(-UnitsPerPixel);
        writer.I16(UnitsPerPixel);
        writer.U32(0);
        writer.U32(0); writer.U32(0); writer.U32(0); writer.U32(0);
        return stream.ToArray();
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var numTables = tables.Count;
        var power = HighestPowerOfTwo(numTables);
        var directoryLength = 12 + numTables * 16;
        var records = new List<TableRecord>(numTables);
        var offset = directoryLength;
        foreach (var pair in tables)
        {
            offset = Align4(offset);
            records.Add(new TableRecord(pair.Key, Checksum(pair.Value), offset, pair.Value.Length, pair.Value));
            offset += Align4(pair.Value.Length);
        }

        var font = new byte[offset];
        using (var stream = new MemoryStream(font, true))
        {
            var writer = new BeWriter(stream);
            writer.U32(0x00010000);
            writer.U16(checked((ushort)numTables));
            writer.U16(checked((ushort)(power * 16)));
            writer.U16(checked((ushort)Math.Log2(power)));
            writer.U16(checked((ushort)(numTables * 16 - power * 16)));
            foreach (var record in records)
            {
                writer.Tag(record.Tag);
                writer.U32(record.Checksum);
                writer.U32(checked((uint)record.Offset));
                writer.U32(checked((uint)record.Length));
                Array.Copy(record.Data, 0, font, record.Offset, record.Data.Length);
            }
        }

        var sum = Checksum(font);
        var adjustment = unchecked(ChecksumMagic - sum);
        var head = records.Single(record => record.Tag == "head");
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(head.Offset + 8, 4), adjustment);
        return font;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        Span<byte> word = stackalloc byte[4];
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            word.Clear();
            data.AsSpan(offset, Math.Min(4, data.Length - offset)).CopyTo(word);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(word));
        }
        return sum;
    }

    private static int HighestPowerOfTwo(int value)
    {
        var result = 1;
        while (result * 2 <= value) result *= 2;
        return result;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private sealed record TableRecord(string Tag, uint Checksum, int Offset, int Length, byte[] Data);
    private sealed record GlyphMetric(ushort Advance, short LeftSideBearing, short XMin, short YMin, short XMax, short YMax)
    {
        internal short Width => checked((short)(XMax - XMin));
    }
    private sealed record FontBounds(short XMin, short YMin, short XMax, short YMax)
    {
        internal FontBounds Include(short xMin, short yMin, short xMax, short yMax) => new(
            Math.Min(XMin, xMin), Math.Min(YMin, yMin), Math.Max(XMax, xMax), Math.Max(YMax, yMax));
    }
    private sealed class GlyphMaxima
    {
        internal int MaxPoints;
        internal int MaxContours;
    }
}

sealed class BeWriter
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    internal BeWriter(Stream stream) => _stream = stream;
    internal void U8(byte value) => _stream.WriteByte(value);
    internal void U16(ushort value) { BinaryPrimitives.WriteUInt16BigEndian(_buffer, value); _stream.Write(_buffer, 0, 2); }
    internal void I16(short value) => U16(unchecked((ushort)value));
    internal void U32(uint value) { BinaryPrimitives.WriteUInt32BigEndian(_buffer, value); _stream.Write(_buffer, 0, 4); }
    internal void U64(ulong value) { BinaryPrimitives.WriteUInt64BigEndian(_buffer, value); _stream.Write(_buffer, 0, 8); }
    internal void Tag(string value) { var bytes = Encoding.ASCII.GetBytes(value); if (bytes.Length != 4) throw new ArgumentException("Tag must be four bytes."); _stream.Write(bytes); }
}
