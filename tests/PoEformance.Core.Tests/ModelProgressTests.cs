using System.Diagnostics;
using PoEformance.Features;

namespace PoEformance.Core.Tests;

/// <summary>The bar under a model being built: the outermost step counts, the ones inside it do not.</summary>
public class ModelProgressTests
{
    [Fact]
    public void ASTEPCountsWhatItIsToldAndTheLineSaysSo()
    {
        var progress = new ModelProgress();
        Assert.StartsWith("building the model · ", progress.Said(), StringComparison.Ordinal);
        Assert.Equal(0f, progress.Fraction);

        using (ModelProgress.Step step = ModelProgress.Begin(progress, "laying the doodads", 4))
        {
            step.Advance();
            step.Advance();
            Assert.Equal(0.5f, progress.Fraction);
            Assert.StartsWith("building the model · laying the doodads 2 of 4 · ", progress.Said(), StringComparison.Ordinal);
            Assert.EndsWith(" s", progress.Said(), StringComparison.Ordinal);
        }

        // THE NEXT STEP STARTS FROM NOUGHT, under its own name.
        using ModelProgress.Step next = ModelProgress.Begin(progress, "compiling the shader graphs", 10);
        Assert.Equal("compiling the shader graphs", progress.Stage);
        Assert.Equal(0, progress.Done);
        Assert.Equal(10, progress.Total);
    }

    [Fact]
    public void ASTEPInsideAnotherIsInert()
    {
        var progress = new ModelProgress();
        using ModelProgress.Step outer = ModelProgress.Begin(progress, "reading the tile files", 3);
        using (ModelProgress.Step inner = ModelProgress.Begin(progress, "painting the shapes", 100))
        {
            inner.Advance();
            inner.Advance();
        }

        outer.Advance();
        Assert.Equal("reading the tile files", progress.Stage);
        Assert.Equal(1, progress.Done);
        Assert.Equal(3, progress.Total);
    }

    [Fact]
    public void NOPROGRESSCostsNothingAndSaysNothing()
    {
        using ModelProgress.Step none = ModelProgress.Begin(null, "anything", 5);
        none.Advance();

        // MORE DONE THAN SAID is shown as whole, never past it.
        var progress = new ModelProgress();
        using ModelProgress.Step step = ModelProgress.Begin(progress, "reading the sub-tiles", 1);
        step.Advance();
        step.Advance();
        Assert.Equal(1f, progress.Fraction);
        Assert.StartsWith("building the model · reading the sub-tiles 1 of 1 · ", progress.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void AKINDSTimeStopsWhileAKindInsideItRuns()
    {
        var progress = new ModelProgress();
        using (ModelProgress.Step step = ModelProgress.Begin(progress, "compiling the shader graphs", 1))
        {
            using (ModelProgress.Time(progress, BuildWork.Decoding))
            {
                Spin(0.01);
                using (ModelProgress.Time(progress, BuildWork.Reading))
                {
                    Spin(0.1);
                }
            }
        }

        // EXCLUSIVE: the read is reading's alone, and decoding would be over 0.11 s had it gone on counting through the read.
        Assert.InRange(progress.SecondsOn(BuildWork.Reading), 0.1, 10);
        Assert.InRange(progress.SecondsOn(BuildWork.Decoding), 0.01, 0.08);
        Assert.Equal(0, progress.SecondsOn(BuildWork.Compiling));

        IReadOnlyList<string> report = progress.Report();
        Assert.StartsWith("built in ", report[0], StringComparison.Ordinal);
        Assert.Contains(report, line => line.StartsWith("step \"compiling the shader graphs\" (1 things): ", StringComparison.Ordinal)
            && line.Contains("texture decoding 0.0", StringComparison.Ordinal));

        // NO PROGRESS, NO TIMING - and nothing thrown.
        using ModelProgress.Timed none = ModelProgress.Time(null, BuildWork.Reading);
    }

    [Fact]
    public void ATIMEDReadIsReading()
    {
        var progress = new ModelProgress();
        Func<string, byte[]?> read = progress.Timing(_ =>
        {
            Spin(0.02);
            return [1, 2, 3];
        });

        Assert.Equal(3, read("Art/a.dds")!.Length);
        Assert.InRange(progress.SecondsOn(BuildWork.Reading), 0.02, 10);
    }

    private static void Spin(double seconds)
    {
        long until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
        {
            Thread.SpinWait(64);
        }
    }
}
