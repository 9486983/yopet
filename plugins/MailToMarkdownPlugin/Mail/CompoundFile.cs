using System.Buffers.Binary;
using System.Text;

namespace MailToMarkdownPlugin.Mail;

internal sealed class DirEntry
{
    public uint Id { get; init; }
    public string Name { get; set; } = "";
    public byte Type { get; set; }
    public uint Left { get; set; }
    public uint Right { get; set; }
    public uint Child { get; set; }
    public uint StartSector { get; set; }
    public ulong Size { get; set; }
}

internal sealed class CompoundFile
{
    private const uint NoStream = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;

    private readonly byte[] _data;
    private readonly ushort _majorVersion;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _miniCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly List<DirEntry> _entries = new();
    private readonly Dictionary<string, DirEntry> _rootStreams = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DirEntry> _rootStorages = new();
    private readonly byte[] _miniStream;
    private readonly DirEntry _root;

    public CompoundFile(string path) : this(File.ReadAllBytes(path))
    {
    }

    internal CompoundFile(byte[] data)
    {
        _data = data;
        if (data.Length < 512 ||
            data[0] != 0xD0 || data[1] != 0xCF || data[2] != 0x11 || data[3] != 0xE0 ||
            data[4] != 0xA1 || data[5] != 0xB1 || data[6] != 0x1A || data[7] != 0xE1)
            throw new InvalidDataException("Not an OLE compound file.");

        _majorVersion = U16(26);
        var sectorShift = U16(30);
        var miniSectorShift = U16(32);
        if (sectorShift is < 7 or > 12 || miniSectorShift is < 2 or > 12)
            throw new InvalidDataException("Unsupported compound file sector size.");

        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << miniSectorShift;
        _miniCutoff = U32(56);

        var fatSectorCount = U32(44);
        var firstDirectorySector = U32(48);
        var firstMiniFatSector = U32(60);
        var firstDifatSector = U32(68);
        var difatSectorCount = U32(72);

        var fatSectors = ReadDifat(firstDifatSector, difatSectorCount);
        if (fatSectorCount > 0 && fatSectorCount < fatSectors.Count)
            fatSectors = fatSectors.Take((int)fatSectorCount).ToList();

        _fat = LoadFat(fatSectors);
        LoadDirectory(firstDirectorySector);

        _root = _entries.FirstOrDefault(e => e.Type == 5)
                ?? throw new InvalidDataException("Compound file root entry is missing.");

        foreach (var entry in EnumerateSiblings(_root.Child))
        {
            if (entry.Type == 2)
                _rootStreams[entry.Name] = entry;
            else if (entry.Type == 1)
                _rootStorages.Add(entry);
        }

        _miniStream = ReadRegularChain(_root.StartSector, _root.Size);
        _miniFat = LoadMiniFat(firstMiniFatSector);
    }

    public byte[]? GetRootStream(string name) =>
        _rootStreams.TryGetValue(name, out var entry) ? ReadStream(entry) : null;

    public IReadOnlyList<DirEntry> GetRootStorages(string namePrefix) =>
        _rootStorages
            .Where(s => s.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public Dictionary<string, DirEntry> GetStorageStreams(DirEntry storage)
    {
        var result = new Dictionary<string, DirEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateSiblings(storage.Child))
        {
            if (entry.Type == 2)
                result[entry.Name] = entry;
        }
        return result;
    }

    public byte[] ReadStream(DirEntry entry)
    {
        if (entry.Type != 2 || entry.Size == 0) return Array.Empty<byte>();

        if (entry.Size < _miniCutoff)
            return ReadMiniChain(entry.StartSector, entry.Size);

        return ReadRegularChain(entry.StartSector, entry.Size);
    }

    private List<uint> ReadDifat(uint firstDifatSector, uint difatSectorCount)
    {
        var sectors = new List<uint>();
        for (var i = 0; i < 109; i++)
        {
            var sector = U32(76 + i * 4);
            if (sector != FreeSector) sectors.Add(sector);
        }

        var current = firstDifatSector;
        var perSector = _sectorSize / 4;
        var guard = 0;
        while (current != EndOfChain && current != FreeSector && guard++ < 65536 &&
               (difatSectorCount == 0 || guard <= difatSectorCount))
        {
            var offset = SectorOffset(current);
            if (offset + _sectorSize > _data.Length) break;

            for (var i = 0; i < perSector - 1; i++)
            {
                var sector = U32(offset + i * 4);
                if (sector != FreeSector) sectors.Add(sector);
            }

            current = U32(offset + (perSector - 1) * 4);
        }

        return sectors;
    }

    private uint[] LoadFat(List<uint> fatSectors)
    {
        var perSector = _sectorSize / 4;
        var fat = new uint[fatSectors.Count * perSector];

        for (var i = 0; i < fatSectors.Count; i++)
        {
            var offset = SectorOffset(fatSectors[i]);
            if (offset + _sectorSize > _data.Length) continue;

            for (var j = 0; j < perSector; j++)
                fat[i * perSector + j] = U32(offset + j * 4);
        }

        return fat;
    }

    private uint[] LoadMiniFat(uint firstMiniFatSector)
    {
        var sectors = ReadSectorChain(firstMiniFatSector);
        var perSector = _sectorSize / 4;
        var miniFat = new uint[sectors.Count * perSector];

        for (var i = 0; i < sectors.Count; i++)
        {
            var offset = SectorOffset(sectors[i]);
            for (var j = 0; j < perSector; j++)
                miniFat[i * perSector + j] = U32(offset + j * 4);
        }

        return miniFat;
    }

    private void LoadDirectory(uint firstSector)
    {
        var entriesPerSector = _sectorSize / 128;
        foreach (var sector in ReadSectorChain(firstSector))
        {
            var offset = SectorOffset(sector);
            for (var i = 0; i < entriesPerSector; i++)
            {
                var entryOffset = offset + i * 128;
                if (entryOffset + 128 > _data.Length) break;

                var nameLength = (int)U16(entryOffset + 64);
                var name = nameLength is >= 2 and <= 64
                    ? Encoding.Unicode.GetString(_data, (int)entryOffset, nameLength - 2)
                    : "";

                var size = U64(entryOffset + 120);
                if (_majorVersion < 4) size &= 0xFFFFFFFF;

                _entries.Add(new DirEntry
                {
                    Id = (uint)_entries.Count,
                    Name = name,
                    Type = _data[entryOffset + 66],
                    Left = U32(entryOffset + 68),
                    Right = U32(entryOffset + 72),
                    Child = U32(entryOffset + 76),
                    StartSector = U32(entryOffset + 116),
                    Size = size,
                });
            }
        }
    }

    private IEnumerable<DirEntry> EnumerateSiblings(uint first)
    {
        var stack = new Stack<uint>();
        var seen = new HashSet<uint>();
        stack.Push(first);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == NoStream || id >= _entries.Count || !seen.Add(id)) continue;

            var entry = _entries[(int)id];
            if (entry.Type == 0) continue;

            yield return entry;
            stack.Push(entry.Left);
            stack.Push(entry.Right);
        }
    }

    private List<uint> ReadSectorChain(uint start)
    {
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        var sector = start;
        var guard = 0;

        while (sector != EndOfChain && sector != FreeSector &&
               guard++ < _fat.Length + 2 && seen.Add(sector))
        {
            if (SectorOffset(sector) + _sectorSize > _data.Length) break;

            result.Add(sector);
            sector = sector < _fat.Length ? _fat[sector] : EndOfChain;
        }

        return result;
    }

    private byte[] ReadRegularChain(uint start, ulong size)
    {
        if (size == 0) return Array.Empty<byte>();

        var total = (int)Math.Min(size, int.MaxValue);
        var buffer = new byte[total];
        var written = 0;
        var sector = start;
        var seen = new HashSet<uint>();
        var guard = 0;

        while (written < total && sector != EndOfChain && sector != FreeSector &&
               guard++ < _fat.Length + 2 && seen.Add(sector))
        {
            var offset = SectorOffset(sector);
            if (offset < 0 || offset >= _data.Length) break;

            var count = Math.Min(_sectorSize, total - written);
            if (offset + count > _data.Length) break;

            Buffer.BlockCopy(_data, (int)offset, buffer, written, count);
            written += count;
            sector = sector < _fat.Length ? _fat[sector] : EndOfChain;
        }

        return written == total ? buffer : buffer[..written];
    }

    private byte[] ReadMiniChain(uint start, ulong size)
    {
        if (size == 0 || _miniStream.Length == 0) return Array.Empty<byte>();

        var total = (int)Math.Min(size, int.MaxValue);
        var buffer = new byte[total];
        var written = 0;
        var sector = start;
        var seen = new HashSet<uint>();
        var guard = 0;

        while (written < total && sector != EndOfChain && sector != FreeSector &&
               guard++ < _miniFat.Length + 2 && seen.Add(sector))
        {
            var offset = (long)sector * _miniSectorSize;
            if (offset < 0 || offset >= _miniStream.Length) break;

            var count = Math.Min(_miniSectorSize, total - written);
            if (offset + count > _miniStream.Length) break;

            Buffer.BlockCopy(_miniStream, (int)offset, buffer, written, count);
            written += count;
            sector = sector < _miniFat.Length ? _miniFat[sector] : EndOfChain;
        }

        return written == total ? buffer : buffer[..written];
    }

    private long SectorOffset(uint sector) => ((long)sector + 1) * _sectorSize;

    private ushort U16(long offset) =>
        offset >= 0 && offset + 2 <= _data.Length
            ? BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan((int)offset, 2))
            : (ushort)0;

    private uint U32(long offset) =>
        offset >= 0 && offset + 4 <= _data.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan((int)offset, 4))
            : 0;

    private ulong U64(long offset) =>
        offset >= 0 && offset + 8 <= _data.Length
            ? BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan((int)offset, 8))
            : 0;
}
