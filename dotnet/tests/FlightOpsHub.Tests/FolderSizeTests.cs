using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class FolderSizeTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_foldersize_{label}_{Guid.NewGuid():N}");

    private static void MakeFile(string path, int sizeBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
    }

    [Fact]
    public void ScanFolderUsage_SumsPerPackageAndTotal()
    {
        var root = TempDir("sums");
        try
        {
            MakeFile(Path.Combine(root, "pkg-a", "one.bgl"), 100);
            MakeFile(Path.Combine(root, "pkg-a", "two.bgl"), 50);
            MakeFile(Path.Combine(root, "pkg-b", "big.bgl"), 1000);

            var result = FolderSize.ScanFolderUsage(root);

            Assert.Equal(1150, result["total_bytes"]!.GetValue<long>());
            var packages = result["packages"]!.AsArray();
            Assert.Equal("pkg-b", packages[0]!["folder_name"]!.GetValue<string>());
            Assert.Equal(1000, packages[0]!["bytes"]!.GetValue<long>());
            Assert.Equal("pkg-a", packages[1]!["folder_name"]!.GetValue<string>());
            Assert.Equal(150, packages[1]!["bytes"]!.GetValue<long>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanFolderUsage_IgnoresLooseFilesAtTopLevel()
    {
        var root = TempDir("loosefiles");
        try
        {
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(root).FullName, "readme.txt"), "not a package");
            MakeFile(Path.Combine(root, "pkg", "file.bgl"), 10);

            var result = FolderSize.ScanFolderUsage(root);
            var packages = result["packages"]!.AsArray();

            Assert.Single(packages);
            Assert.Equal("pkg", packages[0]!["folder_name"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanFolderUsage_SumsNestedSubfolders()
    {
        var root = TempDir("nested");
        try
        {
            MakeFile(Path.Combine(root, "pkg", "top.bgl"), 10);
            MakeFile(Path.Combine(root, "pkg", "nested", "mid.bgl"), 20);
            MakeFile(Path.Combine(root, "pkg", "nested", "deeper", "bottom.bgl"), 30);

            var result = FolderSize.ScanFolderUsage(root);
            var packages = result["packages"]!.AsArray();

            Assert.Single(packages);
            Assert.Equal(60, packages[0]!["bytes"]!.GetValue<long>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanFolderUsage_MissingPathReturnsEmpty()
    {
        var result = FolderSize.ScanFolderUsage(TempDir("missing"));
        Assert.Equal(0, result["total_bytes"]!.GetValue<long>());
        Assert.Empty(result["packages"]!.AsArray());
    }

    [Fact]
    public void ScanFolderUsage_EmptyFolder()
    {
        var root = TempDir("empty");
        Directory.CreateDirectory(root);
        try
        {
            var result = FolderSize.ScanFolderUsage(root);
            Assert.Equal(0, result["total_bytes"]!.GetValue<long>());
            Assert.Empty(result["packages"]!.AsArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
