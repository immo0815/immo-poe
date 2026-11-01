using System.Collections.Concurrent;

namespace PoEformance.Overlay;

/// <summary>
/// The agent link's work on the render thread: each piece asked once a frame until it says it is done.
/// </summary>
/// <remarks>
/// THE ONE WAY IN from the agent link's threads to the overlay's state, which belongs to the frame (the books,
/// the panes, the capture key). A piece is a step that answers null while it waits - for a model to build, a
/// picture to be drawn, a capture to end - and its answer once it has one; or the late answer once its time has
/// run out. Pumped at the top of every frame BEFORE the foreground gate, so a step that only sets something runs
/// while another application is in front too; what needs drawing waits for the next frame that draws.
/// </remarks>
internal sealed class AgentFrames
{
    private readonly ConcurrentQueue<Piece> _incoming = new();
    private readonly List<Piece> _running = [];
    private long _pumped;

    /// <summary>When the frames last ran these, as Environment.TickCount64 counts - nought before the first.</summary>
    public long Pumped => Volatile.Read(ref _pumped);

    /// <summary>How many pieces wait.</summary>
    public int Waiting => _incoming.Count + Volatile.Read(ref _count);

    private int _count;

    /// <summary>Runs <paramref name="step"/> once a frame until it answers, or answers with <paramref name="late"/> after <paramref name="within"/>.</summary>
    public Task<T> Until<T>(Func<T?> step, TimeSpan within, Func<T> late)
        where T : class
    {
        var piece = new Piece<T>(step, late, Environment.TickCount64 + (long)within.TotalMilliseconds);
        _incoming.Enqueue(piece);
        return piece.Done.Task;
    }

    /// <summary>Runs <paramref name="step"/> on the next frame and answers what it answers.</summary>
    public Task<T> Next<T>(Func<T> step)
        where T : class
        => Until<T>(step, TimeSpan.FromMinutes(10), static () => throw new TimeoutException("the frame never came"));

    /// <summary>Asks every piece once. Render thread only, once a frame.</summary>
    public void Pump()
    {
        long now = Environment.TickCount64;
        Volatile.Write(ref _pumped, now);
        while (_incoming.TryDequeue(out Piece? piece))
        {
            _running.Add(piece);
        }

        for (int i = _running.Count - 1; i >= 0; i--)
        {
            if (_running[i].Step(now))
            {
                _running.RemoveAt(i);
            }
        }

        Volatile.Write(ref _count, _running.Count);
    }

    private abstract class Piece
    {
        public abstract bool Step(long now);
    }

    private sealed class Piece<T>(Func<T?> step, Func<T> late, long deadline) : Piece
        where T : class
    {
        public TaskCompletionSource<T> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool Step(long now)
        {
            try
            {
                if (step() is { } answer)
                {
                    Done.TrySetResult(answer);
                    return true;
                }

                if (now >= deadline)
                {
                    Done.TrySetResult(late());
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Done.TrySetException(exception);
                return true;
            }
        }
    }
}
