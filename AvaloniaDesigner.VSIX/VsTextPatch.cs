using AvaloniaDesigner.Host.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvaloniaDesigner.VSIX;

// DTE absolute offsets collapse CRLF; round-trip offsets are UTF-16 source offsets.
internal static class VsTextPatch
{
    public static (int Line, int Column) Position(string text, int offset)
    {
        if (offset < 0 || offset > text.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    if (i + 1 == offset)
                        throw new InvalidDataException("Patch boundary splits CRLF.");
                    i++;
                }
                line++;
                column = 1;
            }
            else if (text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        return (line, column);
    }

    public static IReadOnlyList<TextEditPayload> Validate(string text, IReadOnlyList<TextEditPayload> edits)
    {
        var ordered = edits.OrderBy(item => item.Start).ToArray();
        var previousEnd = -1;
        var previousStart = -1;
        foreach (var edit in ordered)
        {
            if (edit.Start < 0 || edit.Length < 0 || edit.Start > text.Length - edit.Length
                || edit.Start < previousEnd || edit.Start == previousStart)
                throw new InvalidDataException("Designer patch contains overlapping or out-of-range edits.");
            Position(text, edit.Start);
            Position(text, edit.Start + edit.Length);
            previousStart = edit.Start;
            previousEnd = edit.Start + edit.Length;
        }
        return ordered.Reverse().ToArray();
    }
}
