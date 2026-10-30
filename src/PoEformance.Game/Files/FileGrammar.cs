using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PoEformance.Game.Files;

/// <summary>
/// A text file's lines as poe_data_tools cuts them before its grammar runs: split, trimmed, the blank ones and
/// the comments dropped - each format choosing which, as its own parser does.
/// </summary>
/// <remarks>
/// THE ONE PLACE THE SMALL TEXT FORMATS SHARE, so the line grammar of every reader written after poe_data_tools'
/// <c>file_parsers</c> (<c>.amd</c>, <c>.cht</c>, <c>.clt</c>, <c>.dct</c>, <c>.ddt</c>, <c>.epk</c>, <c>.et</c>,
/// <c>.gcf</c>, <c>.gt</c>, <c>.pet</c>, <c>.trl</c>) is written once. A line is kept as where it lies in the text,
/// not copied: a reader asks for its span, and only the values it keeps become strings.
///
/// THE REFERENCE'S GRAMMAR IS STRICT, and so are the readers on this: a winnow parser lifted over a line must
/// consume the whole line (<c>Parser::parse</c>), and the file's parser the whole list of lines, so a line the
/// grammar does not take fails the file. That is what makes a reader's <c>Why</c> a measurement - the parser
/// survey counts files read whole against files that stopped, per version, over the install.
/// </remarks>
public sealed class FileLines
{
    private readonly string _text;
    private readonly List<(int Start, int Length, int Number)> _lines;

    private FileLines(string text, List<(int Start, int Length, int Number)> lines)
    {
        _text = text;
        _lines = lines;
    }

    /// <summary>How many lines were kept.</summary>
    public int Count => _lines.Count;

    /// <summary>A kept line, trimmed as asked.</summary>
    public ReadOnlySpan<char> this[int index] => _text.AsSpan(_lines[index].Start, _lines[index].Length);

    /// <summary>The line's number in the file, from one, for a reader's <c>Why</c>.</summary>
    public int Number(int index) => _lines[index].Number;

    /// <summary>
    /// Cuts the text into lines - at <c>\n</c>, and at tabs too where <paramref name="tabs"/> (the <c>.amd</c>
    /// parser's <c>split_inclusive(['\n', '\t'])</c>) - trims each (only its end where <paramref name="endOnly"/>,
    /// <c>.epk</c>'s <c>trim_end</c>), and drops the empty ones and those starting with <paramref name="comment"/>.
    /// </summary>
    public static FileLines Of(string text, string? comment, bool tabs = false, bool endOnly = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<(int, int, int)>();
        int at = 0;
        int number = 1;
        while (at <= text.Length)
        {
            int end = tabs ? text.AsSpan(at).IndexOfAny('\n', '\t') : text.AsSpan(at).IndexOf('\n');
            end = end < 0 ? text.Length : at + end;

            ReadOnlySpan<char> raw = text.AsSpan(at, end - at);
            ReadOnlySpan<char> line = endOnly ? raw.TrimEnd() : raw.Trim();
            if (!line.IsEmpty && (comment is null || !line.StartsWith(comment, StringComparison.Ordinal)))
            {
                int start = at + (endOnly ? 0 : raw.Length - raw.TrimStart().Length);
                lines.Add((start, line.Length, number));
            }

            if (end < text.Length && text[end] == '\n')
            {
                number++;
            }

            at = end + 1;
        }

        return new FileLines(text, lines);
    }

    /// <summary>A line's number and its first 80 characters, for a reader's <c>Why</c>.</summary>
    public string Said(int index)
    {
        if (index >= Count)
        {
            return "the end of the file";
        }

        ReadOnlySpan<char> line = this[index];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"line {Number(index)} \"{(line.Length > 80 ? string.Concat(line[..80], "...") : line.ToString())}\"");
    }

    /// <summary>
    /// The <c>version N</c> line at <paramref name="at"/> (poe_data_tools' <c>version_line</c>: the word, ONE
    /// space, a whole number, nothing after), stepping past it. Null where the line is not one.
    /// </summary>
    public int? Version(ref int at)
    {
        if (at >= Count)
        {
            return null;
        }

        var words = new LineWords(this[at]);
        if (words.Literal("version ") && words.UInt(out uint version) && words.End)
        {
            at++;
            return (int)Math.Min(version, int.MaxValue);
        }

        return null;
    }

    /// <summary>
    /// The kept lines from <paramref name="from"/> on, joined with nothing between them - the reference's
    /// <c>lines.concat()</c>, which a <c>.pet</c> or <c>.trl</c> hands serde_json as its payload.
    /// </summary>
    public string Joined(int from)
    {
        var joined = new StringBuilder();
        for (int one = from; one < Count; one++)
        {
            _ = joined.Append(this[one]);
        }

        return joined.ToString();
    }

    /// <summary>
    /// Whether the text is one JSON value as serde_json takes one: strict - no comments, no trailing commas, nothing
    /// after the value. Read with <see cref="Utf8JsonReader"/>, which builds nothing.
    /// </summary>
    public static bool IsJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] bytes = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(text.Length));
        try
        {
            int length = Encoding.UTF8.GetBytes(text, bytes);
            var reader = new Utf8JsonReader(bytes.AsSpan(0, length));
            if (!reader.Read())
            {
                return false;
            }

            reader.Skip();
            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}

/// <summary>
/// A cursor over one line, with the pieces poe_data_tools' winnow helpers read (<c>shared/winnow.rs</c>):
/// <c>space1</c>, <c>quoted</c>, <c>unquoted</c>, <c>dec_uint</c>, <c>dec_int</c>, <c>float</c>,
/// <c>parse_bool</c>, <c>safe_u32</c>, <c>nullable_uint</c> and <c>quoted_comma_separated</c>.
/// </summary>
/// <remarks>
/// EACH PIECE READS AS WINNOW'S DOES, NOT AS A WORD: <c>dec_uint</c> takes the digits and leaves what follows
/// them, so <c>1 0.5</c> after a float and an optional whole number is the float, the 0, and a <c>.5</c> the line
/// then fails on - the reference's answer, which a reader splitting on spaces would not give. A failed piece
/// leaves the cursor where it was; a copy of the cursor is a checkpoint, as winnow's <c>opt</c> backtracks.
/// </remarks>
public ref struct LineWords
{
    private ReadOnlySpan<char> _rest;

    /// <summary>A cursor at the start of <paramref name="line"/>.</summary>
    public LineWords(ReadOnlySpan<char> line) => _rest = line;

    /// <summary>Whether the whole line has been read.</summary>
    public readonly bool End => _rest.IsEmpty;

    /// <summary>How many characters are left - a bound on any count the line may still satisfy.</summary>
    public readonly int Left => _rest.Length;

    /// <summary>What is left of the line (winnow's <c>rest</c>), stepping to its end.</summary>
    public ReadOnlySpan<char> Rest()
    {
        ReadOnlySpan<char> rest = _rest;
        _rest = default;
        return rest;
    }

    /// <summary>One or more spaces or tabs (<c>space1</c>).</summary>
    public bool Space()
    {
        int count = 0;
        while (count < _rest.Length && _rest[count] is ' ' or '\t')
        {
            count++;
        }

        _rest = _rest[count..];
        return count > 0;
    }

    /// <summary>Any spaces or tabs, none included (<c>space0</c>).</summary>
    public void Spaces() => _ = Space();

    /// <summary>The text <paramref name="word"/>, exactly.</summary>
    public bool Literal(string word)
    {
        if (!_rest.StartsWith(word, StringComparison.Ordinal))
        {
            return false;
        }

        _rest = _rest[word.Length..];
        return true;
    }

    /// <summary>A quoted string (<c>quoted('"')</c>): up to the next quote, no escapes; the quotes are stepped past.</summary>
    public bool Quoted(out ReadOnlySpan<char> inside)
    {
        inside = default;
        if (_rest.IsEmpty || _rest[0] != '"')
        {
            return false;
        }

        int close = _rest[1..].IndexOf('"');
        if (close < 0)
        {
            return false;
        }

        inside = _rest.Slice(1, close);
        _rest = _rest[(close + 2)..];
        return true;
    }

    /// <summary>A quoted string as a string.</summary>
    public bool Quoted(out string inside)
    {
        bool read = Quoted(out ReadOnlySpan<char> span);
        inside = read ? span.ToString() : string.Empty;
        return read;
    }

    /// <summary>
    /// A quoted path ending in <paramref name="extension"/> (<c>quoted('"').and_then(filename(ext))</c>), with its
    /// slashes forward as every path this project keeps.
    /// </summary>
    public bool QuotedPath(string extension, out string path)
    {
        LineWords mark = this;
        if (Quoted(out ReadOnlySpan<char> inside) && inside.EndsWith(extension, StringComparison.Ordinal))
        {
            path = Forward(inside);
            return true;
        }

        this = mark;
        path = string.Empty;
        return false;
    }

    /// <summary>One or more characters up to whitespace (<c>unquoted</c>).</summary>
    public bool Word(out ReadOnlySpan<char> word)
    {
        int count = 0;
        while (count < _rest.Length && !char.IsWhiteSpace(_rest[count]))
        {
            count++;
        }

        word = _rest[..count];
        _rest = _rest[count..];
        return count > 0;
    }

    /// <summary>A word as a string.</summary>
    public bool Word(out string word)
    {
        bool read = Word(out ReadOnlySpan<char> span);
        word = read ? span.ToString() : string.Empty;
        return read;
    }

    /// <summary>Decimal digits as a whole number (<c>dec_uint</c>), what follows them left; fails past <c>uint.MaxValue</c>.</summary>
    public bool UInt(out uint value)
    {
        int count = 0;
        while (count < _rest.Length && char.IsAsciiDigit(_rest[count]))
        {
            count++;
        }

        if (count == 0 || !uint.TryParse(_rest[..count], NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        _rest = _rest[count..];
        return true;
    }

    /// <summary>A whole number that is not the front of a float (<c>safe_u32</c>): no <c>.</c> after its digits.</summary>
    public bool SafeUInt(out uint value)
    {
        LineWords mark = this;
        if (UInt(out value) && (_rest.IsEmpty || _rest[0] != '.'))
        {
            return true;
        }

        this = mark;
        value = 0;
        return false;
    }

    /// <summary>A signed whole number (<c>dec_int</c>): an optional sign, then digits.</summary>
    public bool Int(out int value)
    {
        int count = _rest.Length > 0 && _rest[0] is '+' or '-' ? 1 : 0;
        int digits = count;
        while (digits < _rest.Length && char.IsAsciiDigit(_rest[digits]))
        {
            digits++;
        }

        if (digits == count || !int.TryParse(_rest[..digits], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        _rest = _rest[digits..];
        return true;
    }

    /// <summary>
    /// -1 or a whole number from nought (<c>nullable_uint</c>): null for -1. The reference stops on anything below
    /// -1 (<c>unreachable!</c>); this fails the piece.
    /// </summary>
    public bool NullableUInt(out uint? value)
    {
        LineWords mark = this;
        if (Int(out int read) && read >= -1)
        {
            value = read < 0 ? null : (uint)read;
            return true;
        }

        this = mark;
        value = null;
        return false;
    }

    /// <summary>A nought or a one as a bool (<c>parse_bool</c>).</summary>
    public bool Bool(out bool value)
    {
        LineWords mark = this;
        if (UInt(out uint read) && read <= 1)
        {
            value = read == 1;
            return true;
        }

        this = mark;
        value = false;
        return false;
    }

    /// <summary>
    /// A float as winnow's <c>float</c> recognises one: an optional sign, digits with an optional point and more
    /// digits or a point and digits, an optional exponent - or <c>inf</c>, <c>infinity</c> or <c>nan</c>, in any case.
    /// </summary>
    public bool Float(out float value)
    {
        value = 0;
        int at = _rest.Length > 0 && _rest[0] is '+' or '-' ? 1 : 0;

        ReadOnlySpan<char> word = _rest[at..];
        foreach (string named in (ReadOnlySpan<string>)["infinity", "inf", "nan"])
        {
            if (word.StartsWith(named, StringComparison.OrdinalIgnoreCase))
            {
                value = named[0] == 'n' ? float.NaN : _rest[0] == '-' ? float.NegativeInfinity : float.PositiveInfinity;
                _rest = _rest[(at + named.Length)..];
                return true;
            }
        }

        int digits = Digits(at);
        int end = at + digits;
        if (digits > 0)
        {
            if (end < _rest.Length && _rest[end] == '.')
            {
                end += 1 + Digits(end + 1);
            }
        }
        else if (end < _rest.Length && _rest[end] == '.' && Digits(end + 1) > 0)
        {
            end += 1 + Digits(end + 1);
        }
        else
        {
            return false;
        }

        if (end < _rest.Length && _rest[end] is 'e' or 'E')
        {
            int exponent = end + 1 + (end + 1 < _rest.Length && _rest[end + 1] is '+' or '-' ? 1 : 0);
            int places = Digits(exponent);
            if (places == 0)
            {
                // CUT_ERR IN THE REFERENCE: an exponent with no digits fails the float outright.
                return false;
            }

            end = exponent + places;
        }

        if (!float.TryParse(_rest[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        _rest = _rest[end..];
        return true;
    }

    /// <summary>
    /// A quoted list of names (<c>quoted_comma_separated</c>): inside the quotes one or more names, each at least
    /// one character and no comma, joined by exactly <c>", "</c>.
    /// </summary>
    public bool QuotedList(out List<string> names)
    {
        names = [];
        LineWords mark = this;
        if (!Quoted(out ReadOnlySpan<char> inside))
        {
            return false;
        }

        while (true)
        {
            int comma = inside.IndexOf(',');
            ReadOnlySpan<char> name = comma < 0 ? inside : inside[..comma];
            if (name.IsEmpty)
            {
                this = mark;
                return false;
            }

            names.Add(name.ToString());
            if (comma < 0)
            {
                return true;
            }

            inside = inside[(comma + 1)..];
            if (inside.IsEmpty || inside[0] != ' ')
            {
                this = mark;
                return false;
            }

            inside = inside[1..];
        }
    }

    /// <summary>A path as this project keeps them: slashes forward.</summary>
    public static string Forward(ReadOnlySpan<char> path)
    {
        string text = path.ToString();
        return text.Contains('\\', StringComparison.Ordinal) ? text.Replace('\\', '/') : text;
    }

    private readonly int Digits(int from)
    {
        int count = 0;
        while (from + count < _rest.Length && char.IsAsciiDigit(_rest[from + count]))
        {
            count++;
        }

        return count;
    }
}
