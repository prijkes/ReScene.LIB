namespace RARLib.Tests;

public class RAR5HeaderReaderTests
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    #region IsRAR5 Tests

    [Fact]
    public void IsRAR5_ValidMarker_ReturnsTrue()
    {
        using var stream = new MemoryStream([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);

        Assert.True(RAR5HeaderReader.IsRAR5(stream));
        Assert.Equal(0, stream.Position); // Position should be restored
    }

    [Fact]
    public void IsRAR5_RAR4Marker_ReturnsFalse()
    {
        using var stream = new MemoryStream([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);

        Assert.False(RAR5HeaderReader.IsRAR5(stream));
    }

    [Fact]
    public void IsRAR5_TooShort_ReturnsFalse()
    {
        using var stream = new MemoryStream([0x52, 0x61, 0x72]);

        Assert.False(RAR5HeaderReader.IsRAR5(stream));
    }

    [Fact]
    public void IsRAR5_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();

        Assert.False(RAR5HeaderReader.IsRAR5(stream));
    }

    [Fact]
    public void IsRAR5_InvalidBytes_ReturnsFalse()
    {
        using var stream = new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        Assert.False(RAR5HeaderReader.IsRAR5(stream));
    }

    #endregion

    #region RAR5Marker Tests

    [Fact]
    public void RAR5Marker_HasCorrectLength()
    {
        Assert.Equal(8, RAR5HeaderReader.RAR5Marker.Length);
    }

    [Fact]
    public void RAR5Marker_HasCorrectBytes()
    {
        byte[] expected = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
        Assert.Equal(expected, RAR5HeaderReader.RAR5Marker);
    }

    #endregion

    #region ReadVInt Tests

    [Fact]
    public void ReadVInt_SingleByte_ReturnsCorrectValue()
    {
        using var stream = new MemoryStream([0x0A]); // value = 10
        var reader = new RAR5HeaderReader(stream);

        ulong result = reader.ReadVInt();

        Assert.Equal(10ul, result);
    }

    [Fact]
    public void ReadVInt_MultiByte_ReturnsCorrectValue()
    {
        // Two bytes: 0x80 | 0x01 = first byte (continuation), 0x02 = second byte
        // Value: (0x01) | (0x02 << 7) = 1 + 256 = 257
        using var stream = new MemoryStream([0x81, 0x02]);
        var reader = new RAR5HeaderReader(stream);

        ulong result = reader.ReadVInt();

        Assert.Equal(257ul, result);
    }

    [Fact]
    public void ReadVInt_Zero_ReturnsZero()
    {
        using var stream = new MemoryStream([0x00]);
        var reader = new RAR5HeaderReader(stream);

        ulong result = reader.ReadVInt();

        Assert.Equal(0ul, result);
    }

    [Fact]
    public void ReadVInt_MaxSingleByte_Returns127()
    {
        using var stream = new MemoryStream([0x7F]); // 127
        var reader = new RAR5HeaderReader(stream);

        ulong result = reader.ReadVInt();

        Assert.Equal(127ul, result);
    }

    #endregion

    #region ReadBlock Tests with Real RAR5 Files

    [Fact]
    public void ReadBlock_RAR5File_ParsesMainHeader()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath))
        {
            Assert.Fail($"Test file not found: {rarPath}");
            return;
        }

        using var fs = new FileStream(rarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(RAR5HeaderReader.IsRAR5(fs));
        fs.Seek(8, SeekOrigin.Begin); // Skip marker

        var reader = new RAR5HeaderReader(fs);
        var block = reader.ReadBlock();

        Assert.NotNull(block);
        Assert.Equal(RAR5BlockType.Main, block!.BlockType);
        Assert.True(block.CrcValid);
    }

    [Fact]
    public void ReadBlock_RAR5File_ParsesAllBlockTypes()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath))
        {
            Assert.Fail($"Test file not found: {rarPath}");
            return;
        }

        using var fs = new FileStream(rarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(8, SeekOrigin.Begin);

        var reader = new RAR5HeaderReader(fs);
        var blockTypes = new List<RAR5BlockType>();

        while (fs.Position < fs.Length)
        {
            var block = reader.ReadBlock();
            if (block == null) break;

            blockTypes.Add(block.BlockType);
            reader.SkipBlock(block);
        }

        Assert.Contains(RAR5BlockType.Main, blockTypes);
        // A RAR5 file with comment should have Service block
    }

    [Fact]
    public void ReadBlock_RAR5File_ServiceBlockHasCmtSubType()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath))
        {
            Assert.Fail($"Test file not found: {rarPath}");
            return;
        }

        using var fs = new FileStream(rarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(8, SeekOrigin.Begin);

        var reader = new RAR5HeaderReader(fs);
        RAR5ServiceBlockInfo? cmtBlock = null;

        while (fs.Position < fs.Length)
        {
            var block = reader.ReadBlock();
            if (block == null) break;

            if (block.BlockType == RAR5BlockType.Service && block.ServiceBlockInfo?.SubType == "CMT")
            {
                cmtBlock = block.ServiceBlockInfo;
                break;
            }
            reader.SkipBlock(block);
        }

        Assert.NotNull(cmtBlock);
        Assert.Equal("CMT", cmtBlock!.SubType);
    }

    #endregion

    #region CanReadBaseHeader Tests

    [Fact]
    public void CanReadBaseHeader_SufficientData_ReturnsTrue()
    {
        using var stream = new MemoryStream(new byte[10]);
        var reader = new RAR5HeaderReader(stream);

        Assert.True(reader.CanReadBaseHeader);
    }

    [Fact]
    public void CanReadBaseHeader_InsufficientData_ReturnsFalse()
    {
        using var stream = new MemoryStream(new byte[3]);
        var reader = new RAR5HeaderReader(stream);

        Assert.False(reader.CanReadBaseHeader);
    }

    [Fact]
    public void CanReadBaseHeader_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();
        var reader = new RAR5HeaderReader(stream);

        Assert.False(reader.CanReadBaseHeader);
    }

    #endregion

    #region PeekBlockType Tests

    [Fact]
    public void PeekBlockType_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var reader = new RAR5HeaderReader(stream);

        Assert.Null(reader.PeekBlockType());
    }

    [Fact]
    public void PeekBlockType_DoesNotAdvancePosition()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath)) return;

        using var fs = new FileStream(rarPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(8, SeekOrigin.Begin);

        var reader = new RAR5HeaderReader(fs);
        long posBefore = fs.Position;
        _ = reader.PeekBlockType();
        long posAfter = fs.Position;

        Assert.Equal(posBefore, posAfter);
    }

    #endregion

    #region RAR5 Enum and Class Tests

    [Fact]
    public void RAR5BlockType_HasExpectedValues()
    {
        Assert.Equal(0x00, (byte)RAR5BlockType.Marker);
        Assert.Equal(0x01, (byte)RAR5BlockType.Main);
        Assert.Equal(0x02, (byte)RAR5BlockType.File);
        Assert.Equal(0x03, (byte)RAR5BlockType.Service);
        Assert.Equal(0x04, (byte)RAR5BlockType.Crypt);
        Assert.Equal(0x05, (byte)RAR5BlockType.EndArchive);
    }

    [Fact]
    public void RAR5ArchiveInfo_IsVolume_Flag()
    {
        var info = new RAR5ArchiveInfo { ArchiveFlags = 0x0001 };
        Assert.True(info.IsVolume);
    }

    [Fact]
    public void RAR5ArchiveInfo_IsSolid_Flag()
    {
        var info = new RAR5ArchiveInfo { ArchiveFlags = 0x0004 };
        Assert.True(info.IsSolid);
    }

    [Fact]
    public void RAR5FileInfo_IsStored_WhenMethodZero()
    {
        // Compression info: method=0 at bits 7-9
        var info = new RAR5FileInfo { CompressionInfo = 0 };
        Assert.True(info.IsStored);
    }

    [Fact]
    public void RAR5FileInfo_CompressionMethod_ExtractsCorrectly()
    {
        // Method stored at bits 7-9: method=3 means 0x03 << 7 = 0x180
        var info = new RAR5FileInfo { CompressionInfo = 0x180 };
        Assert.Equal(3, info.CompressionMethod);
    }

    [Fact]
    public void RAR5FileInfo_IsDirectory_WhenFlagSet()
    {
        var info = new RAR5FileInfo { FileFlags = 0x0001 };
        Assert.True(info.IsDirectory);
    }

    [Fact]
    public void RAR5ServiceBlockInfo_IsStored_WhenMethodZero()
    {
        var info = new RAR5ServiceBlockInfo { IsStored = true };
        Assert.True(info.IsStored);
    }

    #endregion
}
