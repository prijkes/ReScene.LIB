using System.Text;
using RARLib;

namespace SRRLib.Tests;

public class SRRFileTests : IDisposable
{
    private readonly string _testDir;

    public SRRFileTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"srrlib_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    #region Header Block Tests

    [Fact]
    public void Load_SrrHeader_ParsesBlockType()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .BuildToFile(_testDir, "header.srr");

        var srr = SRRFile.Load(path);

        Assert.NotNull(srr.HeaderBlock);
        Assert.Equal(SRRBlockType.Header, srr.HeaderBlock!.BlockType);
    }

    [Fact]
    public void Load_SrrHeaderWithAppName_ParsesAppName()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader("pyReScene 0.7")
            .BuildToFile(_testDir, "header_appname.srr");

        var srr = SRRFile.Load(path);

        Assert.NotNull(srr.HeaderBlock);
        Assert.True(srr.HeaderBlock!.HasAppName);
        Assert.Equal("pyReScene 0.7", srr.HeaderBlock.AppName);
    }

    [Fact]
    public void Load_SrrHeaderWithoutAppName_NoAppName()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .BuildToFile(_testDir, "header_no_appname.srr");

        var srr = SRRFile.Load(path);

        Assert.NotNull(srr.HeaderBlock);
        Assert.False(srr.HeaderBlock!.HasAppName);
        Assert.Null(srr.HeaderBlock.AppName);
    }

    #endregion

    #region Stored File Tests

    [Fact]
    public void Load_StoredFile_ParsesFileName()
    {
        byte[] sfvData = Encoding.UTF8.GetBytes("testfile.rar DEADBEEF\r\n");

        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddStoredFile("release.sfv", sfvData)
            .BuildToFile(_testDir, "stored.srr");

        var srr = SRRFile.Load(path);

        Assert.Single(srr.StoredFiles);
        Assert.Equal("release.sfv", srr.StoredFiles[0].FileName);
        Assert.Equal((uint)sfvData.Length, srr.StoredFiles[0].FileLength);
    }

    [Fact]
    public void Load_MultipleStoredFiles_ParsesAll()
    {
        byte[] sfvData = Encoding.UTF8.GetBytes("test.rar 12345678\r\n");
        byte[] nfoData = Encoding.UTF8.GetBytes("Release NFO content\r\n");

        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddStoredFile("release.sfv", sfvData)
            .AddStoredFile("release.nfo", nfoData)
            .BuildToFile(_testDir, "multi_stored.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(2, srr.StoredFiles.Count);
        Assert.Equal("release.sfv", srr.StoredFiles[0].FileName);
        Assert.Equal("release.nfo", srr.StoredFiles[1].FileName);
    }

    [Fact]
    public void ExtractStoredFile_ExtractsCorrectData()
    {
        byte[] sfvData = Encoding.UTF8.GetBytes("testfile.rar DEADBEEF\r\n");

        string srrPath = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddStoredFile("release.sfv", sfvData)
            .BuildToFile(_testDir, "extract.srr");

        var srr = SRRFile.Load(srrPath);
        string outputDir = Path.Combine(_testDir, "extracted");

        string? extracted = srr.ExtractStoredFile(srrPath, outputDir, name => name.EndsWith(".sfv"));

        Assert.NotNull(extracted);
        Assert.True(File.Exists(extracted));
        byte[] readBack = File.ReadAllBytes(extracted!);
        Assert.Equal(sfvData, readBack);
    }

    [Fact]
    public void ExtractStoredFile_NoMatch_ReturnsNull()
    {
        byte[] data = Encoding.UTF8.GetBytes("test");
        string srrPath = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddStoredFile("release.sfv", data)
            .BuildToFile(_testDir, "nomatch.srr");

        var srr = SRRFile.Load(srrPath);
        string outputDir = Path.Combine(_testDir, "extracted");

        string? extracted = srr.ExtractStoredFile(srrPath, outputDir, name => name.EndsWith(".nfo"));

        Assert.Null(extracted);
    }

    [Fact]
    public void ExtractStoredFile_NullSrrPath_ThrowsArgumentException()
    {
        var srr = new SRRFile();
        Assert.Throws<ArgumentException>(() => srr.ExtractStoredFile("", "output", _ => true));
    }

    [Fact]
    public void ExtractStoredFile_NullOutputDir_ThrowsArgumentException()
    {
        var srr = new SRRFile();
        Assert.Throws<ArgumentException>(() => srr.ExtractStoredFile("file.srr", "", _ => true));
    }

    [Fact]
    public void ExtractStoredFile_NullMatch_ThrowsArgumentNullException()
    {
        var srr = new SRRFile();
        Assert.Throws<ArgumentNullException>(() => srr.ExtractStoredFile("file.srr", "output", null!));
    }

    #endregion

    #region RAR File Reference and Embedded Header Tests

    [Fact]
    public void Load_RarFileBlock_ParsesRarFileName()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("testfile.txt")
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "rarfile.srr");

        var srr = SRRFile.Load(path);

        Assert.Single(srr.RarFiles);
        Assert.Equal("release.rar", srr.RarFiles[0].FileName);
    }

    [Fact]
    public void Load_EmbeddedRarHeaders_ExtractsFileEntries()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("sample.txt", packedSize: 500, unpackedSize: 1024)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "fileentries.srr");

        var srr = SRRFile.Load(path);

        Assert.Contains("sample.txt", srr.ArchivedFiles);
    }

    [Fact]
    public void Load_EmbeddedRarHeaders_ExtractsCompressionMethod()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", method: 0x35) // Best compression
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "method.srr");

        var srr = SRRFile.Load(path);

        // Method is stored as raw 0x30-0x35, decoded to 0-5 by the parser
        Assert.NotNull(srr.CompressionMethod);
        Assert.Equal(5, srr.CompressionMethod); // 0x35 - 0x30 = 5
    }

    [Fact]
    public void Load_EmbeddedRarHeaders_DetectsHostOS()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", hostOS: 3) // Unix
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "hostos.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal((byte)3, srr.DetectedHostOS);
        Assert.Equal("Unix", srr.DetectedHostOSName);
    }

    [Theory]
    [InlineData(0, "MS-DOS")]
    [InlineData(1, "OS/2")]
    [InlineData(2, "Windows")]
    [InlineData(3, "Unix")]
    [InlineData(4, "Mac OS")]
    [InlineData(5, "BeOS")]
    public void Load_HostOS_MapsToCorrectName(byte hostOS, string expectedName)
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", hostOS: hostOS)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, $"hostos_{hostOS}.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(hostOS, srr.DetectedHostOS);
        Assert.Equal(expectedName, srr.DetectedHostOSName);
    }

    [Fact]
    public void Load_EmbeddedRarHeaders_DetectsFileAttributes()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", fileAttributes: 0x000081B4) // Unix mode
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "attrs.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(0x000081B4u, srr.DetectedFileAttributes);
    }

    [Fact]
    public void Load_EmbeddedRarHeaders_DetectsUnpackVersion()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", unpVer: 29)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "unpver.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(29, srr.RARVersion);
    }

    [Fact]
    public void Load_ArchiveHeaderFlags_DetectsVolumeArchive()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.Volume | RARArchiveFlags.NewNumbering | RARArchiveFlags.FirstVolume)
                       .AddFileHeader("file.dat")
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "volume.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.IsVolumeArchive);
        Assert.True(srr.HasNewVolumeNaming);
        Assert.True(srr.HasFirstVolumeFlag);
    }

    [Fact]
    public void Load_ArchiveHeaderFlags_DetectsSolidArchive()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.Solid)
                       .AddFileHeader("file.dat")
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "solid.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.IsSolidArchive);
    }

    [Fact]
    public void Load_ArchiveHeaderFlags_DetectsRecoveryRecord()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.Protected)
                       .AddFileHeader("file.dat")
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "recovery.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.HasRecoveryRecord);
    }

    #endregion

    #region CMT Block Tests

    [Fact]
    public void Load_CmtServiceBlock_ExtractsStoredComment()
    {
        string comment = "Test archive comment.";

        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat")
                       .AddCmtServiceBlock(comment, method: 0x30) // Stored
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_stored.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(comment, srr.ArchiveComment);
        Assert.NotNull(srr.CmtCompressedData);
    }

    [Fact]
    public void Load_CmtServiceBlock_DetectsCmtHostOS()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat", hostOS: 2) // File: Windows
                       .AddCmtServiceBlock("Comment", hostOS: 3) // CMT: Unix
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_hostos.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal((byte)2, srr.DetectedHostOS);   // File header Host OS
        Assert.Equal((byte)3, srr.CmtHostOS);        // CMT Host OS
        Assert.Equal("Unix", srr.CmtHostOSName);
    }

    [Fact]
    public void Load_CmtServiceBlock_DetectsZeroedFileTime()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat")
                       .AddCmtServiceBlock("Comment", fileTimeDOS: 0)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_zeroed_time.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(0u, srr.CmtFileTimeDOS);
        Assert.True(srr.CmtHasZeroedFileTime);
        Assert.Equal("Zeroed (no timestamp)", srr.CmtTimestampMode);
    }

    [Fact]
    public void Load_CmtServiceBlock_DetectsNonZeroFileTime()
    {
        uint dosTime = 0x5A8E3100; // Some non-zero DOS time
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat")
                       .AddCmtServiceBlock("Comment", fileTimeDOS: dosTime)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_has_time.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(dosTime, srr.CmtFileTimeDOS);
        Assert.False(srr.CmtHasZeroedFileTime);
        Assert.Equal("Preserved (has timestamp)", srr.CmtTimestampMode);
    }

    [Fact]
    public void Load_CmtServiceBlock_StoresCompressionMethod()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat")
                       .AddCmtServiceBlock("Comment", method: 0x33) // Normal
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_method.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal((byte)0x33, srr.CmtCompressionMethod);
    }

    [Fact]
    public void Load_CmtServiceBlock_DetectsCmtFileAttributes()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file.dat")
                       .AddCmtServiceBlock("Comment", fileAttributes: 0x000081B4)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "cmt_attrs.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(0x000081B4u, srr.CmtFileAttributes);
    }

    #endregion

    #region OSO Hash Tests

    [Fact]
    public void Load_OsoHashBlock_ParsesCorrectly()
    {
        byte[] osoHash = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddOsoHash("video.avi", 734003200, osoHash)
            .BuildToFile(_testDir, "osohash.srr");

        var srr = SRRFile.Load(path);

        Assert.Single(srr.OsoHashBlocks);
        Assert.Equal("video.avi", srr.OsoHashBlocks[0].FileName);
        Assert.Equal(734003200UL, srr.OsoHashBlocks[0].FileSize);
        Assert.Equal(osoHash, srr.OsoHashBlocks[0].OsoHash);
    }

    #endregion

    #region RAR Padding Tests

    [Fact]
    public void Load_RarPaddingBlock_ParsesCorrectly()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarPadding("release.r00", 512)
            .BuildToFile(_testDir, "padding.srr");

        var srr = SRRFile.Load(path);

        Assert.Single(srr.RarPaddingBlocks);
        Assert.Equal("release.r00", srr.RarPaddingBlocks[0].RarFileName);
        Assert.Equal(512u, srr.RarPaddingBlocks[0].PaddingSize);
    }

    #endregion

    #region Volume Size Detection Tests

    [Fact]
    public void Load_MultipleRarVolumes_CalculatesVolumeSize()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.Volume | RARArchiveFlags.FirstVolume)
                       .AddFileHeader("file.dat", packedSize: 5000)
                       .AddEndArchive();
            })
            .AddRarFileWithHeaders("release.r00", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.Volume)
                       .AddFileHeader("file.dat", packedSize: 5000, extraFlags: RARFileFlags.ExtTime | RARFileFlags.SplitBefore | RARFileFlags.SplitAfter)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "volumes.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(2, srr.RarFiles.Count);
        Assert.Equal(2, srr.RarVolumeSizes.Count);
        Assert.NotNull(srr.VolumeSizeBytes);
    }

    #endregion

    #region Directory Entry Tests

    [Fact]
    public void Load_DirectoryEntry_TracksAsDirectory()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("subdir\\", isDirectory: true, packedSize: 0, unpackedSize: 0)
                       .AddFileHeader("subdir\\file.txt")
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "dirs.srr");

        var srr = SRRFile.Load(path);

        Assert.Contains("subdir", srr.ArchivedDirectories);
        // On Windows, path separator normalizes to backslash
        bool hasFile = srr.ArchivedFiles.Any(f => f.EndsWith("file.txt"));
        Assert.True(hasFile);
    }

    #endregion

    #region Multiple File Entries Tests

    [Fact]
    public void Load_MultipleFiles_TracksCrcs()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("file1.txt", fileCrc: 0xAABBCCDD)
                       .AddFileHeader("file2.txt", fileCrc: 0x11223344)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "multi_files.srr");

        var srr = SRRFile.Load(path);

        Assert.Equal(2, srr.ArchivedFiles.Count);
        Assert.True(srr.ArchivedFileCrcs.ContainsKey("file1.txt"));
        Assert.True(srr.ArchivedFileCrcs.ContainsKey("file2.txt"));
        Assert.Equal("aabbccdd", srr.ArchivedFileCrcs["file1.txt"]);
        Assert.Equal("11223344", srr.ArchivedFileCrcs["file2.txt"]);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Load_EmptyStoredFile_ParsesCorrectly()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddStoredFile("empty.txt", [])
            .BuildToFile(_testDir, "empty_stored.srr");

        var srr = SRRFile.Load(path);

        Assert.Single(srr.StoredFiles);
        Assert.Equal("empty.txt", srr.StoredFiles[0].FileName);
        Assert.Equal(0u, srr.StoredFiles[0].FileLength);
    }

    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => SRRFile.Load("nonexistent.srr"));
    }

    [Fact]
    public void Load_CaseInsensitivePaths_WorkCorrectly()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader()
                       .AddFileHeader("FILE.TXT", fileCrc: 0x12345678)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "case.srr");

        var srr = SRRFile.Load(path);

        // Case-insensitive lookup should work
        Assert.Contains("FILE.TXT", srr.ArchivedFiles);
        Assert.Contains("file.txt", srr.ArchivedFiles);
        Assert.True(srr.ArchivedFileCrcs.ContainsKey("file.txt"));
    }

    [Fact]
    public void DetectedHostOSName_NullHostOS_ReturnsUnknown()
    {
        var srr = new SRRFile();
        // SRRFile is public, new instance has null DetectedHostOS
        Assert.Equal("Unknown", srr.DetectedHostOSName);
    }

    [Fact]
    public void CmtHostOSName_NullCmtHostOS_ReturnsUnknown()
    {
        var srr = new SRRFile();
        Assert.Equal("Unknown", srr.CmtHostOSName);
    }

    [Fact]
    public void CmtTimestampMode_NullCmtFileTime_ReturnsUnknown()
    {
        var srr = new SRRFile();
        Assert.Equal("Unknown", srr.CmtTimestampMode);
    }

    #endregion

    #region Complete SRR Structure Tests

    [Fact]
    public void Load_CompleteSrrFile_ParsesAllBlockTypes()
    {
        byte[] sfvData = Encoding.UTF8.GetBytes("release.rar DEADBEEF\r\n");
        byte[] osoHash = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        string path = new SRRTestDataBuilder()
            .AddSrrHeader("TestApp 1.0")
            .AddStoredFile("release.sfv", sfvData)
            .AddOsoHash("video.avi", 734003200, osoHash)
            .AddRarFileWithHeaders("release.rar", headers =>
            {
                headers.AddArchiveHeader(RARArchiveFlags.FirstVolume)
                       .AddFileHeader("video.avi", hostOS: 2, unpVer: 29, method: 0x33, fileCrc: 0xDEADBEEF)
                       .AddCmtServiceBlock("Release comment", hostOS: 2, method: 0x30)
                       .AddEndArchive();
            })
            .BuildToFile(_testDir, "complete.srr");

        var srr = SRRFile.Load(path);

        // Header
        Assert.NotNull(srr.HeaderBlock);
        Assert.Equal("TestApp 1.0", srr.HeaderBlock!.AppName);

        // Stored files
        Assert.Single(srr.StoredFiles);
        Assert.Equal("release.sfv", srr.StoredFiles[0].FileName);

        // OSO hashes
        Assert.Single(srr.OsoHashBlocks);

        // RAR files
        Assert.Single(srr.RarFiles);
        Assert.Equal("release.rar", srr.RarFiles[0].FileName);

        // Archive metadata
        Assert.Equal(29, srr.RARVersion);
        Assert.Equal((byte)2, srr.DetectedHostOS);
        Assert.Equal("Windows", srr.DetectedHostOSName);

        // Comment
        Assert.Equal("Release comment", srr.ArchiveComment);
        Assert.Equal((byte)0x30, srr.CmtCompressionMethod);

        // Archived files
        Assert.Contains("video.avi", srr.ArchivedFiles);
        Assert.Equal("deadbeef", srr.ArchivedFileCrcs["video.avi"]);
    }

    #endregion

    #region Custom Packer Detection Tests

    [Fact]
    public void Load_NormalFileHeaders_NoCustomPackerDetected()
    {
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeader("video.avi", packedSize: 1024, unpackedSize: 1024);
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "normal.srr");

        var srr = SRRFile.Load(path);

        Assert.False(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.None, srr.CustomPackerDetected);
    }

    [Fact]
    public void Load_AllOnesUnpackedSize_DetectsCustomPacker()
    {
        // Sentinel 1: unpacked_size = 0xFFFFFFFFFFFFFFFF (RELOADED/HI2U/0x0007/0x0815 style)
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeaderWithLargeSize("video.avi",
                    packedSizeLow: 0xFFFFFFFF, packedSizeHigh: 0xFFFFFFFF,
                    unpackedSizeLow: 0xFFFFFFFF, unpackedSizeHigh: 0xFFFFFFFF);
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "reloaded_style.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.AllOnesWithLargeFlag, srr.CustomPackerDetected);
    }

    [Fact]
    public void Load_MaxUint32WithoutLargeFlag_DetectsCustomPacker()
    {
        // Sentinel 2: unpacked_size = 0xFFFFFFFF without LARGE flag (QCF style)
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeader("video.avi",
                    packedSize: 1024,
                    unpackedSize: 0xFFFFFFFF,
                    extraFlags: RARFileFlags.ExtTime); // No LARGE flag
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "qcf_style.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.MaxUint32WithoutLargeFlag, srr.CustomPackerDetected);
    }

    [Fact]
    public void Load_LargeFileWithHighUnpSizeZero_NoFalsePositive()
    {
        // Legitimate large file: UnpackedSize = 0xFFFFFFFF but LARGE flag set with HIGH_UNP = 0
        // This is a valid ~4GB file, NOT a custom packer sentinel
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeaderWithLargeSize("video.avi",
                    packedSizeLow: 0xFFFFFFFF, packedSizeHigh: 0,
                    unpackedSizeLow: 0xFFFFFFFF, unpackedSizeHigh: 0);
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "large_legit.srr");

        var srr = SRRFile.Load(path);

        // Combined UnpackedSize = 0x00000000FFFFFFFF, not 0xFFFFFFFFFFFFFFFF
        Assert.False(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.None, srr.CustomPackerDetected);
    }

    [Fact]
    public void Load_SecondFileHasSentinel_StillDetected()
    {
        // Detection should trigger even if only the second file header has the sentinel
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeader("readme.nfo", packedSize: 100, unpackedSize: 100);
                h.AddFileHeaderWithLargeSize("video.avi",
                    packedSizeLow: 0xFFFFFFFF, packedSizeHigh: 0xFFFFFFFF,
                    unpackedSizeLow: 0xFFFFFFFF, unpackedSizeHigh: 0xFFFFFFFF);
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "second_sentinel.srr");

        var srr = SRRFile.Load(path);

        Assert.True(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.AllOnesWithLargeFlag, srr.CustomPackerDetected);
    }

    [Fact]
    public void Load_DirectoryWithMaxSize_NotDetected()
    {
        // Directory entries should not trigger detection (directories often have size=0 or garbage)
        string path = new SRRTestDataBuilder()
            .AddSrrHeader()
            .AddRarFileWithHeaders("release.rar", h =>
            {
                h.AddArchiveHeader(RARArchiveFlags.Volume);
                h.AddFileHeader("subdir",
                    packedSize: 0,
                    unpackedSize: 0xFFFFFFFF,
                    extraFlags: RARFileFlags.ExtTime,
                    isDirectory: true);
                h.AddFileHeader("subdir\\video.avi", packedSize: 1024, unpackedSize: 1024);
                h.AddEndArchive();
            })
            .BuildToFile(_testDir, "dir_maxsize.srr");

        var srr = SRRFile.Load(path);

        Assert.False(srr.HasCustomPackerHeaders);
        Assert.Equal(CustomPackerType.None, srr.CustomPackerDetected);
    }

    #endregion
}
