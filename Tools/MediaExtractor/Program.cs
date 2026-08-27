using System.Buffers.Binary;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MediaExtractor <sharedassets.resource> <output-directory>");
    return 2;
}

var data = File.ReadAllBytes(args[0]);
var starts = new List<int>();
for (var index = 4; index <= data.Length - 4; index++)
{
    if (data[index] == (byte)'f' && data[index + 1] == (byte)'t' &&
        data[index + 2] == (byte)'y' && data[index + 3] == (byte)'p')
    {
        starts.Add(index - 4);
    }
}

Directory.CreateDirectory(args[1]);
var extracted = 0;
foreach (var start in starts)
{
    var position = (long)start;
    var atoms = new List<string>();
    var sawMdat = false;
    var sawMoov = false;
    while (position + 8 <= data.Length)
    {
        var atomStart = position;
        var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)position, 4));
        var type = Encoding.ASCII.GetString(data, (int)position + 4, 4);
        long atomSize = size;
        if (size == 1)
        {
            if (position + 16 > data.Length) break;
            atomSize = checked((long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan((int)position + 8, 8)));
        }
        else if (size == 0)
        {
            break;
        }

        if (atomSize < 8 || atomStart + atomSize > data.Length || !type.All(value => value is >= ' ' and <= '~'))
        {
            break;
        }
        atoms.Add(type);
        sawMdat |= type == "mdat";
        sawMoov |= type == "moov";
        position += atomSize;

        if (sawMdat && sawMoov)
        {
            if (position + 8 > data.Length)
            {
                break;
            }
            var nextType = Encoding.ASCII.GetString(data, (int)position + 4, 4);
            if (nextType is not ("free" or "skip" or "wide" or "mfra" or "sidx" or "uuid"))
            {
                break;
            }
        }
    }

    if (!sawMdat || !sawMoov || position <= start)
    {
        continue;
    }

    extracted++;
    var outputPath = Path.Combine(args[1], $"help{extracted}.mp4");
    using (var output = File.Create(outputPath))
    {
        output.Write(data, start, checked((int)(position - start)));
    }
    Console.WriteLine($"{Path.GetFileName(outputPath)}: offset={start}, size={position - start}, atoms={string.Join(',', atoms)}");
}

Console.WriteLine($"Extracted {extracted} MP4 files from {starts.Count} ftyp signatures.");
return extracted == 0 ? 1 : 0;
