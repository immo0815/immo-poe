using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace PoEformance.Gpu;

/// <summary>
/// What the card holds of the particles it runs, read back for the capture's model-panes part: every emitter's record and update globals as
/// last written, and its pool, order and culling buffers as the update left them - each pool member by the engine's own ParticleData layout.
/// </summary>
/// <remarks>
/// BUILT BEFORE THE FIRST LIVE LOOK AT THE LAMP (CLAUDE.md, "a question the capture cannot answer is a new part, built BEFORE the question is
/// asked"): where the picture shows no flame, a picture alone cannot tell an update that spawned nothing from particles placed out of view or a
/// pass that drew them transparent - the pool's numbers can. PRINT BOTH HALVES: what was handed to the card beside what it made of it.
/// </remarks>
public sealed partial class ModelGpu
{
    /// <summary>Particles of each pool written out member by member - the rest summed per member.</summary>
    private const int MostStateParticles = 4;

    /// <summary>
    /// Every emitter the card holds, read back - its record and update globals as last written, its pool member by member with each member's
    /// range over the pool, its order and culling buffers' first words. Run on the render thread; empty where the card runs no particles.
    /// </summary>
    public IReadOnlyList<string> ParticleState()
    {
        var lines = new List<string>();
        if (_particleCards.Count == 0)
        {
            return lines;
        }

        lines.Add(ParticlesSaid.Length > 0 ? ParticlesSaid : "particles: none drawn in the last picture");
        foreach (ParticleCard card in _particleCards.Values)
        {
            try
            {
                card.State(_device, _context, lines);
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                lines.Add("  the card's buffers would not read back: " + exception.Message);
            }
        }

        return lines;
    }

    /// <summary>One member of the pool's element: its name, where it starts, its scalar kind and how many of them.</summary>
    private readonly record struct PoolMember(string Name, int Offset, ShaderVariableType Kind, int Components);

    private sealed partial class ParticleCard
    {
        /// <summary>The record its last step handed the card.</summary>
        public EmitterRecord LastRecord { get; set; }

        /// <summary>How many steps it has run.</summary>
        public long Steps { get; set; }

        /// <summary>Its account, read back: see <see cref="ParticleState"/>.</summary>
        public void State(ID3D11Device device, ID3D11DeviceContext context, List<string> lines)
        {
            ParticleProgram program = _program;
            lines.Add(Say($"emitter {program.Name}: pool {program.Count}, group {program.GroupSize}, {program.Passes.Count} passes ({string.Join(", ", program.Passes.Select(one => one.Blend))}), mesh {program.Mesh.Source}, {Steps} steps run"));
            if (!Landing())
            {
                lines.Add("  still compiling");
                return;
            }

            if (!_made)
            {
                lines.Add("  not made: " + (_why.Length > 0 ? _why : "not asked yet"));
                return;
            }

            EmitterRecord record = LastRecord;
            lines.Add(Say($"  record: translation {At(record.Translation)}, last {At(record.LastTranslation)}, rotation {At(record.Rotation)}, time {record.Time:0.####} (prev {record.PrevTime:0.####}), duration {record.EmitterDuration:0.###}, bones {record.Bones >> 8} + {record.Bones & 0xFF}, lock flags 0x{record.LockFlags:X}, culling tag 0x{record.CullingTag:X8} (flags 0x{record.CullingTag >> 14:X}, condition {(record.CullingTag >> 11) & 7}, group {((record.CullingTag >> 5) & 63) + 1}), start {record.ParticlesStart}"));
            if (Update is { } update)
            {
                lines.Add("  update globals: " + Globals(update));
            }

            for (var p = 0; p < Passes.Count; p++)
            {
                lines.Add(Say($"  pass {p} vertex globals: ") + Globals(Passes[p].Vertex));
            }

            List<PoolMember> layout = Update?.PoolLayout is { Count: > 0 } own ? own : Passes.Select(one => one.Vertex.PoolLayout).FirstOrDefault(one => one.Count > 0) ?? [];
            if (_pool is not null)
            {
                byte[] pool = Back(device, context, _pool);
                int stride = program.Count > 0 ? pool.Length / program.Count : pool.Length;
                Pool(lines, pool, stride, program.Count, layout);
            }

            if (_index is not null)
            {
                lines.Add("  order (particles_index_data): " + Words(Back(device, context, _index), 32));
            }

            if (_meta is not null)
            {
                lines.Add("  culling meta: " + Words(Back(device, context, _meta), 16));
            }
        }

        /// <summary>
        /// The pool written out: each member's range over every particle - floats as floats, a uint also as the two halves it packs - and the
        /// first particles member by member; the words alone where the reflection described no element.
        /// </summary>
        private static void Pool(List<string> lines, byte[] pool, int stride, int count, List<PoolMember> layout)
        {
            lines.Add(Say($"  pool: {count} particles of {stride} bytes, {pool.Length} read back; {Living(pool, stride, count)} not all nought"));
            if (layout.Count == 0)
            {
                for (var at = 0; at < Math.Min(count, MostStateParticles); at++)
                {
                    lines.Add(Say($"    particle {at}: ") + Words(pool.AsSpan(at * stride, stride).ToArray(), stride / 4));
                }

                return;
            }

            foreach (PoolMember member in layout)
            {
                lines.Add("    " + member.Name + " (" + member.Kind.ToString().ToLowerInvariant() + (member.Components > 1 ? member.Components.ToString(CultureInfo.InvariantCulture) : string.Empty)
                    + Say($", +{member.Offset}): ") + Range(pool, stride, count, member));
            }

            for (var at = 0; at < Math.Min(count, MostStateParticles); at++)
            {
                var one = new List<string>(layout.Count);
                foreach (PoolMember member in layout)
                {
                    var values = new string[member.Components];
                    for (var c = 0; c < member.Components; c++)
                    {
                        values[c] = Value(pool, (at * stride) + member.Offset + (c * 4), member.Kind);
                    }

                    one.Add(member.Name + " " + string.Join(" ", values));
                }

                lines.Add(Say($"    particle {at}: ") + string.Join(", ", one));
            }
        }

        /// <summary>How many particles hold any word that is not nought.</summary>
        private static int Living(byte[] pool, int stride, int count)
        {
            var living = 0;
            for (var at = 0; at < count && ((at + 1) * stride) <= pool.Length; at++)
            {
                if (pool.AsSpan(at * stride, stride).IndexOfAnyExcept((byte)0) >= 0)
                {
                    living++;
                }
            }

            return living;
        }

        /// <summary>A member's range over the pool, component by component.</summary>
        private static string Range(byte[] pool, int stride, int count, PoolMember member)
        {
            var parts = new string[member.Components];
            for (var c = 0; c < member.Components; c++)
            {
                double least = double.MaxValue, most = double.MinValue, sum = 0;
                float lowLeast = float.MaxValue, lowMost = float.MinValue, highLeast = float.MaxValue, highMost = float.MinValue;
                var read = 0;
                var nought = 0;
                for (var at = 0; at < count; at++)
                {
                    int offset = (at * stride) + member.Offset + (c * 4);
                    if (offset + 4 > pool.Length)
                    {
                        break;
                    }

                    uint word = BitConverter.ToUInt32(pool, offset);
                    nought += word == 0 ? 1 : 0;
                    double value = member.Kind switch
                    {
                        ShaderVariableType.Float => BitConverter.UInt32BitsToSingle(word),
                        ShaderVariableType.Int => (int)word,
                        _ => word,
                    };
                    if (double.IsFinite(value))
                    {
                        least = Math.Min(least, value);
                        most = Math.Max(most, value);
                        sum += value;
                        read++;
                    }

                    if (member.Kind != ShaderVariableType.Float)
                    {
                        float low = (float)BitConverter.UInt16BitsToHalf((ushort)word), high = (float)BitConverter.UInt16BitsToHalf((ushort)(word >> 16));
                        if (float.IsFinite(low))
                        {
                            lowLeast = Math.Min(lowLeast, low);
                            lowMost = Math.Max(lowMost, low);
                        }

                        if (float.IsFinite(high))
                        {
                            highLeast = Math.Min(highLeast, high);
                            highMost = Math.Max(highMost, high);
                        }
                    }
                }

                string halves = member.Kind == ShaderVariableType.Float || lowLeast > lowMost
                    ? string.Empty
                    : Say($" (halves {lowLeast:0.####}..{lowMost:0.####} | {(highLeast > highMost ? "-" : Say($"{highLeast:0.####}..{highMost:0.####}"))})");
                parts[c] = read == 0 ? "none finite" : Say($"{least:0.####}..{most:0.####} mean {sum / read:0.####}, {nought} nought") + halves;
            }

            return string.Join("; ", parts);
        }

        /// <summary>One word as its kind reads it - a uint also as its two halves.</summary>
        private static string Value(byte[] pool, int offset, ShaderVariableType kind)
        {
            if (offset + 4 > pool.Length)
            {
                return "-";
            }

            uint word = BitConverter.ToUInt32(pool, offset);
            return kind switch
            {
                ShaderVariableType.Float => Say($"{BitConverter.UInt32BitsToSingle(word):0.####}"),
                ShaderVariableType.Int => Say($"{(int)word}"),
                _ => Say($"{word}[{(float)BitConverter.UInt16BitsToHalf((ushort)word):0.###}|{(float)BitConverter.UInt16BitsToHalf((ushort)(word >> 16)):0.###}]"),
            };
        }

        /// <summary>A shader's $Globals as last written, every variable by name in the order it lies: its words as floats, a word that is no plain float also as a uint.</summary>
        private static string Globals(Shader shader)
        {
            byte[] bytes = shader.GlobalsHeld;
            var said = new List<string>(shader.Globals.Count);
            foreach ((string name, (int offset, int size)) in shader.Globals.OrderBy(one => one.Value.Offset))
            {
                int words = Math.Min(size / 4, 16);
                var values = new string[words];
                for (var w = 0; w < words; w++)
                {
                    int at = offset + (w * 4);
                    if (at + 4 > bytes.Length)
                    {
                        values[w] = "-";
                        continue;
                    }

                    uint word = BitConverter.ToUInt32(bytes, at);
                    float value = BitConverter.UInt32BitsToSingle(word);
                    values[w] = word != 0 && (float.IsSubnormal(value) || !float.IsFinite(value)) ? Say($"u{word}") : Say($"{value:0.####}");
                }

                said.Add(name + " " + string.Join(" ", values));
            }

            return said.Count == 0 ? "none" : string.Join(", ", said);
        }

        /// <summary>A buffer's first words.</summary>
        private static string Words(byte[] bytes, int most)
        {
            int words = Math.Min(bytes.Length / 4, most);
            var said = new string[words];
            for (var w = 0; w < words; w++)
            {
                said[w] = BitConverter.ToUInt32(bytes, w * 4).ToString(CultureInfo.InvariantCulture);
            }

            int nonzero = 0;
            for (var w = 0; w + 4 <= bytes.Length; w += 4)
            {
                nonzero += BitConverter.ToUInt32(bytes, w) != 0 ? 1 : 0;
            }

            return Say($"{bytes.Length / 4} words, {nonzero} not nought; first {string.Join(" ", said)}");
        }

        /// <summary>A buffer copied to the processor through a staging buffer of its size (the copy waits for the card).</summary>
        private static byte[] Back(ID3D11Device device, ID3D11DeviceContext context, ID3D11Buffer buffer)
        {
            int size = buffer.Description.ByteWidth;
            using ID3D11Buffer staging = device.CreateBuffer(new BufferDescription(size, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            context.CopyResource(staging, buffer);
            MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(mapped.DataPointer, bytes, 0, size);
                return bytes;
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        private static string At(Vector3 point) => Say($"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})");

        private static string At(Vector4 point) => Say($"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###}, {point.W:0.###})");

        private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}
