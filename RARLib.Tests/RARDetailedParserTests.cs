namespace RARLib.Tests;

public class RARDetailedParserTests
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    #region RAR 4.x Parsing Tests

    [Fact]
    public void Parse_RAR4File_ReturnsBlocks()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath))
        {
            Assert.Fail($"Test file not found: {rarPath}");
            return;
        }

        var blocks = RARDetailedParser.Parse(rarPath);

        Assert.NotEmpty(blocks);

        // First block should be signature
        Assert.Equal("Signature", blocks[0].BlockType);
        Assert.Equal(0, blocks[0].StartOffset);
        Assert.Equal(7, blocks[0].TotalSize);
    }

    [Fact]
    public void Parse_RAR4File_ContainsArchiveHeader()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);

        Assert.Contains(blocks, b => b.BlockType == "Archive Header");
    }

    [Fact]
    public void Parse_RAR4File_ContainsFileHeader()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);

        var fileBlock = blocks.FirstOrDefault(b => b.BlockType == "File Header");
        Assert.NotNull(fileBlock);
        Assert.NotNull(fileBlock!.ItemName);
    }

    [Fact]
    public void Parse_RAR4File_FileHeaderHasExpectedFields()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);
        var fileBlock = blocks.FirstOrDefault(b => b.BlockType == "File Header");
        Assert.NotNull(fileBlock);

        var fieldNames = fileBlock!.Fields.Select(f => f.Name).ToList();
        Assert.Contains("Header CRC", fieldNames);
        Assert.Contains("Block Type", fieldNames);
        Assert.Contains("Flags", fieldNames);
        Assert.Contains("Header Size", fieldNames);
        Assert.Contains("Host OS", fieldNames);
        Assert.Contains("Compression Method", fieldNames);
    }

    [Fact]
    public void Parse_RAR4File_ServiceBlockHasCmtName()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);
        var serviceBlock = blocks.FirstOrDefault(b => b.BlockType == "Service Block");

        if (serviceBlock != null)
        {
            // Service block should have item name "CMT"
            Assert.Equal("CMT", serviceBlock.ItemName);
        }
    }

    #endregion

    #region RAR 5.x Parsing Tests

    [Fact]
    public void Parse_RAR5File_ReturnsBlocks()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath))
        {
            Assert.Fail($"Test file not found: {rarPath}");
            return;
        }

        var blocks = RARDetailedParser.Parse(rarPath);

        Assert.NotEmpty(blocks);
        Assert.Equal("Signature", blocks[0].BlockType);
        Assert.Equal(8, blocks[0].TotalSize); // RAR5 signature is 8 bytes
    }

    [Fact]
    public void Parse_RAR5File_ContainsMainHeader()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);

        Assert.Contains(blocks, b => b.BlockType == "Main Archive Header");
    }

    [Fact]
    public void Parse_RAR5File_ContainsServiceHeader()
    {
        string rarPath = Path.Combine(TestDataPath, "test_rar5_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);

        Assert.Contains(blocks, b => b.BlockType == "Service Header");
    }

    #endregion

    #region Stream-based Parsing Tests

    [Fact]
    public void Parse_Stream_WorksCorrectly()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        using var stream = File.OpenRead(rarPath);
        var blocks = RARDetailedParser.Parse(stream);

        Assert.NotEmpty(blocks);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Parse_RAR4_SignatureFieldHasCorrectDescription()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);
        var sigBlock = blocks.First(b => b.BlockType == "Signature");
        var sigField = sigBlock.Fields.First(f => f.Name == "Signature");

        Assert.Equal("Valid RAR 4.x signature", sigField.Description);
    }

    [Fact]
    public void Parse_HostOSField_HasDescription()
    {
        string rarPath = Path.Combine(TestDataPath, "test_wrar40_m3.rar");
        if (!File.Exists(rarPath)) return;

        var blocks = RARDetailedParser.Parse(rarPath);
        var fileBlock = blocks.First(b => b.BlockType == "File Header");
        var hostOSField = fileBlock.Fields.FirstOrDefault(f => f.Name == "Host OS");

        Assert.NotNull(hostOSField?.Description);
    }

    #endregion
}
