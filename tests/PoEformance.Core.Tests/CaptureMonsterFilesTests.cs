using System.Numerics;
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
        string text = CaptureMonsterFiles.Write(null, new CapturePane("monster", [], staged) { Path = Path }, [boss, other], new Vector2(4000f, 3100f));

        Assert.StartsWith($"monster files: ? [{Path}] - on a stage, 0 parts as hung, 1 of 1 animations swept, 1 of the game's entities of him read", text, StringComparison.Ordinal);

        // THE FLOOR AT THE PLACE AND ROUND IT: nine places asked, the deck at z 40 everywhere.
        Assert.Contains("  floor\tasked at the place and round it: (0, 0) 40; (120, 0) 40;", text, StringComparison.Ordinal);
        Assert.Equal(9, staged.Standing.Floors.Count);

        // HIS ORIGIN OVER THE FLOOR by the rest's lowest point - the rule held against the game's entity below.
        Assert.Contains($" - {body.Most.Z:0.###} over the floor he was set on (40)", text, StringComparison.Ordinal);

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

    /// <summary>Standing alone, and with no entity of him near: said so, rather than left out.</summary>
    [Fact]
    public void AMONSTERAloneAndUnseenSaysSo()
    {
        (_, _, SkinnedMesh body) = RigBody.Ready();
        var actor = new MonsterModel(body, null, "rig.smd", string.Empty, string.Empty) { Rig = RigBody.Rig() };
        string text = CaptureMonsterFiles.Write(null, new CapturePane("monster", [], actor) { Path = Path }, [], null);
        Assert.Contains("  place\tnot staged: he stands alone, his origin the picture's", text, StringComparison.Ordinal);
        Assert.Contains($"(none of the 0 entities read is {Path} - stand where the game shows him", text, StringComparison.Ordinal);
    }

    /// <summary>A deck a thousand square at z 40, facing up (minus z).</summary>
    private static SkinnedMesh Deck() => SkinnedMesh.Of(
        [new(0f, 0f, 40f), new(1000f, 0f, 40f), new(1000f, 1000f, 40f), new(0f, 1000f, 40f)],
        [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ],
        [0, 1, 2, 0, 2, 3],
        new Vector3(0f, 0f, 40f),
        new Vector3(1000f, 1000f, 40f));
}
