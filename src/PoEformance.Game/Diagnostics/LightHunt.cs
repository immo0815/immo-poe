using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Schema;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>What a light hunt settled: the report, and the readings it found if it found exactly one.</summary>
/// <param name="Report">Every needle and where it was found, for the person reading it and for the clipboard.</param>
/// <param name="Summary">One line of it.</param>
/// <param name="Sun">The sun reading whose vector the game holds, where exactly one's is found - an index into <see cref="SceneLight.SunReading"/>.</param>
/// <param name="Cube">The cube reading whose turn the game holds as written, where exactly one's is - an index into <see cref="SceneLight.CubeReading"/>.</param>
public sealed record LightHuntVerdict(string Report, string Summary, int? Sun, int? Cube);

/// <summary>
/// Where the parsed environment keeps area.dust_color, counted from the two angles the light hunt finds it by - the offset schema's ParsedEnvironment.
/// </summary>
/// <param name="AfterPhi">Bytes from directional_light.phi to the colour's red.</param>
/// <param name="AfterHor">Bytes from environment_mapping.hor_angle to it.</param>
public sealed record DustPlace(int AfterPhi, int AfterHor)
{
    /// <summary>The place a schema struct states, or null where it lacks one of Phi, HorAngle and DustColor or puts the colour before either angle.</summary>
    /// <remarks>
    /// READ OFF THE SCHEMA rather than kept here, because it is an offset like any other: it was
    /// measured in one client, and the next may move it. A move shows in the report as the colour read
    /// beside the angles disagreeing with the file's own, and is put right in the schema alone.
    /// </remarks>
    public static DustPlace? From(StructDef? layout)
    {
        if (layout?.Field("DustColor") is not { } dust || layout.Field("Phi") is not { } phi || layout.Field("HorAngle") is not { } hor)
        {
            return null;
        }

        int afterPhi = dust.Offset - phi.Offset, afterHor = dust.Offset - hor.Offset;
        return afterPhi > 0 && afterHor > 0 ? new DustPlace(afterPhi, afterHor) : null;
    }
}

/// <summary>
/// Asks the game which candidate reading of an environment's sun and cube angles it uses, by looking for each reading's vector and matrix in its memory.
/// </summary>
/// <remarks>
/// WHY THIS CAN ANSWER WHAT A SCREENSHOT ONLY SUGGESTS: the .env gives the sun as two angles and the
/// cube's turn as two more, and the game turns them into a vector and a matrix on its processor
/// before handing them to its shaders - so each candidate reading's vector, worked out here, either
/// is in the game's memory or is not. <see cref="FloatHunt"/> looks.
///
/// WHAT A SUN VECTOR IN MEMORY MEANS: the shader is handed the way the light travels - lighting.ffx's
/// ComputeDirectionalLightParams turns <c>light_direction_data</c> round (<c>direction =
/// -light_direction_data.xyz</c>) to get the way to the light - so the copy the processor keeps is
/// that vector, and each reading's needle is the way ITS light travels (<see cref="SceneLight.SunFrom"/>).
/// The two readings that differ only in which way the vector points are each other's negatives;
/// finding both says the game keeps the vector both ways, and then this cannot choose between them.
///
/// WHAT A CUBE MATRIX IN MEMORY MEANS: the shaders read the cube by <c>mul(float4(dir, 0),
/// env_map_rotation)</c>, a row vector times the matrix - System.Numerics' own convention, and the
/// camera matrix at WorldData + 0x1A0 is kept in the same order (its clip components are dots with
/// the flat array's columns). So a matrix found AS WRITTEN is a reading the picture can use as it
/// stands; one found only transposed is reported and not taken, since it is the inverse turn.
///
/// THE RAW ANGLES ARE LOOKED FOR TOO, exactly, so a vector found near its environment's phi and theta
/// reads as the environment's own rather than a coincidence elsewhere in the heap.
///
/// AND THE DUST COLOUR, exactly as area.dust_color gives it: 411 environments set none, and where the
/// game keeps the colour it does use there is the one place that says what it is. That place was found
/// this way, beside phi and hor_angle, and is the offset schema's ParsedEnvironment; handed in as a
/// <see cref="DustPlace"/>, the hunt reads what lies there beside every phi and hor_angle it finds.
/// </remarks>
public sealed class LightHunt
{
    /// <summary>How far a computed float may be from the game's - its sine and cosine are not this one's to the last bit.</summary>
    public const float Tolerance = 1e-4f;

    /// <summary>How near an angle must sit to a vector or matrix to be said to be beside it, in bytes.</summary>
    public const int Beside = 4096;

    /// <summary>How many bytes either side of the sun's vector are read back, to see what the game keeps beside it.</summary>
    public const int Around = 256;

    /// <summary>Most of the other arrangements of the cube's turn listed when found.</summary>
    private const int MostArrangementsSaid = 24;

    private static readonly string[] CubeOrders =
    [
        "axes swapped to the cube's, turned about its up by -hor",
        "axes swapped, turned about the cube's up by -hor, then tipped about its x by vert",
        "axes swapped, tipped about the cube's x by vert, then turned about its up by -hor",
        "axes swapped, turned about the cube's up by -hor, then tipped about its z by vert",
        "axes swapped, tipped about the cube's z by vert, then turned about its up by -hor",
    ];

    private readonly List<Entry> _entries;
    private readonly Func<int, string> _sunName;
    private readonly Dictionary<int, int> _sameCube;
    private readonly DustPlace? _dust;

    private LightHunt(string path, EnvironmentSettings environment, List<Entry> entries, Dictionary<int, int> sameCube, Func<int, string> sunName, DustPlace? dust)
    {
        Path = path;
        Environment = environment;
        _entries = entries;
        _sameCube = sameCube;
        _sunName = sunName;
        _dust = dust;
        Needles = [.. entries.Select(one => one.Needle)];
    }

    /// <summary>The environment's path.</summary>
    public string Path { get; }

    /// <summary>The environment the needles were worked out from.</summary>
    public EnvironmentSettings Environment { get; }

    /// <summary>What to look for, in the order the result's sightings name.</summary>
    public IReadOnlyList<FloatNeedle> Needles { get; }

    /// <summary>
    /// The needles for an environment: every sun reading's vector, every cube reading's matrix and the raw angles - or null with why there is nothing to look for.
    /// </summary>
    /// <param name="path">The environment's path, for the report.</param>
    /// <param name="environment">The environment, read.</param>
    /// <param name="sunName">What a sun reading is called in the panel, by its index.</param>
    /// <param name="why">Why there is nothing to look for, or empty.</param>
    /// <param name="dust">Where the parsed environment keeps the dust colour beside phi and hor_angle, or null not to read it there.</param>
    public static LightHunt? For(string path, EnvironmentSettings environment, Func<int, string> sunName, out string why, DustPlace? dust = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(sunName);
        why = string.Empty;
        if (!environment.Ready)
        {
            why = environment.Why.Length > 0 ? environment.Why : "the environment was not read";
            return null;
        }

        var entries = new List<Entry>();
        if (environment.Phi is { } phi && environment.Theta is { } theta)
        {
            foreach (SceneLight.SunReading reading in Enum.GetValues<SceneLight.SunReading>())
            {
                Vector3 travels = SceneLight.SunFrom(phi, theta, reading);
                entries.Add(new Entry(What.Sun, (int)reading, false, false,
                    new FloatNeedle($"sun reading {(int)reading + 1}", [travels.X, travels.Y, travels.Z], Tolerance, Around)));
            }

            entries.Add(Angle("phi", phi, Reach(dust?.AfterPhi)));
            entries.Add(Angle("theta", theta));

            // AND THE PARSED ENVIRONMENT'S WINDOW: phi again, read as far either side as the hunt reads anything, so the
            // file's own values can be found in it - a map of the parsed copy - and what stands beside them read. See
            // Layout, and WaterWindow for the water's own lines.
            //
            // ROUND EVERY PLACE PHI IS FOUND, not the first four: Clearfell's phi stood in eleven places, the first four in
            // address order were pairs of some other object's, and the parsed copies - hor_angle 0xD0 and theta 0x338 after
            // them - were among the seven never read. Window picks the copy holding most of the file's values.
            if (environment.All.Count > 0)
            {
                entries.Add(new Entry(What.Water, -1, false, false,
                    new FloatNeedle("phi, for the parsed environment", [phi], 0f, FloatHunt.MostAround, FloatHunt.MostPerNeedle)));
            }
        }

        // AND THE WATER PASS'S UNIFORMS AS common.ffx DECLARES THEM, where the processor keeps a copy in that order: the
        // file's water values each in its uniform's slot, every other slot left unchecked - see Uniforms.
        if (UniformNeedle(environment.Water) is { } uniforms)
        {
            entries.Add(new Entry(What.WaterUniforms, -1, false, false, uniforms));
        }

        var sameCube = new Dictionary<int, int>();
        float horizontal = environment.HorAngle ?? 0f, vertical = environment.VertAngle ?? 0f;
        if (horizontal != 0f || vertical != 0f)
        {
            var built = new List<(int Reading, Matrix4x4 Turn)>();
            foreach (SceneLight.CubeReading reading in Enum.GetValues<SceneLight.CubeReading>())
            {
                Matrix4x4 turn = SceneLight.CubeTurnFrom(horizontal, vertical, reading);
                int same = built.FindIndex(one => Near(one.Turn, turn));
                if (same >= 0)
                {
                    sameCube[(int)reading] = built[same].Reading;
                    continue;
                }

                built.Add(((int)reading, turn));
                foreach (bool transposed in (ReadOnlySpan<bool>)[false, true])
                {
                    Matrix4x4 laid = transposed ? Matrix4x4.Transpose(turn) : turn;
                    entries.Add(new Entry(What.Cube, (int)reading, transposed, false,
                        new FloatNeedle($"cube reading {(int)reading + 1}{(transposed ? " transposed" : string.Empty)}, 3 by 3", Rows(laid, padded: false), Tolerance)));
                    entries.Add(new Entry(What.Cube, (int)reading, transposed, true,
                        new FloatNeedle($"cube reading {(int)reading + 1}{(transposed ? " transposed" : string.Empty)}, rows of four", Rows(laid, padded: true), Tolerance)));
                }
            }

            // EVERY OTHER ARRANGEMENT OF THE SAME TWO TURNS: about any axis, either way round, after any
            // swap or flip of the axes - the cube's own frame need not be the world's. The first hunt
            // found no reading's turn in AzmerianRanges, as written or transposed, while the sun's
            // vector was there nine times over.
            entries.AddRange(Arrangements(horizontal, vertical, built.Select(one => one.Turn)));

            if (environment.HorAngle is { } h)
            {
                entries.Add(Angle("hor_angle", h, Reach(dust?.AfterHor)));
            }

            if (environment.VertAngle is { } v)
            {
                entries.Add(Angle("vert_angle", v));
            }
        }

        // THE DUST COLOUR AS THE FILE GIVES IT, exactly, with what lies round it: found once beside the
        // sun or the angles, the same offset reads the colour the engine holds in an area whose file sets
        // none - which is what EnvironmentSettings.AssumedDust stands in for until then.
        if (environment.Dust is { } colour)
        {
            entries.Add(new Entry(What.Dust, -1, false, false, new FloatNeedle("area.dust_color", [colour.X, colour.Y, colour.Z], 0f, Around)));
        }

        if (!entries.Exists(one => one.What is What.Sun or What.Cube or What.CubeOther or What.Dust or What.WaterUniforms))
        {
            why = "the environment gives neither a sun's angles, a cube turn nor a dust colour";
            return null;
        }

        return new LightHunt(path, environment, entries, sameCube, sunName, dust);
    }

    /// <summary>What a finished hunt found, said.</summary>
    public LightHuntVerdict Read(FloatHuntResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var found = new List<ulong>[_entries.Count];
        for (var at = 0; at < found.Length; at++)
        {
            found[at] = [];
        }

        foreach (FloatSighting one in result.Sightings)
        {
            if (one.Needle >= 0 && one.Needle < found.Length)
            {
                found[one.Needle].Add(one.At);
            }
        }

        var capped = new HashSet<int>(result.Capped);
        var unsearched = new HashSet<int>(result.Unsearched);
        var angles = new List<(string Name, ulong At)>();
        for (var at = 0; at < _entries.Count; at++)
        {
            if (_entries[at].What == What.Angle)
            {
                angles.AddRange(found[at].Select(place => (_entries[at].Needle.Name, place)));
            }
        }

        var report = new StringBuilder();
        EnvironmentSettings env = Environment;
        report.Append(CultureInfo.InvariantCulture, $"light hunt for {Path}: phi {Say(env.Phi)} theta {Say(env.Theta)}, hor_angle {Say(env.HorAngle)} vert_angle {Say(env.VertAngle)}").AppendLine();
        report.Append(CultureInfo.InvariantCulture,
            $"looked through {result.BytesScanned / (1024.0 * 1024 * 1024):0.00} GB in {result.RegionsWalked} regions in {result.Took.TotalSeconds:0.0} s on {result.Workers} threads{(result.Truncated ? " - stopped at the budget, so a reading not found may lie past it" : result.Stopped ? " - STOPPED before the last region, so a reading not found may lie past it" : string.Empty)}").AppendLine();

        int? sun = null;
        var sunFound = new List<int>();
        if (_entries.Exists(one => one.What == What.Sun))
        {
            report.AppendLine("sun - each reading's vector is the way its light travels, which is what the shader is handed (lighting.ffx: direction = -light_direction_data):");
            for (var at = 0; at < _entries.Count; at++)
            {
                Entry entry = _entries[at];
                if (entry.What != What.Sun)
                {
                    continue;
                }

                if (found[at].Count > 0)
                {
                    sunFound.Add(entry.Reading);
                }

                report.Append(CultureInfo.InvariantCulture, $"  {_sunName(entry.Reading)}: {Places(found[at], capped.Contains(at), unsearched.Contains(at), angles)}").AppendLine();
            }

            sun = sunFound.Count == 1 ? sunFound[0] : null;

            // BOTH WAYS ROUND, SIDE BY SIDE, is how AzmerianRanges held it: each copy of a reading's
            // vector had its negative sixteen bytes on. Which of the two is the light's way is then
            // settled by the ground: the sun of a sunlit area stands above it, so its light travels
            // down - plus z, the game's up being minus z.
            if (sunFound.Count == 2 && Math.Abs(sunFound[0] - sunFound[1]) == 4 && env.Phi is { } phi && env.Theta is { } theta)
            {
                int first = _entries.FindIndex(one => one.What == What.Sun && one.Reading == sunFound[0]);
                int second = _entries.FindIndex(one => one.What == What.Sun && one.Reading == sunFound[1]);
                int beside = found[first].Count(place => found[second].Exists(other => Math.Abs((long)other - (long)place) == 16));
                int[] lighting = [.. sunFound.Where(one => SceneLight.SunFrom(phi, theta, (SceneLight.SunReading)one).Z > 0f)];
                report.Append(CultureInfo.InvariantCulture,
                    $"  the two are each other's negatives, {beside} of {found[first].Count} copies side by side, sixteen bytes apart").AppendLine();
                if (lighting.Length == 1)
                {
                    sun = lighting[0];
                    report.Append(CultureInfo.InvariantCulture,
                        $"  {_sunName(lighting[0])} is the one whose sun stands above the ground - its light travels down, plus z - so it is the light's way").AppendLine();
                }
            }

            if (sun is { } settled)
            {
                report.AppendLine(settled == (int)SceneLight.GameSun
                    ? "  that is the reading the picture draws the sun by (SceneLight.GameSun)"
                    : $"  that is NOT the reading the picture draws the sun by - it draws {_sunName((int)SceneLight.GameSun)} (SceneLight.GameSun)");
            }
        }

        foreach (int at in Enumerable.Range(0, _entries.Count).Where(one => _entries[one].What == What.Angle))
        {
            report.Append(CultureInfo.InvariantCulture, $"{_entries[at].Needle.Name} {_entries[at].Needle.Values[0]:0.#####}, exactly: {Places(found[at], capped.Contains(at), unsearched.Contains(at), null)}").AppendLine();
        }

        foreach (int at in Enumerable.Range(0, _entries.Count).Where(one => _entries[one].What == What.Dust))
        {
            // BESIDE THE SUN'S VECTOR AS WELL AS THE ANGLES: the vector is the copy the renderer keeps.
            var marks = new List<(string Name, ulong At)>(angles);
            for (var sunAt = 0; sunAt < _entries.Count; sunAt++)
            {
                if (_entries[sunAt].What == What.Sun)
                {
                    marks.AddRange(found[sunAt].Select(place => (_sunName(_entries[sunAt].Reading), place)));
                }
            }

            float[] rgb = _entries[at].Needle.Values;
            report.Append(CultureInfo.InvariantCulture,
                $"dust - area.dust_color {rgb[0]:0.#####} {rgb[1]:0.#####} {rgb[2]:0.#####}, exactly: {Places(found[at], capped.Contains(at), unsearched.Contains(at), marks)}").AppendLine();
        }

        int? cube = null;
        var cubeFound = new List<int>();
        if (_entries.Exists(one => one.What == What.Cube))
        {
            report.AppendLine("cube - each reading's env_map_rotation, the shaders' mul(float4(dir, 0), env_map_rotation): as written is the order the picture uses, transposed the inverse turn; reading 1 is the game's turn as found, the rest add vert_angle's candidate tips:");
            for (var at = 0; at < _entries.Count; at++)
            {
                Entry entry = _entries[at];
                if (entry.What != What.Cube)
                {
                    continue;
                }

                if (found[at].Count > 0 && !entry.Transposed && !cubeFound.Contains(entry.Reading))
                {
                    cubeFound.Add(entry.Reading);
                }

                report.Append(CultureInfo.InvariantCulture, $"  {entry.Needle.Name} ({CubeOrders[entry.Reading]}): {Places(found[at], capped.Contains(at), unsearched.Contains(at), angles)}").AppendLine();
            }

            foreach ((int reading, int same) in _sameCube)
            {
                report.Append(CultureInfo.InvariantCulture, $"  cube reading {reading + 1} is reading {same + 1}'s turn for these angles - with vert_angle nought, or one angle nought, the two cannot be told apart here").AppendLine();
            }

            int arrangements = _entries.Count(one => one.What == What.CubeOther);
            var arranged = Enumerable.Range(0, _entries.Count).Where(one => _entries[one].What == What.CubeOther && found[one].Count > 0).ToList();
            report.Append(CultureInfo.InvariantCulture,
                $"  every other arrangement of the same turns - about any axis, either way, after any swap or flip of the axes: {arrangements} looked for, {arranged.Count} found").AppendLine();
            foreach (int at in arranged.Take(MostArrangementsSaid))
            {
                report.Append(CultureInfo.InvariantCulture, $"    {_entries[at].Needle.Name}: {Places(found[at], capped.Contains(at), false, angles)}").AppendLine();
            }

            cube = cubeFound.Count == 1 ? cubeFound[0] : null;
        }

        // THE DUST COLOUR WHERE THE PARSED ENVIRONMENT KEEPS IT, read beside phi and hor_angle rather
        // than looked for: in an area whose file sets none, that is the only way to see what the engine
        // uses instead. Beside a file that sets one, the two agreeing says the place is still right.
        if (_dust is not { } place)
        {
            report.AppendLine("dust beside the angles: not read - the offset schema states no ParsedEnvironment with Phi, HorAngle and DustColor");
        }
        else
        {
            var dustSaid = false;
            foreach (FloatDump dump in result.Dumps ?? [])
            {
                if (_entries[dump.Needle].What != What.Angle || DustBeside(dump, _entries[dump.Needle].Needle.Name, place) is not { } beside)
                {
                    continue;
                }

                if (!dustSaid)
                {
                    report.Append(CultureInfo.InvariantCulture,
                        $"dust beside the angles - the parsed environment keeps area.dust_color 0x{place.AfterPhi:X} bytes after phi and 0x{place.AfterHor:X} after hor_angle; {(env.Dust is { } dust ? $"the file's is {Say(dust)}" : "the file sets none, so this is the engine's own")}:").AppendLine();
                    dustSaid = true;
                }

                report.Append(CultureInfo.InvariantCulture,
                    $"  beside {_entries[dump.Needle].Needle.Name} at 0x{dump.At:X}: {Say(beside)}{(env.Dust is { } file ? (beside == file ? " - the file's" : " - NOT the file's") : string.Empty)}").AppendLine();
            }
        }

        FloatDump? window = Window(result);
        WaterWindow(report, result);
        Layout(report, window);

        // WHAT THE GAME KEEPS BESIDE THE SUN'S VECTOR, a row of eight floats at a time, the vector's
        // own in brackets - the cube's turn and the light's other numbers may sit next to it. The
        // angles' own windows are only there for the dust, said above; the water's for the water.
        foreach (FloatDump dump in result.Dumps ?? [])
        {
            if (_entries[dump.Needle].What is What.Angle or What.Water or What.WaterUniforms)
            {
                continue;
            }

            report.Append(CultureInfo.InvariantCulture, $"beside {_entries[dump.Needle].Needle.Name} at 0x{dump.At:X}:").AppendLine();
            Dumped(report, dump, _entries[dump.Needle].Needle.Values.Length);
        }

        string sunSaid = !_entries.Exists(one => one.What == What.Sun) ? "no sun angles"
            : sun is { } s ? $"sun: {_sunName(s)}"
            : sunFound.Count == 0 ? "sun: no reading's vector found"
            : sunFound.Count == 2 && Math.Abs(sunFound[0] - sunFound[1]) == 4 ? $"sun: both {_sunName(sunFound[0])} and {_sunName(sunFound[1])} - the game keeps the vector both ways"
            : $"sun: {sunFound.Count} readings found - {string.Join(", ", sunFound.Select(one => one + 1))}";
        string cubeSaid = !_entries.Exists(one => one.What == What.Cube) ? "no cube turn"
            : cube is { } c ? $"cube: reading {c + 1}{(_sameCube.ContainsValue(c) ? " (or one that is the same for these angles)" : string.Empty)}"
            : cubeFound.Count == 0 ? "cube: no reading's matrix found as written"
            : $"cube: {cubeFound.Count} readings found - {string.Join(", ", cubeFound.Select(one => one + 1))}";
        string summary = $"{sunSaid} · {cubeSaid}";
        report.Append("verdict: ").Append(summary).AppendLine();
        if (sun is null && cube is null)
        {
            report.AppendLine("nothing settled: the game may keep these where this cannot see, in another form, or for another environment than the area's own");
        }

        Costs(report, result);
        return new LightHuntVerdict(report.ToString(), summary, sun, cube);
    }

    /// <summary>
    /// Where the hunt's time went, by kind of memory - the reading out of the game and the looking through apart.
    /// </summary>
    /// <remarks>
    /// WRITTEN because a capture's hunt over sixteen gigabytes was still running after two minutes on
    /// one thread, and whether the time goes on the game's heap, on files mapped in or on memory the
    /// driver shares - and on reading it or on looking through it - decides what would make it faster.
    /// The times are summed over the threads, so beside the hunt's own time they say how busy each was.
    /// </remarks>
    private static void Costs(StringBuilder report, FloatHuntResult result)
    {
        if (result.Kinds.Count == 0)
        {
            return;
        }

        report.Append(CultureInfo.InvariantCulture,
            $"where the time went: {result.Listed} regions listed and ordered in {result.Listing.TotalSeconds:0.00} s, then by kind of memory (the times summed over the threads):").AppendLine();
        report.AppendLine("  kind                              regions        GB   reading s  sweeping s   GB/s read  gave up");
        foreach (FloatHuntKind kind in result.Kinds)
        {
            double gb = kind.Bytes / (1024.0 * 1024 * 1024);
            double rate = kind.Reading.TotalSeconds > 0 ? gb / kind.Reading.TotalSeconds : 0;
            report.Append(CultureInfo.InvariantCulture,
                $"  {kind.Kind,-32} {kind.Regions,8} {gb,9:0.000} {kind.Reading.TotalSeconds,11:0.00} {kind.Sweeping.TotalSeconds,11:0.00} {rate,11:0.00} {kind.Failed,8}").AppendLine();
        }
    }

    private static Entry Angle(string name, float value, int around = 0) => new(What.Angle, -1, false, false, new FloatNeedle(name, [value], 0f, around));

    /// <summary>
    /// The water pass's uniforms in the order common.ffx declares them, each with its first float's slot and its width: three
    /// float4s, then sixteen floats - packed as HLSL packs a block, a float4 on a 16-byte boundary and the floats after it.
    /// </summary>
    /// <remarks>
    /// AN ORDER READ OFF THE SOURCE, NOT A LAYOUT MEASURED: whether the processor keeps a copy of the block in this order is
    /// exactly what the needle built from it asks the game. Found, the slots before the file's values are the engine's own -
    /// water_color_open among them, its alpha the density water.ffx takes as exp(-distance * density).
    /// </remarks>
    private static readonly (string Name, int Slot, int Width)[] Uniforms =
    [
        ("water_color_terrain", 0, 4), ("water_color_open", 4, 4), ("water_wind_direction", 8, 4),
        ("water_subsurface_scattering", 12, 1), ("water_refraction_index", 13, 1), ("water_reflectiveness", 14, 1),
        ("water_caustics_mult", 15, 1), ("water_wind_intensity", 16, 1), ("water_wind_speed", 17, 1),
        ("water_directionness", 18, 1), ("water_swell_intensity", 19, 1), ("water_swell_height", 20, 1),
        ("water_swell_angle", 21, 1), ("water_swell_period", 22, 1), ("water_flow_intensity", 23, 1),
        ("water_flow_foam", 24, 1), ("water_dispersion", 25, 1), ("water_clarity", 26, 1), ("water_height_offset", 27, 1),
    ];

    /// <summary>The .env water keys that name one uniform each, by the uniform's slot - the colours their first three floats.</summary>
    /// <remarks>
    /// ONLY WHERE THE NAMES SAY IT: a key that is the uniform's name without "water_", and the two colours by what they are.
    /// The murkiness keys are left out - that they become the colours' alpha fits the shader and is not written anywhere.
    /// </remarks>
    private static readonly Dictionary<string, int> UniformOfKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["terrain_water_color"] = 0, ["open_water_color"] = 4, ["subsurface_scattering"] = 12, ["refraction_index"] = 13,
        ["reflectiveness"] = 14, ["caustics_mult"] = 15, ["wind_intensity"] = 16, ["wind_speed"] = 17, ["directionness"] = 18,
        ["swell_intensity"] = 19, ["swell_height"] = 20, ["swell_angle"] = 21, ["swell_period"] = 22, ["flow_intensity"] = 23,
        ["flow_foam"] = 24, ["height_offset"] = 27,
    };

    /// <summary>The uniform block as a needle: the file's water values each in its slot, from the first slot one fills to the last, every other slot unchecked - or null where fewer than two slots are filled.</summary>
    private static FloatNeedle? UniformNeedle(IReadOnlyList<EnvironmentValue> water)
    {
        var slots = new SortedDictionary<int, float>();
        foreach (EnvironmentValue one in water)
        {
            if (one.Flag || !UniformOfKey.TryGetValue(one.Key, out int slot))
            {
                continue;
            }

            for (var at = 0; at < Math.Min(one.Values.Count, slot < 8 ? 3 : 1); at++)
            {
                slots[slot + at] = one.Values[at];
            }
        }

        if (slots.Count < 2)
        {
            return null;
        }

        int first = slots.Keys.First(), last = slots.Keys.Last();
        var values = new float[last - first + 1];
        Array.Fill(values, float.NaN);
        foreach ((int slot, float value) in slots)
        {
            values[slot - first] = value;
        }

        // READ FROM THE BLOCK'S FIRST SLOT ON, wherever the file's values begin: the colours stand before them.
        string named = string.Join(", ", slots.Keys.Select(slot => Uniforms.Last(one => one.Slot <= slot).Name).Distinct());
        return values.Length > FloatHunt.LongestNeedle ? null : new FloatNeedle($"the water uniforms as common.ffx declares them, {named} in their slots", values, 0f, (first * sizeof(float)) + 64);
    }

    /// <summary>The water's two lines of evidence: the uniform block where it is found, and the file's values in the window round phi.</summary>
    private void WaterWindow(StringBuilder report, FloatHuntResult result)
    {
        IReadOnlyList<EnvironmentValue> water = Environment.Water;
        if (water.Count == 0)
        {
            return;
        }

        report.Append("water - the file's water section: ").AppendLine(string.Join(", ", water.Select(Said)));
        int uniformAt = _entries.FindIndex(one => one.What == What.WaterUniforms);
        if (uniformAt < 0)
        {
            report.AppendLine("  the uniform block: not looked for - fewer than two of the file's keys name a uniform");
        }
        else
        {
            FloatNeedle needle = _entries[uniformAt].Needle;
            int first = UniformOfKey.Where(pair => water.Any(one => !one.Flag && string.Equals(one.Key, pair.Key, StringComparison.OrdinalIgnoreCase))).Min(pair => pair.Value);
            List<FloatSighting> seen = [.. result.Sightings.Where(one => one.Needle == uniformAt)];
            report.Append(CultureInfo.InvariantCulture, $"  {needle.Name}: {seen.Count} places{(result.Capped.Contains(uniformAt) ? " (capped)" : string.Empty)}").AppendLine();
            foreach (FloatDump dump in (result.Dumps ?? []).Where(one => one.Needle == uniformAt))
            {
                long block = (long)(dump.At - dump.From) - (first * sizeof(float));
                report.Append(CultureInfo.InvariantCulture, $"  the block at 0x{dump.At - (ulong)(first * sizeof(float)):X}, read as declared:").AppendLine();
                foreach ((string name, int slot, int width) in Uniforms)
                {
                    long at = block + (slot * sizeof(float));
                    if (at < 0 || at + (width * sizeof(float)) > dump.Bytes.Length)
                    {
                        continue;
                    }

                    IEnumerable<string> floats = Enumerable.Range(0, width).Select(one => Say(BitConverter.ToSingle(dump.Bytes, (int)at + (one * sizeof(float)))));
                    report.Append("    ").Append(name).Append(' ').AppendLine(string.Join(" ", floats));
                }
            }
        }

        int windowAt = _entries.FindIndex(one => one.What == What.Water);
        List<FloatDump> windows = windowAt < 0 ? [] : [.. (result.Dumps ?? []).Where(one => one.Needle == windowAt)];
        if (windows.Count == 0)
        {
            report.AppendLine("  beside phi: no window read - phi was not found, or the file gives none");
            return;
        }

        var lines = new List<(int Found, string Line)>();
        foreach (FloatDump dump in windows)
        {
            long origin = (long)(dump.At - dump.From);
            int found = 0;
            var line = new StringBuilder();
            foreach (EnvironmentValue one in water)
            {
                List<long> places = Places(dump.Bytes, one, origin);
                found += places.Count > 0 ? 1 : 0;
                line.Append("; ").Append(one.Key).Append(' ').Append(places.Count == 0 ? "-" : string.Join(" ", places.Take(8).Select(Offset)));
            }

            lines.Add((found, string.Create(CultureInfo.InvariantCulture, $"    phi at 0x{dump.At:X}: {found} of {water.Count} found{line}")));
        }

        report.Append(CultureInfo.InvariantCulture,
            $"  beside phi, within 0x{FloatHunt.MostAround:X} bytes either side, {windows.Count} windows read, the {Math.Min(windows.Count, MostWaterWindowsSaid)} holding most of the file's water values - where each lies, as a float and a whole number as an int too:").AppendLine();
        foreach ((_, string line) in lines.OrderByDescending(one => one.Found).Take(MostWaterWindowsSaid))
        {
            report.AppendLine(line);
        }
    }

    /// <summary>Most windows round phi the water's lines are printed for.</summary>
    private const int MostWaterWindowsSaid = 4;

    /// <summary>The window round phi holding the most of the file's values - the parsed environment's own, of the copies found - or null.</summary>
    private FloatDump? Window(FloatHuntResult result)
    {
        int at = _entries.FindIndex(one => one.What == What.Water);
        FloatDump? fullest = null;
        int most = -1;
        foreach (FloatDump dump in (result.Dumps ?? []).Where(one => one.Needle == at))
        {
            long origin = (long)(dump.At - dump.From);
            int found = Environment.All.Count(one => Places(dump.Bytes, one, origin).Count > 0);
            if (found > most)
            {
                (fullest, most) = (dump, found);
            }
        }

        return fullest;
    }

    /// <summary>
    /// The parsed environment mapped by the file's own values: each key the file sets, where it lies beside phi, in the order of the struct - then the window whole.
    /// </summary>
    /// <remarks>
    /// EACH FIELD A VALUE AND A FLAG: Port's copy (v0.1.208) holds every value its file sets with a one after it and every value it
    /// leaves to the engine with a nought - the engine's defaults, where the water's colours are when the file names none. Which
    /// default is which key is said by an area whose file sets that key: there it is found here, at its slot.
    /// </remarks>
    private void Layout(StringBuilder report, FloatDump? window)
    {
        if (window is null)
        {
            report.AppendLine("the parsed environment: no window read - phi was not found, or the file gives none");
            return;
        }

        long origin = (long)(window.At - window.From);
        var placed = new List<(long Nearest, string Line)>();
        var missing = new List<string>();
        foreach (EnvironmentValue one in Environment.All)
        {
            List<long> places = Places(window.Bytes, one, origin);
            if (places.Count == 0)
            {
                missing.Add(Said(one));
                continue;
            }

            long[] nearest = [.. places.OrderBy(Math.Abs).Take(MostPlacesSaid)];
            placed.Add((nearest[0], string.Create(
                CultureInfo.InvariantCulture,
                $"    {Offset(nearest[0]),8}  {Said(one)}{(places.Count > 1 ? $"  (also {string.Join(" ", nearest.Skip(1).Select(Offset))}{(places.Count > MostPlacesSaid ? $" and {places.Count - MostPlacesSaid} more" : string.Empty)})" : string.Empty)}")));
        }

        report.Append(CultureInfo.InvariantCulture,
            $"the parsed environment round phi at 0x{window.At:X}: {placed.Count} of {Environment.All.Count} of the file's values found in it, each at its nearest place - a value set by the file has a one after it, one left to the engine a nought:").AppendLine();
        foreach ((_, string line) in placed.OrderBy(one => one.Nearest))
        {
            report.AppendLine(line);
        }

        if (missing.Count > 0)
        {
            report.Append("    not in the window: ").AppendLine(string.Join("; ", missing));
        }

        report.Append(CultureInfo.InvariantCulture, $"  the window round phi at 0x{window.At:X} whole, eight floats a row, phi in brackets:").AppendLine();
        Dumped(report, window, 1);
    }

    /// <summary>Most places one value is listed at in the map.</summary>
    private const int MostPlacesSaid = 4;

    private static string Offset(long offset) => string.Create(CultureInfo.InvariantCulture, $"{(offset < 0 ? "-" : "+")}0x{Math.Abs(offset):X}");

    /// <summary>Where a file's value lies in a window, as offsets from the needle: its floats in a row, and a whole number as an int too.</summary>
    private static List<long> Places(byte[] bytes, EnvironmentValue value, long origin)
    {
        var places = new List<long>();
        int width = value.Values.Count * sizeof(float);
        bool whole = value.Values.Count == 1 && !value.Flag && value.Values[0] == MathF.Round(value.Values[0]) && MathF.Abs(value.Values[0]) < 1e6f;
        for (var at = 0; at + width <= bytes.Length; at += sizeof(float))
        {
            var floats = true;
            for (var one = 0; one < value.Values.Count && floats; one++)
            {
                floats = BitConverter.ToSingle(bytes, at + (one * sizeof(float))) == value.Values[one];
            }

            if (floats || (whole && BitConverter.ToInt32(bytes, at) == (int)value.Values[0]))
            {
                places.Add(at - origin);
            }
        }

        return places;
    }

    private static string Said(EnvironmentValue value)
        => value.Key + " " + (value.Flag ? (value.Values[0] != 0f ? "true" : "false") : string.Join(" ", value.Values.Select(one => one.ToString("0.#####", CultureInfo.InvariantCulture))));

    private static string Say(float value)
        => float.IsFinite(value) && (value == 0f || MathF.Abs(value) is >= 1e-6f and < 1e7f)
            ? value.ToString("0.#####", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"#{BitConverter.SingleToUInt32Bits(value):X8}");

    /// <summary>
    /// Every arrangement of the environment's two turns but the readings' own: each turn about any of the three axes, either way, in either order, after any of the 48 swaps and flips of the axes - each matrix once, in both layouts.
    /// </summary>
    private static IEnumerable<Entry> Arrangements(float horizontal, float vertical, IEnumerable<Matrix4x4> readings)
    {
        var seen = new List<Matrix4x4>();
        foreach (Matrix4x4 one in readings)
        {
            seen.Add(one);
            seen.Add(Matrix4x4.Transpose(one));
        }

        string[] axes = ["x", "y", "z"];
        var entries = new List<Entry>();
        foreach ((Matrix4x4 swap, string swapSaid) in Swaps())
        {
            for (var first = 0; first < 3; first++)
            {
                for (var second = 0; second < 3; second++)
                {
                    if (second == first && vertical != 0f && horizontal != 0f)
                    {
                        continue;
                    }

                    foreach (float h in (ReadOnlySpan<float>)[horizontal, -horizontal])
                    {
                        foreach (float v in (ReadOnlySpan<float>)[vertical, -vertical])
                        {
                            foreach (bool horFirst in (ReadOnlySpan<bool>)[true, false])
                            {
                                Matrix4x4 round = Turn(first, h), tip = Turn(second, v);
                                Matrix4x4 turn = swap * (horFirst ? round * tip : tip * round);
                                if (seen.Exists(one => Near(one, turn)))
                                {
                                    continue;
                                }

                                seen.Add(turn);
                                string byHor = string.Create(CultureInfo.InvariantCulture, $"about {axes[first]} by {(h < 0f ? "-" : "+")}hor");
                                string byVert = string.Create(CultureInfo.InvariantCulture, $"about {axes[second]} by {(v < 0f ? "-" : "+")}vert");
                                string turns = horizontal == 0f ? byVert : vertical == 0f ? byHor : horFirst ? $"{byHor}, then {byVert}" : $"{byVert}, then {byHor}";
                                string said = $"axes {swapSaid}, turned {turns}";
                                entries.Add(new Entry(What.CubeOther, -1, false, false, new FloatNeedle(said + ", 3 by 3", Rows(turn, padded: false), Tolerance)));
                                entries.Add(new Entry(What.CubeOther, -1, false, true, new FloatNeedle(said + ", rows of four", Rows(turn, padded: true), Tolerance)));
                            }
                        }
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>The 48 swaps and flips of three axes, each as the matrix a row vector is put through first, and how it is said.</summary>
    private static IEnumerable<(Matrix4x4 Swap, string Said)> Swaps()
    {
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        string[] axes = ["x", "y", "z"];
        foreach (int[] order in orders)
        {
            for (var signs = 0; signs < 8; signs++)
            {
                var m = Matrix4x4.Identity;
                m.M11 = m.M22 = m.M33 = 0f;
                var said = new StringBuilder();
                for (var row = 0; row < 3; row++)
                {
                    float sign = (signs & (1 << row)) != 0 ? -1f : 1f;
                    Set(ref m, row, order[row], sign);
                    said.Append(axes[row]).Append('>').Append(sign < 0f ? "-" : string.Empty).Append(axes[order[row]]).Append(row < 2 ? " " : string.Empty);
                }

                yield return (m, said.ToString());
            }
        }
    }

    private static void Set(ref Matrix4x4 m, int row, int column, float value)
    {
        switch ((row * 3) + column)
        {
            case 0: m.M11 = value; break;
            case 1: m.M12 = value; break;
            case 2: m.M13 = value; break;
            case 3: m.M21 = value; break;
            case 4: m.M22 = value; break;
            case 5: m.M23 = value; break;
            case 6: m.M31 = value; break;
            case 7: m.M32 = value; break;
            default: m.M33 = value; break;
        }
    }

    private static Matrix4x4 Turn(int axis, float angle) => axis switch
    {
        0 => Matrix4x4.CreateRotationX(angle),
        1 => Matrix4x4.CreateRotationY(angle),
        _ => Matrix4x4.CreateRotationZ(angle),
    };

    /// <summary>How far round an angle is read back to reach the dust colour this many bytes after it - nought, for no window, where it is not read.</summary>
    private static int Reach(int? after) => after is { } bytes ? bytes + (3 * sizeof(float)) : 0;

    /// <summary>The three floats where the dust colour lies after an angle, out of what was read round it - null where the window does not reach.</summary>
    private static Vector3? DustBeside(FloatDump dump, string angle, DustPlace place)
    {
        int after = angle switch
        {
            "phi" => place.AfterPhi,
            "hor_angle" => place.AfterHor,
            _ => -1,
        };
        long at = (long)(dump.At - dump.From) + after;
        return after < 0 || at < 0 || at + (3 * sizeof(float)) > dump.Bytes.Length
            ? null
            : new Vector3(BitConverter.ToSingle(dump.Bytes, (int)at), BitConverter.ToSingle(dump.Bytes, (int)at + 4), BitConverter.ToSingle(dump.Bytes, (int)at + 8));
    }

    /// <summary>A dump as rows of eight floats, each row's first offset from the needle's place, the needle's own floats in brackets.</summary>
    private static void Dumped(StringBuilder report, FloatDump dump, int length)
    {
        int count = dump.Bytes.Length / sizeof(float);
        long origin = (long)(dump.At - dump.From);
        for (var at = 0; at < count; at += 8)
        {
            long offset = (at * sizeof(float)) - origin;
            report.Append(CultureInfo.InvariantCulture, $"  {(offset < 0 ? "-" : "+")}0x{Math.Abs(offset):X3}:");
            for (int one = at; one < Math.Min(at + 8, count); one++)
            {
                float value = BitConverter.ToSingle(dump.Bytes, one * sizeof(float));
                long place = (one * sizeof(float)) - origin;
                bool own = place >= 0 && place < length * sizeof(float);
                string said = float.IsFinite(value) && (value == 0f || MathF.Abs(value) is >= 1e-6f and < 1e7f)
                    ? value.ToString("0.#####", CultureInfo.InvariantCulture)
                    : string.Create(CultureInfo.InvariantCulture, $"#{BitConverter.ToUInt32(dump.Bytes, one * sizeof(float)):X8}");
                report.Append(own ? " [" : "  ").Append(said).Append(own ? "]" : string.Empty);
            }

            report.AppendLine();
        }
    }

    /// <summary>A matrix's first three rows, each three wide - or four wide with the fourth left unchecked.</summary>
    private static float[] Rows(Matrix4x4 m, bool padded) => padded
        ? [m.M11, m.M12, m.M13, float.NaN, m.M21, m.M22, m.M23, float.NaN, m.M31, m.M32, m.M33]
        : [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];

    private static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        float[] x = Rows(a, false), y = Rows(b, false);
        for (var at = 0; at < x.Length; at++)
        {
            if (MathF.Abs(x[at] - y[at]) > 1e-6f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where a needle was found, and for a vector or matrix the nearest raw angle beside it.</summary>
    private static string Places(List<ulong> places, bool capped, bool unsearched, List<(string Name, ulong At)>? angles)
    {
        if (unsearched)
        {
            return "not looked for - nothing in it but nought and one, within the tolerance";
        }

        if (places.Count == 0)
        {
            return "not found";
        }

        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture, $"{places.Count}{(capped ? "+" : string.Empty)} place{(places.Count == 1 ? string.Empty : "s")}: ");
        foreach (ulong place in places.Take(8))
        {
            said.Append(CultureInfo.InvariantCulture, $"0x{place:X}");
            if (angles is not null && Nearest(place, angles) is { } beside)
            {
                long off = (long)(beside.At - place);
                said.Append(CultureInfo.InvariantCulture, $" ({beside.Name} {Math.Abs(off)} bytes {(off < 0 ? "before" : "after")})");
            }

            said.Append(", ");
        }

        said.Length -= 2;
        if (places.Count > 8)
        {
            said.Append(CultureInfo.InvariantCulture, $" and {places.Count - 8} more");
        }

        return said.ToString();
    }

    private static (string Name, ulong At)? Nearest(ulong place, List<(string Name, ulong At)> angles)
    {
        (string Name, ulong At)? best = null;
        ulong bestDistance = ulong.MaxValue;
        foreach ((string Name, ulong At) one in angles)
        {
            ulong distance = one.At > place ? one.At - place : place - one.At;
            if (distance <= Beside && distance < bestDistance)
            {
                best = one;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static string Say(float? value) => value is { } one ? one.ToString("0.#####", CultureInfo.InvariantCulture) : "absent";

    private static string Say(Vector3 value) => string.Create(CultureInfo.InvariantCulture, $"{value.X:0.#####} {value.Y:0.#####} {value.Z:0.#####}");

    private enum What
    {
        Sun,
        Cube,
        CubeOther,
        Angle,
        Dust,
        Water,
        WaterUniforms,
    }

    private readonly record struct Entry(What What, int Reading, bool Transposed, bool Padded, FloatNeedle Needle);
}
