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

    /// <summary>The kinds an object names an effect by: a particle effect, an effect pack, a trail.</summary>
    private static readonly string[] EffectKinds = [".pet", ".epk", ".trl"];

    /// <summary>The text kinds of the effects' walk: written whole and read for what they name.</summary>
    private static readonly string[] EffectTexts = [".ao", ".aoc", ".ot", ".otc", ".pet", ".epk", ".trl", ".mat", ".fxgraph", ".atlas", ".amd"];

    /// <summary>Most files the effects' walk reads - well past the lantern's chain, a guard against a pack naming the world.</summary>
    private const int MostEffectFiles = 600;

    /// <summary>How many files deep the effects' walk follows - from his .ao to an effect to its material to its graph and texture.</summary>
    private const int MostEffectHops = 6;

    /// <summary>The characters the effects' text files may take whole, and the most one file may.</summary>
    private const int EffectChars = 6 * 1024 * 1024, LargestEffect = 1024 * 1024;

    /// <summary>The bytes the effects' textures may take whole beside the part, and the most one texture may - CHOSEN.</summary>
    private const long EffectTextures = 32L * 1024 * 1024;

    private const int LargestEffectTexture = 8 * 1024 * 1024;

    /// <summary>The name a bone the engine simulates starts with - the lantern's phys_tri_lamp_jntBnd, a skirt's phys_skinned_*.</summary>
    internal const string PhysicsBone = "phys_";

    /// <summary>
    /// The part's text: a line saying what it holds, then the stage's placing, the parts, the animations, the game's entities of him, the
    /// effects he and his parts name, and his files whole. Never throws on a missing file.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path - or null, and nothing is read.</param>
    /// <param name="pane">The Monster Book's pane as it stood.</param>
    /// <param name="entities">The game's entities as the capture read them, or null.</param>
    /// <param name="player">Where the player stood, for each entity's distance - or null.</param>
    /// <param name="wholes">The effects' textures, each to be written whole beside the part under its name.</param>
    public static string Write(
        Func<string, byte[]?>? read, CapturePane pane, IReadOnlyList<WorldEntity>? entities, Vector2? player, out IReadOnlyList<(string File, byte[] Bytes)> wholes)
    {
        ArgumentNullException.ThrowIfNull(pane);
        MonsterModel model = pane.Model;
        MonsterModel actor = model.Actor ?? model;
        var body = new StringBuilder();

        Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking = pane.Unpack ?? ((_, _) => null);
        Staging(body, model, actor);
        Parts(body, actor);
        Carried(body, actor);
        Swung(body, actor, unpacking);
        List<(PartFit Part, int Bone)> sockets = SocketsOf(actor);
        List<Swept> his = Animations(body, model, actor, unpacking, sockets);
        Sockets(body, actor, sockets, his);
        int played = Pieces(body, read, actor, unpacking, his);
        Cloth(body, read, [.. pane.Variety?.AoFiles ?? [], .. actor.Fitted.Where(part => !part.Lost).Select(part => part.File)]);
        int seen = Entities(body, pane.Path, entities, player);
        var textures = new List<(string File, byte[] Bytes)>();
        int effects = Effects(body, read, pane, actor, textures);
        wholes = textures;

        body.AppendLine().AppendLine("=== his files whole (ModelDump): every .ao he names and hangs off himself, the manifest, the shapes and how each part fitted");
        body.AppendLine(read is null ? "(no install to read them from)" : ModelDump.Of(read, pane.Variety, pane.Path, model).TrimEnd());

        int types = Types(body, read, pane);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"monster files: {pane.Variety?.Name ?? "?"} [{pane.Path}] - {(model.Standing is null ? "standing alone" : "on a stage")}, {actor.Fitted.Count} parts as hung, {his.Count} of {actor.Rig.Animations.Count} animations swept, {played} parts' own rigs played, {seen} of the game's entities of him read, {effects} effect files read ({textures.Count} textures whole beside it), {types} object type files whole");
        return head + Environment.NewLine + body;
    }

    /// <summary>
    /// The effects he and his parts name - particles, effect packs, trails - walked hop by hop from his own .ao files and each part's: an
    /// object's chain for the effects it names and what it extends, an effect for everything it names. Each file is listed with its size,
    /// hop and what named it; the text ones are written whole and the textures whole beside the part (<paramref name="textures"/>).
    /// Returns how many files were read.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT (v0.1.401, the Exile's Staff's lantern, which burns in the game): WandererStaffHeld.ao names
    /// <c>continuous_effect = "lamp Metadata/Items/Weapons/TwoHandWeapons/Staves/mtx/WandererStaff/fx/lamp.pet"</c> on its bone group
    /// <c>lamp</c>, and no capture held the file - the tool draws no particle at all. DECIDED from the live client: the files first, the
    /// particle drawing after, from what they say.
    /// </remarks>
    private static int Effects(StringBuilder body, Func<string, byte[]?>? read, CapturePane pane, MonsterModel actor, List<(string File, byte[] Bytes)> textures)
    {
        body.AppendLine().Append(Invariant($"=== the effects he and his parts name (particles .pet, effect packs .epk, trails .trl), hop by hop: from his own .ao files and each part's through what each extends, every effect named, and every file an effect names - each with its size, hop, what named it and its kind; the lines of an object naming an effect; the text files whole below, the textures whole beside this file to {EffectTextures >> 20} MB")).AppendLine();
        if (read is null)
        {
            body.AppendLine("(no install to read them from)");
            return 0;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Hop, string By)>();
        void Offer(string path, int hop, string by)
        {
            string clean = path.Replace('\\', '/').Trim();
            if (clean.Length > 0 && seen.Count < MostEffectFiles * 4 && seen.Add(clean))
            {
                queue.Enqueue((clean, hop, by));
            }
        }

        foreach (string one in pane.Variety?.AoFiles ?? [])
        {
            Offer(one, 0, "his own");
        }

        foreach (PartFit part in actor.Fitted)
        {
            Offer(part.File, 0, Invariant($"a part at {(part.Socket.Length > 0 ? part.Socket : "(none)")}"));
        }

        var texts = new List<(string Path, string Text, string Why)>();
        var named = new List<string>();
        long kept = 0;
        int taken = 0, effects = 0;
        body.AppendLine("  file\tpath\tbytes\thop\tnamed by\tkind\twritten");
        while (queue.Count > 0 && taken < MostEffectFiles)
        {
            (string path, int hop, string by) = queue.Dequeue();
            byte[]? bytes = read(path);
            taken++;
            string extension = Path.GetExtension(path);
            bool model = extension is ".ao" or ".aoc" or ".ot" or ".otc";
            bool effect = EffectKinds.Contains(extension, StringComparer.OrdinalIgnoreCase);
            effects += effect ? 1 : 0;
            if (bytes is not { Length: > 0 })
            {
                body.Append(Invariant($"  file\t{path}\tnot in the install\t{hop}\t{by}")).AppendLine();
                continue;
            }

            bool text = EffectTexts.Contains(extension, StringComparer.OrdinalIgnoreCase);
            string kind = text ? "text" : extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? ModelDump.Header(bytes) : "binary";
            string written = "-";
            if (!text && extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                // THE TEXTURES WHOLE, for a particle's sprite or flipbook is what it looks like - in order while the budget lasts.
                if (bytes.Length <= LargestEffectTexture && kept + bytes.Length <= EffectTextures)
                {
                    written = Invariant($"monster-files-effect-{textures.Count + 1}-{Path.GetFileName(path).ToLowerInvariant()}");
                    textures.Add((written, bytes));
                    kept += bytes.Length;
                }
                else
                {
                    written = "(past the budget)";
                }
            }

            body.Append(Invariant($"  file\t{path}\t{bytes.Length}\t{hop}\t{by}\t{kind}\t{written}")).AppendLine();
            if (!text)
            {
                continue;
            }

            string decoded = StatDescriptionFiles.Decode(bytes);

            // AN OBJECT'S CHAIN is his files' business (ModelDump writes them whole): here only the lines naming an effect, and onward
            // only to what it extends and the effects it names. An effect, a material or a graph is written whole and followed wholly.
            if (model)
            {
                foreach (string line in decoded.Split('\n'))
                {
                    if (EffectKinds.Any(kind_ => line.Contains(kind_, StringComparison.OrdinalIgnoreCase)))
                    {
                        named.Add(Invariant($"  named\t{path}\t{line.Trim()}"));
                    }
                }
            }
            else
            {
                texts.Add((path, decoded, Invariant($"hop {hop}, named by {by}")));
            }

            if (hop >= MostEffectHops)
            {
                continue;
            }

            foreach (string next in CaptureLightExtras.NamedIn(decoded))
            {
                string kindOf = Path.GetExtension(next);
                bool objectOf = kindOf is ".ao" or ".aoc" or ".ot" or ".otc";
                if (model && !objectOf && !EffectKinds.Contains(kindOf, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // FROM AN OBJECT, only what it extends - not every model, rig or sound it names; FROM AN EFFECT, everything it names.
                if (model && objectOf && !Extended(decoded, next))
                {
                    continue;
                }

                Offer(next, hop + 1, path);
                if (next.EndsWith(".ao", StringComparison.OrdinalIgnoreCase))
                {
                    Offer(next + "c", hop + 1, path);
                }
            }
        }

        if (queue.Count > 0)
        {
            body.Append(Invariant($"  (cut: {queue.Count} more files named past the {MostEffectFiles} the walk reads)")).AppendLine();
        }

        if (effects == 0)
        {
            body.AppendLine("  (no effect named by him or any part)");
        }

        foreach (string line in named)
        {
            body.AppendLine(line);
        }

        _ = CaptureLightExtras.Wholes(texts, EffectChars, LargestEffect, "effect files", body);
        return taken;
    }

    /// <summary>Whether an object's text extends <paramref name="path"/> - its <c>extends "path"</c> line, written with or without the .ao.</summary>
    private static bool Extended(string text, string path)
    {
        string bare = path.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)
            ? path[..^3] : path.EndsWith(".aoc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otc", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("extends", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(bare, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where the stage stood him: his place, the rest's box and lowest point, and the floor asked at nine points.</summary>
    private static void Staging(StringBuilder body, MonsterModel model, MonsterModel actor)
    {
        body.AppendLine().AppendLine("=== where the stage stands him (StagedModels.Staged): his origin set on the floor round the place, as the game stands him (up being minus z)");
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

        // WHICH OF THEM, BY THE RULE THE PLACE TOOK: a boss's the middle, the player's place the one nearest the ground he stood on (v0.1.375).
        body.Append(' ').AppendLine(standing.Taken);
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
    /// The lights he and his parts carry on their bones (CarriedLights): the bone of his rig that moves each, the bone its own file names, where
    /// the bind pose puts it in his own frame, its colour, radius and penumbra, and its .ao - then what was left out of them.
    /// </summary>
    private static void Carried(StringBuilder body, MonsterModel actor)
    {
        body.AppendLine().Append(Invariant($"=== the lights he carries on his bones (CarriedLights, the light panel's \"carried\"): {actor.Carried.Count} - each where the bind pose puts it in his own frame, moved by its bone's skin matrix every pose")).AppendLine();
        if (actor.Carried.Count == 0)
        {
            body.AppendLine(actor.CarriedSaid.Length > 0 ? "  " + actor.CarriedSaid : "  (his files and his parts' name no point light)");
            return;
        }

        body.AppendLine("  bone\this bone\tits own file's bone\trest\tcolour\tradius\tpenumbra\tstyle\tsource");
        foreach (CarriedLight light in actor.Carried)
        {
            string his = light.Bone >= 0 && light.Bone < actor.Rig.Bones.Count ? actor.Rig.Bones[light.Bone].Name : "(none - it stands where he does)";
            string style = light.Style == 0 ? "-" : LightStyle.Measured(light.Style) ? Invariant($"{light.Style} (flickers, measured)") : Invariant($"{light.Style} (not measured - drawn as written)");
            body.Append(Invariant($"  {light.Bone}\t{his}\t{(light.BoneName.Length > 0 ? light.BoneName : "-")}\t{At(light.Rest)}\t{At(light.Colour)}\t{light.Radius:0.###}\t{light.PenumbraDist:0.###}\t{style}\t"))
                .AppendLine(light.Source);
        }

        body.Append("  ").AppendLine(actor.CarriedSaid);
    }

    /// <summary>How long the swing is played through his opening animation for the part, in seconds.</summary>
    private const double SwingSeconds = 8;

    /// <summary>
    /// The bones of his pieces the tool swings (MonsterModel.Swings, the light panel's "swing"): each rod's top and end at the bind pose in his
    /// own frame, its length and the bone it hangs from; the measured constants; and the swing played through his opening animation from
    /// hanging straight down, tick by tick as the pane plays it - its angle from straight down each second, and the most.
    /// </summary>
    private static void Swung(StringBuilder body, MonsterModel actor, Func<ReadOnlyMemory<byte>, int, byte[]?> unpacking)
    {
        body.AppendLine().Append(Invariant($"=== the bones he swings (BoneSwing, the light panel's \"swing\"): {actor.Swings.Count} - a rod from its parent's rest to its own, stepped every {Pendulum.Tick * 1000:0.#} ms, keep {Pendulum.Keep}, gravity {Pendulum.Fall:0.000000} of the rod a tick squared, the hand's acceleration a tick late times {Pendulum.Inertia} (all MEASURED)")).AppendLine();
        if (actor.SwingSaid.Length > 0)
        {
            body.Append("  ").AppendLine(actor.SwingSaid);
        }

        if (actor.Swings.Count == 0)
        {
            return;
        }

        body.AppendLine("  bone\tits own bone\this bone it hangs from\ttop at rest\tend at rest\tlength\tsource");
        foreach (BoneSwing swing in actor.Swings)
        {
            string socket = swing.Socket >= 0 && swing.Socket < actor.Rig.Bones.Count ? actor.Rig.Bones[swing.Socket].Name : "-";
            body.AppendLine(Invariant($"  {swing.Bone}\t{swing.Name}\t{socket}\t{At(swing.Pivot)}\t{At(swing.Rest)}\t{Vector3.Distance(swing.Pivot, swing.Rest):0.###}\t{swing.Source}"));
        }

        // THE SWING PLAYED as the pane plays it, a pose a tick, through the animation a picture opens with.
        IReadOnlyList<SkeletonAnimation> moves = actor.Rig.Animations;
        int opening = moves.Count == 0 ? -1 : Math.Max(0, moves.ToList().FindIndex(one => string.Equals(one.Name, actor.Opening.Length > 0 ? actor.Opening : "idle_01", StringComparison.Ordinal)));
        if (opening < 0 || SkeletonPose.Of(actor.Rig) is not { } pose)
        {
            body.AppendLine("  (no animation to play the swing through)");
            return;
        }

        SkeletonAnimation move = moves[opening];
        AnimationTracks tracks = actor.Rig.Played(move, unpacking);
        if (!tracks.Ready || tracks.Frames <= 0f)
        {
            body.AppendLine(Invariant($"  ({move.Name}'s keyframes did not read: {tracks.Why})"));
            return;
        }

        float rate = move.Rate > 0 ? move.Rate : 30f;
        var swinger = new BoneSwinger(actor.Swings);
        var most = new float[actor.Swings.Count];
        var line = new StringBuilder();
        float frame = 0f;
        int ticks = (int)Math.Round(SwingSeconds / Pendulum.Tick);
        for (var tick = 0; tick <= ticks; tick++)
        {
            if (tick > 0)
            {
                frame += (float)(Pendulum.Tick * rate);
                if (frame > tracks.Frames)
                {
                    frame %= tracks.Frames;
                }

                swinger.Due(Pendulum.Tick);
            }

            pose.Take(tracks, frame);
            swinger.Swing(pose, tracks, frame, rate, tracks.Frames, swinging: true);
            for (var one = 0; one < actor.Swings.Count; one++)
            {
                Vector3 rod = swinger.OffsetOf(one);
                float angle = rod.LengthSquared() > 0f ? MathF.Acos(Math.Clamp(rod.Z / rod.Length(), -1f, 1f)) * 180f / MathF.PI : 0f;
                most[one] = MathF.Max(most[one], angle);
                if (tick % (int)Math.Round(1 / Pendulum.Tick) == 0)
                {
                    line.Append(Invariant($"\t{angle:0.0}"));
                }
            }
        }

        body.AppendLine(Invariant($"  played through {move.Name} ({tracks.Frames:0.#} frames at {rate:0.#} a second, round) for {SwingSeconds:0} s from hanging straight down - each rod's angle from straight down each second, degrees:{line}"));
        body.AppendLine("  the most over the run: " + string.Join(", ", Enumerable.Range(0, most.Length).Select(one => Invariant($"{actor.Swings[one].Name} {most[one]:0.0}"))));
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
            // HIS AND EVERY RIDER'S of the same name, as the pane plays them - see AnimationSkeleton.Played.
            AnimationTracks tracks = rig.Played(move, unpacking);
            if (!tracks.Ready)
            {
                body.Append("  ").Append(move.Name).Append("\t(").Append(tracks.Why.Length > 0 ? tracks.Why : "its keyframes did not read").AppendLine(")");
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
            if (part.Lost || !done.Add(part.File))
            {
                continue;
            }

            // A PART HUNG WITH NO BONES OF HIS may still bring a rig of its own - a held weapon hung rigidly in his hand (WornPiece.Held), the
            // Exile's Staff with its lantern on phys_tri_lamp_jntBnd among them (v0.1.401): its rig is played too, a skin on his rig is not.
            (AnimationSkeleton rig, string rigPath, SkinnedMesh mesh) = MonsterModels.Piece(read, part.File);
            if (part.Bones <= 0 && !rig.Ready)
            {
                continue;
            }

            body.Append("--- ").Append(part.File).Append(Invariant($"  socket {(part.Socket.Length > 0 ? part.Socket : "(none)")} ({part.Place}, depth {part.Depth}, {part.Kind}), its rig {(rigPath.Length > 0 ? rigPath : "(none named)")}"));
            if (!rig.Ready || SkeletonPose.Of(rig) is not { } rest)
            {
                body.AppendLine(rig.Ready ? " - its bone tree does not hold together" : " - did not read");
                continue;
            }

            body.Append(Invariant($": {rig.Bones.Count} bones, {rig.Animations.Count} animations"));
            if (!mesh.Ready)
            {
                body.AppendLine("; no mesh read");
                Bones(body, rig, rest);
                continue;
            }

            body.Append(Invariant($"; its mesh {mesh.Positions.Length} vertices, at rest {At(mesh.Least)} .. {At(mesh.Most)}"));
            if (SkeletonPose.Highest(mesh) >= rig.Bones.Count)
            {
                body.AppendLine(" - INDEXED PAST ITS OWN RIG: its numbers are its carrier's, so its own rig does not move it");
                continue;
            }

            body.AppendLine();
            Bones(body, rig, rest);
            var its = new HashSet<string>(rig.Animations.Select(one => one.Name), StringComparer.OrdinalIgnoreCase);
            string[] lacks = [.. his.Select(one => one.Name).Where(one => !its.Contains(one))];
            string[] own = [.. rig.Animations.Select(one => one.Name).Where(one => !named.ContainsKey(one))];
            body.Append(Invariant($"  of his {his.Count} animations it has {his.Count - lacks.Length} by name; his it lacks: {Listed(lacks)}; its own he lacks: {Listed(own)}")).AppendLine();
            body.AppendLine($"  animation\tframes\tbox at 0\tsize at 0\tsize least..most\tat frame\tits root at 0\the plays it\this bone nearest its middle at 0\tits {PhysicsBone} bones' most travel from frame 0, in its root's frame");
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

        // HIS OWN BONES ALONE, not the riders' after them nor the bones the tool swings: a piece riding his rig is nearest its own bones by
        // construction, and a swung bone stands at the identity until a pose sets it.
        int own = his.Bones.Count - his.Riders.Sum(rider => rider.Rig.Bones.Count) - his.Swung;

        // THE BONES THE ENGINE SIMULATES, for whether the piece's own animations already move them (the lantern's swing - v0.1.401).
        int[] simulated = [.. Enumerable.Range(0, rig.Bones.Count).Where(bone => rig.Bones[bone].Name.StartsWith(PhysicsBone, StringComparison.OrdinalIgnoreCase))];
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
                var physicsFirst = new Vector3[simulated.Length];
                float travel = 0f;
                int travelled = -1;
                for (var at = 0; at < frames; at++)
                {
                    float frame = keys.Frames * at / (frames - 1);
                    pose.Take(keys, frame);
                    pose.Move(mesh, local.Positions.AsSpan(), local.Normals.AsSpan());
                    (Vector3 least, Vector3 most) = Box(local.Positions);
                    float size = MathF.Max(most.X - least.X, MathF.Max(most.Y - least.Y, most.Z - least.Z));

                    // EACH SIMULATED BONE IN THE ROOT'S FRAME, so a piece carried about by its root still says whether its bone moves on it.
                    if (simulated.Length > 0 && Matrix4x4.Invert(pose.World[root], out Matrix4x4 toRoot))
                    {
                        for (var one_ = 0; one_ < simulated.Length; one_++)
                        {
                            Vector3 place = Vector3.Transform(pose.World[simulated[one_]].Translation, toRoot);
                            if (at == 0)
                            {
                                physicsFirst[one_] = place;
                            }
                            else if (Vector3.Distance(place, physicsFirst[one_]) is var moved && moved > travel)
                            {
                                (travel, travelled) = (moved, simulated[one_]);
                            }
                        }
                    }

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
                    for (var bone = 0; bone < Math.Min(own, his_.Bones.Length); bone++)
                    {
                        float apart = Vector3.Distance(his_.Bones[bone], middle);
                        if (apart < distance)
                        {
                            (best, distance) = (bone, apart);
                        }
                    }

                    nearest = best >= 0 && best < his.Bones.Count ? Invariant($"{his.Bones[best].Name} at {At(his_.Bones[best])}, {distance:0.#} away") : "-";
                }

                string physics = simulated.Length == 0 ? "-"
                    : travelled < 0 ? Invariant($"none of {simulated.Length} moves")
                    : Invariant($"{rig.Bones[travelled].Name} {travel:0.###} (of {simulated.Length})");
                rows[one] = "  " + move.Name + Invariant($"\t{keys.Frames:0.#}\t{At(first.Least)} .. {At(first.Most)}\t{sizeFirst:0.#}\t{sizeLeast:0.#}..{sizeMost:0.#}\t{leastAt:0.#}\t{At(rootFirst)}\t{(plays ? "yes" : "no")}\t{nearest}\t{physics}");
                return local;
            },
            _ => { });

        foreach (string row in rows)
        {
            body.AppendLine(row);
        }
    }

    /// <summary>
    /// A piece's own bones: each one's index, name, parent and place at rest in the piece's frame - what a bone watch in memory is held
    /// against (the bones' count and order) and what a light on a bone is put at.
    /// </summary>
    private static void Bones(StringBuilder body, AnimationSkeleton rig, SkeletonPose rest)
    {
        body.AppendLine("  bone\tindex\tname\tparent\tat rest");
        for (var bone = 0; bone < rig.Bones.Count; bone++)
        {
            int parent = bone < rest.Parents.Count ? rest.Parents[bone] : -1;
            Vector3 place = bone < rest.BindModel.Count ? rest.BindModel[bone].Translation : default;
            body.Append(Invariant($"  bone\t{bone}\t{rig.Bones[bone].Name}\t{(parent >= 0 && parent < rig.Bones.Count ? rig.Bones[parent].Name : "-")}\t{At(place)}")).AppendLine();
        }
    }

    /// <summary>The most simulation vertices, triangles and pinned numbers written for one part - CHOSEN; all are counted.</summary>
    private const int MostClothRecords = 256;

    /// <summary>How near a phys_ bone's bind a simulation vertex must rest to be taken as standing on it - CHOSEN.</summary>
    private const float OnBone = 0.5f;

    /// <summary>
    /// Each file's cloth as the engine builds it: the simulation mesh in its .smd's tail - every vertex with its four bones by name and
    /// weights, whether it is pinned, and the phys_ bone of the file's own rig its rest stands nearest - the triangles and pinned vertices,
    /// the collision spheres, capsules and ellipsoids, and the simulation its ClientAnimationController names as the engine reads it
    /// (ClothSettings, defaults starred). The body's own files first, then every part's.
    /// </summary>
    /// <remarks>
    /// ASKED FOR (v0.1.420, chains, cloth and hair decided from the live client): the engine's cloth is a mesh of particles, not the phys_
    /// bones the render mesh is skinned to - so which bone a particle drives, if any, is to be read off where they rest, side by side.
    /// </remarks>
    internal static void Cloth(StringBuilder body, Func<string, byte[]?>? read, IEnumerable<string> files)
    {
        body.AppendLine().AppendLine("=== the cloth: each file's simulation mesh (its .smd's tail - SimulationMesh) and its simulation as the engine reads it (ClothSettings; a star marks the engine's default)");
        if (read is null)
        {
            body.AppendLine("(no install to read them from)");
            return;
        }

        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            if (file.Length == 0 || !done.Add(file))
            {
                continue;
            }

            (AnimationSkeleton rig, string rigPath, SkinnedMesh mesh, Dictionary<string, string> keys) = MonsterModels.Cloth(read, file);
            SimulationMesh cloth = mesh.Simulation;
            ClothSettings settings = ClothSettings.Of(keys, out IReadOnlyList<string> unknown);
            body.Append("--- ").Append(file).Append(Invariant(
                $": its mesh {(mesh.Ready ? $"{mesh.Positions.Length} vertices" : "not read")}, tail version {cloth.TailVersion}, {cloth.Vertices.Count} simulation vertices, {cloth.Triangles.Count / 3} triangles, {cloth.Pinned.Count} pinned, {cloth.Spheres.Count} spheres, {cloth.Connections.Count} capsules, {cloth.Ellipsoids.Count} ellipsoids{(cloth.Why.Length > 0 ? " - " + cloth.Why : string.Empty)}; its rig {(rigPath.Length > 0 ? rigPath : "(none)")}")).AppendLine();
            if (keys.Count > 0)
            {
                body.Append("  simulation: ").AppendLine(settings.Say());
                if (unknown.Count > 0)
                {
                    body.Append("  keys the engine's parsers do not name: ").AppendLine(string.Join(", ", unknown));
                }
            }

            SkeletonPose? rest = rig.Ready ? SkeletonPose.Of(rig) : null;
            var phys = new List<(int Bone, Vector3 At)>();
            if (rest is not null)
            {
                for (var bone = 0; bone < rig.Bones.Count; bone++)
                {
                    if (rig.Bones[bone].Name.StartsWith(PhysicsBone, StringComparison.OrdinalIgnoreCase))
                    {
                        phys.Add((bone, rest.BindModel[bone].Translation));
                    }
                }
            }

            if (cloth.Vertices.Count > 0)
            {
                var pinned = new HashSet<int>(cloth.Pinned);
                var standing = new HashSet<int>();
                int on = 0;
                body.AppendLine("  vertex\tat rest\tbones (weight)\tpinned\tthe nearest phys_ bone of its rig at bind (distance)");
                for (var one = 0; one < cloth.Vertices.Count; one++)
                {
                    SimulationVertex vertex = cloth.Vertices[one];
                    (int Bone, Vector3 At) nearest = phys.Count == 0 ? (-1, default) : phys.MinBy(bone => Vector3.Distance(bone.At, vertex.Position));
                    float distance = nearest.Bone >= 0 ? Vector3.Distance(nearest.At, vertex.Position) : float.NaN;
                    if (distance <= OnBone)
                    {
                        on++;
                        standing.Add(nearest.Bone);
                    }

                    if (one < MostClothRecords)
                    {
                        var weighted = new StringBuilder();
                        for (var each = 0; each < 4; each++)
                        {
                            float weight = each switch { 0 => vertex.Weights.X, 1 => vertex.Weights.Y, 2 => vertex.Weights.Z, _ => vertex.Weights.W };
                            int bone = vertex.Bones[each];
                            if (weight != 0f)
                            {
                                weighted.Append(Invariant($"{(bone >= 0 && bone < rig.Bones.Count ? rig.Bones[bone].Name : $"#{bone}")} ({weight:0.###}) "));
                            }
                        }

                        body.Append(Invariant($"  vertex\t{one}\t{At(vertex.Position)}\t{weighted.ToString().TrimEnd()}\t{(pinned.Contains(one) ? "pinned" : "-")}\t{(nearest.Bone >= 0 ? $"{rig.Bones[nearest.Bone].Name} ({distance:0.###})" : "-")}")).AppendLine();
                    }
                }

                body.Append(Invariant($"  {on} of {cloth.Vertices.Count} vertices rest within {OnBone} of a phys_ bone's bind; {standing.Count} of the rig's {phys.Count} phys_ bones have one on them")).AppendLine();
                body.Append(Invariant($"  triangles, {cloth.Triangles.Count / 3}:"));
                for (var one = 0; one + 2 < cloth.Triangles.Count && one < MostClothRecords * 3; one += 3)
                {
                    body.Append(Invariant($" {cloth.Triangles[one]}-{cloth.Triangles[one + 1]}-{cloth.Triangles[one + 2]}"));
                }

                body.AppendLine();
                body.Append("  pinned: ").AppendLine(string.Join(' ', cloth.Pinned.Take(MostClothRecords)));
            }

            foreach (SimulationSphere sphere in cloth.Spheres)
            {
                body.Append(Invariant($"  sphere\t{sphere.Name}\t{At(sphere.Centre)}\tradius {sphere.Radius:0.###}\t{sphere.Unknown}")).AppendLine();
            }

            foreach ((int from, int to) in cloth.Connections)
            {
                body.Append(Invariant($"  capsule\t{from}\t{to}")).AppendLine();
            }

            foreach (SimulationEllipsoid ellipsoid in cloth.Ellipsoids)
            {
                body.Append(Invariant($"  ellipsoid\t{ellipsoid.Name}\t{string.Join(' ', ellipsoid.Floats.Select(one => one.ToString("G6", CultureInfo.InvariantCulture)))}\t{ellipsoid.Unknown}")).AppendLine();
            }
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
