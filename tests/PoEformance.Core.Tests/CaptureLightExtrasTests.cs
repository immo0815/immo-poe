using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's light extras part over a made-up install: each section finds what it should - the clouds', the fog's and the player
/// light's keys, the sources and graphs naming them, the program's strings, the textures they name, the roof-section materials - and
/// leaves out what it should not; each stays within its bounds and says what it cut; and the offline reader reads it back.
/// </summary>
public sealed class CaptureLightExtrasTests
{
    private const string RedVale = "Metadata/EnvironmentSettings/Gallows/1_5.env";
    private const string Port = "Metadata/EnvironmentSettings/Maps/Port.env";
    private const string Dry = "Metadata/EnvironmentSettings/Maps/Dry.env";
    private const string Broken = "Metadata/EnvironmentSettings/Maps/Broken.env";

    private const string Common = "shaders/renderer/common.ffx";
    private const string Effects = "shaders/renderer/nodes/effects.ffx";
    private const string Lighting = "shaders/renderer/lighting.ffx";
    private const string Legacy = "shaders/renderer/nodes/legacy.ffx";
    private const string Busy = "shaders/renderer/busy.ffx";
    private const string Other = "shaders/renderer/other.ffx";

    private const string FogGraph = "Metadata/EngineGraphs/screenspace_fog.fxgraph";
    private const string SsfGraph = "Metadata/Materials/Environment/SSFTest.fxgraph";
    private const string CloudGraph = "Metadata/Effects/Graphs/Environment/CloudShadows.fxgraph";
    private const string ProofGraph = "Metadata/Effects/Graphs/Skills/waterproof.fxgraph";
    private const string PlainGraph = "Metadata/Effects/Graphs/General/Plain.fxgraph";
    private const string FogMaterial = "Metadata/Materials/Fog.mat";

    private const string Perlin = "Art/2DArt/Lookup/perlin_noise.dds";
    private const string Cell = "Art/2DArt/Lookup/noise_cell_06.dds";
    private const string CloudNoise = "Art/2DArt/Lookup/clouds_noise.dds";
    private const string FogNoise = "Art/2DArt/Lookup/fog_noise.dds";
    private const string CloudMask = "Art/Textures/cloud_mask.dds";
    private const string PlainMask = "Art/Textures/plain_mask.dds";

    private const string Branches = "Art/Models/Trees/RoofFade_JungleTreeBranches01c.mat";
    private const string Forest = "Art/Models/Trees/OFO_BlackForestTrees01A_fadec.mat";
    private const string Dirt = "Art/Textures/Ground/G_Dirt.mat";
    private const string NotRoof = "Art/Textures/Ground/NotRoof.mat";
    private const string Gone = "Art/Textures/Gone.mat";

    [Fact]
    public void THEENVIRONMENTSCloudsFogAndPlayerLightAreCountedKeyByKeyTheAreasOwnFirst()
    {
        string text = Written(out _);

        Assert.Contains("3 of 5 environments set one, 9 keys, 1 did not read as JSON\n", text, StringComparison.Ordinal);

        // A SECTION NAMING A WORD WHOLE, each key with its files and values, the first with the file that wrote it.
        Assert.Contains("  key\tclouds.clouds_scale\t2\t0.8 (" + RedVale + ")  |  0.6 (" + Port + ")\n", text, StringComparison.Ordinal);
        Assert.Contains("  key\tplayer_light.colour\t1\t[0.9, 0.8, 0.7] (" + RedVale + ")\n", text, StringComparison.Ordinal);
        Assert.Contains("  key\tscreenspace_fog.ssf_layer_count\t1\t5.0 (" + RedVale + ")\n", text, StringComparison.Ordinal);

        // A KEY ELSEWHERE taken by its own name - and not where its word stands only inside another (waterproof).
        Assert.Contains("  key\tarea.clouds_extra\t1\t", text, StringComparison.Ordinal);
        Assert.Contains("  key\tcamera.roof_fade_distance\t1\t300 (" + Dry + ")\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("waterproof", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dust_color", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wind_intensity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("directional_light", text, StringComparison.Ordinal);
        Assert.DoesNotContain("water.", text, StringComparison.Ordinal);

        // FILE BY FILE, the area's own first though it sorts after the others.
        Assert.Contains("=== 1b. every environment setting one, file by file - the area's own (" + Port + ") first\n  env\t" + Port + "\t1\tclouds.clouds_scale 0.6\n", text, StringComparison.Ordinal);
        Assert.Contains("  env\t" + RedVale + "\t8\tplayer_light.colour [0.9, 0.8, 0.7]; player_light.intensity 1.5; clouds.clouds_scale 0.8;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void THESOURCESNamingTheWordsAreWrittenWholeTheMostWordsFirst()
    {
        string text = Written(out _);

        Assert.Contains("4 of 6 sources name one\n", text, StringComparison.Ordinal);
        int common = text.IndexOf("  source\t" + Common + "\t", StringComparison.Ordinal);
        int effects = text.IndexOf("  source\t" + Effects + "\t", StringComparison.Ordinal);
        int lighting = text.IndexOf("  source\t" + Lighting + "\t", StringComparison.Ordinal);
        int legacy = text.IndexOf("  source\t" + Legacy + "\t", StringComparison.Ordinal);
        Assert.True(common >= 0 && effects > common && lighting > effects && legacy > lighting, text);
        Assert.Contains("\tclouds_, ssf_, screenspace_shadow, roof_fade, fade_radius\n", text, StringComparison.Ordinal);
        Assert.Contains("\tclouds_, CloudWind, ssf_, SSF\n", text, StringComparison.Ordinal);
        Assert.Contains("\tscreen_space_shadow, player_light, PlayerLight\n", text, StringComparison.Ordinal);

        // THE SEE-THROUGH FADE'S SOURCE, which spells none of the light's other words.
        Assert.Contains("\tSeeThroughFade\n", text, StringComparison.Ordinal);
        Assert.Contains("##### " + Legacy + "  (SeeThroughFade)\n  | FRAGMENT SeeThroughFade\n", text, StringComparison.Ordinal);

        // WHOLE, every line marked, so a source's own rules of = signs are no headings.
        Assert.Contains("##### " + Common + "  (clouds_, ssf_, screenspace_shadow, roof_fade, fade_radius)\n  | uniform pass float clouds_scale\n", text, StringComparison.Ordinal);
        Assert.Contains("  | // ===== the fog =====\n", text, StringComparison.Ordinal);

        // SSF ANY CASE STANDS IN ProcessFrame AND passFlags: the busy source names nothing.
        Assert.DoesNotContain(Busy, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Other, text, StringComparison.Ordinal);
    }

    [Fact]
    public void THEGRAPHSNamingANodeOrWhosePathNamesAWordAreWrittenWhole()
    {
        string text = Written(out _);

        Assert.Contains(": 3 of 5 searched (", text, StringComparison.Ordinal);

        // A NODE FIRST, the most nodes first; a graph in UTF-16 found as one in UTF-8.
        int ssf = text.IndexOf("  graph\t" + SsfGraph + "\t", StringComparison.Ordinal);
        int fog = text.IndexOf("  graph\t" + FogGraph + "\t", StringComparison.Ordinal);
        int cloud = text.IndexOf("  graph\t" + CloudGraph + "\t", StringComparison.Ordinal);
        Assert.True(ssf >= 0 && fog > ssf && cloud > fog, text);
        Assert.Contains("\tSSFLayerCountThicknessTurbulence, SSFColor\t-\n", text, StringComparison.Ordinal);
        Assert.Contains("\tScreenspaceFog\tfog\n", text, StringComparison.Ordinal);
        Assert.Contains("\t-\tcloud\n", text, StringComparison.Ordinal);
        Assert.Contains("##### " + FogGraph + "  (names ScreenspaceFog; its path holds fog)\n", text, StringComparison.Ordinal);
        Assert.Contains("##### " + CloudGraph + "  (names no node; its path holds cloud)\n", text, StringComparison.Ordinal);

        // NOT A ROOF INSIDE PROOF, NOT A GRAPH WHOSE TEXTURE'S NAME HOLDS A NODE'S (RoofFade_Branches_colour.dds), NOT A MATERIAL.
        Assert.DoesNotContain(ProofGraph, text, StringComparison.Ordinal);
        Assert.DoesNotContain(PlainGraph, text, StringComparison.Ordinal);
        Assert.DoesNotContain(FogMaterial, text, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPROGRAMSStringsNamingTheWordsAreListedAFileFirst()
    {
        string text = Written(out _);

        Assert.Contains(" bytes\t5 strings, 2 more holding a word only inside another\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "  string\t1\t" + CloudNoise + "\n  string\t1\tPlayerLightIntensity\n  string\t1\tRoofSection\n  string\t1\tSSF_LAYERS\n  string\t2\tscreenspace_fog_enabled\n",
            text, StringComparison.Ordinal);
        Assert.DoesNotContain("\tProcessFrame\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\twaterproof\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void THETEXTURESNamedAreReadAndTheFirstDrawnBesideThePart()
    {
        string text = Written(out IReadOnlyList<(string File, GamePicture Picture)> pictures);

        // THE ENVIRONMENT'S AND THE LIGHT'S GRAPHS' WHATEVER THEIR PATH; THE OTHERS' ONLY WHERE THE PATH NAMES A WORD.
        Assert.Contains("named above: every one an environment's taken section", text, StringComparison.Ordinal);
        Assert.Contains(" - 5, each one's header and who names it first\n", text, StringComparison.Ordinal);
        int perlin = text.IndexOf("  texture\t" + Perlin + "\tthe environment " + RedVale + "\t", StringComparison.Ordinal);
        int cell = text.IndexOf("  texture\t" + Cell + "\tthe graph " + FogGraph + "\t", StringComparison.Ordinal);
        int noise = text.IndexOf("  texture\t" + CloudNoise + "\tthe program\t(not in the install)\n", StringComparison.Ordinal);
        int source = text.IndexOf("  texture\t" + FogNoise + "\tthe source " + Lighting + "\t", StringComparison.Ordinal);
        int mask = text.IndexOf("  texture\t" + CloudMask + "\tthe graph " + CloudGraph + "\t", StringComparison.Ordinal);
        Assert.True(perlin >= 0 && cell > perlin && noise > cell && source > noise && mask > source, text);
        Assert.DoesNotContain("  texture\t" + PlainMask, text, StringComparison.Ordinal);

        // DRAWN, SHRUNK TO 256 A SIDE, THE ALPHA ONLY WHERE IT IS NOT ONE VALUE.
        Assert.Contains("  decoded\t1\t" + Perlin + "\t64 x 32\tlight-extras-1-perlin_noise-rgb.png, light-extras-1-perlin_noise-a.png\n", text, StringComparison.Ordinal);
        Assert.Contains("  decoded\t2\t" + Cell + "\t600 x 20\tlight-extras-2-noise_cell_06-rgb.png, light-extras-2-noise_cell_06-a.png\n", text, StringComparison.Ordinal);
        Assert.Contains("  undecoded\t" + FogNoise + "\tnot a DDS\n", text, StringComparison.Ordinal);
        Assert.Contains("  decoded\t3\t" + CloudMask + "\t16 x 16\tlight-extras-3-cloud_mask-rgb.png\n", text, StringComparison.Ordinal);
        Assert.Equal(
            ["light-extras-1-perlin_noise-rgb.png", "light-extras-1-perlin_noise-a.png", "light-extras-2-noise_cell_06-rgb.png", "light-extras-2-noise_cell_06-a.png", "light-extras-3-cloud_mask-rgb.png"],
            pictures.Select(one => one.File));
        Assert.Equal((200, 7), (pictures[2].Picture.Width, pictures[2].Picture.Height));
        Assert.All(pictures, one => Assert.Equal(255, one.Picture.Rgba[3]));
    }

    [Fact]
    public void THEROOFSectionsAModelWearsAreCountedByShapesAndTrianglesAndWrittenWhole()
    {
        string text = Written(out _);

        // SIX SHAPES OVER FIVE MATERIALS, ONE NOT IN THE INSTALL, TWO ROOF SECTIONS - the most triangles first.
        Assert.Contains("  pane\tmonster\troom.arm\t6\t5\t1\t2\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "  roof\tmonster\t" + Branches + "\t2\t3\tMetadata/Terrain/Doodads/Trees/JungleTree01.ao, Metadata/Terrain/Doodads/Trees/JungleTree02.ao\n"
            + "  roof\tmonster\t" + Forest + "\t1\t1\tMetadata/Terrain/Doodads/Trees/BlackForest01.ao\n",
            text, StringComparison.Ordinal);
        Assert.DoesNotContain("\t" + Dirt + "\t", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\t" + NotRoof + "\t", text, StringComparison.Ordinal);
        Assert.Contains("=== 6b. the roof-section materials whole: 2", text, StringComparison.Ordinal);
        Assert.Contains("##### " + Branches + "  (\"roofsection\": true, worn in the monster book's pane)\n  | {\"version\":4,\"graphinstances\":", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ITSFirstLineSaysWhatItHoldsAndTheReplayReadsItBack()
    {
        string text = Written(out _);

        Assert.StartsWith(
            "light extras: 3 of 5 environments set 9 keys of the clouds, the fog and the player's light; 4 shader sources name a word, 4 whole; 3 of 5 graphs name a node or their path a word, 3 whole; 5 strings in the program name one; 5 textures named, 3 drawn; 2 roof-section materials over 1 model panes (",
            text, StringComparison.Ordinal);

        string replay = CaptureLightExtras.Replay(text).ReplaceLineEndings("\n");
        Assert.StartsWith(
            "light extras replay: 9 environment keys in 3 files; 4 shader sources, 4 whole; 3 graphs, 3 whole; 5 program strings in 1 programs; 5 textures, 3 drawn; 2 roof-section materials over 1 panes\n",
            replay, StringComparison.Ordinal);
        Assert.Contains("  clouds: 3 keys - clouds_scale 2, clouds_intensity 1, clouds_texture 1\n", replay, StringComparison.Ordinal);
        Assert.Contains("    8 keys  " + RedVale + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  " + Common + "  (clouds_, ssf_, screenspace_shadow, roof_fade, fade_radius) - whole\n", replay, StringComparison.Ordinal);
        Assert.Contains("  SSF: 1 - " + Effects + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  SeeThroughFade: 1 - " + Legacy + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  node SSFColor: 1 - " + SsfGraph + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  path cloud: 1 - " + CloudGraph + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  naming a file: 1 - " + CloudNoise + "\n", replay, StringComparison.Ordinal);
        Assert.Contains("  " + Perlin + "  (the environment " + RedVale + "): ", replay, StringComparison.Ordinal);
        Assert.Contains(" - drawn 64 x 32 as light-extras-1-perlin_noise-rgb.png, light-extras-1-perlin_noise-a.png\n", replay, StringComparison.Ordinal);
        Assert.Contains("    " + Branches + ": 2 shapes, 3 triangles, from Metadata/Terrain/Doodads/Trees/JungleTree01.ao, Metadata/Terrain/Doodads/Trees/JungleTree02.ao - whole\n", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void EACHSectionStaysWithinItsBoundsAndSaysWhatItCut()
    {
        string program = Program();
        try
        {
            var bounds = new CaptureLightExtras.Bounds(
                EnvironmentChars: 10, SourceChars: 260, LargestSource: 1024, GraphChars: 1024, LargestGraph: 100, MostStrings: 2, MostTextures: 2,
                MostPreviews: 1, WidestPreview: 8, RoofChars: 1024, LargestRoof: 1024);
            string text = CaptureLightExtras.Write(
                    Read, [RedVale, Port, Dry, Broken, "missing.env"], [Common, Effects, Lighting, Legacy, Busy, Other],
                    [FogGraph, SsfGraph, CloudGraph, ProofGraph, PlainGraph, FogMaterial], [program], [Pane()], Port, bounds,
                    out IReadOnlyList<(string File, GamePicture Picture)> pictures)
                .ReplaceLineEndings("\n");

            Assert.Contains("(cut: 3 files not written - past the section's 0 KB; their keys are counted above)\n", text, StringComparison.Ordinal);
            Assert.Contains("(cut: 3 sources not written whole - past the section's 0 KB, or the 1 KB one file may take; named above: " + Effects + ", " + Lighting + ", " + Legacy + ")\n", text, StringComparison.Ordinal);
            Assert.Contains("(cut: 2 graphs not written whole - past the section's 1 KB, or the 0 KB one file may take; named above: " + FogGraph + ", " + CloudGraph + ")\n", text, StringComparison.Ordinal);
            Assert.Contains("(cut: 3 more strings not written - past the 2 a program; those naming a file come first)\n", text, StringComparison.Ordinal);
            Assert.Contains("(cut: 3 more textures not read - past the 2: " + CloudNoise + ", " + FogNoise + ", " + CloudMask + ")\n", text, StringComparison.Ordinal);

            // ONE PICTURE, AT EIGHT A SIDE: 64 x 32 by steps of eight.
            Assert.Equal(["light-extras-1-perlin_noise-rgb.png", "light-extras-1-perlin_noise-a.png"], pictures.Select(one => one.File));
            Assert.Equal((8, 4), (pictures[0].Picture.Width, pictures[0].Picture.Height));
        }
        finally
        {
            File.Delete(program);
        }
    }

    [Fact]
    public void BEFORETheInstallWalkHasRunItSaysSo()
    {
        string text = CaptureLightExtras.Write(Read, [], null, null, [], null, null, out IReadOnlyList<(string File, GamePicture Picture)> pictures);

        Assert.StartsWith(
            "light extras: no environments; no shader sources; no graphs; no program to search; 0 textures named, 0 drawn; no model pane (",
            text, StringComparison.Ordinal);
        Assert.Empty(pictures);
        Assert.StartsWith("light extras replay: 0 environment keys in 0 files;", CaptureLightExtras.Replay(text), StringComparison.Ordinal);
    }

    [Fact]
    public void AMATERIALIsARoofSectionWhereItsJsonSaysSo()
    {
        Assert.True(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"version\":4,\"graphinstances\":[],\"roofsection\":true}")));
        Assert.True(CaptureLightExtras.RoofSection(CaptureGroundTests.Text("{\"version\": 4, \"roofsection\": true,}")));
        Assert.True(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"RoofSection\": 1}")));
        Assert.True(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"defaultgraph\": {\"roof_section\": \"true\"}}")));
        Assert.False(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"roofsection\": false}")));
        Assert.False(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"roofsections\": true}")));
        Assert.False(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"name\": \"roofsection\", \"value\": true}")));
        Assert.False(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("{\"roofsection\": ")));
        Assert.False(CaptureLightExtras.RoofSection(Encoding.UTF8.GetBytes("version 3\nroofsection = true\n")));
        Assert.False(CaptureLightExtras.RoofSection(null));
    }

    [Fact]
    public void ROOFAndSsfAreTakenOnlyWhereTheyStandForThemselves()
    {
        Assert.False(CaptureLightExtras.Holds("ProcessFrame", "ssf"));
        Assert.False(CaptureLightExtras.Holds("passFlags", "ssf"));
        Assert.True(CaptureLightExtras.Holds("ssf_color", "ssf"));
        Assert.True(CaptureLightExtras.Holds("SSFColor", "ssf"));
        Assert.True(CaptureLightExtras.Holds("the_ssf", "ssf"));
        Assert.False(CaptureLightExtras.Holds("Art/Textures/waterproof.dds", "roof"));
        Assert.True(CaptureLightExtras.Holds("waterproof_roof", "roof"));
        Assert.True(CaptureLightExtras.Holds("RoofFade", "roof"));
        Assert.True(CaptureLightExtras.Holds("CloudShadows", "cloud"));

        // ONE WORD ANY CASE ONCE; SSF AS WRITTEN.
        Assert.Equal(["roofsection"], CaptureLightExtras.SourceNamed("RoofSection beside roofsection"));
        Assert.Equal(["SSF"], CaptureLightExtras.SourceNamed("passFlags SSFColor"));
        Assert.Empty(CaptureLightExtras.SourceNamed("void ProcessFrame(int passFlags)"));
    }

    [Fact]
    public void APICTUREIsShrunkByWholeStepsEachTexelItsBlocksMean()
    {
        var rgba = new byte[6 * 2 * 4];
        for (var at = 0; at < 6 * 2; at++)
        {
            rgba[at * 4] = (byte)(at * 10);
            rgba[(at * 4) + 3] = 255;
        }

        GamePicture shrunk = CaptureLightExtras.Shrunk(new GamePicture(6, 2, rgba), 3);

        // STEPS OF TWO: three by one, each the mean of a two-by-two block - (0 + 10 + 60 + 70) / 4 first.
        Assert.Equal((3, 1), (shrunk.Width, shrunk.Height));
        Assert.Equal([35, 55, 75], new[] { shrunk.Rgba[0], shrunk.Rgba[4], shrunk.Rgba[8] }.Select(one => (int)one));
        Assert.Same(rgba, CaptureLightExtras.Shrunk(new GamePicture(6, 2, rgba), 6).Rgba);
    }

    [Fact]
    public void THEPARTIsInTheCatalogueUnderTheAreasFilesOnByDefault()
    {
        CapturePart part = CaptureParts.All.Single(one => one.Key == CaptureParts.LightExtras);

        Assert.Equal("Area files", part.Group);
        Assert.Equal("the light beyond the sun", part.Label);
        Assert.Equal(CaptureReport.LightExtrasFile, part.File);
        Assert.False(part.OffByDefault);
        Assert.Equal("area", CaptureParts.FolderOf(CaptureReport.LightExtrasFile));
    }

    /// <summary>The part over the whole made-up install, line endings made one.</summary>
    private static string Written(out IReadOnlyList<(string File, GamePicture Picture)> pictures)
    {
        string program = Program();
        try
        {
            return CaptureLightExtras.Write(
                    Read, [RedVale, Port, Dry, Broken, "missing.env"], [Common, Effects, Lighting, Legacy, Busy, Other],
                    [FogGraph, SsfGraph, CloudGraph, ProofGraph, PlainGraph, FogMaterial, FogGraph], [program], [Pane()], Port, out pictures)
                .ReplaceLineEndings("\n");
        }
        finally
        {
            File.Delete(program);
        }
    }

    /// <summary>A model pane whose model wears two roof sections, a ground, a material saying it is none, and one not in the install.</summary>
    private static CapturePane Pane()
    {
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f)];
        SkinnedMesh mesh = SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 0, 2, 1, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2], Vector3.Zero, Vector3.One, null,
            [new MeshShape("0", 0, 6), new MeshShape("1", 6, 3), new MeshShape("2", 9, 3), new MeshShape("3", 12, 3), new MeshShape("4", 15, 3), new MeshShape("5", 18, 3)]);
        var model = new MonsterModel(mesh, null, "room.arm", string.Empty, string.Empty)
        {
            ShapeMaterials = [Branches + ":0", Branches, Forest, Dirt, NotRoof, Gone],
            ShapeSources =
            [
                "Metadata/Terrain/Doodads/Trees/JungleTree01.ao", "Metadata/Terrain/Doodads/Trees/JungleTree02.ao", "Metadata/Terrain/Doodads/Trees/BlackForest01.ao",
                "ground", "Metadata/Terrain/Doodads/Rock.ao", "Metadata/Terrain/Doodads/Gone.ao",
            ],
        };

        return new CapturePane("monster", [], model);
    }

    /// <summary>A program's bytes on disk: a cloud texture's path, the fog and roof words, a player light in two bytes a character - and ssf and roof inside other words.</summary>
    private static string Program()
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x4D, 0x5A, 0x90, 0x00, 0x03]);
        foreach (string text in (string[])[CloudNoise, "screenspace_fog_enabled", "screenspace_fog_enabled", "ProcessFrame", "waterproof", "RoofSection", "unrelated", "SSF_LAYERS"])
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(text));
            bytes.AddRange([0x00, 0x01]);
        }

        bytes.AddRange([0x00, 0xFF, 0x01]);
        bytes.AddRange(Encoding.Unicode.GetBytes("PlayerLightIntensity"));
        bytes.AddRange([0x00, 0x00, 0x07]);

        string path = Path.Combine(Path.GetTempPath(), $"poef-light-program-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [.. bytes]);
        return path;
    }

    private static byte[]? Read(string path) => path switch
    {
        RedVale => Encoding.UTF8.GetBytes(
            "{\"directional_light\": {\"phi\": 2.9}, \"player_light\": {\"colour\": [0.9, 0.8, 0.7], \"intensity\": 1.5}, "
            + "\"clouds\": {\"clouds_scale\": 0.8, \"clouds_intensity\": 0.9, \"clouds_texture\": \"" + Perlin + "\"}, "
            + "\"screenspace_fog\": {\"ssf_color\": [0.139, 0.109, 0.119], \"ssf_layer_count\": 5.0}, "
            + "\"area\": {\"dust_color\": [0.3, 0.3, 0.2], \"clouds_extra\": 2.0}, \"water\": {\"height_offset\": 3}}"),
        Port => CaptureGroundTests.Text("{\"clouds\": {\"clouds_scale\": 0.6}, \"water\": {\"flow_intensity\": 1.5},}"),
        Dry => Encoding.UTF8.GetBytes("{\"area\": {\"wind_intensity\": 60.0, \"waterproof\": 1}, \"camera\": {\"roof_fade_distance\": 300}}"),
        Broken => Encoding.UTF8.GetBytes("{\"clouds\": "),

        Common => Encoding.UTF8.GetBytes(
            "uniform pass float clouds_scale\nuniform pass float clouds_fade_radius\n// ===== the fog =====\nuniform pass float4 ssf_color\n"
            + "uniform object float roof_fade\nuniform pass texture screenspace_shadowmap\n"),
        Effects => Encoding.UTF8.GetBytes("FRAGMENT SSFColor\n{{\n\tcolor = ssf_color;\n}}\nFRAGMENT CloudWindDirection\n{{\n\tdirection = clouds_velocity.xy;\n}}\n"),
        Lighting => CaptureGroundTests.Text(
            "DECLARATIONS screen_space_shadows\n{{\n}}\nfloat4 PlayerLightColour() { return player_light_colour; }\n// texture: " + FogNoise + "\n"),
        Legacy => Encoding.UTF8.GetBytes("FRAGMENT SeeThroughFade\n{{\n\tfloat3 camera_dir = normalize(player_position.xyz - GetCameraPosition());\n}}\n"),
        Busy => Encoding.UTF8.GetBytes("void ProcessFrame(int passFlags) { }\n"),
        Other => Encoding.UTF8.GetBytes("float4 Unrelated() { return 0; }\n"),

        FogGraph => CaptureGroundTests.Text(
            "{\"nodes\": [{\"type\": \"ScreenspaceFog\"}, {\"type\": \"SampleTexture\", \"parameters\": [{\"path\": \"" + Cell + "\"}]}]}"),
        SsfGraph => Encoding.UTF8.GetBytes("{\"nodes\": [{\"type\": \"SSFColor\"}, {\"type\": \"SSFLayerCountThicknessTurbulence\"}]}"),
        CloudGraph => Encoding.UTF8.GetBytes(
            "{\"nodes\": [{\"type\": \"SampleTexture\", \"parameters\": [{\"path\": \"" + CloudMask + "\"}, {\"path\": \"" + PlainMask + "\"}]}]}"),
        ProofGraph => Encoding.UTF8.GetBytes("{\"nodes\": [{\"type\": \"InputUV\"}]}"),
        PlainGraph => Encoding.UTF8.GetBytes("{\"nodes\": [{\"type\": \"SampleTexture\", \"parameters\": [{\"path\": \"Art/Models/RoofFade_Branches_colour.dds\"}]}]}"),
        FogMaterial => Encoding.UTF8.GetBytes("{\"version\": 4}"),

        Perlin => GameArtTests.Dds(64, 32, Texels(64 * 32, alpha: null)),
        Cell => GameArtTests.Dds(600, 20, Texels(600 * 20, alpha: null)),
        FogNoise => Encoding.ASCII.GetBytes("not a picture, though its name says one"),
        CloudMask => GameArtTests.Dds(16, 16, Texels(16 * 16, alpha: 255)),
        PlainMask => GameArtTests.Dds(16, 16, Texels(16 * 16, alpha: 255)),

        Branches => Encoding.UTF8.GetBytes(
            "{\"version\":4,\"graphinstances\":[{\"parent\":\"Metadata/Materials/DielectricSpecGlossBN.fxgraph\"},{\"parent\":\"Metadata/Effects/Graphs/General/ForceAlphaTest.fxgraph\"}],\"roofsection\":true}"),
        Forest => CaptureGroundTests.Text("{\"version\": 4, \"roofsection\": true}"),
        Dirt => Encoding.UTF8.GetBytes("{\"version\":4,\"graphinstances\":[]}"),
        NotRoof => Encoding.UTF8.GetBytes("{\"version\":4,\"roofsection\":false}"),
        _ => null,
    };

    /// <summary>Texels in the blue-first order a DDS stores, the alpha one value where given.</summary>
    private static byte[] Texels(int count, byte? alpha)
    {
        var bgra = new byte[count * 4];
        for (var at = 0; at < bgra.Length; at++)
        {
            bgra[at] = alpha is { } one && at % 4 == 3 ? one : (byte)(at * 7);
        }

        return bgra;
    }
}
