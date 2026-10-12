using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>Two grounds of a tile mixed per pixel - ground.ffx's GroundGenerateWeights_1a1b.</summary>
public sealed partial class ShadeProgram
{
    /// <summary>
    /// Two ground materials mixed per pixel by a mask's green, as the engine weighs a tile of two grounds - or null where the two
    /// cannot be run as one (see the remarks).
    /// </summary>
    /// <param name="first">The ground at green nought, bound.</param>
    /// <param name="second">The ground at green one, bound.</param>
    /// <param name="mask">The template's ground mask, read at the mesh's own coordinates - the ground block's.</param>
    /// <param name="maskSheet">The mask decoded.</param>
    /// <param name="scale">The ground materials' tiling: their coordinates are the place's x and y times it.</param>
    /// <remarks>
    /// AS THE ENGINE WEIGHS THEM (ground.ffx): GetUserMask reads the green of the mask (<c>user_blend_texture</c>) at the
    /// block's own coordinates (<c>semanticsData.uv2</c>, the vertex's TEXCOORD as OutputUserBlendUvs hands it on), and
    /// GroundGenerateWeights_1a1b mixes the two grounds by it, <c>lerp(first_ground, second_ground, user_blend)</c>, per pixel.
    /// The ground materials themselves read the engine's projected coordinates (CreateProjectedCoords), the place times
    /// <c>ground_scalemove_uv</c> - so the mesh carries the block's coordinates for the mask, and the program works the
    /// materials' out of the place before running either.
    ///
    /// ONE PROGRAM OF TWO: the mask's read, the coordinates, the first ground's steps and the second's - each in registers of
    /// its own past the fixed ones, its reads and textures after the ones before it - then their colours mixed, and their
    /// specular colours and gloss where they leave them (a ground that leaves no specular is drawn with none, as it is alone).
    /// The engine mixes the whole surfaces, normals included, before the light; the picture lights the mesh's normal.
    ///
    /// NOT THE ENGINE'S HEIGHT BLENDING (GROUND_HEIGHT_BLENDING, which a material's EnableGroundHeightBlending turns on and
    /// which sharpens the weights by the materials' height maps): left out by choice (2026-10-12, from the live client). And
    /// not where either ground's program reads the triangle's tangent frame, which the picture takes from the mesh's own
    /// coordinates - the block's here, not the material's - nor for any of the water's programs.
    /// </remarks>
    public static ShadeProgram? Grounds(ShadeProgram first, ShadeProgram second, ShadeTexture mask, Mipmaps? maskSheet, float scale)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.UsesTangents || second.UsesTangents || first._waves >= 0 || second._waves >= 0 || first.Sprays || second.Sprays)
        {
            return null;
        }

        var steps = new List<Step>(first._steps.Length + second._steps.Length + 8);
        var constantAt = new List<int>(first._constantAt.Length + second._constantAt.Length + 2);
        var constants = new List<Vector4>(constantAt.Capacity);
        var samples = new List<int>(first.Samples + second.Samples + 1);
        int maskTexture = first.Textures.Count + second.Textures.Count;
        int next = Fixed;

        // THE MASK'S GREEN, at the block's own coordinates - the first read.
        int texel = next++;
        samples.Add(maskTexture);
        steps.Add(new Step(Op.Sample, texel, Coordinates, 0, -1, -1, -1, maskTexture));
        int green = next++;
        steps.Add(new Step(Op.Swizzle, green, texel, -1, -1, -1, -1, AllFrom(1)));

        // THE MATERIALS' COORDINATES, the engine's projected ones, in the coordinates' own register for both grounds to read.
        int tiling = next++;
        constantAt.Add(tiling);
        constants.Add(new Vector4(scale, scale, 0f, 0f));
        steps.Add(new Step(Op.Multiply, Coordinates, Position, tiling, -1, -1, -1, 0));

        int firstBase = next;
        next += first.Registers - Fixed;
        int secondBase = next;
        next += second.Registers - Fixed;
        Appended(first, firstBase, 0, samples.Count, steps, constantAt, constants);
        samples.AddRange(first._sampleTexture);
        Appended(second, secondBase, first.Textures.Count, samples.Count, steps, constantAt, constants);
        foreach (int read in second._sampleTexture)
        {
            samples.Add(read + first.Textures.Count);
        }

        int Lerped(int from, int to)
        {
            int into = next++;
            steps.Add(new Step(Op.Lerp, into, from, to, green, -1, -1, 0));
            return into;
        }

        int nothing = -1;
        int Nothing()
        {
            if (nothing < 0)
            {
                nothing = next++;
                constantAt.Add(nothing);
                constants.Add(Vector4.Zero);
            }

            return nothing;
        }

        int firstSpecular = Moved(first._specular, firstBase), secondSpecular = Moved(second._specular, secondBase);
        int firstGloss = Moved(first._gloss, firstBase), secondGloss = Moved(second._gloss, secondBase);
        int result = Lerped(Moved(first._result, firstBase), Moved(second._result, secondBase));
        int specular = firstSpecular >= 0 || secondSpecular >= 0
            ? Lerped(firstSpecular >= 0 ? firstSpecular : Nothing(), secondSpecular >= 0 ? secondSpecular : Nothing())
            : -1;
        int gloss = firstGloss >= 0 && secondGloss >= 0 ? Lerped(firstGloss, secondGloss) : Math.Max(firstGloss, secondGloss);
        if (specular < 0)
        {
            gloss = -1;
        }

        if (next > MostRegisters)
        {
            return null;
        }

        ShadeTexture[] textures = [.. first.Textures, .. second.Textures, mask];
        Mipmaps?[] sheets = [.. first._sheets, .. second._sheets, maskSheet];
        return new ShadeProgram(
            [.. steps], [.. constantAt], [.. constants], next, result, specular, gloss, textures, [.. samples], -1,
            [.. first.Graphs, .. second.Graphs], sheets, hasAlpha: false)
        {
            Mix = (green, Moved(first._result, firstBase), Moved(second._result, secondBase)),
        };
    }

    /// <summary>
    /// Where a program of two grounds (<see cref="Grounds"/>) leaves the mask's green and each ground's colour - nought to one for none;
    /// what the probe reads of a pixel it ran on (<see cref="Mixed"/>).
    /// </summary>
    internal (int Green, int First, int Second) Mix { get; private init; } = (-1, -1, -1);

    /// <summary>Whether the program is two grounds mixed by a mask's green - see <see cref="Grounds"/>.</summary>
    public bool MixesGrounds => Mix.Green >= 0;

    /// <summary>The mask's green and the two grounds' colours, linear, as a run of this program left them in its registers.</summary>
    internal (float Green, Vector3 First, Vector3 Second) Mixed(ReadOnlySpan<Vector4> registers)
    {
        Vector4 first = registers[Mix.First], second = registers[Mix.Second];
        return (registers[Mix.Green].X, new Vector3(first.X, first.Y, first.Z), new Vector3(second.X, second.Y, second.Z));
    }

    /// <summary>One ground's steps and constants, its registers past the fixed ones moved to start at <paramref name="at"/> and its textures and reads after those before it.</summary>
    private static void Appended(
        ShadeProgram program, int at, int textures, int reads, List<Step> steps, List<int> constantAt, List<Vector4> constants)
    {
        foreach (Step step in program._steps)
        {
            int b = step.B;
            int extra = step.Extra;
            if (step.Op is Op.Sample or Op.Parallax)
            {
                b += reads;
                extra += textures;
            }
            else if (step.Op == Op.SampleLod)
            {
                b = Moved(b, at);
                extra += textures;
            }
            else if (step.Op is Op.Spray or Op.Beach)
            {
                // THREE SHEETS A BYTE EACH, the output's byte above them kept.
                int sheets = 0;
                for (var part = 0; part < 3; part++)
                {
                    sheets |= (((extra >> (part * 8)) & 0xFF) + textures) << (part * 8);
                }

                extra = sheets | (extra & ~0xFFFFFF);
                b = Moved(b, at);
            }
            else
            {
                b = Moved(b, at);
            }

            steps.Add(new Step(
                step.Op, Moved(step.To, at), Moved(step.A, at), b, Moved(step.C, at), Moved(step.D, at), Moved(step.E, at), extra, step.Splat));
        }

        for (var one = 0; one < program._constantAt.Length; one++)
        {
            constantAt.Add(Moved(program._constantAt[one], at));
            constants.Add(program._constants[one]);
        }
    }

    /// <summary>A register of one of the grounds where it lies in the mixed program: the fixed ones are shared, and below nought is no register.</summary>
    private static int Moved(int register, int at) => register < Fixed ? register : at + (register - Fixed);
}
