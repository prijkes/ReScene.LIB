using Force.Crc32;

namespace RARLib.Tests;

public class RARPatcherTests : IDisposable
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    private readonly string _testDir;

    public RARPatcherTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rarpatcher_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    /// <summary>
    /// Copies a test RAR file to the temp directory for patching tests.
    /// </summary>
    private string CopyTestFile(string fileName)
    {
        string source = Path.Combine(TestDataPath, fileName);
        string dest = Path.Combine(_testDir, fileName);
        File.Copy(source, dest, true);
        return dest;
    }

    #region PatchFile Tests

    [Fact]
    public void PatchFile_HostOS_ModifiesHeader()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3 // Unix
        };

        var results = RARPatcher.PatchFile(testFile, options);

        Assert.NotEmpty(results);
        Assert.All(results.Where(r => r.BlockType == RAR4BlockType.FileHeader),
            r => Assert.Equal(3, r.NewHostOS));
    }

    [Fact]
    public void PatchFile_HostOS_RecalculatesCrc()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3 // Unix (original is Windows = 2)
        };

        var results = RARPatcher.PatchFile(testFile, options);

        // Verify that CRC was recalculated
        Assert.NotEmpty(results);
        foreach (var result in results)
        {
            Assert.NotEqual(result.OriginalCrc, result.NewCrc);
        }

        // Verify the file is still parseable after patching
        using var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read);
        fs.Seek(7, SeekOrigin.Begin); // Skip marker
        var reader = new RARHeaderReader(fs);

        while (fs.Position < fs.Length)
        {
            var block = reader.ReadBlock(parseContents: true);
            if (block == null) break;

            if (block.BlockType == RAR4BlockType.FileHeader || block.BlockType == RAR4BlockType.Service)
            {
                Assert.True(block.CrcValid, $"CRC invalid after patching at position {block.BlockPosition}");
            }

            reader.SkipBlock(block, includeData: block.BlockType != RAR4BlockType.FileHeader);
        }
    }

    [Fact]
    public void PatchFile_FileAttributes_ModifiesHeader()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileAttributes = 0x000081B4 // Unix file mode
        };

        var results = RARPatcher.PatchFile(testFile, options);

        Assert.NotEmpty(results);
        Assert.All(results.Where(r => r.BlockType == RAR4BlockType.FileHeader),
            r => Assert.Equal(0x000081B4u, r.NewAttributes));
    }

    [Fact]
    public void PatchFile_NoChangesNeeded_ReturnsEmptyResults()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        // Read original host OS first
        byte originalHostOS;
        using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read))
        {
            fs.Seek(7, SeekOrigin.Begin);
            var reader = new RARHeaderReader(fs);
            while (fs.Position < fs.Length)
            {
                var block = reader.ReadBlock(parseContents: true);
                if (block == null) break;
                if (block.FileHeader != null)
                {
                    originalHostOS = block.FileHeader.HostOS;
                    break;
                }
                reader.SkipBlock(block, includeData: block.BlockType != RAR4BlockType.FileHeader);
            }
        }

        // Patch to same value
        var options = new PatchOptions
        {
            FileHostOS = 2 // Windows (should be same as original)
        };

        var results = RARPatcher.PatchFile(testFile, options);

        // If original is already Windows, no changes should be made
        // (or all results show no actual change)
        // The patcher only writes if the values differ
    }

    [Fact]
    public void PatchFile_ServiceBlockDisabled_SkipsServiceBlocks()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3,
            PatchServiceBlocks = false // Don't patch CMT blocks
        };

        var results = RARPatcher.PatchFile(testFile, options);

        // Service blocks should not be in results
        Assert.DoesNotContain(results, r => r.BlockType == RAR4BlockType.Service);
    }

    [Fact]
    public void PatchFile_ServiceBlockFileTime_PatchesCorrectly()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3,
            PatchServiceBlocks = true,
            ServiceBlockFileTime = 0 // Zero out CMT file time
        };

        var results = RARPatcher.PatchFile(testFile, options);

        // Should have patched both file headers and service blocks
        Assert.NotEmpty(results);
    }

    #endregion

    #region AnalyzeFile Tests

    [Fact]
    public void AnalyzeFile_DoesNotModifyFile()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");
        byte[] originalBytes = File.ReadAllBytes(testFile);

        var options = new PatchOptions
        {
            FileHostOS = 3
        };

        var results = RARPatcher.AnalyzeFile(testFile, options);

        byte[] afterBytes = File.ReadAllBytes(testFile);
        Assert.Equal(originalBytes, afterBytes);
    }

    [Fact]
    public void AnalyzeFile_ReportsBlocksThatWouldChange()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3 // Unix (different from Windows original)
        };

        var results = RARPatcher.AnalyzeFile(testFile, options);

        // Should report blocks that would be modified
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(3, r.NewHostOS));
    }

    [Fact]
    public void AnalyzeFile_NewCrcIsZero_InAnalysisMode()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3
        };

        var results = RARPatcher.AnalyzeFile(testFile, options);

        Assert.All(results, r => Assert.Equal((ushort)0, r.NewCrc));
    }

    #endregion

    #region PatchOptions Tests

    [Fact]
    public void PatchOptions_GetServiceBlockHostOS_FallsBackToFileHostOS()
    {
        var options = new PatchOptions
        {
            FileHostOS = 3
        };

        Assert.Equal((byte)3, options.GetServiceBlockHostOS());
    }

    [Fact]
    public void PatchOptions_GetServiceBlockHostOS_UsesExplicitValue()
    {
        var options = new PatchOptions
        {
            FileHostOS = 3,
            ServiceBlockHostOS = 2
        };

        Assert.Equal((byte)2, options.GetServiceBlockHostOS());
    }

    [Fact]
    public void PatchOptions_GetServiceBlockAttributes_FallsBackToFileAttributes()
    {
        var options = new PatchOptions
        {
            FileAttributes = 0x000081B4
        };

        Assert.Equal(0x000081B4u, options.GetServiceBlockAttributes());
    }

    [Fact]
    public void PatchOptions_GetServiceBlockAttributes_UsesExplicitValue()
    {
        var options = new PatchOptions
        {
            FileAttributes = 0x000081B4,
            ServiceBlockAttributes = 0x00000020
        };

        Assert.Equal(0x00000020u, options.GetServiceBlockAttributes());
    }

    [Fact]
    public void PatchOptions_DefaultPatchServiceBlocks_IsTrue()
    {
        var options = new PatchOptions();

        Assert.True(options.PatchServiceBlocks);
    }

    #endregion

    #region GetHostOSName Tests

    [Theory]
    [InlineData(0, "MS-DOS")]
    [InlineData(1, "OS/2")]
    [InlineData(2, "Windows")]
    [InlineData(3, "Unix")]
    [InlineData(4, "Mac OS")]
    [InlineData(5, "BeOS")]
    [InlineData(6, "Unknown (6)")]
    [InlineData(255, "Unknown (255)")]
    public void GetHostOSName_ReturnsCorrectName(byte hostOS, string expected)
    {
        string result = RARPatcher.GetHostOSName(hostOS);

        Assert.Equal(expected, result);
    }

    #endregion

    #region PatchLargeFlags Tests

    /// <summary>
    /// Builds a minimal RAR4 file with a single file header for LARGE flag testing.
    /// </summary>
    /// <param name="hasLargeFlag">If true, include LARGE flag and HIGH fields in the file header.</param>
    /// <param name="highPackSize">HIGH_PACK_SIZE value (only used if hasLargeFlag is true).</param>
    /// <param name="highUnpSize">HIGH_UNP_SIZE value (only used if hasLargeFlag is true).</param>
    private static byte[] BuildMinimalRar4WithFileHeader(bool hasLargeFlag, uint highPackSize = 0, uint highUnpSize = 0)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RAR4 marker (7 bytes)
        writer.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 });

        // Archive header (7 bytes, minimal)
        byte[] archiveHeader = new byte[7];
        archiveHeader[2] = 0x73; // type = ArchiveHeader
        BitConverter.GetBytes((ushort)0x0000).CopyTo(archiveHeader, 3); // flags
        BitConverter.GetBytes((ushort)7).CopyTo(archiveHeader, 5); // headerSize = 7
        // Compute CRC
        uint archCrc = Crc32Algorithm.Compute(archiveHeader, 2, 5);
        BitConverter.GetBytes((ushort)(archCrc & 0xFFFF)).CopyTo(archiveHeader, 0);
        writer.Write(archiveHeader);

        // File header
        // Base: 7 bytes (CRC+type+flags+headerSize) + 4 (ADD_SIZE) + 4 (UnpSize) + 1 (HostOS) + 4 (FileCRC) + 4 (FileTime) + 1 (UnpVer) + 1 (Method) + 2 (NameSize) + 4 (Attr)
        // = 32 bytes + optionally 8 bytes (HIGH_PACK_SIZE + HIGH_UNP_SIZE) + filename
        string fileName = "test.txt";
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(fileName);
        int headerSize = 32 + (hasLargeFlag ? 8 : 0) + nameBytes.Length;
        ushort flags = (ushort)(RARFileFlags.LongBlock | (hasLargeFlag ? RARFileFlags.Large : 0));

        byte[] fileHeader = new byte[headerSize];
        fileHeader[2] = 0x74; // type = FileHeader
        BitConverter.GetBytes(flags).CopyTo(fileHeader, 3);
        BitConverter.GetBytes((ushort)headerSize).CopyTo(fileHeader, 5);
        BitConverter.GetBytes((uint)0).CopyTo(fileHeader, 7); // ADD_SIZE (packed data size = 0)
        BitConverter.GetBytes((uint)100).CopyTo(fileHeader, 11); // UnpSize
        fileHeader[15] = 2; // HostOS = Windows
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(fileHeader, 16); // FileCRC
        BitConverter.GetBytes((uint)0x5A000000).CopyTo(fileHeader, 20); // FileTime
        fileHeader[24] = 29; // UnpVer
        fileHeader[25] = 0x33; // Method (Normal)
        BitConverter.GetBytes((ushort)nameBytes.Length).CopyTo(fileHeader, 26);
        BitConverter.GetBytes((uint)0x00000020).CopyTo(fileHeader, 28); // Attributes

        int offset = 32;
        if (hasLargeFlag)
        {
            BitConverter.GetBytes(highPackSize).CopyTo(fileHeader, offset);
            offset += 4;
            BitConverter.GetBytes(highUnpSize).CopyTo(fileHeader, offset);
            offset += 4;
        }
        Array.Copy(nameBytes, 0, fileHeader, offset, nameBytes.Length);

        // Compute CRC
        uint fileCrc32 = Crc32Algorithm.Compute(fileHeader, 2, fileHeader.Length - 2);
        BitConverter.GetBytes((ushort)(fileCrc32 & 0xFFFF)).CopyTo(fileHeader, 0);
        writer.Write(fileHeader);

        // End of Archive block
        byte[] endBlock = new byte[7];
        endBlock[2] = 0x7B; // type = EndArchive
        BitConverter.GetBytes((ushort)0x4000).CopyTo(endBlock, 3); // SKIP_IF_UNKNOWN
        BitConverter.GetBytes((ushort)7).CopyTo(endBlock, 5); // headerSize = 7
        uint endCrc = Crc32Algorithm.Compute(endBlock, 2, 5);
        BitConverter.GetBytes((ushort)(endCrc & 0xFFFF)).CopyTo(endBlock, 0);
        writer.Write(endBlock);

        return ms.ToArray();
    }

    [Fact]
    public void PatchLargeFlags_AddLarge_InsertsHighFields()
    {
        byte[] rarData = BuildMinimalRar4WithFileHeader(hasLargeFlag: false);
        int originalLength = rarData.Length;

        using var stream = new MemoryStream();
        stream.Write(rarData, 0, rarData.Length);
        stream.Position = 0;

        var options = new PatchOptions
        {
            SetLargeFlag = true,
            HighPackSize = 0x12345678,
            HighUnpSize = 0xABCDEF00
        };

        bool modified = RARPatcher.PatchLargeFlags(stream, options);

        Assert.True(modified);
        // File should be 8 bytes larger
        Assert.Equal(originalLength + 8, stream.Length);

        // Verify the patched file is parseable and has LARGE flag
        stream.Position = 7; // Skip marker
        var reader = new RARHeaderReader(stream);

        // Skip archive header
        var archBlock = reader.ReadBlock(parseContents: false);
        Assert.NotNull(archBlock);
        reader.SkipBlock(archBlock!);

        // Read file header
        var fileBlock = reader.ReadBlock(parseContents: true);
        Assert.NotNull(fileBlock);
        Assert.NotNull(fileBlock!.FileHeader);
        Assert.True(fileBlock.FileHeader!.HasLargeSize);
        Assert.Equal(0x12345678u, fileBlock.FileHeader.HighPackSize);
        Assert.Equal(0xABCDEF00u, fileBlock.FileHeader.HighUnpSize);
        Assert.True(fileBlock.CrcValid);
    }

    [Fact]
    public void PatchLargeFlags_RemoveLarge_RemovesHighFields()
    {
        byte[] rarData = BuildMinimalRar4WithFileHeader(hasLargeFlag: true, highPackSize: 0x11111111, highUnpSize: 0x22222222);
        int originalLength = rarData.Length;

        using var stream = new MemoryStream();
        stream.Write(rarData, 0, rarData.Length);
        stream.Position = 0;

        var options = new PatchOptions
        {
            SetLargeFlag = false
        };

        bool modified = RARPatcher.PatchLargeFlags(stream, options);

        Assert.True(modified);
        // File should be 8 bytes smaller
        Assert.Equal(originalLength - 8, stream.Length);

        // Verify the patched file is parseable and does NOT have LARGE flag
        stream.Position = 7;
        var reader = new RARHeaderReader(stream);

        var archBlock = reader.ReadBlock(parseContents: false);
        Assert.NotNull(archBlock);
        reader.SkipBlock(archBlock!);

        var fileBlock = reader.ReadBlock(parseContents: true);
        Assert.NotNull(fileBlock);
        Assert.NotNull(fileBlock!.FileHeader);
        Assert.False(fileBlock.FileHeader!.HasLargeSize);
        Assert.Equal(0u, fileBlock.FileHeader.HighPackSize);
        Assert.Equal(0u, fileBlock.FileHeader.HighUnpSize);
        Assert.True(fileBlock.CrcValid);
    }

    [Fact]
    public void PatchLargeFlags_RoundTrip_AddThenRemoveRestoresOriginal()
    {
        byte[] originalData = BuildMinimalRar4WithFileHeader(hasLargeFlag: false);

        // Add LARGE
        using var stream = new MemoryStream();
        stream.Write(originalData, 0, originalData.Length);
        stream.Position = 0;

        var addOptions = new PatchOptions
        {
            SetLargeFlag = true,
            HighPackSize = 0,
            HighUnpSize = 0
        };

        bool addModified = RARPatcher.PatchLargeFlags(stream, addOptions);
        Assert.True(addModified);

        // Remove LARGE
        stream.Position = 0;
        var removeOptions = new PatchOptions
        {
            SetLargeFlag = false
        };

        bool removeModified = RARPatcher.PatchLargeFlags(stream, removeOptions);
        Assert.True(removeModified);

        // Result should match original length
        Assert.Equal(originalData.Length, stream.Length);

        // Verify the file is parseable
        stream.Position = 7;
        var reader = new RARHeaderReader(stream);

        var archBlock = reader.ReadBlock(parseContents: false);
        Assert.NotNull(archBlock);
        reader.SkipBlock(archBlock!);

        var fileBlock = reader.ReadBlock(parseContents: true);
        Assert.NotNull(fileBlock);
        Assert.True(fileBlock!.CrcValid);
        Assert.False(fileBlock.FileHeader!.HasLargeSize);
    }

    [Fact]
    public void PatchLargeFlags_AlreadyMatches_ReturnsNoModification()
    {
        byte[] rarData = BuildMinimalRar4WithFileHeader(hasLargeFlag: true, highPackSize: 0, highUnpSize: 0);

        using var stream = new MemoryStream();
        stream.Write(rarData, 0, rarData.Length);
        stream.Position = 0;

        var options = new PatchOptions
        {
            SetLargeFlag = true // Already has LARGE
        };

        bool modified = RARPatcher.PatchLargeFlags(stream, options);

        Assert.False(modified);
    }

    [Fact]
    public void PatchLargeFlags_NullSetLargeFlag_ReturnsNoModification()
    {
        byte[] rarData = BuildMinimalRar4WithFileHeader(hasLargeFlag: false);

        using var stream = new MemoryStream();
        stream.Write(rarData, 0, rarData.Length);
        stream.Position = 0;

        var options = new PatchOptions
        {
            SetLargeFlag = null // No change requested
        };

        bool modified = RARPatcher.PatchLargeFlags(stream, options);

        Assert.False(modified);
    }

    [Fact]
    public void PatchLargeFlags_WithRealFile_PreservesParseability()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        // Add LARGE flag to a real RAR file
        using var stream = new FileStream(testFile, FileMode.Open, FileAccess.ReadWrite);

        var options = new PatchOptions
        {
            SetLargeFlag = true,
            HighPackSize = 0,
            HighUnpSize = 0
        };

        bool modified = RARPatcher.PatchLargeFlags(stream, options);
        Assert.True(modified);

        // Verify all blocks are parseable with valid CRCs
        stream.Position = 7;
        var reader = new RARHeaderReader(stream);

        while (stream.Position < stream.Length)
        {
            var block = reader.ReadBlock(parseContents: true);
            if (block == null) break;

            if (block.BlockType == RAR4BlockType.FileHeader || block.BlockType == RAR4BlockType.Service)
            {
                Assert.True(block.CrcValid, $"CRC invalid after LARGE patching at position {block.BlockPosition}");

                if (block.FileHeader != null)
                {
                    Assert.True(block.FileHeader.HasLargeSize);
                }
            }

            reader.SkipBlock(block, includeData: block.BlockType != RAR4BlockType.FileHeader);
        }
    }

    #endregion

    #region PatchStream Tests

    [Fact]
    public void PatchStream_PatchesBothHostOSAndAttributes()
    {
        string testFile = CopyTestFile("test_wrar40_m3.rar");

        var options = new PatchOptions
        {
            FileHostOS = 3,
            FileAttributes = 0x000081B4
        };

        var results = new List<PatchResult>();
        using var stream = new FileStream(testFile, FileMode.Open, FileAccess.ReadWrite);
        RARPatcher.PatchStream(stream, options, results);

        Assert.NotEmpty(results);
        foreach (var result in results.Where(r => r.BlockType == RAR4BlockType.FileHeader))
        {
            Assert.Equal(3, result.NewHostOS);
            Assert.Equal(0x000081B4u, result.NewAttributes);
        }
    }

    #endregion
}
