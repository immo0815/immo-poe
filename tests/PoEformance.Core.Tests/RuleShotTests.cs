using PoEformance.Features;
using PoEformance.Game.Components;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// MonsterCountInShot: the monsters a projectile fired from the player reaches, over the projectiles' grid beside the
/// walkable one - and the aim and IsTargetable asking the same monsters.
/// </summary>
/// <remarks>
/// Beside MonsterCountInSight rather than instead of it: the sight fact keeps the walkable grid, so every rule already
/// written keeps its meaning. The grids are drawn as the captures measured them - floor open in both, a chasm open to a
/// projectile only, a wall shut in both (CaptureFixtureTests.THESECONDGridIsWhereProjectilesStop).
/// </remarks>
public class RuleShotTests
{
    private static readonly RuleTimers Timers = new();

    /// <summary>'.' floor, '~' a chasm, '#' a wall; null for no projectiles' grid.</summary>
    private static TerrainGrid Grid(bool withShots, params string[] rows)
    {
        int width = rows[0].Length;
        int stride = (width + 1) / 2;
        var walkable = new byte[stride * rows.Length];
        var shots = new byte[stride * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte bit = (byte)((x & 1) == 0 ? 0x01 : 0x10);
                int at = (y * stride) + (x / 2);
                if (rows[y][x] == '.')
                {
                    walkable[at] |= bit;
                }

                if (rows[y][x] is '.' or '~')
                {
                    shots[at] |= bit;
                }
            }
        }

        return new TerrainGrid(walkable, stride, rows.Length, 0, 0, heights: null, shots: withShots ? shots : null);
    }

    /// <summary>A chasm between the player's side and the far side, and a wall across the bottom rows.</summary>
    private static TerrainGrid Gorge(bool withShots = true) => Grid(
        withShots,
        "...~~~....",
        "...~~~....",
        "...~~~....",
        "....#.....",
        "....#.....");

    private static NearMonster Monster((float X, float Y) at, (float X, float Y) player, ulong address = 1, double life = 50, bool targetable = true)
    {
        float dx = at.X - player.X;
        float dy = at.Y - player.Y;
        return new NearMonster(MathF.Sqrt((dx * dx) + (dy * dy)), ItemRarity.Normal, at.X, at.Y, life, 0, address, 50, 100, targetable);
    }

    private static RuleState Around(TerrainGrid? grid, (float X, float Y) player, params NearMonster[] monsters) => new()
    {
        InGame = true,
        GameFocused = true,
        Alive = true,
        Terrain = grid,
        PlayerAt = player,
        Monsters = [.. monsters.OrderBy(m => m.Distance)],
    };

    private static RuleCondition Within(RuleFact fact, double radius = 10_000)
        => RuleCondition.Of(fact, Compare.AtLeast, 1) with { Argument = radius };

    [Fact]
    public void AMONSTERAcrossTheChasmIsInShotAndNotInSight()
    {
        (float X, float Y) player = RuleSightTests.At(1, 1);
        RuleState state = Around(Gorge(), player, Monster(RuleSightTests.At(8, 1), player));

        Assert.Equal(0, state.MonsterCountInSight(10_000));
        Assert.Equal(1, state.MonsterCountInShot(10_000));
    }

    [Fact]
    public void AWALLStillStopsTheShot()
    {
        (float X, float Y) player = RuleSightTests.At(1, 4);
        RuleState state = Around(Gorge(), player, Monster(RuleSightTests.At(8, 4), player));

        Assert.Equal(0, state.MonsterCountInSight(10_000));
        Assert.Equal(0, state.MonsterCountInShot(10_000));
    }

    [Fact]
    public void THERADIUSStillApplies()
    {
        (float X, float Y) player = RuleSightTests.At(1, 1);
        RuleState state = Around(Gorge(), player, Monster(RuleSightTests.At(2, 1), player), Monster(RuleSightTests.At(8, 1), player));

        Assert.Equal(2, state.MonsterCountInShot(10_000));
        Assert.Equal(1, state.MonsterCountInShot(20));
    }

    [Fact]
    public void NOProjectilesGridIsNoAnswerRatherThanNone()
    {
        // A zero would say every line is blocked; the truth is that the grid is missing. The sight fact still answers.
        (float X, float Y) player = RuleSightTests.At(1, 1);
        RuleState state = Around(Gorge(withShots: false), player, Monster(RuleSightTests.At(2, 1), player));

        Assert.Null(state.MonsterCountInShot(10_000));
        Assert.Equal(1, state.MonsterCountInSight(10_000));
        Assert.Null(Around(null, player).MonsterCountInShot(10_000));

        RuleCondition condition = Within(RuleFact.MonsterCountInShot);
        Assert.False(condition.Holds(state, Timers, "r"));
        Assert.False((condition with { Compare = Compare.Below, Value = 1 }).Holds(state, Timers, "r"));
    }

    [Fact]
    public void ISTARGETABLEAsksTheMonstersTheShotReaches()
    {
        (float X, float Y) player = RuleSightTests.At(1, 1);
        RuleState state = Around(Gorge(), player, Monster(RuleSightTests.At(8, 1), player));

        Assert.True(RuleCondition.All(Within(RuleFact.MonsterCountInShot), RuleCondition.Of(RuleFact.IsTargetable)).Holds(state, Timers, "r"));
        Assert.False(RuleCondition.All(Within(RuleFact.MonsterCountInSight), RuleCondition.Of(RuleFact.IsTargetable)).Holds(state, Timers, "r"));
        Assert.False(RuleCondition.All(Within(RuleFact.MonsterCountInShot), RuleCondition.Of(RuleFact.IsTargetable))
            .Holds(state with { Terrain = Gorge(withShots: false) }, Timers, "r"));
    }

    [Fact]
    public void THEAIMUsesTheGridOfTheConditionInTheRule()
    {
        // Two monsters under the threshold: the weaker across the chasm, the other on the player's side. Beside the shot
        // fact the aim takes the weaker; beside the sight fact the one across the chasm is not among the candidates.
        (float X, float Y) player = RuleSightTests.At(1, 1);
        RuleState state = Around(
            Gorge(),
            player,
            Monster(RuleSightTests.At(8, 1), player, address: 0xA, life: 5),
            Monster(RuleSightTests.At(1, 2), player, address: 0xB, life: 15));

        Assert.Equal(0xAUL, Assert.NotNull(state.AimTarget(Within(RuleFact.MonsterCountInShot), null, 20)).Address);
        Assert.Equal(0xBUL, Assert.NotNull(state.AimTarget(Within(RuleFact.MonsterCountInSight), null, 20)).Address);
    }

    [Fact]
    public void ITISOfferedToTheEditorBesideTheSightFact()
    {
        FactInfo info = RuleFacts.Describe(RuleFact.MonsterCountInShot);

        Assert.Equal("MonsterCountInShot", info.Name);
        Assert.Equal(FactShape.Number, info.Shape);
        Assert.Equal(FactArgument.Distance, info.Argument);
        Assert.False(info.AtCursor);
        Assert.Same(info, RuleFacts.Find("monstercountinshot"));
        Assert.Equal((int)RuleFact.MonsterCountInSight + 1, (int)RuleFact.MonsterCountInShot);
    }
}
