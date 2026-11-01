using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's monster-files part: where the stage stands the Monster Book's monster, his parts, his lowest point in every animation,
/// and the game's own entity of him - written for Malgor, who floats over Port's deck and throne.
/// </summary>
public sealed class CaptureMonsterFilesTests
{
    private const string Path = "Metadata/Monsters/Test/TestBoss";

    /// <summary>
    /// On a stage with a boss's place: the floor asked at the place and eight round it, his origin over the floor, every animation's
    /// lowest point as the pose works it out, and the game's entity of him with the terrain under it - both halves of where he stands.
    /// </summary>
    [Fact]
    public void ASTAGEDMonsterIsWrittenWithTheFloorHisAnimationsAndTheGamesEntityOfHim()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        AnimationSkeleton rig = RigBody.Rig();
        var actor = new MonsterModel(body, null, "Art/Models/MONSTERS/Test/rig.smd", string.Empty, string.Empty) { Rig = rig };
        var tile = new MonsterModel(Deck(), null, "Metadata/Terrain/Test/Boss.arm", string.Empty, string.Empty)
        {
            BossSpot = new RoomSpot(new Vector2(500f, 500f), 0f, "mapboss", 0),
        };
        MonsterModel staged = StagedModels.Staged(actor, tile, 100, "Metadata/Terrain/Test/Boss.arm");
        Assert.NotNull(staged.Standing);

        WorldEntity boss = new(7, 0x1000, Path + "@73", EntityKind.Monster, 4000f, 3000f, -150f, TerrainHeight: -120f, ModelBoundsZ: 310f);
        WorldEntity other = new(8, 0x2000, "Metadata/Monsters/Other", EntityKind.Monster, 4100f, 3000f, 0f);
        string text = CaptureMonsterFiles.Write(null, new CapturePane("monster", [], staged) { Path = Path }, [boss, other], new Vector2(4000f, 3100f), out _);

        Assert.StartsWith($"monster files: ? [{Path}] - on a stage, 0 parts as hung, 1 of 1 animations swept, 0 parts' own rigs played, 1 of the game's entities of him read", text, StringComparison.Ordinal);

        // THE FLOOR AT THE PLACE AND ROUND IT: nine places asked, the deck at z 40 everywhere - and a boss's place takes their middle.
        Assert.Contains("  floor\tasked at the place and round it: (0, 0) 40; (120, 0) 40;", text, StringComparison.Ordinal);
        Assert.Contains("(85, -85) 40; the middle of those found taken", text, StringComparison.Ordinal);
        Assert.Equal(9, staged.Standing.Floors.Count);

        // HIS ORIGIN ON THE FLOOR, as the game stands him - the rule held against the game's entity below.
        Assert.Contains(" - 0 over the floor he was set on (40)", text, StringComparison.Ordinal);

        // THE ANIMATION'S LOWEST POINT AT FRAME NOUGHT IS THE POSE'S, and where it lands against the floor.
        pose.Take(tracks, 0f);
        float lowest = pose.Lowest(body);
        Assert.Contains($"  {rig.Animations[0].Name}\t{tracks.Frames:0.#}\t{lowest:0.###}\t", text, StringComparison.Ordinal);
        Assert.Contains($"\t{staged.ActorPlace.Translation.Z + lowest - 40f:0.###}\t", text, StringComparison.Ordinal);

        // THE GAME'S ENTITY OF HIM, matched by his path whatever follows its '@', and not the other.
        Assert.Contains("  7\t100\t4000\t3000\t-150\t-120\t-30\t310\tno\t-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\t4100\t", text, StringComparison.Ordinal);
        Assert.Contains("(no install to read them from)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stood where the player stood when the area round him was taken, the floor line says the one nearest the player's own height was
    /// taken - not the middle, a boss's rule, which the line said of every stage before v0.1.375.
    /// </summary>
    [Fact]
    public void THEPLAYERSPlaceSaysItsFloorWasTheOneNearestHisHeight()
    {
        (_, _, SkinnedMesh body) = RigBody.Ready();
        var actor = new MonsterModel(body, null, "Art/Models/MONSTERS/Test/rig.smd", string.Empty, string.Empty) { Rig = RigBody.Rig() };
        var block = new MonsterModel(Deck(), null, AreaBlock.Path, string.Empty, string.Empty) { Kind = ModelKind.Room };
        MonsterModel staged = StagedModels.Staged(actor, block, 100, AreaBlock.Path, stood: new Vector2(500f, 500f), height: 38f);

        string text = CaptureMonsterFiles.Write(null, new CapturePane("monster", [], staged) { Path = Path }, [], null, out _);
        Assert.Contains("(85, -85) 40; the one nearest the player's own height (38) taken", text, StringComparison.Ordinal);
        Assert.Contains("(on the floor nearest his own height there, 38)", text, StringComparison.Ordinal);
    }

    /// <summary>Standing alone, and with no entity of him near: said so, rather than left out.</summary>
    [Fact]
    public void AMONSTERAloneAndUnseenSaysSo()
    {
        (_, _, SkinnedMesh body) = RigBody.Ready();
        var actor = new MonsterModel(body, null, "rig.smd", string.Empty, string.Empty) { Rig = RigBody.Rig() };
        string text = CaptureMonsterFiles.Write(null, new CapturePane("monster", [], actor) { Path = Path }, [], null, out _);
        Assert.Contains("  place\tnot staged: he stands alone, his origin the picture's", text, StringComparison.Ordinal);
        Assert.Contains($"(none of the 0 entities read is {Path} - stand where the game shows him", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A part with a rig of its own is played by that rig - its mesh's box at frame nought, his bone nearest it in his animation of the
    /// same name - and a part on a bone of his has that bone written in every animation: the wheel's and the spyglass's measurements.
    /// </summary>
    [Fact]
    public void APIECEWithARigOfItsOwnIsPlayedByItAndASocketsBoneIsWrittenInEveryAnimation()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        AnimationSkeleton rig = RigBody.Rig();
        const int Held = 5;
        string socket = rig.Bones[Held].Name;
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["piece.ao"] = Encoding.UTF8.GetBytes(
                "version 3\nClientAnimationController\n{\n\tskeleton = \"piece/rig.ast\"\n}\nSkinMesh\n{\n\tskin = \"piece/mesh.sm\"\n}\n"),
            ["piece/rig.ast"] = RigBody.Bytes(),
            ["piece/mesh.sm"] = MonsterModelTests.Sm("piece/mesh.smd", "piece/mesh.mat"),
            ["piece/mesh.smd"] = MonsterModelTests.Smd(),
        };

        var actor = new MonsterModel(body, null, "rig.smd", string.Empty, string.Empty)
        {
            Rig = rig,
            Fitted =
            [
                new PartFit("piece.ao", "<root>", PartPlace.Root, 1, PartKind.Rigid, rig.Bones.Count, 1, 0, 0, false, 0, false, string.Empty, default, default),
                new PartFit("held.ao", socket, PartPlace.Bone, 1, PartKind.Prop, 0, 0, 0, 0, false, 0, false, string.Empty, default, default),
            ],
        };

        string text = CaptureMonsterFiles.Write(
            path => files.TryGetValue(path, out byte[]? bytes) ? bytes : null, new CapturePane("monster", [], actor) { Path = Path }, null, null, out _);
        Assert.Contains("animations swept, 1 parts' own rigs played,", text, StringComparison.Ordinal);

        // THE SOCKET'S BONE AT FRAME NOUGHT, as the pose puts it, and its scale.
        string animation = rig.Animations[0].Name;
        pose.Take(tracks, 0f);
        Matrix4x4 bone = pose.World[Held];
        float scale = MathF.Min(new Vector3(bone.M11, bone.M12, bone.M13).Length(), MathF.Min(new Vector3(bone.M21, bone.M22, bone.M23).Length(), new Vector3(bone.M31, bone.M32, bone.M33).Length()));
        Assert.Contains($"  held.ao\t{socket}\t{socket}\t{animation}\t{At(bone.Translation)}\t{F(scale)}\t", text, StringComparison.Ordinal);

        // THE PIECE PLAYED BY ITS OWN RIG: the mesh as the pose moves it, and his bone nearest its middle in his animation of that name.
        SkinnedMesh piece = SkinnedMesh.Read(MonsterModelTests.Smd());
        var moved = new Vector3[piece.Positions.Length];
        pose.Move(piece, moved, new Vector3[moved.Length]);
        Vector3 least = moved.Aggregate(new Vector3(float.MaxValue), Vector3.Min), most = moved.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        Vector3 middle = (least + most) * 0.5f;
        int nearest = Enumerable.Range(0, rig.Bones.Count).MinBy(one => Vector3.Distance(pose.World[one].Translation, middle));
        Assert.Contains($"--- piece.ao  socket <root> (Root, depth 1, Rigid), its rig piece/rig.ast: {rig.Bones.Count} bones, 1 animations; its mesh 4 vertices", text, StringComparison.Ordinal);
        Assert.Contains("  of his 1 animations it has 1 by name; his it lacks: none; its own he lacks: none", text, StringComparison.Ordinal);
        Assert.Contains($"  {animation}\t{tracks.Frames:0.#}\t{At(least)} .. {At(most)}\t", text, StringComparison.Ordinal);
        Assert.Contains($"\tyes\t{rig.Bones[nearest].Name} at {At(pose.World[nearest].Translation)}", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A weapon hung rigidly in his hand still brings its own rig - its bones written, the physics bone among them - and the effects its .ao
    /// names are followed hop by hop: the particle effect, the material it names, the texture - the text whole, the texture beside the part.
    /// The Exile's Staff's lantern (v0.1.401): <c>continuous_effect = "lamp ...fx/lamp.pet"</c> on its bone group, phys_tri_lamp_jntBnd.
    /// </summary>
    [Fact]
    public void AHELDWeaponsRigAndTheEffectsItNamesAreWrittenHopByHop()
    {
        (_, _, SkinnedMesh body) = RigBody.Ready();
        byte[] flame = new byte[148];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(flame, 0);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["staff/StaffHeld.ao"] = Encoding.UTF8.GetBytes(
                "version 3\nextends \"Metadata/Parent\"\n\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"staff/rig.ast\"\n\t}\n"
                + "\tParticleEffects\n\t{\n\t\tcontinuous_effect = \"lamp staff/fx/lamp.pet\"\n\t}\n}\n"),
            ["staff/rig.ast"] = Packed.Skeleton(
                [("root_jntBnd", 255, 1, 0f), ("staff_jntBnd", 255, 2, -50f), ("phys_tri_lamp_jntBnd", 255, 255, -100f)],
                [("idle_01", string.Empty, 3, 30, 0x6f, 0, 0)]),
            ["Metadata/Parent.ao"] = Encoding.UTF8.GetBytes("version 3\n"),
            ["staff/fx/lamp.pet"] = Encoding.UTF8.GetBytes("{ \"render_material\": \"staff/fx/lamp.mat\" }"),
            ["staff/fx/lamp.mat"] = Encoding.UTF8.GetBytes("{ \"texture\": \"staff/fx/flame.dds\" }"),
            ["staff/fx/flame.dds"] = flame,
        };

        var actor = new MonsterModel(body, null, "rig.smd", string.Empty, string.Empty)
        {
            Rig = RigBody.Rig(),
            Fitted = [new PartFit("staff/StaffHeld.ao", "R_Weapon", PartPlace.Bone, 1, PartKind.Rigid, 0, 0, 0, 0, false, 0, false, string.Empty, default, default)],
        };

        string text = CaptureMonsterFiles.Write(
            path => files.TryGetValue(path, out byte[]? bytes) ? bytes : null, new CapturePane("monster", [], actor) { Path = Path }, null, null,
            out IReadOnlyList<(string File, byte[] Bytes)> wholes);

        // ITS OWN RIG, though it hangs with no bone of his: its bones by index, name, parent and place at rest in the piece's frame.
        Assert.Contains("--- staff/StaffHeld.ao  socket R_Weapon (Bone, depth 1, Rigid), its rig staff/rig.ast: 3 bones, 1 animations; no mesh read", text, StringComparison.Ordinal);
        Assert.Contains("  bone\t2\tphys_tri_lamp_jntBnd\tstaff_jntBnd\t(0, 0, -150)", text, StringComparison.Ordinal);

        // THE EFFECT HOP BY HOP: the .ao's line naming it, the effect, the material it names, the texture the material names.
        Assert.Contains("  named\tstaff/StaffHeld.ao\tcontinuous_effect = \"lamp staff/fx/lamp.pet\"", text, StringComparison.Ordinal);
        Assert.Contains("  file\tstaff/fx/lamp.pet\t", text, StringComparison.Ordinal);
        Assert.Contains("\t1\tstaff/StaffHeld.ao\ttext\t-", text, StringComparison.Ordinal);
        Assert.Contains("  file\tstaff/fx/lamp.mat\t", text, StringComparison.Ordinal);
        Assert.Contains("\tmonster-files-effect-1-flame.dds", text, StringComparison.Ordinal);
        Assert.Contains("##### staff/fx/lamp.pet  (hop 1, named by staff/StaffHeld.ao)", text, StringComparison.Ordinal);
        Assert.Contains("  | { \"render_material\": \"staff/fx/lamp.mat\" }", text, StringComparison.Ordinal);

        // WHAT AN OBJECT EXTENDS IS FOLLOWED, its rig is not: the rig is a part's business above, not an effect's.
        Assert.Contains("  file\tMetadata/Parent.ao\t", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  file\tstaff/rig.ast", text, StringComparison.Ordinal);
        Assert.Equal([("monster-files-effect-1-flame.dds", flame)], wholes.Select(one => (one.File, one.Bytes)));
    }

    private static string At(Vector3 point) => $"({F(point.X)}, {F(point.Y)}, {F(point.Z)})";

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A deck a thousand square at z 40, facing up (minus z).</summary>
    private static SkinnedMesh Deck() => SkinnedMesh.Of(
        [new(0f, 0f, 40f), new(1000f, 0f, 40f), new(1000f, 1000f, 40f), new(0f, 1000f, 40f)],
        [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ],
        [0, 1, 2, 0, 2, 3],
        new Vector3(0f, 0f, 40f),
        new Vector3(1000f, 1000f, 40f));
}
