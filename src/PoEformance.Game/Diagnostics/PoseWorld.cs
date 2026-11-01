using System.Globalization;
using System.Numerics;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// A piece's place in the world as the game keeps it, for finding its bones there: its own rig, where its placement's World matrix lies,
/// and that matrix as read before the search.
/// </summary>
/// <param name="Rig">The piece's own rig, by its place in the list looked for.</param>
/// <param name="WorldAt">The address of its World matrix (PiecePlacement.World) - read again once the sweep is done.</param>
/// <param name="World">
/// The World matrix as read before the sweep: by rows, the translation its last row, its turn scaled - 0.925 for the player's pieces
/// (MEASURED in the 04:49 capture of 2026-11-01, v0.1.404).
/// </param>
public sealed record PiecePlace(int Rig, ulong WorldAt, Matrix4x4 World);

/// <summary>
/// A place near a piece in the world: three floats that, taken into the piece's frame by its World, stand from a simulated bone's nearest
/// fixed ancestor at nearly that bone's rest distance - a state the engine's simulation may keep - or at a fixed bone's rest.
/// </summary>
/// <param name="Place">The piece, by its place in the list of places.</param>
/// <param name="At">The address of its x.</param>
/// <param name="Layout">How its x, y and z lie.</param>
/// <param name="Bone">The simulated bone it was taken for, or the fixed bone at whose rest it stands.</param>
/// <param name="Parent">That simulated bone's nearest fixed ancestor - -1 for a fixed bone's rest.</param>
/// <param name="Ratio">
/// Its distance from that ancestor over the bone's rest distance from it - for a fixed bone's rest, its distance from that rest in the
/// model's units.
/// </param>
public sealed record WorldCandidate(int Place, ulong At, PoseLayout Layout, int Bone, int Parent, float Ratio);

/// <summary>
/// Looks for the pieces' bones in the game's WORLD: every three floats near a piece, taken into its frame by its placement's World matrix
/// and set against its rig - arrays of its bones there by their fixed bones' rests, and single places where a simulated bone may be kept.
/// </summary>
/// <remarks>
/// WHY (v0.1.407, DECIDED from the live client: a new search, then a new capture). v0.1.404 looked for each piece's skeleton in its parents'
/// frames and its model's, and the 04:49 capture of 2026-11-01 found only copies of the rest: 21 arrays of the body, the wand, the focus and
/// the shawl, not one bone of which moved over twelve seconds while the hand did (the wand's placement 5 units). So the poses the game draws
/// with are not kept in those frames, or not as their rests' floats - and a physics simulation stepping a lantern on a moving hand keeps its
/// state where the hand moves it: in the world. There each fixed bone stands at its model rest through the piece's World matrix
/// (PiecePlacement, its translation the piece's root and its rows scaled by 0.925), and a simulated bone at its length from its fixed
/// parent - the lantern's .ao allows its link no stretch (linear_allowed_contraction 0).
///
/// HOW. One box a piece, round its bones' rests through its World and wide enough for its simulated chains to swing out and for the hand
/// to wander while the person stands still (<see cref="Slack"/>), looked for in the same sweep as the rests, both ways a translation may
/// lie. Once the sweep is done each piece's World is read again and every place found is read again with it, so both are of one moment;
/// a place standing at a fixed bone's rest votes, as PoseHunt's do, for where an array of the rig would start, and a place standing from a
/// simulated bone's fixed ancestor at that bone's rest distance (within <see cref="RatioSlack"/>) is kept as a candidate. Both are watched;
/// what moves like a pendulum in the piece's frame is told apart offline (BoneWatchSeries).
/// </remarks>
public static class PoseWorld
{
    /// <summary>
    /// How far, in world units, the hand may wander from its place at the search's start while the person stands still - CHOSEN: the
    /// wand's placement moved 5 units over twelve seconds of his idle in the 04:49 capture, and a sweep takes some seconds.
    /// </summary>
    public const float Slack = 30f;

    /// <summary>Most places kept of one piece's box in one layout, nearest the anchor first - CHOSEN.</summary>
    public const int MostPlaces = 8192;

    /// <summary>How near a fixed bone's rest, in the model's units, a place taken into the piece's frame must stand to be that bone's - CHOSEN.</summary>
    public const float Tolerance = 0.5f;

    /// <summary>
    /// How far from one a place's distance from a simulated bone's fixed parent over the bone's rest distance may lie - CHOSEN: the lamp's link
    /// keeps its length (linear_allowed_contraction 0), the rest is reading noise and a frame between the two reads.
    /// </summary>
    public const float RatioSlack = 0.1f;

    /// <summary>Most candidates kept of one piece for its simulated bones, the nearest their bone's length first - CHOSEN.</summary>
    public const int MostCandidates = 256;

    /// <summary>Most places at a fixed bone's rest kept of one piece outside the arrays found - CHOSEN.</summary>
    public const int MostCopies = 64;

    /// <summary>A World matrix's scale: its three rows' lengths, averaged.</summary>
    public static float Scale(Matrix4x4 world)
        => (new Vector3(world.M11, world.M12, world.M13).Length() + new Vector3(world.M21, world.M22, world.M23).Length() + new Vector3(world.M31, world.M32, world.M33).Length()) / 3f;

    /// <summary>Whether a matrix read as a World is one: finite, its rows of one length, at right angles, a translation of its own.</summary>
    public static bool IsWorld(Matrix4x4 world)
    {
        byte[] bytes = new byte[64];
        Write(bytes, 0, world);
        return BoneWatchSeries.IsTurn(bytes, 0, 16) && float.IsFinite(world.M41 + world.M42 + world.M43) && MathF.Abs(world.M44 - 1f) < 1e-3f;
    }

    /// <summary>A 4x4 read by rows from 64 bytes.</summary>
    public static Matrix4x4 Read(ReadOnlySpan<byte> bytes, int at)
    {
        var matrix = default(Matrix4x4);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                matrix[row, column] = BitConverter.ToSingle(bytes.Slice(at + (16 * row) + (4 * column), 4));
            }
        }

        return matrix;
    }

    /// <summary>A 4x4 written by rows into 64 bytes.</summary>
    public static void Write(byte[] bytes, int at, Matrix4x4 matrix)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(at + (16 * row) + (4 * column), 4), matrix[row, column]);
            }
        }
    }

    /// <summary>A simulated bone's nearest ancestor that holds its rest in the model's frame - -1 where none does.</summary>
    public static int FixedAncestor(PoseRig rig, int bone)
    {
        ArgumentNullException.ThrowIfNull(rig);
        for (int at = rig.Parents[bone], steps = 0; at >= 0 && steps <= rig.Count; at = rig.Parents[at], steps++)
        {
            if (!rig.Moved(at))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>How far a simulated chain can reach from its fixed ancestor: its links' lengths summed.</summary>
    public static float Reach(PoseRig rig, int bone, int ancestor)
    {
        ArgumentNullException.ThrowIfNull(rig);
        float reach = 0;
        for (int at = bone, steps = 0; at >= 0 && at != ancestor && steps <= rig.Count; at = rig.Parents[at], steps++)
        {
            reach += rig.Local[at].Translation.Length();
        }

        return reach;
    }

    /// <summary>
    /// The box each place's piece is looked for in, both ways a translation may lie: round its bones' rests through its World, as wide as its
    /// rests spread from their middle and its longest simulated chain reaches, scaled, and <see cref="Slack"/> more.
    /// </summary>
    /// <param name="rigs">The rigs looked for.</param>
    /// <param name="places">The pieces' places.</param>
    /// <param name="owners">For each needle made, its place and layout.</param>
    public static List<FloatNeedle> Needles(IReadOnlyList<PoseRig> rigs, IReadOnlyList<PiecePlace> places, List<(int Place, PoseLayout Layout)> owners)
    {
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(owners);
        var needles = new List<FloatNeedle>();
        for (var place = 0; place < places.Count; place++)
        {
            PiecePlace one = places[place];
            PoseRig rig = rigs[one.Rig];
            if (rig.Count == 0)
            {
                continue;
            }

            (Vector3 middle, float radius) = Ball(rig);
            Vector3 centre = Vector3.Transform(middle, one.World);
            float tolerance = (radius * Scale(one.World)) + Slack;
            foreach (PoseLayout layout in (ReadOnlySpan<PoseLayout>)[PoseLayout.Packed, PoseLayout.Columns])
            {
                float[] values = layout == PoseLayout.Packed
                    ? [centre.X, centre.Y, centre.Z]
                    : [centre.X, float.NaN, float.NaN, float.NaN, centre.Y, float.NaN, float.NaN, float.NaN, centre.Z];
                needles.Add(new FloatNeedle(
                    string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(rig.Piece)} in the world, {layout}"), values, tolerance, Most: MostPlaces));
                owners.Add((place, layout));
            }
        }

        return needles;
    }

    /// <summary>The middle of a rig's rests in the model's frame and how far from it its bones may reach: their spread and the longest chain.</summary>
    public static (Vector3 Middle, float Radius) Ball(PoseRig rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        Vector3 least = new(float.MaxValue), most = new(float.MinValue);
        for (var bone = 0; bone < rig.Count; bone++)
        {
            Vector3 rest = rig.Model[bone].Translation;
            least = Vector3.Min(least, rest);
            most = Vector3.Max(most, rest);
        }

        Vector3 middle = (least + most) / 2f;
        float spread = 0, chain = 0;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            spread = MathF.Max(spread, Vector3.Distance(rig.Model[bone].Translation, middle));
            if (rig.Simulated(bone) && FixedAncestor(rig, bone) is var ancestor and >= 0)
            {
                chain = MathF.Max(chain, Reach(rig, bone, ancestor));
            }
        }

        return (middle, spread + chain);
    }

    /// <summary>
    /// What the places found in the world are, read again with each piece's World of the same moment: the arrays of a rig their fixed bones
    /// agree on, the single places where a simulated bone may be kept, and the places at a fixed bone's rest outside those arrays.
    /// </summary>
    /// <param name="reader">The game's memory.</param>
    /// <param name="rigs">The rigs looked for.</param>
    /// <param name="places">The pieces' places as read before the sweep.</param>
    /// <param name="sightings">Where each piece's box held three floats, and in which layout.</param>
    /// <param name="lines">What it says of itself, for the report.</param>
    public static (List<PoseArray> Arrays, List<WorldCandidate> Candidates, List<PiecePlace> Places) Found(
        IMemoryReader reader,
        IReadOnlyList<PoseRig> rigs,
        IReadOnlyList<PiecePlace> places,
        IReadOnlyList<(ulong At, int Place, PoseLayout Layout)> sightings,
        List<string> lines)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(sightings);
        ArgumentNullException.ThrowIfNull(lines);
        var arrays = new List<PoseArray>();
        var candidates = new List<WorldCandidate>();
        var fresh = new List<PiecePlace>(places);
        byte[] matrix = new byte[64];
        byte[] triple = new byte[PoseHunt.Span(PoseLayout.Columns)];
        for (var place = 0; place < places.Count; place++)
        {
            // THE WORLD AGAIN, then every place of this piece at once: the two of one moment, as near as reads allow.
            PiecePlace one = places[place];
            if (reader.TryRead(one.WorldAt, matrix) && Read(matrix, 0) is var now && IsWorld(now))
            {
                fresh[place] = one with { World = now };
            }

            PoseRig rig = rigs[one.Rig];
            if (!Matrix4x4.Invert(fresh[place].World, out Matrix4x4 back))
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {Path.GetFileName(rig.Piece)}: its World does not invert - nothing taken into its frame"));
                continue;
            }

            var copies = new List<(ulong At, PoseLayout Layout, int Bone, float Off)>();
            var kept = new List<WorldCandidate>();
            int asked = 0, unread = 0;
            foreach ((ulong at, int owner, PoseLayout layout) in sightings)
            {
                if (owner != place)
                {
                    continue;
                }

                asked++;
                Span<byte> bytes = triple.AsSpan(0, PoseHunt.Span(layout));
                if (!reader.TryRead(at, bytes))
                {
                    unread++;
                    continue;
                }

                Vector3 found = Vector3.Transform(PoseHunt.At(bytes, 0, layout), back);
                if (!float.IsFinite(found.X + found.Y + found.Z))
                {
                    continue;
                }

                (int nearest, float off) = Nearest(rig, found);
                if (off <= Tolerance && !rig.Moved(nearest))
                {
                    copies.Add((at, layout, nearest, off));
                    continue;
                }

                if (Simulated(rig, found) is { } taken)
                {
                    kept.Add(new WorldCandidate(place, at, layout, taken.Bone, taken.Ancestor, taken.Ratio));
                }
            }

            List<PoseArray> held = Arrays(reader, rig, one.Rig, place, back, copies, out int agreed);
            arrays.AddRange(held);

            // THE COPIES OUTSIDE THE ARRAYS: a lone world matrix of one bone - the lamp's parent's, kept for its light or its particles - says
            // where the engine keeps that much at least.
            int Inside(ulong at) => held.FindIndex(array => at >= array.First && at < array.First + (ulong)(rig.Count * array.Stride));
            List<WorldCandidate> loose = [.. copies
                .Where(copy => Inside(copy.At) < 0)
                .OrderBy(copy => copy.Off)
                .Take(MostCopies)
                .Select(copy => new WorldCandidate(place, copy.At, copy.Layout, copy.Bone, -1, copy.Off))];
            List<WorldCandidate> best = [.. kept.OrderBy(candidate => MathF.Abs(candidate.Ratio - 1f)).Take(MostCandidates)];
            candidates.AddRange(best);
            candidates.AddRange(loose);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(rig.Piece)} in the world: {asked} places in its box ({unread} no longer read), {copies.Count} at a fixed bone's rest - {agreed} addresses three of them agreed on, {held.Count} arrays kept - and {kept.Count} at a simulated bone's rest distance from its fixed ancestor ({best.Count} kept), {loose.Count} lone copies kept; its scale {Scale(fresh[place].World):0.####}, its root at ({fresh[place].World.M41:0.#}, {fresh[place].World.M42:0.#}, {fresh[place].World.M43:0.#})"));
        }

        return (arrays, candidates, fresh);
    }

    /// <summary>The bone whose model rest a place in the piece's frame stands nearest, and how far.</summary>
    public static (int Bone, float Off) Nearest(PoseRig rig, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(rig);
        int best = -1;
        float least = float.MaxValue;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            float off = Vector3.Distance(at, rig.Model[bone].Translation);
            if (off < least)
            {
                least = off;
                best = bone;
            }
        }

        return (best, least);
    }

    /// <summary>
    /// The simulated bone a place in the piece's frame may be: the one whose distance from its nearest fixed ancestor the place's stands
    /// nearest, within <see cref="RatioSlack"/> of a single link's length - a chain's anywhere from half its rest distance to its reach - or
    /// null where none.
    /// </summary>
    public static (int Bone, int Ancestor, float Ratio)? Simulated(PoseRig rig, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(rig);
        (int Bone, int Ancestor, float Ratio)? best = null;
        float least = float.MaxValue;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            if (!rig.Simulated(bone) || FixedAncestor(rig, bone) is not (var ancestor and >= 0))
            {
                continue;
            }

            Vector3 from = rig.Model[ancestor].Translation;
            float straight = Vector3.Distance(rig.Model[bone].Translation, from);
            if (straight < 1e-3f)
            {
                continue;
            }

            float distance = Vector3.Distance(at, from), ratio = distance / straight;
            bool link = rig.Parents[bone] == ancestor;
            bool fits = link
                ? MathF.Abs(ratio - 1f) <= RatioSlack
                : distance >= 0.5f * straight && distance <= (1f + RatioSlack) * Reach(rig, bone, ancestor);
            if (fits && MathF.Abs(ratio - 1f) < least)
            {
                least = MathF.Abs(ratio - 1f);
                best = (bone, ancestor, ratio);
            }
        }

        return best;
    }

    /// <summary>
    /// The arrays of a rig in the world its fixed bones' places agree on - as PoseHunt.Arrays votes, each read whole again and kept where at
    /// least half the fixed bones stand at their rest through the World.
    /// </summary>
    private static List<PoseArray> Arrays(
        IMemoryReader reader, PoseRig rig, int rigIndex, int place, Matrix4x4 back, List<(ulong At, PoseLayout Layout, int Bone, float Off)> copies, out int agreed)
    {
        int fixedBones = Enumerable.Range(0, rig.Count).Count(bone => !rig.Moved(bone));
        int need = PoseHunt.Agreeing(fixedBones);
        var votes = new Dictionary<(PoseLayout Layout, int Stride, ulong First), int>();
        foreach ((ulong at, PoseLayout layout, int bone, _) in copies)
        {
            foreach (int stride in PoseHunt.Strides)
            {
                ulong before = (ulong)bone * (ulong)stride;
                if (stride < PoseHunt.Span(layout) || at < before)
                {
                    continue;
                }

                var key = (layout, stride, at - before);
                votes[key] = votes.GetValueOrDefault(key) + 1;
            }
        }

        agreed = 0;
        var arrays = new List<PoseArray>();
        foreach (((PoseLayout layout, int stride, ulong first), int count) in votes)
        {
            if (count < need)
            {
                continue;
            }

            agreed++;
            byte[] bytes = new byte[((rig.Count - 1) * stride) + PoseHunt.Span(layout)];
            if (!reader.TryRead(first, bytes))
            {
                continue;
            }

            int matched = 0;
            float stray = 0;
            int simulated = 0;
            for (var bone = 0; bone < rig.Count; bone++)
            {
                Vector3 at = Vector3.Transform(PoseHunt.At(bytes, bone * stride, layout), back);
                float off = Vector3.Distance(at, rig.Model[bone].Translation);
                if (!rig.Moved(bone))
                {
                    matched += off <= Tolerance ? 1 : 0;
                }
                else if (rig.Simulated(bone))
                {
                    stray += float.IsFinite(off) ? off : float.MaxValue / 1024;
                    simulated++;
                }
            }

            if (matched >= Math.Max(need, (int)Math.Ceiling(PoseHunt.LeastShare * fixedBones)))
            {
                arrays.Add(new PoseArray(rigIndex, PoseFrame.World, layout, stride, first, matched, fixedBones, count, simulated == 0 ? 0f : stray / simulated) { Place = place });
            }
        }

        return [.. arrays.OrderByDescending(one => one.Matched).ThenByDescending(one => one.Votes).Take(PoseHunt.MostPerRig)];
    }
}
