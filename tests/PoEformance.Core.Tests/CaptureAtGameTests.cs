using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The two parts that hold a stage against the game's picture (CaptureAtGame, CaptureLightCompare, CaptureShadowCasters), end to end over a
/// made stage and a made game picture whose difference from the pane is known.
/// </summary>
public class CaptureAtGameTests
{
    private const int Width = 512, Height = 256;

    /// <summary>
    /// A game picture half the pane's light everywhere comes out a ratio of a half in the sun and in the shade, the neutral fit's s a half,
    /// lined up at no shift - and the replay of the kept numbers says the same.
    /// </summary>
    [Fact]
    public void AGAMEPictureHalfThePanesLightIsMeasuredHalfAndReplayedSo()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out string why)!;
        Assert.True(why.Length == 0, why);
        Assert.Equal(1, own.Factor);
        Assert.Contains(own.Picture.Where((_, at) => at % 4 == 3), alpha => alpha == 255);

        byte[] game = Game(own, 0.5f);
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, game, Width, Height, null, out _)!;
        string report = CaptureLightCompare.Write(null, scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);

        string head = report[..report.IndexOf('\n')];
        Assert.Matches(@"median 0\.(4[89]|5[01])\d*; in the pane's sun 0\.(4[89]|5[01])\d* \(", head);
        Assert.Contains("lined up: the edges' correlation at no shift", report, StringComparison.Ordinal);
        Assert.Contains("best", report, StringComparison.Ordinal);
        Assert.Matches(@"all\s+s 0\.(4[89]|5[01])", report);
        Assert.Equal(4, pictures.Count);

        // VERSION 3 KEEPS EVERY SAMPLE'S PLACE and the stage's corner in the area, what the clouds' noise is measured over offline.
        (CaptureLightCompare.Header kept, List<CaptureLightCompare.Sample> samples) = CaptureLightCompare.Read(numbers);
        Assert.Equal(scene.Origin, kept.Origin);
        Assert.NotEmpty(samples);
        Assert.All(samples, one => Assert.True(float.IsFinite(one.Place.X) && float.IsFinite(one.Place.Z)));
        Assert.Contains("no measurement: no .env read", report, StringComparison.Ordinal);

        string folder = Directory.CreateTempSubdirectory("light-against-game").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            string replay = CaptureLightCompare.Replay(folder);
            Assert.StartsWith("light against the game replay: ", replay, StringComparison.Ordinal);
            Assert.Equal(head[head.IndexOf(':', StringComparison.Ordinal)..], replay[..replay.IndexOf('\n')][replay.IndexOf(':', StringComparison.Ordinal)..]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A shadow-only slab whose texture is clear casts a shadow a cut takes away; the game's picture, the pane's own, keeps it - so t comes out
    /// near nought, the game casting as the picture does - and the ground's map by kind is read back by the replay.
    /// </summary>
    [Fact]
    public void ACLEARShadowOnlySlabsShadowIsFoundLostWhenCutAndJudgedAgainstTheGame()
    {
        CapturePane pane = Stage(MaterialBlend.ShadowOnly);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Game(own, 1f), Width, Height, null, out _)!;

        string report = CaptureShadowCasters.Write(null, pane, scene, null, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);
        Assert.StartsWith("shadow casters: ", report, StringComparison.Ordinal);
        Assert.Matches(@"[1-9][\d,]* of them are shadow-only shapes' that a cut", report);
        Assert.Matches(@"t over the [\d,]+ judged -?0(\.0\d*)?\s", report);
        Assert.Matches(@"Art/canopy\.mat\s+1\s+1\s", report);
        Assert.Equal(2, pictures.Count);

        string folder = Directory.CreateTempSubdirectory("shadow-casters").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureShadowCasters.Numbers), numbers);
            string replay = CaptureShadowCasters.Replay(folder);
            Assert.StartsWith("shadow casters replay: 2048 texels across", replay, StringComparison.Ordinal);
            Assert.Matches(@"a shadow-only shape's a cut takes away: [1-9]", replay);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A screen taller than wide is drawn at the fraction its height asks - by its width alone a 200 x 2100 screen would want 2100 rows of a
    /// canvas 2048 square.
    /// </summary>
    [Fact]
    public void ATALLScreenIsDrawnAtTheFractionItsHeightAsks()
    {
        const int width = 200, height = 2100;
        CaptureAtGame scene = CaptureAtGame.Draw(Stage(MaterialBlend.Opaque), Shot(width, height), null, width, height, null, out string why)!;
        Assert.True(why.Length == 0, why);
        Assert.Equal(2, scene.Factor);
        Assert.Equal((100, 1050, 1050), (scene.DrawnWidth, scene.DrawnHeight, scene.Side));
        Assert.Equal(100 * 1050 * 4, scene.Picture.Length);
    }

    /// <summary>
    /// A stage that does not show through the game's camera is not said to line up with it - no shift had pixels enough to measure - and a
    /// light with no sun is said to reach none of the compared pixels rather than to light every face towards its way.
    /// </summary>
    [Fact]
    public void ASTAGEOutOfViewIsNotSaidToLineUpAndALightWithNoSunReachesNothing()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque);
        WorldSnapshot away = Shot(target: new Vector3(40000f, 9000f, 0f));
        CaptureAtGame empty = CaptureAtGame.Draw(pane, away, Game(Blank(), 1f), Width, Height, null, out _)!;
        Assert.DoesNotContain(empty.Picture.Where((_, at) => at % 4 == 3), alpha => alpha != 0);
        string report = CaptureLightCompare.Write(null, empty, out _, out _);
        Assert.Contains("lined up: not measured", report, StringComparison.Ordinal);
        Assert.DoesNotContain("placing agree", report, StringComparison.Ordinal);

        CapturePane sunless = Stage(MaterialBlend.Opaque, sunless: true);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(sunless, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(sunless, shot, Game(own, 1f), Width, Height, null, out _)!;
        string lit = CaptureLightCompare.Write(null, scene, out _, out _);
        Assert.Matches(@"the pane's sun reaches none of them, in its shade [\d.]+ \([1-9]", lit[..lit.IndexOf('\n')]);
        Assert.Contains("the pane's sun reaches none of the compared pixels", lit, StringComparison.Ordinal);
    }

    /// <summary>
    /// Under a clear shadow-only card a block's shadow stays when the card is cut: it is counted kept against the card, whose texels less its
    /// kept are the shadow a cut takes away; the floor of two shapes of one material is judged as one; and a game picture as bright lit as
    /// shaded judges nothing rather than t nought.
    /// </summary>
    [Fact]
    public void ACUTCardsKeptShadowIsItsOwnAndAGroundOfOneMaterialIsJudgedWhole()
    {
        CapturePane pane = Stage(MaterialBlend.ShadowOnly, splitFloor: true, blockUnder: true);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Game(own, 1f), Width, Height, null, out _)!;

        string report = CaptureShadowCasters.Write(null, pane, scene, null, out _, out _);
        int lost = Count(Regex.Match(report, @"in shadow; ([\d,]+) of them are shadow-only shapes'").Groups[1].Value);
        Match card = Regex.Match(report, @"ShadowOnly\s+\S+\s+([\d,]+)\s+([\d,]+)\s+1\s+2\s+\S+\s+Art/canopy\.mat");
        Assert.True(card.Success, report);
        int texels = Count(card.Groups[1].Value), kept = Count(card.Groups[2].Value);
        Assert.True(kept > 0, $"the block's shadow under the card: {kept} kept of {texels}");
        Assert.Equal(lost, texels - kept);
        Assert.Matches(@"ground\s+lit [\d,\s]+at .+\(2 shapes\)", report);

        CaptureAtGame flat = CaptureAtGame.Draw(pane, shot, Grey(), Width, Height, null, out _)!;
        string even = CaptureShadowCasters.Write(null, pane, flat, null, out _, out _);
        Assert.Matches(@"t over the 0 judged -; [1-9][\d,]* more in groups no brighter lit than shaded, not judged", even);
    }

    /// <summary>
    /// A game picture that is the pane's own under a sun bright enough to clip it compares as the same picture: the blocks at its ceiling are
    /// left out rather than read as a game darker and greyer than the pane - and a version 1 file, which kept no ceiling, still replays.
    /// </summary>
    [Fact]
    public void THEGAMESPictureAtItsCeilingIsLeftOutNotTakenAsItsLight()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque, sun: 9f);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Game(own, 1f), Width, Height, null, out _)!;
        Assert.Contains(scene.Clipped, one => one);

        string report = CaptureLightCompare.Write(null, scene, out _, out byte[] numbers);
        string head = report[..report.IndexOf('\n')];
        // WITHIN THE BYTES' ROUNDING of the dark shade left over - taken as exact, the clipped sun would put the median near 0.4.
        Assert.True(Regex.IsMatch(head, @"median (0\.9[5-9]\d*|1(\.0[0-5]\d*)?);"), report);
        Assert.Matches(@"[1-9][\d,]* the game's picture at its ceiling", report);
        Assert.True(Regex.IsMatch(report, @"all\s+s (0\.9[5-9]\d*|1(\.0[0-5]\d*)?)\s"), report);

        // VERSION 1: the same numbers less the ninth reason - read with nought there. Made from version 2's, which version 3 is written as on asking.
        (CaptureLightCompare.Header header, List<CaptureLightCompare.Sample> samples) = CaptureLightCompare.Read(numbers);
        byte[] second = CaptureLightCompare.Bytes(header, samples, version: 2);
        byte[] first = [.. second[..4], .. BitConverter.GetBytes(1), .. second[8..60], .. second[64..]];
        string folder = Directory.CreateTempSubdirectory("light-against-game-v1").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), first);
            string replay = CaptureLightCompare.Replay(folder);
            Assert.Contains(", 0 the game's picture at its ceiling", replay, StringComparison.Ordinal);
            Assert.Equal(head[head.IndexOf(':', StringComparison.Ordinal)..], replay[..replay.IndexOf('\n')][replay.IndexOf(':', StringComparison.Ordinal)..]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// With no stage laid in the instance the shadow part takes a stage with ground to cast on, not a lone monster under a light - and a stage
    /// with no ground says so rather than measuring nothing as nought.
    /// </summary>
    [Fact]
    public void THESHADOWPartFallsBackToAStageWithGroundNotALoneMonster()
    {
        CapturePane stage = Stage(MaterialBlend.Opaque);
        CapturePane monster = Monster();
        Assert.Same(stage, CaptureShadowCasters.Fallback([monster, stage], out string why));
        Assert.Empty(why);
        Assert.Null(CaptureShadowCasters.Fallback([monster], out why));
        Assert.Contains("ground", why, StringComparison.Ordinal);
        Assert.StartsWith(
            "not written: the monster book's stage has no ground", CaptureShadowCasters.Write(null, monster, null, null, out _, out _), StringComparison.Ordinal);
        Assert.Contains(
            "the stage: the tile book's", CaptureShadowCasters.Write(null, stage, null, null, out _, out _), StringComparison.Ordinal);
    }

    /// <summary>
    /// A shape whose .mat says "roofsection" - the game hides it round the player and shows what stands behind - is left out of every
    /// statistic: the pixels compared and the roof's add up to those compared without the install's word, the head says how many it left
    /// out, the game equal to the pane everywhere else reads as equal, and its own section sets it out by the distance to the player, the
    /// ratio picture marks it - and the replay keeps it out from the numbers alone.
    /// </summary>
    [Fact]
    public void AROOFSectionIsLeftOutOfEveryStatisticAndSetOutByTheDistanceToThePlayer()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque);
        WorldSnapshot shot = Shot(player: new Vector3(5000f, 9000f, 0f));
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        int rock = pane.Model.ShapeMaterials.ToList().IndexOf("Art/rock.mat");
        Assert.True(rock >= 0);

        // THE GAME: the pane's own picture, the rock a quarter as bright - what the game shows behind a roof it hides.
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Exact(own, at => ShapeAt(own, at) == rock ? 0.25f : 1f), Width, Height, null, out _)!;
        string plain = CaptureLightCompare.Write(null, scene, out _, out _);
        string report = CaptureLightCompare.Write(RoofRock, scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);

        string head = report[..report.IndexOf('\n')];
        int compared = Count(Regex.Match(head, @": ([\d,]+) pixels compared").Groups[1].Value);
        int roofs = Count(Regex.Match(report, @"([\d,]+) roof sections the game hides round the player").Groups[1].Value);
        Assert.True(roofs > 50, report);
        Assert.Equal(Count(Regex.Match(plain, @": ([\d,]+) pixels compared").Groups[1].Value), compared + roofs);
        Assert.Contains(", 0 roof sections the game hides round the player", plain, StringComparison.Ordinal);
        Assert.True(Regex.IsMatch(head, @"median (0\.9[5-9]\d*|1(\.0[0-5]\d*)?);"), report);
        Assert.Matches(@"roof sections: 1 of the \d+ materials the compared pixels wear say ""roofsection""", report);
        Assert.Matches(@"\n  roof: rock: [\d,]+ pixels, ratio 0\.2[3-7]", report);
        Assert.Matches(@"\n\s+250-500\s+units:\s+[\d,]+ pixels, ratio 0\.2[3-7]", report);

        GamePicture ratio = pictures.Single(one => one.File == CaptureLightCompare.RatioPicture).Picture;
        Assert.Contains(Enumerable.Range(0, ratio.Width * ratio.Height), at => ratio.Rgba[at * 4] == 255 && ratio.Rgba[(at * 4) + 1] == 0 && ratio.Rgba[(at * 4) + 2] == 255);

        string folder = Directory.CreateTempSubdirectory("light-against-game-roof").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            string replay = CaptureLightCompare.Replay(folder);
            Assert.Equal(compared, Count(Regex.Match(replay, @": ([\d,]+) pixels compared").Groups[1].Value));
            Assert.Equal(roofs, Count(Regex.Match(replay, @"([\d,]+) roof sections the game hides round the player").Groups[1].Value));
            Assert.Matches(@"\n  roof: rock: [\d,]+ pixels, ratio 0\.2[3-7]", replay);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A game picture darker than the pane's on the lit ground alone - its sun weaker, its shade the pane's - comes out block by block: the
    /// lit blocks' game over pane the darkening, the shaded blocks' one, the game's contrast between them the pane's times the darkening -
    /// and the blocks' picture shows the lit ones darker; the replay sets them out alike.
    /// </summary>
    [Fact]
    public void THEGROUNDBlockByBlockSaysHowMuchTheGameDarkensTheSunOverTheGroundThePaneLights()
    {
        CapturePane pane = Stage(MaterialBlend.ShadowOnly, floorHalf: 1500f, slabHalf: 450f, slabHeight: 300f);
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Exact(own, at => ShapeAt(own, at) == 0 && Lit(own, at) ? 0.3f : 1f), Width, Height, null, out _)!;
        string report = CaptureLightCompare.Write(null, scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);

        static void Rows(string text)
        {
            Match lit = Regex.Match(text, @"\n  0\.7-1\s+([\d,]+)\s+([\d.]+)\s+([\d.]+)\.\.([\d.]+)\s+([\d.]+)\s+([\d.]+)\r?\n");
            Match shaded = Regex.Match(text, @"\n  0-0\.1\s+([\d,]+)\s+([\d.]+)\s+([\d.]+)\.\.([\d.]+)\s+([\d.]+)\s+([\d.]+)\r?\n");
            Assert.True(lit.Success && shaded.Success, text);
            Assert.True(Count(lit.Groups[1].Value) > 0 && Count(shaded.Groups[1].Value) > 0, text);
            Assert.InRange(Number(lit.Groups[6].Value), 0.27, 0.33);
            Assert.InRange(Number(shaded.Groups[6].Value), 0.95, 1.05);
            Match contrast = Regex.Match(
                text, @"over its shaded ones' \(under 0\.1, [\d,]+\) ([\d.]+) to 1, the game's over the same blocks ([\d.]+) to 1 - the game keeps ([\d.]+) of the pane's");
            Assert.True(contrast.Success, text);
            Assert.True(Number(contrast.Groups[1].Value) > 10d, text);
            Assert.InRange(Number(contrast.Groups[3].Value), 0.27, 0.33);
        }

        Rows(report);
        Assert.Matches(@"\n  column  row  pixels  sun share  game light  pane light  game over pane\r?\n\s+\d+\s+\d+\s+[\d,]+\s", report);

        GamePicture blocks = pictures.Single(one => one.File == CaptureLightCompare.BlocksPicture).Picture;
        Assert.Contains(Enumerable.Range(0, blocks.Width * blocks.Height), at => blocks.Rgba[(at * 4) + 2] == 255 && blocks.Rgba[at * 4] < 80);

        string folder = Directory.CreateTempSubdirectory("light-against-game-blocks").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            Rows(CaptureLightCompare.Replay(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A game whose light in the pane's shade is the rest of the pane's 1.2 times and its lamp 2.8 times - over pixels the lamp gives from a
    /// twentieth to past four fifths of their light - comes out of the lamp's fit as both, pooled and in every bin where the lamp gives a
    /// twentieth or more, and the replay alike; while each pixel's own ratio, 2.8 + 0.2 rest / lamp, asks the more the thinner the lamp's
    /// share, so its median - the reading before the fit - lies off 2.8. The lamp's share falls with the distance.
    /// </summary>
    /// <remarks>
    /// NOT AT THE LAMP'S EDGE, where it gives a few thousandths of the light: there the game's picture rounded to its bytes says nothing of
    /// the lamp, whatever reads it (2.15 asked of 508 pixels at a median share of 0.002).
    /// </remarks>
    [Fact]
    public void THEPLAYERSLampsGainIsFittedApartFromTheRestOfTheLightInThePanesShade()
    {
        var at = new Vector3(0f, 0f, -100f);
        var lamp = new SceneLight.PlayerLamp(at, new Vector3(0.6f), 300f);
        CapturePane pane = Stage(MaterialBlend.ShadowOnly, floorHalf: 1500f, slabHalf: 450f, slabHeight: 300f, lamp: lamp);
        WorldSnapshot shot = Shot(player: new Vector3(5000f, 9000f, 0f));
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Exact(own, gathered => Asked(own, gathered, 1.2, 2.8)), Width, Height, null, out _)!;
        string report = CaptureLightCompare.Write(null, scene, out _, out byte[] numbers);

        static void Pooled(string text)
        {
            Match all = Regex.Match(text, @"all within 1,200 units: the lamp's gain g ([\d.]+), the rest's a ([\d.]+) - over ([\d,]+) pixels");
            Assert.True(all.Success, text);
            Assert.InRange(Number(all.Groups[1].Value), 2.72, 2.88);
            Assert.InRange(Number(all.Groups[2].Value), 1.165, 1.235);
        }

        Pooled(report);
        MatchCollection rows = Regex.Matches(report, @"\n\s+\d+-\d+\s+units:\s+[\d,]+ pixels in shade, the lamp's share ([\d.]+); g ([\d.]+), a ([\d.]+) over ([\d,]+) the lamp reaches");
        List<(double Share, double Gain, double Others, int Pixels)> bins =
            [.. rows.Select(row => (Number(row.Groups[1].Value), Number(row.Groups[2].Value), Number(row.Groups[3].Value), Count(row.Groups[4].Value)))];
        Assert.True(bins.Count(row => row.Share > 0.3) >= 2, report);
        Assert.True(bins.Zip(bins.Skip(1)).All(pair => pair.Second.Share < pair.First.Share), report);
        List<(double Share, double Gain, double Others, int Pixels)> lit = [.. bins.Where(row => row.Pixels >= 200 && row.Share >= 0.05)];
        Assert.True(lit.Count >= 3, report);
        Assert.All(lit, row =>
        {
            Assert.InRange(row.Gain, 2.66, 2.94);
            Assert.InRange(row.Others, 1.14, 1.26);
        });

        // EACH PIXEL'S OWN RATIO ASKS THE MORE THE THINNER ITS LAMP'S SHARE, in every band of it.
        Match banded = Regex.Match(
            report, @"its median by that share: 0\.05-0\.1 ([\d.]+) \(([\d,]+)\), 0\.1-0\.2 ([\d.]+) \(([\d,]+)\), 0\.2-0\.3 ([\d.]+) \(([\d,]+)\), 0\.3-0\.5 ([\d.]+) \(([\d,]+)\), 0\.5 and more ([\d.]+) \(([\d,]+)\)");
        Assert.True(banded.Success, report);
        Assert.All(Enumerable.Range(0, 5), band => Assert.True(Count(banded.Groups[2 + (2 * band)].Value) > 0, report));
        double[] medians = [.. Enumerable.Range(0, 5).Select(band => Number(banded.Groups[1 + (2 * band)].Value))];
        Assert.True(medians.Zip(medians.Skip(1)).All(pair => pair.Second < pair.First), report);
        Assert.True(medians[0] > 3.5, report);

        // THE SHARES SPREAD FROM UNDER A TWENTIETH TO PAST FOUR FIFTHS; and the reading before the fit, the median of each pixel's ratio
        // where the lamp gives a twentieth or more of the light, asks too much of the lamp.
        (_, List<CaptureLightCompare.Sample> samples) = CaptureLightCompare.Read(numbers);
        List<double> shares = [], ratios = [];
        foreach (CaptureLightCompare.Sample one in samples)
        {
            double light = CaptureAtGame.Luminance(one.Unexposed), its = CaptureAtGame.Luminance(one.Player);
            if (one.Class != CaptureLightCompare.Lighting.Shade || !(one.PlayerDistance < 1200f) || !(light > 1e-6) || !(its > 0d))
            {
                continue;
            }

            shares.Add(its / light);
            if (its / light > 0.05)
            {
                ratios.Add((CaptureAtGame.Luminance(one.GameUnexposed) - (light - its)) / its);
            }
        }

        Assert.True(shares.Min() < 0.05 && shares.Max() > 0.8, $"the lamp's shares {shares.Min():0.###} to {shares.Max():0.###}");
        ratios.Sort();
        Assert.True(ratios[ratios.Count / 2] > 2.95, $"the median of each pixel's ratio {ratios[ratios.Count / 2]:0.###}");

        string folder = Directory.CreateTempSubdirectory("light-against-game-lamp").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            Pooled(CaptureLightCompare.Replay(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The lamp's fit takes a and g apart where the pixels hold both; where the normal equations put one below nought it is nought and the
    /// other is fitted alone; and where every pixel's lamp gives the same share of its light - one pixel alone - it gives no fit.
    /// </summary>
    [Fact]
    public void THELAMPSFitKeepsBothGainsNoughtAtLeastAndGivesNoneItCannotTellApart()
    {
        (double Others, double Lamp)[] pixels = [(1, 0.5), (1, 1), (0.5, 2), (2, 0.4)];
        var both = new CaptureLightCompare.LampFit();
        foreach ((double rest, double lamp) in pixels)
        {
            both.Add(rest, lamp, (1.2 * rest) + (2.8 * lamp));
        }

        (double Others, double Lamp) fitted = both.Solve()!.Value;
        Assert.Equal(1.2, fitted.Others, 9);
        Assert.Equal(2.8, fitted.Lamp, 9);

        // A GAME ASKING LESS THAN NOTHING OF THE REST: the rest nought, the lamp fitted alone.
        var under = new CaptureLightCompare.LampFit();
        foreach ((double rest, double lamp) in pixels)
        {
            under.Add(rest, lamp, (-0.2 * rest) + (3 * lamp));
        }

        double alone = pixels.Sum(one => one.Lamp * ((-0.2 * one.Others) + (3 * one.Lamp))) / pixels.Sum(one => one.Lamp * one.Lamp);
        (double Others, double Lamp) edge = under.Solve()!.Value;
        Assert.Equal(0d, edge.Others);
        Assert.Equal(alone, edge.Lamp, 9);

        // ONE PIXEL ALONE, EVERY PIXEL AT ONE SHARE OF ITS LIGHT, AND NONE: no fit rather than a nought.
        var same = new CaptureLightCompare.LampFit();
        same.Add(1, 0.5, 2.6);
        Assert.Null(same.Solve());
        same.Add(2, 1, 5.2);
        Assert.Null(same.Solve());
        Assert.Null(new CaptureLightCompare.LampFit().Solve());
    }

    /// <summary>
    /// A ground of two grounds mixed wears a name that is no file: asked whether it is a roof section, its two grounds are read and counted
    /// as the materials they are, the name itself neither read nor counted as unread - a ground not in the install still is - and the mix is
    /// a roof section where one of its grounds says so.
    /// </summary>
    [Fact]
    public void AGROUNDMixIsAskedWhetherItIsARoofSectionAsItsTwoGrounds()
    {
        var asked = new List<string>();
        byte[]? Read(string path)
        {
            asked.Add(path);
            return path switch
            {
                "Art/ground.mat" or "Art/sand.mat" => Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[]}"""),
                "Art/rock.mat" => RoofRock(path),
                _ => null,
            };
        }

        var roofs = new CaptureLightCompare.RoofSections(Read);
        Assert.False(roofs.Of(GroundBlend.Cornered("Art/ground.mat", "Art/sand.mat")));
        Assert.False(roofs.Of(GroundBlend.Name("Art/ground.mat", "Art/sand.mat", "Art/Textures/newmask.dds", heights: true)));
        Assert.Equal(["Art/ground.mat", "Art/sand.mat"], asked);
        Assert.StartsWith("roof sections: 0 of the 2 materials the compared pixels wear say \"roofsection\" in their .mat - named", roofs.Said(), StringComparison.Ordinal);

        Assert.True(roofs.Of(GroundBlend.Cornered("Art/rock.mat", "Art/gone.mat")));
        Assert.StartsWith("roof sections: 1 of the 4 materials the compared pixels wear say \"roofsection\" in their .mat (1 not in the install or not read) - named", roofs.Said(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A floor of two shapes, one wearing sand alone and one sand mixed with the ground by its sub-tiles' corners, is set out by material as
    /// the two it is - the mix under both its grounds' names, not under the second's, whose own group it would otherwise join - and so in
    /// the numbers the replay reads.
    /// </summary>
    [Fact]
    public void AGROUNDMixIsSetOutByMaterialUnderBothItsGroundsNotTheSecondsName()
    {
        CapturePane stage = Stage(MaterialBlend.Opaque, splitFloor: true);
        Assert.Equal(["Art/ground.mat", "Art/ground.mat"], stage.Model.ShapeMaterials.Take(2));
        MonsterModel model = stage.Model with
        {
            ShapeMaterials = [GroundBlend.Cornered("Art/ground.mat", "Art/sand.mat"), "Art/sand.mat", .. stage.Model.ShapeMaterials.Skip(2)],
        };
        var pane = new CapturePane("tile", [], model) { Light = stage.Light, LightOn = true, Blends = stage.Blends };
        WorldSnapshot shot = Shot();
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Exact(own, _ => 1f), Width, Height, null, out _)!;
        string report = CaptureLightCompare.Write(null, scene, out _, out byte[] numbers);

        Assert.Matches(@"\n  ground: ground \+ sand\s+[\d,]+\s", report);
        Assert.Matches(@"\n  ground: sand\s+[\d,]+\s", report);
        string folder = Directory.CreateTempSubdirectory("light-against-game-mix").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, CaptureLightCompare.Numbers), numbers);
            Assert.Matches(@"\n  ground: ground \+ sand\s+[\d,]+\s", CaptureLightCompare.Replay(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static int Count(string number) => int.Parse(number, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>The stage's shapes as a lone monster's - lit, and none of them ground.</summary>
    private static CapturePane Monster()
    {
        CapturePane stage = Stage(MaterialBlend.Opaque);
        var model = new MonsterModel(stage.Model.Mesh, null, "monster", string.Empty, string.Empty)
        {
            ShapeMaterials = stage.Model.ShapeMaterials,
            ShapeSources = ["body.ao", "body.ao", "body.ao"],
            Modes = stage.Model.Modes,
            Skins = stage.Model.Skins,
        };
        return new CapturePane("monster", [], model) { Light = stage.Light, LightOn = true, Blends = stage.Blends };
    }

    /// <summary>
    /// A floor laid in the area under a slab of the blend asked and a solid rock beside it, lit by a sun from the side - with no sun, the
    /// floor in two shapes of one material, a block under the slab, a wider floor or slab, a player's lamp or room lights, where asked.
    /// </summary>
    private static CapturePane Stage(
        MaterialBlend slab, bool sunless = false, bool splitFloor = false, bool blockUnder = false, float sun = 2f, float floorHalf = 600f,
        float slabHalf = 120f, float slabHeight = 150f, SceneLight.PlayerLamp? lamp = null, IReadOnlyList<PointLight>? lights = null,
        Vector3? sunWay = null, bool patterned = false, string rockSource = "rock.ao", bool rockHoles = false)
    {
        var places = new List<Vector3>
        {
            new(-floorHalf, -floorHalf, 0f), new(floorHalf, -floorHalf, 0f), new(floorHalf, floorHalf, 0f), new(-floorHalf, floorHalf, 0f),
            new(-slabHalf, -slabHalf, -slabHeight), new(slabHalf, -slabHalf, -slabHeight), new(slabHalf, slabHalf, -slabHeight), new(-slabHalf, slabHalf, -slabHeight),
            new(-420f, -420f, -150f), new(-260f, -420f, -150f), new(-260f, -260f, -150f), new(-420f, -260f, -150f),
        };
        var indices = new List<int> { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11 };
        List<MeshShape> shapes = splitFloor
            ? [new MeshShape("floor", 0, 3), new MeshShape("floor", 3, 3), new MeshShape("slab", 6, 6), new MeshShape("rock", 12, 6)]
            : [new MeshShape("floor", 0, 6), new MeshShape("slab", 6, 6), new MeshShape("rock", 12, 6)];
        if (blockUnder)
        {
            places.AddRange([new(-60f, -60f, -60f), new(60f, -60f, -60f), new(60f, 60f, -60f), new(-60f, 60f, -60f)]);
            indices.AddRange([12, 13, 14, 12, 14, 15]);
            shapes.Add(new MeshShape("block", 18, 6));
        }

        Vector3[] normals = [.. places.Select(_ => -Vector3.UnitZ)];
        Vector2[] spots = [.. places.Select(place => new Vector2((place.X / (2f * floorHalf)) + 0.5f, (place.Y / (2f * floorHalf)) + 0.5f))];
        SkinnedMesh mesh = SkinnedMesh.Of(
            [.. places], normals, [.. indices], new Vector3(-floorHalf, -floorHalf, -MathF.Max(150f, slabHeight)), new Vector3(floorHalf, floorHalf, 0f), spots, shapes);
        var floor = new byte[16 * 16 * 4];
        for (var at = 0; at < floor.Length; at += 4)
        {
            floor[at] = 150;
            floor[at + 1] = 120;
            floor[at + 2] = 90;
            floor[at + 3] = 255;
        }

        Mipmaps ground = patterned ? Patterned(stripes: false) : Mipmaps.Of(new GamePicture(16, 16, floor))!;
        Mipmaps clear = Mipmaps.Of(new GamePicture(16, 16, new byte[16 * 16 * 4]))!;
        Mipmaps card = slab == MaterialBlend.ShadowOnly ? clear : ground;
        string mode = slab == MaterialBlend.ShadowOnly ? "AlphaTestWithShadowOnly" : "Opaque";
        List<string> materials = ["Art/ground.mat", "Art/canopy.mat", "Art/rock.mat"];
        List<string> sources = [RoomModelsGround + "tile", "canopy.ao", rockSource];
        List<string> modes = ["Opaque", mode, "Opaque"];
        List<Mipmaps?> skins = [ground, card, patterned ? Patterned(stripes: true, holes: rockHoles) : ground];
        List<MaterialBlend> blends = [MaterialBlend.Opaque, slab, MaterialBlend.Opaque];
        if (splitFloor)
        {
            materials.Insert(0, "Art/ground.mat");
            sources.Insert(0, RoomModelsGround + "tile");
            modes.Insert(0, "Opaque");
            skins.Insert(0, ground);
            blends.Insert(0, MaterialBlend.Opaque);
        }

        if (blockUnder)
        {
            materials.Add("Art/block.mat");
            sources.Add("block.ao");
            modes.Add("Opaque");
            skins.Add(ground);
            blends.Add(MaterialBlend.Opaque);
        }

        var model = new MonsterModel(mesh, null, "stage", string.Empty, string.Empty)
        {
            AreaOrigin = new Vector2(5000f, 9000f),
            AreaStamp = 1,
            ShapeMaterials = materials,
            ShapeSources = sources,
            Modes = modes,
            Skins = skins,
        };
        var light = new SceneLight(lights ?? [], SceneLight.PointShape.Zero, lamp)
        {
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.05f,
            SunColour = sunless ? Vector3.Zero : new Vector3(sun),
            SunDirection = sunWay ?? Vector3.Normalize(new Vector3(0.2f, 0.1f, 1f)),
            SunShadows = true,
        };
        return new CapturePane("tile", [], model)
        {
            Light = light,
            LightOn = true,
            Blends = blends,
        };
    }

    /// <summary>The ground source's word, as the room builder writes it.</summary>
    private const string RoomModelsGround = "ground of ";

    /// <summary>
    /// A texture with a pattern a correlation can follow: the floor's checks of four texels, or the rock's stripes a texel wide - their dark
    /// ones clear where holes are asked for, as a cut-out's leaves are.
    /// </summary>
    private static Mipmaps Patterned(bool stripes, bool holes = false)
    {
        const int side = 64;
        var rgba = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                bool dark = stripes ? x % 2 == 0 : ((x / 4) + (y / 4)) % 2 == 0;
                (byte r, byte g, byte b) = stripes ? (dark ? ((byte)60, (byte)60, (byte)70) : ((byte)210, (byte)200, (byte)180))
                    : (dark ? ((byte)95, (byte)80, (byte)60) : ((byte)150, (byte)120, (byte)90));
                int at = ((y * side) + x) * 4;
                (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (r, g, b, holes && dark ? (byte)0 : (byte)255);
            }
        }

        return Mipmaps.Of(new GamePicture(side, side, rgba))!;
    }

    /// <summary>An area's tiles: each piece a file laid as filed from its least corner, every tile naming its place from that corner.</summary>
    private static TerrainTiles Laid(int width, int height, params (string File, int X, int Y, int Wide, int Tall)[] pieces)
    {
        var paths = new List<string>();
        var ids = new int[width * height];
        Array.Fill(ids, -1);
        var subX = new byte[width * height];
        var subY = new byte[width * height];
        var placements = new sbyte[width * height];
        Array.Fill(placements, (sbyte)-1);
        foreach ((string file, int x, int y, int wide, int tall) in pieces)
        {
            int id = paths.IndexOf(file);
            if (id < 0)
            {
                id = paths.Count;
                paths.Add(file);
            }

            for (var dy = 0; dy < tall; dy++)
            {
                for (var dx = 0; dx < wide; dx++)
                {
                    int at = ((y + dy) * width) + x + dx;
                    (ids[at], subX[at], subY[at], placements[at]) = (id, (byte)dx, (byte)dy, (sbyte)0);
                }
            }
        }

        return new TerrainTiles(paths, ids, subX, subY, placements, width, height);
    }

    /// <summary>
    /// A camera on a screen of the size asked, looking down at a point from above and to one side - the stage's corner, (5000, 9000), unless
    /// another is asked - and a player standing where asked, or none.
    /// </summary>
    private static WorldSnapshot Shot(int width = Width, int height = Height, Vector3? target = null, Vector3? player = null)
    {
        Vector3 at = target ?? new Vector3(5000f, 9000f, 0f);
        Vector3 forward = Vector3.Normalize(new Vector3(0.47f, 0.47f, 0.75f));
        Vector3 eye = at - (forward * 2200f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 down = Vector3.Cross(right, forward);
        float focal = 0.5f * height / 0.2679492f;
        Vector3 ax = (((2f * (width / 2f) / width) - 1f) * forward) + (2f * focal / width * right);
        Vector3 ay = ((1f - (2f * (height / 2f) / height)) * forward) - (2f * focal / height * down);
        const float near = 100f, far = 6000f;
        float a = far / (far - near), b = -far * near / (far - near);
        Vector3 az = a * forward;
        float[] matrix =
        [
            ax.X, ay.X, az.X, forward.X,
            ax.Y, ay.Y, az.Y, forward.Y,
            ax.Z, ay.Z, az.Z, forward.Z,
            -Vector3.Dot(ax, eye), -Vector3.Dot(ay, eye), b - Vector3.Dot(az, eye), -Vector3.Dot(forward, eye),
        ];
        WorldEntity? you = player is { } stands ? new WorldEntity(1, 0, "Metadata/Characters/Player", EntityKind.Player, stands.X, stands.Y, stands.Z) : null;
        return new WorldSnapshot(true, you, [], matrix);
    }

    /// <summary>A game picture: the pane's own drawn picture, its light times a share, blue green red at the screen's size.</summary>
    private static byte[] Game(CaptureAtGame drawn, float share)
    {
        var bgr = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int from = (((y / drawn.Factor) * drawn.DrawnWidth) + (x / drawn.Factor)) * 4;
                int to = ((y * Width) + x) * 3;
                for (var c = 0; c < 3; c++)
                {
                    float linear = Decode(drawn.Picture[from + c]) * share;
                    bgr[to + 2 - c] = Encode(linear);
                }
            }
        }

        return bgr;
    }

    /// <summary>
    /// A game picture of a drawing at the screen's own size (a factor of one): at each gathered pixel its own light times the share asked of
    /// it, rounded once to the bytes - the drawn picture's bytes times the same share between them.
    /// </summary>
    /// <remarks>
    /// FROM THE LIGHT, NOT THE DRAWN BYTES, where it is measured: the drawn picture truncates to its bytes (MeshPicture's Byte), half a level
    /// dark at the mean - some three hundredths of the shade's few levels, which a gain read where the lamp gives a fifth of the light
    /// multiplies sixfold.
    /// </remarks>
    private static byte[] Exact(CaptureAtGame drawn, Func<int, float> share)
    {
        Assert.Equal(1, drawn.Factor);
        var bgr = new byte[Width * Height * 3];
        int step = drawn.Gather.Step, across = drawn.Gather.Width;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int gathered = ((y / step) * across) + (x / step);
                float times = share(gathered);
                int to = ((y * Width) + x) * 3;
                if (x % step == 0 && y % step == 0 && drawn.Gather.Triangle[gathered] >= 0)
                {
                    Vector3 light = drawn.Gather.Terms[gathered].Graded * times;
                    (bgr[to + 2], bgr[to + 1], bgr[to]) = (Encode(light.X), Encode(light.Y), Encode(light.Z));
                    continue;
                }

                int from = ((y * drawn.DrawnWidth) + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    bgr[to + 2 - c] = Encode(Decode(drawn.Picture[from + c]) * times);
                }
            }
        }

        return bgr;
    }

    /// <summary>
    /// The share of a gathered pixel's own light asked of a game whose light there is the rest of the pane's <paramref name="rest"/> times
    /// and its lamp <paramref name="lamp"/> times - by their luminances, which the lamp's fit works in; one where nothing is drawn.
    /// </summary>
    private static float Asked(CaptureAtGame drawn, int gathered, double rest, double lamp)
    {
        if (gathered >= drawn.Gather.Triangle.Length || drawn.Gather.Triangle[gathered] < 0)
        {
            return 1f;
        }

        SceneLight.LightTerms terms = drawn.Gather.Terms[gathered];
        double light = CaptureAtGame.Luminance(terms.Unexposed), its = CaptureAtGame.Luminance(terms.PlayerDiffuse);
        return light > 1e-9 ? (float)(((rest * (light - its)) + (lamp * its)) / light) : 1f;
    }

    /// <summary>The shape a drawing shows at a gathered pixel, or -1.</summary>
    private static int ShapeAt(CaptureAtGame drawn, int gathered)
    {
        int triangle = gathered < drawn.Gather.Triangle.Length ? drawn.Gather.Triangle[gathered] : -1;
        return triangle >= 0 && triangle < drawn.ShapeOf.Length ? drawn.ShapeOf[triangle] : -1;
    }

    /// <summary>Whether the pane's sun lights a gathered pixel, as the report classes it.</summary>
    private static bool Lit(CaptureAtGame drawn, int gathered)
    {
        SceneLight.LightTerms terms = drawn.Gather.Terms[gathered];
        return CaptureLightCompare.ClassOf(terms.Sun, terms.SunFacing) == CaptureLightCompare.Lighting.Lit;
    }

    /// <summary>
    /// The install as far as the made stage asks it: the rock's material says it is a roof section as The Red Vale's trees do - the word last
    /// at the top level - and nothing else is in it.
    /// </summary>
    private static byte[]? RoofRock(string path)
        => path == "Art/rock.mat"
            ? Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[{"parent":"Metadata/Materials/DielectricSpecGlossBN.fxgraph"},{"parent":"Metadata/Effects/Graphs/General/ForceAlphaTest.fxgraph"}],"roofsection":true}""")
            : null;

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// A roof section the game hides comes out hidden and one it shows comes out shown, by the game's pattern - the stage drawn without the
    /// section held against the game (CaptureRoofFade); and where it is hidden, whether its shadow stays: a game drawn with the rock as a
    /// shadow caster alone keeps it, one drawn without the rock drops it. The rock is a piece of a tile laid in the area, its footprint read
    /// from the area's tiles beside another piece of the same file, the player standing on it - the reading that his piece fades whole
    /// agrees with the hidden verdicts and not with the shown one. As an object of its own its words are read through what it extends. The
    /// replay of the kept numbers judges alike.
    /// </summary>
    [Fact]
    public void AROOFSectionTheGameHidesIsMeasuredHiddenAndOneItShowsShown()
    {
        const string rocky = "Metadata/Terrain/Woods/rocky.tdt";
        TerrainTiles tiles = Laid(40, 40, (rocky, 18, 34, 3, 3), (rocky, 21, 34, 3, 3), ("Metadata/Terrain/Woods/grass.tdt", 18, 37, 2, 2));
        Vector3 away = Vector3.Normalize(new Vector3(-0.5f, -0.5f, 1f));
        WorldSnapshot shot = Shot(player: new Vector3(5000f, 9000f, 0f));

        static byte[]? Read(string path) => path switch
        {
            "Art/rock.mat" => RoofRock(path),
            "rock.ao" => Encoding.Unicode.GetBytes("\ufeffversion 2\r\nextends \"Metadata/Terrain/RoofParent\"\r\nAnimatedRender\r\n{\r\n\tenable_roof_fade = true\r\n}\r\n"),
            "Metadata/Terrain/RoofParent.ao" => Encoding.UTF8.GetBytes("version 2\nextends \"nothing\"\nAnimatedRender\n{\n\troof_fade_cylinder = \"150 300 0\"\n}\n"),
            _ => null,
        };

        foreach ((string game, string source, string verdict, string shadow) in new[]
        {
            ("hidden, its shadow kept", rocky + " at tile 18, 34", "hidden", "the game KEEPS a hidden section's shadow"),
            ("hidden, its shadow dropped", rocky + " at tile 18, 34", "hidden", "the game DROPS a hidden section's shadow"),
            ("shown", rocky + " at tile 18, 34", "shown", "the hidden sections' shadows: no section called hidden"),
            ("shown, an object of its own", "rock.ao", "shown", "the hidden sections' shadows: no section called hidden"),
        })
        {
            CapturePane pane = Stage(MaterialBlend.Opaque, sunWay: away, patterned: true, rockSource: source);
            CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
            SkinnedMesh mesh = pane.Model.Mesh;
            int rock = pane.Model.ShapeMaterials.ToList().IndexOf("Art/rock.mat");
            MaterialBlend[] hiding = [.. Enumerable.Range(0, mesh.Shapes.Count).Select(shape => shape == rock ? MaterialBlend.ShadowOnly : pane.Blends![shape])];
            int[] shapeOf = CaptureAtGame.ShapesOf(mesh);
            byte[] picture = game switch
            {
                "hidden, its shadow kept" => own.Redrawn(mesh, hiding),
                "hidden, its shadow dropped" => own.Redrawn(mesh.Keeping([.. shapeOf.Select(shape => shape != rock)]), pane.Blends!),
                _ => own.Picture,
            };

            CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Bgr(picture), Width, Height, null, out _)!;
            bool ofTile = source.StartsWith(rocky, StringComparison.Ordinal);
            string report = CaptureRoofFade.Write(
                Read, scene, string.Empty, ofTile ? tiles : null, null, null, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);

            Assert.Matches(@"^roof fade: 1 roof sections on the stage, 1 on the screen, [\d,]+ of their pixels compared", report);
            Match row = Regex.Match(report, @"\n  (\d+)\trock\t([\d,]+)\t([\d,]+)\t(hidden|shown|unclear|-)\t([-\d.]+)\t([-\d.]+)\t([-\d.]+)\t([-\d.]+)\t([^\t]+)\t");
            Assert.True(row.Success, report);
            Assert.Equal(rock, int.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.True(Count(row.Groups[3].Value) > 50, $"{game}: {row.Groups[3].Value} telling pixels\n{report}");
            Assert.True(verdict == row.Groups[4].Value, $"{game}: {row.Groups[4].Value}\n{report}");
            Assert.Contains(shadow, report, StringComparison.Ordinal);
            Assert.Equal(CaptureRoofFade.Tinted, Assert.Single(pictures).File);
            if (ofTile)
            {
                Assert.Equal("his", row.Groups[9].Value);
                Assert.Contains("on tile 20, 36, of the piece rocky at tile 18, 34, 3 x 3", report, StringComparison.Ordinal);
                Assert.Contains(
                    verdict == "hidden"
                        ? "the piece the player stands on fades whole, no other (a reading of RoofFadeMode \"Tile\" - the pane's until v0.1.393): 1 of 1 verdicts agree\n"
                        : $"the piece the player stands on fades whole, no other (a reading of RoofFadeMode \"Tile\" - the pane's until v0.1.393): 0 of 1 verdicts agree - against it: {rock} shown (rocky at tile 18, 34, 3 x 3)",
                    report.ReplaceLineEndings("\n"),
                    StringComparison.Ordinal);

                // THE PANE'S OWN READING, held first: the rock round the player reaches into the region, so it agrees with hidden and not with shown.
                Assert.Contains(
                    verdict == "hidden"
                        ? "250 aside, 0 behind (the pane's reading, RoofFade - its extents CHOSEN): 1 of 1 verdicts agree\n"
                        : $"250 aside, 0 behind (the pane's reading, RoofFade - its extents CHOSEN): 0 of 1 verdicts agree - against it: {rock} shown (rocky at tile 18, 34, 3 x 3)",
                    report.ReplaceLineEndings("\n"),
                    StringComparison.Ordinal);
                Assert.Contains("=== the region's extents the verdicts allow", report, StringComparison.Ordinal);
                Assert.Matches(@"\n  rocky at tile 18, 34, 3 x 3\this\t[-\d]+ to [-\d]+\t[-\d]+ to [-\d]+\t", report);
                Assert.Contains("  1 tile pieces: their words are the area's, below", report, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("-", row.Groups[9].Value);
                Assert.Contains("the player's piece: not known", report, StringComparison.Ordinal);
                Assert.Contains("  rock.ao: enable_roof_fade = true | Metadata/Terrain/RoofParent.ao: roof_fade_cylinder = \"150 300 0\"", report, StringComparison.Ordinal);
            }

            string folder = Directory.CreateTempSubdirectory("roof-fade").FullName;
            try
            {
                File.WriteAllBytes(Path.Combine(folder, CaptureRoofFade.Numbers), numbers);
                string replay = CaptureRoofFade.Replay(folder);
                Assert.StartsWith("roof fade replay: 1 roof sections on the stage", replay, StringComparison.Ordinal);
                Assert.Contains(row.Value, replay, StringComparison.Ordinal);
                Assert.Contains(shadow, replay, StringComparison.Ordinal);

                // THE EXTENTS OVER CAPTURES read the same numbers: a tile piece's verdict placed, an object's left out.
                string extents = CaptureRoofFade.ExtentsOver([folder]);
                Assert.StartsWith(
                    string.Create(CultureInfo.InvariantCulture, $"roof fade extents over 1 captures: #1 {Path.GetFileName(folder)}: {(ofTile && verdict == "hidden" ? 1 : 0)} hidden, {(ofTile && verdict == "shown" ? 1 : 0)} shown"),
                    extents,
                    StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// The verdict goes by the game's pattern, not its light: a game showing what lies behind a section at a third of its light and an offset
    /// comes out hidden, one showing the section three times as bright shown - v0.1.385's fit of the light read such a light's own mismatch
    /// as a share; a game whose logarithm is the two drawings' halved is unclear, its share without one half; a section whose pixels tell
    /// nothing has no verdict. The hidden sections' shadows go by the step at their edge: a game keeping it comes out kept, one without it
    /// dropped. The numbers read back from their file judge alike.
    /// </summary>
    [Fact]
    public void THEROOFFadesVerdictGoesByTheGamesPatternNotItsLight()
    {
        var section = new CaptureRoofFade.Section(4, "branches", "tree.ao", new Vector3(-50f, -50f, -400f), new Vector3(50f, 50f, -100f));
        var header = new CaptureRoofFade.Header(
            64, 64, 1, new Vector2(1000f, 2000f), new Vector3(1000f, 2000f, 0f), new Vector3(0f, 0f, -2000f), Vector3.UnitZ, Vector3.UnitX, new Vector2(32f, 32f), [section]);
        List<CaptureRoofFade.Sample> Samples(Func<Vector3, Vector3, Vector3, Vector3> game)
        {
            var made = new List<CaptureRoofFade.Sample>();
            for (var at = 0; at < 200; at++)
            {
                var with = new Vector3(0.05f * MathF.Exp(0.4f * MathF.Sin(at * 0.37f)));
                var kept = new Vector3(0.4f * MathF.Exp(0.4f * MathF.Sin((at * 1.13f) + 1f)));
                Vector3 gone = kept * 1.1f;
                made.Add(new CaptureRoofFade.Sample(4, at, game(with, kept, gone), with, kept, gone, new Vector3(at % 64, at / 64, -200f), 1800f));
            }

            return made;
        }

        List<CaptureRoofFade.Sample> behind = Samples((with, kept, gone) => (kept * 0.3f) + new Vector3(0.002f));
        CaptureRoofFade.Judged hidden = CaptureRoofFade.Judge(behind);
        Assert.Equal("hidden", hidden.Verdict);
        Assert.True(hidden.Kept > 0.99f && MathF.Abs(hidden.With) < 0.3f, $"{hidden}");
        Assert.Equal(1f, hidden.Share, 1);

        CaptureRoofFade.Judged shown = CaptureRoofFade.Judge(Samples((with, kept, gone) => with * 3f));
        Assert.Equal("shown", shown.Verdict);
        Assert.Equal(1f, shown.With, 4);
        Assert.Equal(0f, shown.Share, 3);

        CaptureRoofFade.Judged half = CaptureRoofFade.Judge(Samples((with, kept, gone) => new Vector3(MathF.Sqrt(with.X * kept.X))));
        Assert.Equal("unclear", half.Verdict);
        Assert.Equal(0.5f, half.Share, 1);

        List<CaptureRoofFade.Sample> alike = [.. Samples((with, kept, gone) => with).Select(one => one with { Kept = one.With, Gone = one.With })];
        Assert.Equal("-", CaptureRoofFade.Judge(alike).Verdict);
        Assert.Contains("\n  4\tbranches\t200\t0\t-\t-\t-\t-\t-\t-\t", CaptureRoofFade.Report(header, alike, [], "test").ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // THE SHADOWS: half the pixels in a shadow a tenth as bright, the floor's pattern under all of them.
        List<CaptureRoofFade.Shade> Shades(bool keeps)
        {
            var made = new List<CaptureRoofFade.Shade>();
            for (var at = 0; at < 200; at++)
            {
                var floor = new Vector3(0.3f * MathF.Exp(0.3f * MathF.Sin(at * 0.71f)));
                Vector3 shaded = at < 100 ? floor * 0.1f : floor;
                made.Add(new CaptureRoofFade.Shade(at, (keeps ? shaded : floor) * 0.6f, shaded, floor));
            }

            return made;
        }

        Assert.Equal("kept", CaptureRoofFade.Shadow(Shades(keeps: true)).Verdict);
        Assert.Equal("dropped", CaptureRoofFade.Shadow(Shades(keeps: false)).Verdict);

        CaptureRoofFade.Header told = header with { Removed = [4] };
        string report = CaptureRoofFade.Report(told, behind, Shades(keeps: true), "test");
        Assert.Contains("\n  4\tbranches\t200\t200\thidden\t", report.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("the game KEEPS a hidden section's shadow", report, StringComparison.Ordinal);
        (CaptureRoofFade.Header back, List<CaptureRoofFade.Sample> samples, List<CaptureRoofFade.Shade> shades) = CaptureRoofFade.Read(CaptureRoofFade.Write(told, behind, Shades(keeps: true)));
        Assert.Equal(report, CaptureRoofFade.Report(back, samples, shades, "test"));
    }

    /// <summary>
    /// A section's piece is the tile file its source names laid from the corner it names, its footprint every tile of that file naming its
    /// place from that corner - not a neighbouring piece of the same file - and the player's is the piece of the tile he stands on; a corner
    /// the tiles do not hold keeps the corner alone, and a source naming no piece has none.
    /// </summary>
    [Fact]
    public void ASECTIONSPieceIsReadFromTheAreasTilesAtTheCornerItsSourceNames()
    {
        const string file = "Metadata/Terrain/Woods/Features_RedvaleRiver/curve5.tdt";
        TerrainTiles tiles = Laid(48, 48, (file, 21, 30, 9, 6), (file, 21, 36, 9, 6), ("Metadata/Terrain/Woods/StSW_01.tdt", 30, 30, 3, 3));
        CaptureRoofFade.Piece piece = CaptureRoofFade.PieceOf(file + " at tile 21, 30", tiles, null)!;
        Assert.Equal(new CaptureRoofFade.Piece(file, 21, 30, 29, 35), piece);
        Assert.True(piece.Known);
        Assert.True(piece.Holds(23, 33));
        Assert.False(piece.Holds(23, 36));
        Assert.Equal("curve5 at tile 21, 30, 9 x 6", piece.Said);

        CaptureRoofFade.Piece mine = CaptureRoofFade.PieceAt(tiles, 23, 33)!;
        Assert.True(mine.Same(piece));
        CaptureRoofFade.Piece below = CaptureRoofFade.PieceAt(tiles, 25, 40)!;
        Assert.Equal(new CaptureRoofFade.Piece(file, 21, 36, 29, 41), below);
        Assert.False(below.Same(mine));
        Assert.True(below.Touches(mine));
        Assert.True(CaptureRoofFade.PieceAt(tiles, 31, 31)!.Touches(mine));
        Assert.Null(CaptureRoofFade.PieceAt(tiles, 2, 2));

        CaptureRoofFade.Piece lost = CaptureRoofFade.PieceOf(file + " at tile 5, 5", tiles, null)!;
        Assert.False(lost.Known);
        Assert.Equal("curve5 at tile 5, 5", lost.Said);
        Assert.False(CaptureRoofFade.PieceOf(file + " at tile 21, 30", null, null)!.Known);
        Assert.Null(CaptureRoofFade.PieceOf("Art/Models/tree.ao", tiles, null));
    }

    /// <summary>
    /// A shape hidden whole (MaterialBlend.Hidden, the pane's roof fade) draws as the mesh without it - neither seen nor casting its shadow -
    /// and a canvas that drew the stage with the shape casting draws it hidden as a fresh one does: the processor's sun map is kept for the
    /// same blends only.
    /// </summary>
    [Fact]
    public void AHIDDENShapeDrawsAsTheMeshWithoutItOnAKeptCanvasToo()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque, sunWay: Vector3.Normalize(new Vector3(-0.5f, -0.5f, 1f)), patterned: true);
        WorldSnapshot shot = Shot(player: new Vector3(5000f, 9000f, 0f));
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        SkinnedMesh mesh = pane.Model.Mesh;
        int rock = pane.Model.ShapeMaterials.ToList().IndexOf("Art/rock.mat");
        MaterialBlend[] hidden = [.. Enumerable.Range(0, mesh.Shapes.Count).Select(shape => shape == rock ? MaterialBlend.Hidden : pane.Blends![shape])];
        int[] shapeOf = CaptureAtGame.ShapesOf(mesh);

        byte[] withHidden = own.Redrawn(mesh, hidden);
        Assert.Equal(own.Redrawn(mesh.Keeping([.. shapeOf.Select(shape => shape != rock)]), pane.Blends!), withHidden);
        Assert.NotEqual(own.Redrawn(mesh, pane.Blends!), withHidden);

        // ONE CANVAS, drawn first with the rock casting and then with it hidden, against a fresh canvas.
        MeshPicture.Canvas Canvas() => new(own.Side)
        {
            Lens = MeshPicture.Camera.Through(own.Camera, own.Origin, own.Side, default, 1f / own.Factor),
            Rows = own.DrawnHeight,
            Light = pane.Light,
        };

        MeshPicture.Canvas kept = Canvas();
        _ = MeshPicture.Of(mesh, kept, skins: pane.Model.Skins, blends: pane.Blends);
        byte[] again = MeshPicture.Of(mesh, kept, skins: pane.Model.Skins, blends: hidden).Rgba;
        byte[] fresh = MeshPicture.Of(mesh, Canvas(), skins: pane.Model.Skins, blends: hidden).Rgba;
        Assert.Equal(fresh, again);
    }

    /// <summary>
    /// A shape drawn as a shadow caster alone through its texture (MaterialBlend.ShadowCut) is not drawn and casts as a cut-out does: its
    /// texture's holes let the sun through, where one drawn as a shadow caster alone (ShadowOnly) casts every triangle whole - the leaves
    /// of a roof section kept as their shadow in the roof part's drawing, which drawn ShadowOnly cast square cards.
    /// </summary>
    [Fact]
    public void ACUTShadowCasterCastsThroughItsTextureAndIsNotDrawn()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque, sunWay: Vector3.Normalize(new Vector3(-0.5f, -0.5f, 1f)), patterned: true, rockHoles: true);
        CaptureAtGame own = CaptureAtGame.Draw(pane, Shot(player: new Vector3(5000f, 9000f, 0f)), null, Width, Height, null, out _)!;
        SkinnedMesh mesh = pane.Model.Mesh;
        int rock = pane.Model.ShapeMaterials.ToList().IndexOf("Art/rock.mat");
        byte[] As(MaterialBlend blend) => own.Redrawn(mesh, [.. Enumerable.Range(0, mesh.Shapes.Count).Select(shape => shape == rock ? blend : pane.Blends![shape])]);
        byte[] none = As(MaterialBlend.Hidden), cut = As(MaterialBlend.ShadowCut), whole = As(MaterialBlend.ShadowOnly), drawn = As(MaterialBlend.Cutout);

        // DARKER THAN THE STAGE WITHOUT THE ROCK, pixel by pixel: the shadow each casts. And drawn, the rock covers pixels of its own.
        static int Shadowed(byte[] cast, byte[] bare)
        {
            var count = 0;
            for (var at = 0; at < bare.Length; at += 4)
            {
                count += cast[at] + cast[at + 1] + cast[at + 2] + 30 < bare[at] + bare[at + 1] + bare[at + 2] ? 1 : 0;
            }

            return count;
        }

        int cutShadow = Shadowed(cut, none), wholeShadow = Shadowed(whole, none);
        Assert.True(cutShadow > 30, $"{cutShadow} pixels in the cut caster's shadow");
        Assert.True(cutShadow < wholeShadow * 0.8, $"cut {cutShadow}, whole {wholeShadow}");

        // NOT DRAWN: where the cut caster's picture differs from the stage without it, it is darker - its shadow, never its own colour.
        for (var at = 0; at < none.Length; at += 4)
        {
            Assert.False(cut[at] + cut[at + 1] + cut[at + 2] > none[at] + none[at + 1] + none[at + 2] + 3, $"pixel {at / 4} brighter than the stage without the rock");
        }

        // AND THE CUT CASTER'S SHADOW IS THE DRAWN CUT-OUT'S, wherever the drawn rock does not stand over it.
        var same = 0;
        var compared = 0;
        for (var at = 0; at < none.Length; at += 4)
        {
            bool rockThere = Math.Abs(drawn[at] - cut[at]) + Math.Abs(drawn[at + 1] - cut[at + 1]) + Math.Abs(drawn[at + 2] - cut[at + 2]) > 3
                && Math.Abs(drawn[at] - none[at]) + Math.Abs(drawn[at + 1] - none[at + 1]) + Math.Abs(drawn[at + 2] - none[at + 2]) > 3
                && Math.Abs(cut[at] - none[at]) + Math.Abs(cut[at + 1] - none[at + 1]) + Math.Abs(cut[at + 2] - none[at + 2]) <= 3;
            if (rockThere)
            {
                continue;
            }

            compared++;
            same += Math.Abs(drawn[at] - cut[at]) + Math.Abs(drawn[at + 1] - cut[at + 1]) + Math.Abs(drawn[at + 2] - cut[at + 2]) <= 3 ? 1 : 0;
        }

        Assert.True(same > compared * 0.97, $"{same} of {compared} alike");
    }

    /// <summary>A stage with no roof section says so and keeps no numbers.</summary>
    [Fact]
    public void ASTAGEWithoutRoofSectionsSaysSo()
    {
        CapturePane pane = Stage(MaterialBlend.Opaque);
        WorldSnapshot shot = Shot(player: new Vector3(5000f, 9000f, 0f));
        CaptureAtGame own = CaptureAtGame.Draw(pane, shot, null, Width, Height, null, out _)!;
        CaptureAtGame scene = CaptureAtGame.Draw(pane, shot, Bgr(own.Picture), Width, Height, null, out _)!;
        string report = CaptureRoofFade.Write(_ => null, scene, string.Empty, null, null, null, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers);
        Assert.StartsWith("roof fade: no roof sections on the stage", report, StringComparison.Ordinal);
        Assert.Empty(pictures);
        Assert.Empty(numbers);
    }

    /// <summary>A drawing's RGBA bytes as the game's picture at the screen's size, blue green red - a drawing of a factor of one.</summary>
    private static byte[] Bgr(byte[] rgba)
    {
        var bgr = new byte[Width * Height * 3];
        for (var at = 0; at < Width * Height; at++)
        {
            (bgr[at * 3], bgr[(at * 3) + 1], bgr[(at * 3) + 2]) = (rgba[(at * 4) + 2], rgba[(at * 4) + 1], rgba[at * 4]);
        }

        return bgr;
    }

    /// <summary>A drawing of nothing, for a game picture of nothing.</summary>
    private static CaptureAtGame Blank()
        => CaptureAtGame.Draw(Stage(MaterialBlend.Opaque), Shot(target: new Vector3(40000f, 9000f, 0f)), null, Width, Height, null, out _)!;

    /// <summary>A game picture one grey throughout - as bright where the pane is lit as where it is shaded.</summary>
    private static byte[] Grey()
    {
        var bgr = new byte[Width * Height * 3];
        Array.Fill(bgr, (byte)128);
        return bgr;
    }

    private static float Decode(byte value)
    {
        float v = value / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static byte Encode(float linear)
    {
        float v = Math.Clamp(linear, 0f, 1f);
        float e = v <= 0.0031308f ? v * 12.92f : (1.055f * MathF.Pow(v, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)((e * 255f) + 0.5f), 0, 255);
    }
}
