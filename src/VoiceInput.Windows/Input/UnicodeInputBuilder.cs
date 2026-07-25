namespace VoiceInput.Windows.Input;

public readonly record struct UnicodeKeyStroke(ushort ScanCode, bool IsKeyUp);

public static class UnicodeInputBuilder
{
    public static IReadOnlyList<UnicodeKeyStroke> Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var strokes = new UnicodeKeyStroke[text.Length * 2];
        var index = 0;

        foreach (var codeUnit in text)
        {
            strokes[index++] = new UnicodeKeyStroke(codeUnit, false);
            strokes[index++] = new UnicodeKeyStroke(codeUnit, true);
        }

        return strokes;
    }
}
