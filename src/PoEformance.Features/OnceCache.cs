using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace PoEformance.Features;

/// <summary>
/// A value per path, made once however many threads ask for it at once: the first to ask makes it, the others wait for that one.
/// </summary>
/// <remarks>
/// WHY NOT A PLAIN CONCURRENT DICTIONARY: its GetOrAdd may run the maker twice when two threads ask together, and keeps one -
/// so a texture would be decoded twice and two shapes of one sheet would wear two copies, which the renderer then uploads twice
/// (the reference is what says "the same texture"). The slot is what makes the second asker wait rather than make its own.
/// A slot of its own rather than Lazy, whose constructor the trimmer wants annotated for every value kept.
///
/// AN EXCEPTION IS KEPT LIKE A VALUE: a maker that threw throws again for the next asker. The caches here live for one build,
/// and a build that met an exception is answered with it.
/// </remarks>
/// <typeparam name="T">What is kept.</typeparam>
internal sealed class OnceCache<T>
{
    private readonly ConcurrentDictionary<string, Slot> _made = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The value for <paramref name="key"/>, made by <paramref name="make"/> if nobody has made it yet.</summary>
    public T Get(string key, Func<string, T> make)
    {
        if (!_made.TryGetValue(key, out Slot? slot))
        {
            slot = _made.GetOrAdd(key, static (one, maker) => new Slot(one, maker), make);
        }

        return slot.Value;
    }

    /// <summary>Keeps <paramref name="value"/> for <paramref name="key"/>, in place of whatever was kept.</summary>
    public void Set(string key, T value) => _made[key] = new Slot(value);

    /// <summary>Lets go of what is kept for <paramref name="key"/>.</summary>
    public void Forget(string key) => _made.TryRemove(key, out _);

    /// <summary>One key's value, made on the first asking.</summary>
    private sealed class Slot
    {
        private readonly Lock _gate = new();
        private string? _key;
        private Func<string, T>? _make;
        private T _value = default!;
        private ExceptionDispatchInfo? _failed;
        private volatile bool _done;

        public Slot(string key, Func<string, T> make)
        {
            _key = key;
            _make = make;
        }

        public Slot(T value)
        {
            _value = value;
            _done = true;
        }

        public T Value
        {
            get
            {
                if (!_done)
                {
                    lock (_gate)
                    {
                        if (!_done)
                        {
                            try
                            {
                                _value = _make!(_key!);
                            }
                            catch (Exception exception)
                            {
                                _failed = ExceptionDispatchInfo.Capture(exception);
                            }

                            // LET GO OF THE MAKER, which holds the build's reader and everything it closed over.
                            _make = null;
                            _key = null;
                            _done = true;
                        }
                    }
                }

                _failed?.Throw();
                return _value;
            }
        }
    }
}
