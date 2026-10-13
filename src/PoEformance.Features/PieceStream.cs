namespace PoEformance.Features;

/// <summary>
/// One file written as pieces of at most a given size - <c>name.001</c>, <c>name.002</c> and on - and seekable across them, so
/// a zip is written into it in one pass and comes out already cut; one piece is named as the whole file once finished.
/// </summary>
/// <remarks>
/// ASKED FOR from the live client (2026-10-13): the place a capture is sent to takes no file over 30 MB, and a capture with its
/// pictures runs past that. The pieces are the zip's own bytes in order, so they join back by concatenation (7-Zip opens the
/// <c>.001</c> as the whole; <c>copy /b a.zip.001+a.zip.002 a.zip</c> or <c>cat</c> makes it again) - chosen over several
/// whole zips. SEEKABLE because ZipArchive goes back to write each entry's sizes into its header once the entry is written, and
/// that header may lie in the piece before; written straight into the pieces, the zip is not written whole first and cut after.
/// </remarks>
internal sealed class PieceStream : Stream
{
    private readonly string _path;
    private readonly long _most;
    private readonly List<FileStream> _pieces = [];
    private long _position;
    private long _length;
    private List<string>? _finished;

    /// <param name="path">The whole file's name; the pieces are it with <c>.001</c>, <c>.002</c> and on after it.</param>
    /// <param name="most">The most bytes a piece holds.</param>
    public PieceStream(string path, long most)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);
        _path = path;
        _most = most;
    }

    public override bool CanRead => false;

    public override bool CanSeek => true;

    public override bool CanWrite => _finished is null;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>The name of the piece at an index, from nought.</summary>
    public string PieceName(int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_path}.{index + 1:000}");

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_finished is not null, this);
        while (!buffer.IsEmpty)
        {
            FileStream piece = PieceAt((int)(_position / _most));
            long within = _position % _most;
            int count = (int)Math.Min(buffer.Length, _most - within);
            if (piece.Position != within)
            {
                piece.Position = within;
            }

            piece.Write(buffer[..count]);
            _position += count;
            _length = Math.Max(_length, _position);
            buffer = buffer[count..];
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        int keep = Math.Max(1, (int)((value + _most - 1) / _most));
        while (_pieces.Count > keep)
        {
            FileStream last = _pieces[^1];
            _pieces.RemoveAt(_pieces.Count - 1);
            last.Dispose();
            File.Delete(PieceName(_pieces.Count));
        }

        for (var index = 0; index < keep; index++)
        {
            PieceAt(index).SetLength(Math.Clamp(value - (index * _most), 0, _most));
        }

        _length = value;
    }

    public override void Flush()
    {
        foreach (FileStream piece in _pieces)
        {
            piece.Flush();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Closes the pieces and names them for good: one piece becomes the whole file's name, several keep their numbers. Returns
    /// the files, in order.
    /// </summary>
    public IReadOnlyList<string> Finish()
    {
        if (_finished is not null)
        {
            return _finished;
        }

        PieceAt(0);
        int count = _pieces.Count;
        Close(_pieces);
        _finished = [];
        if (count == 1)
        {
            File.Move(PieceName(0), _path, overwrite: true);
            _finished.Add(_path);
        }
        else
        {
            for (var index = 0; index < count; index++)
            {
                _finished.Add(PieceName(index));
            }
        }

        return _finished;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close(_pieces);
        }

        base.Dispose(disposing);
    }

    private static void Close(List<FileStream> pieces)
    {
        foreach (FileStream piece in pieces)
        {
            piece.Dispose();
        }

        pieces.Clear();
    }

    private FileStream PieceAt(int index)
    {
        while (_pieces.Count <= index)
        {
            _pieces.Add(new FileStream(PieceName(_pieces.Count), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16));
        }

        return _pieces[index];
    }
}
