using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The readers written after poe_data_tools' <c>file_parsers</c> that the tool had none of: the master's tables
/// (<c>.cht</c>, <c>.clt</c>, <c>.dct</c>, <c>.ddt</c>), the ground and edge types (<c>.gt</c>, <c>.et</c>,
/// <c>.gcf</c>), animation data (<c>.amd</c>), effects (<c>.epk</c>, <c>.pet</c>, <c>.trl</c>) and the passive graph
/// (<c>.psg</c>).
/// </summary>
/// <remarks>
/// The master's tables and the ground types are held against every one the committed captures write whole
/// (<c>area-masters.txt</c>); the rest against texts written to the reference's grammar, since no capture carries
/// one. The parser survey (<c>parser-survey.txt</c>) holds them all against the install.
/// </remarks>
public class FileParsersTests
{
    /// <summary>Every master's table and ground type the captures carry reads whole - the game's own files.</summary>
    [Fact]
    public void EVERYTableAndGroundTypeTheCapturesCarryReadsWhole()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string path, string text) in CapturedFiles())
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string why = extension switch
            {
                ChestTable.Extension => ChestTable.Parse(text).Why,
                CritterTable.Extension => CritterTable.Parse(text).Why,
                DecalTable.Extension => DecalTable.Parse(text).Why,
                DoodadTable.Extension => DoodadTable.Parse(text).Why,
                GroundTypeFile.Extension => GroundTypeFile.Parse(text).Why,
                _ => "-",
            };

            if (why == "-")
            {
                continue;
            }

            Assert.True(why.Length == 0, $"{path}: {why}");
            counts[extension] = counts.GetValueOrDefault(extension) + 1;
        }

        Assert.Equal(5, counts[ChestTable.Extension]);
        Assert.Equal(5, counts[CritterTable.Extension]);
        Assert.Equal(2, counts[DecalTable.Extension]);
        Assert.Equal(4, counts[DoodadTable.Extension]);
        Assert.Equal(16, counts[GroundTypeFile.Extension]);
    }

    /// <summary>The Titan Grotto's chest table: the default group's numbers, a third whole number from version 3, a named group after the default's lines.</summary>
    [Fact]
    public void ACHESTTableIsTheDefaultGroupsNumbersThenWeightedChestLists()
    {
        ChestTable read = ChestTable.Parse(
            "version 3\r\n0.3 0.2 0 15 20\r\n"
            + "7 \"TitanValleyPots/ValleyPot01, TitanValleyPots/ValleyPot02\"\r\n# a comment\r\n\r\n"
            + "\"G2_7, G2_8\" 0.5\r\n5 \"TitanGrottoPots\"\r\n"
            + "\"G2_9\" 0.1 0.2 1 2 3\r\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(3, read.Version);
        Assert.Equal(3, read.Groups.Count);
        Assert.Equal(["Default"], read.Groups[0].Areas);
        Assert.Equal(new ChestNumbers(0.3f, 0.2f, 0, 15, 20), read.Groups[0].Numbers);
        Assert.Equal(["TitanValleyPots/ValleyPot01", "TitanValleyPots/ValleyPot02"], read.Groups[0].Entries[0].Chests);
        Assert.Equal(["G2_7", "G2_8"], read.Groups[1].Areas);
        Assert.Equal(0.5f, read.Groups[1].Lone);
        Assert.Null(read.Groups[1].Numbers);
        Assert.Equal(5u, read.Groups[1].Entries[0].Weight);
        Assert.Equal(new ChestNumbers(0.1f, 0.2f, 1, 2, 3), read.Groups[2].Numbers);

        // VERSION 2 HAS NO THIRD NUMBER, so five numbers stop the file there, and a list joined without its space stops it too.
        Assert.NotEqual(string.Empty, ChestTable.Parse("version 2\n0.3 0.2 0 15 20\n").Why);
        Assert.NotEqual(string.Empty, ChestTable.Parse("version 2\n0.3 0.2 0 15\n7 \"a,b\"\n").Why);
    }

    /// <summary>A critter line carries its .ao from version 4 and its second float from 3.</summary>
    [Fact]
    public void ACRITTERLineCarriesItsAoFromVersionFour()
    {
        CritterTable four = CritterTable.Parse(
            "version 4\n0.04\nDefault\n1 \"Metadata/Critters/Bat/Vampire\" \"Metadata/Critters/Bat/Vampire.ao\" 5.0 20.0 2 6\n\"Cave\" 0.5\n");
        Assert.Equal(string.Empty, four.Why);
        Assert.Equal(0.04f, four.Float1);
        Assert.Equal(new CritterLine(1, "Metadata/Critters/Bat/Vampire", "Metadata/Critters/Bat/Vampire.ao", 5, 20, 2, 6), four.Groups[0].Lines[0]);
        Assert.Equal(("Cave", 0.5f), (four.Groups[1].Name, four.Groups[1].Float));

        CritterTable two = CritterTable.Parse("version 2\n0.1\nDefault\n1 \"Metadata/Critters/Rat\" 5.0 2 6\n");
        Assert.Equal(string.Empty, two.Why);
        Assert.Equal(new CritterLine(1, "Metadata/Critters/Rat", null, 5, null, 2, 6), two.Groups[0].Lines[0]);
    }

    /// <summary>A decal table starts with a Default line; each decal a weight, an .atlas, a tag and one or two floats.</summary>
    [Fact]
    public void ADECALTableIsADefaultGroupThenQuotedAreas()
    {
        DecalTable read = DecalTable.Parse(
            "version 2\n0.6\nDefault\n3 \"Art/Decals/blood.atlas\" \"blood\" 1.5\n\"Marrow\" 2\n1 \"Art\\Decals\\bone.atlas\" \"bone\" 1 2\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(new DecalEntry(3, "Art/Decals/blood.atlas", "blood", 1.5f, null), read.Groups[0].Entries[0]);
        Assert.Equal(("Marrow", (float?)2), (read.Groups[1].Area, read.Groups[1].Float));
        Assert.Equal(new DecalEntry(1, "Art/Decals/bone.atlas", "bone", 1, 2), read.Groups[1].Entries[0]);
        Assert.NotEqual(string.Empty, DecalTable.Parse("version 2\n0.6\n\"Marrow\"\n").Why);
    }

    /// <summary>A doodad line's weight may be All; the whole number after the .ao is no float's front; a D and a float may follow.</summary>
    [Fact]
    public void ADOODADTableLinesWeightMayBeAll()
    {
        DoodadTable read = DoodadTable.Parse(
            "version 3\n1 1\n7\nDefault D 0.5\nAll \"Metadata/a.ao\" 3 D 0.25\n4 \"Metadata/b.ao\" 0.5\n\"BalbalaArena\"\n1 \"Metadata/c.ao\"\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal((1f, (uint?)7), (read.Scale, read.Uint1));
        Assert.Equal([1u], read.ScaleNumbers);
        Assert.Equal(("Default", "D", (float?)0.5f), (read.Groups[0].Name, read.Groups[0].D, read.Groups[0].Float1));
        Assert.Equal(new DoodadTableLine(null, "Metadata/a.ao", 3, true, 0.25f), read.Groups[0].Lines[0]);
        Assert.True(read.Groups[0].Lines[0].All);

        // SAFE_U32: 0.5 is the line's float, not a whole number 0 and a .5 the line then fails on.
        Assert.Equal(new DoodadTableLine(4, "Metadata/b.ao", null, false, 0.5f), read.Groups[0].Lines[1]);
        Assert.Equal("BalbalaArena", read.Groups[1].Name);

        // DEC_UINT LEAVES WHAT FOLLOWS ITS DIGITS: a first line of a float and a half-float stops the file, as there.
        Assert.NotEqual(string.Empty, DoodadTable.Parse("version 3\n1 0.5\nDefault\n").Why);
    }

    /// <summary>Sulphuric Caverns' black_inside_wall.gt, and the optional string a ground type may carry.</summary>
    [Fact]
    public void AGROUNDTypeIsANameTwoToFiveFlagsAndAnOptionalString()
    {
        Assert.Equal(new[] { false, true, false, false }, GroundTypeFile.Parse("BlackInsideWall\r\n0 1 0 0\r\n").Flags);
        GroundTypeFile read = GroundTypeFile.Parse("Stromatolite\n1 1\n\"extra\"\n");
        Assert.Equal(("Stromatolite", "extra", string.Empty), (read.Name, read.Extra, read.Why));
        Assert.NotEqual(string.Empty, GroundTypeFile.Parse("Stromatolite\n1\n").Why);
        Assert.NotEqual(string.Empty, GroundTypeFile.Parse("Stromatolite\n1 2\n").Why);
    }

    /// <summary>An edge type's name and colour, its two grounds (one a wildcard), its numbers, one more ground and a virtual section.</summary>
    [Fact]
    public void ANEDGETypeIsANameTwoGroundsAndWhatFollows()
    {
        EdgeTypeFile read = EdgeTypeFile.Parse(
            "coral_thickwall #FF8800\n\"Metadata/Terrain/a.gt\"\nwildcard\n1 2 0 1\nMetadata\\Terrain\\b.gt\nvirtual\n"
            + "Metadata/Terrain/x.et 1\nMetadata/Terrain/y.et 0\n90 180\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(("coral_thickwall", "FF8800"), (read.Name, read.Hex));
        Assert.Equal(["Metadata/Terrain/a.gt", EdgeTypeFile.Wildcard], read.Grounds);
        Assert.Equal(new EdgeTypeNumbers(1, 2, false, true, null, null), read.Numbers);
        Assert.Equal("Metadata/Terrain/b.gt", read.OtherGround);
        Assert.Equal([new EdgeTypeVirtual("Metadata/Terrain/x.et", true), new EdgeTypeVirtual("Metadata/Terrain/y.et", false)], read.Virtual);
        Assert.Equal(((uint)90, (uint)180), read.Rotations);

        Assert.Equal(string.Empty, EdgeTypeFile.Parse("plain\na.gt\nb.gt\n").Why);
        Assert.NotEqual(string.Empty, EdgeTypeFile.Parse("plain\na.gt\nb.dds\n").Why);
    }

    /// <summary>A ground combination line is exactly three quoted ground types.</summary>
    [Fact]
    public void AGROUNDCombinationLineIsThreeQuotedGroundTypes()
    {
        GroundCombinations read = GroundCombinations.Parse("version 1\n// a comment\n\"a.gt\" \"b.gt\"  \"c.gt\"\n");
        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(new GroundCombination("a.gt", "b.gt", "c.gt"), Assert.Single(read.Lines));
        Assert.NotEqual(string.Empty, GroundCombinations.Parse("version 1\n\"a.gt\" \"b.gt\"\n").Why);
        Assert.NotEqual(string.Empty, GroundCombinations.Parse("version 1\n\"a.gt\" \"b.gt\" \"\"\n").Why);
    }

    /// <summary>
    /// An animation data file of version 5: lines cut at tabs too, a group's stages three lines each, its counted
    /// floats, its bone rotations, the two trailing numbers, and the bone groups.
    /// </summary>
    [Fact]
    public void ANANIMATIONDataFileIsCutAtTabsToo()
    {
        AnimationData read = AnimationData.Parse(
            "version 5\n1\n\"attack_01\"\nmelee\n840\n2\n"
            + "\"start\"\t100\t1 2 3\n"
            + "\"hit\"\n400\n4 5 6\n"
            + "2 0.5 1.5\n"
            + "1 2\n\"spine_01\" xy 1 2 3 4\n"
            + "-1\n7\n"
            + "BoneGroups 1\n\"upper\" 2 \"spine_01\" \"head\"\n");

        Assert.Equal(string.Empty, read.Why);
        AnimationDataGroup group = Assert.Single(read.Groups);
        Assert.Equal(("attack_01", "melee", 840u), (group.Name, group.Type, group.Time));
        Assert.Equal(new AnimationDataStage("start", 100, new(1, 2, 3)), group.Stages[0]);
        Assert.Equal(new AnimationDataStage("hit", 400, new(4, 5, 6)), group.Stages[1]);
        Assert.Equal([0.5f, 1.5f], group.Floats!);
        AnimationDataRotation rotation = Assert.Single(group.Rotations!);
        Assert.Equal(("spine_01", "xy"), (rotation.Bone, rotation.Order));
        Assert.Equal([1f, 2, 3, 4], rotation.Coordinates);
        Assert.Equal(((uint?)null, (uint?)7), group.ExtraInts);
        Assert.Equal(["spine_01", "head"], Assert.Single(read.BoneGroups!).Bones);

        // VERSION 1 HAS NO FLOATS, ROTATIONS OR BONE GROUPS; version 0 has no reading at all.
        AnimationData one = AnimationData.Parse("version 1\n1\n\"idle\"\nloop\n10\n0\n");
        Assert.Equal(string.Empty, one.Why);
        Assert.Null(one.Groups[0].Floats);
        Assert.Null(one.BoneGroups);
        Assert.NotEqual(string.Empty, AnimationData.Parse("version 0\n1\n\"idle\"\nloop\n10\n0\n").Why);
    }

    /// <summary>A particle effect's counted emitters, each a type, a .mat or "" and a body, and the JSON joined after them.</summary>
    [Fact]
    public void APARTICLEEffectIsCountedEmittersThenAJsonPayload()
    {
        ParticleEmitters read = ParticleEmitters.Parse(
            "version 2\n2\n{\nSpriteEmitter\n\"Art/a.mat\"\nlife 1\nsize 2\n}\n{\nMeshEmitter\n\"\"\n}\n{\"update_material\":\n\"x.mat\"}\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(2, read.Version);
        Assert.Equal(new ParticleEmitter("SpriteEmitter", "Art/a.mat", "life 1\nsize 2"), read.Emitters[0]);
        Assert.Equal(new ParticleEmitter("MeshEmitter", null, string.Empty), read.Emitters[1]);
        Assert.Equal("{\"update_material\":\"x.mat\"}", read.Payload);

        Assert.Equal(string.Empty, ParticleEmitters.Parse("1\n{\nA\n\"\"\n}\n").Why);
        Assert.NotEqual(string.Empty, ParticleEmitters.Parse("1\n{\nA\n\"\"\n}\n{not json,}\n").Why);
        Assert.NotEqual(string.Empty, ParticleEmitters.Parse("1\n{\nA\n\"a.dds\"\n}\n").Why);
    }

    /// <summary>A trail effect's count comes before its version; its emitters are key and value lines.</summary>
    [Fact]
    public void ATRAILEffectsCountComesBeforeItsVersion()
    {
        TrailEffects read = TrailEffects.Parse("1\nversion 3\n{\nmaterial \"a.mat\"\nwidth 10 20\nwidth 30\n}\n[1, 2]\n");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(3, read.Version);
        TrailEmitter emitter = Assert.Single(read.Emitters);
        Assert.Equal(3, emitter.Values.Count);
        Assert.Equal("\"a.mat\"", emitter["material"]);
        Assert.Equal("30", emitter["width"]);
        Assert.Equal("[1, 2]", read.Payload);
        Assert.NotEqual(string.Empty, TrailEffects.Parse("1\n{\nlonely\n}\n").Why);
    }

    /// <summary>
    /// An effect package's keywords, its render passes block with a trailing comma, its repairs - the closing ""
    /// and the line break after "&lt;root&gt;" - and an unknown keyword as Other.
    /// </summary>
    [Fact]
    public void ANEFFECTPackageIsALineAnEffectAndARenderPassesBlock()
    {
        EffectPackage read = EffectPackage.Parse(
            "RenderPasses\n{\n\"passes\": [\n{ \"filename\": \"a.mat\", \"is_main\": true, \"type\": null },\n]\n}\n"
            + "ParticleEffect \"*\" \"Metadata/Effects/a.pet\" \"limit\" 1 2 \"ignore_errors\"\n"
            + "TrailEffect \"*\" \"Metadata/Effects/b.trl\"\n"
            + "AttachedObjectEx \"jnt\" 2 \"a.ao\" \"b.fmt\" 1 2 0.5 1.5 -90 \"multi_attach\"\n"
            + "ChildAttachedObject \"c.ao\" from_bone \"a, b\" from_bone_group_index -1 to_bone \"root\" to_child_bone \"x\" to_child_bone_group_index 2\n"
            + "// a comment\n"
            + "HideFirstPassUsingEPKParameter speed 0.5 1\n"
            + "PlayMiscEffectPackAfterDelay \"boom\" 0.25\n"
            + "ApplyToAllPasses\n"
            + "SomethingNew 1 2 3\n"
            + "AttachedObject \"<root>\"\n\"d.ao\"\"");

        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(10, read.Effects.Count);
        Assert.Equal(new RenderPass("a.mat", true, null, null), Assert.Single(Assert.IsType<PackagePasses>(read.Effects[0]).Passes));
        Assert.Equal(new PackageEmitter("ParticleEffect", "*", "Metadata/Effects/a.pet", (1u, 2u), true), read.Effects[1]);
        Assert.Equal(new PackageEmitter("TrailEffect", "*", "Metadata/Effects/b.trl", null, false), read.Effects[2]);

        var ex = Assert.IsType<PackageAttachedEx>(read.Effects[3]);
        Assert.Equal(("jnt", (1u, 2u), (0.5f, 1.5f), ((int?)-90, (int?)null)), (ex.Point, ex.Numbers, ex.Floats, ex.Rotations));
        Assert.Equal(["a.ao", "b.fmt"], ex.Files);
        Assert.Equal((false, true, false), (ex.IncludeAux, ex.MultiAttach, ex.IgnoreErrors));

        var child = Assert.IsType<PackageChild>(read.Effects[4]);
        Assert.Equal(["a", "b"], child.From.Names);
        Assert.Null(child.From.Index);
        Assert.Equal(["root"], child.To!.Names);
        Assert.Equal(["x"], child.To.Children!);
        Assert.Equal(2u, child.To.Index);

        Assert.Equal(new PackageHideUsing("HideFirstPassUsingEPKParameter", "speed", 0.5f, 1), read.Effects[5]);
        Assert.Equal(new PackagePlay("PlayMiscEffectPackAfterDelay", "boom", 0.25f), read.Effects[6]);
        Assert.Equal(new PackageFlag("ApplyToAllPasses"), read.Effects[7]);
        Assert.Equal(new PackageOther("SomethingNew", "1 2 3"), read.Effects[8]);
        Assert.Equal(new PackageAttached("<root>", "d.ao", false), read.Effects[9]);

        // A KNOWN KEYWORD WHOSE ARGUMENTS DO NOT READ STOPS THE FILE, and so does an indented line.
        Assert.NotEqual(string.Empty, EffectPackage.Parse("ParticleEffect \"*\" \"a.trl\"\n").Why);
        Assert.NotEqual(string.Empty, EffectPackage.Parse("ApplyToAllPasses\n  ApplyToAllPasses\n").Why);
        Assert.Equal(new PackagePasses([]), Assert.Single(EffectPackage.Parse("RenderPasses { }").Effects));
    }

    /// <summary>A passive graph of one group with one passive and one connection, read whole in PoE2's layout; a count past the end stops it.</summary>
    [Fact]
    public void APASSIVEGraphIsGroupsOfPassivesOnOrbits()
    {
        var bytes = new List<byte> { 3, 1, 2, 1, 6 };
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(77ul));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(10.5f));
        bytes.AddRange(BitConverter.GetBytes(-20f));
        bytes.AddRange(BitConverter.GetBytes(4u));
        bytes.AddRange(BitConverter.GetBytes(3u));
        bytes.Add(1);
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(500u));
        bytes.AddRange(BitConverter.GetBytes(2));
        bytes.AddRange(BitConverter.GetBytes(5u));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(501u));
        bytes.AddRange(BitConverter.GetBytes(-3));

        PassiveGraph read = PassiveGraph.Read(bytes.ToArray());
        Assert.Equal(string.Empty, read.Why);
        Assert.Equal(((byte)3, (byte)1), (read.Version, read.GraphType));
        Assert.Equal([(byte)1, (byte)6], read.PassivesPerOrbit);
        Assert.Equal([77ul], read.Roots);
        PassiveGraphGroup group = Assert.Single(read.Groups);
        Assert.Equal((10.5f, -20f, 4u, 3u, (byte)1), (group.X, group.Y, group.Flags, group.Unk1, group.Unk2));
        PassiveGraphNode node = Assert.Single(group.Passives);
        Assert.Equal((500u, 2, 5u), (node.Id, node.Orbit, node.OrbitPosition));
        Assert.Equal(new PassiveGraphLink(501, -3), Assert.Single(node.Links));

        bytes[^28] = 0xFF;
        Assert.NotEqual(string.Empty, PassiveGraph.Read(bytes.ToArray()).Why);
        Assert.NotEqual(string.Empty, PassiveGraph.Read([.. bytes, 0]).Why);
    }

    /// <summary>Every file the captures' area-masters.txt writes whole: its path and its text, the "  | " each line is written behind taken off.</summary>
    internal static IEnumerable<(string Path, string Text)> CapturedFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        foreach (string masters in Directory.GetFiles(Path.Combine(dir.FullName, "tests", "fixtures", "captures"), "area-masters.txt", SearchOption.AllDirectories))
        {
            string? path = null;
            var text = new System.Text.StringBuilder();
            foreach (string line in File.ReadLines(masters))
            {
                if (line.StartsWith("##### ", StringComparison.Ordinal))
                {
                    if (path is not null)
                    {
                        yield return (path, text.ToString());
                    }

                    path = line[6..].Split("  (", 2)[0].Trim();
                    _ = text.Clear();
                }
                else if (path is not null && line.StartsWith("  |", StringComparison.Ordinal))
                {
                    _ = text.Append(line.Length > 4 ? line[4..] : string.Empty).Append('\n');
                }
            }

            if (path is not null)
            {
                yield return (path, text.ToString());
            }
        }
    }
}
