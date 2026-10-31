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
    /// not where either ground's program marches a parallax, which reads the triangle's tangent frame the picture takes from the
    /// mesh's own coordinates - the block's here, not the material's - nor for any of the water's programs.
    ///
    /// A GROUND THAT CARRIES A VECTOR OUT OF ITS TBN BASIS (<see cref="Op.Tbn"/>) MIXES ALL THE SAME (v0.1.376): its frame is handed
    /// the materials' own rather than the mesh's - the engine's ground basis, READ: its tangent is the world's x (InitGroundVectors'
    /// <c>(1, 0, 0, 1)</c>), along which the materials' coordinates run, and its binormal <c>normalize(cross(normal, x))</c> (the ground
    /// graphs' VertexBinormal) - GetGroundTBN's (1, 0, 0), (0, -1, 0), (0, 0, -1) on flat ground. So the step is handed x and, per
    /// pixel, <c>cross(x, N)</c>, already at right angles to both, which it turns into the engine's binormal on every slope. CHOSEN on a
    /// slope: the step makes the tangent orthogonal to the normal where the engine's stays x itself. The frame is the pixel's and the
    /// same for both grounds, so a mixed program reads no frame of the triangle's at all, and neither drawing works one out for it.
    /// </remarks>
    /// <param name="heights">Whether the two are weighed by their heights where either's graphs turn that on - see <see cref="Mixing"/>.</param>
    public static ShadeProgram? Grounds(ShadeProgram first, ShadeProgram second, ShadeTexture mask, Mipmaps? maskSheet, float scale, bool heights = false)
        => Mixing(first, second, mask, maskSheet, scale, heights);

    /// <summary>
    /// Two ground materials mixed per pixel by the second ground's share of the sub-tile's corners, which the mesh carries in its
    /// coordinates' first channel - or null where the two cannot be run as one (see <see cref="Grounds"/>).
    /// </summary>
    /// <param name="first">The ground at share nought, bound.</param>
    /// <param name="second">The ground at share one, bound.</param>
    /// <param name="scale">The ground materials' tiling, as <see cref="Grounds"/> takes it.</param>
    /// <param name="heights">Whether the two are weighed by their heights where either's graphs turn that on - see <see cref="Mixing"/>.</param>
    /// <remarks>
    /// CHOSEN, NOT THE ENGINE'S (v0.1.310, from the live client): a stand-in where the mask's green at the block's own coordinates
    /// comes out in squares - see GroundPainter. The share is worked out per vertex, bilinear from the sub-tile's four corners in the
    /// definition's ground grid, and runs across each triangle as any coordinate does; everything after the weight is the mask's
    /// program's, step for step.
    /// </remarks>
    public static ShadeProgram? GroundsByCorners(ShadeProgram first, ShadeProgram second, float scale, bool heights = false)
        => Mixing(first, second, null, null, scale, heights);

    /// <summary>Whether the program mixes two grounds by the corners' share in its coordinates rather than a mask - see <see cref="GroundsByCorners"/>.</summary>
    public bool MixesByCorners { get; private init; }

    /// <summary>The program behind <see cref="Grounds"/> and <see cref="GroundsByCorners"/>: the weight from the mask where there is one, from the coordinates' first channel where not.</summary>
    /// <remarks>
    /// WEIGHED BY THEIR HEIGHTS AS THE ENGINE WEIGHS THEM, where <paramref name="heights"/> and either ground's graphs carry
    /// <see cref="HeightBlendingNode"/> (its macro, GROUND_HEIGHT_BLENDING, is the whole ground shader's): GroundInterpolate (ground.ffx)
    /// as written - each weight times <c>1 / (1 + (max_height - height) / 0.01)</c>, the weights then divided by their sum, and the
    /// surfaces added up by them, the first's then the second's. A ground's height is what its graphs write (<see cref="Heighted"/>,
    /// PBRGroundBN's the SmoothStep of its normal map's blue), nought where they write none, as InitSurfaceData leaves it - so the two
    /// handed in are their Heighted twins where they have them.
    /// </remarks>
    private static ShadeProgram? Mixing(ShadeProgram first, ShadeProgram second, ShadeTexture? mask, Mipmaps? maskSheet, float scale, bool heights)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Marches || second.Marches || first._waves >= 0 || second._waves >= 0 || first.Sprays || second.Sprays)
        {
            return null;
        }

        var steps = new List<Step>(first._steps.Length + second._steps.Length + 8);
        var constantAt = new List<int>(first._constantAt.Length + second._constantAt.Length + 2);
        var constants = new List<Vector4>(constantAt.Capacity);
        var samples = new List<int>(first.Samples + second.Samples + 1);
        int maskTexture = first.Textures.Count + second.Textures.Count;
        int next = Fixed;

        // THE WEIGHT FIRST, before the coordinates' register is given the materials': the mask's green at the block's own
        // coordinates, or the corners' share the mesh carries in their first channel.
        int green;
        if (mask is not null)
        {
            int texel = next++;
            samples.Add(maskTexture);
            steps.Add(new Step(Op.Sample, texel, Coordinates, 0, -1, -1, -1, maskTexture));
            green = next++;
            steps.Add(new Step(Op.Swizzle, green, texel, -1, -1, -1, -1, AllFrom(1)));
        }
        else
        {
            green = next++;
            steps.Add(new Step(Op.Swizzle, green, Coordinates, -1, -1, -1, -1, AllFrom(0)));
        }

        // THE MATERIALS' COORDINATES, the engine's projected ones, in the coordinates' own register for both grounds to read.
        int tiling = next++;
        constantAt.Add(tiling);
        constants.Add(new Vector4(scale, scale, 0f, 0f));
        steps.Add(new Step(Op.Multiply, Coordinates, Position, tiling, -1, -1, -1, 0));

        // THE ENGINE'S GROUND FRAME, where either ground carries a vector out of its TBN basis - see the remarks: the world's x, and
        // x × N = (0, -N.z, N.y) per pixel for the binormal's side, which the step turns into normalize(cross(N, x)) on every slope.
        (int U, int V) frame = (-1, -1);
        if (Array.Exists(first._steps, one => one.Op == Op.Tbn) || Array.Exists(second._steps, one => one.Op == Op.Tbn))
        {
            frame = (next++, next++);
            constantAt.Add(frame.U);
            constants.Add(Vector4.UnitX);

            // THE NORMAL'S X, Z, Y AND W - x from x, y from z, z from y - its y and z then signed (0, -1, 1, 0).
            int turned = next++;
            steps.Add(new Step(Op.Swizzle, turned, Normal, -1, -1, -1, -1, (2 << 2) | (1 << 4) | (3 << 6) | (0b1111 << 8)));
            int signs = next++;
            constantAt.Add(signs);
            constants.Add(new Vector4(0f, -1f, 1f, 0f));
            steps.Add(new Step(Op.Multiply, frame.V, turned, signs, -1, -1, -1, 0));
        }

        int firstBase = next;
        next += first.Registers - Fixed;
        int secondBase = next;
        next += second.Registers - Fixed;
        Appended(first, firstBase, 0, samples.Count, steps, constantAt, constants, frame);
        samples.AddRange(first._sampleTexture);
        Appended(second, secondBase, first.Textures.Count, samples.Count, steps, constantAt, constants, frame);
        foreach (int read in second._sampleTexture)
        {
            samples.Add(read + first.Textures.Count);
        }

        int Emitted(Op op, int a, int b = -1, int c = -1, int extra = 0)
        {
            int into = next++;
            steps.Add(new Step(op, into, a, b, c, -1, -1, extra));
            return into;
        }

        int Constant(Vector4 value)
        {
            int at = next++;
            constantAt.Add(at);
            constants.Add(value);
            return at;
        }

        int nothing = -1;
        int Nothing()
        {
            if (nothing < 0)
            {
                nothing = Constant(Vector4.Zero);
            }

            return nothing;
        }

        // THE ENGINE'S HEIGHT BLENDING, GroundInterpolate as written - see the remarks.
        bool byHeight = heights && (first.BlendsGroundHeights || second.BlendsGroundHeights);
        int share = green, firstShare = -1, firstHeight = -1, secondHeight = -1;
        if (byHeight)
        {
            firstHeight = first.HeightRegister >= 0 ? Emitted(Op.Swizzle, Moved(first.HeightRegister, firstBase), extra: AllFrom(0)) : Nothing();
            secondHeight = second.HeightRegister >= 0 ? Emitted(Op.Swizzle, Moved(second.HeightRegister, secondBase), extra: AllFrom(0)) : Nothing();
            int one = Constant(Vector4.One), threshold = Constant(new Vector4(HeightThreshold));
            int most = Emitted(Op.Max, firstHeight, secondHeight);
            int Weighed(int weight, int height)
                => Emitted(Op.Multiply, weight, Emitted(Op.Divide, one, Emitted(Op.Add, one, Emitted(Op.Divide, Emitted(Op.Subtract, most, height), threshold))));

            int x = Weighed(Emitted(Op.OneMinus, green), firstHeight), y = Weighed(green, secondHeight);
            int sum = Emitted(Op.Add, x, y);
            firstShare = Emitted(Op.Divide, x, sum);
            share = Emitted(Op.Divide, y, sum);
        }

        int Lerped(int from, int to)
            => byHeight ? Emitted(Op.MultiplyAdd, to, share, Emitted(Op.Multiply, from, firstShare)) : Emitted(Op.Lerp, from, to, green);

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

        ShadeTexture[] textures = mask is { } masked ? [.. first.Textures, .. second.Textures, masked] : [.. first.Textures, .. second.Textures];
        Mipmaps?[] sheets = mask is not null ? [.. first._sheets, .. second._sheets, maskSheet] : [.. first._sheets, .. second._sheets];
        return new ShadeProgram(
            [.. steps], [.. constantAt], [.. constants], next, result, specular, gloss, textures, [.. samples], -1,
            [.. first.Graphs, .. second.Graphs], sheets, hasAlpha: false)
        {
            Mix = (green, Moved(first._result, firstBase), Moved(second._result, secondBase)),
            MixesByCorners = mask is null,
            Weighed = (share, firstHeight, secondHeight),
        };
    }

    /// <summary>GroundInterpolate's height_threshold: how far below the highest ground's height a ground's weight halves.</summary>
    public const float HeightThreshold = 0.01f;

    /// <summary>
    /// Where a program of two grounds weighed by their heights leaves the second ground's share once weighed and each ground's height;
    /// the heights below nought where it is not weighed so - see <see cref="Mixing"/>.
    /// </summary>
    internal (int Share, int FirstHeight, int SecondHeight) Weighed { get; private init; } = (-1, -1, -1);

    /// <summary>Whether the program weighs its two grounds by their heights - see <see cref="Mixing"/>.</summary>
    public bool MixesByHeights => Weighed.FirstHeight >= 0;

    /// <summary>The second ground's share once weighed by the heights, and each ground's height, as a run of this program left them in its registers.</summary>
    internal (float Share, float FirstHeight, float SecondHeight) Heights(ReadOnlySpan<Vector4> registers)
        => (registers[Weighed.Share].X, registers[Weighed.FirstHeight].X, registers[Weighed.SecondHeight].X);

    /// <summary>Whether the program keeps the height its graphs write - see <see cref="Heighted"/>.</summary>
    public bool WritesHeight => HeightRegister >= 0;

    /// <summary>
    /// GroundInterpolate's weighing of two grounds by their heights, for a weight of the second ground <paramref name="share"/> - the second
    /// ground's share once weighed (see <see cref="Mixing"/>).
    /// </summary>
    public static float WeighedShare(float share, float firstHeight, float secondHeight)
    {
        float most = MathF.Max(firstHeight, secondHeight);
        float first = (1f - share) * (1f / (1f + ((most - firstHeight) / HeightThreshold)));
        float second = share * (1f / (1f + ((most - secondHeight) / HeightThreshold)));
        return second / (first + second);
    }

    /// <summary>
    /// A ground's program run at places on the ground outside the drawing - the height it writes, the capture's measure of the engine's
    /// height blending (CaptureGroundCompare), and the colour it draws, the capture's measure of which material of a group the game took
    /// (CaptureGroundChoices). One a thread: its registers are its own.
    /// </summary>
    /// <remarks>
    /// AT THE LEVEL THE PICTURE WOULD READ, per triangle as the renderer's own rule works it out (<see cref="Levels"/>,
    /// MeshPicture.Level): a height read from the top level of a texture the game reads three levels down is a height the game never
    /// saw - PBRGroundBN's comes to nought only where its normal map's blue is nought, which a coarser level averages away.
    /// </remarks>
    public sealed class GroundReader
    {
        private static readonly Vector3 Up = new(0f, 0f, -1f);

        private readonly ShadeProgram _program;
        private readonly Vector4[] _registers;
        private readonly float[] _levels;
        private readonly Vector2[] _spots;

        /// <summary>A reader for one ground's program, bound - its <see cref="Heighted"/> twin where it has one.</summary>
        public GroundReader(ShadeProgram program)
        {
            ArgumentNullException.ThrowIfNull(program);
            _program = program;
            _registers = new Vector4[program.Registers];
            _levels = new float[Math.Max(1, program.Samples)];
            _spots = new Vector2[Math.Max(1, program.Samples) * 3];

            // NOT Preset, which hands the drawing in hand its shore through a static: a capture reads heights beside a live picture.
            program.Constants(_registers);
        }

        /// <summary>
        /// The levels the reads take over one triangle of the ground: its corners' places, where they land on the screen in pixels, and
        /// the materials' tiling - their coordinates the place's x and y times it.
        /// </summary>
        public void Triangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 c0, Vector2 c1, Vector2 c2, float scale)
        {
            ReadOnlySpan<Vector2> coordinates = [new(p0.X * scale, p0.Y * scale), new(p1.X * scale, p1.Y * scale), new(p2.X * scale, p2.Y * scale)];
            ReadOnlySpan<Vector3> positions = [p0, p1, p2];
            ReadOnlySpan<Vector3> normals = [Up, Up, Up];
            float area = ((c1.X - c0.X) * (c2.Y - c0.Y)) - ((c1.Y - c0.Y) * (c2.X - c0.X));
            _program.Levels(_registers, coordinates, positions, normals, new Vector3(c0, 0f), new Vector3(c1, 0f), new Vector3(c2, 0f), area, _spots, _levels);
        }

        /// <summary>The height at a place, its reads at the levels the last <see cref="Triangle"/> set - nought where the program writes none.</summary>
        public float HeightAt(Vector3 place, float scale)
        {
            if (!_program.WritesHeight)
            {
                return 0f;
            }

            _ = _program.Colour(_registers, new Vector2(place.X * scale, place.Y * scale), place, Up, _levels);
            return _registers[_program.HeightRegister].X;
        }

        /// <summary>The colour's brightness at a place, sRGB weighed as the capture weighs the game's picture, nought to one - its reads at the levels the last <see cref="Triangle"/> set.</summary>
        public float BrightnessAt(Vector3 place, float scale)
        {
            Vector3 colour = _program.Colour(_registers, new Vector2(place.X * scale, place.Y * scale), place, Up, _levels);
            return (0.299f * colour.X) + (0.587f * colour.Y) + (0.114f * colour.Z);
        }
    }

    /// <summary>
    /// Where a program of two grounds (<see cref="Grounds"/>) leaves the mask's green and each ground's colour - nought to one for none;
    /// what the probe reads of a pixel it ran on (<see cref="Mixed"/>).
    /// </summary>
    internal (int Green, int First, int Second) Mix { get; private init; } = (-1, -1, -1);

    /// <summary>Whether the program is two grounds mixed per pixel, by a mask's green or the corners' share - see <see cref="Grounds"/>.</summary>
    public bool MixesGrounds => Mix.Green >= 0;

    /// <summary>The weight - the mask's green or the corners' share - and the two grounds' colours, linear, as a run of this program left them in its registers.</summary>
    internal (float Green, Vector3 First, Vector3 Second) Mixed(ReadOnlySpan<Vector4> registers)
    {
        Vector4 first = registers[Mix.First], second = registers[Mix.Second];
        return (registers[Mix.Green].X, new Vector3(first.X, first.Y, first.Z), new Vector3(second.X, second.Y, second.Z));
    }

    /// <summary>
    /// One ground's steps and constants, its registers past the fixed ones moved to start at <paramref name="at"/> and its textures and
    /// reads after those before it - a vector carried out of its TBN basis read over <paramref name="frame"/>'s two registers, the
    /// materials' own frame, where given.
    /// </summary>
    private static void Appended(
        ShadeProgram program, int at, int textures, int reads, List<Step> steps, List<int> constantAt, List<Vector4> constants, (int U, int V) frame)
    {
        foreach (Step step in program._steps)
        {
            int b = step.B;
            int c = Moved(step.C, at);
            int extra = step.Extra;
            if (step.Op == Op.Tbn && frame.U >= 0)
            {
                b = frame.U;
                c = frame.V;
            }
            else if (step.Op is Op.Sample or Op.Parallax)
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
                step.Op, Moved(step.To, at), Moved(step.A, at), b, c, Moved(step.D, at), Moved(step.E, at), extra, step.Splat));
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
