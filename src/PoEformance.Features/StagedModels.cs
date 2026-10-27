using System.Globalization;
using System.Numerics;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>A place a room names for its boss, in the room picture's own frame - see LaidRoomModels.BossSpotOf.</summary>
/// <param name="At">Where, in the picture's x and y.</param>
/// <param name="Turn">The way the boss faces there, in radians, as a doodad's angle turns its model.</param>
/// <param name="Tag">The point's tag - "mapboss", "force:mapboss", "mapboss1".</param>
/// <param name="Others">How many other boss points the room names, which a stage passes over.</param>
public sealed record RoomSpot(Vector2 At, float Turn, string Tag, int Others);

/// <summary>
/// A monster standing in the middle of a terrain tile - its arena, or any tile the book is pointed at - or of a room as the area laid it, as one model.
/// </summary>
/// <remarks>
/// A ROOM IS A STAGE ONLY AS THE AREA LAID IT. Its file names slot types, not tiles - the tiles are
/// chosen when the area is generated (RoomLayout) - so the file alone is doodads over nothing, and a
/// monster set on that stood on nothing. The area the player stands in knows every tile it laid, and
/// LaidRoomModels builds the room from them, floor and all; the key names the place and the grid it
/// was found in (RoomKey.Laid), and a grid that is gone is a stage that is gone, said under the picture.
///
/// ONE MESH, SO THE RENDERER NEED NOT KNOW. The picture draws a mesh and, for an animation, an
/// array of where each vertex is now; a room is already a mesh joined out of many. So the stage
/// and the monster are joined the way a room's doodads are - the tile first, the monster after it
/// at its place - and what moves is the monster's slice of the posed arrays alone: the portrait
/// poses the monster's own mesh and writes the result, placed, over its vertices, while the tile's
/// keep the positions the file gave them. Blends, programs, skins and the probe's sources are per
/// shape and come along in the join, on the processor and on the card alike.
///
/// THE MONSTER IS SCALED THE WAY THE GAME SCALES IT: a variety's ModelSizeMultiplier is a
/// percentage the game draws the mesh at, on the same tiles, so on a stage counted in world units
/// the mesh is that much larger or smaller - the same number ModelFloor.TileOn shrinks the floor's
/// squares by under a bare picture.
///
/// AND IT STANDS ON THE FLOOR THE PLAYER SEES, at the tile's middle. Not on the tile file's own
/// "ground" block: on a boss arena that block is the earth under a stone floor built of props, and
/// the first monster set on it stood a storey under the arena. The floor is the highest surface that
/// faces up under the middle, over every solid shape of the tile - props and ground alike, cut-out
/// and mixed shapes (foliage, decals, ground layers) left out - since the game's camera looks down
/// and what it sees at that point is what is stood on. The files' up is minus z. Where nothing faces
/// up under the middle the tile's lowest point stands in, and the line under the picture says so.
/// </remarks>
public static class StagedModels
{
    /// <summary>What the monster's shapes are called in the probe's list, beside the tile's own path.</summary>
    public const string ActorSource = "the monster";

    /// <summary>How far from straight up a surface may face and still be a floor: the z of its normal, the files' up being minus z.</summary>
    /// <remarks>Half, which is sixty degrees from level: a ramp or a stair tread passes, a wall or a cliff face does not.</remarks>
    private const float FacesUp = -0.5f;

    /// <summary>
    /// The monster on its stage - or the monster alone where there is no stage to stand on, with
    /// <see cref="MonsterModel.StageSaid"/> saying why. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="one">The monster.</param>
    /// <param name="stage">The stage as a <see cref="TileKey"/> string, or a room's <see cref="RoomKey"/> string laid in an area, or empty for the monster alone.</param>
    /// <param name="wearing">Whether the monster's attachments are joined on - see MonsterModels.Of.</param>
    /// <param name="shaded">Whether the monster's material graphs are read; the stage's always are.</param>
    /// <param name="progress">Where the build says how far it has got, or null.</param>
    /// <param name="terrain">The current area's grid, asked when a room is to be laid from it, or null - see <see cref="LaidRoom"/>.</param>
    /// <param name="keep">Where the pane keeps its last stage, so the next monster on it is not built on a new one - or null to build it anew.</param>
    public static MonsterModel Of(
        Func<string, byte[]?>? read,
        MonsterVariety? one,
        string stage,
        bool wearing = true,
        bool shaded = false,
        ModelProgress? progress = null,
        Func<TerrainGrid?>? terrain = null,
        StageKeep? keep = null)
    {
        MonsterModel actor = MonsterModels.Of(read, one, wearing, shaded, progress);
        if (string.IsNullOrWhiteSpace(stage) || read is null || !actor.Ready)
        {
            return actor;
        }

        string path = PathOf(stage);
        MonsterModel Built()
        {
            if (TileBook.IsRoom(path))
            {
                return LaidRoom(read, stage, terrain, progress);
            }

            TileKey key = TileKey.Read(stage);
            return TileModels.Of(
                read,
                key.Path,
                ground: key.Ground,
                walls: key.Walls,
                shaded: true,
                swaps: key.Tileset.Length > 0 ? TilesetIndex.Overrides(read, key.Tileset) : null,
                tileset: key.Tileset.Length > 0 ? TilesetIndex.Short(key.Tileset) : string.Empty,
                progress: progress);
        }

        MonsterModel tile;
        if (keep is null)
        {
            tile = Built();
        }
        else
        {
            tile = keep.Tile(stage, Built, out bool kept);
            if (kept)
            {
                progress?.Note("the stage was the one this pane built last, kept - only the monster was built");
            }
        }

        if (!tile.Ready)
        {
            return actor with { Stage = path, StageSaid = "no stage: " + tile.Why };
        }

        return Staged(actor, tile, one?.ModelSize ?? 0, path);
    }

    /// <summary>The path a stage key names, before its words.</summary>
    public static string PathOf(string stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        int mark = stage.IndexOf(TileKey.Mark, StringComparison.Ordinal);
        return mark >= 0 ? stage[..mark] : stage;
    }

    /// <summary>
    /// A room as the area laid it, or built from its tileset, for a stage - or why there is none. Never throws.
    /// </summary>
    /// <remarks>
    /// WHOLE, WHATEVER THE CAMERA SEES: the tile book may leave out what the game's camera cannot see from
    /// anywhere the player stands, which is right for holding a room against the game and wrong for a
    /// stage, where the monster is looked at from every side. The doodad heights line against the area's
    /// entities is left out for the same reason - this is a stage, not a survey.
    ///
    /// AND WHOLE WHATEVER THE KEY'S CAP: every doodad and tile piece is placed (RoomModels.EveryDoodad). The
    /// usual cap left Port's harbour without its 48 water planes and six of its tile pieces.
    ///
    /// TWO ROADS TO A FLOOR: the area the player stands in (the key names the place and the grid), or the
    /// map's own tileset, the room built from it without the area (the key names the tileset; see
    /// BuiltRoomModels) - the second is what a map boss's arena is stood on from anywhere.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="key">The room's <see cref="RoomKey"/> string; one that names neither a place nor a tileset is no stage.</param>
    /// <param name="terrain">The current area's grid, or null.</param>
    /// <param name="progress">Where the build says how far it has got, or null.</param>
    public static MonsterModel LaidRoom(Func<string, byte[]?>? read, string key, Func<TerrainGrid?>? terrain, ModelProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        RoomKey room = RoomKey.Read(key);
        if (!room.TryLaid(out int x, out int y, out int turn, out int area))
        {
            if (room.Built.Length > 0)
            {
                return BuiltRoomModels.Of(read, room.Path, room.Built, shaded: true, doodads: RoomModels.EveryDoodad, tools: room.Tools, progress: progress, heights: room.Heights, mirrored: room.Mirrored, maskCells: room.MaskCells, groundHeights: room.GroundHeights);
            }

            return MonsterModel.None with { Why = "a room is a stage only as the area laid it or built from its tileset - its file alone is doodads over nothing" };
        }

        TerrainGrid? grid = terrain?.Invoke();
        if (grid is null || RoomKey.Stamp(grid) != area)
        {
            return MonsterModel.None with { Why = "the area this room was laid in is gone - it is a stage while the player stands in that area" };
        }

        return LaidRoomModels.Of(
            read, room.Path, grid, x, y, turn, shaded: true, doodads: RoomModels.EveryDoodad, tools: room.Tools, atLevel: room.AtLevel,
            progress: progress, heights: room.Heights, cut: room.Cut, maskCells: room.MaskCells, groundHeights: room.GroundHeights);
    }

    /// <summary>
    /// The two joined: the monster placed at the tile's middle, on its floor, at the size the game draws it.
    /// </summary>
    /// <param name="actor">The monster, ready.</param>
    /// <param name="tile">The stage, ready.</param>
    /// <param name="modelSize">The variety's ModelSizeMultiplier in percent; anything not positive is a hundred.</param>
    /// <param name="stage">The tile's path, for the lines and the probe.</param>
    public static MonsterModel Staged(MonsterModel actor, MonsterModel tile, int modelSize, string stage)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(stage);

        float scale = modelSize > 0 ? modelSize / 100f : 1f;
        Vector3 middle = (tile.Mesh.Least + tile.Mesh.Most) * 0.5f;
        bool found;
        float floor;
        float turn = 0f;
        string where;
        if (tile.BossSpot is { } spot)
        {
            // WHERE THE ROOM NAMES ITS BOSS, on the floor round the place rather than at it: a throne at the place has a seat
            // that faces up, and the boss stands on the deck under it - sitting in it and rising out of it are his animation's.
            (middle.X, middle.Y, turn) = (spot.At.X, spot.At.Y, spot.Turn);
            found = FloorAround(tile.Mesh, tile.Blends, spot.At.X, spot.At.Y, out floor);
            where = string.Create(CultureInfo.InvariantCulture, $"where the room names its boss (\"{spot.Tag}\", facing {spot.Turn * 180f / MathF.PI:0}°{(spot.Others > 0 ? $"; {spot.Others} more boss places passed over" : string.Empty)})");
        }
        else
        {
            found = Floor(tile.Mesh, tile.Blends, middle.X, middle.Y, out floor);
            where = "at its middle";
        }

        if (!found)
        {
            floor = tile.Mesh.Most.Z;
        }

        // SCALED ABOUT ITS OWN ORIGIN, TURNED, AND THEN MOVED, as row vectors multiply: the monster's feet - its
        // greatest z, the files' up being minus z - land on the floor.
        Matrix4x4 place = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationZ(turn)
            * Matrix4x4.CreateTranslation(middle.X, middle.Y, floor - (actor.Mesh.Most.Z * scale));

        var pile = new ModelPile();
        pile.Add(tile, Matrix4x4.Identity, stage);

        // THE PIECES' OWN NAMES KEPT where the stage has them - a laid or built room names every shape's tile piece, its
        // ground and its doodads. Named all for the room, the probe said "from Boss_01.arm" of Port's white pier, which is
        // the one question it was clicked to answer.
        for (var shape = 0; shape < tile.ShapeSources.Count && shape < tile.Mesh.Shapes.Count; shape++)
        {
            if (tile.ShapeSources[shape].Length > 0)
            {
                pile.Sources[shape] = tile.ShapeSources[shape];
            }
        }

        int first = tile.Mesh.Positions.Length;
        pile.Add(actor, place, ActorSource);
        SkinnedMesh joined = SkinnedMesh.Joined(pile.Joins);

        string said = string.Create(
            CultureInfo.InvariantCulture,
            $"stage: {Tail(stage)}{(TileBook.IsRoom(stage) ? " as the area laid it" : string.Empty)} · the monster stands {where} at {scale:0.##} of its file's size, {(found ? $"on the floor there ({floor:0} on the tile's z)" : "at the tile's lowest point - nothing faces up there")}");

        return RoomModels.Piled(joined, pile, actor.Mesh_, [tile, actor], tile.Shore) with
        {
            // THE MONSTER'S OWN ACCOUNT OF ITSELF, kept whole: the lines under the picture, the dump and
            // the sweep read these, and a stage changes where he stands, not what he is.
            Kind = ModelKind.Ao,
            Material = actor.Material,
            Paint = actor.Paint,
            Fitted = actor.Fitted,
            BodyLeast = Vector3.Transform(actor.BodyLeast, place),
            BodyMost = Vector3.Transform(actor.BodyMost, place),
            BodyFacts = actor.BodyFacts,
            NamedInAo = actor.NamedInAo,
            NamedInMesh = actor.NamedInMesh,
            Runs = actor.Runs,
            Guessed = actor.Guessed,
            Rig = actor.Rig,
            Rig_ = actor.Rig_,
            Move = actor.Move,
            Parts = actor.Parts,
            Sections = actor.Sections,
            Materials = Union(tile.Materials, actor.Materials),
            Textures = Union(tile.Textures, actor.Textures),
            Unshaded = Union(tile.Unshaded, actor.Unshaded),
            Clocked = Union(tile.Clocked, actor.Clocked),
            Shades = Shades(tile, actor, gloss: false),
            GlossShades = Shades(tile, actor, gloss: true),
            ShadedBy = tile.ShadedBy + actor.ShadedBy,
            Bytes = tile.Bytes + actor.Bytes,
            Files = tile.Files + actor.Files,

            // THE STAGE'S OWN LIGHTS, where it has any, shine on the monster too: the light is built for
            // the one model, and the monster is in it. A tile carries none; a stage that does will.
            Lights = tile.Lights,
            LightsSaid = tile.LightsSaid,
            Actor = actor,
            ActorFirst = first,
            ActorPlace = place,
            Stage = stage,
            StageSaid = said,

            // A ROOM'S OWN ACCOUNT, kept beside the monster's: what of its pieces, ground and doodads drew is said nowhere else.
            StageMove = tile.Kind == ModelKind.Room ? tile.Move : string.Empty,
            DoodadFiles = tile.DoodadFiles,

            // THE TILE'S SHORE, made with what stands in its sea once its materials were compiled - the monster set on the stage is
            // no shore, and the tile lies where it was (the pile's identity).
            Shore = tile.Shore,
        };
    }

    /// <summary>The ring the floor round a boss's place is asked on, in world units from it - wider than a throne, narrower than a room.</summary>
    private const float Around = 120f;

    /// <summary>
    /// The floor round a point: the middle of the floors found at it and at eight points a ring round it - or false where none is found.
    /// </summary>
    /// <remarks>
    /// THE MIDDLE OF NINE, so one surface at the point itself - the seat of the throne a boss is set on - does not lift him off
    /// the deck the rest stand on, nor one hole in the ring drop him through it.
    /// </remarks>
    private static bool FloorAround(SkinnedMesh mesh, IReadOnlyList<MaterialBlend>? blends, float x, float y, out float height)
    {
        var floors = new List<float>(9);
        for (var one = -1; one < 8; one++)
        {
            (float sin, float cos) = MathF.SinCos(one * MathF.PI / 4f);
            float r = one < 0 ? 0f : Around;
            if (Floor(mesh, blends, x + (r * cos), y + (r * sin), out float found))
            {
                floors.Add(found);
            }
        }

        floors.Sort();
        height = floors.Count > 0 ? floors[floors.Count / 2] : float.PositiveInfinity;
        return floors.Count > 0;
    }

    /// <summary>
    /// How high the floor is under a point: the highest surface facing up there, over the mesh's solid shapes - or false where none does.
    /// </summary>
    /// <remarks>
    /// EVERY SOLID TRIANGLE WHOSE SHADOW COVERS THE POINT AND WHOSE NORMALS FACE UP, and the least z among
    /// them - the files' up is minus z, so the least is the highest, which is the surface the game's camera
    /// sees at that point. A shape drawn cut out or mixed is passed over: foliage, a decal, a ground layer
    /// laid over the floor are not stood on. One walk over the mesh, at load.
    /// </remarks>
    /// <param name="mesh">The tile's mesh.</param>
    /// <param name="blends">Each shape's blend, in the mesh's shape order - or null for every shape solid.</param>
    /// <param name="x">The point, in the mesh's own units.</param>
    /// <param name="y">The point, in the mesh's own units.</param>
    /// <param name="height">The floor's z there, where one was found.</param>
    public static bool Floor(SkinnedMesh? mesh, IReadOnlyList<MaterialBlend>? blends, float x, float y, out float height)
    {
        height = float.PositiveInfinity;
        if (mesh is not { Ready: true })
        {
            return false;
        }

        Vector3[] at = mesh.Positions;
        Vector3[] up = mesh.Normals;
        int[] indices = mesh.Indices;
        var found = false;
        IReadOnlyList<MeshShape> shapes = mesh.Shapes.Count > 0 ? mesh.Shapes : [new MeshShape(string.Empty, 0, indices.Length)];
        for (var which = 0; which < shapes.Count; which++)
        {
            if (blends is not null && which < blends.Count && blends[which] != MaterialBlend.Opaque)
            {
                continue;
            }

            MeshShape shape = shapes[which];
            int end = Math.Min(shape.From + shape.Count, indices.Length);
            for (int one = Math.Max(shape.From, 0); one + 2 < end; one += 3)
            {
                int ia = indices[one], ib = indices[one + 1], ic = indices[one + 2];
                Vector3 a = at[ia], b = at[ib], c = at[ic];

                // Barycentric in the plane, with a tolerance a little past the edge so a point on a seam
                // between two triangles is under one of them rather than between both.
                float area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
                if (MathF.Abs(area) < 1e-6f)
                {
                    continue;
                }

                float u = (((b.X - x) * (c.Y - y)) - ((c.X - x) * (b.Y - y))) / area;
                float v = (((c.X - x) * (a.Y - y)) - ((a.X - x) * (c.Y - y))) / area;
                float w = 1f - u - v;
                const float Edge = -1e-4f;
                if (u < Edge || v < Edge || w < Edge)
                {
                    continue;
                }

                // FACING UP by its normals rather than its winding: the winding's sense is the files' and the
                // picture lights both sides, while a vertex normal is a direction the file wrote down.
                if (ia < up.Length && ib < up.Length && ic < up.Length && (up[ia].Z + up[ib].Z + up[ic].Z) / 3f > FacesUp)
                {
                    continue;
                }

                float z = (u * a.Z) + (v * b.Z) + (w * c.Z);
                if (z < height)
                {
                    height = z;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>The two models' programs as one list over the joined shapes, each shape keeping its own or none.</summary>
    /// <remarks>
    /// EMPTY ONLY WHERE BOTH ARE, so a stage drawn from its graphs beside a monster drawn from his
    /// texture still has a list the picture can index by shape. For the gloss list a model with no
    /// gloss of its own lends its flat programs, which is what the model's own lists promise - see
    /// MonsterModel.GlossShades.
    /// </remarks>
    private static IReadOnlyList<ShadeProgram?> Shades(MonsterModel tile, MonsterModel actor, bool gloss)
    {
        IReadOnlyList<ShadeProgram?> first = gloss && tile.GlossShades.Count > 0 ? tile.GlossShades : tile.Shades;
        IReadOnlyList<ShadeProgram?> second = gloss && actor.GlossShades.Count > 0 ? actor.GlossShades : actor.Shades;
        if (first.Count == 0 && second.Count == 0)
        {
            return [];
        }

        int tiles = tile.Mesh.Shapes.Count;
        var all = new ShadeProgram?[tiles + actor.Mesh.Shapes.Count];
        for (var one = 0; one < tiles; one++)
        {
            all[one] = one < first.Count ? first[one] : null;
        }

        for (var one = tiles; one < all.Length; one++)
        {
            all[one] = one - tiles < second.Count ? second[one - tiles] : null;
        }

        return all;
    }

    private static IReadOnlyList<string> Union(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => first.Count == 0 ? second : second.Count == 0 ? first : [.. first.Concat(second).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// A pane's last stage, kept while the pane asks for it: the next monster set on it is built alone and joined to the stage already made.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT: a stage takes tens of seconds and a monster tens of milliseconds, and every monster picked on the
/// same stage built the stage again. The stage does not depend on the monster - StagedModels.Staged joins the two and changes
/// neither - so the room is made once per key.
///
/// ONE STAGE, BY ITS WHOLE KEY: the key names the room, how it was laid or built, its switches and the area it was laid in, so a
/// different key is a different stage and the one kept is let go of - a stage is gigabytes of meshes and textures, and two are not
/// kept. A build already under way for the key is waited for rather than started again; one that failed or came out empty is not kept.
/// </remarks>
public sealed class StageKeep
{
    private readonly Lock _gate = new();
    private string _key = string.Empty;
    private Task<MonsterModel>? _tile;

    /// <summary>
    /// The stage for <paramref name="key"/>: the one kept, or one built by <paramref name="build"/> and kept - and whether it was kept.
    /// </summary>
    internal MonsterModel Tile(string key, Func<MonsterModel> build, out bool kept)
    {
        TaskCompletionSource<MonsterModel>? mine = null;
        Task<MonsterModel> tile;
        lock (_gate)
        {
            if (_tile is { } had && string.Equals(_key, key, StringComparison.Ordinal)
                && !had.IsFaulted && !had.IsCanceled && !(had.IsCompletedSuccessfully && !had.Result.Ready))
            {
                tile = had;
            }
            else
            {
                mine = new TaskCompletionSource<MonsterModel>(TaskCreationOptions.RunContinuationsAsynchronously);
                _key = key;
                _tile = tile = mine.Task;
            }
        }

        kept = mine is null;
        if (mine is not null)
        {
            try
            {
                mine.SetResult(build());
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
            {
                mine.SetException(exception);
            }
        }

        return tile.GetAwaiter().GetResult();
    }

    /// <summary>Lets go of the stage kept - the pane asks for none.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _key = string.Empty;
            _tile = null;
        }
    }
}
