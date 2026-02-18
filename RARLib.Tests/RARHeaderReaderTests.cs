using System.Text;
using Force.Crc32;

namespace RARLib.Tests;

public class RARHeaderReaderTests
{
    private static readonly byte[] RAR4Marker = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];

    /// <summary>
    /// Builds a minimal RAR4 archive header block with valid CRC.
    /// </summary>
    private static byte[] BuildArchiveHeader(RARArchiveFlags flags = RARArchiveFlags.None)
    {
        ushort headerSize = 13;
        byte[] header = new byte[headerSize];
        header[2] = 0x73; // ArchiveHeader
        BitConverter.GetBytes((ushort)flags).CopyTo(header, 3);
        BitConverter.GetBytes(headerSize).CopyTo(header, 5);

        uint crc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort crc = (ushort)(crc32 & 0xFFFF);
        BitConverter.GetBytes(crc).CopyTo(header, 0);
        return header;
    }

    /// <summary>
    /// Builds a minimal RAR4 file header block with valid CRC.
    /// </summary>
    private static byte[] BuildFileHeader(string fileName, byte hostOS = 2, uint packedSize = 100,
        uint unpackedSize = 100, byte method = 0x33, byte unpVer = 29, uint fileCrc = 0,
        uint fileTimeDOS = 0x5A8E3100, uint fileAttributes = 0x20,
        RARFileFlags extraFlags = RARFileFlags.ExtTime)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(fileName);
        ushort nameSize = (ushort)nameBytes.Length;
        RARFileFlags flags = RARFileFlags.LongBlock | extraFlags;

        int extTimeSize = (extraFlags & RARFileFlags.ExtTime) != 0 ? 2 : 0;
        ushort headerSize = (ushort)(7 + 25 + nameSize + extTimeSize);

        byte[] header = new byte[headerSize];
        header[2] = 0x74;
        BitConverter.GetBytes((ushort)flags).CopyTo(header, 3);
        BitConverter.GetBytes(headerSize).CopyTo(header, 5);
        BitConverter.GetBytes(packedSize).CopyTo(header, 7);
        BitConverter.GetBytes(unpackedSize).CopyTo(header, 11);
        header[15] = hostOS;
        BitConverter.GetBytes(fileCrc).CopyTo(header, 16);
        BitConverter.GetBytes(fileTimeDOS).CopyTo(header, 20);
        header[24] = unpVer;
        header[25] = method;
        BitConverter.GetBytes(nameSize).CopyTo(header, 26);
        BitConverter.GetBytes(fileAttributes).CopyTo(header, 28);
        nameBytes.CopyTo(header, 32);

        if ((extraFlags & RARFileFlags.ExtTime) != 0)
        {
            ushort extFlags = 0x8000; // mtime present, no extra bytes
            BitConverter.GetBytes(extFlags).CopyTo(header, 32 + nameSize);
        }

        uint crc32Full = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort crc = (ushort)(crc32Full & 0xFFFF);
        BitConverter.GetBytes(crc).CopyTo(header, 0);
        return header;
    }

    /// <summary>
    /// Builds a minimal end-of-archive block with valid CRC.
    /// </summary>
    private static byte[] BuildEndArchive()
    {
        byte[] header = new byte[7];
        header[2] = 0x7B;
        BitConverter.GetBytes((ushort)7).CopyTo(header, 5);
        uint crc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort crc = (ushort)(crc32 & 0xFFFF);
        BitConverter.GetBytes(crc).CopyTo(header, 0);
        return header;
    }

    private static MemoryStream BuildStreamWithBlocks(params byte[][] blocks)
    {
        var ms = new MemoryStream();
        foreach (var block in blocks)
        {
            ms.Write(block);
        }
        ms.Position = 0;
        return ms;
    }

    #region ReadBlock Tests

    [Fact]
    public void ReadBlock_ArchiveHeader_ParsesCorrectly()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block);
        Assert.Equal(RAR4BlockType.ArchiveHeader, block!.BlockType);
        Assert.True(block.CrcValid);
        Assert.NotNull(block.ArchiveHeader);
    }

    [Fact]
    public void ReadBlock_ArchiveHeaderWithFlags_ParsesFlagsCorrectly()
    {
        var flags = RARArchiveFlags.Volume | RARArchiveFlags.Solid | RARArchiveFlags.Protected;
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader(flags));
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.ArchiveHeader);
        Assert.True(block!.ArchiveHeader!.IsVolume);
        Assert.True(block.ArchiveHeader.IsSolid);
        Assert.True(block.ArchiveHeader.HasRecoveryRecord);
    }

    [Fact]
    public void ReadBlock_FileHeader_ParsesFields()
    {
        using var stream = BuildStreamWithBlocks(BuildFileHeader("testfile.txt", hostOS: 3, method: 0x35, unpVer: 29));
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.FileHeader);
        Assert.Equal(3, block!.FileHeader!.HostOS);     // Unix
        Assert.Equal(5, block.FileHeader.CompressionMethod); // Best (0x35 - 0x30)
        Assert.Equal(29, block.FileHeader.UnpackVersion);
        Assert.Equal("testfile.txt", block.FileHeader.FileName);
        Assert.True(block.CrcValid);
    }

    [Fact]
    public void ReadBlock_FileHeader_ParsesDOSTimestamp()
    {
        // DOS time 0x5A8E3100 encodes a specific date/time
        uint dosTime = 0x5A8E3100;
        using var stream = BuildStreamWithBlocks(BuildFileHeader("file.txt", fileTimeDOS: dosTime));
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.FileHeader);
        Assert.Equal(dosTime, block!.FileHeader!.FileTimeDOS);
        Assert.NotNull(block.FileHeader.ModifiedTime);
    }

    [Fact]
    public void ReadBlock_FileHeader_ZeroDOSTime_NullModifiedTime()
    {
        using var stream = BuildStreamWithBlocks(BuildFileHeader("file.txt", fileTimeDOS: 0));
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.FileHeader);
        Assert.Null(block!.FileHeader!.ModifiedTime);
    }

    [Fact]
    public void ReadBlock_FileHeader_DictionarySize()
    {
        // Default flags include no explicit dictionary, so 64KB
        using var stream = BuildStreamWithBlocks(BuildFileHeader("file.txt", extraFlags: RARFileFlags.ExtTime));
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.FileHeader);
        Assert.Equal(64, block!.FileHeader!.DictionarySizeKB);
    }

    [Fact]
    public void ReadBlock_ParseContentsDisabled_NoArchiveHeader()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: false);

        Assert.NotNull(block);
        Assert.Equal(RAR4BlockType.ArchiveHeader, block!.BlockType);
        Assert.Null(block.ArchiveHeader);
    }

    [Fact]
    public void ReadBlock_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock();

        Assert.Null(block);
    }

    [Fact]
    public void ReadBlock_TruncatedHeader_ReturnsNull()
    {
        // Only 5 bytes, need at least 7
        using var stream = new MemoryStream([0x00, 0x00, 0x73, 0x00, 0x00]);
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock();

        Assert.Null(block);
    }

    [Fact]
    public void ReadBlock_InvalidCrc_DetectedAsInvalid()
    {
        byte[] header = BuildArchiveHeader();
        // Corrupt the CRC
        header[0] = 0xFF;
        header[1] = 0xFF;

        using var stream = BuildStreamWithBlocks(header);
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block);
        Assert.False(block!.CrcValid);
    }

    #endregion

    #region PeekBlockType Tests

    [Fact]
    public void PeekBlockType_ReturnsTypeWithoutAdvancing()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        var reader = new RARHeaderReader(stream);

        long posBefore = stream.Position;
        byte? type = reader.PeekBlockType();
        long posAfter = stream.Position;

        Assert.Equal((byte)0x73, type); // ArchiveHeader
        Assert.Equal(posBefore, posAfter);
    }

    [Fact]
    public void PeekBlockType_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var reader = new RARHeaderReader(stream);

        Assert.Null(reader.PeekBlockType());
    }

    [Fact]
    public void PeekBlockType_TooFewBytes_ReturnsNull()
    {
        using var stream = new MemoryStream([0x00, 0x00]); // Only 2 bytes, need 3
        var reader = new RARHeaderReader(stream);

        Assert.Null(reader.PeekBlockType());
    }

    #endregion

    #region CanReadBaseHeader Tests

    [Fact]
    public void CanReadBaseHeader_SufficientBytes_ReturnsTrue()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        var reader = new RARHeaderReader(stream);

        Assert.True(reader.CanReadBaseHeader);
    }

    [Fact]
    public void CanReadBaseHeader_InsufficientBytes_ReturnsFalse()
    {
        using var stream = new MemoryStream([0x00, 0x00, 0x00]);
        var reader = new RARHeaderReader(stream);

        Assert.False(reader.CanReadBaseHeader);
    }

    #endregion

    #region SkipBlock Tests

    [Fact]
    public void SkipBlock_AdvancesToNextBlock()
    {
        byte[] archHeader = BuildArchiveHeader();
        byte[] fileHeader = BuildFileHeader("test.txt");

        using var stream = BuildStreamWithBlocks(archHeader, fileHeader);
        var reader = new RARHeaderReader(stream);

        var firstBlock = reader.ReadBlock(parseContents: false);
        Assert.NotNull(firstBlock);
        reader.SkipBlock(firstBlock!, includeData: false);

        var secondBlock = reader.ReadBlock(parseContents: true);
        Assert.NotNull(secondBlock);
        Assert.Equal(RAR4BlockType.FileHeader, secondBlock!.BlockType);
    }

    #endregion

    #region Multiple Blocks Tests

    [Fact]
    public void ReadBlock_MultipleBlocks_ReadsInSequence()
    {
        byte[] archHeader = BuildArchiveHeader(RARArchiveFlags.FirstVolume);
        byte[] fileHeader = BuildFileHeader("data.bin");
        byte[] endBlock = BuildEndArchive();

        using var stream = BuildStreamWithBlocks(archHeader, fileHeader, endBlock);
        var reader = new RARHeaderReader(stream);

        var block1 = reader.ReadBlock(parseContents: true);
        Assert.NotNull(block1);
        Assert.Equal(RAR4BlockType.ArchiveHeader, block1!.BlockType);
        reader.SkipBlock(block1, includeData: false);

        var block2 = reader.ReadBlock(parseContents: true);
        Assert.NotNull(block2);
        Assert.Equal(RAR4BlockType.FileHeader, block2!.BlockType);
        Assert.Equal("data.bin", block2.FileHeader?.FileName);
        reader.SkipBlock(block2, includeData: false);

        var block3 = reader.ReadBlock(parseContents: true);
        Assert.NotNull(block3);
        Assert.Equal(RAR4BlockType.EndArchive, block3!.BlockType);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RARHeaderReader((Stream)null!));
    }

    [Fact]
    public void Constructor_NullBinaryReader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RARHeaderReader((BinaryReader)null!));
    }

    [Fact]
    public void Constructor_WithBinaryReader_Works()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        using var br = new BinaryReader(stream);
        var reader = new RARHeaderReader(br);

        var block = reader.ReadBlock();
        Assert.NotNull(block);
        Assert.Equal(RAR4BlockType.ArchiveHeader, block!.BlockType);
    }

    #endregion

    #region Service Block (CMT) Tests

    [Fact]
    public void ReadBlock_CmtServiceBlock_ParsesSubType()
    {
        // Build a CMT service block manually
        byte[] subTypeName = "CMT"u8.ToArray();
        byte[] commentData = "Hello"u8.ToArray();
        uint addSize = (uint)commentData.Length;

        ushort headerSize = (ushort)(7 + 25 + subTypeName.Length);
        byte[] header = new byte[headerSize];
        header[2] = 0x7A; // Service block
        ushort flags = (ushort)(RARFileFlags.LongBlock | RARFileFlags.SkipIfUnknown);
        BitConverter.GetBytes(flags).CopyTo(header, 3);
        BitConverter.GetBytes(headerSize).CopyTo(header, 5);
        BitConverter.GetBytes(addSize).CopyTo(header, 7);
        BitConverter.GetBytes((uint)commentData.Length).CopyTo(header, 11);
        header[15] = 2; // Windows
        header[24] = 29;
        header[25] = 0x30; // Store
        BitConverter.GetBytes((ushort)3).CopyTo(header, 26);
        BitConverter.GetBytes(0x00000020u).CopyTo(header, 28);
        subTypeName.CopyTo(header, 32);

        uint crc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort crc = (ushort)(crc32 & 0xFFFF);
        BitConverter.GetBytes(crc).CopyTo(header, 0);

        using var stream = new MemoryStream();
        stream.Write(header);
        stream.Write(commentData);
        stream.Position = 0;

        var reader = new RARHeaderReader(stream);
        var block = reader.ReadBlock(parseContents: true);

        Assert.NotNull(block?.ServiceBlockInfo);
        Assert.Equal("CMT", block!.ServiceBlockInfo!.SubType);
        Assert.Equal(2, block.ServiceBlockInfo.HostOS);
        Assert.True(block.ServiceBlockInfo.IsStored);
        Assert.Equal((ulong)commentData.Length, block.ServiceBlockInfo.PackedSize);
    }

    [Fact]
    public void ReadServiceBlockData_CmtBlock_ReturnsData()
    {
        byte[] subTypeName = "CMT"u8.ToArray();
        byte[] commentData = "Test comment data"u8.ToArray();
        uint addSize = (uint)commentData.Length;

        ushort headerSize = (ushort)(7 + 25 + subTypeName.Length);
        byte[] header = new byte[headerSize];
        header[2] = 0x7A;
        ushort flags = (ushort)(RARFileFlags.LongBlock | RARFileFlags.SkipIfUnknown);
        BitConverter.GetBytes(flags).CopyTo(header, 3);
        BitConverter.GetBytes(headerSize).CopyTo(header, 5);
        BitConverter.GetBytes(addSize).CopyTo(header, 7);
        BitConverter.GetBytes((uint)commentData.Length).CopyTo(header, 11);
        header[15] = 2;
        header[24] = 29;
        header[25] = 0x30;
        BitConverter.GetBytes((ushort)3).CopyTo(header, 26);
        BitConverter.GetBytes(0x00000020u).CopyTo(header, 28);
        subTypeName.CopyTo(header, 32);

        uint crc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort crc = (ushort)(crc32 & 0xFFFF);
        BitConverter.GetBytes(crc).CopyTo(header, 0);

        using var stream = new MemoryStream();
        stream.Write(header);
        stream.Write(commentData);
        stream.Position = 0;

        var reader = new RARHeaderReader(stream);
        var block = reader.ReadBlock(parseContents: true);

        byte[]? data = reader.ReadServiceBlockData(block!);
        Assert.NotNull(data);
        Assert.Equal(commentData, data);
    }

    [Fact]
    public void ReadServiceBlockData_NonServiceBlock_ReturnsNull()
    {
        using var stream = BuildStreamWithBlocks(BuildArchiveHeader());
        var reader = new RARHeaderReader(stream);

        var block = reader.ReadBlock(parseContents: true);
        byte[]? data = reader.ReadServiceBlockData(block!);

        Assert.Null(data);
    }

    #endregion
}
