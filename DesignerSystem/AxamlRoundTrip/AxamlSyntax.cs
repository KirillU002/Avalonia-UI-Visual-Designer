using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>
/// Непрерывный диапазон исходного AXAML. Диапазоны намеренно работают с offsets,
/// чтобы patch writer мог изменять только пользовательские фрагменты.
/// </summary>
public readonly record struct AxamlTextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool IsEmpty => Length == 0;
}

public sealed record AxamlAttributeSyntax(
    string Name,
    string Value,
    AxamlTextSpan FullSpan,
    AxamlTextSpan ValueSpan,
    char Quote);

public sealed class AxamlElementSyntax
{
    internal AxamlElementSyntax(string name, AxamlTextSpan openingTagSpan, bool isSelfClosing)
    {
        Name = name;
        OpeningTagSpan = openingTagSpan;
        IsSelfClosing = isSelfClosing;
        ContentStart = openingTagSpan.End;
        ElementSpan = isSelfClosing ? openingTagSpan : new AxamlTextSpan(openingTagSpan.Start, 0);
        EndTagSpan = isSelfClosing ? new AxamlTextSpan(openingTagSpan.End, 0) : new AxamlTextSpan(0, 0);
    }

    public string Name { get; }
    public string LocalName => AxamlSyntaxDocument.GetLocalName(Name);
    public AxamlTextSpan OpeningTagSpan { get; }
    public AxamlTextSpan EndTagSpan { get; internal set; }
    public AxamlTextSpan ElementSpan { get; internal set; }
    public int ContentStart { get; }
    public bool IsSelfClosing { get; }
    public AxamlElementSyntax? Parent { get; internal set; }
    public List<AxamlElementSyntax> Children { get; } = new();
    public List<AxamlAttributeSyntax> Attributes { get; } = new();

    public AxamlAttributeSyntax? FindAttribute(string name) =>
        Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? GetAttributeValue(string name) => FindAttribute(name)?.Value;
}

/// <summary>
/// Малый XML tokenizer, сохраняющий исходные spans. Это не replacement для полного
/// XML/AXAML parser: semantic interpretation происходит отдельно в importer.
/// </summary>
public sealed class AxamlSyntaxDocument
{
    private AxamlSyntaxDocument(string text, AxamlElementSyntax root)
    {
        Text = text;
        Root = root;
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        IndentUnit = DetectIndentUnit(text, root);
    }

    public string Text { get; }
    public AxamlElementSyntax Root { get; }
    public string NewLine { get; }
    public string IndentUnit { get; }

    public static AxamlSyntaxDocument Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new AxamlSyntaxException("AXAML document is empty.");

        var stack = new Stack<AxamlElementSyntax>();
        AxamlElementSyntax? root = null;
        var index = 0;

        while (index < text.Length)
        {
            var tagStart = text.IndexOf('<', index);
            if (tagStart < 0)
                break;

            if (StartsWith(text, tagStart, "<!--"))
            {
                index = SkipDelimited(text, tagStart, "-->");
                continue;
            }

            if (StartsWith(text, tagStart, "<![CDATA["))
            {
                index = SkipDelimited(text, tagStart, "]]>");
                continue;
            }

            if (StartsWith(text, tagStart, "<?"))
            {
                index = SkipDelimited(text, tagStart, "?>");
                continue;
            }

            if (StartsWith(text, tagStart, "<!"))
            {
                index = SkipDeclaration(text, tagStart);
                continue;
            }

            if (tagStart + 1 < text.Length && text[tagStart + 1] == '/')
            {
                var endTagEnd = FindTagEnd(text, tagStart);
                var closingNameStart = SkipWhitespace(text, tagStart + 2, endTagEnd);
                var closingNameEnd = ReadNameEnd(text, closingNameStart, endTagEnd);
                var name = text[closingNameStart..closingNameEnd];
                if (stack.Count == 0)
                    throw new AxamlSyntaxException($"Unexpected closing element </{name}>.");

                var openElement = stack.Pop();
                if (!string.Equals(openElement.Name, name, StringComparison.Ordinal))
                    throw new AxamlSyntaxException($"Closing element </{name}> does not match <{openElement.Name}>.");

                openElement.EndTagSpan = new AxamlTextSpan(tagStart, endTagEnd - tagStart + 1);
                openElement.ElementSpan = new AxamlTextSpan(openElement.OpeningTagSpan.Start, endTagEnd - openElement.OpeningTagSpan.Start + 1);
                index = endTagEnd + 1;
                continue;
            }

            var tagEnd = FindTagEnd(text, tagStart);
            var nameStart = SkipWhitespace(text, tagStart + 1, tagEnd);
            var nameEnd = ReadNameEnd(text, nameStart, tagEnd);
            if (nameEnd == nameStart)
                throw new AxamlSyntaxException($"Element name is missing at position {tagStart}.");

            var lastNonWhitespace = tagEnd - 1;
            while (lastNonWhitespace > nameEnd && char.IsWhiteSpace(text[lastNonWhitespace]))
                lastNonWhitespace--;
            var isSelfClosing = text[lastNonWhitespace] == '/';
            var element = new AxamlElementSyntax(
                text[nameStart..nameEnd],
                new AxamlTextSpan(tagStart, tagEnd - tagStart + 1),
                isSelfClosing);
            ParseAttributes(text, element, nameEnd, isSelfClosing ? lastNonWhitespace : tagEnd);

            if (stack.Count > 0)
            {
                element.Parent = stack.Peek();
                stack.Peek().Children.Add(element);
            }
            else if (root is null)
            {
                root = element;
            }
            else
            {
                throw new AxamlSyntaxException("AXAML document contains more than one root element.");
            }

            if (!isSelfClosing)
                stack.Push(element);

            index = tagEnd + 1;
        }

        if (stack.Count > 0)
            throw new AxamlSyntaxException($"Element <{stack.Peek().Name}> is not closed.");
        if (root is null)
            throw new AxamlSyntaxException("AXAML root element was not found.");

        return new AxamlSyntaxDocument(text, root);
    }

    public static string GetLocalName(string qualifiedName)
    {
        var separator = qualifiedName.LastIndexOf(':');
        return separator >= 0 ? qualifiedName[(separator + 1)..] : qualifiedName;
    }

    private static void ParseAttributes(string text, AxamlElementSyntax element, int index, int limit)
    {
        while (index < limit)
        {
            index = SkipWhitespace(text, index, limit);
            if (index >= limit)
                return;

            var attributeStart = index;
            var nameEnd = ReadNameEnd(text, index, limit);
            if (nameEnd == index)
                throw new AxamlSyntaxException($"Attribute name is invalid at position {index}.");

            var name = text[index..nameEnd];
            index = SkipWhitespace(text, nameEnd, limit);
            if (index >= limit || text[index] != '=')
                throw new AxamlSyntaxException($"Attribute '{name}' must have a value.");

            index = SkipWhitespace(text, index + 1, limit);
            if (index >= limit || (text[index] != '\'' && text[index] != '"'))
                throw new AxamlSyntaxException($"Attribute '{name}' must use a quoted value.");

            var quote = text[index];
            var valueStart = index + 1;
            var valueEnd = text.IndexOf(quote, valueStart);
            if (valueEnd < 0 || valueEnd > limit)
                throw new AxamlSyntaxException($"Attribute '{name}' is not closed.");

            var attributeEnd = valueEnd + 1;
            element.Attributes.Add(new AxamlAttributeSyntax(
                name,
                text[valueStart..valueEnd],
                new AxamlTextSpan(attributeStart, attributeEnd - attributeStart),
                new AxamlTextSpan(valueStart, valueEnd - valueStart),
                quote));
            index = attributeEnd;
        }
    }

    private static int FindTagEnd(string text, int tagStart)
    {
        char quote = '\0';
        for (var index = tagStart + 1; index < text.Length; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }

            if (current == '\'' || current == '"')
                quote = current;
            else if (current == '>')
                return index;
        }

        throw new AxamlSyntaxException($"Tag starting at position {tagStart} is not closed.");
    }

    private static int SkipDeclaration(string text, int start)
    {
        var bracketDepth = 0;
        char quote = '\0';
        for (var index = start + 2; index < text.Length; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }

            if (current == '\'' || current == '"')
                quote = current;
            else if (current == '[')
                bracketDepth++;
            else if (current == ']')
                bracketDepth = Math.Max(0, bracketDepth - 1);
            else if (current == '>' && bracketDepth == 0)
                return index + 1;
        }

        throw new AxamlSyntaxException($"Declaration starting at position {start} is not closed.");
    }

    private static int SkipDelimited(string text, int start, string terminator)
    {
        var terminatorIndex = text.IndexOf(terminator, start + 1, StringComparison.Ordinal);
        if (terminatorIndex < 0)
            throw new AxamlSyntaxException($"Comment or processing instruction starting at position {start} is not closed.");
        return terminatorIndex + terminator.Length;
    }

    private static int SkipWhitespace(string text, int index, int limit)
    {
        while (index < limit && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    private static int ReadNameEnd(string text, int index, int limit)
    {
        while (index < limit)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current) || current == '=' || current == '/' || current == '>')
                break;
            index++;
        }

        return index;
    }

    private static bool StartsWith(string text, int index, string value) =>
        index + value.Length <= text.Length && text.AsSpan(index, value.Length).SequenceEqual(value.AsSpan());

    private static string DetectIndentUnit(string text, AxamlElementSyntax root)
    {
        var child = root.Children.FirstOrDefault();
        if (child is null)
            return "    ";

        var childIndent = GetLineIndent(text, child.OpeningTagSpan.Start);
        var rootIndent = GetLineIndent(text, root.OpeningTagSpan.Start);
        if (childIndent.Length > rootIndent.Length && childIndent.StartsWith(rootIndent, StringComparison.Ordinal))
            return childIndent[rootIndent.Length..];
        return childIndent.Contains('\t') ? "\t" : "    ";
    }

    internal static string GetLineIndent(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var index = lineStart;
        while (index < position && (text[index] == ' ' || text[index] == '\t'))
            index++;
        return text[lineStart..index];
    }

    internal static int GetLineStart(string text, int position)
    {
        var lineBreak = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return lineBreak < 0 ? 0 : lineBreak + 1;
    }
}

public sealed class AxamlSyntaxException : Exception
{
    public AxamlSyntaxException(string message) : base(message)
    {
    }
}
