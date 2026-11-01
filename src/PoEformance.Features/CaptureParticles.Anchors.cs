using System.Globalization;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

public static partial class CaptureParticles
{
    /// <summary>
    /// The numbers the panes' particle emitters carry that a dispatch's globals may hold, as needles for the particle emitters part's search
    /// (ParticleEmitterHunt's globals): each emitter's emit_burst where it is not nought, and its particle_duration_min and _max side by
    /// side - gpu_particle_burst and gpu_particles_duration by their names, a reading. Equal needles are given once.
    /// </summary>
    /// <remarks>
    /// WHY THESE (v0.1.436): the lamp's embers burst at 45.8 and its shimmer lives 0.7 to 1.3 s - numbers rare in memory - while no file and
    /// no emitter record holds what the update's own globals are set to, so where the processor keeps them is found by what they should hold.
    /// </remarks>
    /// <param name="read">The install's files by path.</param>
    /// <param name="panes">The model panes at the press.</param>
    public static List<FloatNeedle> Anchors(Func<string, byte[]?> read, IReadOnlyList<CapturePane>? panes)
    {
        ArgumentNullException.ThrowIfNull(read);
        var seeds = new List<(string Path, string By)>();
        foreach (CapturePane pane in panes ?? [])
        {
            seeds.AddRange((pane.Variety?.AoFiles ?? []).Select(file => (file, "a pane's monster")));
            seeds.AddRange((pane.Model.Actor ?? pane.Model).Fitted.Select(part => (part.File, "a part of a pane's monster")));
        }

        var needles = new List<FloatNeedle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string name, float[] values)
        {
            if (seen.Add(string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))))
            {
                needles.Add(new FloatNeedle(name, values, 1e-5f, Around: GlobalsAround, Dumps: GlobalsDumps, Most: 4096));
            }
        }

        foreach (Walked one in Walk(read, seeds, new StringBuilder()).Where(one => one.Text is not null && one.Path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<ParticleEmitter> emitters = ParticleEffect.Read(one.Text!);
            for (var e = 0; e < emitters.Count; e++)
            {
                string name = $"{Path.GetFileName(one.Path)} gpu_emitters[{e}]";
                float burst = (float)emitters[e].Number("emit_burst");
                if (burst != 0f)
                {
                    Add($"{name} emit_burst", [burst]);
                }

                Add($"{name} particle_duration_min, _max", [(float)emitters[e].Number("particle_duration_min"), (float)emitters[e].Number("particle_duration_max")]);
            }
        }

        return needles;
    }

    /// <summary>How many bytes either side of an anchor's place are read back - CHOSEN: a cbuffer of the update's globals and more.</summary>
    public const int GlobalsAround = 256;

    /// <summary>How many of an anchor's places are read back - CHOSEN.</summary>
    public const int GlobalsDumps = 48;
}
