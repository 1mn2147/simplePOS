using Pos.Core;

namespace Pos.Tests;

public sealed class CoreInputTests
{
    [Fact]
    public void BarcodeBuffer_CompletesRapidInputAtConfiguredTerminator()
    {
        var buffer = new BarcodeInputBuffer(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500));
        var timestamp = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

        foreach (var character in "880123")
        {
            var result = buffer.PushCharacter(character, timestamp);
            Assert.Equal(BarcodeInputStatus.Buffering, result.Status);
            timestamp = timestamp.AddMilliseconds(20);
        }

        var completed = buffer.PushKey("Enter", timestamp);
        Assert.Equal(BarcodeInputStatus.Completed, completed.Status);
        Assert.Equal("880123", completed.Barcode);
        Assert.Empty(buffer.BufferedText);
    }

    [Fact]
    public void BarcodeBuffer_RejectsSlowTypingAndIgnoresTextEntry()
    {
        var buffer = new BarcodeInputBuffer(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500));
        var timestamp = DateTimeOffset.UtcNow;

        buffer.PushCharacter('1', timestamp);
        buffer.PushCharacter('2', timestamp.AddMilliseconds(100));
        buffer.PushCharacter('3', timestamp.AddMilliseconds(200));
        var rejected = buffer.PushKey("Enter", timestamp.AddMilliseconds(220));
        Assert.Equal(BarcodeInputStatus.Rejected, rejected.Status);

        buffer.PushCharacter('8', timestamp.AddSeconds(1));
        var ignored = buffer.PushCharacter('8', timestamp.AddSeconds(1.01), textInputActive: true);
        Assert.Equal(BarcodeInputStatus.Ignored, ignored.Status);
        Assert.Empty(buffer.BufferedText);
    }

    [Theory]
    [InlineData("gksrmf", "한글")]
    [InlineData("dkssud", "안녕")]
    [InlineData("rhk", "과")]
    public void KoreanComposer_ComposesTwoBeolsikSequences(string keys, string expected)
    {
        var composer = new KoreanInputComposer();

        foreach (var key in keys)
        {
            composer.Input(key);
        }

        Assert.Equal(expected, composer.Text);
    }

    [Fact]
    public void KoreanComposer_HandlesShiftedConsonantsAndCompoundFinalSplitting()
    {
        var shifted = new KoreanInputComposer();
        shifted.Input('r', shift: true);
        shifted.Input('k');
        Assert.Equal("까", shifted.Text);

        var compound = new KoreanInputComposer();
        foreach (var key in "rkqtk")
        {
            compound.Input(key);
        }

        Assert.Equal("갑사", compound.Text);
    }

    [Fact]
    public void KoreanComposer_BackspaceDecomposesCompoundSyllable()
    {
        var composer = new KoreanInputComposer();
        foreach (var key in "rkqt")
        {
            composer.Input(key);
        }

        Assert.Equal("값", composer.Text);
        Assert.Equal("갑", composer.Backspace());
        Assert.Equal("가", composer.Backspace());
        Assert.Equal("ㄱ", composer.Backspace());
        Assert.Equal(string.Empty, composer.Backspace());
    }

    [Fact]
    public void KoreanComposer_SupportsDirectJamoSpaceAndCommittedText()
    {
        var composer = new KoreanInputComposer("POS ");
        composer.InputJamo('ㅎ');
        composer.InputJamo('ㅏ');
        composer.InputJamo('ㄴ');
        composer.Space();
        composer.InputJamo('ㄱ');
        composer.InputJamo('ㅡ');
        composer.InputJamo('ㄹ');
        composer.Commit();

        Assert.Equal("POS 한 글", composer.Text);
    }
}
