using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>A pane's last stage: built once per key, waited for while it is built, let go of when another or none is asked for.</summary>
public class StageKeepTests
{
    [Fact]
    public void ASTAGEIsBuiltOncePerKeyAndAnotherKeyLetsItGo()
    {
        var keep = new StageKeep();
        var built = 0;
        MonsterModel Build()
        {
            built++;
            return Ready();
        }

        MonsterModel first = keep.Tile("Boss_01.arm|port", Build, out bool kept);
        Assert.False(kept);
        Assert.Same(first, keep.Tile("Boss_01.arm|port", Build, out kept));
        Assert.True(kept);
        Assert.Equal(1, built);

        // ANOTHER KEY IS ANOTHER STAGE, and the first is not kept beside it.
        keep.Tile("Boss_01.arm|port|mirrored", Build, out kept);
        Assert.False(kept);
        keep.Tile("Boss_01.arm|port", Build, out kept);
        Assert.False(kept);
        Assert.Equal(3, built);

        // NONE ASKED FOR, NONE KEPT.
        keep.Forget();
        keep.Tile("Boss_01.arm|port", Build, out kept);
        Assert.False(kept);
        Assert.Equal(4, built);
    }

    [Fact]
    public void ASTAGEThatFailedOrCameOutEmptyIsBuiltAgain()
    {
        var keep = new StageKeep();
        Assert.Throws<InvalidOperationException>(() => keep.Tile("a", () => throw new InvalidOperationException("no"), out _));
        Assert.True(keep.Tile("a", Ready, out bool kept).Ready);
        Assert.False(kept);

        keep.Tile("b", () => MonsterModel.None, out _);
        keep.Tile("b", Ready, out kept);
        Assert.False(kept);
    }

    [Fact]
    public async Task ASTAGEBeingBuiltIsWaitedForNotBuiltTwice()
    {
        var keep = new StageKeep();
        var built = 0;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        MonsterModel Slow()
        {
            Interlocked.Increment(ref built);
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return Ready();
        }

        Task<MonsterModel> first = Task.Run(() => keep.Tile("a", Slow, out _));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var keptSecond = false;
        Task<MonsterModel> second = Task.Run(() =>
        {
            MonsterModel got = keep.Tile("a", Slow, out bool kept);
            keptSecond = kept;
            return got;
        });

        release.Set();
        Assert.Same(await first, await second);
        Assert.True(keptSecond);
        Assert.Equal(1, built);
    }

    /// <summary>A model that counts as ready - one with a mesh - and a new one each time.</summary>
    private static MonsterModel Ready()
    {
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f)];
        SkinnedMesh mesh = SkinnedMesh.Of(positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2], Vector3.Zero, Vector3.One, null, [new MeshShape("0", 0, 3)]);
        return new MonsterModel(mesh, null, "Boss_01.arm", string.Empty, string.Empty);
    }
}
