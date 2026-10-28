using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The capture's monster-files part: the Monster Book's monster as the tool builds and stands it - where the stage put him and the
/// floor it found, each part as it was hung, his lowest point in every animation, his files whole - beside the game's own entity of
/// him where one is near.
/// </summary>
/// <remarks>
/// WRITTEN FOR MALGOR (v0.1.327, asked for from the live client): on Port's stage he floats above the deck in every animation and
/// above his wooden throne in emerge_idle_01, the spyglass he holds in his left hand in the game is not drawn at all, and his ship's
/// wheel sits at the bottom of his body whatever he does. The captures held neither his own files nor his entity, so none of the
/// three could be read - only guessed. PRINT BOTH HALVES: what the tool did (the place, the floor asked at nine points, the rest's
/// lowest point, each part's socket and box, every animation's lowest point and root) and what it did it from (every .ao the monster
/// names and hangs off itself, and his object types, whole) - and the game's own entity of him (its place, its terrain height, its
/// bounds and the animation it plays), the measurement of where the game stands him.
/// </remarks>
public static class CaptureMonsterFiles
{
    /// <summary>Most frames of one animation stood in - the animation's own where it has fewer.</summary>
    private const int MostFrames = 64;

    /// <summary>The object type files a monster is made of, each followed through what it extends.</summary>
    private static readonly string[] Kinds = [".ot", ".otc"];

    /// <summary>
    /// The part's text: a line saying what it holds, then the stage's placing, the parts, the animations, the game's entities of him, and
    /// his files whole. Never throws on a missing file.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path - or null, and nothing is read.</param>
    /// <param name="pane">The Monster Book's pane as it stood.</param>
    /// <param name="entities">The game's entities as the capture read them, or null.</param>
    /// <param name="player">Where the player stood, for each entity's distance - or null.</param>
    public static string Write(Func<string, byte[]?>? read, CapturePane pane, IReadOnlyList<WorldEntity>? entities, Vector2? player)
    {
        ArgumentNullException.ThrowIfNull(pane);
        MonsterModel model = pane.Model;
        MonsterModel actor = model.Actor ?? model;
        var body = new StringBuilder();

        Staging(body, model, actor);
        Parts(body, actor);
        int swept = Animations(body, model, actor, pane.Unpack);
        int seen = Entities(body, pane.Path, entities, player);

        body.AppendLine().AppendLine("=== his files whole (ModelDump): every .ao he names and hangs off himself, the manifest, the shapes and how each part fitted");
        body.AppendLine(read is null ? "(no install to read them from)" : ModelDump.Of(read, pane.Variety, pane.Path, model).TrimEnd());

        int types = Types(body, read, pane);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"monster files: {pane.Variety?.Name ?? "?"} [{pane.Path}] - {(model.Standing is null ? "standing alone" : "on a stage")}, {actor.Fitted.Count} parts as hung, {swept} of {actor.Rig.Animations.Count} animations swept, {seen} of the game's entities of him read, {types} object type files whole");
        return head + Environment.NewLine + body;
    }

    /// <summary>Where the stage stood him: his place, the rest's box and lowest point, and the floor asked at nine points.</summary>
    private static void Staging(StringBuilder body, MonsterModel model, MonsterModel actor)
    {
        body.AppendLine().AppendLine("=== where the stage stands him (StagedModels.Staged): his feet - the rest's lowest point, up being minus z - set on the floor round the place");
        Vector3 least = actor.Mesh.Least, most = actor.Mesh.Most;
        body.Append("  rest\this own box at rest, his own frame: ").Append(At(least)).Append(" .. ").Append(At(most))
            .Append(Invariant($" - his lowest point at rest {most.Z:0.###} under his origin, his highest {-least.Z:0.###} over it")).AppendLine();
        if (model.Standing is not { } standing)
        {
            body.AppendLine("  place\tnot staged: he stands alone, his origin the picture's");
            return;
        }

        Vector3 origin = model.ActorPlace.Translation;
        body.Append("  stage\t").AppendLine(model.Stage);
        body.Append("  said\t").AppendLine(model.StageSaid);
        body.Append(Invariant($"  place\tscale {standing.Scale:0.###}, turn {standing.Turn * 180f / MathF.PI:0.##} degrees, at ({standing.At.X:0.###}, {standing.At.Y:0.###}); his origin at {At(origin)} in the stage's frame - {standing.Floor - origin.Z:0.###} over the floor he was set on ({standing.Floor:0.###}{(standing.Found ? string.Empty : ", none found: the tile's lowest point")})")).AppendLine();
        body.Append("  floor\tasked at the place and round it:");
        foreach ((Vector2 off, float height) in standing.Floors)
        {
            // ROUNDED, AND A NOUGHT FROM A SINE'S LAST BIT WRITTEN AS ONE: -0 plus 0 is 0.
            body.Append(Invariant($" ({MathF.Round(off.X) + 0f:0}, {MathF.Round(off.Y) + 0f:0}) {(float.IsFinite(height) ? height.ToString("0.###", CultureInfo.InvariantCulture) : "none")};"));
        }

        body.AppendLine(" the middle of those found taken");
    }

    /// <summary>Each part as it was hung (PartFit): its file, its socket and how that was answered, what it is, and its box once on him.</summary>
    private static void Parts(StringBuilder body, MonsterModel actor)
    {
        body.AppendLine().Append(Invariant($"=== the parts as hung (MonsterModels' Dressing, PartFit): {actor.Fitted.Count} - its box in his own frame at rest, up being minus z")).AppendLine();
        body.AppendLine("  file\tsocket\tplace\tdepth\tkind\tbones\tmatched\tstrays\tpaired\tmalformed\tbox\twhy");
        foreach (PartFit part in actor.Fitted)
        {
            body.Append("  ").Append(part.File).Append('\t').Append(part.Socket).Append('\t').Append(part.Place).Append('\t').Append(Invariant($"{part.Depth}"))
                .Append('\t').Append(part.Kind).Append('\t').Append(Invariant($"{part.Bones}\t{part.Matched}\t{part.Strays}\t{part.Paired}")).Append('\t').Append(part.Malformed ? "yes" : "no")
                .Append('\t').Append(At(part.Least)).Append(" .. ").Append(At(part.Most)).Append('\t').AppendLine(part.Why);
        }
    }

    /// <summary>
    /// Every animation stood in at up to <see cref="MostFrames"/> frames: his lowest point (SkeletonPose.Lowest, every part with him) and
    /// his root bone's place - and, on a stage, where that lowest point lands against the floor he was set on. Returns how many were swept.
    /// </summary>
    private static int Animations(StringBuilder body, MonsterModel model, MonsterModel actor, Func<ReadOnlyMemory<byte>, int, byte[]?>? unpack)
    {
        AnimationSkeleton rig = actor.Rig;
        body.AppendLine().Append(Invariant($"=== every animation ({rig.Animations.Count}), stood in at up to {MostFrames} frames each: his lowest point in his own frame, its least and most over the animation and the frame of the most; his root bone's place; and on a stage how far the lowest point lands under (+) or over (-) the floor he was set on")).AppendLine();
        if (!rig.Ready || SkeletonPose.Of(rig) is not { } pose)
        {
            body.AppendLine(rig.Ready ? "(the rig's bone tree does not hold together)" : "(no rig: " + actor.Move + ")");
            return 0;
        }

        int root = Math.Max(0, pose.Parents.ToList().IndexOf(-1));
        body.Append(Invariant($"  the root bone: {(root < rig.Bones.Count ? rig.Bones[root].Name : "?")} (bone {root}), at rest {At(pose.BindModel[root].Translation)}")).AppendLine();
        body.AppendLine("  animation\tframes\tlowest at 0\tlowest least..most\tat frame\troot at 0\troot z least..most\tlowest less the floor at 0\tat the most");
        float scale = model.Standing?.Scale ?? 1f;
        float originZ = model.ActorPlace.Translation.Z;
        float? floor = model.Standing?.Floor;
        int swept = 0;
        Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking = unpack ?? ((_, _) => null);
        foreach (SkeletonAnimation move in rig.Animations)
        {
            byte[]? frames = rig.Tracks(move, unpacking);
            AnimationTracks? tracks = frames is null ? null : AnimationTracks.Read(frames, move.Tracks, rig.Version);
            if (tracks is not { Ready: true })
            {
                body.Append("  ").Append(move.Name).Append("\t(").Append(frames is null ? "its keyframes did not unpack" : tracks?.Why ?? "its keyframes did not read").AppendLine(")");
                continue;
            }

            int count = Math.Clamp((int)MathF.Ceiling(tracks.Frames) + 1, 2, MostFrames);
            float first = 0f, least = float.MaxValue, most = float.MinValue, mostAt = 0f, rootLeast = float.MaxValue, rootMost = float.MinValue;
            Vector3 rootFirst = default;
            for (var one = 0; one < count; one++)
            {
                float frame = tracks.Frames * one / (count - 1);
                pose.Take(tracks, frame);
                float lowest = pose.Lowest(actor.Mesh);
                Vector3 at = pose.World[root].Translation;
                if (one == 0)
                {
                    (first, rootFirst) = (lowest, at);
                }

                least = MathF.Min(least, lowest);
                if (lowest > most)
                {
                    (most, mostAt) = (lowest, frame);
                }

                rootLeast = MathF.Min(rootLeast, at.Z);
                rootMost = MathF.Max(rootMost, at.Z);
            }

            swept++;
            body.Append("  ").Append(move.Name).Append(Invariant($"\t{tracks.Frames:0.#}\t{first:0.###}\t{least:0.###}..{most:0.###}\t{mostAt:0.#}\t{At(rootFirst)}\t{rootLeast:0.###}..{rootMost:0.###}"));
            body.AppendLine(floor is { } set
                ? Invariant($"\t{originZ + (first * scale) - set:0.###}\t{originZ + (most * scale) - set:0.###}")
                : "\t-\t-");
        }

        return swept;
    }

    /// <summary>
    /// The game's entities of this monster the capture read - its metadata path the pane's - with where each stands, the terrain's height
    /// under it, its bounds and what it is doing. Returns how many.
    /// </summary>
    private static int Entities(StringBuilder body, string path, IReadOnlyList<WorldEntity>? entities, Vector2? player)
    {
        body.AppendLine().AppendLine("=== the game's own entities of him near the player (Render's place and TerrainHeight, the model's bounds - as entities.txt writes them; the action as the game holds it, its animation's number among them)");
        string wanted = Base(path);
        WorldEntity[] his = wanted.Length == 0 || entities is null
            ? []
            : [.. entities.Where(one => string.Equals(Base(one.Path), wanted, StringComparison.OrdinalIgnoreCase))];
        if (his.Length == 0)
        {
            body.AppendLine(entities is null
                ? "(the entities were not read)"
                : $"(none of the {entities.Count} entities read is {wanted} - stand where the game shows him, near enough to see his health bar)");
            return 0;
        }

        body.AppendLine("  id\tdistance\tx\ty\tz\tterrain\tz less the terrain\tbounds z\tremembered\taction");
        foreach (WorldEntity one in his)
        {
            float distance = player is { } at ? Vector2.Distance(at, new Vector2(one.WorldX, one.WorldY)) : float.NaN;
            body.Append(Invariant($"  {one.Id}\t{distance:0.#}\t{one.WorldX:0.###}\t{one.WorldY:0.###}\t{one.WorldZ:0.###}\t{one.TerrainHeight:0.###}\t{one.WorldZ - one.TerrainHeight:0.###}\t{one.ModelBoundsZ:0.###}\t{(one.IsRemembered ? "yes" : "no")}\t"));
            body.AppendLine(one.Action is { } action
                ? Invariant($"{action.Kind} (raw {action.RawId}, animation {action.AnimationId})")
                : "-");
        }

        return his.Length;
    }

    /// <summary>His object types whole - the metadata path's and those his row says he is built on - each through what it extends. Returns how many files.</summary>
    private static int Types(StringBuilder body, Func<string, byte[]?>? read, CapturePane pane)
    {
        body.AppendLine().AppendLine("=== his object types whole (.ot, .otc): his own and those his row is built on, each through what it extends");
        if (read is null)
        {
            body.AppendLine("(no install to read them from)");
            return 0;
        }

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (string type in new[] { Base(pane.Path) }.Concat((pane.Variety?.Inherits ?? []).Select(Base)).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string kind in Kinds)
            {
                files += CaptureWobble.Chain(read, type + kind, kind, written, body, depth: 0);
            }
        }

        return files;
    }

    /// <summary>A metadata path without what follows an '@' and without an object type's extension.</summary>
    private static string Base(string path)
    {
        string clean = path.Trim();
        int at = clean.IndexOf('@', StringComparison.Ordinal);
        clean = at >= 0 ? clean[..at] : clean;
        foreach (string kind in Kinds)
        {
            if (clean.EndsWith(kind, StringComparison.OrdinalIgnoreCase))
            {
                return clean[..^kind.Length];
            }
        }

        return clean;
    }

    private static string At(Vector3 point) => Invariant($"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
