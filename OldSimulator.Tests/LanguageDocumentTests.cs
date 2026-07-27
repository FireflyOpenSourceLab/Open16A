using Open16A.Lsp;
using Xunit;

namespace OldSimulator.Tests;

public sealed class LanguageDocumentTests
{
    [Fact]
    public void LabelsProvideDefinitionsAndHoverAddresses()
    {
        var document = new LanguageDocument("file:///hello.o16a", ".org 0300h\nstart:\n  LI R0, target\ntarget:\n  HALT\n");

        Assert.Empty(document.Diagnostics);
        Assert.Equal(new TextRange(new TextPosition(3, 0), new TextPosition(3, 6)), document.Definition("target"));
        Assert.Contains("0304h", document.Hover("target"));
    }

    [Fact]
    public void AssemblyErrorsBecomeLineDiagnostics()
    {
        var document = new LanguageDocument("file:///bad.o16a", "LI R8, 0\n");

        DiagnosticInfo diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(0, diagnostic.Range.Start.Line);
        Assert.Contains("R0-R7", diagnostic.Message);
    }

    [Fact]
    public void TokenAndHoverRecognizeInstructionsAndRegisters()
    {
        var document = new LanguageDocument("file:///code.o16a", "OUT 0020h, R0\n");

        Assert.Equal("OUT", document.TokenAt(new TextPosition(0, 1)));
        Assert.Contains("I/O port", document.Hover("OUT"));
        Assert.Contains("general-purpose", document.Hover("R0"));
    }

    [Fact]
    public void EveryInstructionHasSpecificEnglishHoverDocumentation()
    {
        var document = new LanguageDocument("file:///code.o16a", "NOP\n");

        foreach (string mnemonic in LanguageDocument.Mnemonics)
        {
            string documentation = Assert.IsType<string>(document.Hover(mnemonic));
            Assert.Contains(mnemonic, documentation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Open16A instruction.", documentation, StringComparison.Ordinal);
        }
    }
}
