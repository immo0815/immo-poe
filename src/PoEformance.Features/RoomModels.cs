using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// A room's doodads, each drawn from its own .ao and put where the room file says.
/// </summary>
/// <remarks>
/// THE PART OF A ROOM THAT CAN BE DRAWN EXACTLY. Its tiles are chosen when the area is generated -
/// see RoomLayout - but its doodads are named outright: a model, a position, an angle, a scale. Each
/// model is the same .ao walk a monster or an item takes, read ONCE however many times the room
/// places it, and joined into one mesh at each place.
///
/// A POSITION COUNTS CELLS, 23 to a tile. The file does not say so in words; the reference does in
/// its types: poe_data_tools' <c>arm</c> parser reads a doodad's x and y as <c>u32</c> and a decal's
/// as <c>f32</c>, and a whole number is a place on the grid every .tdt's walkability blocks are
/// written in, not a length. The first real room agreed - read as world units its furniture piled
/// onto a fifth of one tile, read as cells it filled the room. <see cref="Spread"/> still reports how
/// far the doodads reach against the room's size in cells, so a room that breaks the rule says so.
///
/// NOTHING HERE IS ANIMATED - a doodad's rig, where it has one, stays in its rest pose - and there
/// are caps, because a room of three hundred trees is three hundred copies of a tree to rasterise.
/// </remarks>
public static class RoomModels
{
    /// <summary>
    /// Most doodads placed unless the tile book's slider says otherwise. Beyond it the rest are counted and left out.
    /// </summary>
    /// <remarks>
    /// A CHOICE AND NOT A CONSTANT, because what the cap protects - turning the picture without a
    /// stutter - depends on the machine, and what it costs depends on the room: the ship in Port's
    /// boss room places 596 doodads and the old fixed 400 left a third of it out.
    /// </remarks>
    public const int UsualDoodads = 500;

    /// <summary>The slider's ends.</summary>
    public const int LeastDoodads = 100;

    /// <summary>See <see cref="LeastDoodads"/>.</summary>
    public const int MostDoodads = 5000;

    /// <summary>
    /// No cap: every doodad and every tile piece a room names is placed, whatever triangles they come to - a stage's.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT after model-panes.txt (v0.1.202) counted Port's boss room: 2.25 million triangles,
    /// the ship alone 355,726 of the usual 500,000, so the usual cap laid 56 of its 554 doodads and left out six of its 29
    /// tile pieces - all 48 water planes among what went, which is why the harbour stood drained. A stage is one picture
    /// of one boss, and its cap is what the room needs. Times <see cref="TrianglesPerDoodad"/> it still fits a long.
    /// </remarks>
    public const int EveryDoodad = int.MaxValue;

    /// <summary>
    /// Triangles allowed per doodad allowed. The renderer is on the processor; past the product, turning stutters.
    /// </summary>
    /// <remarks>
    /// The ratio the two fixed caps had - 400 doodads, 400,000 triangles - kept, so raising the
    /// doodads raises the triangles with them; a cap on one alone would stop the slider short.
    /// </remarks>
    public const int TrianglesPerDoodad = 1000;

    /// <summary>The object a room names beside a doodad the game places and never draws.</summary>
    public const string InvisibleStub = "Metadata/MiscellaneousObjects/DoodadInvisible";

    /// <summary>The folder of the level editor's own pieces - walk blockers, markers, pins.</summary>
    public const string ToolFolder = "Metadata/Terrain/Doodads/Tools/";

    /// <summary>
    /// Whether a doodad is one of the level editor's tools rather than part of the scene.
    /// </summary>
    /// <remarks>
    /// TWO WORDS, ONE THE GAME'S AND ONE A FOLDER'S. A room names an object beside each doodad, and
    /// the yellow box in the deserted 1open_01.arm - Blocker_Walk_3_1_01, its colour the vertex
    /// colours of VertexColourTransparentc.mat at a quarter of their alpha - is placed as
    /// <see cref="InvisibleStub"/>: the game says outright that it is not drawn. Its siblings in
    /// <see cref="ToolFolder"/> are not always placed that way - invisible_blocker.ao as a plain
    /// Doodad, marker4.ao as a BossArenaBlocker, the pins as league markers - but they are the same
    /// kind of thing: walk blockers in a flat vertex colour and markers wearing iconsc.mat, a
    /// camera-facing sheet of editor icons hung 350 units over the pin. That half is a rule on a
    /// folder's name, not the game's word, which is why it is a choice the book offers rather than
    /// a filter it applies.
    /// </remarks>
    public static bool IsTool(RoomDoodad doodad)
        => string.Equals(doodad.Stub, InvisibleStub, StringComparison.OrdinalIgnoreCase)
            || doodad.Ao.StartsWith(ToolFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cells to a tile - the grid a room's doodad positions and a tile's walkability are written in.</summary>
    public const int CellsPerTile = 23;

    /// <summary>World units in one cell.</summary>
    public static float CellSize => TileModels.Side / CellsPerTile;

    /// <summary>
    /// How far a room's doodads reach, and what that says about the unit.
    /// </summary>
    /// <param name="Most">The largest x and y any doodad has.</param>
    /// <param name="Cells">The room's size in cells - 23 to a tile.</param>
    /// <param name="NotCells">True when a doodad lies past the room's edge read as cells - a room the rule does not hold for.</param>
    public readonly record struct Spread(Vector2 Most, Vector2 Cells, bool NotCells)
    {
        /// <summary>The line the view prints.</summary>
        public string Say => string.Create(
                CultureInfo.InvariantCulture,
                $"doodads reach x {Most.X}, y {Most.Y}; the room is {Cells.X} x {Cells.Y} cells")
            + (NotCells ? " - past its edge, so this room's positions are not cells" : string.Empty);
    }

    /// <summary>How far the doodads reach against the room's size. See <see cref="Spread"/>.</summary>
    public static Spread SpreadOf(RoomLayout room)
    {
        ArgumentNullException.ThrowIfNull(room);

        var most = Vector2.Zero;
        foreach (RoomDoodad one in room.Doodads)
        {
            most = Vector2.Max(most, new Vector2(one.X, one.Y));
        }

        var cells = new Vector2(room.Width * CellsPerTile, room.Height * CellsPerTile);
        return new Spread(most, cells, most.X > cells.X || most.Y > cells.Y);
    }

    /// <summary>
    /// The room's doodads as one model, or why there is none. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="path">The room's <c>.arm</c>.</param>
    /// <param name="shaded">Whether each material's shader graphs are read - see MonsterModels.Shaded.</param>
    /// <param name="doodads">Most doodads placed - see <see cref="UsualDoodads"/>; zero or less is the usual.</param>
    /// <param name="tools">Whether the level editor's tools are placed too - see <see cref="IsTool"/>.</param>
    /// <param name="progress">Where the build says how far it has got, or null - see <see cref="ModelProgress"/>.</param>
    /// <param name="heights">How high a doodad whose line carries a height is set - see <see cref="DoodadHeight"/>.</param>
    public static MonsterModel Of(
        Func<string, byte[]?>? read,
        string? path,
        bool shaded = false,
        int doodads = UsualDoodads,
        bool tools = false,
        ModelProgress? progress = null,
        DoodadHeight heights = DoodadHeight.File)
    {
        if (read is null)
        {
            return MonsterModel.None with { Why = "no install to read" };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return MonsterModel.None with { Why = "no room to read" };
        }

        // COUNTED, as a tile's and a monster's files are: a room is the heaviest thing the book
        // opens - every distinct doodad's .ao walk, materials and textures - and the line under
        // the picture should say so rather than read as nothing.
        long bytes = 0;
        var files = 0;
        byte[]? Counted(string one)
        {
            byte[]? got = read(one.Replace('\\', '/').Trim());
            if (got is not null)
            {
                Interlocked.Add(ref bytes, got.Length);
                Interlocked.Increment(ref files);
            }

            return got;
        }

        RoomLayout room = RoomLayout.Read(Counted(path));
        if (!room.Ready)
        {
            return MonsterModel.None with { Why = $"the room did not read: {path} - {room.Why}", Bytes = bytes, Files = files };
        }

        if (room.Doodads.Count == 0)
        {
            return MonsterModel.None with { Why = $"the room places no doodads: {path}", Bytes = bytes, Files = files };
        }

        var paints = new MonsterModels.Paints { Progress = progress };
        var pile = new ModelPile();
        var lights = new RoomLights.Gathered(Counted);
        Doodads laid = Lay(room, Counted, paints, pile, doodads, tools, beyond: null, heights, lights);

        SkinnedMesh joined = Watered(SkinnedMesh.Joined(pile.Joins), pile.Modes);
        if (!joined.Ready)
        {
            return MonsterModel.None with
            {
                Why = laid.FirstMissing.Length > 0
                    ? $"none of the room's doodads drew; the first: {laid.FirstMissing}"
                    : laid.Hidden > 0 && laid.Placed == 0
                        ? $"the room places only the level editor's tools, which are hidden: {path}"
                        : $"the room's doodads hold no geometry: {path}",
                Bytes = bytes,
                Files = files,
            };
        }

        var said = new List<string> { SpreadOf(room).Say };
        said.AddRange(laid.Said());
        MonsterModel model = Piled(joined, pile, path, laid.Models) with
        {
            Parts = laid.Placed,
            Bytes = bytes,
            Files = files,
            Move = string.Join("; ", said),
            Lights = lights.Lights,
            LightsSaid = lights.Said(),
            DoodadFiles = laid.Files,
        };

        // THE GRAPHS ONCE, OVER THE JOINED ROOM: every doodad's materials are in the one list, and a
        // material two doodads share is compiled once through the shared cache.
        return shaded ? MonsterModels.Shaded(Counted, model, paints) : model;
    }

    /// <summary>What laying a room's doodads came to.</summary>
    internal sealed record Doodads(
        int Placed, int Missing, int Capped, int Hidden, string FirstMissing, int Most, long MostTriangles, IReadOnlyCollection<MonsterModel> Models)
    {
        /// <summary>Doodads not loaded at all: every one of their file's places was asked and none could be seen - see <see cref="DoodadSieve"/>.</summary>
        public int Unloaded { get; init; }

        /// <summary>Doodads loaded for another place of their file, and not laid at this one because it could not be seen there.</summary>
        public int Unseen { get; init; }

        /// <summary>Every doodad file and what became of its lines, in the order first named - see <see cref="DoodadFile"/>.</summary>
        public IReadOnlyList<DoodadFile> Files { get; init; } = [];

        /// <summary>What the room's wobble cores came to - see RoomSways - or empty where it has none.</summary>
        public string Swayed { get; init; } = string.Empty;

        /// <summary>The lines under the picture about what did not draw, and why.</summary>
        public IEnumerable<string> Said()
        {
            if (Swayed.Length > 0)
            {
                // WHAT THE SWAY IS AND WHERE IT COMES FROM, said as the files give it - the rule a reading, the motion the game's.
                yield return "sways (on the card; the processor's picture stands still): " + Swayed
                    + " - the zone, the bone and the animation READ from the files; which doodads a sector sways is the files' reading"
                    + " (the zone holds the line, its type sets no no_wobble), held against the game's own list by wobble.txt's census";
            }

            if (Missing > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{Missing} doodads did not draw; the first: {FirstMissing}");
            }

            if (Hidden > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"{Hidden} of the level editor's tools hidden - walk blockers and markers the game does not draw; \"tools\" shows them");
            }

            if (Unloaded + Unseen > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"{Unloaded + Unseen} doodads left out unseen - {Unloaded} never loaded, {Unseen} loaded for another place and not laid at this one; \"leave out the hidden\" lays them");
            }

            if (Capped > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"{Capped} more left out to keep it turnable - {Most} doodads or {MostTriangles} triangles; the doodads slider raises it");
            }
        }
    }

    /// <summary>
    /// A room's doodads laid into a pile, each where the room puts it and then by <paramref name="beyond"/> - the room as laid in an area - or nowhere further.
    /// </summary>
    /// <remarks>
    /// ONE LOAD PER MODEL, keyed by its file: a room places the same tree or rock dozens of times. AND
    /// ONE TEXTURE CACHE FOR ALL OF THEM, because two different doodads sharing a material - the rock
    /// and the stump on one sheet - are one decode and one upload, not one each.
    ///
    /// WITH A SIEVE, EVERY PLACE IS ASKED BEFORE ANYTHING IS LOADED - a box <see cref="DoodadSieve.Margin"/>
    /// round where it stands, its scale applied - and a file none of whose places passes is never read.
    /// A file that is read has every place asked again with its own box, so a place the margin let
    /// through is still left out where the doodad itself cannot be seen.
    /// </remarks>
    internal static Doodads Lay(
        RoomLayout room,
        Func<string, byte[]?> counted,
        MonsterModels.Paints paints,
        ModelPile pile,
        int doodads,
        bool tools,
        Func<RoomDoodad, Matrix4x4>? beyond,
        DoodadHeight heights = DoodadHeight.File,
        RoomLights.Gathered? lights = null,
        DoodadSieve? sieve = null,
        IReadOnlyList<(RoomDoodad One, Matrix4x4 Place)>? also = null)
    {
        // ONE TALLY PER FILE, which also holds its model once loaded: a room places the same tree dozens of times.
        var files = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        var named = new List<Tally>();
        float size = CellSize;
        long triangles = 0;
        int placed = 0, missing = 0, capped = 0, hidden = 0, unloaded = 0, unseen = 0;
        int mostDoodads = doodads > 0 ? doodads : UsualDoodads;
        long mostTriangles = (long)mostDoodads * TrianglesPerDoodad;
        string firstMissing = string.Empty;

        // THE ROOM'S WOBBLE CORES, and which of its lines each sways - see RoomSways. Each core's own line says where it stands.
        RoomSways? sways = RoomSways.Of(room, counted, WobbleRig.Unpacking);
        var coreAt = new Matrix4x4?[sways?.Count ?? 0];

        // WHERE EACH GOES FIRST, and whether it could be seen there at all.
        var laid = new List<(RoomDoodad One, Matrix4x4 Place, Tally File, int Sway)>(room.Doodads.Count + (also?.Count ?? 0));
        void Take(RoomDoodad one, Matrix4x4 place, int sway)
        {
            // THE LIGHTS WHETHER OR NOT THE DOODAD DRAWS: a light can be all a doodad is -
            // grimtangle_ambientlight.ao has no mesh - and a doodad left out to keep the picture
            // turnable, or because the camera cannot see it, still lights the room in the game.
            lights?.Add(one.Ao, place);
            if (!files.TryGetValue(one.Ao, out Tally? file))
            {
                file = new Tally(one.Ao, place.Translation);
                files[one.Ao] = file;
                named.Add(file);
            }

            file.Lines++;
            laid.Add((one, place, file, sway));
            if (sieve is not null && !file.Wanted)
            {
                var half = new Vector3(sieve.Margin * (one.Scale > 0f ? one.Scale : 1f));
                file.Wanted = sieve.Kept(place.Translation - half, place.Translation + half);
            }
        }

        foreach (RoomDoodad one in room.Doodads)
        {
            if (one.Ao.Length == 0)
            {
                continue;
            }

            float scale = one.Scale > 0f ? one.Scale : 1f;
            Matrix4x4 place = Matrix4x4.CreateScale(scale)
                * one.Turning
                * Matrix4x4.CreateTranslation(one.X * size, one.Y * size, 0f);
            if (beyond is not null)
            {
                // THE ROOM AS LAID SETS ITS OWN HEIGHT, from the area's ground under the doodad.
                place *= beyond(one);
            }
            else if (heights != DoodadHeight.Ground && one.Height is { } height)
            {
                // ON ITS OWN THE ROOM'S GROUND IS NOUGHT, so the height and the ground plus it are one.
                place *= Matrix4x4.CreateTranslation(0f, 0f, height);
            }

            // A CORE'S PLACE WHETHER OR NOT IT DRAWS - its model is a plane no one sees; its place is what the sway turns about.
            if (sways?.CoreOf(one) is >= 0 and int core && sways.RigOf(core) is not null)
            {
                coreAt[core] = place;
            }

            if (!tools && IsTool(one))
            {
                hidden++;
                continue;
            }

            Take(one, place, sways?.GroupOf(one) ?? -1);
        }

        // THE TILES' OWN DOODADS AFTER THE ROOM'S, placed already by whoever laid the tiles: where the doodads slider
        // runs out it is a fill tile's bones that are left out, not the room's own props.
        foreach ((RoomDoodad one, Matrix4x4 place) in also ?? [])
        {
            if (one.Ao.Length == 0)
            {
                continue;
            }

            if (!tools && IsTool(one))
            {
                hidden++;
                continue;
            }

            Take(one, place, -1);
        }

        // EVERY FILE THAT WILL BE LAID, ON EVERY CORE, before any is laid: one model per file, through the shared cache (Paints), so
        // a sheet two doodads wear is still one decode. The files are the ones the loop below would load on meeting their first
        // line - all of them where nothing is sieved, the wanted ones where something is - and the loop then lays them in the
        // lines' order as it always did, so the picture is the same one.
        Tally[] loading = [.. named.Where(file => sieve is null || file.Wanted)];
        using (ModelProgress.Step reading = ModelProgress.Begin(paints.Progress, "reading the doodad files", loading.Length))
        {
            Parallel.ForEach(loading, file =>
            {
                file.Model = MonsterModels.OfFiles(counted, [file.Ao], wearing: true, paints);
                reading.Advance();
            });
        }

        // EACH CORE INTO THE PILE where its line stood, its lines counted - a line's group then names its place among the pile's cores.
        var groupOf = new int[coreAt.Length];
        int firstCore = pile.Cores.Count;
        for (var core = 0; core < coreAt.Length; core++)
        {
            groupOf[core] = -1;
            if (coreAt[core] is { } at && sways!.RigOf(core) is { } rig)
            {
                groupOf[core] = pile.Cores.Count;
                pile.Cores.Add(new RoomSway(rig, at, laid.Count(one => one.Sway == core)));
            }
        }

        using ModelProgress.Step step = ModelProgress.Begin(paints.Progress, "laying the doodads", laid.Count);
        foreach ((RoomDoodad one, Matrix4x4 place, Tally file, int sway) in laid)
        {
            step.Advance();
            if (sieve is not null && !file.Wanted)
            {
                unloaded++;
                file.Unseen++;
                continue;
            }

            MonsterModel model = file.Model ??= MonsterModels.OfFiles(counted, [one.Ao], wearing: true, paints);
            if (!model.Ready)
            {
                missing++;
                file.Missing++;
                if (firstMissing.Length == 0)
                {
                    firstMissing = $"{one.Ao} - {model.Why}";
                }

                continue;
            }

            if (sieve is not null)
            {
                (Vector3 low, Vector3 high) = Boxed(model.Mesh.Least, model.Mesh.Most, place);
                if (!sieve.Kept(low, high))
                {
                    unseen++;
                    file.Unseen++;
                    continue;
                }
            }

            if (placed >= mostDoodads || triangles + model.Mesh.Triangles > mostTriangles)
            {
                capped++;
                file.Capped++;
                continue;
            }

            pile.Add(model, place, one.Ao, sway >= 0 ? groupOf[sway] : -1);
            triangles += model.Mesh.Triangles;
            placed++;
            file.Laid++;
        }

        return new Doodads(
            placed, missing, capped, hidden, firstMissing, mostDoodads, mostTriangles, [.. named.Where(one => one.Model is not null).Select(one => one.Model!)])
        {
            Unloaded = unloaded,
            Unseen = unseen,
            Swayed = sways is null ? string.Empty
                : string.Join("; ", new[] { RoomSways.Counted([.. pile.Cores.Skip(firstCore)]), sways.Said }.Where(one => one.Length > 0)),
            Files = [.. named.Select(one => one.Said())],
        };
    }

    /// <summary>One doodad file's lines as they are laid, and its model once loaded - see <see cref="DoodadFile"/>.</summary>
    private sealed class Tally(string ao, Vector3 first)
    {
        /// <summary>The file, as its first line names it.</summary>
        public string Ao => ao;

        public MonsterModel? Model;
        public bool Wanted;
        public int Lines;
        public int Laid;
        public int Missing;
        public int Capped;
        public int Unseen;

        public DoodadFile Said() => Model is { Ready: true } model
            ? new DoodadFile(ao, Lines, Laid, Missing, Capped, Unseen, string.Empty, first)
            {
                Triangles = model.Mesh.Triangles,
                Least = model.Mesh.Least,
                Most = model.Mesh.Most,
                Materials = model.ShapeMaterials,
                Modes = model.Modes,
                Rig = model.Rig_,
                Animations = [.. model.Rig.Animations.Select(one => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture, $"{one.Name} ({one.Rate} fps, {one.Tracks} tracks, kind {one.Kind})"))],
            }
            : new DoodadFile(ao, Lines, Laid, Missing, Capped, Unseen, Model?.Why ?? string.Empty, first);
    }

    /// <summary>A box through a transform, by its eight corners - a turned box's corners are not the old corners turned.</summary>
    internal static (Vector3 Least, Vector3 Most) Boxed(Vector3 least, Vector3 most, Matrix4x4 place)
    {
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        for (var one = 0; one < 8; one++)
        {
            Vector3 put = Vector3.Transform(
                new Vector3((one & 1) == 0 ? least.X : most.X, (one & 2) == 0 ? least.Y : most.Y, (one & 4) == 0 ? least.Z : most.Z),
                place);
            low = Vector3.Min(low, put);
            high = Vector3.Max(high, put);
        }

        return (low, high);
    }

    /// <summary>
    /// How long an edge of a water shape may be once it is split for its waves to lift it, in world units - CHOSEN.
    /// </summary>
    /// <remarks>
    /// THE RIPPLES' CELLS RUN ABOUT 210, 70 AND 23 UNITS ACROSS THE WIND (ShadeProgram.Ocean over its noise,
    /// four and a half times that along it at the engine's former directionness), the first carrying nearly
    /// all the height: edges of 48 follow the first two and leave the finest to the light's own normal. A WaterPlane3x3 - 750 a side, its diagonal 1,060 - is halved five times,
    /// 2,048 triangles; Port's harbour's 48 such come to under 100,000.
    /// </remarks>
    public const float WaterEdge = 48f;

    /// <summary>
    /// A joined room with its water shapes split fine enough for their waves to lift them - see SceneLight.WaterLift; the room as it was where none is water.
    /// </summary>
    /// <param name="joined">The room joined.</param>
    /// <param name="modes">Per shape, its blend word - "water" for an Ocean material (Paints.Modes).</param>
    internal static SkinnedMesh Watered(SkinnedMesh joined, IReadOnlyList<string> modes)
    {
        var fine = new bool[joined.Shapes.Count];
        var any = false;
        for (var shape = 0; shape < fine.Length && shape < modes.Count; shape++)
        {
            fine[shape] = modes[shape] == "water";
            any |= fine[shape];
        }

        return any ? joined.Refined(fine, WaterEdge) : joined;
    }

    /// <summary>What a laid tile's ground is named as the source of its shapes - LaidRoomModels' <c>"ground of " + tile</c>.</summary>
    internal const string GroundSource = "ground of ";

    /// <summary>
    /// A room's shore from its water and its ground, and - once its materials are compiled - what stands in the sea: every shape whose
    /// program draws the sea's spray on it (ShadeProgram.Sprays). What may stand in the waves' way is every solid shape that is neither
    /// water, the ground nor the beach's sheet - the ship's hull among them (CHOSEN, see ShoreField's remarks). See ShoreField.Of.
    /// </summary>
    internal static ShoreField? ShoreOf(SkinnedMesh mesh, IReadOnlyList<string> modes, IReadOnlyList<string> sources, IReadOnlyList<ShadeProgram?>? shades)
        => ShoreField.Of(
            mesh,
            shape => shape < modes.Count && modes[shape] == "water",
            shape => shape < sources.Count && sources[shape].StartsWith(GroundSource, StringComparison.Ordinal),
            shades is null ? null : shape => shape < shades.Count && shades[shape] is { Sprays: true },
            shape => shape < modes.Count && shape < sources.Count
                && MaterialBlends.Of(modes[shape]) is MaterialBlend.Opaque or MaterialBlend.Cutout
                && !sources[shape].StartsWith(GroundSource, StringComparison.Ordinal)
                && sources[shape] != BeachSource);

    /// <summary>What the beach waves' sheet is named as the source of its shape - see <see cref="Piled"/>.</summary>
    internal const string BeachSource = "the beach waves' sheet over the shore's ground";

    /// <summary>The beach's sheet's blend word - mixed (MaterialBlend.Of), drawn over the solid part by its program (ShadeProgram.Beaches).</summary>
    internal const string BeachMode = "beach";

    /// <summary>A pile joined into a room's model, with the shaders and meshes of the models in it.</summary>
    /// <remarks>
    /// THE ENGINE'S BEACH WAVES join a room with a shore: a sheet over its ground in the shore's band (ShoreField.Beach), wearing
    /// the engine's own graph (ShadeProgram.BeachGraph) - once, so a stage piling a room that brings its sheet keeps the one.
    ///
    /// A STAGE BRINGS ITS SHORE ALREADY MADE (<paramref name="shore"/>): the monster joined to it is neither water nor ground, so the
    /// shore over the joined mesh is the stage's own, and working it out again over millions of triangles was thrown away by
    /// StagedModels.Staged, which keeps the stage's. Its beach sheet came with it, or its shore makes none.
    /// </remarks>
    /// <param name="shore">The shore already made for what is piled, which is then neither made again nor given a beach sheet - or null to make it.</param>
    internal static MonsterModel Piled(SkinnedMesh joined, ModelPile pile, string path, IEnumerable<MonsterModel> models, ShoreField? shore = null)
    {
        MonsterModel[] all = [.. models];
        bool made = shore is null;
        shore ??= ShoreOf(joined, pile.Modes, pile.Sources, null);
        if (made && shore is not null && !pile.Sources.Contains(BeachSource, StringComparer.Ordinal) && shore.Beach() is { } sheet)
        {
            joined = SkinnedMesh.Joined([new MeshJoin(joined, null, null, null), new MeshJoin(sheet, null, null, null)]);
            pile.Joined(sheet, BeachMode, ShadeProgram.BeachGraph, BeachSource);
        }

        return new MonsterModel(joined, pile.Skins.FirstOrDefault(one => one is not null), path, string.Empty, string.Empty)
        {
            Skins = pile.Skins,
            Modes = pile.Modes,
            ShapeMaterials = pile.Wearing,

            // CARRIED LIKE THE MATERIALS, so the dump's "tex" column names each doodad's colour map;
            // left out, every room shape read "tex -" whether it was textured or not.
            ShapeTextures = pile.Textures,
            ShapeSources = pile.Sources,

            // THE SWAY OF A SHIP AND ITS DECK - see RoomSways; every shape at -1 where nothing sways, so empty then.
            ShapeSways = pile.Cores.Count > 0 ? pile.Sways : [],
            Sways = pile.Cores,

            // THE SHORE FROM THE ROOM'S OWN WATER AND GROUND - the engine makes its own at run time; see ShoreField. What stands
            // in the sea joins it once the materials are compiled (MonsterModels.Shaded).
            Shore = shore,
            BodyLeast = joined.Least,
            BodyMost = joined.Most,
            Kind = ModelKind.Room,
            Shaders = [.. all.SelectMany(one => one.Shaders).Distinct(StringComparer.OrdinalIgnoreCase)],
            Meshes = [.. all.Where(one => one.Ready).Select(one => new MeshNamed(one.Mesh_, one.BodyFacts)).Distinct()],
        };
    }
}

/// <summary>
/// Which of a room's doodads are laid at all: asked with a box round each place before anything is loaded, and with the doodad's own box once it is.
/// </summary>
/// <param name="Kept">Whether anything in a box, in the pile's frame, may be seen - false leaves what is in it out.</param>
/// <param name="Margin">How far round a place the box reaches before the doodad's own size is known, in world units, times its scale.</param>
internal sealed record DoodadSieve(Func<Vector3, Vector3, bool> Kept, float Margin);

/// <summary>
/// One doodad file of a room and what became of its lines - laid, not drawn, left out by the cap or unseen - with its mesh's facts where it loaded.
/// </summary>
/// <remarks>
/// PER FILE, FOR THE CAPTURE (CaptureReport.Panes): Port's harbour tile brings 31 lines of one water plane, the Monster Book's
/// stage showed no water, and the line under the picture said only how many doodads in all were left out and which ONE did not
/// draw first - not whether the water was among them, nor which cause it met.
/// </remarks>
/// <param name="Ao">The file, as the lines name it.</param>
/// <param name="Lines">How many lines name it, the room's and its tiles' together - the level editor's tools not counted where hidden.</param>
/// <param name="Laid">How many were laid into the picture.</param>
/// <param name="Missing">How many were not because the file did not draw - see <paramref name="Why"/>.</param>
/// <param name="Capped">How many were left out to keep the picture turnable - see RoomModels.UsualDoodads.</param>
/// <param name="Unseen">How many were left out because the game's camera could not see them there, loaded or not.</param>
/// <param name="Why">Why the file did not draw, or empty.</param>
/// <param name="First">Where its first line puts it, in the picture's frame.</param>
public sealed record DoodadFile(string Ao, int Lines, int Laid, int Missing, int Capped, int Unseen, string Why, Vector3 First)
{
    /// <summary>Its mesh's triangles, where it loaded.</summary>
    public int Triangles { get; init; }

    /// <summary>Its mesh's box in its own frame, where it loaded.</summary>
    public Vector3 Least { get; init; }

    /// <summary>See <see cref="Least"/>.</summary>
    public Vector3 Most { get; init; }

    /// <summary>Each shape's material, where it loaded.</summary>
    public IReadOnlyList<string> Materials { get; init; } = [];

    /// <summary>Each shape's blend as its material spells it, where it loaded - see MonsterModel.Modes.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>The skeleton its .ao names, where it names one - a doodad laid in a room stays in its rest pose (RoomModels).</summary>
    public string Rig { get; init; } = string.Empty;

    /// <summary>Its skeleton's animations, each with its rate, tracks and kind - what the game may play on it.</summary>
    public IReadOnlyList<string> Animations { get; init; } = [];
}
