using VoiceInput.Windows.Input;

namespace VoiceInput.Windows.Tests.Input;

public sealed class UnicodeInputBuilderTests
{
    [Fact]
    public void BuilderEmitsKeyDownAndKeyUpForEachUtf16CodeUnit()
    {
        var strokes = UnicodeInputBuilder.Build("Я🙂");

        Assert.Equal(
            [
                new UnicodeKeyStroke(0x042F, false),
                new UnicodeKeyStroke(0x042F, true),
                new UnicodeKeyStroke(0xD83D, false),
                new UnicodeKeyStroke(0xD83D, true),
                new UnicodeKeyStroke(0xDE42, false),
                new UnicodeKeyStroke(0xDE42, true),
            ],
            strokes);
    }
}
