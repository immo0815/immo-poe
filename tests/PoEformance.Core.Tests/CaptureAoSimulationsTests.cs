using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The .ao simulations part (v0.1.418): every .ao grouped by the simulation its chain names, its rig's simulated bones read, and how much the
/// tables and the other .ao files use it - and the part worked out again from its numbers.
/// </summary>
public sealed class CaptureAoSimulationsTests
{
    /// <summary>WandererStaffHeld.ao's simulation lines, the set the tool swings.</summary>
    private const string Measured =
        "\t\tlinear_allowed_contraction = 0.000\n\t\tbend_stiffness = 800.000\n\t\tbend_allowed_angle = 0.000\n"
        + "\t\tnormal_viscosity_multiplier = 0.000\n\t\ttangent_viscosity_multiplier = 0.000\n\t\tenclosure_radius = 5.000\n"
        + "\t\tenclosure_angle = 5.000\n\t\tanimation_velocity_factor = 2.000\n\t\tanimation_force_factor = 2.000\n\t\tuse_shock_propagation = false\n";

    /// <summary>
    /// A staff with the lamp's set, a staff extending it, a cape with another set hung by a monster, and a plain sword: two sets, the swung
    /// one marked, each file's rig read for its rods and chains, and the rows reaching each counted - the cape's through the monster's .ao.
    /// </summary>
    [Fact]
    public void EVERYAoIsGroupedByItsSimulationAndCountedByItsUse()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Metadata/Items/Staff.ao"] = Text(Ao("Art/Staff/rig.ast", Measured)),
            ["Metadata/Items/StaffChild.ao"] = Text("version 3\nextends \"Metadata/Items/Staff\"\n"),
            ["Metadata/Items/Cape.ao"] = Text(Ao("Art/Cape/rig.ast", "\t\tlinear_allowed_contraction = 0.600\n\t\tanimation_force_factor = 10.000\n")),
            ["Metadata/Items/Sword.ao"] = Text(Ao("Art/Sword/rig.ast", string.Empty)),
            ["Metadata/Monsters/Goblin.ao"] = Text("version 3\nclient\n{\n\tAttachedAnimatedObject\n\t{\n\t\tattached_object = \"Back Metadata/Items/Cape.ao\"\n\t}\n}\n"),
            ["Art/Staff/rig.ast"] = Packed.Skeleton(
                [("root_jntBnd", 255, 1, 0f), ("base_lamp_jntBnd", 255, 2, -10f), ("phys_tri_lamp_jntBnd", 255, 255, -20f)], []),
            ["Art/Cape/rig.ast"] = Packed.Skeleton(
                [("root_jntBnd", 255, 1, 0f), ("phys_skinned_rope_01", 255, 2, -5f), ("phys_skinned_rope_02", 255, 255, -5f)], []),
        };

        IReadOnlyList<IReadOnlyList<string>> items = [["Staff", "Metadata/Items/Staff.ao"], ["StaffAgain", "Metadata/Items/Staff.ao"], ["Child", "Metadata/Items/StaffChild.ao"]];
        IReadOnlyList<IReadOnlyList<string>> monsters = [["Goblin", "Metadata/Monsters/Goblin.ao"]];
        List<string> lines = CaptureAoSimulations.Write(
            path => files.TryGetValue(path, out byte[]? bytes) ? bytes : null,
            [.. files.Keys.Where(one => one.EndsWith(".ao", StringComparison.OrdinalIgnoreCase))],
            items, monsters, "fixture", new CaptureAoSimulations.Running(), out string data);
        string text = string.Join('\n', lines);

        Assert.StartsWith("5 .ao files read (0 not)", lines[0], StringComparison.Ordinal);
        Assert.Contains(": 3 carry a simulation in 2 sets; 2 rigs read", lines[0], StringComparison.Ordinal);

        // THE LAMP'S SET FIRST - three rows reach its two files - and marked as the one swung.
        Assert.Contains("--- set 1 - SWUNG (BoneSwing, v0.1.416): 2 files, 1 rigs; used 3 (items 3, monsters 0;", text, StringComparison.Ordinal);
        Assert.Contains("    Metadata/Items/Staff.ao  used 2 (items 2, monsters 0, attached by 0)  3 bones, 1 simulated: 1 rods, 0 chains (the longest 1)", text, StringComparison.Ordinal);

        // THE CAPE'S, REACHED BY THE MONSTER THROUGH HIS .ao, its rig a chain of two skinned bones.
        Assert.Contains("--- set 2: 1 files, 1 rigs; used 1 (items 0, monsters 1; named directly 0 and 0, attached by 1 .ao lines)", text, StringComparison.Ordinal);
        Assert.Contains("  keys: animation_force_factor=10, linear_allowed_contraction=0.6", text, StringComparison.Ordinal);
        Assert.Contains("3 bones, 2 simulated: 0 rods, 1 chains (the longest 2), 2 skinned", text, StringComparison.Ordinal);

        // THE SWORD SAYS NOTHING OF A SIMULATION, and every key is tallied, the rig's among them.
        Assert.DoesNotContain("Sword.ao", text, StringComparison.Ordinal);
        Assert.Contains("  skeleton\t4\t(not a simulation key)", text, StringComparison.Ordinal);
        Assert.Contains("  bend_stiffness\t2", text, StringComparison.Ordinal);

        // THE REPLAY RANKS THE SETS AGAIN FROM THE NUMBERS ALONE.
        List<string> again = CaptureAoSimulations.Replay(data);
        Assert.Equal("3 simulated .ao files worked out again from ao-simulations.tsv", again[0]);
        Assert.Equal(
            string.Join('\n', lines.SkipWhile(one => !one.StartsWith("=== the simulation sets", StringComparison.Ordinal)).TakeWhile(one => !one.StartsWith("=== every key", StringComparison.Ordinal))).TrimEnd(),
            string.Join('\n', again.SkipWhile(one => !one.StartsWith("=== the simulation sets", StringComparison.Ordinal))).TrimEnd());
    }

    /// <summary>A file of nothing to read, a missing walk and a stop say so rather than throw.</summary>
    [Fact]
    public void ANEMPTYWalkSaysSo()
    {
        List<string> lines = CaptureAoSimulations.Write(_ => null, null, null, null, "none", new CaptureAoSimulations.Running(), out string data);
        Assert.StartsWith("0 .ao files read (0 not)", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("# path\tset", data, StringComparison.Ordinal);
    }

    private static string Ao(string rig, string simulation)
        => "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"" + rig + "\"\n" + simulation + "\t}\n}\n";

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
}
