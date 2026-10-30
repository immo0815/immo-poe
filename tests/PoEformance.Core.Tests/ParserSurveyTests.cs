using System.Text;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's parser survey: every kept path judged by its reader, counted per format and version, the first stopped files
/// written beside the part, and those read again offline.
/// </summary>
public class ParserSurveyTests
{
    /// <summary>A whole file and a stopped one of a format are counted apart by version, the stop's reason listed, its file written and read again.</summary>
    [Fact]
    public void THESURVEYCountsEachFormatByVersionAndKeepsTheStoppedFiles()
    {
        var install = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Metadata/Terrain/a.gcf"] = Encoding.UTF8.GetBytes("version 1\n\"a.gt\" \"b.gt\" \"c.gt\"\n"),
            ["Metadata/Terrain/b.gcf"] = Encoding.UTF8.GetBytes("version 1\n\"a.gt\" \"b.gt\"\n"),
            ["Metadata/Terrain/c.gt"] = Encoding.UTF8.GetBytes("BlackInsideWall\n0 1 0 0\n"),
            ["Metadata/Terrain/readme.txt"] = Encoding.UTF8.GetBytes("not surveyed"),
        };

        var kept = new ParserSurvey.Kept();
        Assert.StartsWith("parser survey: not run", ParserSurvey.Run(kept, install.GetValueOrDefault, null, new ParserSurvey.Running())[0], StringComparison.Ordinal);

        kept.Set([.. install.Keys]);
        string folder = Directory.CreateTempSubdirectory("parser-survey-").FullName;
        try
        {
            var running = new ParserSurvey.Running();
            IReadOnlyList<string> lines = ParserSurvey.Run(kept, install.GetValueOrDefault, folder, running);

            Assert.StartsWith("parser survey: 3 of 3 files of 16 formats read", lines[0], StringComparison.Ordinal);
            Assert.Contains("2 whole, 1 stopped, 1 formats with a stop", lines[0], StringComparison.Ordinal);
            Assert.Contains("=== .gcf: 2 files in the install, 2 surveyed, 2 read - 1 whole, 1 stopped", lines);
            Assert.Contains("  version 1: 1 whole, 1 stopped", lines);
            Assert.Contains(lines, line => line.StartsWith("    Metadata/Terrain/b.gcf · line 2 ", StringComparison.Ordinal));
            Assert.Contains("  carries: 4 flags 1", lines);
            Assert.Equal((3L, 3L), (running.Done, running.Of));

            string sample = Path.Combine(folder, ParserSurvey.SampleFolder, "Metadata/Terrain/b.gcf");
            Assert.True(File.Exists(sample));

            File.WriteAllLines(Path.Combine(folder, CaptureReport.ParserSurveyFile), lines);
            string replay = ParserSurvey.Replay(folder);
            Assert.StartsWith("parser survey replay: 1 sample files read again - 0 whole now, 1 still stopped", replay, StringComparison.Ordinal);
            Assert.Contains("Metadata/Terrain/b.gcf · version 1 · was: line 2 ", replay, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Past the most a format keeps, an even stride over its sorted paths is surveyed, and the install's count is still said.</summary>
    [Fact]
    public void AFORMATPastTheMostKeptIsSurveyedByAStride()
    {
        var kept = new ParserSurvey.Kept();
        kept.Set([.. Enumerable.Range(0, ParserSurvey.Kept.MostPerFormat * 2).Select(one => $"Art/m{one:D6}.tgm")]);

        Assert.Equal(ParserSurvey.Kept.MostPerFormat * 2, kept.InInstall(".tgm"));
        IReadOnlyList<string> lines = ParserSurvey.Run(kept, _ => null, null, new ParserSurvey.Running());
        Assert.Contains(lines, line => line.StartsWith($"=== .tgm: {ParserSurvey.Kept.MostPerFormat * 2:N0} files in the install, {ParserSurvey.Kept.MostPerFormat:N0} surveyed", StringComparison.Ordinal));
    }

    /// <summary>Each surveyed extension reaches its own reader - nothing falls to "no reader".</summary>
    [Fact]
    public void EVERYSurveyedFormatHasAReader()
    {
        foreach (string extension in ParserSurvey.Extensions)
        {
            ParserSurvey.Verdict verdict = ParserSurvey.Judge("x" + extension, [0, 0, 0, 0]);
            Assert.DoesNotContain("no reader", verdict.Why, StringComparison.Ordinal);
        }
    }
}
