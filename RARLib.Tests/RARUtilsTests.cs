using Force.Crc32;

namespace RARLib.Tests;

public class RARUtilsTests
{
    #region CRC Validation Tests

    [Fact]
    public void CalculateHeaderCrc_ValidHeader_ReturnsCorrectCrc()
    {
        // Build a simple header and verify CRC calculation
        byte[] header = [0x00, 0x00, 0x73, 0x00, 0x08, 0x0D, 0x00]; // Archive header
        uint expectedCrc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        ushort expectedCrc = (ushort)(expectedCrc32 & 0xFFFF);

        ushort calculated = RARUtils.CalculateHeaderCrc(header);

        Assert.Equal(expectedCrc, calculated);
    }

    [Fact]
    public void CalculateHeaderCrc_TooShortHeader_ReturnsZero()
    {
        byte[] header = [0x00, 0x00]; // Only 2 bytes

        ushort calculated = RARUtils.CalculateHeaderCrc(header);

        Assert.Equal(0, calculated);
    }

    [Fact]
    public void ValidateHeaderCrc_ValidCrc_ReturnsTrue()
    {
        byte[] header = [0x00, 0x00, 0x73, 0x00, 0x00, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        ushort correctCrc = RARUtils.CalculateHeaderCrc(header);
        BitConverter.GetBytes(correctCrc).CopyTo(header, 0);

        bool valid = RARUtils.ValidateHeaderCrc(correctCrc, header);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateHeaderCrc_InvalidCrc_ReturnsFalse()
    {
        byte[] header = [0xFF, 0xFF, 0x73, 0x00, 0x00, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        bool valid = RARUtils.ValidateHeaderCrc(0xFFFF, header);

        Assert.False(valid);
    }

    #endregion

    #region DOS Date/Time Conversion Tests

    [Fact]
    public void DosDateToDateTime_Zero_ReturnsNull()
    {
        DateTime? result = RARUtils.DosDateToDateTime(0);

        Assert.Null(result);
    }

    [Fact]
    public void DosDateToDateTime_ValidDate_ReturnsCorrectDateTime()
    {
        // DOS date/time for 2025-01-15 10:30:00
        // Date part (upper 16 bits): year-1980=45 (7 bits), month=1 (4 bits), day=15 (5 bits)
        // Time part (lower 16 bits): hour=10 (5 bits), minute=30 (6 bits), second/2=0 (5 bits)
        uint yearBits = (45u & 0x7F) << 9;
        uint monthBits = (1u & 0x0F) << 5;
        uint dayBits = 15u & 0x1F;
        uint datePart = yearBits | monthBits | dayBits;

        uint hourBits = (10u & 0x1F) << 11;
        uint minuteBits = (30u & 0x3F) << 5;
        uint secondBits = 0u & 0x1F;
        uint timePart = hourBits | minuteBits | secondBits;

        uint dosTime = (datePart << 16) | timePart;
        DateTime? result = RARUtils.DosDateToDateTime(dosTime);

        Assert.NotNull(result);
        Assert.Equal(2025, result!.Value.Year);
        Assert.Equal(1, result.Value.Month);
        Assert.Equal(15, result.Value.Day);
        Assert.Equal(10, result.Value.Hour);
        Assert.Equal(30, result.Value.Minute);
        Assert.Equal(0, result.Value.Second);
    }

    [Fact]
    public void DosDateToDateTime_Year1980_ReturnsCorrectYear()
    {
        // Year = 0 (1980), Month = 1, Day = 1, Hour = 0, Minute = 0, Second = 0
        uint datePart = (0u << 9) | (1u << 5) | 1u;
        uint dosTime = datePart << 16;

        DateTime? result = RARUtils.DosDateToDateTime(dosTime);

        Assert.NotNull(result);
        Assert.Equal(1980, result!.Value.Year);
    }

    [Fact]
    public void DosDateToDateTime_InvalidDate_ReturnsNull()
    {
        // Invalid: month = 0, day = 0
        uint datePart = (45u << 9) | (0u << 5) | 0u;
        uint dosTime = datePart << 16;

        DateTime? result = RARUtils.DosDateToDateTime(dosTime);

        Assert.Null(result);
    }

    [Fact]
    public void DosDateToDateTime_OddSecond_RoundsDown()
    {
        // DOS time encodes seconds/2, so second=15 means 30 seconds
        uint datePart = (45u << 9) | (6u << 5) | 1u; // 2025-06-01
        uint timePart = (12u << 11) | (0u << 5) | 15u; // 12:00:30
        uint dosTime = (datePart << 16) | timePart;

        DateTime? result = RARUtils.DosDateToDateTime(dosTime);

        Assert.NotNull(result);
        Assert.Equal(30, result!.Value.Second); // 15*2 = 30
    }

    #endregion

    #region Filename Decoding Tests

    [Fact]
    public void DecodeFileName_EmptyBytes_ReturnsNull()
    {
        string? result = RARUtils.DecodeFileName([], false);

        Assert.Null(result);
    }

    [Fact]
    public void DecodeFileName_AsciiWithoutUnicode_ReturnsString()
    {
        byte[] nameBytes = "testfile.txt"u8.ToArray();

        string? result = RARUtils.DecodeFileName(nameBytes, false);

        Assert.Equal("testfile.txt", result);
    }

    [Fact]
    public void DecodeFileName_UnicodeFlagNoNullSeparator_DecodesAsUtf8()
    {
        byte[] nameBytes = "test.txt"u8.ToArray();

        string? result = RARUtils.DecodeFileName(nameBytes, true);

        Assert.Equal("test.txt", result);
    }

    [Fact]
    public void DecodeFileName_UnicodeWithNullSeparator_DecodesUnicode()
    {
        // Standard name: "test.txt" followed by null, then Unicode encoding data
        byte[] stdName = "test.txt"u8.ToArray();
        // Simplest Unicode encoding: just the null separator with empty stdName
        byte[] nameBytes = [.. stdName, 0x00]; // null separator, no encoded data

        string? result = RARUtils.DecodeFileName(nameBytes, true);

        // Should fallback to standard name when no encoded data after null
        Assert.NotNull(result);
        Assert.Equal("test.txt", result);
    }

    #endregion

    #region Dictionary Size Tests

    [Theory]
    [InlineData(RARFileFlags.DictSize64, 64)]
    [InlineData(RARFileFlags.DictSize128, 128)]
    [InlineData(RARFileFlags.DictSize256, 256)]
    [InlineData(RARFileFlags.DictSize512, 512)]
    [InlineData(RARFileFlags.DictSize1024, 1024)]
    [InlineData(RARFileFlags.DictSize2048, 2048)]
    [InlineData(RARFileFlags.DictSize4096, 4096)]
    public void GetDictionarySize_ReturnsCorrectSize(RARFileFlags flags, int expectedKB)
    {
        int result = RARUtils.GetDictionarySize(flags);

        Assert.Equal(expectedKB, result);
    }

    [Fact]
    public void GetDictionarySize_DirectoryFlag_ReturnsZero()
    {
        int result = RARUtils.GetDictionarySize(RARFileFlags.Directory);

        Assert.Equal(0, result);
    }

    [Fact]
    public void IsDirectory_DirectoryFlag_ReturnsTrue()
    {
        bool result = RARUtils.IsDirectory(RARFileFlags.Directory);

        Assert.True(result);
    }

    [Fact]
    public void IsDirectory_NormalFile_ReturnsFalse()
    {
        bool result = RARUtils.IsDirectory(RARFileFlags.None);

        Assert.False(result);
    }

    [Fact]
    public void DictionarySizes_HasCorrectLength()
    {
        Assert.Equal(8, RARUtils.DictionarySizes.Length);
    }

    [Fact]
    public void DictionarySizes_ContainsExpectedValues()
    {
        Assert.Equal([64, 128, 256, 512, 1024, 2048, 4096, 0], RARUtils.DictionarySizes);
    }

    #endregion
}
