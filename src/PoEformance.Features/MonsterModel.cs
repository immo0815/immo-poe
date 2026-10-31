using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>What a model was read from, which decides what the line under its picture calls its pieces.</summary>
public enum ModelKind : byte
{
    /// <summary>An .ao walk - a monster, an item, an effect. Its pieces are what it wears.</summary>
    Ao,

    /// <summary>A terrain tile. Its pieces are sub-tiles, and its unnamed shapes are ground.</summary>
    Tile,

    /// <summary>A room. Its pieces are the doodads placed in it.</summary>
    Room,
}

/// <summary>
/// A monster's model, gathered from the five files it takes to draw one.
/// </summary>
/// <param name="Mesh">The geometry, or <see cref="SkinnedMesh.None"/> where none was found.</param>
/// <param name="Skin">The colour texture with its halved levels, or null to draw the mesh plain.</param>
/// <param name="Mesh_">The <c>.sm</c> that named the geometry, for the report.</param>
/// <param name="Material">The <c>.mat</c> path that was used, for the report.</param>
/// <param name="Why">Where the walk stopped, or empty where it did not.</param>
/// <param name="Paint">
/// Why the monster is drawn in plain ink, or empty where its own texture is on it. A separate
/// answer from <paramref name="Why"/>: a model can be found in full and still have no colour.
/// </param>
/// <summary>One mesh file behind a model, with what its headers said. See <see cref="MonsterModel.Meshes"/>.</summary>
public readonly record struct MeshNamed(string Path, MeshFacts Facts);

public sealed record MonsterModel(
    SkinnedMesh Mesh,
    Mipmaps? Skin,
    string Mesh_,
    string Material,
    string Why,
    string Paint = "")
{
    /// <summary>Nothing found.</summary>
    public static MonsterModel None { get; }
        = new(SkinnedMesh.None, null, string.Empty, string.Empty, "nothing was looked for");

    /// <summary>Whether there is a model to draw.</summary>
    public bool Ready => Mesh.Ready;

    /// <summary>Whether the monster is wearing its own texture rather than plain ink.</summary>
    public bool Painted => Paint.Length == 0 && Skin is not null;

    /// <summary>
    /// One texture per shape of the mesh, in its order - what the renderer paints each part with.
    /// </summary>
    /// <remarks>
    /// A MONSTER IS BUILT OF PARTS AND THEY DO NOT SHARE A SHEET. Body, cloak, wings: the .ao
    /// names a material per shape and each shape's coordinates address ITS OWN texture, so one
    /// texture over the whole mesh puts the body's pixels on the wings. Reported from the live
    /// client by Bahlak the Sky Seer, who came out black with red patches against a game that
    /// draws him in feathers.
    ///
    /// A SHAPE WITH NO MATERIAL OF ITS OWN GETS <see cref="Skin"/>, which is what the whole mesh
    /// used to get. That keeps every monster whose .ao names one material exactly as it was, and
    /// confines the change to the ones that name several - where the old answer was wrong.
    ///
    /// Empty where there is nothing to paint with, and then the renderer draws in ink.
    /// </remarks>
    public IReadOnlyList<Mipmaps?> Skins { get; init; } = [];

    /// <summary>The distinct materials the pictures came out of, for the report.</summary>
    public IReadOnlyList<string> Materials { get; init; } = [];

    /// <summary>
    /// What became of each attachment: where it was put, and how much of it found a bone.
    /// </summary>
    /// <remarks>
    /// THE WALK'S OWN ACCOUNT OF ITSELF, kept because every piece that came out in the wrong
    /// place this month was diagnosed from exactly these numbers and they were reconstructed by
    /// hand each time. It is gathered by the walk that places the pieces, which is the point: a
    /// second walk beside it would answer about itself - the trap the dump fell into when it read
    /// four lines of Gulzal's hammer and reported no rig, no pairing and no materials.
    ///
    /// CHEAP ENOUGH TO ALWAYS KEEP. It is one small record per piece and a monster wears at most
    /// <c>MostParts</c> of them, against a walk that reads tens of megabytes of mesh.
    /// </remarks>
    public IReadOnlyList<PartFit> Fitted { get; init; } = [];

    /// <summary>
    /// What the pieces' ObjectHiding left off or hid - a default outfit under armour, the body's segments, a piece beside another - as
    /// sentences, in the order decided (see <see cref="PieceHiding"/>). Empty where nothing hides anything.
    /// </summary>
    public IReadOnlyList<string> Covered { get; init; } = [];

    /// <summary>
    /// The monster's own box, before anything he wears is joined onto him.
    /// </summary>
    /// <remarks>
    /// BECAUSE <see cref="Mesh"/>'s BOX IS NO LONGER HIS ONCE THE PIECES ARE IN IT. The joined
    /// box is the union of the body and every piece, so a piece held up against it is inside it
    /// by construction and "is this piece anywhere near him" is a question that answers itself.
    ///
    /// That is not a hypothetical: the sweep's first run over 2792 monsters flagged NOTHING as
    /// far from its body, which is what a check that cannot fail looks like from the outside. It
    /// took a run over the whole table to notice, which is the argument for the sweep in one
    /// sentence.
    ///
    /// SECTIONS COUNT AS BODY. A section is not an accessory - see <c>Sections</c>.
    /// </remarks>
    public Vector3 BodyLeast { get; init; }

    /// <inheritdoc cref="BodyLeast"/>
    public Vector3 BodyMost { get; init; }

    /// <summary>
    /// What the body's own mesh file said of itself, kept from before anything was joined onto it.
    /// </summary>
    /// <remarks>
    /// A JOINED MESH HAS NO FACTS - it is several files' geometry in one buffer - and a room is
    /// nothing but joins. The question a room's dump has to answer is about the FILES: which
    /// vertex format each doodad's mesh carries, and what is in the fields of it this reader
    /// steps over. See <see cref="Meshes"/>.
    /// </remarks>
    public MeshFacts BodyFacts { get; init; }

    /// <summary>
    /// The mesh files behind a model built from several, each with its facts - a room's doodads.
    /// </summary>
    /// <remarks>
    /// FOR THE DUMP, which otherwise sees one joined mesh and cannot say which doodad's file holds
    /// a vertex colour and which does not. Empty on a model read from one file, whose
    /// <see cref="Mesh"/> carries its own facts.
    /// </remarks>
    public IReadOnlyList<MeshNamed> Meshes { get; init; } = [];

    /// <summary>
    /// How many materials the files NAMED: the .ao's own, and the mesh manifest's.
    /// </summary>
    /// <remarks>
    /// THE DIFFERENCE BETWEEN "ONE SHEET IS THE TRUTH" AND "WE FOUND ONE". A monster reported
    /// as thirty-five shapes from one texture is either painted from one atlas - which is
    /// ordinary and right - or is a monster whose per-shape materials this walk did not match,
    /// and the picture looks the same either way. The counts tell those apart: one named in the
    /// .ao and none in the .sm is a monster with one material; thirty-five named and one used
    /// is a bug in the matching.
    /// </remarks>
    public int NamedInAo { get; init; }

    /// <inheritdoc cref="NamedInAo"/>
    public int NamedInMesh { get; init; }

    /// <summary>
    /// Whether the manifest's per-material numbers added up and were used to spread its short
    /// material list over the mesh's shapes.
    /// </summary>
    /// <remarks>
    /// SHOWN BECAUSE THE READING IS NOT PROVEN, only tested: the number after each material path
    /// is unidentified - see <see cref="MeshManifest.Spread"/> - and this says, per monster,
    /// whether the file passed the test and the picture depends on it. A line that says so is
    /// what turns "the boss looks right" into evidence about the format.
    /// </remarks>
    public bool Runs { get; init; }

    /// <summary>The distinct colour textures actually put on the mesh, for the report.</summary>
    /// <remarks>
    /// NAMED BECAUSE A WRONG ONE LOOKS LIKE A MISSING ONE. A mask or an occlusion map drawn as
    /// colour is a monster in greyscale, which reads as "no texture" - and the file name says
    /// in a word which of the two it is. See <see cref="Guessed"/>.
    /// </remarks>
    public IReadOnlyList<string> Textures { get; init; } = [];

    /// <summary>
    /// Whether any of those textures was chosen without a slot name saying it is the colour map.
    /// </summary>
    /// <remarks>
    /// The fallback is "the first texture that is not a normal map", which is a guess this
    /// project has always marked as one in its comments and never on screen. Where it is in
    /// force the pane says so, because it is the difference between a monster the game paints
    /// dark and a monster painted from the wrong map.
    /// </remarks>
    public bool Guessed { get; init; }

    /// <summary>The skeleton the monster's .ao names, or <see cref="AnimationSkeleton.None"/>.</summary>
    public AnimationSkeleton Rig { get; init; } = AnimationSkeleton.None;

    /// <summary>The <c>.ast</c> path that was used, for the report.</summary>
    public string Rig_ { get; init; } = string.Empty;

    /// <summary>
    /// What became of an animation pack asked for in place of the rig's own animations (MonsterVariety.Moves) - taken, or why not - or
    /// empty where none was asked for.
    /// </summary>
    public string Pack { get; init; } = string.Empty;

    /// <summary>The animation a picture starts with, where its rig has one of the name (MonsterVariety.Opening), or empty.</summary>
    public string Opening { get; init; } = string.Empty;

    /// <summary>
    /// Why the monster cannot be animated, or empty where it can.
    /// </summary>
    /// <remarks>
    /// A THIRD ANSWER BESIDE <see cref="Why"/> AND <see cref="Paint"/>, for the same reason those
    /// two are apart: a model can be found and coloured and still have no skeleton to move it, and
    /// a picture that holds still looks the same whichever of four files was the missing one.
    /// </remarks>
    public string Move { get; init; } = string.Empty;

    /// <summary>
    /// The point lights a room's doodads carry, in the model's own space - see RoomLights. Empty for anything but a room.
    /// </summary>
    public IReadOnlyList<PointLight> Lights { get; init; } = [];

    /// <summary>What reading those lights came to, for the line under a lit picture - see RoomLights.Gathered.</summary>
    public string LightsSaid { get; init; } = string.Empty;

    /// <summary>Where a stage stood its monster and the floor it found there - see <see cref="StageStanding"/>; null for anything but a staged monster.</summary>
    public StageStanding? Standing { get; init; }

    /// <summary>A room's doodad files and what became of each one's lines - see <see cref="DoodadFile"/>. Empty for anything but a room.</summary>
    public IReadOnlyList<DoodadFile> DoodadFiles { get; init; } = [];

    /// <summary>
    /// Where the model's origin is in the area, in world units, where the model is drawn as the area laid it - else null.
    /// </summary>
    /// <remarks>
    /// A LAID ROOM'S CORNER IS THE PICTURE'S ORIGIN (LaidRoomModels), its x and y the area's own and its
    /// heights the area's terrain heights - so a point the area names, the player's feet, lands in the
    /// picture by taking this off. A room drawn as its file has it has no place in any area.
    /// </remarks>
    public Vector2? AreaOrigin { get; init; }

    /// <summary>
    /// Which of the area's instances the model was laid in - RoomKey.Stamp of the terrain it was laid over - where <see cref="AreaOrigin"/>
    /// is set; else null. What the area holds now (its river's flow map) belongs to the picture only while the area is still this one.
    /// </summary>
    public int? AreaStamp { get; init; }

    /// <summary>
    /// Where a laid or built room names its boss's place, in the picture's own frame - or null where the room names none. See <see cref="RoomSpot"/>.
    /// </summary>
    public RoomSpot? BossSpot { get; init; }

    /// <summary>Whether there is a skeleton with animations on it that fits this mesh.</summary>
    public bool Moves => Move.Length == 0 && Rig.Ready && Rig.Animations.Count > 0;

    /// <summary>How many bytes the walk read out of the install for this monster, over every file it touched.</summary>
    /// <remarks>
    /// COUNTED, NOT ESTIMATED: every file goes through one function and it adds up what came back.
    /// The .ao files and what they extend, the manifest, the geometry, the material, the texture and
    /// the signpost it may sit behind, the skeleton - and nothing that was asked for and not there.
    /// It is what one click in the book costs, and the pane says so under the picture.
    /// </remarks>
    public long Bytes { get; init; }

    /// <summary>How many files those bytes came out of.</summary>
    public int Files { get; init; }

    /// <summary>
    /// How many pieces the monster wears that were joined onto the body.
    /// </summary>
    /// <remarks>
    /// SHOWN UNDER THE PICTURE because a monster with none and a monster whose attachments were
    /// switched off look the same, and so does one whose pieces all failed to read. See
    /// MonsterModels.Dressing.
    /// </remarks>
    public int Parts { get; init; }

    /// <summary>
    /// The shader graphs the materials that were read name as their parent, distinct, in the order met.
    /// </summary>
    /// <remarks>
    /// THE EVIDENCE FOR BLENDING, collected and not yet acted on: whether an additive or a
    /// transparent material is told apart by its graph is what the first real effect shows - see
    /// MaterialFile.Parents.
    /// </remarks>
    public IReadOnlyList<string> Shaders { get; init; } = [];

    /// <summary>
    /// Each shape's blend mode as its material spells it, in shape order - empty where it says none.
    /// </summary>
    /// <remarks>
    /// THE RAW WORD, kept beside <see cref="Blends"/>, because what it is taken to mean is a rule on
    /// words that the first real effect may prove wrong - see MaterialBlends.
    ///
    /// WHERE TWO GRAPHS NAME ONE, THE LAST STANDS - see Paints.Modes for why.
    /// </remarks>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>How each shape is drawn over what is behind it, from <see cref="Modes"/>.</summary>
    public IReadOnlyList<MaterialBlend> Blends => _blends ??= [.. Modes.Select(MaterialBlends.Of)];

    private MaterialBlend[]? _blends;

    /// <summary>
    /// How many <c>.sm</c> files the body itself is, before anything worn over it.
    /// </summary>
    /// <remarks>
    /// ONE IS ORDINARY AND SEVEN IS THE CASE THAT WAS BEING LOST. A SkinMesh block may name
    /// several manifests, each a section of the same body on the same rig: Zar Wali, the Bone
    /// Tyrant is arms, chest, head, neck and tail in seven files, and while only the first was
    /// read the pane drew two floating arms. Shown under the picture because a monster drawn in
    /// pieces and a monster that really IS two arms look the same.
    /// </remarks>
    public int Sections { get; init; } = 1;

    /// <summary>
    /// What the model was read from. See <see cref="ModelKind"/>.
    /// </summary>
    /// <remarks>
    /// THE LINE UNDER THE PICTURE WAS WRITTEN FOR MONSTERS: a tile reported "named: 45 in the .ao,
    /// 0 in the .sm" and "wearing 9 parts" when it has neither file and its parts are sub-tiles.
    /// </remarks>
    public ModelKind Kind { get; init; }

    /// <summary>
    /// Each shape's material as its source names it, graph number and all - empty where it names none.
    /// </summary>
    /// <remarks>
    /// FOR EVERY SHAPE OF THE JOINED MESH, a monster's worn pieces included, in the order the join
    /// lays them down. For the dump, for a tile leaving out the shapes a texture marks - see
    /// TileModels - and for reading each shape's shader graphs - see MonsterModels.Shaded.
    /// </remarks>
    public IReadOnlyList<string> ShapeMaterials { get; init; } = [];

    /// <summary>
    /// Each shape's material as its own file names it, before a master's TileMaterialOverrides swapped it - empty where nothing was
    /// swapped, every shape then wearing its own (<see cref="ShapeMaterials"/>).
    /// </summary>
    /// <remarks>
    /// WHAT STANDS IN THE SEA IS JUDGED BY IT TOO (MonsterModels.Shaded, RoomModels.ShoreOf): Port's list gives the ship's timbers the
    /// sea's spray (its MPRT_* materials add OceanSprayFoam), and from v0.1.373, when the room's own lines took the swaps, the ship stood
    /// in the sea as the rocks do - land wherever its bowsprit, ropes and anchor rose over the water, whole foam drawn on the water there.
    /// </remarks>
    public IReadOnlyList<string> AuthoredMaterials { get; init; } = [];

    /// <summary>Each shape's colour texture, by path - empty where none was found. See <see cref="ShapeMaterials"/>.</summary>
    public IReadOnlyList<string> ShapeTextures { get; init; } = [];

    /// <summary>
    /// Whether each shape is a roof section - its material's .mat says <c>"roofsection": true</c> - in shape order, or empty where none is.
    /// </summary>
    /// <remarks>
    /// THE GAME HIDES THEM ROUND THE PLAYER (RoofFade, CaptureRoofFade): The Red Vale's trees wear OFO_BlackForestTrees01*_fadec and
    /// RoofFade_JungleTreeBranches01c, whose .mat says so. Read where the shapes are compiled (MonsterModels.Shaded), from the shared cache
    /// of material files, and carried through a staged monster's join.
    /// </remarks>
    public IReadOnlyList<bool> RoofSections { get; init; } = [];

    /// <summary>
    /// What each shape of a room came from - a doodad's <c>.ao</c>, a tile piece's file, the ground under it - or empty where the model is not a room.
    /// </summary>
    /// <remarks>
    /// FOR THE PROBE, which names what is under a pixel: a material says what a surface is painted
    /// with, and the same mud is worn by a tile, a doodad and a pile of rubble. See MeshPicture's PixelProbe.
    /// </remarks>
    public IReadOnlyList<string> ShapeSources { get; init; } = [];

    /// <summary>
    /// Per shape, which of <see cref="Sways"/> turns it as the clock runs, or -1 for none - a ship and its deck's load (RoomSways); empty for a model that sways nothing.
    /// </summary>
    public IReadOnlyList<int> ShapeSways { get; init; } = [];

    /// <summary>The wobble cores laid in it, each with its rig played and where it stands - see RoomSway.</summary>
    public IReadOnlyList<RoomSway> Sways { get; init; } = [];

    /// <summary>
    /// The room's shore, for the ocean's swell - made from its water and its ground (ShoreField.Of, CHOSEN); null where it has no water, no ground over it, or is no room.
    /// </summary>
    public ShoreField? Shore { get; init; }

    /// <summary>How many of a tile's black-wall shapes were left out. See TileModels.BlackWall.</summary>
    public int Walls { get; init; }

    /// <summary>How many of a tile's own materials its tileset swapped for others. See TileModels.Of.</summary>
    public int Swapped { get; init; }

    /// <summary>What laying or building a room came to, counted - null for anything not a laid or built room. See RoomAccount.</summary>
    public RoomAccount? Account { get; init; }

    /// <summary>The tileset a tile was drawn as, or empty for the tile's own materials.</summary>
    public string Tileset { get; init; } = string.Empty;

    /// <summary>
    /// The material swaps of the master a laid or built room, the area round you or a tile drawn as its tileset was made under - its
    /// TileMaterialOverrides by the material swapped (TilesetIndex.Overrides), put on its tiles and every doodad laid over them - or empty
    /// where it was made under none, or the master swaps nothing. A stage keeps its room's (StagedModels.Staged).
    /// </summary>
    /// <remarks>
    /// FOR THE CAPTURE, which says what each swap does to the material it puts in place (CapturePanes): nothing did, and Port's quay
    /// runoff went unseen for thirty versions - from v0.1.344 a laid or built room puts its master's swaps on its tiles, and Port's
    /// material_overrides.tmo swaps the quay's MSHD_GraveBrickTile01c.mat, the material the spray from occlusion was built for, for
    /// MPRT_GraveBrickTile01c.mat. The model keeps what its shapes wear, not what they wore, so these are what tells the two apart.
    /// </remarks>
    public IReadOnlyDictionary<string, string> MasterSwaps { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>The master (<c>.tsi</c>) that <see cref="MasterSwaps"/> are of, or empty where the model was made under none.</summary>
    public string MasterFile { get; init; } = string.Empty;

    /// <summary>How a tile was turned to match the way the current area laid it, in words, or empty for the file's own orientation.</summary>
    public string Laid { get; init; } = string.Empty;

    /// <summary>
    /// Each shape's colour as its material's shader graphs compute it, or null where the plain texture stands.
    /// </summary>
    /// <remarks>
    /// EMPTY UNLESS ASKED FOR - see MonsterModels.Shaded. A program carries its textures with it, so
    /// a shape with one is drawn from those and not from <see cref="Skins"/>.
    /// </remarks>
    public IReadOnlyList<ShadeProgram?> Shades { get; init; } = [];

    /// <summary>
    /// Each shape's program lit the game's way, with its glossiness worked out - or null where the shape has none; empty where no material has one.
    /// </summary>
    /// <remarks>
    /// <see cref="Shades"/> LIT FLAT, THESE LIT BY THE GAME'S GGX - see ShadeProgram.Glossy. A shape
    /// drawn from a plain texture under <see cref="Shades"/> has a program here all the same where
    /// its graphs give a gloss: the texture is its colour, not its shine. Where a material gives no
    /// gloss its entry is its flat program, so either list draws every shape.
    /// </remarks>
    public IReadOnlyList<ShadeProgram?> GlossShades { get; init; } = [];

    /// <summary>How many distinct materials are drawn from their graphs rather than from one texture.</summary>
    public int ShadedBy { get; init; }

    /// <summary>
    /// Every graph whose colour was left out, as "node in graph", distinct - for the line under the picture.
    /// </summary>
    /// <remarks>
    /// THE OTHER HALF OF NOT GUESSING. A node this does not understand costs that graph's colour and
    /// nothing else; saying which node, in which graph, is what makes the picture's gap a question
    /// with an address rather than a wrong colour nobody can explain.
    /// </remarks>
    public IReadOnlyList<string> Unshaded { get; init; } = [];

    /// <summary>
    /// The materials whose shader graphs read the game's clock, by file - drawn running, as an animation is. See ShadeProgram.UsesTime.
    /// </summary>
    public IReadOnlyList<string> Clocked { get; init; } = [];

    /// <summary>
    /// The monster alone, where this model is that monster standing on a stage - else null. See StagedModels.
    /// </summary>
    /// <remarks>
    /// KEPT WHOLE, because it is what the pose moves: the skeleton skins the monster's own mesh, and the
    /// result goes over his slice of <see cref="Mesh"/> - from <see cref="ActorFirst"/> on, through
    /// <see cref="ActorPlace"/> - while the stage's vertices stay where its file put them.
    /// </remarks>
    public MonsterModel? Actor { get; init; }

    /// <summary>Where the actor's vertices begin in <see cref="Mesh"/>. See <see cref="Actor"/>.</summary>
    public int ActorFirst { get; init; }

    /// <summary>Where the actor was put on the stage: its scale and its place. See <see cref="Actor"/>.</summary>
    public Matrix4x4 ActorPlace { get; init; } = Matrix4x4.Identity;

    /// <summary>The stage's tile, by path, or empty where the model stands on nothing.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>What became of the stage asked for: where the monster stands on it, or why there is none.</summary>
    public string StageSaid { get; init; } = string.Empty;

    /// <summary>
    /// A room stage's own lines under its picture, as it stood alone - its pieces, ground and doodads - or empty.
    /// </summary>
    /// <remarks>
    /// APART FROM <see cref="Move"/>, which on a staged model is the monster's. Until v0.1.202 the room's lines were dropped
    /// there, and the Monster Book could not say why Port's harbour stood without its water: the tile doodads' count, what
    /// did not draw and what the cap left out were said only where the room was drawn alone.
    /// </remarks>
    public string StageMove { get; init; } = string.Empty;

    /// <summary>Whether this is a monster standing on a stage. See <see cref="Actor"/>.</summary>
    public bool Staged => Actor is not null;
}

/// <summary>
/// Walks a monster from its table row to the triangles and the texture that clothe it.
/// </summary>
/// <remarks>
/// FIVE FILES AND FOUR FORMATS, which is why this exists rather than the window doing it:
///
///     MonsterVarieties.AOFiles   a path, out of the install's own table
///       -> .ao                   text, keyword = value, names a .sm under SkinMesh
///         -> .sm                 text, its own format, names a .smd and a .mat
///           -> .smd              binary, the triangles
///           -> .mat              JSON, names a .dds
///             -> .dds            BC1 or BC3, which GameArt already decodes
///
/// THE EXTENDS CHAIN IS NOT OPTIONAL. Nine of ten monsters checked carry their own SkinMesh and
/// the tenth - BoneRabbleJaguar - carries none at all: its skin is in the file it extends. A
/// walker that read only the monster's own .ao would report that one as having no model, which
/// looks like a gap in the game rather than a gap in the walk.
///
/// THE CHILD WINS. ExpeditionBasicSkeleton carries a skin AND a remove_skin naming the one its
/// base provides, so the nearer file replaces rather than adds. Taking the first skin found while
/// walking from the monster outwards gets that right without reading remove_skin at all.
///
/// READ THROUGH A FUNCTION RATHER THAN AN INSTALL, which is what makes the whole chain testable:
/// the same walk runs over a dictionary of files here and over the game's bundles there. It is
/// also the shape InstalledArt already uses for exactly this reason.
///
/// THE BODY ONLY, FOR NOW. A monster's armour and weapons are attached_object entries naming
/// their own .ao files, each with a mesh of its own - a skeleton warrior without its shield is
/// still a skeleton warrior, and drawing the attachments is a refinement rather than a missing
/// half.
/// </remarks>
public static class MonsterModels
{
    /// <summary>How far the extends chain is followed before giving up.</summary>
    /// <remarks>A file that extends itself is a loop; the visited set catches that, this caps depth.</remarks>
    public const int MostHops = 8;

    /// <summary>The struct that names a monster's mesh, and the entry inside it.</summary>
    private const string Block = "SkinMesh";
    private const string Entry = "skin";

    /// <summary>The struct that names a monster's skeleton, and the entry inside it.</summary>
    /// <remarks>
    /// INSIDE THE FILE'S client BLOCK, which AnimatedObject folds into the same list of structs
    /// with a flag. Over a whole install this is the one key that ever names an .ast - 2581 times,
    /// and nothing else once - so a second spelling is not looked for.
    /// </remarks>
    private const string RigBlock = "ClientAnimationController";
    private const string RigEntry = "skeleton";

    /// <summary>
    /// Gathers the model for one monster, or says where the walk stopped.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="one">The monster. Its AoFiles come from the install's own table.</param>
    /// <param name="wearing">
    /// Whether the pieces the monster hangs off itself are read and joined on. Off gives exactly
    /// the body this returned before there was a choice - see <see cref="Dressing"/> for what it
    /// costs, which is the reason there is one.
    /// </param>
    /// <param name="shaded">
    /// Whether each shape's material's shader graphs are read and compiled - see <see cref="Shaded"/>.
    /// Off gives exactly the model this returned before graphs were read.
    /// </param>
    /// <param name="progress">Where the build says how far it has got, or null - see <see cref="ModelProgress"/>.</param>
    public static MonsterModel Of(Func<string, byte[]?>? read, MonsterVariety? one, bool wearing = true, bool shaded = false, ModelProgress? progress = null)
    {
        if (read is null)
        {
            return MonsterModel.None with { Why = "no install to read" };
        }

        if (one?.AoFiles is not { Count: > 0 } named)
        {
            return MonsterModel.None with
            {
                Why = "the monster names no .ao file - the shipped export does not carry that column",
            };
        }

        var paints = new Paints { Progress = progress };
        MonsterModel model = OfFiles(read, named, wearing, paints, one.Worn, one.Moves, one.Opening);
        return shaded ? Shaded(read, model, paints) : model;
    }

    /// <summary>
    /// The model with each shape's colour worked out from its material's shader graphs, where they say more than a texture.
    /// </summary>
    /// <remarks>
    /// AFTER THE WALK, OVER WHAT IT FOUND: every shape's material is in
    /// <see cref="MonsterModel.ShapeMaterials"/>, so this is one pass over a list, whichever walk
    /// made the model - a monster, an item, a tile, a room. Through the walk's own cache, so a
    /// texture the graphs read and a skin already decoded are one decode.
    ///
    /// THREE OUTCOMES PER MATERIAL. Its colour is one plain texture read - the ordinary material -
    /// and that texture is the shape's skin, which is what it was already in every case looked at.
    /// Its colour is anything more and could be compiled, and the shape gets the program. Or no
    /// graph's colour could be evaluated, and the shape is drawn exactly as before. Whatever was left
    /// out is named in <see cref="MonsterModel.Unshaded"/>. A material named with a graph number -
    /// <c>Boss.mat:1</c> - is left as it is: what the number selects among graphs is established
    /// for the colour slot and nothing more.
    /// </remarks>
    internal static MonsterModel Shaded(Func<string, byte[]?> read, MonsterModel model, Paints paints)
    {
        int count = model.Mesh.Shapes.Count;
        IReadOnlyList<string> wearing = model.ShapeMaterials;
        if (!model.Ready || count == 0 || wearing.Count != count)
        {
            return model;
        }

        long bytes = 0;
        var files = 0;
        byte[]? Counted(string one)
        {
            byte[]? got = read(one);
            if (got is not null)
            {
                Interlocked.Add(ref bytes, got.Length);
                Interlocked.Increment(ref files);
            }

            return got;
        }

        // A SHAPE LEFT WITH NOTHING - every triangle of it hidden from the game's camera, see
        // LaidRoomModels - is not compiled for: nothing of it is drawn.
        bool Compiles(int shape)
        {
            string material = wearing[shape];
            return material.Length > 0 && MaterialFile.SelectorOf(material) < 0 && model.Mesh.Shapes[shape].Count > 0;
        }

        // EVERY MATERIAL ON EVERY CORE FIRST: its graphs compiled and its textures decoded into the shared cache, each once
        // whoever asks (Paints) - a room wears hundreds, and one after another they were most of a stage's wait. The walk below
        // then finds each made, and puts the answers together in the shapes' order as it always did.
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materials = new List<string>();
        for (var shape = 0; shape < count; shape++)
        {
            if (Compiles(shape) && distinct.Add(MaterialFile.Bare(wearing[shape])))
            {
                materials.Add(wearing[shape]);
            }
        }

        using (ModelProgress.Step step = ModelProgress.Begin(paints.Progress, "compiling the shader graphs", materials.Count))
        {
            Parallel.ForEach(materials, material =>
            {
                paints.Shade(Counted, material);
                step.Advance();
            });
        }

        var shades = new ShadeProgram?[count];
        var glossy = new ShadeProgram?[count];
        var shines = false;
        Mipmaps?[]? skins = null;
        var drawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clocked = new List<string>();
        var unshaded = new List<string>();
        for (var shape = 0; shape < count; shape++)
        {
            if (!Compiles(shape))
            {
                continue;
            }

            string material = wearing[shape];

            Shade shade = paints.Shade(Counted, material);
            foreach (string why in shade.Skipped)
            {
                if (!unshaded.Contains(why, StringComparer.Ordinal))
                {
                    unshaded.Add(why);
                }
            }

            // THE GLOSSY PROGRAM WHERE THERE IS ONE, else whatever the flat list draws.
            glossy[shape] = shade.Glossy ?? shade.Program;
            shines |= shade.Glossy is not null;
            if (shade.Program is { } program)
            {
                shades[shape] = program;
                drawn.Add(MaterialFile.Bare(material));
                if (program.UsesTime && !clocked.Contains(MaterialFile.Bare(material), StringComparer.OrdinalIgnoreCase))
                {
                    clocked.Add(MaterialFile.Bare(material));
                }
                continue;
            }

            Mipmaps? had = shape < model.Skins.Count ? model.Skins[shape] : model.Skin;
            if (shade.Plain is { } plain && !ReferenceEquals(plain, had))
            {
                skins ??= [.. Enumerable.Range(0, count).Select(one => one < model.Skins.Count ? model.Skins[one] : model.Skin)];
                skins[shape] = plain;
            }
        }

        // THE ROOF SECTIONS, by their own .mat - see MonsterModel.RoofSections. A mix of two grounds wears a name that is no file, and no
        // ground is a roof; what is not a .mat (the beach's graph) says nothing.
        bool[]? roofs = null;
        for (var shape = 0; shape < count; shape++)
        {
            string material = wearing[shape];
            string file = MaterialFile.Bare(material);
            if (material.Length == 0 || model.Mesh.Shapes[shape].Count == 0 || GroundBlend.Parts(material, out _, out _, out _)
                || !file.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || !paints.Paint(Counted, file).RoofSection)
            {
                continue;
            }

            roofs ??= new bool[count];
            roofs[shape] = true;
        }

        // WHAT STANDS IN THE SEA JOINS THE SHORE now that the materials say which shapes the sea breaks on - see RoomModels.ShoreOf.
        ShoreField? shore = model.Shore;
        if (shore is not null && Standing(Counted, model, shades, paints) is { } stands)
        {
            shore = RoomModels.ShoreOf(model.Mesh, model.Modes, model.ShapeSources, stands) ?? shore;
        }

        return model with
        {
            Shore = shore,
            Shades = shades,
            GlossShades = shines ? glossy : shades,
            RoofSections = roofs ?? (IReadOnlyList<bool>)[],
            Skins = skins ?? model.Skins,
            ShadedBy = drawn.Count,
            Unshaded = unshaded,
            Clocked = clocked,
            Bytes = model.Bytes + bytes,
            Files = model.Files + files,
        };
    }

    /// <summary>
    /// Which shapes stand in the sea - those whose program draws the sea's spray, and whose own material, before a master's swap, draws
    /// it too - or null where none does.
    /// </summary>
    /// <remarks>
    /// A SPRAY ONLY THE SWAP GAVE IS NO SHORE (v0.1.379, said from the live client with a probe: white lines and patches on Port's water
    /// round the ship's bow since v0.1.373). The rule (ShoreField.Of, v0.1.236) took the shapes authored to have the sea crash on them -
    /// the rocks and the pier's stones - and left the ship out, its timbers carrying no spray; Port's list swaps 17 of them for MPRT_*
    /// materials that add OceanSprayFoam, so the ship stood in the sea from the room's lines taking the swaps, and the probe found the
    /// water under its bow on a wall cell, its near-shore term whole. A swap that keeps the spray (the quay's MSHD_GraveBrickTile01c to
    /// MPRT_GraveBrickTile01c) keeps the shape standing; a material worn as its file names it is judged as before. The own materials are
    /// compiled alone, never drawn, so their textures are not read.
    /// </remarks>
    internal static bool[]? Standing(Func<string, byte[]?> read, MonsterModel model, ShadeProgram?[] shades, Paints paints)
    {
        IReadOnlyList<string> authored = model.AuthoredMaterials;
        IReadOnlyList<string> wearing = model.ShapeMaterials;
        string? OwnOf(int shape)
        {
            string own = shape < authored.Count ? authored[shape] : string.Empty;
            return own.Length > 0 && shape < wearing.Count
                && !string.Equals(MaterialFile.Bare(own), MaterialFile.Bare(wearing[shape]), StringComparison.OrdinalIgnoreCase) ? own : null;
        }

        // THE OWN MATERIALS OF THE SWAPPED SHAPES THAT SPRAY, each compiled once on every core - the ship's handful in Port.
        var owns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var shape = 0; shape < shades.Length; shape++)
        {
            if (shades[shape] is { Sprays: true } && OwnOf(shape) is { } own)
            {
                owns.Add(MaterialFile.Bare(own));
            }
        }

        var spraying = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(owns, own => spraying[own] = paints.Compiled(read, own).Compiled.Program is { Sprays: true });

        bool[]? stands = null;
        for (var shape = 0; shape < shades.Length; shape++)
        {
            if (shades[shape] is { Sprays: true } && (OwnOf(shape) is not { } own || spraying.GetValueOrDefault(MaterialFile.Bare(own))))
            {
                stands ??= new bool[shades.Length];
                stands[shape] = true;
            }
        }

        return stands;
    }

    /// <summary>What one material's graphs came to: a program, a plain texture, or neither - and what was left out.</summary>
    /// <param name="Program">The program the shape is drawn with lit flat, or null.</param>
    /// <param name="Plain">The texture the shape is drawn from in its place, or null.</param>
    /// <param name="Skipped">What was left out.</param>
    /// <param name="Glossy">The program lit the game's way, or null where it is <paramref name="Program"/> - see ShadeProgram.Glossy.</param>
    internal readonly record struct Shade(ShadeProgram? Program, Mipmaps? Plain, IReadOnlyList<string> Skipped, ShadeProgram? Glossy = null);

    /// <summary>
    /// Gathers the model an <c>.ao</c> describes, whoever named it, or says where the walk stopped.
    /// </summary>
    /// <remarks>
    /// THE WALK NEVER NEEDED A MONSTER, only the .ao files one names - which is what lets the item
    /// book draw a sword through exactly the reads that draw a boss. An item's art row names its
    /// .ao the way MonsterVarieties.AOFiles does, and from there every hop is the same file format.
    ///
    /// A FIXED MESH CAN BE THE WHOLE MODEL, which a monster never is. See <see cref="Fixed"/>.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="named">The .ao files to start from, nearest first.</param>
    /// <param name="wearing">Whether attached pieces are read and joined on - see the other overload.</param>
    public static MonsterModel OfFiles(Func<string, byte[]?>? read, IReadOnlyList<string>? named, bool wearing = true)
        => OfFiles(read, named, wearing, null);

    /// <summary>
    /// The same walk, with a material and texture cache that outlives it.
    /// </summary>
    /// <remarks>
    /// FOR A ROOM, which loads one model per distinct doodad file and places each many times: two
    /// doodads - a rock and a stump - that share a .mat and its .dds would otherwise decode that
    /// sheet once each and hand the renderer two copies of one texture. The cache is per call by
    /// default and nothing else changes.
    ///
    /// <paramref name="pieces"/> ARE PIECES HUNG BESIDE THE FILES' OWN LINES - the player's composition, each an .ao and the socket it hangs
    /// at ("&lt;root&gt;" for a skin on his rig) - read, placed and hidden as a line of his own file would be.
    /// </remarks>
    internal static MonsterModel OfFiles(
        Func<string, byte[]?>? read, IReadOnlyList<string>? named, bool wearing, Paints? shared,
        IReadOnlyList<WornPiece>? pieces = null, string? moves = null, string? opening = null)
    {
        if (read is null)
        {
            return MonsterModel.None with { Why = "no install to read" };
        }

        if (named is not { Count: > 0 })
        {
            return MonsterModel.None with { Why = "no .ao file to read" };
        }

        // COUNTED AS THEY ARE READ, through the one function everything below reads with, so a
        // file the walk asks for is a file the count saw - the signpost a texture may sit behind
        // included, which GameArt follows on its own. A file that was not there comes back null
        // and counts for nothing.
        var tally = new Tally(read);
        Func<string, byte[]?> counted = tally.Read;

        // A .fmt NAMED OUTRIGHT is the whole model, with no .ao in front of it: an item's AOFile2
        // may be one - the Lumen Mace's is WoodenClub.fmt - and reading it as an .ao found nothing.
        if (named[0].EndsWith(".fmt", StringComparison.OrdinalIgnoreCase))
        {
            return Prop(counted, named[0].Trim(), tally, shared);
        }

        if (Skinned(counted, named) is not { } found)
        {
            return Fixed(counted, named, tally, shared)
                ?? tally.Failed("no SkinMesh or FixedMesh anywhere in the .ao files or what they extend");
        }

        MeshManifest manifest = Read(counted, found.Meshes[0], MeshManifest.Read);
        if (!manifest.Ready)
        {
            return tally.Failed($"the mesh manifest did not read: {found.Meshes[0]}", found.Meshes[0]);
        }

        SkinnedMesh mesh = Read(counted, manifest.Geometry, SkinnedMesh.Read);
        if (!mesh.Ready)
        {
            // THE FILE IS NAMED FIRST AND THE READER'S REASON SECOND. A reader answers about the
            // bytes it was handed and says things like "nothing to read", which is true and
            // useless one layer up: a picture that does not appear looks the same whichever file
            // was missing, so the message has to say which.
            return tally.Failed(
                $"the geometry did not read: {manifest.Geometry}"
                    + (mesh.Why.Length > 0 ? $" - {mesh.Why}" : string.Empty),
                manifest.Geometry);
        }

        // THE .ao's MATERIALS WIN WHERE THERE ARE ANY. The manifest names a default for the mesh
        // and the monster's own file overrides it per shape - which is how one skeleton mesh
        // serves an expedition skeleton and a bone rabble, in different colours.
        //
        // ALL OF THEM, IN ORDER, AND NOT THE FIRST. A monster built out of parts names a
        // material per shape and they are not all the same file: the first can perfectly well
        // be a cloth or a glow with no colour map in it while the body's skin is the second.
        // Taking the first was measured on monsters cut from one sheet and reported wrong from
        // the live client by Veynar the Frostbane, who is plainly painted in the game and came
        // out here in plain ink. The renderer puts ONE texture on the mesh, so the first
        // material that actually carries a colour map is the one it gets.
        List<string> materials =
        [
            .. found.Materials.Select(one => one.Material),
            .. manifest.Materials.Where(one => one.Path.Length > 0).Select(one => one.Path),
        ];

        // ONE CACHE FOR THE WHOLE WALK, keyed by FILE rather than by what named it: a material
        // is read once however many shapes point at it, and a texture is decoded once however
        // many materials name it. Without it the model's own skin and the shape wearing the
        // same material were two decodes of one 2048-square sheet - and two objects, which is
        // also two uploads to the renderer.
        Paints paints = shared ?? new Paints();
        (Mipmaps? skin, string paint, string material) = Painted(counted, mesh, materials, paints);

        // AND ONE PER SHAPE, over the same reads: the walk above already decoded every material
        // that has a colour in it, so this is a lookup rather than a second pass over the
        // bundles. See MonsterModel.Skins for why a monster needs more than one.
        Dress dress = Dressed(counted, mesh, found.Materials, manifest, skin, material, paints);

        (AnimationSkeleton rig, string move) = Rigged(counted, found.Skeleton, mesh);

        // THE ANIMATIONS OF A PACK IN PLACE OF THE RIG'S OWN, where its bones are the rig's, name for name and in order - the player's
        // stance pack, read off his Actor (PlayerDress.Moves): his OneRig.ast holds idle_01 alone, the bind pose, and the game plays him
        // from Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast with a wand in hand (MEASURED in the 16:16 capture).
        string rigPath = found.Skeleton, packed = string.Empty;
        if (moves is { Length: > 0 })
        {
            AnimationSkeleton pack = Read(counted, moves, AnimationSkeleton.Read);
            packed = !pack.Ready ? $"the stance's animations {moves} did not read{(pack.Why.Length > 0 ? ": " + pack.Why : string.Empty)}"
                : !SameBones(rig, pack) ? $"the stance's animations {moves} are on {pack.Bones.Count} bones that are not his {rig.Bones.Count}, name for name and bound alike"
                : $"{pack.Animations.Count} animations of his stance, from {moves}";
            if (SameBones(rig, pack))
            {
                rig = pack;
                rigPath = moves;
            }
        }

        // AND WHAT THE MONSTER WEARS. Doryani's skirt, belt, necklace and six more pieces are
        // attached_object entries naming their own .ao, mesh and rig - see Dressing. Joined into
        // one mesh so the renderer, the per-shape palette and the pose go on working unchanged.
        // THE BODY ITSELF CAN BE SEVERAL FILES, before anything is worn over it. Joined the same
        // way, and always - a section is not an accessory and switching the parts off must not
        // take half the monster with it. See Sections.
        List<Part> parts = Sections(counted, found.Meshes, found.Materials, paints, skin);
        int sections = parts.Count;

        var told = new List<PartFit>();
        var riding = new Riding(rig);
        var covering = new Covering();
        if (wearing)
        {
            parts.AddRange(Dressing(counted, found.Files, rig, paints, skin, told, riding, pieces ?? [], covering));
        }

        // HIS OWN SHAPES HIS PIECES HIDE - a body armour's Body_Seg, the gloves' fingers - off the body and its sections alike (PieceHiding).
        if (covering.Body.Count > 0)
        {
            mesh = Without(mesh, covering.Body, out int gone);
            for (var section = 0; section < sections; section++)
            {
                parts[section] = parts[section] with { Mesh = Without(parts[section].Mesh, covering.Body, out int more) };
                gone += more;
            }

            covering.Said.Add($"{gone} of the body's shapes hidden");
        }

        // THE PIECES THAT PLAY THEIR OWN ANIMATIONS RIDE HIS RIG, their bones after his - see AnimationSkeleton.Carrying. Said by
        // the parts' own account (PartKind.Played), NOT in Move: Move is why a model does NOT animate, and v0.1.329 wrote a line
        // there that stopped Malgor's every animation and took his list of them away (said from the live client with a picture).
        if (riding.Rigs.Count > 0 && rig.Carrying(riding.Rigs) is { } carrying)
        {
            rig = carrying;
        }

        // THE MONSTER WITHOUT WHAT HE IS WEARING, taken BEFORE the join and kept - see
        // MonsterModel.BodyLeast. Afterwards there is no way back to it: the joined box is the
        // union of the body and every piece, so a piece measured against it is inside it by
        // construction and any question of the form "is this piece anywhere near him" answers
        // itself. Sections count as body, because a section is not an accessory.
        Vector3 bare = mesh.Least, worn_ = mesh.Most;
        MeshFacts facts = mesh.Facts;
        for (var section = 0; section < sections; section++)
        {
            bare = Vector3.Min(bare, parts[section].Mesh.Least);
            worn_ = Vector3.Max(worn_, parts[section].Mesh.Most);
        }

        if (parts.Count > 0)
        {
            List<MeshJoin> join =
            [
                new MeshJoin(mesh),
                .. parts.Select(one => new MeshJoin(one.Mesh, one.Bones, one.Weights, one.Place)),
            ];
            List<Mipmaps?> worn = [.. dress.Skins, .. parts.SelectMany(one => one.Skins)];

            // THE MODES RIDE WITH THE SKINS, padded to each piece's own shape count so the two lists
            // stay aligned however short a piece's came back.
            List<string> modes = [.. Padded(dress.Modes, dress.Skins.Count), .. parts.SelectMany(one => Padded(one.Modes, one.Skins.Count))];
            List<string> wears = [.. Padded(dress.ShapeMaterials, dress.Skins.Count), .. parts.SelectMany(one => Padded(one.Wearing, one.Skins.Count))];
            mesh = SkinnedMesh.Joined(join);
            dress = dress with { Skins = worn, Modes = modes, ShapeMaterials = wears };
        }

        return new MonsterModel(mesh, skin, manifest.Geometry, material, string.Empty, paint)
        {
            Parts = parts.Count - sections,
            Sections = found.Meshes.Count,
            Fitted = told,
            Covered = covering.Said,
            BodyLeast = bare,
            BodyMost = worn_,
            BodyFacts = facts,
            Skins = dress.Skins,
            Modes = dress.Modes,
            Materials = dress.Materials,
            NamedInAo = found.Materials.Count,
            NamedInMesh = manifest.Materials.Count,
            Runs = dress.Runs,
            Textures = dress.Textures,
            Guessed = dress.Guessed,
            Rig = rig,
            Rig_ = rigPath,
            Move = move,
            Pack = packed,
            Opening = opening is { Length: > 0 } && rig.Animations.Any(one => string.Equals(one.Name, opening, StringComparison.Ordinal))
                ? opening
                : string.Empty,
            Bytes = tally.Bytes,
            Files = tally.Files,
            Shaders = paints.Shaders,
            ShapeMaterials = dress.ShapeMaterials,
        };
    }

    /// <summary>
    /// The model as a rigid prop, where the .ao chain names a <c>FixedMesh</c> and no skin at all.
    /// </summary>
    /// <remarks>
    /// THE SHAPE A MONSTER NEVER HAS AND AN ITEM MAY. Every monster is a skin on a rig, so the walk
    /// only ever asked for a SkinMesh and treated a FixedMesh as something hung off one - Malgor's
    /// cannon. An item's .ao can be nothing but the prop, and asking only for a skin would report
    /// every such item as having no model when its whole geometry is one .fmt away.
    ///
    /// ASKED ONLY AFTER THE SKIN WALK FOUND NOTHING, so a monster draws exactly as it did: the skin
    /// is still the body wherever there is one, and this cannot change a single monster.
    ///
    /// NOT ANIMATED, AND SAID SO. A .fmt has no bones and no weights, and as the body there is no
    /// socket to pin it to either - binding it to some bone of a skeleton the chain happens to name
    /// would be choosing a bone nobody measured. It is drawn where the file puts it, and
    /// <see cref="MonsterModel.Move"/> carries the reason it does not move.
    /// </remarks>
    private static MonsterModel? Fixed(
        Func<string, byte[]?> read, IReadOnlyList<string> named, Tally tally, Paints? shared)
    {
        string path = string.Empty;
        foreach (string one in named)
        {
            AnimatedObject ao = Object(read, one);
            if (ao.Ready && Entryed(Whole(read, ao), PropBlock, PropEntry) is { Length: > 0 } found)
            {
                path = found;
                break;
            }
        }

        return path.Length == 0 ? null : Prop(read, path, tally, shared);
    }

    /// <summary>A <c>.fmt</c> as the whole model, dressed in its own materials - see <see cref="Fixed"/>.</summary>
    private static MonsterModel Prop(Func<string, byte[]?> read, string path, Tally tally, Paints? shared)
    {
        FixedMesh prop = Read(read, path, FixedMesh.Read);
        if (!prop.Ready)
        {
            return tally.Failed(
                $"the fixed mesh did not read: {path}" + (prop.Why.Length > 0 ? $" - {prop.Why}" : string.Empty),
                path);
        }

        // THE .fmt's OWN MATERIALS, one per shape, are the only ones there are: there is no
        // manifest, and the same list Propped hands to the dressing serves the whole model here.
        return Worn(read, prop.Mesh, prop.Named, paintTheRest: true, path,
            "a fixed mesh is rigid - it has no bones to animate", shared) with
        {
            Bytes = tally.Bytes,
            Files = tally.Files,
        };
    }

    /// <summary>
    /// A rigid mesh dressed in the materials a list gives its shapes - no rig, no pieces.
    /// </summary>
    /// <remarks>
    /// THE PART OF THE WALK THAT NEVER NEEDED AN .ao: given a mesh and which material each shape
    /// wears, the textures are found, decoded once each and handed out per shape. A fixed-mesh item
    /// and a terrain tile both arrive here with that and nothing else.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="mesh">The geometry, its shapes named the way <paramref name="named"/> names them.</param>
    /// <param name="named">Each shape's material, by the shape's name.</param>
    /// <param name="paintTheRest">
    /// Whether a shape with no material of its own wears the first colour found. Right for a prop,
    /// whose unnamed shapes are part of the same object; wrong for a tile, whose unnamed shapes are
    /// its ground and would come out in a wall's texture.
    /// </param>
    /// <param name="source">The file the model is read from, for the line under the picture.</param>
    /// <param name="move">Why it does not move - see <see cref="MonsterModel.Move"/>.</param>
    /// <param name="shared">A cache that outlives this model, or null for one of its own - see the internal OfFiles.</param>
    internal static MonsterModel Worn(
        Func<string, byte[]?> read,
        SkinnedMesh mesh,
        IReadOnlyList<(string Shape, string Material)> named,
        bool paintTheRest,
        string source,
        string move,
        Paints? shared = null)
    {
        List<string> materials = [.. named.Select(one => one.Material).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Paints paints = shared ?? new Paints();
        (Mipmaps? skin, string paint, string material) = Painted(read, mesh, materials, paints);
        Dress dress = Dressed(
            read, mesh, named, MeshManifest.None, paintTheRest ? skin : null, material, paints);

        return new MonsterModel(mesh, skin, source, material, string.Empty, paint)
        {
            BodyLeast = mesh.Least,
            BodyMost = mesh.Most,
            BodyFacts = mesh.Facts,
            Skins = dress.Skins,
            Modes = dress.Modes,
            Materials = dress.Materials,
            NamedInAo = named.Count,
            Runs = dress.Runs,
            Textures = dress.Textures,
            Guessed = dress.Guessed,
            Move = move,
            Shaders = paints.Shaders,
            ShapeMaterials = dress.ShapeMaterials,
            ShapeTextures = dress.ShapeTextures,
        };
    }

    /// <summary>One piece a monster hangs off itself, ready to be joined onto the body.</summary>
    /// <param name="Mesh">Its geometry.</param>
    /// <param name="Bones">Its vertex bones, remapped onto the PARENT's rig, or null where they could not be.</param>
    /// <param name="Skins">One texture per shape of it, in its order - the same rule the body follows.</param>
    private readonly record struct Part(
        SkinnedMesh Mesh,
        byte[]? Bones,
        byte[]? Weights,
        Matrix4x4? Place,
        IReadOnlyList<Mipmaps?> Skins,
        IReadOnlyList<string> Modes)
    {
        /// <summary>Each of its shapes' materials as written - see MonsterModel.ShapeMaterials.</summary>
        public IReadOnlyList<string> Wearing { get; init; } = [];
    }

    /// <summary>The entry keys whose value is another .ao. From the format diagram; see AoSurvey.</summary>
    private static readonly string[] Hangs =
        ["ao", "fixed_ao", "attached_object", "attached_slaved_animation_object"];

    /// <summary>How deep the attachment chain is followed. Doryani's belt hangs a dagger; that is two.</summary>
    private const int MostDeep = 4;

    /// <summary>
    /// Most pieces joined onto one monster. A guard on a walk whose shape nobody has measured.
    /// </summary>
    /// <remarks>
    /// EFFECTS ATTACH EFFECTS, which is the failure this is against: an .ao chain followed without
    /// a cap is how a click in the book becomes a minute. Doryani wears nine with four more under
    /// his belt, so this is well clear of what a dressed monster needs.
    /// </remarks>
    private const int MostParts = 48;

    /// <summary>
    /// The body's other sections, where its SkinMesh block names more than one manifest.
    /// </summary>
    /// <remarks>
    /// A SKIN BLOCK IS A LIST, NOT A FIELD, and reading it as a field cost a boss his whole body.
    /// Reported from the live client: Zar Wali, the Bone Tyrant came out as two arms floating in
    /// the air, painted from <c>GiantSnakeSkeletonBossArm_colour.dds</c> - and his .ao says why
    /// in seven consecutive lines, <c>GSSBArmA</c> through <c>GSSBTail</c>, of which this walk
    /// read the first.
    ///
    /// THEY ARE SECTIONS OF ONE BODY ON ONE RIG, which is what makes them cheap to join: each
    /// carries its own vertex bones and they already index the SAME skeleton, so unlike a worn
    /// piece there is nothing to remap and nowhere to put it. Its own manifest names its own
    /// materials, so each section is dressed on its own and the palette comes out per shape as
    /// before.
    ///
    /// NOT UNDER THE PARTS SWITCH. Turning attachments off is meant to undress a monster, not
    /// to behead one.
    /// </remarks>
    private static List<Part> Sections(
        Func<string, byte[]?> read,
        IReadOnlyList<string> skins,
        IReadOnlyList<(string Shape, string Material)> named,
        Paints paints,
        Mipmaps? fallback)
    {
        var parts = new List<Part>();
        for (var one = 1; one < skins.Count && parts.Count < MostParts; one++)
        {
            MeshManifest manifest = Read(read, skins[one], MeshManifest.Read);
            if (!manifest.Ready)
            {
                continue;
            }

            SkinnedMesh mesh = Read(read, manifest.Geometry, SkinnedMesh.Read);
            if (!mesh.Ready)
            {
                continue;
            }

            Dress dress = Dressed(read, mesh, named, manifest, fallback, string.Empty, paints);

            // NO BONES AND NO PLACE: the section's own vertex bones index the body's rig
            // already, so handing null keeps them and the join leaves the geometry where it is.
            parts.Add(new Part(mesh, null, null, null, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials });
        }

        return parts;
    }

    /// <summary>
    /// Everything the monster wears, read from the attachments its own files name.
    /// </summary>
    /// <remarks>
    /// WHAT THIS IS FOR. Reported from the live client: Doryani stands in the game in a skirt and
    /// the pane drew him bare-legged. The paint was never the problem - his runs add up and every
    /// shape has its sheet - the geometry simply was not there, because a skirt is not part of the
    /// body mesh. It is <c>attached_object = "hip_jntBnd …/attachments/Skirt.ao"</c>, with a mesh,
    /// a rig and an idle animation of its own, and the body's files say nothing else about it.
    ///
    /// A PIECE IS EITHER A PROP OR A SKIN, and its own rig says which - see
    /// <see cref="Retargeted"/>. A prop shares only <c>root_jntBnd</c> with the parent, is
    /// modelled around its own origin, and goes rigidly at its socket. A skin carries the body's
    /// own bone names, is already in the monster's space, and wants its bone NUMBERS translated
    /// and no transform at all.
    ///
    /// GETTING THAT WRONG IS INVISIBLE AT REST, which is what made it expensive: a piece bound
    /// rigidly to one bone stands in the right place until something moves, and then follows that
    /// one bone while the body deforms around it.
    ///
    /// WHAT IT COSTS is the reason the pane has a switch for it. Doryani's body is 32 MB across
    /// 15 files; his nine pieces bring their own meshes, rigs and sheets, and the belt hangs three
    /// more under itself. That is the price of a monster that looks like itself, and it is paid on
    /// every click in a list somebody scrolls.
    /// </remarks>
    private static List<Part> Dressing(
        Func<string, byte[]?> read,
        IReadOnlyList<string> files,
        AnimationSkeleton parent,
        Paints paints,
        Mipmaps? fallback,
        List<PartFit> told,
        Riding riding,
        IReadOnlyList<WornPiece> worn,
        Covering covering)
    {
        var parts = new List<Part>();
        if (SkeletonPose.Of(parent) is not { } rest)
        {
            // Without the parent's rest pose there is nowhere to put a piece, and a pile of
            // clothing at the monster's feet is worse than a monster in its underwear.
            return parts;
        }

        var where = new Dictionary<string, int>(parent.Bones.Count, StringComparer.OrdinalIgnoreCase);
        for (var one = 0; one < parent.Bones.Count; one++)
        {
            where.TryAdd(parent.Bones[one].Name, one);
        }

        // THE MONSTER'S OWN RIG IS THE OUTERMOST FIT: every bone stands for itself and needs no
        // transform to reach model space. Everything below is expressed against it - see Fit. Its sockets are its files' own.
        var body = new Fit(where, rest, Itself(parent.Bones.Count), []) { Plain = true, Sockets = SocketsOf(read, files, parent) };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Hangs_>();

        // EACH PIECE'S CHAIN READ ONCE for what its own file says of where it hangs (Overridden).
        var chains = new Dictionary<string, IReadOnlyList<AnimatedObject>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<AnimatedObject> ChainOf(string path)
        {
            if (!chains.TryGetValue(path, out IReadOnlyList<AnimatedObject>? chain))
            {
                AnimatedObject ao = Object(read, path);
                chains[path] = chain = ao.Ready ? Whole(read, ao) : [];
            }

            return chain;
        }

        // THE MONSTER ITSELF IS THE OUTERMOST CARRIER, at the identity: a piece socketed to
        // "<root>" at the top level is already in the monster's own space and wants no transform
        // at all. Bone 0 is the rig's root, so the piece still follows the monster.
        // A FILE THAT CLEARS WHAT IT INHERITS (clear_inherited_attached_objects, the player's IntFourB.ao) ends the walk up its chain, and
        // the pieces worn beyond the files' own lines - the player's composition (PlayerPieces) - hang beside them.
        var top = new List<Line_>();
        foreach (string one in files)
        {
            AnimatedObject ao = Object(read, one);
            top.AddRange(Hung(ao));
            if (Clears(ao))
            {
                break;
            }
        }

        // A WORN PIECE THAT IS A BACK ATTACHMENT - its chain extends Metadata/ParentBackAttachment, the player's cape - hangs rigidly at
        // the body's Back socket (DECIDED from the live client, 2026-10-31: no file names its socket, the files' word for it is the back).
        foreach (WornPiece one in worn)
        {
            bool back = one.SkinOnly && OnTheBack(read, one.Path);
            top.Add(new Line_(back ? BackSocket : one.Socket, one.Path, Matrix4x4.Identity, string.Empty)
            {
                SkinOnly = one.SkinOnly && !back,
                Held = one.Held || back,
            });
        }

        foreach ((Line_ line, PieceHiding hiding, HashSet<string> drops) in Uncovered(read, top, covering))
        {
            string socket = Rooted(line.Socket) ? Overridden(ChainOf, line.Path) ?? line.Socket : line.Socket;
            if (Socketed(socket, body, Matrix4x4.Identity, 0, Matrix4x4.Identity, top: true, line.Local) is { } put)
            {
                queue.Enqueue(new Hangs_(
                    line.Path, put.Place, put.Bone, put.Put, put.Socket, socket, 1, Rooted(socket), body)
                {
                    Carrier = Monster,
                    Hiding = hiding,
                    Drops = drops,
                    SkinOnly = line.SkinOnly && Rooted(socket),
                    Held = line.Held,
                });
            }
            else
            {
                // THE ONE ANSWER THAT LOSES A PIECE OUTRIGHT, and the walk is otherwise silent
                // about it: a socket the monster's own rig has no bone for at the top level.
                // Counted here because nothing downstream will ever see this piece again.
                told.Add(new PartFit(
                    line.Path, socket, PartPlace.Dropped, 1, PartKind.None, 0, 0, 0, 0, false, 0, false,
                    string.Empty, Vector3.Zero, Vector3.Zero));
            }
        }

        // EVERY PIECE BUILT, with its carrier and what it hides - the shapes are taken off once the whole tree is built (Cover).
        var made = new List<Made_>();
        while (queue.Count > 0 && parts.Count < MostParts)
        {
            Hangs_ one_ = queue.Dequeue();
            if (one_.Path.Length == 0 || !seen.Add(one_.Path))
            {
                continue;
            }

            AnimatedObject ao = Object(read, one_.Path);
            if (!ao.Ready)
            {
                continue;
            }

            // THE PIECE IS READ BEFORE ITS CHILDREN ARE QUEUED, because what it turns into IS the
            // rig they are authored against - see Fit. The order parts come out in is the order
            // they are dequeued, which this does not change.
            (Part? part, Fit inner_, PartFit fit) = Worn(read, ao, one_, paints, fallback, body, riding);
            told.Add(fit);

            // EVERY PIECE THE GAME WEARS HIDES WHAT IT HIDES, drawn here or not - a held piece left out still covers what it covers there.
            int carrier = Unbuilt;
            if (part is { } built)
            {
                parts.Add(built);
                carrier = made.Count;
                made.Add(new Made_(parts.Count - 1, one_.Carrier, one_.Hiding, PieceHiding.Aliases(Whole(read, ao)), one_.Path));
            }
            else if (one_.Hiding.Any)
            {
                made.Add(new Made_(Unbuilt, one_.Carrier, one_.Hiding, new Dictionary<string, string[]>(), one_.Path));
            }

            if (one_.Depth < MostDeep)
            {
                // ITS OWN PIECES BUT THOSE ITS CARRIER'S OTHER PIECES HIDE (hide_sibling_attachments), and among them those one of them hides.
                var lines = new List<Line_>();
                foreach (Line_ line in Hung(ao))
                {
                    if (line.Alias.Length > 0 && one_.Drops.Contains(line.Alias))
                    {
                        covering.Said.Add($"{Name(line.Path)} ({line.Alias}) left off {Name(one_.Path)}: a piece beside it hides it");
                        continue;
                    }

                    lines.Add(line);
                }

                foreach ((Line_ line, PieceHiding hiding, HashSet<string> drops) in Uncovered(read, lines, covering))
                {
                    string socket = Rooted(line.Socket) ? Overridden(ChainOf, line.Path) ?? line.Socket : line.Socket;
                    if (Socketed(socket, inner_, one_.Place, one_.Bone, one_.Put, top: false, line.Local) is { } where_)
                    {
                        queue.Enqueue(new Hangs_(
                            line.Path,
                            where_.Place,
                            where_.Bone,
                            where_.Put,
                            where_.Socket,
                            socket,
                            one_.Depth + 1,
                            Rooted(socket),
                            inner_)
                        {
                            Carrier = carrier,
                            Hiding = hiding,
                            Drops = drops,
                        });
                    }
                }
            }
        }

        Cover(read, files, parts, made, covering);
        return parts;
    }

    /// <summary>The carrier of a top-level piece: the monster himself.</summary>
    private const int Monster = -1;

    /// <summary>The carrier of a piece whose own carrier drew nothing: none whose shapes it could hide.</summary>
    private const int Unbuilt = -2;

    /// <summary>One attached_object line: its socket, its .ao, the turn and shift its children ask for, and the alias it ends with.</summary>
    private readonly record struct Line_(string Socket, string Path, Matrix4x4 Local, string Alias)
    {
        /// <summary>Whether it is drawn only as a skin on its carrier's rig - see WornPiece.SkinOnly.</summary>
        public bool SkinOnly { get; init; }

        /// <summary>Whether it hangs rigidly at its socket - see WornPiece.Held.</summary>
        public bool Held { get; init; }
    }

    /// <summary>
    /// A piece dressed: its place among the parts (<see cref="Unbuilt"/> for one left out that still hides), its carrier among the pieces
    /// built (or the monster), what it hides, and its skin's groups.
    /// </summary>
    private readonly record struct Made_(int Part, int Carrier, PieceHiding Hiding, IReadOnlyDictionary<string, string[]> Aliases, string Path);

    /// <summary>
    /// What the pieces' ObjectHiding took off (see <see cref="PieceHiding"/>): the monster's own shapes, which <see cref="OfFiles"/> takes off
    /// his mesh and his sections, and the account said under the picture.
    /// </summary>
    internal sealed class Covering
    {
        /// <summary>The monster's own shapes his pieces hide.</summary>
        public HashSet<string> Body { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>What was hidden and by what, in the order decided.</summary>
        public List<string> Said { get; } = [];
    }

    /// <summary>
    /// The lines of one carrier with those its other pieces hide (hide_parent_attachments, by the alias a line ends with) left off, each
    /// kept line with its own hiding and the aliases of ITS pieces its siblings hide (hide_sibling_attachments).
    /// </summary>
    private static List<(Line_ Line, PieceHiding Hiding, HashSet<string> Drops)> Uncovered(Func<string, byte[]?> read, List<Line_> lines, Covering covering)
    {
        var hidings = new PieceHiding[lines.Count];
        var hidden = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var one = 0; one < lines.Count; one++)
        {
            AnimatedObject ao = Object(read, lines[one].Path);
            hidings[one] = ao.Ready ? PieceHiding.Of(Whole(read, ao)) : PieceHiding.None;
            foreach (string alias in hidings[one].ParentAttachments)
            {
                hidden.TryAdd(alias, lines[one].Path);
            }
        }

        var kept = new List<(Line_ Line, PieceHiding Hiding, HashSet<string> Drops)>(lines.Count);
        for (var one = 0; one < lines.Count; one++)
        {
            Line_ line = lines[one];
            if (line.Alias.Length > 0 && hidden.TryGetValue(line.Alias, out string? by))
            {
                covering.Said.Add($"{Name(line.Path)} ({line.Alias}) left off: {Name(by)} hides it");
                continue;
            }

            var drops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var other = 0; other < lines.Count; other++)
            {
                if (other != one)
                {
                    drops.UnionWith(hidings[other].SiblingAttachments);
                }
            }

            kept.Add((line, hidings[one], drops));
        }

        return kept;
    }

    /// <summary>
    /// The shapes the built pieces hide taken off: a piece's hide_parent_segments off its carrier (the monster's into <see cref="Covering.Body"/>,
    /// a piece's off its part), its hide_sibling_segments off its carrier's other pieces - each name a group of the target's skin
    /// (<see cref="PieceHiding.Aliases"/>) or a shape itself. A shape keeps its place in its piece's list with nothing left in it (SkinnedMesh.Keeping).
    /// </summary>
    private static void Cover(Func<string, byte[]?> read, IReadOnlyList<string> files, List<Part> parts, List<Made_> made, Covering covering)
    {
        if (!made.Any(one => one.Hiding.ParentSegments.Count > 0 || one.Hiding.SiblingSegments.Count > 0))
        {
            return;
        }

        IReadOnlyDictionary<string, string[]> own = PieceHiding.Aliases([.. files.Select(one => Object(read, one)).Where(one => one.Ready)]);
        var hidden = new HashSet<string>[made.Count];
        for (var one = 0; one < made.Count; one++)
        {
            Made_ piece = made[one];
            if (piece.Hiding.ParentSegments.Count > 0)
            {
                if (piece.Carrier == Monster)
                {
                    covering.Body.UnionWith(PieceHiding.Shapes(piece.Hiding.ParentSegments, own));
                    covering.Said.Add($"{Name(piece.Path)} hides the body's {string.Join(", ", piece.Hiding.ParentSegments)}");
                }
                else if (piece.Carrier >= 0)
                {
                    (hidden[piece.Carrier] ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                        .UnionWith(PieceHiding.Shapes(piece.Hiding.ParentSegments, made[piece.Carrier].Aliases));
                    covering.Said.Add($"{Name(piece.Path)} hides {Name(made[piece.Carrier].Path)}'s {string.Join(", ", piece.Hiding.ParentSegments)}");
                }
            }

            if (piece.Hiding.SiblingSegments.Count == 0)
            {
                continue;
            }

            for (var other = 0; other < made.Count; other++)
            {
                if (other != one && made[other].Carrier == piece.Carrier && made[other].Part >= 0)
                {
                    (hidden[other] ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                        .UnionWith(PieceHiding.Shapes(piece.Hiding.SiblingSegments, made[other].Aliases));
                }
            }
        }

        for (var one = 0; one < made.Count; one++)
        {
            if (made[one].Part >= 0 && hidden[one] is { Count: > 0 } shapes)
            {
                Part part = parts[made[one].Part];
                SkinnedMesh kept = Without(part.Mesh, shapes, out int gone);
                if (gone > 0)
                {
                    parts[made[one].Part] = part with { Mesh = kept };
                    covering.Said.Add($"{gone} shapes of {Name(made[one].Path)} hidden by the pieces beside or on it");
                }
            }
        }
    }

    /// <summary>A mesh without the triangles of the shapes named - each shape keeping its place, empty - and how many shapes lost any.</summary>
    internal static SkinnedMesh Without(SkinnedMesh mesh, IReadOnlySet<string> shapes, out int gone)
    {
        gone = 0;
        if (shapes.Count == 0 || mesh.Shapes.Count == 0)
        {
            return mesh;
        }

        bool[]? keep = null;
        foreach (MeshShape shape in mesh.Shapes)
        {
            if (!shapes.Contains(shape.Name) || shape.Count < 3)
            {
                continue;
            }

            if (keep is null)
            {
                keep = new bool[mesh.Triangles];
                Array.Fill(keep, true);
            }

            gone++;
            int first = shape.From / 3, last = Math.Min(keep.Length, (shape.From + shape.Count) / 3);
            for (int triangle = first; triangle < last; triangle++)
            {
                keep[triangle] = false;
            }
        }

        return keep is null ? mesh : mesh.Keeping(keep);
    }

    /// <summary>Whether a file says to take none of the attached objects of what it extends.</summary>
    private static bool Clears(AnimatedObject ao)
    {
        foreach (AoStruct block in ao.Named("AttachedAnimatedObject"))
        {
            foreach (AoEntry entry in block.Entries)
            {
                if (string.Equals(entry.Key, "clear_inherited_attached_objects", StringComparison.Ordinal)
                    && string.Equals(entry.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A file's name without its folders, for the account.</summary>
    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>One piece waiting to be read: where its carrier puts it, and the rig it belongs to.</summary>
    /// <param name="Place">Its transform into model space, for a piece that goes rigidly.</param>
    /// <param name="Bone">The MONSTER's bone it follows, for the same.</param>
    /// <param name="Put">Its socket's bind, in model space, without the attachment line's own turn.</param>
    /// <param name="Socket">That socket's number in the CARRIER's rig, or -1 where it named none.</param>
    /// <param name="Named">The socket as the line WROTE it, which is what a report has to say.</param>
    /// <param name="Under">The rig the piece is authored against - see <see cref="Fit"/>.</param>
    private readonly record struct Hangs_(
        string Path,
        Matrix4x4 Place,
        int Bone,
        Matrix4x4 Put,
        int Socket,
        string Named,
        int Depth,
        bool Rooted,
        Fit Under)
    {
        /// <summary>Its carrier among the pieces built, <see cref="Monster"/> or <see cref="Unbuilt"/>.</summary>
        public int Carrier { get; init; } = Monster;

        /// <summary>What its ObjectHiding hides - see <see cref="PieceHiding"/>.</summary>
        public PieceHiding Hiding { get; init; } = PieceHiding.None;

        /// <summary>The aliases of its own pieces that its carrier's other pieces hide.</summary>
        public HashSet<string> Drops { get; init; } = [];

        /// <summary>Whether it is drawn only as a skin on its carrier's rig - see WornPiece.SkinOnly.</summary>
        public bool SkinOnly { get; init; }

        /// <summary>Whether it hangs rigidly at its socket - see WornPiece.Held.</summary>
        public bool Held { get; init; }
    }

    /// <summary>
    /// The rig a piece is authored against, and what that rig's bones are in the MONSTER's.
    /// </summary>
    /// <remarks>
    /// A PIECE HANGS OFF WHATEVER CARRIES IT, AND NOT OFF THE MONSTER. Reported from the live
    /// client: Tycho's skirt sits on him and the layers under it lay on the floor - and the two
    /// are not siblings. <c>SkirtLayers.ao</c> is an attached_object of <c>Skirt.ao</c>, its rig
    /// lives in the skirt's own folder, and it rests <c>root_jntBnd</c> at (0,0,0) and
    /// <c>spine_2_jntBnd</c> at (0,0,-12.8) - which is exactly where the SKIRT rests them. The
    /// body rests its spine_2 at (0,2,-132.3).
    ///
    /// SO MATCHING A NESTED PIECE'S BONES AGAINST THE BODY IS MATCHING THE WRONG RIG. Its two
    /// shared names found the body's bones, were corrected out of a bind pose they were never in,
    /// and every bone the body does not have - the whole simulated cloth - fell to the body's
    /// root, which is the floor between his feet.
    ///
    /// WHAT THIS CARRIES is the answer for one rig: <see cref="Where"/> and <see cref="Rest"/>
    /// are that rig, <see cref="Onto"/> says which of the MONSTER's bones each of its bones ends
    /// up following, and <see cref="Into"/> is the transform from that rig's bind pose into the
    /// monster's. Composing the two is what lets a piece be fitted to its carrier and still come
    /// out in model space, however deep it hangs.
    ///
    /// FOR THE BODY ITSELF both are the identity, so every case that was already right stays
    /// bit-for-bit what it was.
    /// </remarks>
    private readonly record struct Fit(
        IReadOnlyDictionary<string, int> Where,
        SkeletonPose Rest,
        byte[] Onto,
        Matrix4x4[] Into)
    {
        /// <summary>
        /// Whether this IS the monster's own rig, so every bone stands for itself.
        /// </summary>
        /// <remarks>
        /// The common case by far, and worth knowing about rather than proving per bone: it
        /// makes the composition below a no-op instead of a matrix multiply and a copy of the
        /// geometry per piece.
        /// </remarks>
        public bool Plain { get; init; }

        /// <summary>
        /// The sockets the rig's own .ao chain defines - each its bone in this rig and its own turn, shift and size off that bone - or
        /// null. See <see cref="Socketed"/>.
        /// </summary>
        public IReadOnlyDictionary<string, (int Bone, Matrix4x4 Local)>? Sockets { get; init; }
    }

    /// <summary>The bone table of a rig that stands for itself: nought is nought, one is one.</summary>
    private static byte[] Itself(int bones)
    {
        var said = new byte[bones];
        for (var one = 0; one < bones && one <= byte.MaxValue; one++)
        {
            said[one] = (byte)one;
        }

        return said;
    }

    /// <summary>A transform per bone, all of them the identity.</summary>
    private static Matrix4x4[] Flat(int bones)
    {
        var said = new Matrix4x4[bones];
        Array.Fill(said, Matrix4x4.Identity);
        return said;
    }

    /// <summary>Which of the monster's bones a carrier's bone is, and the transform to get there.</summary>
    private static (int Bone, Matrix4x4 Into) Through(Fit fit, int at)
    {
        if (fit.Plain)
        {
            return (at >= 0 ? at : 0, Matrix4x4.Identity);
        }

        return at >= 0 && at < fit.Onto.Length && at < fit.Into.Length
            ? (fit.Onto[at], fit.Into[at])
            : (0, Matrix4x4.Identity);
    }

    /// <summary>Vertex bones of a mesh already numbered by the carrier's rig, put into the monster's.</summary>
    private static byte[] Passed(Fit fit, byte[] bones)
    {
        var said = new byte[bones.Length];
        for (var one = 0; one < bones.Length; one++)
        {
            said[one] = (byte)Through(fit, bones[one]).Bone;
        }

        return said;
    }

    /// <summary>
    /// The bone a name means in the rig it is being fitted to, a MERGED rig's path included.
    /// </summary>
    /// <remarks>
    /// A RIG BUILT OUT OF SEVERAL WRITES THE PATH WHERE A NAME WOULD COLLIDE. Brughor's corpse
    /// armour is a monster's skeleton with a heap of corpses merged into it, and its bones read
    /// <c>root_jntBnd|spine_2_jntBnd</c>, <c>root_jntBnd|spine_2_jntBnd|chest_jntBnd</c>,
    /// <c>…|neck_jntBnd</c> - the bone's whole path from the root, because the corpses carry a
    /// spine and a chest of their own.
    ///
    /// THE PATH IS THE PROOF, not a guess at one: the piece rests <c>L_clavicle_jntBnd</c> at
    /// exactly (27.7,-37.7,-464.2), which is where the BODY rests it, and that bone hangs off
    /// <c>root_jntBnd|spine_2_jntBnd|chest_jntBnd</c> here and off <c>chest_jntBnd</c> there. Read
    /// as a plain name none of the three matched anything, so his chest, spine and neck - and the
    /// corpses hanging off them - fell to the rig root, which is the ground.
    ///
    /// THE FULL NAME IS TRIED FIRST, so a rig that really has a bone called that is unaffected.
    /// </remarks>
    private static bool Meant(IReadOnlyDictionary<string, int> where, string name, out int found)
    {
        if (where.TryGetValue(name, out found))
        {
            return true;
        }

        int bar = name.LastIndexOf('|');
        return bar >= 0 && where.TryGetValue(name[(bar + 1)..], out found);
    }

    /// <summary>
    /// The bone a SOCKET means, including the mount a rig spells out in full.
    /// </summary>
    /// <remarks>
    /// MEASURED OVER THE WHOLE TABLE, and it is the largest thing the first sweep found: 451
    /// attachments name a socket their monster's rig has no bone for, so they are dropped
    /// outright, and 220 of those name a mount the rig plainly HAS under a longer spelling.
    /// Unanimously, with no exception in 220:
    ///
    ///     the line says        the rig carries               how many
    ///     Head_Attachment      aux_Head_attachment                115
    ///     R_Weapon             aux_R_Weapon_attachment             81
    ///     L_Weapon             aux_L_Weapon_attachment             12
    ///     Back                 aux_Back_attachment                 12
    ///
    /// One transformation covers both shapes: put <c>aux_</c> in front, and add
    /// <c>_attachment</c> where the socket does not already end in it. What was being thrown
    /// away is not decoration - it is VaalGuard02HeaddressLivingLow, VaalLivingSpear,
    /// TwilightOrderSorcererStaff, BlackJawSuperuniqueWeaponHeld, Olroth_SwordHeld: helmets,
    /// headdresses and held weapons, on something close to a hundred and eighty monsters.
    ///
    /// THE OTHER 185 ARE RIGHT TO BE DROPPED and are what makes this checkable rather than a
    /// guess: halo_01, core_light, aux_facelight, eye_L, FX_eye, spore_r_1..5. Those name
    /// nothing of the sort on their own rigs, they are lights and effect points, and this leaves
    /// every one of them exactly where it was.
    ///
    /// A FALLBACK AND ONLY A FALLBACK. The literal name is tried first, so this cannot move a
    /// piece that already resolves; the only pieces it can reach are ones currently drawn
    /// nowhere at all. That is what makes it a different animal from the bone-matching rules
    /// that broke two monsters apiece earlier - those chose among candidates for a piece already
    /// on screen.
    ///
    /// NO OPEN REFERENCE ANSWERS THIS. poe_data_tools has no .ao parser and its FORMATS.md is
    /// diagrams; the game does answer it, in the bone names its own rigs carry, which is what
    /// the 220 above are counted from.
    /// </remarks>
    private static bool Mounts(IReadOnlyDictionary<string, int> where, string socket, out int at)
    {
        if (where.TryGetValue(socket, out at))
        {
            return true;
        }

        string aux = Aux + socket;
        return where.TryGetValue(aux, out at)
            || (!socket.EndsWith(Mount, StringComparison.OrdinalIgnoreCase)
                && where.TryGetValue(aux + Mount, out at));
    }

    /// <summary>What a rig calls an attachment point, around the name a socket writes.</summary>
    private const string Aux = "aux_";

    /// <inheritdoc cref="Aux"/>
    private const string Mount = "_attachment";

    /// <summary>
    /// Where a socket puts a piece: the transform to model space, and the bone it then follows.
    /// </summary>
    /// <remarks>
    /// THREE CASES, AND THE GAME'S OWN SPELLING SEPARATES THEM.
    ///
    /// "&lt;root&gt;" IS NOT A BONE NAME. It is how the game says "at my carrier's own origin", so
    /// the piece is already in whatever space its carrier is in and wants that carrier's
    /// transform unchanged - which at the top level is the identity. Reported from the live
    /// client: Bahlak the Sky Seer wears one piece, socketed "&lt;root&gt;", and treating that as an
    /// unknown bone put the parent rig's ROOT transform on a piece already in model space. The
    /// root carries the rig's own orientation, so his feathers were tipped over and laid out
    /// flat on the floor at his feet.
    ///
    /// A NAME THE PARENT RIG HAS is the ordinary case: the bone's rest transform places the
    /// piece, and binding to that bone is what makes it follow an arm that lifts.
    ///
    /// A NAME IT DOES NOT HAVE means different things inside and outside. On a piece hung off
    /// another piece it is a bone of the CARRIER's rig - Doryani's dagger and mirror name
    /// phys_skinned_L_1_jntBnd and _2_, which belong to his belt - and THAT is the rig looked in,
    /// so the strand is found rather than fallen back from. Where it is in neither, the carrier's
    /// own place is the nearest thing the body knows. At the TOP level there is no carrier to
    /// fall back to, and bone 0 is not an answer, it is the floor: the piece is left out, on the
    /// same rule that leaves a monster with no rig undressed.
    ///
    /// THREE VALUES COME BACK AND THEY ARE NOT THE SAME THING. <c>Place</c> and <c>Bone</c> put a
    /// rigid piece in model space. <c>Put</c> is the socket's bind alone, WITHOUT the attachment
    /// line's own turn and shift, because that is what a skinned socketed piece has always been
    /// corrected by - see <see cref="Correcting"/>. Folding the two together would move the
    /// Frostborn Fiend's block of ice twice.
    /// </remarks>
    private static (Matrix4x4 Place, int Bone, Matrix4x4 Put, int Socket)? Socketed(
        string socket,
        Fit fit,
        Matrix4x4 carrier,
        int bone,
        Matrix4x4 put,
        bool top,
        Matrix4x4 local)
    {
        if (socket.Length == 0 || socket.StartsWith('<'))
        {
            return (local * carrier, bone, put, -1);
        }

        if (Mounts(fit.Where, socket, out int at) && at < fit.Rest.BindModel.Count)
        {
            (int onto, Matrix4x4 into) = Through(fit, at);
            Matrix4x4 there = fit.Rest.BindModel[at] * into;
            return (local * there, onto, there, at);
        }

        // A SOCKET THE CARRIER'S OWN FILES DEFINE, where no bone carries its name in any spelling Mounts tries: the player's
        // Character.ao writes socket = "R_Weapon" with parent = "aux_R_Weapon_attachment_jntBnd", a bone his OneRig names with the
        // _jntBnd the monsters' rigs leave off. READ from the files; LAST, so nothing that resolved before moves.
        if (fit.Sockets is { } defined && defined.TryGetValue(socket, out (int Bone, Matrix4x4 Local) named)
            && named.Bone >= 0 && named.Bone < fit.Rest.BindModel.Count)
        {
            (int onto, Matrix4x4 into) = Through(fit, named.Bone);
            Matrix4x4 there = named.Local * fit.Rest.BindModel[named.Bone] * into;
            return (local * there, onto, there, named.Bone);
        }

        return top ? null : (local * carrier, bone, put, -1);
    }

    /// <summary>
    /// The sockets a rig's own .ao files define (see <see cref="BodySockets"/>), each as its bone in the rig and the transform off it:
    /// its size, then its turn about x, y and z (radians, the order the attachment lines' turn takes - <see cref="Local"/>, not
    /// settled), then its shift. Null where none names a bone the rig has.
    /// </summary>
    private static Dictionary<string, (int Bone, Matrix4x4 Local)>? SocketsOf(
        Func<string, byte[]?> read, IReadOnlyList<string> files, AnimationSkeleton rig)
    {
        Dictionary<string, (int Bone, Matrix4x4 Local)>? said = null;
        foreach (string file in files)
        {
            AnimatedObject ao = Object(read, file);
            if (!ao.Ready)
            {
                continue;
            }

            foreach (BodySocket socket in BodySockets.Of(Whole(read, ao), rig))
            {
                if (socket.Bone < 0)
                {
                    continue;
                }

                Matrix4x4 turn = Matrix4x4.CreateScale(socket.Scale)
                    * Matrix4x4.CreateRotationX(socket.Rotation.X)
                    * Matrix4x4.CreateRotationY(socket.Rotation.Y)
                    * Matrix4x4.CreateRotationZ(socket.Rotation.Z)
                    * Matrix4x4.CreateTranslation(socket.Translation);
                (said ??= new Dictionary<string, (int, Matrix4x4)>(StringComparer.OrdinalIgnoreCase)).TryAdd(socket.Name, (socket.Bone, turn));
            }
        }

        return said;
    }

    /// <summary>
    /// The bone of its carrier a piece's own file hangs it at - BaseAnimationEvents' <c>attach_bone_override</c> through its chain - where
    /// it pairs none of its own bones with its carrier's (attachment_bones), or null. Asked only of a line that places nothing ("&lt;root&gt;",
    /// see <see cref="Rooted"/>): which wins over a line naming a socket of its own is not read.
    /// </summary>
    /// <remarks>
    /// READ FROM THE FILES (v0.1.397, the player's capture of 16:16 in The Red Vale): the gloves hang their feathers at "&lt;root&gt;" and each
    /// feather's own .ao says <c>attach_bone_override = "R_elbow_jntBnd"</c> (the left one L_), so they stood at his feet; the robe's skirt says
    /// <c>"hip_jntBnd"</c> and pairs its root with the very same bone by attachment_bones, so a piece that pairs is placed by its pairing and
    /// left as it was.
    /// </remarks>
    private static string? Overridden(Func<string, IReadOnlyList<AnimatedObject>> chainOf, string path)
    {
        IReadOnlyList<AnimatedObject> chain = chainOf(path);
        if (chain.Count == 0)
        {
            return null;
        }

        string bone = Entryed(chain, EventsBlock, OverrideEntry).Trim();
        return bone.Length > 0 && Attaching(chain, out _).Count == 0 ? bone : null;
    }

    /// <summary>The block a piece's own placing words are written in, and the word naming the carrier's bone it hangs at.</summary>
    private const string EventsBlock = "BaseAnimationEvents";

    /// <inheritdoc cref="EventsBlock"/>
    private const string OverrideEntry = "attach_bone_override";

    /// <summary>Whether a piece's chain extends the game's parent of back attachments - the player's cape (GoblinShamanHeld.ao).</summary>
    private static bool OnTheBack(Func<string, byte[]?> read, string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(Object(read, path).Extends);
        while (queue.Count > 0 && seen.Count <= MostDeep * 2)
        {
            string up = queue.Dequeue().Replace('\\', '/').Trim();
            if (up.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase))
            {
                up = up[..^AnimatedObject.Suffix.Length];
            }

            if (up.Length == 0 || !seen.Add(up))
            {
                continue;
            }

            if (string.Equals(up, BackParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string further in Object(read, up).Extends)
            {
                queue.Enqueue(further);
            }
        }

        return false;
    }

    /// <summary>What a back attachment extends, and the socket of the player's body it is hung at (DECIDED from the live client).</summary>
    private const string BackParent = "Metadata/ParentBackAttachment";

    /// <inheritdoc cref="BackParent"/>
    private const string BackSocket = "Back";

    /// <summary>
    /// The turn and the shift an attachment line asks for on top of its socket.
    /// </summary>
    /// <remarks>
    /// TWO LINES THAT WERE BEING THROWN AWAY. An attached_object entry can carry children, and
    /// the Frostborn Fiend's block of ice carries both:
    ///
    ///     attached_object = "R_Weapon …/QuadrillaArcticIceHeld.ao"
    ///         attached_object_translation = "0 0 -55"
    ///         attached_object_rotation = "-3.141 -0 0"
    ///
    /// That is half a turn about x and a shift of fifty-five, and without it the ice he is
    /// holding stands upright on the floor beside him - which is what the pane drew.
    ///
    /// THE AXIS ORDER IS NOT SETTLED. The one sample in hand turns about a single axis, where
    /// order cannot matter, and no open reader of this format reads these lines at all - so x
    /// then y then z is what this applies and what the dump prints, and a monster that turns
    /// about two axes at once is what would settle it. Radians, as written.
    /// </remarks>
    private static Matrix4x4 Local(AoEntry entry)
    {
        Matrix4x4 said = Matrix4x4.Identity;
        foreach (AoEntry child in entry.Children)
        {
            if (string.Equals(child.Key, Turn, StringComparison.Ordinal) && Three(child.Value) is { } turn)
            {
                said *= Matrix4x4.CreateRotationX(turn.X)
                    * Matrix4x4.CreateRotationY(turn.Y)
                    * Matrix4x4.CreateRotationZ(turn.Z);
            }
            else if (string.Equals(child.Key, Shift, StringComparison.Ordinal) && Three(child.Value) is { } shift)
            {
                said *= Matrix4x4.CreateTranslation(shift);
            }
        }

        return said;
    }

    /// <summary>Three numbers separated by spaces, or null where the line is not that.</summary>
    private static Vector3? Three(string said)
    {
        string[] parts = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3
            && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, Invariant, out float x)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, Invariant, out float y)
            && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, Invariant, out float z)
                ? new Vector3(x, y, z)
                : null;
    }

    private static System.Globalization.CultureInfo Invariant
        => System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>The children of an attachment line that move it off its socket.</summary>
    private const string Turn = "attached_object_rotation";

    /// <inheritdoc cref="Turn"/>
    private const string Shift = "attached_object_translation";

    /// <summary>
    /// What each of a piece's bones does to a vertex to put it in the PARENT's bind pose.
    /// </summary>
    /// <remarks>
    /// THE TWO RIGS DO NOT CARRY THE SAME REST POSE, which is the thing three fixes in a row
    /// assumed and Bahlak's dump finally showed. His feathers and his body both name spine_1,
    /// chest and M_head, and rest them nowhere near each other:
    ///
    ///     spine_1_jntBnd   the piece (0,0,-29.1)      the body (0,-1.9,-179.7)
    ///     chest_jntBnd     the piece (0,0,-87.4)      the body (0,-42.4,-221.6)
    ///     M_head_jntBnd    the piece (0,-67,-197.6)   the body (0,-167.2,-254.2)
    ///
    /// Not even a constant offset. The piece's rig is straight - its spine steps a regular 29
    /// units down z with no y at all - and the body's is the crouched owl. So renumbering the
    /// bones and drawing leaves the feathers standing in the PIECE's bind pose while the body
    /// stands in its own, which is a bundle hanging off him rather than on him.
    ///
    /// THE CORRECTION IS THE ONE EVERY SKINNING PIPELINE USES: undo the bind the vertex was
    /// authored in, then apply the bind of the bone it is moving to. It is derived rather than
    /// guessed, and it cannot break what already worked, because it REDUCES to what those cases
    /// already did:
    ///
    ///   - where the two rigs agree, as Malgor's seaweed does, it is the identity;
    ///   - where the piece's rig is the parent's subtree re-rooted on the socket, as the Fallen
    ///     Knight's are, the parent's bind is the piece's bind times the socket's - so it
    ///     cancels down to the socket's transform, which is exactly the old placement.
    ///
    /// A BONE WITH NO MATCH ANYWHERE UP ITS CHAIN keeps the old answer too: its piece-space is
    /// taken as local to whatever it fell back to, which for a socketed piece is the socket and
    /// for a "&lt;root&gt;" piece is the monster's root.
    /// </remarks>
    private static Matrix4x4[] Correcting(
        SkeletonPose mine,
        Fit fit,
        int bones,
        int[] onto,
        int[] anchor,
        bool rooted,
        Matrix4x4 put,
        int spare)
    {
        var into = new Matrix4x4[bones];

        // A SOCKETED PIECE GOES TO ITS SOCKET AND NOWHERE ELSE. Where its rig really is the
        // parent's subtree the arithmetic below gives exactly that anyway - the piece's own bind
        // cancels against the parent's, leaving the socket's - so forcing it costs those pieces
        // nothing and protects the ones whose rig only LOOKS like the parent's. The Frostborn
        // Fiend's block of ice is three bones all resting at the origin, one of them called
        // aux_position, which the parent also has somewhere up his body; corrected by name it
        // would leave his fist for that bone, which is the 0.1.13 regression in a new shape.
        if (!rooted)
        {
            Array.Fill(into, put);
            return into;
        }

        for (var one = 0; one < bones; one++)
        {
            // THE CARRIER'S BIND, AND THEN THE CARRIER'S OWN WAY INTO MODEL SPACE. For a piece on
            // the monster himself the second half is the identity and this is what it always was;
            // for one hung off another piece it is what carries Tycho's skirt layers up onto his
            // skirt instead of leaving them where the body's rig happens to put the same names.
            int said = onto[one] >= 0 ? onto[one] : spare;
            int at = said >= 0 && said < fit.Rest.BindModel.Count ? said : 0;
            (int _, Matrix4x4 over) = Through(fit, at);
            Matrix4x4 there = at < fit.Rest.BindModel.Count
                ? fit.Rest.BindModel[at] * over
                : Matrix4x4.Identity;

            into[one] = anchor[one] >= 0
                && anchor[one] < mine.BindModel.Count
                && Matrix4x4.Invert(mine.BindModel[anchor[one]], out Matrix4x4 back)
                    ? back * there
                    : there;
        }

        return into;
    }

    /// <summary>
    /// A piece's geometry moved out of its own bind pose and into the parent's.
    /// </summary>
    /// <remarks>
    /// BAKED ONCE, HERE, rather than handed to the renderer: the correction is per BONE and the
    /// join takes one transform per piece, so the vertices are moved now and everything
    /// downstream goes on working on a mesh that is already in the monster's space.
    ///
    /// BLENDED BY THE VERTEX'S OWN WEIGHTS, the same way a pose is - a vertex held half by the
    /// chest and half by the neck is corrected half by each, and a seam between two bones stays
    /// a seam rather than tearing.
    /// </remarks>
    private static SkinnedMesh Corrected(SkinnedMesh mesh, IReadOnlyList<Matrix4x4> into)
    {
        int count = mesh.Positions.Length;
        if (into.Count == 0 || mesh.Bones.Length != count * 4 || mesh.Weights.Length != count * 4)
        {
            return mesh;
        }

        var positions = new Vector3[count];
        var normals = new Vector3[count];
        Vector3 least = new(float.MaxValue), most = new(float.MinValue);

        for (var one = 0; one < count; one++)
        {
            Matrix4x4 sum = default;
            var total = 0f;
            for (var slot = 0; slot < 4; slot++)
            {
                byte weight = mesh.Weights[(one * 4) + slot];
                byte bone = mesh.Bones[(one * 4) + slot];
                if (weight == 0 || bone >= into.Count)
                {
                    continue;
                }

                sum += into[bone] * (weight / 255f);
                total += weight / 255f;
            }

            Matrix4x4 put = total > 0.0001f ? sum * (1f / total) : Matrix4x4.Identity;
            positions[one] = Vector3.Transform(mesh.Positions[one], put);
            normals[one] = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[one], put));
            least = Vector3.Min(least, positions[one]);
            most = Vector3.Max(most, positions[one]);
        }

        return SkinnedMesh.Of(
            positions, normals, mesh.Indices, least, most, mesh.Coordinates, mesh.Shapes, mesh.Facts, mesh.Colours, mesh.Coloured);
    }

    /// <summary>
    /// Whether an attachment line names no socket at all - <c>&lt;root&gt;</c>, or nothing.
    /// </summary>
    /// <remarks>
    /// THE GAME'S OWN SPELLING DECIDES, which is worth more than any threshold this could invent.
    /// A line that names a bone is PLACING the piece there: the anchor at aux_anchor_jntBnd, the
    /// beard at head_jntBnd, the Frostborn Fiend's block of ice at R_Weapon. A line that says
    /// "&lt;root&gt;" places nothing, so the piece has to carry its own placement - and the only
    /// thing it can carry it in is a rig of the parent's bones.
    ///
    /// WHAT THIS REPLACED, and why: two goes at reading the piece's rig INSTEAD of the line, and
    /// each broke a monster the other fixed. Counting shared bone names made the ice - three
    /// bones, of which the parent has two - into a skinned cloak and stood it upright on the
    /// floor. Requiring the shared bones to rest in the same place as the parent's then rejected
    /// Bahlak's feathers, which are the clearest skinned piece there is, and dropped them on the
    /// floor in three flat clumps. The line was carrying the answer the whole time.
    /// </remarks>
    private static bool Rooted(string socket)
        => socket.Length == 0 || socket.StartsWith('<');

    /// <summary>The socket and file of every .ao this one hangs off itself.</summary>
    /// <remarks>
    /// A SOCKET AND THEN A PATH INSIDE ONE PAIR OF QUOTES, which taken whole is a path no install
    /// has - the trap that cost AoSurvey 2289 of its 3262 files. Split on the first space; the
    /// socket is a bone name of the parent's rig, or <c>&lt;root&gt;</c> for the piece's own.
    ///
    /// A LINE MAY END IN AN ALIAS - <c>"&lt;root&gt; Metadata/Characters/Int/attachments/Outfits/SorceressOutfit/SorceressBody.ao alias Body_Attach"</c>,
    /// the player's default outfit - the name a worn piece's ObjectHiding leaves it off by (<see cref="PieceHiding"/>). Read whole the
    /// path ended in the alias and not in ".ao", and every such line was dropped.
    /// </remarks>
    private static IEnumerable<Line_> Hung(AnimatedObject ao)
    {
        foreach (AoStruct block in ao.Structs)
        {
            foreach (AoEntry entry in block.Entries)
            {
                if (Array.IndexOf(Hangs, entry.Key) < 0 || entry.Kind != AoValueKind.Quoted)
                {
                    continue;
                }

                string said = entry.Value.Trim();
                int space = said.IndexOf(' ', StringComparison.Ordinal);
                string path = space < 0 ? said : said[(space + 1)..].Trim();
                string alias = string.Empty;
                int named = path.IndexOf(AliasWord, StringComparison.Ordinal);
                if (named > 0)
                {
                    alias = path[(named + AliasWord.Length)..].Trim();
                    path = path[..named].Trim();
                }

                if (path.EndsWith(AnimatedObject.Suffix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new Line_(space < 0 ? string.Empty : said[..space], path, Local(entry), alias);
                }
            }
        }
    }

    /// <summary>What an attached_object line names its alias after.</summary>
    private const string AliasWord = " alias ";

    /// <summary>One attachment read whole: its mesh, its textures, and where on the body it goes.</summary>
    /// <remarks>
    /// RIGID AT ITS SOCKET, which is what the boxes said to do. Every one of Doryani's thirteen
    /// pieces has a box a few tens of units across sitting on the origin - the left and right
    /// shoulder pieces mirrored in x rather than standing apart - so each is modelled in its own
    /// space and means nothing in the monster's until the socket bone's rest transform is on it.
    ///
    /// WHY NOT POSE IT WITH ITS OWN RIG. A piece has one, and in the game it is simulated - the
    /// .ao carries bend stiffness, viscosity and an enclosure angle. At rest that rig puts the
    /// vertices exactly where the file already has them, so the rest pose IS the mesh, and
    /// hanging it rigidly off the bone is the same answer with none of the work. What it gives
    /// up is cloth that swings while the monster moves, which a turning picture does not miss.
    /// </remarks>
    private static (Part? Part, Fit Under, PartFit Told) Worn(
        Func<string, byte[]?> read,
        AnimatedObject ao,
        Hangs_ hung,
        Paints paints,
        Mipmaps? fallback,
        Fit body,
        Riding riding)
    {
        (_, Matrix4x4 place, int bone, Matrix4x4 put, int socket, _, _, bool rooted, Fit fit) = hung;

        // WHERE THE SOCKET LANDED, in the walk's own terms - see PartPlace. Astray is the nested
        // piece whose socket neither rig has; Dropped cannot appear here, because such a piece
        // was never queued.
        PartPlace where = rooted ? PartPlace.Root : socket >= 0 ? PartPlace.Bone : PartPlace.Astray;

        // THE PIECE AND EVERYTHING IT EXTENDS - see Whole. Gulzal's hammer is four lines and a
        // parent, and reading the four lines alone left it on the floor.
        List<AnimatedObject> chain = Whole(read, ao);

        string skin = Skin(chain);
        if (skin.Length == 0)
        {
            if (hung.SkinOnly)
            {
                return (null, body, Told(hung, where, PartKind.None, default, null, Unsocketed));
            }

            Part? prop = Propped(read, chain, place, bone, paints, fallback);
            return (
                prop,
                body,
                Told(hung, where, prop is null ? PartKind.None : PartKind.Prop, default, prop));
        }

        MeshManifest manifest = Read(read, skin, MeshManifest.Read);
        if (!manifest.Ready)
        {
            return (null, body, Told(hung, where, PartKind.Unread, default, null, "the manifest: " + skin));
        }

        SkinnedMesh mesh = Read(read, manifest.Geometry, SkinnedMesh.Read);
        if (!mesh.Ready)
        {
            // THE READER'S OWN REASON, not just that it failed. "version 1 puts its geometry
            // outside a ..." names a format gap; a missing file names an install. A corpus that
            // cannot tell those apart cannot say whether 113 monsters are 113 problems or one.
            return (null, body, Told(
                hung, where, PartKind.Unread, default, null,
                mesh.Why.Length > 0 ? mesh.Why : "the geometry: " + manifest.Geometry));
        }

        Dress dress = Dressed(read, mesh, [], manifest, fallback, string.Empty, paints);

        // A HELD PIECE GOES RIGIDLY AT ITS SOCKET, its mesh as its file rests it (see WornPiece.Held): the player's wand at R_Weapon, his
        // focus at L_Weapon, his cape at Back - none of their own rigs is matched against his.
        if (hung.Held)
        {
            (byte[] held, byte[] heldWeights) = Bound(mesh.Positions.Length, bone);
            var kept = new Part(mesh, held, heldWeights, place, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials };
            return (kept, body, Told(hung, where, PartKind.Rigid, default, kept));
        }

        // EITHER WAY THE PIECE'S OWN RIG SAYS WHICH OF THE PARENT'S BONES EACH VERTEX BELONGS
        // TO - see Retargeted. What the attachment line decides is where the piece SITS: a
        // "<root>" piece is already in the monster's space and wants no transform, a socketed
        // one is in its socket's space and wants that bone's rest transform on it.
        Retarget? said = Retargeted(read, chain, fit, mesh, rooted, put, socket, bone, out Retold counted);
        if (said is { } map)
        {
            // NO PLACE: the correction already carries wherever the piece belongs, because a
            // socketed piece's own bind cancels down to exactly its socket's transform.
            var worn = new Part(Corrected(mesh, map.Into), map.Bones, mesh.Weights, null, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials };
            return (worn, map.Under, Told(hung, where, PartKind.Skin, counted, worn));
        }

        // A PIECE TO BE DRAWN AS A SKIN ALONE that is none - the player's held pieces, whose socket no file names - is left out, said why.
        if (hung.SkinOnly)
        {
            return (null, body, Told(hung, where, PartKind.None, counted, null, Unsocketed));
        }

        // A "<root>" PIECE ON A RIG OF ITS OWN WHOSE ANIMATIONS CARRY HIS NAMES RIDES HIS RIG: its vertices keep its own bones,
        // numbered after his, and its own animation of each name plays in step with his - see AnimationSkeleton.Carrying. Malgor's
        // ship's wheel, which every other answer here left at his origin while the game hangs it round his neck.
        if (rooted && hung.Depth == 1
            && Entryed(chain, RigBlock, RigEntry) is { Length: > 0 } own
            && riding.Take(Read(read, own, AnimationSkeleton.Read), mesh) is >= 0 and int first)
        {
            var moved = new byte[mesh.Bones.Length];
            for (var one = 0; one < moved.Length; one++)
            {
                moved[one] = (byte)Math.Min(first + mesh.Bones[one], byte.MaxValue);
            }

            var ridden = new Part(mesh, moved, mesh.Weights, null, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials };
            return (ridden, body, Told(hung, where, PartKind.Played, counted, ridden));
        }

        (byte[] bones, byte[] weights) = Bound(mesh.Positions.Length, bone);
        var rigid = new Part(mesh, bones, weights, place, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials };
        return (rigid, body, Told(hung, where, PartKind.Rigid, counted, rigid));
    }

    /// <summary>Why a piece to be drawn as a skin alone (WornPiece.SkinOnly) was left out.</summary>
    internal const string Unsocketed =
        "a piece of a rig or a mesh of its own at no socket read - left out rather than drawn at his feet";

    /// <summary>
    /// The rigs of the pieces that play their own animations, gathered as the monster is dressed, each numbered after his bones and
    /// those gathered before it - see AnimationSkeleton.Carrying.
    /// </summary>
    private sealed class Riding(AnimationSkeleton parent)
    {
        private readonly HashSet<string> _names = new(parent.Animations.Select(one => one.Name), StringComparer.Ordinal);
        private int _next = parent.Bones.Count;

        /// <summary>The rigs taken, in the order their bones follow his.</summary>
        public List<AnimationSkeleton> Rigs { get; } = [];

        /// <summary>
        /// The number a piece's own rig's first bone takes after his, or -1 where it does not ride: its rig does not hold together,
        /// its mesh is not weighted to it, the palette would pass the format's limit, or none of its animations carries one of his names.
        /// </summary>
        public int Take(AnimationSkeleton own, SkinnedMesh mesh)
        {
            if (!own.Ready || SkeletonPose.Of(own) is null || mesh.Bones.Length != mesh.Positions.Length * 4
                || SkeletonPose.Highest(mesh) >= own.Bones.Count || _next + own.Bones.Count > AnimationSkeleton.MostBones
                || !own.Animations.Any(one => _names.Contains(one.Name)))
            {
                return -1;
            }

            int first = _next;
            _next += own.Bones.Count;
            Rigs.Add(own);
            return first;
        }
    }

    /// <summary>One line of the walk's account of itself - see <see cref="PartFit"/>.</summary>
    /// <remarks>
    /// THE BOX IS TAKEN AFTER THE CORRECTION and through the piece's own <see cref="Part.Place"/>
    /// where it has one, so what it says is where the piece ends up in MODEL space. That is the
    /// number a sweep can compare against the body's own box without a picture - a piece whose
    /// box sits at the origin under a monster who does not is the shape of every report this
    /// month.
    /// </remarks>
    private static PartFit Told(
        Hangs_ hung, PartPlace where, PartKind kind, Retold counted, Part? part, string why = "")
    {
        (Vector3 least, Vector3 most) = part is { } made
            ? made.Place is { } put
                ? (Vector3.Transform(made.Mesh.Least, put), Vector3.Transform(made.Mesh.Most, put))
                : (made.Mesh.Least, made.Mesh.Most)
            : (Vector3.Zero, Vector3.Zero);

        return new PartFit(
            hung.Path,
            hung.Named,
            where,
            hung.Depth,
            kind,
            counted.Bones,
            counted.Matched,
            counted.Strays,
            counted.Paired,
            counted.Malformed,
            counted.Paths,
            counted.Past,
            why,
            Vector3.Min(least, most),
            Vector3.Max(least, most));
    }

    /// <summary>
    /// A piece's vertex bones moved onto the PARENT's rig, or null where it is a rigid prop.
    /// </summary>
    /// <remarks>
    /// TWO KINDS OF ATTACHMENT WEAR THE SAME SPELLING, and the piece's own skeleton is what tells
    /// them apart. Measured, on five pieces across two bosses:
    ///
    ///     Malgor's anchor    12 bones, one shared with the parent - root_jntBnd
    ///     Malgor's beard      8 bones, one shared - root_jntBnd
    ///     Malgor's ship wheel 10 bones, one shared - root_jntBnd
    ///     Malgor's seaweed   23 bones, TWELVE shared - hip, spine_1, spine_2, chest, clavicles…
    ///     Bahlak's feathers  31 bones, ELEVEN shared - spine_1, spine_2, chest, neck, head…
    ///
    /// A PROP SHARES ONLY THE ROOT, which every rig has and which therefore means nothing. Its
    /// bones are its own - phys_tri_chain, jaw_jntBnd_1..8 - it is modelled around its own origin,
    /// and it belongs rigidly at its socket. That is the case <see cref="Bound"/> handles and it
    /// has been right all along.
    ///
    /// A SKINNED PIECE SHARES THE BODY'S OWN BONES, and its rig rests them where the body's rest:
    /// the seaweed's hip_jntBnd at (0,18.7,-97.6), its chest at (0,26.3,-171.6). Its vertices are
    /// ALREADY in the monster's space - the seaweed's box is y 40..131, draped over a spine that
    /// sits at y 19..26 - so it wants no transform at all. What it wants is its bone NUMBERS
    /// translated, because bone 9 in the piece's file is the parent's bone 47.
    ///
    /// WHY THAT WAS THE WHOLE BUG. Binding every vertex to one bone is harmless at rest and wrong
    /// the moment anything moves: the body deforms and the piece rigidly follows a single bone.
    /// For a socketed piece that bone is an arm or a head and it looks right; for a "&lt;root&gt;"
    /// piece it is the RIG ROOT, which is the one bone that does not follow the body at all. So
    /// Bahlak's feathers stayed on the floor while he rose, and Malgor's seaweed hung in the air
    /// behind him - and only the "&lt;root&gt;" pieces ever did, which is exactly what was reported.
    ///
    /// A BONE THE PARENT DOES NOT HAVE goes to its nearest ancestor that the parent does have.
    /// The unshared ones are all simulated - phys_skinned_M_skirt_2, phys_tri_seaweed_belt - so
    /// their nearest real ancestor is the spine or chest they hang off, which is where a tool
    /// that does not simulate cloth should hold them.
    ///
    /// ONLY EVER FOR A PIECE THAT NAMES NO SOCKET - see <see cref="Rooted"/>. The rig's shared
    /// names alone are NOT enough, and two goes at making them enough each broke a monster the
    /// other fixed: counting them turned the Frostborn Fiend's block of ice, three bones of which
    /// the parent has two, into a cloak and stood it on the floor; requiring the shared bones to
    /// rest where the parent's rest then rejected Bahlak's feathers and dropped them in three
    /// flat clumps. The attachment line was carrying the answer the whole time.
    ///
    /// WHAT IS STILL ASKED OF THE RIG is only the other half: a "&lt;root&gt;" piece that shares
    /// nothing but the root has no bones to be moved onto. Malgor's ship's wheel is that piece, and
    /// from v0.1.329 it rides his rig and plays its own animations in step - see Riding.
    /// </remarks>
    private readonly record struct Retarget(byte[] Bones, Matrix4x4[] Into, Fit Under);

    /// <summary>
    /// What the retarget found, whether or not it could use it - see <see cref="PartFit"/>.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="Retarget"/> ON PURPOSE. The cases that return no retarget at all
    /// are the ones a sweep over the whole table is hunting for - a piece with a rig of its own
    /// and not one bone in common is Malgor's ship's wheel, left at the origin - and a report
    /// carried on the return value would be silent about exactly those.
    /// </remarks>
    private readonly record struct Retold(
        int Bones, int Matched, int Strays, int Paired, bool Malformed, int Paths, bool Past);

    private static Retarget? Retargeted(
        Func<string, byte[]?> read,
        IReadOnlyList<AnimatedObject> chain,
        Fit fit,
        SkinnedMesh mesh,
        bool rooted,
        Matrix4x4 put,
        int socket,
        int fall,
        out Retold told)
    {
        told = default;

        string path = Entryed(chain, RigBlock, RigEntry);
        bool weighted = mesh.Bones.Length == mesh.Positions.Length * 4;

        // A SKIN WITH NO RIG OF ITS OWN, at its carrier's root: every armour piece the player wears - BetaDoryaniArmourBodyInt.ao extends
        // Metadata/SkinBodyArmour, which extends Metadata/Skin, and no file of the chain names a skeleton - so the only rig its weighted
        // vertices can name is its carrier's, and its numbers are taken as that rig's, as a mesh indexed past its own rig's are (below).
        // READ, not measured: bound rigidly to the root before, it stood right at rest and stayed behind as the body moved.
        if (path.Length == 0)
        {
            if (rooted && weighted && mesh.Positions.Length > 0 && SkeletonPose.Highest(mesh) < fit.Onto.Length)
            {
                told = told with { Past = true };
                return new Retarget(fit.Plain ? mesh.Bones : Passed(fit, mesh.Bones), fit.Into, fit);
            }

            return null;
        }

        if (!weighted)
        {
            return null;
        }

        AnimationSkeleton own = Read(read, path, AnimationSkeleton.Read);
        if (!own.Ready || SkeletonPose.Of(own) is not { } mine)
        {
            return null;
        }

        // COUNTED WHATEVER THIS RETURNS, because the cases that return nothing are exactly the
        // ones a sweep is looking for: a piece left at the origin says so by having a rig and no
        // matches at all. See PartFit.
        var paths = 0;
        foreach (SkeletonBone bone in own.Bones)
        {
            if (bone.Name.Contains('|', StringComparison.Ordinal))
            {
                paths++;
            }
        }

        told = told with { Bones = own.Bones.Count, Paths = paths };

        // A MESH THAT REACHES PAST ITS OWN RIG WAS NOT RIGGED TO IT, and putting its numbers
        // through a table that short is how the last of Bahlak's feathers stayed on the floor:
        // every bone the table did not cover fell to entry nought, the rig root, while the rest
        // of the piece followed his chest - so the bundle came out stretched between the two.
        //
        // THE ONLY OTHER RIG IN PLAY IS THE PARENT'S, and a piece socketed "<root>" is already in
        // the parent's space, so a mesh indexed past its own rig is a mesh indexed by the
        // parent's: its numbers are already the right ones and want passing through untouched.
        // Mapping them to nought is the one answer that is certainly wrong.
        if (SkeletonPose.Highest(mesh) >= own.Bones.Count)
        {
            // A MESH INDEXED PAST ITS OWN RIG WAS NOT RIGGED TO IT, so the only rig its numbers
            // can mean is the parent's - and then they are already right and want no correction.
            // A socketed piece is moved by its socket as well, so there the old rigid binding
            // stays: applying both would move it twice.
            //
            // "THE PARENT'S" IS THE CARRIER'S, which on the monster himself is the monster's and
            // is where this stops: the numbers pass through fit.Onto, which is the identity there.
            told = told with { Past = true, Matched = own.Bones.Count, Strays = 0 };
            return rooted
                ? new Retarget(fit.Plain ? mesh.Bones : Passed(fit, mesh.Bones), fit.Into, fit)
                : null;
        }

        // THE FILE'S OWN PAIRING FIRST, where it wrote one down - see Attaching.
        Dictionary<string, string> named = Attaching(chain, out bool malformed);
        told = told with { Paired = named.Count, Malformed = malformed };

        // NUMBERS OF THE CARRIER'S RIG, not the monster's: what this walk matches against is the
        // rig the piece was authored beside, and the step into model space is Fit's - see there.
        // Minus one means nothing up the chain matched, which is a different answer from "the
        // root matched" and was told apart by neither array before.
        var onto = new int[own.Bones.Count];
        var anchor = new int[own.Bones.Count];
        var shared = 0;
        for (var one = 0; one < own.Bones.Count; one++)
        {
            // UP THE CHAIN BY NAME, NOT BY NUMBER. A first attempt read the parent's answer out
            // of the array on the reasoning that a parent is always earlier in the list, and it
            // is not: SkeletonPose walks the child-and-sibling tree with a stack, so a bone's
            // parent can perfectly well carry a HIGHER number. Bahlak's feathers are the case -
            // phys_skinned_head_feathers_1 is bone 19 and hangs off M_head_jntBnd, bone 22 - and
            // every bone that lost that race fell to the root instead. The root is the one bone
            // that does not follow the body, so the piece came out stretched between the monster
            // and the origin: a long black spike from his chest to the floor.
            // THE PIECE'S OWN ROOT IS THE SOCKET, on a socketed piece, whatever it is called.
            // Measured: the Fallen Knight's bicep fingers carry root_jntBnd AND L_shoulder_jntBnd
            // and both rest at (0,0,0) - the piece's origin - while the parent rests its shoulder
            // at (22.3,7,-157.9). Matching that root by NAME would send every vertex weighted to
            // it across to the monster's own root, which is the far end of him.
            onto[one] = -1;
            anchor[one] = -1;

            int at = one;
            for (var hops = 0; at >= 0 && hops <= own.Bones.Count; hops++)
            {
                // A BONE THE FILE PAIRED IS ANSWERED WHATEVER ELSE IS TRUE OF IT, the piece's
                // own rig root included - which is the one the pairing usually renames.
                bool paired = named.TryGetValue(own.Bones[at].Name, out string? theirs);
                bool grown = paired || (at < mine.Parents.Count && mine.Parents[at] >= 0);
                if (grown
                    && Meant(fit.Where, theirs ?? own.Bones[at].Name, out int found)
                    && found <= byte.MaxValue)
                {
                    onto[one] = found;
                    anchor[one] = at;
                    shared++;
                    break;
                }

                at = at < mine.Parents.Count ? mine.Parents[at] : -1;
            }
        }

        // A "<root>" PIECE WITH NOTHING SHARED HAS NOWHERE TO GO here - Malgor's ship's wheel, which
        // rides his rig instead where its own animations carry his names (Worn, Riding). A SOCKETED piece always
        // has somewhere: its socket, which is where it used to go wholesale, so the map can only
        // improve on that and is always worth returning.
        // A BONE THAT MATCHED NOTHING AND IS NOT A ROOT OF ITS OWN RIG - see PartFit.Strays.
        // The rig root is left out because it never matches by design: a socketed piece's own
        // root IS its socket, whatever it is called, and counting it would make "some of this
        // piece fell back" true of very nearly every piece there is.
        var strays = 0;
        for (var one = 0; one < onto.Length; one++)
        {
            if (onto[one] < 0 && one < mine.Parents.Count && mine.Parents[one] >= 0)
            {
                strays++;
            }
        }

        told = told with { Matched = shared, Strays = strays };
        if (rooted && shared == 0)
        {
            return null;
        }

        // WHERE A BONE MATCHED NOTHING it keeps the answer it always had: the piece's socket on a
        // socketed piece, and the carrier's own root on a "<root>" one. Both are then read as
        // bones of the CARRIER's rig and stepped into the monster's, which on the monster himself
        // leaves them exactly as they were.
        int spare = rooted ? 0 : socket;
        var over = new byte[own.Bones.Count];
        for (var one = 0; one < over.Length; one++)
        {
            over[one] = onto[one] >= 0 || spare >= 0
                ? (byte)Through(fit, onto[one] >= 0 ? onto[one] : spare).Bone
                : (byte)fall;
        }

        var bones = new byte[mesh.Bones.Length];
        for (var one = 0; one < bones.Length; one++)
        {
            byte said = mesh.Bones[one];
            bones[one] = said < over.Length ? over[said] : (byte)0;
        }

        Matrix4x4[] into = Correcting(mine, fit, own.Bones.Count, onto, anchor, rooted, put, spare);
        return new Retarget(bones, into, new Fit(Where(own), mine, over, into));
    }

    /// <summary>A rig's bones by name, first number wins - the table every fit is looked up in.</summary>
    private static Dictionary<string, int> Where(AnimationSkeleton rig)
    {
        var said = new Dictionary<string, int>(rig.Bones.Count, StringComparer.OrdinalIgnoreCase);
        for (var one = 0; one < rig.Bones.Count; one++)
        {
            said.TryAdd(rig.Bones[one].Name, one);
        }

        return said;
    }

    /// <summary>
    /// A piece that is a rigid prop rather than a skin: a <c>FixedMesh</c> naming a <c>.fmt</c>.
    /// </summary>
    /// <remarks>
    /// THE OTHER KIND OF ATTACHMENT, and the one the walk used to throw away. An .ao hangs either
    /// a SkinMesh - a .sm and then a .smd - or a FixedMesh, which names a .fmt and nothing else;
    /// asking only for the first meant every prop came back as "no SkinMesh" and was skipped.
    /// Reported from the live client: Malgor, the Nautilord carries a cannon the pane never drew,
    /// and his attachment's whole .ao is four lines with <c>fixed_mesh</c> in the middle of them.
    ///
    /// NO MANIFEST, BECAUSE A .fmt CARRIES ITS OWN MATERIALS - one .mat per shape, by name. That
    /// is the same list the .ao's own material lines take, so it goes down the path that is
    /// already there: <see cref="Dressed"/> matches a shape to a material by name first, and an
    /// empty manifest simply has nothing to add underneath it.
    /// </remarks>
    private static Part? Propped(
        Func<string, byte[]?> read,
        IReadOnlyList<AnimatedObject> chain,
        Matrix4x4 place,
        int bone,
        Paints paints,
        Mipmaps? fallback)
    {
        string path = Entryed(chain, PropBlock, PropEntry);
        if (path.Length == 0)
        {
            // An effect pack or a sound emitter. Ordinary, and nothing to draw.
            return null;
        }

        FixedMesh prop = Read(read, path, FixedMesh.Read);
        if (!prop.Ready)
        {
            return null;
        }

        Dress dress = Dressed(
            read, prop.Mesh, prop.Named, MeshManifest.None, fallback, string.Empty, paints);
        (byte[] bones, byte[] weights) = Bound(prop.Mesh.Positions.Length, bone);
        return new Part(prop.Mesh, bones, weights, place, dress.Skins, dress.Modes) { Wearing = dress.ShapeMaterials };
    }

    /// <summary>
    /// One bone, all the weight - the skin a rigid piece gets.
    /// </summary>
    /// <remarks>
    /// The piece moves with its socket and nothing else, so every vertex names that bone four
    /// times over with the whole 255 on the first, which is what SkeletonPose.Move expects and
    /// what makes the piece follow an arm that lifts.
    /// </remarks>
    private static (byte[] Bones, byte[] Weights) Bound(int count, int bone)
    {
        var bones = new byte[count * 4];
        var weights = new byte[count * 4];
        var at = bone is >= 0 and <= byte.MaxValue ? (byte)bone : (byte)0;
        for (var one = 0; one < count; one++)
        {
            bones[one * 4] = at;
            bones[(one * 4) + 1] = at;
            bones[(one * 4) + 2] = at;
            bones[(one * 4) + 3] = at;
            weights[one * 4] = 255;
        }

        return (bones, weights);
    }

    /// <summary>
    /// A piece and everything it extends, nearest first.
    /// </summary>
    /// <remarks>
    /// AN ATTACHMENT'S .ao EXTENDS ANOTHER JUST AS A MONSTER'S DOES, and reading only the file
    /// the attachment line named is how Gulzal's hammer came to lie on the floor. Its whole file
    /// is four lines - it extends the axe and swaps one skin for another - while the rig, the
    /// bone pairing and everything else that says where it goes are in the axe's.
    ///
    /// The body's walk has followed extends from the start; the pieces' did not, which is the
    /// kind of gap that only shows on the one monster built that way.
    /// </remarks>
    /// <summary>
    /// A piece's OWN rig and mesh, as the chain of its .ao names them - for the capture's monster-files part, which plays a piece by its
    /// own rig to see where its animations put it. Nothing here draws.
    /// </summary>
    /// <remarks>
    /// MALGOR'S SHIP'S WHEEL ASKED IT (v0.1.328): a "&lt;root&gt;" piece sharing no bone but the root, left at his origin - while in the
    /// game it hangs round his neck in his second phase (said from the live client). Its rig of ten bones carries animations of its own,
    /// which nothing here has ever played. The mesh is the piece's skin, or its fixed mesh where it is a prop.
    /// </remarks>
    internal static (AnimationSkeleton Rig, string RigPath, SkinnedMesh Mesh) Piece(Func<string, byte[]?> read, string path)
    {
        AnimatedObject ao = Object(read, path);
        if (!ao.Ready)
        {
            return (AnimationSkeleton.None, string.Empty, SkinnedMesh.None);
        }

        List<AnimatedObject> chain = Whole(read, ao);
        string rig = Entryed(chain, RigBlock, RigEntry);
        string skin = Skin(chain);
        SkinnedMesh mesh = SkinnedMesh.None;
        if (skin.Length > 0)
        {
            MeshManifest manifest = Read(read, skin, MeshManifest.Read);
            mesh = manifest.Ready ? Read(read, manifest.Geometry, SkinnedMesh.Read) : SkinnedMesh.None;
        }
        else if (Entryed(chain, PropBlock, PropEntry) is { Length: > 0 } prop)
        {
            mesh = Read(read, prop, FixedMesh.Read).Mesh;
        }

        return (rig.Length > 0 ? Read(read, rig, AnimationSkeleton.Read) : AnimationSkeleton.None, rig, mesh);
    }

    /// <summary>The bone a socket names in the monster's own rig, the mounts it spells out in full included - see <see cref="Mounts"/>.</summary>
    internal static int SocketOf(AnimationSkeleton rig, string socket)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var where = new Dictionary<string, int>(rig.Bones.Count, StringComparer.OrdinalIgnoreCase);
        for (var one = 0; one < rig.Bones.Count; one++)
        {
            where.TryAdd(rig.Bones[one].Name, one);
        }

        return socket.Length > 0 && Mounts(where, socket, out int at) ? at : -1;
    }

    private static List<AnimatedObject> Whole(Func<string, byte[]?> read, AnimatedObject ao)
    {
        var chain = new List<AnimatedObject> { ao };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(ao.Extends);

        while (queue.Count > 0 && chain.Count <= MostDeep * 2)
        {
            string path = queue.Dequeue();
            if (path.Length == 0 || !seen.Add(path))
            {
                continue;
            }

            AnimatedObject over = Object(read, path);
            if (!over.Ready)
            {
                continue;
            }

            chain.Add(over);
            foreach (string up in over.Extends)
            {
                queue.Enqueue(up);
            }
        }

        return chain;
    }

    /// <summary>
    /// The skin a piece really wears, once what it inherits and what it drops are both counted.
    /// </summary>
    /// <remarks>
    /// remove_skin IS WHY THE CHAIN CANNOT JUST BE UNIONED. Gulzal's hammer extends his axe and
    /// says, in the same block, to take the axe's mesh away and put the hammer's on - so a walk
    /// that added every skin it found would hang an axe off him as well.
    /// </remarks>
    private static string Skin(IReadOnlyList<AnimatedObject> chain)
    {
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        foreach (AnimatedObject one in chain)
        {
            foreach (AoStruct block in one.Named(Block))
            {
                foreach (AoEntry entry in block.Entries)
                {
                    if (entry.Value.Length == 0)
                    {
                        continue;
                    }

                    if (string.Equals(entry.Key, Dropped, StringComparison.Ordinal))
                    {
                        gone.Add(entry.Value);
                    }
                    else if (string.Equals(entry.Key, Entry, StringComparison.Ordinal))
                    {
                        kept.Add(entry.Value);
                    }
                }
            }
        }

        foreach (string one in kept)
        {
            if (!gone.Contains(one))
            {
                return one;
            }
        }

        return string.Empty;
    }

    /// <summary>What a piece says to stop wearing out of whatever it extends.</summary>
    private const string Dropped = "remove_skin";

    /// <summary>One entry's value, taken from the nearest file in the chain that has it.</summary>
    private static string Entryed(IReadOnlyList<AnimatedObject> chain, string block, string key)
    {
        foreach (AnimatedObject one in chain)
        {
            if (Entryed(one, block, key) is { Length: > 0 } said)
            {
                return said;
            }
        }

        return string.Empty;
    }

    /// <summary>The <c>skin</c> a SkinMesh block names, or empty where the file has none.</summary>
    private static string Skin(AnimatedObject ao) => Entryed(ao, Block, Entry);

    /// <summary>
    /// Which of the PARENT's bones each of a piece's own bones is, where the file says so outright.
    /// </summary>
    /// <remarks>
    /// THE GAME WRITES THE MAPPING DOWN, and matching by name was a workaround for not having
    /// read it. Bahlak's feathers carry both halves of it:
    ///
    ///     attachment_bones = "hip_jntBnd   L_thigh_jntBnd … M_jaw_jntBnd child_attach"
    ///     bone_group = "child_attach false root_jntBnd L_thigh_jntBnd … M_jaw_jntBnd"
    ///
    /// The lists are PARALLEL: attachment_bones names the parent's bones, the bone group it ends
    /// with names the piece's, and the two run in step. Everything in the middle agrees - which
    /// is why matching by name got most of the piece right - and the FIRST entry does not. The
    /// piece's root_jntBnd is the parent's hip_jntBnd.
    ///
    /// WHICH IS THE WHOLE OF THE REMAINING BUG. The piece's phys_skinned_*_skirt_* chain hangs
    /// off that root and off nothing else, so matched by name it went to the monster's root -
    /// the ground between his feet - while the rest of the piece was corrected onto his body,
    /// and the train came out as a beam between the two.
    ///
    /// MEASURED, TOO: the parent rests L_thigh_jntBnd at (23.6,18.4,-158.8) and the piece rests
    /// it at (23.6,0,0), which is exactly its offset from a hip - not from the ground.
    ///
    /// Empty where the file says nothing, and then the names are all there is to go on.
    /// </remarks>
    private static Dictionary<string, string> Attaching(
        IReadOnlyList<AnimatedObject> chain, out bool malformed)
    {
        malformed = false;

        var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] theirs = Entryed(chain, RigBlock, Attachment)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (theirs.Length < 2)
        {
            return said;
        }

        // The last name is the BONE GROUP that carries the piece's own side of the pairing,
        // and it may sit in a file further up the chain than the line that named it.
        string group = theirs[^1];
        foreach (AoStruct block in chain.SelectMany(one => one.Named(GroupBlock)))
        {
            foreach (AoEntry entry in block.Entries)
            {
                if (!string.Equals(entry.Key, GroupEntry, StringComparison.Ordinal))
                {
                    continue;
                }

                // "<name> <a flag nothing here reads> <bone> <bone> …"
                string[] mine = entry.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (mine.Length < 3 || !string.Equals(mine[0], group, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var one = 0; one + 2 < mine.Length && one < theirs.Length - 1; one++)
                {
                    said.TryAdd(mine[one + 2], theirs[one]);
                }

                return said;
            }
        }

        // THE LINE IS THERE AND NAMES A GROUP NOBODY DECLARES, which is a file the game shipped
        // wrong rather than a format nobody has read. Tycho's SkirtLayers.ao is the case: its
        // attachment_bones is a verbatim copy of the bone_group line beneath it, so its last name
        // is a BONE. Said out loud rather than swallowed, because every such line found so far
        // has been worth looking at - and matching by name is what happens instead.
        malformed = true;
        return said;
    }

    /// <summary>Where a piece names the parent's bones its own stand for, and its side of it.</summary>
    private const string Attachment = "attachment_bones";

    /// <inheritdoc cref="Attachment"/>
    private const string GroupBlock = "BoneGroups";

    /// <inheritdoc cref="Attachment"/>
    private const string GroupEntry = "bone_group";

    /// <summary>One entry's value out of one kind of block, or empty where the file has none.</summary>
    private static string Entryed(AnimatedObject ao, string block, string key)
    {
        foreach (AoStruct one in ao.Named(block))
        {
            foreach (AoEntry entry in one.Entries)
            {
                if (string.Equals(entry.Key, key, StringComparison.Ordinal) && entry.Value.Length > 0)
                {
                    return entry.Value;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>The block a rigid prop's file is named in, and the entry inside it.</summary>
    private const string PropBlock = "FixedMesh";

    /// <summary>The entry inside <see cref="PropBlock"/> that names the <c>.fmt</c>.</summary>
    private const string PropEntry = "fixed_mesh";

    /// <summary>Reads through another function and adds up what comes back.</summary>
    private sealed class Tally
    {
        private readonly Func<string, byte[]?> _read;

        public Tally(Func<string, byte[]?> read) => _read = read;

        public long Bytes { get; private set; }

        public int Files { get; private set; }

        public byte[]? Read(string path)
        {
            byte[]? said = _read(path);
            if (said is not null)
            {
                Bytes += said.Length;
                Files++;
            }

            return said;
        }

        /// <summary>A model that was not found, still carrying what was read looking for it.</summary>
        public MonsterModel Failed(string why, string mesh = "")
            => MonsterModel.None with { Mesh_ = mesh, Why = why, Bytes = Bytes, Files = Files };
    }

    /// <summary>
    /// Whether two rigs carry the same bones, name for name, in the same order and bound alike (each bind within <see cref="BindTolerance"/>)
    /// - what lets one's animations pose the other's mesh: the mesh is skinned against the binds of the rig it is played on.
    /// </summary>
    internal static bool SameBones(AnimationSkeleton rig, AnimationSkeleton pack)
    {
        if (!rig.Ready || rig.Bones.Count != pack.Bones.Count)
        {
            return false;
        }

        for (var one = 0; one < rig.Bones.Count; one++)
        {
            if (!string.Equals(rig.Bones[one].Name, pack.Bones[one].Name, StringComparison.Ordinal) || !Alike(rig.Bones[one].Bind, pack.Bones[one].Bind))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>How far two binds' entries may lie apart and still be the same bind - CHOSEN, a hundredth of a unit or of a turn's cosine.</summary>
    private const float BindTolerance = 0.01f;

    private static bool Alike(Matrix4x4 one, Matrix4x4 other)
    {
        Matrix4x4 gap = one - other;
        return MathF.Abs(gap.M11) <= BindTolerance && MathF.Abs(gap.M12) <= BindTolerance && MathF.Abs(gap.M13) <= BindTolerance
            && MathF.Abs(gap.M21) <= BindTolerance && MathF.Abs(gap.M22) <= BindTolerance && MathF.Abs(gap.M23) <= BindTolerance
            && MathF.Abs(gap.M31) <= BindTolerance && MathF.Abs(gap.M32) <= BindTolerance && MathF.Abs(gap.M33) <= BindTolerance
            && MathF.Abs(gap.M41) <= BindTolerance && MathF.Abs(gap.M42) <= BindTolerance && MathF.Abs(gap.M43) <= BindTolerance;
    }

    /// <summary>
    /// The monster's skeleton, and - when it cannot be animated - which of the ways that is.
    /// </summary>
    /// <remarks>
    /// THE LAST CHECK IS THE ONE THAT NEEDS THE GAME. A vertex names its bones by index and the
    /// only bone list in the whole chain is the skeleton's, so the indices must be into it - but
    /// that is an argument from there being nothing else, and this is where it is tested against
    /// every real mesh somebody opens: a highest weighted bone at or past the rig's count means
    /// the argument was wrong, and the model says so instead of skinning garbage.
    /// </remarks>
    private static (AnimationSkeleton Rig, string Why) Rigged(
        Func<string, byte[]?> read, string skeleton, SkinnedMesh mesh)
    {
        if (skeleton.Length == 0)
        {
            return (AnimationSkeleton.None, "no .ao names a skeleton under ClientAnimationController");
        }

        AnimationSkeleton rig = Read(read, skeleton, AnimationSkeleton.Read);
        if (!rig.Ready)
        {
            return (rig, $"the skeleton did not read: {skeleton}"
                + (rig.Why.Length > 0 ? $" - {rig.Why}" : string.Empty));
        }

        if (rig.Animations.Count == 0)
        {
            return (rig, $"the skeleton carries no animations: {skeleton}"
                + (rig.Why.Length > 0 ? $" - {rig.Why}" : string.Empty));
        }

        int highest = SkeletonPose.Highest(mesh);
        if (highest >= rig.Bones.Count)
        {
            return (rig, $"the mesh weights bone {highest} and the skeleton has only {rig.Bones.Count}");
        }

        return (rig, string.Empty);
    }

    /// <summary>
    /// The monster's colour texture, and - when there is none - which of the ways it can be
    /// missing this one is.
    /// </summary>
    /// <remarks>
    /// THE REASON IS THE POINT, not a nicety. A monster drawn without its texture comes out in a
    /// pale warm grey that is all but indistinguishable from bare skin, so "is this monster
    /// untextured or is it just pale" is a question a picture CANNOT answer - it was asked from
    /// the live client and could only be settled by reading code. Everything needed to answer it
    /// passes through here and used to be thrown away.
    ///
    /// THE COORDINATES ARE CHECKED LAST AND SEPARATELY, because a mesh can have a perfectly good
    /// texture and no way to look it up. That is the expected state of a bare body whose clothes
    /// are attached objects, and it is a different answer from "the file would not read".
    /// </remarks>
    /// <param name="named">
    /// Every material the files name, best first: the .ao's own, per shape, then the mesh's
    /// default. Tried in order until one carries a colour map - see the remark in Of.
    /// </param>
    private static (Mipmaps? Skin, string Why, string Material) Painted(
        Func<string, byte[]?> read, SkinnedMesh mesh, IReadOnlyList<string> named, Paints paints)
    {
        var tried = new List<string>();
        var reasons = new List<string>();

        foreach (string one in named)
        {
            string bare = MaterialFile.Bare(one);
            if (bare.Length == 0 || tried.Contains(bare, StringComparer.OrdinalIgnoreCase))
            {
                // The same file with a different :n selector is the same file. Reading it twice
                // would only report the same answer twice in a line somebody has to read.
                continue;
            }

            tried.Add(bare);
            (Mipmaps? skin, string why, _, _) = Colour(read, mesh, bare, paints);
            if (skin is not null)
            {
                return (skin, why, bare);
            }

            reasons.Add(why);
        }

        return tried.Count switch
        {
            0 => (null, "neither the .ao nor the .sm names a material", string.Empty),
            1 => (null, reasons[0], tried[0]),

            // SEVERAL, AND EACH ONE'S REASON, because "none of the three had a colour map" and
            // "the one material there is has none" are different findings and only the first
            // says the shape-by-shape walk was tried and came back empty.
            _ => (null, $"none of the {tried.Count} materials has a colour texture - {string.Join(" | ", reasons.Take(Some))}", tried[0]),
        };
    }

    /// <summary>
    /// Which texture each shape of the mesh wears, and the materials they came out of.
    /// </summary>
    /// <remarks>
    /// THE SHAPE'S NAME IS THE JOIN, and it was already being read - the .ao's SkinMesh block
    /// holds one child per shape, keyed by the shape's own name and valued with its material
    /// (<c>HipsShape = ".../Body.mat:0"</c>), and SkinnedMesh.Shapes carries the same names with
    /// the range of indices each covers. Nothing new has to be parsed to paint a monster part by
    /// part; the two sides simply were never put next to each other.
    ///
    /// THE MANIFEST IS THE SECOND SOURCE and only where the counts agree. A .sm lists materials
    /// in what looks like shape order, which is worth using and not worth trusting blind: a list
    /// of a different length is a list this does not understand, and indexing into it anyway
    /// would paint parts from whatever happened to line up.
    ///
    /// WHAT IS NOT NAMED KEEPS THE OLD ANSWER. A shape with no material of its own gets the
    /// single skin the whole mesh used to wear, so every monster whose .ao names one material
    /// draws exactly as it did and only the ones that name several change.
    ///
    /// READ ONCE PER MATERIAL, through a cache: nine shapes over two sheets is two decodes, not
    /// nine, and a boss's sheet is 2048 square.
    /// </remarks>
    private static Dress Dressed(
        Func<string, byte[]?> read,
        SkinnedMesh mesh,
        IReadOnlyList<(string Shape, string Material)> named,
        MeshManifest manifest,
        Mipmaps? fallback,
        string material,
        Paints paints)
    {
        if (mesh.Shapes.Count == 0)
        {
            return new Dress([], fallback is null ? [] : [material], [], false);
        }

        // ONCE, NOT PER SHAPE. The runs are walked to prove they add up before any of them is
        // used, so asking inside the loop would re-prove the same thing for every shape.
        IReadOnlyList<string> spread = manifest.Spread(mesh.Shapes.Count);
        IReadOnlyList<string> paths = [.. manifest.Materials.Select(one => one.Path)];

        // AND THE NAMES ONCE TOO. Matching each shape against the named list by scanning it is
        // quadratic in the shapes, which a monster never notices and a terrain tile does: its
        // list holds one entry per prop shape over up to 256 sub-tiles. First spelling wins, as
        // the scan's first match did.
        var byName = new Dictionary<string, string>(named.Count, StringComparer.OrdinalIgnoreCase);
        foreach ((string called, string wears) in named)
        {
            byName.TryAdd(called, wears);
        }

        var textures = new List<string>();
        var guessed = false;

        var skins = new Mipmaps?[mesh.Shapes.Count];
        var modes = new string[mesh.Shapes.Count];
        var wearing = new string[mesh.Shapes.Count];
        var painted = new string[mesh.Shapes.Count];
        var used = new List<string>();
        using ModelProgress.Step step = ModelProgress.Begin(paints.Progress, "painting the shapes", mesh.Shapes.Count);
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            step.Advance();
            // AS WRITTEN, colon and all. The number picks a graph INSIDE the material, one
            // per shape - see MaterialFile.Graphs - so two shapes naming the same file are
            // two different textures and the cache has to tell them apart by the whole
            // string rather than by the file.
            string wants = Wanted(mesh.Shapes[shape].Name, shape, mesh.Shapes.Count, named, byName, spread, paths);
            wearing[shape] = wants;
            painted[shape] = string.Empty;
            if (wants.Length == 0)
            {
                skins[shape] = fallback;
                modes[shape] = string.Empty;
                continue;
            }

            modes[shape] = paints.Modes(read, wants).Last;

            (Mipmaps? worn, _, string texture, bool said) = Colour(read, mesh, wants, paints);
            painted[shape] = worn is not null ? texture : string.Empty;
            if (worn is not null)
            {
                guessed |= !said;
                if (!textures.Contains(texture, StringComparer.OrdinalIgnoreCase))
                {
                    textures.Add(texture);
                }
            }

            skins[shape] = worn ?? fallback;
            string file = MaterialFile.Bare(wants);
            if (worn is not null && !used.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                used.Add(file);
            }
        }

        if (fallback is not null && used.Count == 0 && material.Length > 0)
        {
            used.Add(material);
        }

        return new Dress(skins, used, textures, guessed)
        {
            Runs = spread.Count > 0,
            Modes = modes,
            ShapeMaterials = wearing,
            ShapeTextures = painted,
        };
    }

    /// <summary>
    /// A loaded model with a tileset's material swaps put on its shapes - each swapped shape wearing the material the area draws, its
    /// blend and colour map that material's - or the model itself where none of its materials is swapped.
    /// </summary>
    /// <remarks>
    /// FOR EVERY DOODAD A LAID OR BUILT ROOM LAYS (RoomModels.Lay): the master's TileMaterialOverrides swap what the tiles it places
    /// wear, and Crimson Shores' names the material of a tile doodad - <c>CorruptionGradientc.mat</c>, the tendrils of its stromatolite
    /// tiles, to the mask nothing draws - so the swap reaches the tile's own doodads (its <c>.tmd</c>) as well as its mesh (DECIDED from
    /// the live client, 2026-10-29). The ground's scatter wears it too (v0.1.368, chosen: its layers and its list are the master's own).
    /// AND FROM 2026-10-30 THE ROOM'S OWN DOODAD LINES, and other rooms' laid round the player (DECIDED from the live client, reversing
    /// "the room's own doodads as they are"): one master swaps for the whole of what it lays. As a tile's mesh takes it (TileModels'
    /// Runs): the swap is the whole material, any selector after it dropped. The room compiles its shapes from what they wear once
    /// they are joined (Shaded), so the swapped material is the one compiled.
    /// </remarks>
    internal static MonsterModel Swapped(Func<string, byte[]?> read, MonsterModel model, IReadOnlyDictionary<string, string> swaps, Paints paints)
    {
        if (!model.Ready || swaps.Count == 0)
        {
            return model;
        }

        int count = model.Mesh.Shapes.Count;
        string[]? wearing = null;
        for (var shape = 0; shape < count && shape < model.ShapeMaterials.Count; shape++)
        {
            string material = model.ShapeMaterials[shape];
            if (material.Length > 0 && swaps.TryGetValue(MaterialFile.Bare(material).Replace('\\', '/').Trim(), out string? swap))
            {
                wearing ??= [.. model.ShapeMaterials];
                wearing[shape] = swap;
            }
        }

        if (wearing is null)
        {
            return model;
        }

        var skins = new Mipmaps?[count];
        var modes = new string[count];
        var textures = new string[count];
        for (var shape = 0; shape < count; shape++)
        {
            skins[shape] = shape < model.Skins.Count ? model.Skins[shape] : model.Skin;
            modes[shape] = shape < model.Modes.Count ? model.Modes[shape] : string.Empty;
            textures[shape] = shape < model.ShapeTextures.Count ? model.ShapeTextures[shape] : string.Empty;
            if (shape >= wearing.Length || ReferenceEquals(wearing[shape], model.ShapeMaterials[shape]))
            {
                continue;
            }

            modes[shape] = paints.Modes(read, wearing[shape]).Last;
            (Mipmaps? worn, _, string texture, _) = Colour(read, model.Mesh, wearing[shape], paints);
            skins[shape] = worn;
            textures[shape] = worn is not null ? texture : string.Empty;
        }

        return model with
        {
            ShapeMaterials = wearing,
            AuthoredMaterials = model.AuthoredMaterials.Count > 0 ? model.AuthoredMaterials : model.ShapeMaterials,
            Skins = skins,
            Modes = modes,
            ShapeTextures = textures,
        };
    }

    /// <summary>What the shapes ended up wearing, and how sure the walk is about it.</summary>
    /// <param name="Skins">One texture per shape, in the mesh's order.</param>
    /// <param name="Materials">The distinct materials they came out of.</param>
    /// <param name="Textures">The distinct colour textures, by path.</param>
    /// <param name="Guessed">Whether any was chosen with no slot name saying it is the colour map.</param>
    private readonly record struct Dress(
        IReadOnlyList<Mipmaps?> Skins,
        IReadOnlyList<string> Materials,
        IReadOnlyList<string> Textures,
        bool Guessed)
    {
        /// <summary>Whether the manifest's numbers added up and were used as runs of shapes.</summary>
        public bool Runs { get; init; }

        /// <summary>Each shape's material blend mode, raw - see MonsterModel.Modes.</summary>
        public IReadOnlyList<string> Modes { get; init; } = [];

        /// <summary>Each shape's material as written - see MonsterModel.ShapeMaterials.</summary>
        public IReadOnlyList<string> ShapeMaterials { get; init; } = [];

        /// <summary>Each shape's colour texture - see MonsterModel.ShapeTextures.</summary>
        public IReadOnlyList<string> ShapeTextures { get; init; } = [];
    }

    /// <summary>
    /// The material a shape asks for: its own by name, else the manifest's by position.
    /// </summary>
    /// <remarks>
    /// BY NAME FIRST, because that is the join the files really make - the .ao's child is keyed
    /// by the shape's own name - and it is the only one that survives a mesh whose shapes are
    /// listed in another order.
    ///
    /// THEN BY POSITION, AND ONLY WHERE THERE IS EXACTLY ONE ENTRY PER SHAPE. Reported from the
    /// live client: Count Geonor's human form is fifteen shapes that came back painted from one
    /// texture and visibly in pieces, while his wolf form - whose materials this matched by
    /// name - was right. A list as long as the shape list is a list in shape order; a list of
    /// any other length is one whose order is not established here, and indexing into it anyway
    /// would paint parts from whatever happened to line up.
    ///
    /// THEN THE MANIFEST'S RUNS, which is the case the three above cannot reach: a monster whose
    /// .ao names nothing and whose manifest names FEWER materials than the mesh has shapes. That
    /// is not a rare shape - measured from the live client, it is Veynar (35 shapes, 3 materials),
    /// Connal (11 and 2) and Count Geonor's human form (15 and 7), all three of which came back
    /// painted from a single sheet and visibly wrong. The number on each material line is what
    /// spreads the short list over the long one, and <see cref="MeshManifest.Spread"/> only hands
    /// one back when the numbers add up to exactly the shape count.
    ///
    /// The manifest's bare list is the last of the four, on the same terms as the second.
    /// </remarks>
    private static string Wanted(
        string shape,
        int at,
        int shapes,
        IReadOnlyList<(string Shape, string Material)> named,
        IReadOnlyDictionary<string, string> byName,
        IReadOnlyList<string> spread,
        IReadOnlyList<string> manifest)
    {
        if (byName.TryGetValue(shape, out string? material))
        {
            return material;
        }

        if (named.Count == shapes && at < named.Count)
        {
            return named[at].Material;
        }

        if (spread.Count == shapes && at < spread.Count)
        {
            return spread[at];
        }

        return manifest.Count == shapes && at < manifest.Count ? manifest[at] : string.Empty;
    }

    /// <summary>One material: its colour texture, or which way this one is missing it.</summary>
    private static (Mipmaps? Skin, string Why, string Texture, bool Named) Colour(
        Func<string, byte[]?> read, SkinnedMesh mesh, string material, Paints paints)
    {
        if (material.Length == 0)
        {
            return (null, "neither the .ao nor the .sm names a material", string.Empty, true);
        }

        MaterialFile paint = paints.Paint(read, MaterialFile.Bare(material));
        if (!paint.Ready)
        {
            return (null, $"the material did not read: {MaterialFile.Bare(material)}", string.Empty, true);
        }

        // THE NUMBER AFTER THE FILE PICKS THE GRAPH, which this file's own remark said all
        // along and nothing acted on: a monster built of parts names one material per shape
        // as Boss.mat:0, Boss.mat:1, and the colour map is per graph. Taking the first one
        // for every shape paints the head's sheet onto the cloak - patches of the wrong
        // colour, which is how Bahlak, Connal and Count Geonor were reported.
        (string texture, bool named) = paint.AlbedoOf(MaterialFile.SelectorOf(material));
        if (texture.Length == 0)
        {
            // WHAT IT DOES NAME, because this is the one reason here that is a QUESTION rather
            // than an answer. Every other line says what went wrong and where; this one said
            // only that the rule found nothing, and the rule - a slot whose name carries
            // Albedo, Colour or Color, or the first texture that is not a normal map - was
            // measured on the materials that happened to be looked at. A boss drawn in plain
            // ink is then indistinguishable from a boss whose slot is called something this
            // has never seen, and the only way to tell was to read the file with other tools.
            // So it prints the material and what is in it, which is exactly what deciding
            // between those two needs.
            return (
                null,
                $"the material names no colour texture - {Listed(MaterialFile.Bare(material), paint)}",
                string.Empty,
                true);
        }

        // DECODED ONCE PER TEXTURE, not once per shape that points at it: two shapes of one material name the same sheet through
        // different graphs, and a boss's sheet is sixteen megabytes of pixels before its levels are built. See Paints.Texture.
        (Mipmaps? skin, bool readAtAll) = paints.Texture(read, texture);
        return skin is not null
            ? Fitted(skin, mesh, texture, named)
            : (null, readAtAll ? $"the texture did not decode: {texture}" : $"the texture did not read: {texture}", texture, named);
    }

    /// <summary>A decoded texture with the one thing that can still be wrong about it.</summary>
    /// <remarks>
    /// THE COORDINATES ARE CHECKED SEPARATELY AND LAST, because a mesh can have a perfectly
    /// good texture and no way to look it up - the expected state of a bare body whose clothes
    /// are attached objects, and a different answer from "the file would not read".
    /// </remarks>
    private static (Mipmaps? Skin, string Why, string Texture, bool Named) Fitted(
        Mipmaps skin, SkinnedMesh mesh, string texture, bool named)
        => mesh.Coordinated
            ? (skin, string.Empty, texture, named)
            : (skin,
                "the mesh carries no texture coordinates, so the texture cannot be applied",
                texture,
                named);

    /// <summary>What has already been read, so nothing is read or decoded twice.</summary>
    /// <remarks>
    /// SHARED BY EVERY THREAD OF ONE BUILD: a room's tiles, doodads and materials are made on all cores at once (LaidRoomModels,
    /// RoomModels.Lay, Shaded), and every cache here is made once per key whoever asks first (<see cref="OnceCache{T}"/>) - a texture
    /// two doodads wear is one decode and one object, as it was on one thread. What depends on the order things were first read
    /// (<see cref="Shaders"/>) keeps that order where one thread reads, and the order they landed in where several do.
    /// </remarks>
    internal sealed class Paints
    {
        /// <summary>Where the build this cache serves says how far it has got, or null - see <see cref="ModelProgress"/>.</summary>
        /// <remarks>ON THE CACHE because the cache is the one thing every step of a build is handed already.</remarks>
        public ModelProgress? Progress { get; init; }

        /// <summary>Material file by its path, with the selector taken off.</summary>
        private readonly OnceCache<MaterialFile> _files = new();

        /// <summary>
        /// Every parent graph the read materials name, distinct, in the order first named - kept as they are read, see <see cref="Shaders"/>.
        /// </summary>
        /// <remarks>
        /// KEPT AS THEY COME rather than gathered from every material on each ask: every model built asks, and a cache shared by every
        /// room of the stage sweep holds tens of thousands of materials, which gathered on each ask would cost the sweep more than the
        /// builds. The list handed out is made again only when a new graph has come.
        /// </remarks>
        private readonly List<string> _parents = [];

        /// <summary>The graphs in <see cref="_parents"/>, for the test whether one is new.</summary>
        private readonly HashSet<string> _parentSet = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The list last handed out, or null once a new graph has come.</summary>
        private IReadOnlyList<string>? _shaders;

        /// <summary>Blend modes by material file, the first graph's and the last's, worked out once. See <see cref="Modes"/>.</summary>
        private readonly OnceCache<(string First, string Last)> _modes = new();

        /// <summary>A shader graph's own blend mode by its path, read once however many materials name it.</summary>
        private readonly OnceCache<string> _graphs = new();

        /// <summary>Decoded texture by its path, with whether its file read at all.</summary>
        private readonly OnceCache<(Mipmaps? Levels, bool Read)> _sheets = new();

        /// <summary>A shader graph by its path, read once however many materials are instances of it.</summary>
        private readonly OnceCache<ShaderGraph> _graphFiles = new();

        /// <summary>What each material's graphs came to, by material file.</summary>
        private readonly OnceCache<Shade> _shades = new();

        /// <summary>What compiling each material's graphs came to, by material file - before any texture is read.</summary>
        private readonly OnceCache<(ShadeCompile Compiled, IReadOnlyList<string> Inputs)> _compiled = new();

        /// <summary>What mixing each pair of grounds came to: the name its program is kept under, or null and why not - asked once each.</summary>
        private readonly OnceCache<(string? Name, string Why)> _mixed = new();

        /// <summary>A texture's bytes as the install holds them, its own wrapping taken off - timed, see ModelProgress.Time.</summary>
        public byte[]? Raw(Func<string, byte[]?> read, string texture)
        {
            using ModelProgress.Timed timed = ModelProgress.Time(Progress, BuildWork.Unpacking);
            return GameArt.ReadRaw(read, texture);
        }

        /// <summary>
        /// A texture with its levels: the file's own, kept as its blocks, where it carries every one (Mipmaps.Kept(BlockTexture)) - else
        /// decoded and halved, timed (ModelProgress.Time); counted by its format either way (ModelProgress.Kept, Decoded).
        /// </summary>
        /// <remarks>
        /// NOTHING DECODED HERE FOR A BLOCK FILE (v0.1.326): decoding was most of a stage's build (Port, v0.1.319: 25 of the build's
        /// thread-seconds, mipmaps 8 more), and the card reads the blocks as they are. The processor decodes them behind the load.
        /// </remarks>
        public Mipmaps? Levels(byte[] bytes)
        {
            if (Mipmaps.Kept(BlockTexture.Of(bytes)) is { } kept)
            {
                Progress?.Kept(ModelDump.Format(bytes), (long)kept.Width * kept.Height, kept.Count);
                return kept;
            }

            long started = Stopwatch.GetTimestamp();
            GamePicture? top;
            using (ModelProgress.Time(Progress, BuildWork.Decoding))
            {
                top = GameArt.Decode(bytes);
            }

            long decoded = Stopwatch.GetTimestamp();
            Mipmaps? levels;
            using (ModelProgress.Time(Progress, BuildWork.Mipmapping))
            {
                levels = Mipmaps.Of(top);
            }

            Progress?.Decoded(ModelDump.Format(bytes), top is { Ready: true } picture ? (long)picture.Width * picture.Height : 0, decoded - started, Stopwatch.GetTimestamp() - decoded);
            return levels;
        }

        /// <summary>
        /// A texture decoded with its levels, once however many shapes and materials name it - or null levels, and whether its file
        /// read at all, where it did not decode.
        /// </summary>
        /// <remarks>
        /// THROUGH ReadRaw AND NOT A BARE READ. A texture in this game is one of three things and only the third is a .dds as it
        /// stands: it may be a SIGNPOST - a star and the path of the file that really holds it - or it may sit behind a compressed
        /// header. Decoding a plain read handles the third and silently fails the other two.
        ///
        /// THE LEVELS ARE BUILT HERE, ON THE LOAD'S TASK, and not where the picture is drawn: a boss's skin is 2048 square, and
        /// halving it down is a pass over sixteen megabytes that belongs beside the bundle reads it follows rather than on the
        /// frame that first draws it.
        /// </remarks>
        public (Mipmaps? Levels, bool Read) Texture(Func<string, byte[]?> read, string texture)
            => _sheets.Get(texture, path => Raw(read, path) is { Length: > 0 } bytes ? (Lean ? Readable(bytes) : Levels(bytes), true) : (null, false));

        /// <summary>
        /// Whether a texture is only asked whether it reads, never drawn - one pixel stands for every texture that does (the stage sweep's).
        /// </summary>
        /// <remarks>
        /// A SWEEP OVER EVERY ROOM HOLDS NO PICTURE: it asks what a room's shapes are drawn with, and a texture that does not read leaves
        /// its material undrawn, so each is still read and its blocks' header or its decode checked - then dropped, where every room of
        /// every set would otherwise keep its sheets in the one cache the sweep shares.
        /// </remarks>
        public bool Lean { get; init; }

        /// <summary>One pixel, standing for a texture that reads - see <see cref="Lean"/>.</summary>
        private static readonly Mipmaps? Stand = Mipmaps.Of(new GamePicture(1, 1, [128, 128, 128, 255]));

        /// <summary>The stand-in where the texture's blocks or its decode read, else null.</summary>
        private Mipmaps? Readable(byte[] bytes)
        {
            if (BlockTexture.Of(bytes) is { Complete: true })
            {
                return Stand;
            }

            using ModelProgress.Timed timed = ModelProgress.Time(Progress, BuildWork.Decoding);
            return GameArt.Decode(bytes) is { Ready: true } ? Stand : null;
        }

        /// <summary>A material file parsed - timed, see ModelProgress.Time.</summary>
        private MaterialFile Material(Func<string, byte[]?> read, string file)
        {
            byte[]? bytes = read(file.Replace('\\', '/').Trim());
            MaterialFile paint;
            using (ModelProgress.Time(Progress, BuildWork.GraphReading))
            {
                paint = MaterialFile.Read(bytes);
            }

            lock (_parents)
            {
                foreach (string parent in paint.Parents)
                {
                    if (parent.Length > 0 && _parentSet.Add(parent))
                    {
                        _parents.Add(parent);
                        _shaders = null;
                    }
                }
            }

            return paint;
        }

        /// <summary>A shader graph parsed, once - timed, see ModelProgress.Time.</summary>
        private ShaderGraph Graph(Func<string, byte[]?> read, string path)
            => _graphFiles.Get(path, one =>
            {
                byte[]? bytes = read(one);
                using ModelProgress.Timed timed = ModelProgress.Time(Progress, BuildWork.GraphReading);
                return ShaderGraph.Read(bytes);
            });

        /// <summary>One material file by its path, the selector taken off - read once.</summary>
        public MaterialFile Paint(Func<string, byte[]?> read, string file) => _files.Get(file, one => Material(read, one));

        /// <summary>
        /// A material handed over already read, in place of reading it - the graph survey reads every material in the install itself,
        /// and hands each in before asking its <see cref="Modes"/>. See <see cref="Forget"/>.
        /// </summary>
        public void Hand(string file, MaterialFile paint) => _files.Set(file, paint);

        /// <summary>Lets go of a material handed over and what was worked out of it - a survey's memory stays one material's.</summary>
        public void Forget(string file)
        {
            _files.Forget(file);
            _modes.Forget(file);
        }

        /// <summary>Whether the engine draws a material in its own water pass - the ocean, a river or standing water; see ShadeProgram.KindOf.</summary>
        public static bool IsWater(MaterialFile paint) => ShadeProgram.KindOf(paint.ShaderType) != WaterKind.None;

        /// <summary>The blend word of the engine's ocean - the one the room's shore, the lift and the field waves are made from.</summary>
        public const string OceanMode = "water";

        /// <summary>The blend word of the engine's river - mixed as the ocean is (MaterialBlends), but no shore of the sea's.</summary>
        public const string RiverMode = "river water";

        /// <summary>The blend word of standing water - mixed as the ocean is, but no shore of the sea's.</summary>
        public const string StillMode = "still water";

        /// <summary>The blend word a water's kind is drawn under, or empty for none.</summary>
        public static string ModeOf(WaterKind kind) => kind switch
        {
            WaterKind.Ocean => OceanMode,
            WaterKind.River => RiverMode,
            WaterKind.Still => StillMode,
            _ => string.Empty,
        };

        /// <summary>Whether a blend word is one of the engine's water - see <see cref="ModeOf"/>.</summary>
        public static bool IsWaterMode(string? mode) => mode is OceanMode or RiverMode or StillMode;

        /// <summary>
        /// The blend mode a material asks for, as it spells it, or empty: where the first graph to name one wins, and where the last does.
        /// </summary>
        /// <remarks>
        /// THE MATERIAL'S OWN WORD FIRST - its defaultgraph's overriden_blend_mode - in both readings,
        /// and the graphs it is an instance of after it: zao's reader puts the member on both, and the
        /// nearer file is the one that overrides, as everywhere else in this walk.
        ///
        /// THE PICTURE DRAWS THE LAST. Which of two graphs naming a mode stands is written down
        /// nowhere, and for a while both were drawable behind a switch. What decided it is the files:
        /// every ground layer of seepage's offices names AlphaBlend in its contact-fade graph and then
        /// ForceNoZWriteAlphaBlend AFTER it, and the deserted room's walk blocker names Opaque and then
        /// ForceAlphaBlendNoGI. Under "the first stands" every one of those Force graphs would be dead
        /// weight. That is a reading of how the files are written, not the engine's code, so the first
        /// is still worked out: the graph survey counts where the two differ (3,459 of 112,255
        /// materials the first time it ran, nearly all effects and microtransactions, terrain almost
        /// never - which is why switching never changed a tile). Every graph is read for the second,
        /// once each through the cache.
        /// </remarks>
        public (string First, string Last) Modes(Func<string, byte[]?> read, string material)
            => _modes.Get(MaterialFile.Bare(material), file => ModesOf(read, file));

        private (string First, string Last) ModesOf(Func<string, byte[]?> read, string file)
        {
            // THE BEACH'S SHEET, said as the word MaterialBlends takes for it - see RoomModels.Piled.
            if (string.Equals(file, ShadeProgram.BeachGraph, StringComparison.OrdinalIgnoreCase))
            {
                return (RoomModels.BeachMode, RoomModels.BeachMode);
            }

            MaterialFile paint = Paint(read, file);
            if (ModeOf(ShadeProgram.KindOf(paint.ShaderType)) is { Length: > 0 } water)
            {
                // THE ENGINE'S WATER PASS, said as the word MaterialBlends takes for it - see ShadeProgram.Water: the ocean's alone makes the
                // room's shore, its lift and its field waves (RoomModels.ShoreOf, Watered), a river's and standing water's mixed the same.
                return (water, water);
            }

            string first = paint.Blend;
            string last = paint.Blend;
            if (paint.Blend.Length == 0)
            {
                foreach (string parent in paint.Parents)
                {
                    if (parent.Length == 0)
                    {
                        continue;
                    }

                    string graph = _graphs.Get(parent, one => MaterialFile.GraphBlend(read(one.Replace('\\', '/').Trim())));
                    if (graph.Length > 0)
                    {
                        first = first.Length > 0 ? first : graph;
                        last = graph;
                    }
                }
            }

            return (first, last);
        }

        /// <summary>Every parent graph the read materials name, distinct. See MonsterModel.Shaders.</summary>
        public IReadOnlyList<string> Shaders
        {
            get
            {
                lock (_parents)
                {
                    return _shaders ??= [.. _parents];
                }
            }
        }

        /// <summary>
        /// One material's graphs compiled, with the Input nodes they name - the textures not yet read.
        /// </summary>
        /// <remarks>
        /// THE INPUTS ARE FOR THE DUMP, which asks of a ground material where its coordinates come
        /// from: a ground mesh carries none, so whether its graphs read <c>InputUV</c> or
        /// <c>InputWorldPos</c> is the question. Named in the order the graphs list them.
        /// </remarks>
        public (ShadeCompile Compiled, IReadOnlyList<string> Inputs) Compiled(Func<string, byte[]?> read, string material)
            => _compiled.Get(MaterialFile.Bare(material), file => CompiledOf(read, file));

        private (ShadeCompile Compiled, IReadOnlyList<string> Inputs) CompiledOf(Func<string, byte[]?> read, string file)
        {
            // THE BEACH'S SHEET WEARS THE ENGINE'S GRAPH ITSELF, no material: its own program over the textures the graph binds.
            if (string.Equals(file, ShadeProgram.BeachGraph, StringComparison.OrdinalIgnoreCase))
            {
                return (ShadeProgram.BeachOf(Graph(read, file)), []);
            }

            // AN OCEAN, A RIVER OR STANDING WATER IS THE ENGINE'S WATER PASS, not its graphs: they write no colour the picture can draw
            // (nvidian_Ocean's reads an albedo nothing wrote, nvidian_NonsplashyFlowingWater.mat names none), and the area's water is
            // what it is drawn with, through the surface of its kind.
            if (Paint(read, file) is { } water && ShadeProgram.KindOf(water.ShaderType) is var kind and not WaterKind.None)
            {
                string drawn = kind switch
                {
                    WaterKind.Ocean => "drawn as the area's water",
                    WaterKind.River => "drawn as the area's water on the engine's river surface - over the area's flow map read live where the room is laid as the area laid it, at no flow elsewhere",
                    _ => "drawn as the area's water, standing - the ocean's ripples over no shore",
                };
                return (new ShadeCompile(ShadeProgram.Water(kind), [$"the material's graphs, its shadertype being {water.ShaderType}: {drawn}"]), []);
            }

            var chain = new List<(ShaderInstance, ShaderGraph)>();
            var inputs = new List<string>();
            byte[]? instanced = read(file.Replace('\\', '/').Trim());
            IReadOnlyList<ShaderInstance> instances;
            using (ModelProgress.Time(Progress, BuildWork.GraphReading))
            {
                instances = ShaderGraph.Instances(instanced);
            }

            foreach (ShaderInstance instance in instances)
            {
                if (instance.Parent.Length == 0)
                {
                    continue;
                }

                ShaderGraph graph = Graph(read, instance.Parent.Replace('\\', '/').Trim());

                // A GRAPH WITH NO NODES - ForceAlpha is a blend mode and nothing else - has nothing
                // to compile, and is not missing.
                chain.Add((instance, graph));
                foreach (ShaderNode node in graph.Nodes)
                {
                    if (node.Type.StartsWith("Input", StringComparison.Ordinal) && !inputs.Contains(node.Type, StringComparer.Ordinal))
                    {
                        inputs.Add(node.Type);
                    }
                }
            }

            using ModelProgress.Timed timed = ModelProgress.Time(Progress, BuildWork.Compiling);
            return (ShadeProgram.Compile(chain), inputs);
        }

        /// <summary>
        /// The name a tile's ground of two grounds wears when they are mixed per pixel by its mask's green (ShadeProgram.Grounds) -
        /// its program registered under it for <see cref="Shade"/>, and opaque for <see cref="Modes"/> - or null and why not.
        /// </summary>
        /// <param name="read">How to get a file out of the install, by path.</param>
        /// <param name="first">The ground at green nought.</param>
        /// <param name="second">The ground at green one.</param>
        /// <param name="mask">The template's ground mask.</param>
        /// <param name="scale">The ground materials' tiling - see ShadeProgram.Grounds.</param>
        /// <param name="heights">Whether the two are weighed by their heights where their graphs turn that on - see ShadeProgram.Grounds.</param>
        /// <param name="why">Why there is none, where there is none.</param>
        public string? Grounds(Func<string, byte[]?> read, string first, string second, string mask, float scale, bool heights, out string why)
        {
            (string? name, why) = _mixed.Get(GroundBlend.Name(first, second, mask, heights), name => Mixed(read, name, first, second, mask, scale, heights));
            return name;
        }

        /// <summary>
        /// The name a tile's ground of two grounds wears when they are mixed by the second ground's share of the sub-tile's corners
        /// (ShadeProgram.GroundsByCorners) - registered as <see cref="Grounds"/> registers its own - or null and why not.
        /// </summary>
        /// <param name="read">How to get a file out of the install, by path.</param>
        /// <param name="first">The ground at share nought.</param>
        /// <param name="second">The ground at share one.</param>
        /// <param name="scale">The ground materials' tiling - see ShadeProgram.Grounds.</param>
        /// <param name="heights">Whether the two are weighed by their heights where their graphs turn that on - see ShadeProgram.Grounds.</param>
        /// <param name="why">Why there is none, where there is none.</param>
        public string? GroundsByCorners(Func<string, byte[]?> read, string first, string second, float scale, bool heights, out string why)
        {
            (string? name, why) = _mixed.Get(GroundBlend.Cornered(first, second, heights), name => Mixed(read, name, first, second, string.Empty, scale, heights));
            return name;
        }

        /// <summary>
        /// The program of two grounds made and registered: mixed by the mask's green, or by the corners' share where <paramref name="mask"/>
        /// is empty - each ground's twin carrying its height where weighed by the heights (ShadeProgram.Heighted).
        /// </summary>
        private (string? Name, string Why) Mixed(Func<string, byte[]?> read, string name, string first, string second, string mask, float scale, bool heights)
        {
            (ShadeProgram? flatFirst, ShadeProgram? shineFirst, string whyFirst) = Bound(read, first, heights);
            (ShadeProgram? flatSecond, ShadeProgram? shineSecond, string whySecond) = Bound(read, second, heights);
            bool cornered = mask.Length == 0;
            Mipmaps? sheet = cornered ? null : Sheet(read, mask);
            ShadeTexture? texture = cornered ? null : new ShadeTexture(mask, false);
            ShadeProgram? Made(ShadeProgram one, ShadeProgram other)
                => texture is { } mask
                    ? ShadeProgram.Grounds(one, other, mask, sheet, scale, heights)
                    : ShadeProgram.GroundsByCorners(one, other, scale, heights);

            ShadeProgram? flat = flatFirst is null || flatSecond is null || (!cornered && sheet is null)
                ? null
                : Made(flatFirst, flatSecond);
            if (flat is null)
            {
                return (null, flatFirst is null ? whyFirst
                    : flatSecond is null ? whySecond
                    : !cornered && sheet is null ? "its mask did not read: " + mask
                    : "the two grounds' programs cannot be run as one (more registers than a program takes, or one marches a parallax by the triangle's tangent frame)");
            }

            ShadeProgram? shine = shineFirst is not null || shineSecond is not null
                ? Made(shineFirst ?? flatFirst!, shineSecond ?? flatSecond!)
                : null;
            _shades.Set(name, new Shade(flat, null, [], shine));
            _modes.Set(name, (string.Empty, string.Empty));
            return (name, string.Empty);
        }

        /// <summary>
        /// A ground material's program with its textures in hand, the twin that keeps its height where its graphs write one
        /// (ShadeProgram.Heighted) - what the capture reads the engine's height blending with (CaptureGroundCompare) - or null where it
        /// does not compile or a texture does not read.
        /// </summary>
        internal ShadeProgram? Heighted(Func<string, byte[]?> read, string material) => Bound(read, material, heighted: true).Flat;

        /// <summary>A ground material's own program with its textures in hand - what the capture reads its colour with (CaptureGroundChoices) - or null.</summary>
        internal ShadeProgram? Plain(Func<string, byte[]?> read, string material) => Bound(read, material, heighted: false).Flat;

        /// <summary>
        /// One material's program and its glossy twin with their textures in hand - even where its colour is one plain read - or null and
        /// why; the twins that carry its height where <paramref name="heighted"/> and its graphs write one (ShadeProgram.Heighted).
        /// </summary>
        private (ShadeProgram? Flat, ShadeProgram? Glossy, string Why) Bound(Func<string, byte[]?> read, string material, bool heighted)
        {
            if (Compiled(read, material).Compiled.Program is not { } compiled)
            {
                return (null, null, "a ground whose graphs did not compile: " + material);
            }

            ShadeProgram program = heighted && compiled.Heighted is { } raised ? raised : compiled;

            var sheets = new Mipmaps?[program.Textures.Count];
            for (var one = 0; one < sheets.Length; one++)
            {
                sheets[one] = Sheet(read, program.Textures[one].Path);
                if (sheets[one] is null)
                {
                    return (null, null, "a ground's texture that did not read: " + program.Textures[one].Path);
                }
            }

            ShadeProgram? shine = null;
            if (program.Glossy is { } glossy)
            {
                var more = new Mipmaps?[glossy.Textures.Count];
                for (var one = 0; one < more.Length; one++)
                {
                    more[one] = Sheet(read, glossy.Textures[one].Path);
                }

                shine = glossy.With(more) is { Bound: true } lit ? lit : null;
            }

            return (program.With(sheets), shine, string.Empty);
        }

        /// <summary>
        /// What one material's graphs compute for its colour - see MonsterModels.Shaded.
        /// </summary>
        public Shade Shade(Func<string, byte[]?> read, string material)
            => _shades.Get(MaterialFile.Bare(material), file => ShadeOf(read, file));

        private Shade ShadeOf(Func<string, byte[]?> read, string file)
        {
            ShadeCompile compiled = Compiled(read, file).Compiled;
            var skipped = new List<string>(compiled.Skipped);
            if (compiled.Program is not { } program)
            {
                return new Shade(null, null, skipped);
            }

            var sheets = new Mipmaps?[program.Textures.Count];
            for (var one = 0; one < sheets.Length; one++)
            {
                sheets[one] = Sheet(read, program.Textures[one].Path);
                if (sheets[one] is null)
                {
                    skipped.Add($"a texture that did not read: {program.Textures[one].Path}");
                }
            }

            if (Array.Exists(sheets, one => one is null))
            {
                return new Shade(null, null, skipped);
            }

            // THE GLOSSY PROGRAM READS ONE MORE TEXTURE - the gloss's - which is fetched here
            // and handed over beside the rest; one that does not read leaves it out.
            ShadeProgram bound = program.With(sheets);
            ShadeProgram? shine = null;
            if (program.Glossy is { } glossy)
            {
                var more = new Mipmaps?[glossy.Textures.Count];
                for (var one = 0; one < more.Length; one++)
                {
                    more[one] = Sheet(read, glossy.Textures[one].Path);
                }

                shine = glossy.With(more) is { Bound: true } lit ? lit : null;
            }

            return program.Plain >= 0
                ? new Shade(null, sheets[program.Plain], skipped, shine)
                : new Shade(bound, null, skipped, shine);
        }

        /// <summary>A texture decoded with its levels, through the same cache as the skins.</summary>
        private Mipmaps? Sheet(Func<string, byte[]?> read, string texture) => Texture(read, texture).Levels;
    }

    /// <summary>
    /// A material named, with the slots and textures it holds - the evidence for "no colour".
    /// </summary>
    /// <remarks>
    /// SHORT ON PURPOSE. This goes in a line under the picture, so it carries the material's own
    /// file name rather than its path, the slot names as written, and the textures by file name
    /// only - enough to recognise a colour map filed under a slot nobody taught this about, and
    /// not so much that the line stops being readable. Three of each, because a material with
    /// more than three of either has already made the point.
    /// </remarks>
    private static string Listed(string material, MaterialFile paint)
    {
        string Few(IEnumerable<string> names)
        {
            string[] some = [.. names.Take(Some)];
            return some.Length == 0 ? "none" : string.Join(", ", some);
        }

        static string Named(string path) => path[(path.LastIndexOf('/') + 1)..];

        string file = material.Length > 0 ? Named(material) : "?";
        string slots = Few(paint.Slots.Select(one => $"{one.Key}={Named(one.Value)}"));
        string textures = Few(paint.Textures.Select(one => Named(one.Path)));

        return $"{file} names slots: {slots}; textures: {textures}";
    }

    /// <summary>How many slots and textures the line above names before it stops.</summary>
    private const int Some = 3;

    /// <summary>
    /// The first SkinMesh found, walking from the monster's own files outwards through extends.
    /// </summary>
    /// <remarks>
    /// BREADTH FIRST, so the monster's own files are all looked at before anything they extend.
    /// A depth-first walk would reach a base file before the monster's second .ao, and the nearer
    /// file is the one whose answer counts.
    /// </remarks>
    private static (IReadOnlyList<string> Meshes, IReadOnlyList<(string Shape, string Material)> Materials,
        string Skeleton, IReadOnlyList<string> Files)? Skinned(
        Func<string, byte[]?> read, IReadOnlyList<string> named)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        var materials = new List<(string Shape, string Material)>();

        // EVERY .ao THE WALK ACTUALLY READ, kept so the dressing can look for attachments in the
        // same files rather than walking them a second time. The walk stops once it has the skin
        // and the rig, so this is the monster's own files and whatever they extend up to there -
        // which is where attached_object entries live.
        var walked = new List<string>();

        foreach (string one in named)
        {
            queue.Enqueue((one, 0));
        }

        // THE SKIN AND THE SKELETON ARE FOUND SEPARATELY, each by the nearest file that names it,
        // and the walk carries on past the first until it has both or runs out of files. They are
        // usually in the same .ao; a monster whose base supplies the rig and whose own file only
        // swaps the skin is the case that stopping at the skin would leave unable to move.
        var meshes = new List<string>();
        string? skeleton = null;

        while (queue.Count > 0 && (meshes.Count == 0 || skeleton is null))
        {
            (string path, int depth) = queue.Dequeue();
            if (path.Length == 0 || !seen.Add(path))
            {
                continue;
            }

            AnimatedObject ao = Object(read, path);
            if (!ao.Ready)
            {
                continue;
            }

            walked.Add(path);

            if (meshes.Count == 0)
            {
                foreach (AoStruct block in ao.Named(Block))
                {
                    var skinned = false;
                    foreach (AoEntry entry in block.Entries)
                    {
                        // A SHAPE'S MATERIAL BESIDE THE SKIN, not under it: the player's IntFourB.ao indents its first shape line under
                        // `skin = "…Witch.sm"` and the fifty-six after it one tab less, so read by indentation they were the block's own
                        // entries and the body wore the skin's default paint but for one shape. The key is a shape's name either way.
                        if (skinned && Shaped(entry))
                        {
                            materials.Add((entry.Key, entry.Value));
                            continue;
                        }

                        if (!string.Equals(entry.Key, Entry, StringComparison.Ordinal)
                            || entry.Value.Length == 0)
                        {
                            continue;
                        }

                        skinned = true;

                        // THE MATERIAL SITS UNDER THE SKIN, ONE CHILD PER SHAPE, and this used
                        // to take the first on the reasoning that they name the same file with
                        // different indices. That holds for a monster cut from one sheet and
                        // NOT for one built out of parts: reported from the live client, Veynar
                        // the Frostbane draws in plain ink here and is plainly painted in the
                        // game, which is what a shape-0 material with no colour map in it looks
                        // like. All of them are kept now and tried in order - see Painted.
                        // KEY AND VALUE BOTH: the child's key is the SHAPE's own name, which
                        // is what joins a material to the part of the mesh it belongs on -
                        // SkinnedMesh.Shapes carries the same names. See Dressed.
                        foreach (AoEntry child in entry.Children)
                        {
                            if (child.Value.Contains(".mat", StringComparison.OrdinalIgnoreCase))
                            {
                                materials.Add((child.Key, child.Value));
                            }
                        }

                        // EVERY SKIN THE BLOCK NAMES, not the first. Reported from the live
                        // client: Zar Wali, the Bone Tyrant is a giant snake skeleton and the
                        // pane drew two floating arms, because his SkinMesh block names SEVEN
                        // manifests - GSSBArmA, ArmB, ArmC, Chest, Head, Neck, Tail - and this
                        // took the first and stopped. They are sections of one body on one rig,
                        // so they are read and joined rather than chosen between.
                        meshes.Add(entry.Value);
                    }

                    if (meshes.Count > 0)
                    {
                        break;
                    }
                }
            }

            if (skeleton is null)
            {
                foreach (AoStruct block in ao.Named(RigBlock))
                {
                    foreach (AoEntry entry in block.Entries)
                    {
                        if (string.Equals(entry.Key, RigEntry, StringComparison.Ordinal)
                            && entry.Value.Length > 0)
                        {
                            skeleton = entry.Value;
                            break;
                        }
                    }

                    if (skeleton is not null)
                    {
                        break;
                    }
                }
            }

            if (depth >= MostHops)
            {
                continue;
            }

            foreach (string parent in ao.Extends)
            {
                queue.Enqueue((parent, depth + 1));
            }
        }

        return meshes.Count == 0 ? null : (meshes, materials, skeleton ?? string.Empty, walked);
    }

    /// <summary>
    /// Whether a SkinMesh entry beside a skin names one of its shapes' material: a value naming a .mat (or one of its graphs, <c>.mat:0</c>)
    /// under a key that is none of the block's own words.
    /// </summary>
    private static bool Shaped(AoEntry entry)
        => entry.Kind == AoValueKind.Quoted
            && entry.Key is not (Entry or Dropped or "alias")
            && entry.Value.Contains(".mat", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads an .ao, adding the extension where the game left it off.
    /// </summary>
    /// <remarks>
    /// AN EXTENDS LINE CARRIES NO EXTENSION - "extends Metadata/Parent" - while an attached object
    /// carries its .ao in full. The same rule AnimatedObject.Read applies against an install, kept
    /// here too because this reads through a function rather than one.
    /// </remarks>
    private static AnimatedObject Object(Func<string, byte[]?> read, string path)
    {
        string said = path.Replace('\\', '/').Trim();
        if (read(said) is { Length: > 0 } content)
        {
            return AnimatedObject.Read(content);
        }

        int slash = said.LastIndexOf('/');
        return said.IndexOf('.', slash + 1) < 0
            ? AnimatedObject.Read(read(said + AnimatedObject.Suffix))
            : AnimatedObject.None;
    }

    /// <summary>A shape list cut or padded with empty entries to exactly <paramref name="count"/>.</summary>
    private static IEnumerable<string> Padded(IReadOnlyList<string> modes, int count)
    {
        for (var one = 0; one < count; one++)
        {
            yield return one < modes.Count ? modes[one] : string.Empty;
        }
    }

    private static T Read<T>(Func<string, byte[]?> read, string path, Func<byte[]?, T> into)
        => into(path.Length == 0 ? null : read(path.Replace('\\', '/').Trim()));
}
