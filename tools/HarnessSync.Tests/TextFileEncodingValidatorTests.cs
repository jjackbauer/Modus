using System.Text;
using HarnessSync;

namespace HarnessSync.Tests;

public sealed class TextFileEncodingValidatorTests
{
    [Fact]
    public void LooksLikeUtf16_GivenUtf16LeWithoutBom_ExpectedTrue()
    {
        using var temp = new TempTextFile(Encoding.Unicode.GetBytes("namespace HarnessSync;"));

        Assert.True(TextFileEncodingValidator.LooksLikeUtf16(temp.Path));
    }

    [Fact]
    public void LooksLikeUtf16_GivenUtf8WithoutBom_ExpectedFalse()
    {
        using var temp = new TempTextFile(Encoding.UTF8.GetBytes("namespace HarnessSync;"));

        Assert.False(TextFileEncodingValidator.LooksLikeUtf16(temp.Path));
    }

    [Fact]
    public void FindUtf16TextFiles_GivenRepositoryWithUtf16Source_ExpectedPathReported()
    {
        using var fixture = new HarnessSyncFixture();
        var utf16Path = Path.Combine(fixture.Root, "broken.cs");
        File.WriteAllBytes(utf16Path, Encoding.Unicode.GetBytes("public sealed class Broken { }"));

        var issues = TextFileEncodingValidator.FindUtf16TextFiles(fixture.Root);

        Assert.Contains("broken.cs", issues, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindUtf16TextFiles_GivenUtf16InsideTmpScratch_ExpectedNotReported()
    {
        using var fixture = new HarnessSyncFixture();
        var tmpDirectory = Path.Combine(fixture.Root, ".tmp");
        Directory.CreateDirectory(tmpDirectory);
        File.WriteAllBytes(
            Path.Combine(tmpDirectory, "scratch.txt"),
            Encoding.Unicode.GetBytes("scratch output is not a repository artifact"));

        var issues = TextFileEncodingValidator.FindUtf16TextFiles(fixture.Root);

        Assert.DoesNotContain(issues, path => path.StartsWith(".tmp/", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TempTextFile : IDisposable
    {
        public string Path { get; }

        public TempTextFile(byte[] bytes)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "encoding-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(Path, bytes);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
