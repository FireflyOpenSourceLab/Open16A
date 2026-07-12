using Open16A.Ld;
using Xunit;

namespace OldSimulator.Tests;

public sealed class LinkerTests
{
    [Fact]
    public void LinksFixedAddressModulesAndFillsGaps()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-linker");
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "first.bin");
        string second = Path.Combine(directory, "second.bin");
        File.WriteAllBytes(first, [0xAA, 0xBB]);
        File.WriteAllBytes(second, [0xCC]);

        LinkResult result = new Linker().Link([new LinkInput(first, 0x0300), new LinkInput(second, 0x0304)]);

        Assert.Equal(0x0300u, result.Origin);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0x00, 0x00, 0xCC }, result.Bytes);
    }

    [Fact]
    public void RejectsOverlappingModules()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-linker");
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "overlap-a.bin");
        string second = Path.Combine(directory, "overlap-b.bin");
        File.WriteAllBytes(first, [0xAA, 0xBB]);
        File.WriteAllBytes(second, [0xCC]);

        Assert.Throws<LinkException>(() => new Linker().Link([new LinkInput(first, 0x0300), new LinkInput(second, 0x0301)]));
    }
}
