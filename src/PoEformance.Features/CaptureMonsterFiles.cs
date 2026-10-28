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

        Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking = pane.Unpack ?? ((_, _) => null);
        Staging(body, model, actor);
        Parts(body, actor);
        List<(PartFit Part, int Bone)> sockets = SocketsOf(actor);
        List<Swept> his = Animations(body, model, actor, unpacking, sockets);
        Sockets(body, actor, sockets, his);
        int played = Pieces(body, read, actor, unpacking, his);
        int seen = Entities(body, pane.Path, entities, player);

        body.AppendLine().AppendLine("=== his files whole (ModelDump): every .ao he names and hangs off himself, the manifest, the shapes and how each part fitted");
        body.AppendLine(read is null ? "(no install to read them from)" : ModelDump.Of(read, pane.Variety, pane.Path, model).TrimEnd());

        int types = Types(body, read, pane);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"monster files: {pane.Variety?.Name ?? "?"} [{pane.Path}] - {(model.Standing is null ? "standing alone" : "on a stage")}, {actor.Fitted.Count} parts as hung, {his.Count} of {actor.Rig.Animations.Count} animations swept, {played} parts' own rigs played, {seen} of the game's entities of him read, {types} object type files whole");
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

    /// <summary>One of his animations as swept: every bone's place at frame nought, and each socketed part's bone at nought and over it.</summary>
    /// <param name="Name">The animation's name.</param>
    /// <param name="Bones">Every bone's place in his own frame at frame nought.</param>
    /// <param name="SocketAt">Each socket's bone's place at frame nought, in the order of <see cref="SocketsOf"/>.</param>
    /// <param name="SocketScale">That bone's least axis scale at frame nought.</param>
    /// <param name="SocketLeast">Its least axis scale over the animation.</param>
    private sealed record Swept(string Name, Vector3[] Bones, Vector3[] SocketAt, float[] SocketScale, float[] SocketLeast);

    /// <summary>The parts hung on a bone of his own (depth one), each with that bone's number - the mounts a rig spells in full included.</summary>
    private static List<(PartFit Part, int Bone)> SocketsOf(MonsterModel actor)
    {
        var said = new List<(PartFit, int)>();
        foreach (PartFit part in actor.Fitted)
        {
            if (part.Depth == 1 && part.Place == PartPlace.Bone && MonsterModels.SocketOf(actor.Rig, part.Socket) is >= 0 and int bone)
            {
                said.Add((part, bone));
            }
        }

        return said;
    }

    /// <summary>
    /// Every animation stood in at up to <see cref="MostFrames"/> frames: his lowest point (SkeletonPose.Lowest, every part with him) and
    /// his root bone's place - and, on a stage, where that lowest point lands against the floor he was set on. Returns what each swept
    /// animation said of his bones, for the sockets' and the pieces' sections.
    /// </summary>
    private static List<Swept> Animations(
        StringBuilder body, MonsterModel model, MonsterModel actor, Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking, List<(PartFit Part, int Bone)> sockets)
    {
        AnimationSkeleton rig = actor.Rig;
        var his = new List<Swept>();
        body.AppendLine().Append(Invariant($"=== every animation ({rig.Animations.Count}), stood in at up to {MostFrames} frames each: his lowest point in his own frame, its least and most over the animation and the frame of the most; his root bone's place; and on a stage how far the lowest point lands under (+) or over (-) the floor he was set on")).AppendLine();
        if (!rig.Ready || SkeletonPose.Of(rig) is not { } pose)
        {
            body.AppendLine(rig.Ready ? "(the rig's bone tree does not hold together)" : "(no rig: " + actor.Move + ")");
            return his;
        }

        int root = Math.Max(0, pose.Parents.ToList().IndexOf(-1));
        body.Append(Invariant($"  the root bone: {(root < rig.Bones.Count ? rig.Bones[root].Name : "?")} (bone {root}), at rest {At(pose.BindModel[root].Translation)}")).AppendLine();
        body.AppendLine("  animation\tframes\tlowest at 0\tlowest least..most\tat frame\troot at 0\troot z least..most\tlowest less the floor at 0\tat the most");
        float scale = model.Standing?.Scale ?? 1f;
        float originZ = model.ActorPlace.Translation.Z;
        float? floor = model.Standing?.Floor;
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
            var swept = new Swept(move.Name, new Vector3[rig.Bones.Count], new Vector3[sockets.Count], new float[sockets.Count], new float[sockets.Count]);
            Array.Fill(swept.SocketLeast, float.MaxValue);
            for (var one = 0; one < count; one++)
            {
                float frame = tracks.Frames * one / (count - 1);
                pose.Take(tracks, frame);
                float lowest = pose.Lowest(actor.Mesh);
                Vector3 at = pose.World[root].Translation;
                if (one == 0)
                {
                    (first, rootFirst) = (lowest, at);
                    for (var bone = 0; bone < swept.Bones.Length; bone++)
                    {
                        swept.Bones[bone] = pose.World[bone].Translation;
                    }
                }

                for (var socket = 0; socket < sockets.Count; socket++)
                {
                    Matrix4x4 there = pose.World[sockets[socket].Bone];
                    float axes = AxisScale(there);
                    if (one == 0)
                    {
                        (swept.SocketAt[socket], swept.SocketScale[socket]) = (there.Translation, axes);
                    }

                    swept.SocketLeast[socket] = MathF.Min(swept.SocketLeast[socket], axes);
                }

                least = MathF.Min(least, lowest);
                if (lowest > most)
                {
                    (most, mostAt) = (lowest, frame);
                }

                rootLeast = MathF.Min(rootLeast, at.Z);
                rootMost = MathF.Max(rootMost, at.Z);
            }

            his.Add(swept);
            body.Append("  ").Append(move.Name).Append(Invariant($"\t{tracks.Frames:0.#}\t{first:0.###}\t{least:0.###}..{most:0.###}\t{mostAt:0.#}\t{At(rootFirst)}\t{rootLeast:0.###}..{rootMost:0.###}"));
            body.AppendLine(floor is { } set
                ? Invariant($"\t{originZ + (first * scale) - set:0.###}\t{originZ + (most * scale) - set:0.###}")
                : "\t-\t-");
        }

        return his;
    }

    /// <summary>
    /// Each part hung on a bone of his, that bone in every animation: where it is at frame nought, and its least axis scale at nought and
    /// over the animation - a bone scaled to nothing hides what it carries.
    /// </summary>
    /// <remarks>
    /// FOR THE SPYGLASS (v0.1.328): in the game Malgor holds it in his left hand throughout and fires it as a cannon in some skills (said
    /// from the live client), and the pane draws nothing there. Whether its socket is a bone his animations carry, where, and whether they
    /// shrink it away, is this table; the part's own line above says how it was hung.
    /// </remarks>
    private static void Sockets(StringBuilder body, MonsterModel actor, List<(PartFit Part, int Bone)> sockets, List<Swept> his)
    {
        body.AppendLine().Append(Invariant($"=== each part on one of his bones ({sockets.Count}), that bone in every animation: its place in his own frame at frame 0, and its least axis scale at 0 and over the animation (nought: the bone hides what it carries)")).AppendLine();
        if (sockets.Count == 0)
        {
            body.AppendLine("(no part hangs on a bone of his)");
            return;
        }

        body.AppendLine("  part\tsocket\tbone\tanimation\tat 0\tscale at 0\tscale least");
        for (var socket = 0; socket < sockets.Count; socket++)
        {
            (PartFit part, int bone) = sockets[socket];
            string named = bone < actor.Rig.Bones.Count ? actor.Rig.Bones[bone].Name : "?";
            foreach (Swept one in his)
            {
                body.Append("  ").Append(Short(part.File)).Append('\t').Append(part.Socket).Append('\t').Append(named).Append('\t').Append(one.Name)
                    .Append('\t').Append(At(one.SocketAt[socket])).Append(Invariant($"\t{one.SocketScale[socket]:0.###}\t{one.SocketLeast[socket]:0.###}")).AppendLine();
            }
        }
    }

    /// <summary>
    /// Every part that brings a rig of its own, played by that rig alone in each of its animations: where its mesh lies and how big it is
    /// at frame nought and over the animation, its root, whether he plays an animation of that name, and - for a piece in his own frame -
    /// which of his bones lies nearest its middle at frame nought of his. Returns how many pieces were played.
    /// </summary>
    /// <remarks>
    /// FOR THE SHIP'S WHEEL (v0.1.328): socketed "&lt;root&gt;", ten bones of its own sharing only the root with him, so the pane leaves it
    /// at his origin - while in the game it hangs round his neck in his second phase (said from the live client). Its own rig has
    /// animations nothing here plays. PRINT BOTH HALVES: where its own animations put it, beside where his put his bones, so the file says
    /// whether they carry it to his neck - and whether in the others they shrink it to nothing - before the pane is given any rule for it.
    /// </remarks>
    private static int Pieces(StringBuilder body, Func<string, byte[]?>? read, MonsterModel actor, Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking, List<Swept> his)
    {
        body.AppendLine().Append(Invariant($"=== the parts' own rigs, each played by itself (MonsterModels.Piece) at up to {MostFrames} frames an animation: its mesh's box at frame 0 in its own frame, its size (the box's longest side) at 0 and least..most over the animation with the frame of the least, its root bone at 0; whether he plays an animation of that name; and for a piece in his own frame (\"<root>\" on him) his bone nearest the box's middle at frame 0 of his animation of that name")).AppendLine();
        if (read is null)
        {
            body.AppendLine("(no install to read them from)");
            return 0;
        }

        var named = new Dictionary<string, Swept>(StringComparer.OrdinalIgnoreCase);
        foreach (Swept one in his)
        {
            named.TryAdd(one.Name, one);
        }

        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int played = 0;
        foreach (PartFit part in actor.Fitted)
        {
            if (part.Bones <= 0 || part.Lost || !done.Add(part.File))
            {
                continue;
            }

            (AnimationSkeleton rig, string rigPath, SkinnedMesh mesh) = MonsterModels.Piece(read, part.File);
            body.Append("--- ").Append(part.File).Append(Invariant($"  socket {(part.Socket.Length > 0 ? part.Socket : "(none)")} ({part.Place}, depth {part.Depth}), its rig {(rigPath.Length > 0 ? rigPath : "(none named)")}"));
            if (!rig.Ready || SkeletonPose.Of(rig) is null)
            {
                body.AppendLine(rig.Ready ? " - its bone tree does not hold together" : " - did not read");
                continue;
            }

            body.Append(Invariant($": {rig.Bones.Count} bones, {rig.Animations.Count} animations"));
            if (!mesh.Ready)
            {
                body.AppendLine("; no mesh read");
                continue;
            }

            body.Append(Invariant($"; its mesh {mesh.Positions.Length} vertices, at rest {At(mesh.Least)} .. {At(mesh.Most)}"));
            if (SkeletonPose.Highest(mesh) >= rig.Bones.Count)
            {
                body.AppendLine(" - INDEXED PAST ITS OWN RIG: its numbers are its carrier's, so its own rig does not move it");
                continue;
            }

            body.AppendLine();
            var its = new HashSet<string>(rig.Animations.Select(one => one.Name), StringComparer.OrdinalIgnoreCase);
            string[] lacks = [.. his.Select(one => one.Name).Where(one => !its.Contains(one))];
            string[] own = [.. rig.Animations.Select(one => one.Name).Where(one => !named.ContainsKey(one))];
            body.Append(Invariant($"  of his {his.Count} animations it has {his.Count - lacks.Length} by name; his it lacks: {Listed(lacks)}; its own he lacks: {Listed(own)}")).AppendLine();
            body.AppendLine("  animation\tframes\tbox at 0\tsize at 0\tsize least..most\tat frame\tits root at 0\the plays it\this bone nearest its middle at 0");
            Played(body, part, actor.Rig, rig, mesh, unpacking, named);
            played++;
        }

        if (played == 0 && done.Count == 0)
        {
            body.AppendLine("(no part brings a rig of its own)");
        }

        return played;
    }

    /// <summary>One piece's animations played by its own rig, each on a core of its own, written in the rig's order.</summary>
    private static void Played(
        StringBuilder body,
        PartFit part,
        AnimationSkeleton his,
        AnimationSkeleton rig,
        SkinnedMesh mesh,
        Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking,
        Dictionary<string, Swept> named)
    {
        // THE KEYFRAMES UNPACKED HERE, ONE AFTER ANOTHER: the install's decoder is handed over as it is, and nothing says it may be called
        // from several threads at once. The posing after is each animation's own.
        int count = rig.Animations.Count;
        var tracks = new AnimationTracks?[count];
        var why = new string[count];
        for (var one = 0; one < count; one++)
        {
            SkeletonAnimation move = rig.Animations[one];
            byte[]? frames = rig.Tracks(move, unpacking);
            tracks[one] = frames is null ? null : AnimationTracks.Read(frames, move.Tracks, rig.Version);
            why[one] = frames is null ? "its keyframes did not unpack" : tracks[one] is { Ready: true } ? string.Empty : tracks[one]?.Why ?? "its keyframes did not read";
        }

        bool inHisFrame = part.Depth == 1 && part.Place == PartPlace.Root;
        var rows = new string[count];
        Parallel.For(
            0,
            count,
            () => (Pose: SkeletonPose.Of(rig)!, Positions: new Vector3[mesh.Positions.Length], Normals: new Vector3[mesh.Positions.Length]),
            (one, _, local) =>
            {
                SkeletonAnimation move = rig.Animations[one];
                if (tracks[one] is not { Ready: true } keys)
                {
                    rows[one] = "  " + move.Name + "\t(" + why[one] + ")";
                    return local;
                }

                SkeletonPose pose = local.Pose;
                int root = Math.Max(0, pose.Parents.ToList().IndexOf(-1));
                int frames = Math.Clamp((int)MathF.Ceiling(keys.Frames) + 1, 2, MostFrames);
                (Vector3 Least, Vector3 Most) first = default;
                Vector3 rootFirst = default;
                float sizeLeast = float.MaxValue, sizeMost = float.MinValue, leastAt = 0f;
                for (var at = 0; at < frames; at++)
                {
                    float frame = keys.Frames * at / (frames - 1);
                    pose.Take(keys, frame);
                    pose.Move(mesh, local.Positions.AsSpan(), local.Normals.AsSpan());
                    (Vector3 least, Vector3 most) = Box(local.Positions);
                    float size = MathF.Max(most.X - least.X, MathF.Max(most.Y - least.Y, most.Z - least.Z));
                    if (at == 0)
                    {
                        (first, rootFirst) = ((least, most), pose.World[root].Translation);
                    }

                    if (size < sizeLeast)
                    {
                        (sizeLeast, leastAt) = (size, frame);
                    }

                    sizeMost = MathF.Max(sizeMost, size);
                }

                float sizeFirst = MathF.Max(first.Most.X - first.Least.X, MathF.Max(first.Most.Y - first.Least.Y, first.Most.Z - first.Least.Z));
                bool plays = named.TryGetValue(move.Name, out Swept? his_);
                string nearest = "-";
                if (inHisFrame && his_ is not null)
                {
                    Vector3 middle = (first.Least + first.Most) * 0.5f;
                    int best = -1;
                    float distance = float.MaxValue;
                    for (var bone = 0; bone < his_.Bones.Length; bone++)
                    {
                        float apart = Vector3.Distance(his_.Bones[bone], middle);
                        if (apart < distance)
                        {
                            (best, distance) = (bone, apart);
                        }
                    }

                    nearest = best >= 0 && best < his.Bones.Count ? Invariant($"{his.Bones[best].Name} at {At(his_.Bones[best])}, {distance:0.#} away") : "-";
                }

                rows[one] = "  " + move.Name + Invariant($"\t{keys.Frames:0.#}\t{At(first.Least)} .. {At(first.Most)}\t{sizeFirst:0.#}\t{sizeLeast:0.#}..{sizeMost:0.#}\t{leastAt:0.#}\t{At(rootFirst)}\t{(plays ? "yes" : "no")}\t{nearest}");
                return local;
            },
            _ => { });

        foreach (string row in rows)
        {
            body.AppendLine(row);
        }
    }

    /// <summary>The box round some points.</summary>
    private static (Vector3 Least, Vector3 Most) Box(Vector3[] points)
    {
        Vector3 least = new(float.MaxValue), most = new(float.MinValue);
        foreach (Vector3 point in points)
        {
            least = Vector3.Min(least, point);
            most = Vector3.Max(most, point);
        }

        return (least, most);
    }

    /// <summary>A bone's least axis scale: the shortest row of its matrix's turning part, rows being its axes.</summary>
    private static float AxisScale(Matrix4x4 bone) => MathF.Min(
        new Vector3(bone.M11, bone.M12, bone.M13).Length(),
        MathF.Min(new Vector3(bone.M21, bone.M22, bone.M23).Length(), new Vector3(bone.M31, bone.M32, bone.M33).Length()));

    /// <summary>Names in a line, the first sixteen and how many more.</summary>
    private static string Listed(string[] names) => names.Length == 0
        ? "none"
        : string.Join(", ", names.Take(16)) + (names.Length > 16 ? Invariant($" and {names.Length - 16} more") : string.Empty);

    /// <summary>A file's name without its folders.</summary>
    private static string Short(string path) => path[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];

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
