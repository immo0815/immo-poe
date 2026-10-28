using System.Numerics;
using System.Runtime.InteropServices;
using PoEformance.Game.Files;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PoEformance.Gpu;

/// <summary>
/// What one picture of a model is drawn with - MeshPicture.Of's arguments, for the graphics card.
/// </summary>
/// <param name="Mesh">What to draw.</param>
/// <param name="Turn">Rotation about the model's up axis, in radians.</param>
/// <param name="Tilt">Rotation towards the viewer, in radians.</param>
/// <param name="Ink">The colour where there is no texture; default for MeshPicture.UsualInk.</param>
/// <param name="Skin">The model's own texture, or null.</param>
/// <param name="Zoom">How much closer than the fitted view.</param>
/// <param name="Pan">Where the model's centre sits, as a share of the picture off its middle.</param>
/// <param name="Skins">One texture per shape, or null - see MeshPicture.Of.</param>
/// <param name="Blends">How each shape is put over what is behind it, or null for all solid.</param>
/// <param name="Positions">The posed vertices, or null to draw the mesh standing still.</param>
/// <param name="Normals">The posed normals, with <paramref name="Positions"/>.</param>
/// <param name="Light">The game's light, or null for the picture's own lamp - see MeshPicture.Canvas.Light.</param>
/// <param name="Shades">One shade program per shape, or null - see MeshPicture.Of.</param>
/// <param name="Time">The clock a program reads - MeshPicture.Canvas.Time.</param>
/// <param name="Dust">The area's dust colour a program reads, or null for the assumed one - MeshPicture.Canvas.Dust.</param>
/// <param name="Water">The area's open water a program reads, or null for the engine's default - MeshPicture.Canvas.Water.</param>
/// <param name="Sharp">Whether textures are read anisotropically - the one way the card is let draw a picture the processor does not; see <see cref="ModelGpu"/>.</param>
/// <param name="PoseFrom">The first vertex a pose moves: those before it are the mesh's own in <paramref name="Positions"/> and <paramref name="Normals"/>, and stay so while the arrays are the same - a stage's, under the monster posed on it.</param>
/// <param name="PoseKey">Which pose the arrays hold: one key on two draws of the same arrays says nothing moved between them, and nought that nothing is known - the pose is then sent whole every draw.</param>
/// <param name="Sways">Per shape, which of <paramref name="Swayed"/> turns it, or -1 for none - a ship and its deck (MonsterModel.ShapeSways); null for none.</param>
/// <param name="Swayed">This frame's sway of each group, row vectors in the mesh's frame (RoomSway.At) - applied in the vertex shader, the mesh's own vertices left as they are.</param>
/// <param name="Pose">Where the picture is looked at from, or null for <paramref name="Turn"/>, <paramref name="Tilt"/>, <paramref name="Zoom"/> and <paramref name="Pan"/> - MeshPicture.Canvas.Pose.</param>
/// <param name="Skinned">A pose the card skins itself, in the vertex shader, or null - instead of <paramref name="Positions"/> and <paramref name="Normals"/>, which it outranks.</param>
public readonly record struct ModelScene(
    SkinnedMesh Mesh,
    float Turn = 0f,
    float Tilt = 0f,
    Vector3 Ink = default,
    Mipmaps? Skin = null,
    float Zoom = 1f,
    Vector2 Pan = default,
    IReadOnlyList<Mipmaps?>? Skins = null,
    IReadOnlyList<MaterialBlend>? Blends = null,
    Vector3[]? Positions = null,
    Vector3[]? Normals = null,
    SceneLight? Light = null,
    IReadOnlyList<ShadeProgram?>? Shades = null,
    float Time = 0f,
    Vector3? Dust = null,
    bool Sharp = false,
    WaterLook? Water = null,
    int PoseFrom = 0,
    long PoseKey = 0,
    IReadOnlyList<int>? Sways = null,
    Matrix4x4[]? Swayed = null,
    CameraPose? Pose = null,
    CardSkin? Skinned = null);

/// <summary>
/// A pose the graphics card skins itself - each bone's matrix this frame, and the mesh whose bones and weights move the picture's vertices by them.
/// </summary>
/// <param name="Weighted">The mesh whose bones and weights move the picture's vertices from <paramref name="From"/> on, its vertex n the picture's
/// vertex From + n - a monster's own, standing alone (the picture's mesh itself) or on a stage (MonsterModel.Actor's).</param>
/// <param name="From">The picture's first vertex the pose moves - nought alone, MonsterModel.ActorFirst on a stage.</param>
/// <param name="Matrices">Each bone's matrix this frame, row vectors, in the picture's frame: SkeletonPose.Skin alone, and on a stage the same
/// conjugated by his place on it - the inverse of the place, the skin, then the place - since the stage's mesh holds his vertices placed.</param>
/// <param name="Bones">How many of <paramref name="Matrices"/> are the rig's: a bone at or past it moves nothing, as SkeletonPose.Move skips it.</param>
/// <param name="Key">Which pose the matrices hold, above nought: one key on two draws of the same array says nothing moved between them.</param>
/// <param name="Hull">Where the posed vertices lie along the sun - PoseHull.Bounds - for its shadow map's frame, or null where no light casts one.</param>
/// <remarks>
/// THE PROCESSOR'S ARITHMETIC IN THE VERTEX SHADER (ModelShaders' Skinned): SkeletonPose.Move's weighted mean, weight by weight in the
/// same order, a weight of nought and a bone past the rig skipped, the normal made unit length again. On a stage the arithmetic is the
/// conjugated matrix's rather than "skin, then place": the same point, within the floats' rounding.
/// </remarks>
public sealed record CardSkin(SkinnedMesh Weighted, int From, Matrix4x4[] Matrices, int Bones, long Key, ShadowBounds? Hull);

/// <summary>
/// Pictures of models drawn on the graphics card - MeshPicture's pictures, at the card's speed.
/// </summary>
/// <remarks>
/// THE SAME PICTURE, NOT A NEW ONE. The camera is MeshPicture.Camera, mapped onto the card's clip space
/// so that a vertex lands on the very pixel position the processor puts it at; the depth test is
/// "nearer wins, the first drawn on a tie", which is the processor's; the solid and cut-out shapes go
/// first in the mesh's order and the translucent ones after, testing depth and writing none, each
/// covering a pixel once (a stencil value per shape, MeshPicture.Canvas.Stamps' rule); the light and
/// the texture reads are MeshPicture's (see ModelShaders). What cannot match exactly is said where it
/// shows: the card fills a pixel on an edge two triangles share once rather than twice, and works out
/// a texture's level per block of pixels rather than per triangle - which agree wherever the
/// coordinates are straight across a triangle, as the mesh's always are here.
///
/// ON THE OVERLAY'S OWN DEVICE, and only on its render thread: the immediate context is not
/// thread-safe, and the overlay's frame is where the pane is drawn anyway. Whatever this leaves bound,
/// ImGui sets its own state after the frame's drawing returns (the vendored ClickableTransparentOverlay
/// sets every stage it uses before it draws).
///
/// MESHES AND TEXTURES ARE UPLOADED ONCE and kept while they are drawn, by reference: a drag redraws
/// the same mesh every frame, and a room's textures are hundreds of megabytes the card should be sent
/// once. What has not been drawn for a while is let go - see <see cref="Kept"/>.
///
/// THE GAME'S LIGHT (SceneLight) IS DRAWN AS MeshPicture DRAWS IT - see ModelGpu.Light.cs: the sun's
/// shadow map drawn on the card along the sun, on the processor's own texels, and every pixel lit by
/// the same numbers.
///
/// A MATERIAL'S SHADE PROGRAM IS A SHADER OF ITS OWN, written from the program's steps (ShadeHlsl)
/// and compiled behind the frame (ProgramShaders): until every program a picture runs is in, the
/// picture is the processor's, and <see cref="Can"/> says how many are still coming.
///
/// NOT ON THE CARD: the probe and the grey, which read the processor's own pixels - the caller asks
/// <see cref="Can"/> for the rest.
///
/// THE ONE PICTURE THE PROCESSOR DOES NOT DRAW, by choice (ModelScene.Sharp): textures read
/// anisotropically. A trilinear read picks one level for a pixel from the LONGER of the two texel
/// steps across it, so a surface seen at a slant - a floor, a limb from the side - reads a level
/// made for its foreshortened axis and comes out soft along the other; the live client saw it as
/// textures that blurred "from certain angles". An anisotropic read takes up to sixteen samples
/// along the long axis from the level the short one asks for, which is what the game does, and is
/// not a thing the processor's per-triangle level could do at a price anybody would pay. Off, the
/// read is the processor's; the switch is kept in the settings beside the card's own.
/// </remarks>
public sealed partial class ModelGpu : IDisposable
{
    /// <summary>How many draws an upload may go unused before it is let go.</summary>
    private const int Kept = 600;

    /// <summary>The way to the eye in the picture's view - MeshPicture's ToEye.</summary>
    private static readonly Vector3 ToEye = new(0f, 0f, -1f);

    /// <summary>Between the eye and the picture's lamp - MeshPicture.Drawing's _half.</summary>
    private static readonly Vector3 Halfway = Vector3.Normalize(ToEye + MeshPicture.Lamp);

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly bool _owns;
    private readonly ID3D11VertexShader _placed;
    private readonly ID3D11VertexShader _placedWaves;
    private readonly ID3D11VertexShader _placedBeach;
    private ID3D11Texture2D _beachProfile;
    private ID3D11ShaderResourceView _beachProfiled;
    private WaterSheet _beachProfileOf;
    private ID3D11Texture2D _beachNoise;
    private ID3D11ShaderResourceView _beachNoised;
    private WaterSheet _beachNoiseOf;
    private ID3D11Texture2D _fieldProfile;
    private ID3D11ShaderResourceView _fieldProfiled;
    private WaterSheet _fieldProfileOf;
    private ID3D11Texture2D _ripples;
    private ID3D11ShaderResourceView _rippled;
    private WaterSheet _ripplesOf;
    private ID3D11Texture2D _swells;
    private ID3D11ShaderResourceView _swelled;
    private WaterSheet _swellsOf;
    private readonly Dictionary<ShoreField, ShoreTexture> _shores = new(ReferenceEqualityComparer.Instance);
    private readonly ID3D11PixelShader _solid;
    private readonly ID3D11PixelShader _mixed;
    private readonly ID3D11PixelShader _added;
    private readonly ID3D11PixelShader _scened;
    private readonly ID3D11VertexShader _casting;
    private readonly ID3D11PixelShader _away;
    private readonly ID3D11VertexShader _whole;
    private readonly ID3D11PixelShader _straight;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _frame;
    private readonly ID3D11Buffer _part;
    private readonly ID3D11Buffer _scene;
    private readonly ID3D11SamplerState _wrap;
    private readonly ID3D11SamplerState _sharp;
    private readonly ID3D11BlendState _covers;
    private readonly ID3D11BlendState _mixes;
    private readonly ID3D11BlendState _adds;
    private readonly ID3D11BlendState _least;
    private readonly ID3D11BlendState _unseen;
    private readonly ID3D11DepthStencilState _writes;
    private readonly ID3D11DepthStencilState _stamped;
    private readonly ID3D11DepthStencilState _ignores;
    private readonly ID3D11DepthStencilState _surfaced;
    private readonly ID3D11RasterizerState _raster;
    private readonly ProgramShaders _programs;
    private readonly int _largest;
    private readonly Dictionary<SkinnedMesh, MeshBuffers> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Mipmaps, SkinTexture> _skins = new(ReferenceEqualityComparer.Instance);
    private int _sheetsBound;

    /// <summary>Each run's program shader for the drawing in hand, kept from draw to draw so a room's thousands of runs are not an array a frame.</summary>
    private ID3D11PixelShader?[] _runShaders = [];

    /// <summary>What <see cref="_runShaders"/> holds the shaders of: a plan, lit or not, as of so many landed programs.</summary>
    private (Plan Plan, bool Lit, long Landed)? _runShadersFor;

    /// <summary>The last scene <see cref="Can"/> said yes to past its light, by its lists and as of so many landed programs - see Can.</summary>
    private (SkinnedMesh Mesh, Mipmaps? Skin, IReadOnlyList<Mipmaps?>? Skins, IReadOnlyList<MaterialBlend>? Blends, IReadOnlyList<ShadeProgram?>? Shades, bool Lit, long Landed)? _canFor;

    /// <summary>What the part constants hold - written again only when a run asks for something else.</summary>
    private PartConstants? _partHeld;

    /// <summary>This frame's sway of each group - ModelScene.Swayed - or null; a run's group indexes it.</summary>
    private Matrix4x4[]? _swayed;

    /// <summary>The bones' constants - ModelShaders' Bones: whether and from where the card skins, and each bone's matrix.</summary>
    private readonly ID3D11Buffer _bones;

    /// <summary>Each skinned mesh's bones and weights on the card, by the mesh - see <see cref="WeightBuffers"/>.</summary>
    private readonly Dictionary<SkinnedMesh, WeightBuffers> _weights = new(ReferenceEqualityComparer.Instance);

    /// <summary>What <see cref="_bones"/> holds: a skin's matrices by their array and key, or null for "the card skins nothing".</summary>
    private (Matrix4x4[] Matrices, long Key, int From, int Bones, int Count)? _bonesHeld;

    /// <summary>Whether <see cref="_bones"/> has been written at all - an unwritten one says nothing, not "skin nothing".</summary>
    private bool _bonesWritten;

    /// <summary>Which block formats this card samples, by BlockFormat - see <see cref="Blocked"/>.</summary>
    private readonly bool[] _readsBlocks;

    private long _draws;
    private bool _disposed;

    private ModelGpu(ID3D11Device device, ID3D11DeviceContext context, bool owns, Compiled compiled, string? cache)
    {
        _device = device;
        _context = context;
        _owns = owns;
        _placed = compiled.Placed;
        _placedWaves = compiled.PlacedWaves;
        _placedBeach = compiled.PlacedBeach;
        _beachProfileOf = ShadeProgram.BeachProfile;
        (_beachProfile, _beachProfiled) = Sheet(device, _beachProfileOf, Format.R32G32B32A32_Float);
        _beachNoiseOf = ShadeProgram.BeachNoise;
        (_beachNoise, _beachNoised) = Sheet(device, _beachNoiseOf, Format.R32G32B32A32_Float);
        _fieldProfileOf = ShadeProgram.FieldProfile;
        (_fieldProfile, _fieldProfiled) = Sheet(device, _fieldProfileOf, Format.R32G32B32A32_Float);
        _ripplesOf = ShadeProgram.Ripples;
        (_ripples, _rippled) = Sheet(device, _ripplesOf, Format.R32G32B32A32_Float);
        _swellsOf = ShadeProgram.Swells;
        (_swells, _swelled) = Sheet(device, _swellsOf, Format.R32G32B32A32_Float);
        _solid = compiled.Solid;
        _mixed = compiled.Mixed;
        _added = compiled.Added;
        _scened = compiled.Scened;
        _casting = compiled.Casting;
        _away = compiled.Away;
        _whole = compiled.Whole;
        _straight = compiled.Straight;
        _layout = compiled.Layout;
        _frame = device.CreateBuffer(Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _part = device.CreateBuffer(Marshal.SizeOf<PartConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _scene = device.CreateBuffer(Marshal.SizeOf<SceneConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _bones = device.CreateBuffer(BonesBytes, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _readsBlocks = ReadsBlocks(device);
        _wrap = device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear, TextureAddressMode.Wrap, TextureAddressMode.Wrap, TextureAddressMode.Wrap,
            0f, 1, ComparisonFunction.Never, 0f, float.MaxValue));

        // SIXTEEN WHERE THE LEVEL ALLOWS IT: feature level 9.1 caps the anisotropy at two, every
        // level from 9.2 up at sixteen (D3D11_REQ_MAXANISOTROPY and its 9_1 counterpart).
        _sharp = device.CreateSamplerState(new SamplerDescription(
            Filter.Anisotropic, TextureAddressMode.Wrap, TextureAddressMode.Wrap, TextureAddressMode.Wrap,
            0f, device.FeatureLevel >= FeatureLevel.Level_9_2 ? 16 : 2, ComparisonFunction.Never, 0f, float.MaxValue));
        _covers = device.CreateBlendState(BlendDescription.Opaque);
        _mixes = device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _adds = device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
        _least = device.CreateBlendState(Least());
        // THE DEPTH IS REVERSED, one nearest and nought furthest, so its float keeps its precision at every depth - which perspective
        // needs, a near plane of a twentieth of a unit before a harbour ten thousand across (CameraPose.Near). "Nearer wins, the first
        // drawn on a tie" is then greater, not less.
        _writes = device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.Greater));

        // ONCE PER SHAPE PER PIXEL - MeshPicture.Canvas.Stamps: a pixel passes where the stencil does not
        // hold this shape's number yet, and takes it; a pixel the depth test turns away, or the shader
        // discards, takes nothing, as the processor stamps only what it blends.
        _stamped = device.CreateDepthStencilState(new DepthStencilDescription(
            true, false, ComparisonFunction.Greater, true, 0xFF, 0xFF,
            StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Replace, ComparisonFunction.NotEqual,
            StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Replace, ComparisonFunction.NotEqual));
        _ignores = device.CreateDepthStencilState(new DepthStencilDescription(false, DepthWriteMask.Zero, ComparisonFunction.Always));

        // THE LIFTED WATER AS ONE SURFACE - see Surfaced: its nearest crossing written by a walk that draws no colour, then drawn
        // where a fragment is that crossing, once a pixel.
        _unseen = device.CreateBlendState(Unseen());
        _surfaced = device.CreateDepthStencilState(new DepthStencilDescription(
            true, false, ComparisonFunction.GreaterEqual, true, 0xFF, 0xFF,
            StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Replace, ComparisonFunction.NotEqual,
            StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Replace, ComparisonFunction.NotEqual));
        _raster = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
        _largest = device.FeatureLevel >= FeatureLevel.Level_11_0 ? 16384 : 8192;
        FeatureLevel = device.FeatureLevel;
        _tables = Tables(device);
        _programs = new ProgramShaders(device, cache);
    }

    /// <summary>The level the device was made at.</summary>
    public FeatureLevel FeatureLevel { get; }

    /// <summary>
    /// How many material shaders have landed since this was made - a pane whose picture went to the processor while they compiled redraws when this moves.
    /// </summary>
    public long Landed
    {
        get
        {
            _programs.Pump();
            return _programs.Landed;
        }
    }

    /// <summary>
    /// The drawing for a device, or null and why not - a device too old, or shaders that would not compile.
    /// </summary>
    /// <param name="device">The overlay's device.</param>
    /// <param name="context">Its immediate context.</param>
    /// <param name="cache">The folder material shaders are kept in once compiled, or null to keep none.</param>
    /// <param name="why">Why there is none, or empty.</param>
    public static ModelGpu? Of(ID3D11Device? device, ID3D11DeviceContext? context, string? cache, out string why)
    {
        if (device is null || context is null)
        {
            why = "the overlay has no graphics device yet";
            return null;
        }

        return Made(device, context, owns: false, cache, out why);
    }

    /// <summary>
    /// A drawing on WARP, Windows' own processor-run Direct3D - for tests, where there is no card to ask.
    /// </summary>
    /// <param name="why">Why there is none, or empty.</param>
    /// <param name="cache">The folder material shaders are kept in, or null to keep none.</param>
    public static ModelGpu? Warp(out string why, string? cache = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            why = "Direct3D 11 is Windows'";
            return null;
        }

        Result made = D3D11.D3D11CreateDevice(
            null,
            DriverType.Warp,
            DeviceCreationFlags.None,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
            out ID3D11Device device,
            out ID3D11DeviceContext context);
        if (made.Failure)
        {
            why = $"WARP would not start: {made}";
            return null;
        }

        return Made(device, context, owns: true, cache, out why);
    }

    private static ModelGpu? Made(ID3D11Device device, ID3D11DeviceContext context, bool owns, string? cache, out string why)
    {
        Compiled? compiled = Compile(device, out why);
        if (compiled is null)
        {
            if (owns)
            {
                context.Dispose();
                device.Dispose();
            }

            return null;
        }

        return new ModelGpu(device, context, owns, compiled, cache);
    }

    /// <summary>A target to draw into, this many pixels each way - the caller keeps it, see <see cref="ModelTarget"/>.</summary>
    public ModelTarget Target(int size) => new(_device, Math.Clamp(size, 1, MeshPicture.Widest));

    /// <summary>
    /// Whether this can draw a picture as the processor would, or why not - asking for the material shaders it needs, the first time, as it goes.
    /// </summary>
    /// <param name="scene">The picture, its light and its programs among it.</param>
    /// <param name="why">Why not, or empty.</param>
    /// <remarks>On the render thread: it may upload the mesh, to work out which programs run where.</remarks>
    public bool Can(in ModelScene scene, out string why)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (scene.Light is { SunShadows: true } light && light.SunColour != Vector3.Zero && light.ShadowSide > _largest)
        {
            why = $"the sun's shadow map is {light.ShadowSide} square, past the {_largest} this card takes";
            return false;
        }

        // THE SAME LISTS AS THE LAST YES, nothing landed since: the same answer, without walking a room's
        // thousands of runs again every frame. A program's shader, once in, stays while its program does.
        _programs.Pump();
        bool lit = scene.Light is not null;
        if (_canFor is { } was && ReferenceEquals(was.Mesh, scene.Mesh) && ReferenceEquals(was.Skin, scene.Skin)
            && ReferenceEquals(was.Skins, scene.Skins) && ReferenceEquals(was.Blends, scene.Blends) && ReferenceEquals(was.Shades, scene.Shades)
            && was.Lit == lit && was.Landed == _programs.Landed)
        {
            why = string.Empty;
            return true;
        }

        foreach (Mipmaps? one in Worn(scene))
        {
            if (one is not null && (one.Width > _largest || one.Height > _largest))
            {
                why = $"a texture is {one.Width} x {one.Height}, past the {_largest} this card takes";
                return false;
            }

            if (one is not null && Waiting(one))
            {
                why = "a texture this card cannot read as blocks is still being unpacked";
                return false;
            }
        }

        if (scene.Mesh is not { } mesh || scene.Shades is not { Count: > 0 })
        {
            why = string.Empty;
            return true;
        }

        Plan plan = Buffers(mesh).Planned(scene);

        // A GROUND LAYER MEASURES ITSELF AGAINST A COPY OF THE DEPTH, and a depth buffer can be copied
        // only from feature level 10.1 on - Direct3D 10.0 cannot.
        if (plan.Behind && FeatureLevel < FeatureLevel.Level_10_1)
        {
            why = "this card cannot copy its depth for a ground layer to measure against (feature level 10.0)";
            return false;
        }

        var coming = 0;
        foreach (Run run in plan.Runs)
        {
            if (run.Shade < 0 || run.Blend is not (MaterialBlend.Opaque or MaterialBlend.Cutout or MaterialBlend.Alpha))
            {
                continue;
            }

            ShadeProgram program = plan.Programs[run.Shade];
            foreach (Mipmaps? sheet in program.Sheets)
            {
                if (sheet is not null && (sheet.Width > _largest || sheet.Height > _largest))
                {
                    why = $"a material's texture is {sheet.Width} x {sheet.Height}, past the {_largest} this card takes";
                    return false;
                }

                if (sheet is not null && Waiting(sheet))
                {
                    why = "a material's texture this card cannot read as blocks is still being unpacked";
                    return false;
                }
            }

            if (_programs.Of(program, Pass(run, scene.Light), out bool pending, out string failed) is null)
            {
                if (!pending)
                {
                    why = failed;
                    return false;
                }

                coming++;
            }
        }

        if (coming > 0)
        {
            why = coming == 1 ? "a material's shader is still compiling for the card" : $"{coming} material shaders still compiling for the card";
            return false;
        }

        _canFor = (mesh, scene.Skin, scene.Skins, scene.Blends, scene.Shades, lit, _programs.Landed);
        why = string.Empty;
        return true;
    }

    /// <summary>
    /// Draws a picture into a target - on the overlay's render thread only, once <see cref="Can"/> has said yes.
    /// </summary>
    /// <param name="target">Where, made by <see cref="Target"/>.</param>
    /// <param name="scene">What.</param>
    /// <param name="why">Why nothing was drawn, or empty.</param>
    /// <returns>False where the picture could not be drawn; the target then holds nothing to show.</returns>
    public bool Draw(ModelTarget target, in ModelScene scene, out string why)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _draws++;
        ID3D11DeviceContext context = _context;
        context.ClearRenderTargetView(target.AccumulatedTarget, new Color4(0f, 0f, 0f, 0f));
        context.ClearDepthStencilView(target.Depth, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 0f, 0);

        SkinnedMesh mesh = scene.Mesh;
        MeshPicture.Camera camera = scene.Pose is { } looked
            ? MeshPicture.Camera.Of(mesh, looked)
            : MeshPicture.Camera.Of(mesh, scene.Turn, scene.Tilt, scene.Zoom, scene.Pan);
        if (mesh is null || !camera.Ready)
        {
            Resolved(target);
            why = string.Empty;
            return true;
        }

        MeshBuffers buffers = Buffers(mesh);
        Vector3[] positions = mesh.Positions;
        CardSkin? skin = Skinnable(scene.Skinned, mesh);
        bool posed = skin is null && scene.Positions is { } moved && moved.Length == mesh.Positions.Length
            && scene.Normals is { } turned && turned.Length == mesh.Normals.Length;

        // WHICH VERTICES THE PICTURE IS OF, for the shadow kept from them: nought the mesh's own, a pose
        // by its key, and -1 a pose nothing is known of - never kept.
        long pose = 0;
        if (skin is not null)
        {
            pose = skin.Key > 0 ? skin.Key : -1;
        }
        else if (posed)
        {
            buffers.Pose(_device, context, scene.Positions!, scene.Normals!, scene.PoseFrom, scene.PoseKey);
            pose = scene.PoseKey > 0 ? scene.PoseKey : -1;
        }

        Plan plan = buffers.Planned(scene);
        Vector3[] placed = posed ? scene.Positions! : positions;
        _swayed = plan.Sways ? scene.Swayed : null;
        _partHeld = null;
        SceneLight? light = scene.Light;
        float lift = light?.WaterLift ?? 0f;

        // THE FIELD WAVES, THE CARD'S ALONE (ShadeProgram.Field): their crest lifts the water whatever the lift, so the water is drawn
        // lifted - its vertices moved and as one surface - wherever they are on.
        bool crash = light?.WaterCrash ?? false;
        bool lifting = lift > 0f || crash;

        // EVERY PROGRAM'S SHADER IN HAND BEFORE ANYTHING IS DRAWN, so a picture is never half the card's.
        if (_runShaders.Length < plan.Runs.Length)
        {
            _runShaders = new ID3D11PixelShader?[plan.Runs.Length];
        }

        // KEPT FOR THE SAME PLAN, light and landed programs - a room's thousands of runs looked up once.
        ID3D11PixelShader?[] shaders = _runShaders;
        if (_runShadersFor is not { } held || !ReferenceEquals(held.Plan, plan) || held.Lit != (light is not null) || held.Landed != _programs.Landed)
        {
            _runShadersFor = null;
            Array.Clear(shaders, 0, plan.Runs.Length);
            for (var at = 0; at < plan.Runs.Length; at++)
            {
                Run run = plan.Runs[at];
                if (run.Shade >= 0 && run.Blend is MaterialBlend.Opaque or MaterialBlend.Cutout or MaterialBlend.Alpha)
                {
                    shaders[at] = _programs.Of(plan.Programs[run.Shade], Pass(run, light), out _, out string failed);
                    if (shaders[at] is null)
                    {
                        Resolved(target);
                        why = failed.Length > 0 ? failed : "a material's shader is still compiling for the card";
                        return false;
                    }
                }
            }

            _runShadersFor = (plan, light is not null, _programs.Landed);
        }

        // MESHPICTURE'S CAMERA ON THE CARD'S CLIP SPACE: a point the processor puts at pixel
        // (x, y) - its view x and y times the scale, plus the centre, in shares of the side - lands
        // at clip ((2x/side) - 1, 1 - (2y/side)), so a vertex is at the same place to the bit. Depth
        // is the view's z, nearer MORE (see _writes), laid into nought to one over four times the box's
        // diagonal - and as far again as the focus stands off the box's middle - so a pose reaching past its
        // box is not clipped. IN PERSPECTIVE the same over w, the view's z: x and y as the processor
        // divides them, and the depth the near plane's over the view's z - one at the near plane, nought
        // at no end, the hardware cutting what lies nearer.
        float scale = camera.Scale;
        Vector2 centre = camera.Centre;
        float reach = (4f * MathF.Max((mesh.Most - mesh.Least).Length(), 1e-3f))
            + (camera.Perspective ? 0f : MathF.Abs(Vector3.Transform((mesh.Least + mesh.Most) * 0.5f, camera.View).Z));
        Matrix4x4 onto = camera.Perspective
            ? new Matrix4x4(
                2f * scale, 0f, 0f, 0f,
                0f, -2f * scale, 0f, 0f,
                (2f * centre.X) - 1f, 1f - (2f * centre.Y), 0f, 1f,
                0f, 0f, camera.Near, 0f)
            : new Matrix4x4(
                2f * scale, 0f, 0f, 0f,
                0f, -2f * scale, 0f, 0f,
                0f, 0f, -0.5f / reach, 0f,
                (2f * centre.X) - 1f, 1f - (2f * centre.Y), 0.5f, 1f);
        Vector3 ink = scene.Ink == default ? MeshPicture.UsualInk : scene.Ink;
        Matrix4x4 view = camera.View;
        float ambientLight = SceneLight.UsualFlatAmbient;
        Written(_frame, new FrameConstants
        {
            Clip = view * onto,
            View = view,
            Lamp = new Vector4(MeshPicture.Lamp, 0f),
            Ink = new Vector4(ink, 0.5f),
            Shade = new Vector4(
                MeshPicture.Ambient, ambientLight, 1f - ambientLight, GlossLight.Fresnel(Math.Clamp(Vector3.Dot(ToEye, Halfway), 0f, 1f))),
            Halfway = new Vector4(Halfway, 0f),
            Clock = new Vector4(scene.Time, 0f, 0f, 0f),

            // A POINT'S DEPTH IS THE VIEW'S THIRD COLUMN - ShadeProgram.Eye, as MeshPicture hands it.
            Eye = new Vector4(view.M13, view.M23, view.M33, view.M43),
            Depth = new Vector4(reach, camera.Near, camera.Perspective ? 1f : 0f, 0f),
            Viewer = camera.Perspective ? new Vector4(camera.Viewer, 1f) : default,
            Dust = new Vector4(scene.Dust ?? EnvironmentSettings.AssumedDust, 0f),
            Water = (scene.Water ?? WaterLook.EngineDefault).ColourRegister,
            Flow = (scene.Water ?? WaterLook.EngineDefault).FlowRegister with { W = lift },
            Wind = (scene.Water ?? WaterLook.EngineDefault).WindRegister,
            Shore = (scene.Water ?? WaterLook.EngineDefault).ShoreRegister,
            Swell = (scene.Water ?? WaterLook.EngineDefault).SwellRegister with { W = !crash ? 0f : light!.WaterBreakByGround ? 2f : 1f },
            WaterLit = new Vector4((scene.Water ?? WaterLook.EngineDefault).SubsurfaceScattering, 0f, 0f, 0f),
        });

        context.RSSetState(_raster);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetInputLayout(_layout);
        context.IASetVertexBuffer(0, posed ? buffers.PosedPositions! : buffers.Positions, 12, 0);
        context.IASetVertexBuffer(1, posed ? buffers.PosedNormals! : buffers.Normals, 12, 0);
        context.IASetVertexBuffer(2, buffers.Coordinates, 8, 0);
        context.IASetVertexBuffer(3, buffers.Colours, 4, 0);
        context.IASetIndexBuffer(buffers.Indices, Format.R32_UInt, 0);
        context.VSSetConstantBuffer(0, _frame);
        context.PSSetConstantBuffer(0, _frame);
        context.PSSetConstantBuffer(1, _part);

        // THE PART TO THE VERTEX SHADER TOO: a swaying run's matrix is applied to its vertices there.
        context.VSSetConstantBuffer(1, _part);

        // AND THE BONES, for the picture and the sun's map alike - see Skinned.
        Skinned(skin);
        context.PSSetSampler(0, scene.Sharp ? _sharp : _wrap);
        context.PSSetShaderResource(1, _tables.View);

        // THE RIPPLE TEXTURE, THE SWELL'S AND THE ROOM'S SHORE the waves read, in the pixel and the vertex shader alike -
        // the first two made again should the engine's own have been handed over since (ShadeProgram.Use).
        Sheets();
        context.PSSetShaderResource(ModelShaders.RippleSlot, _rippled);
        context.VSSetShaderResource(ModelShaders.RippleSlot, _rippled);
        context.PSSetShaderResource(ModelShaders.SwellSlot, _swelled);
        context.VSSetShaderResource(ModelShaders.SwellSlot, _swelled);
        context.PSSetShaderResource(ModelShaders.BeachProfileSlot, _beachProfiled);
        context.VSSetShaderResource(ModelShaders.BeachProfileSlot, _beachProfiled);
        context.PSSetShaderResource(ModelShaders.BeachNoiseSlot, _beachNoised);
        context.VSSetShaderResource(ModelShaders.BeachNoiseSlot, _beachNoised);
        context.PSSetShaderResource(ModelShaders.FieldProfileSlot, _fieldProfiled);
        context.VSSetShaderResource(ModelShaders.FieldProfileSlot, _fieldProfiled);

        // THE FIELD WAVES' FOAM, uploaded the first time the waves are drawn with them on - two sheets with their levels.
        bool fielded = crash && Array.Exists(plan.Programs, one => one.HasWaves);
        context.PSSetShaderResource(ModelShaders.FieldFoamColourSlot, fielded ? Skin(ShadeProgram.FieldFoamColour).View : null!);
        context.PSSetShaderResource(ModelShaders.FieldFoamNormalSlot, fielded ? Skin(ShadeProgram.FieldFoamNormal).View : null!);
        ID3D11ShaderResourceView? shoreView = scene.Water?.Shore is { } shore ? Shored(shore).View : null;
        context.PSSetShaderResource(ModelShaders.ShoreSlot, shoreView!);
        context.VSSetShaderResource(ModelShaders.ShoreSlot, shoreView!);

        // THE SUN'S SHADOW MAP BEFORE THE PICTURE, into its own target - see Lit.
        if (light is not null)
        {
            Lit(light, buffers, plan, view, placed, _swayed is null ? pose : -1, skin?.From ?? (posed ? scene.PoseFrom : 0), skin?.Hull);
        }

        context.OMSetRenderTargets(target.AccumulatedTarget, target.Depth);
        context.RSSetViewport(new Viewport(0f, 0f, target.Size, target.Size, 0f, 1f));
        context.VSSetShader(_placed);

        // THE SOLID AND CUT-OUT SHAPES FIRST, in the mesh's order, writing depth - then the translucent
        // ones in the mesh's order, testing it and writing none, as MeshPicture.Drawing.Band does.
        context.OMSetBlendState(_covers);
        context.OMSetDepthStencilState(_writes);
        ID3D11PixelShader? current = null;
        for (var at = 0; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (run.Blend is MaterialBlend.Opaque or MaterialBlend.Cutout)
            {
                ID3D11PixelShader shader = shaders[at] ?? (light is null ? _solid : _scened);
                if (!ReferenceEquals(shader, current))
                {
                    context.PSSetShader(shader);
                    current = shader;
                }

                Drawn(run, plan);
            }
        }

        // THE BEACH OVER THE SOLID PART, writing its depth - see Laid.
        if (Array.Exists(plan.Runs, one => Beach(one, plan)))
        {
            Laid(context, target, plan, shaders, ref current);
        }

        if (plan.Translucent)
        {
            // THE SOLID DEPTH, KEPT, for a ground layer to measure itself against - see ModelTarget.Behind.
            if (plan.Behind)
            {
                target.Kept(context);
                context.PSSetShaderResource(ModelShaders.BehindSlot, target.Behind);
            }

            // AND ITS COLOUR, where the water reflects the picture itself - see ScreenReflection.
            if (light is { WaterReflection: true, WaterScene: true } && Array.Exists(plan.Programs, one => one.HasWaves))
            {
                context.PSSetShaderResource(ModelShaders.OpaqueSlot, target.Saw(context));
            }

            var owner = -1;
            var lifted = false;

            // THE LIFTED WATER AS ONE SURFACE where its first run comes - see Surfaced. A card that cannot copy its
            // depth back draws it as any mixed shape.
            bool surface = lifting && FeatureLevel >= FeatureLevel.Level_10_1 && Array.Exists(plan.Runs, one => Sea(one, plan));
            var surfaced = false;
            for (var at = 0; at < plan.Runs.Length; at++)
            {
                Run run = plan.Runs[at];
                if (run.Blend is not (MaterialBlend.Alpha or MaterialBlend.Additive) || Beach(run, plan))
                {
                    continue;
                }

                if (surface && Sea(run, plan))
                {
                    if (!surfaced)
                    {
                        surfaced = true;
                        Surfaced(context, target, plan, shaders, at, ref current);
                        owner = -1;
                        lifted = true;
                    }

                    continue;
                }

                // A NUMBER PER SHAPE, 1 to 255, and the stencil cleared when they come round again - the
                // shapes before are done with by then, a shape's triangles being one stretch of the mesh.
                if (run.Owner != owner)
                {
                    owner = run.Owner;
                    if (owner > 0 && owner % 255 == 0)
                    {
                        context.ClearDepthStencilView(target.Depth, DepthStencilClearFlags.Stencil, 1f, 0);
                    }

                    context.OMSetDepthStencilState(_stamped, (owner % 255) + 1);
                }

                context.OMSetBlendState(run.Blend == MaterialBlend.Additive ? _adds : _mixes);
                ID3D11PixelShader shader = shaders[at] ?? (run.Blend == MaterialBlend.Additive ? _added : _mixed);
                if (!ReferenceEquals(shader, current))
                {
                    context.PSSetShader(shader);
                    current = shader;
                }

                // THE WATER LIFTED BY ITS WAVES - MeshPicture.Lifted - where the game light asks for it.
                bool waves = lifting && run.Shade >= 0 && plan.Programs[run.Shade].HasWaves;
                if (waves != lifted)
                {
                    context.VSSetShader(waves ? _placedWaves : _placed);
                    lifted = waves;
                }

                Drawn(run, plan);
            }
        }

        Unbound();
        Resolved(target);
        Forget();
        why = string.Empty;
        return true;
    }

    /// <summary>Whether a run is the beach's - drawn over the solid part (MeshPicture.Drawing's Laid).</summary>
    private static bool Beach(Run run, Plan plan) => run.Blend == MaterialBlend.Alpha && run.Shade >= 0 && plan.Programs[run.Shade].Beaches;

    /// <summary>
    /// The beach's runs over the solid part, as one surface whose nearest crossing writes its depth - MeshPicture.Drawing's Laid, on the card.
    /// </summary>
    /// <remarks>
    /// THE SOLID DEPTH KEPT FIRST (ModelTarget.Behind), for the film to measure its path to; then the walk that draws no colour
    /// writes the nearest crossing into the depth, every run is drawn where its fragment is that crossing, once a pixel by the
    /// stencil - and the depth is left as it is, the beach's, for what is drawn after it to test against and the sea to measure
    /// to once the translucent pass keeps the depth again.
    /// </remarks>
    private void Laid(ID3D11DeviceContext context, ModelTarget target, Plan plan, ID3D11PixelShader?[] shaders, ref ID3D11PixelShader? current)
    {
        target.Kept(context);
        context.PSSetShaderResource(ModelShaders.BehindSlot, target.Behind);
        context.VSSetShader(_placedBeach);
        context.PSSetShader((ID3D11PixelShader)null!);
        current = null;
        context.OMSetBlendState(_unseen);
        context.OMSetDepthStencilState(_writes);
        for (var at = 0; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (Beach(run, plan))
            {
                // ITS OWN SWAY, NOT THE LAST RUN'S: a walk with no pixel shader still reads the part's matrix in its vertices.
                Part(new Vector4(0f, -1f, 0f, 0f), run.Sway);
                context.DrawIndexed(run.Count * 3, run.First * 3, 0);
            }
        }

        context.ClearDepthStencilView(target.Depth, DepthStencilClearFlags.Stencil, 1f, 0);
        context.OMSetDepthStencilState(_surfaced, 1);
        context.OMSetBlendState(_mixes);
        for (var at = 0; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (!Beach(run, plan))
            {
                continue;
            }

            ID3D11PixelShader shader = shaders[at] ?? _mixed;
            if (!ReferenceEquals(shader, current))
            {
                context.PSSetShader(shader);
                current = shader;
            }

            Drawn(run, plan);
        }

        context.ClearDepthStencilView(target.Depth, DepthStencilClearFlags.Stencil, 1f, 0);
        context.VSSetShader(_placed);
    }

    /// <summary>Whether a run is the lifted water's - drawn as one surface where the light lifts it (MeshPicture.Drawing's Surface).</summary>
    private static bool Sea(Run run, Plan plan) => run.Blend == MaterialBlend.Alpha && run.Shade >= 0 && plan.Programs[run.Shade].HasWaves;

    /// <summary>
    /// The lifted water's runs from <paramref name="from"/> on, as one surface: only its nearest crossing of each pixel drawn - MeshPicture.Drawing's Surface, on the card.
    /// </summary>
    /// <remarks>
    /// A WALK THAT DRAWS NO COLOUR writes the nearest crossing in front of the solid into the depth - the same vertex shader as the
    /// drawing after it, so the same depth to the bit - then every run is drawn where its fragment is that crossing, once a pixel by
    /// the stencil, as the processor draws only the triangle that won. Then the solid depth goes back from its copy
    /// (ModelTarget.Behind), stencil and all, for the translucent runs after the water, which test against the solid alone as the
    /// processor's do.
    /// </remarks>
    private void Surfaced(ID3D11DeviceContext context, ModelTarget target, Plan plan, ID3D11PixelShader?[] shaders, int from, ref ID3D11PixelShader? current)
    {
        if (!plan.Behind)
        {
            target.Kept(context);
        }

        context.VSSetShader(_placedWaves);
        context.PSSetShader((ID3D11PixelShader)null!);
        current = null;
        context.OMSetBlendState(_unseen);
        context.OMSetDepthStencilState(_writes);
        for (int at = from; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (Sea(run, plan))
            {
                Part(new Vector4(0f, -1f, 0f, 0f), run.Sway);
                context.DrawIndexed(run.Count * 3, run.First * 3, 0);
            }
        }

        context.ClearDepthStencilView(target.Depth, DepthStencilClearFlags.Stencil, 1f, 0);
        context.OMSetDepthStencilState(_surfaced, 1);
        context.OMSetBlendState(_mixes);
        for (int at = from; at < plan.Runs.Length; at++)
        {
            Run run = plan.Runs[at];
            if (!Sea(run, plan))
            {
                continue;
            }

            ID3D11PixelShader shader = shaders[at] ?? _mixed;
            if (!ReferenceEquals(shader, current))
            {
                context.PSSetShader(shader);
                current = shader;
            }

            Drawn(run, plan);
        }

        // THE DEPTH TAKEN OFF THE OUTPUT WHILE IT IS WRITTEN BACK, and bound again.
        context.OMSetRenderTargets(target.AccumulatedTarget, null);
        target.Back(context);
        context.OMSetRenderTargets(target.AccumulatedTarget, target.Depth);
    }

    /// <summary>
    /// The finished picture's bytes, read back from the card - for tests and for anything that needs the pixels.
    /// </summary>
    public byte[] Read(ModelTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        int size = target.Size;
        using ID3D11Texture2D staging = _device.CreateTexture2D(new Texture2DDescription(
            Format.R8G8B8A8_UNorm, size, size, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        _context.CopyResource(staging, target.ShownTexture);
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var bytes = new byte[size * size * 4];
        try
        {
            for (var row = 0; row < size; row++)
            {
                Marshal.Copy(mapped.DataPointer + (row * mapped.RowPitch), bytes, row * size * 4, size * 4);
            }
        }
        finally
        {
            _context.Unmap(staging, 0);
        }

        return bytes;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (MeshBuffers one in _meshes.Values)
        {
            one.Dispose();
        }

        foreach (SkinTexture one in _skins.Values)
        {
            one.Dispose();
        }

        foreach (WeightBuffers one in _weights.Values)
        {
            one.Dispose();
        }

        _meshes.Clear();
        _skins.Clear();
        _weights.Clear();
        _bones.Dispose();
        _programs.Dispose();
        DisposeLight();
        _raster.Dispose();
        _surfaced.Dispose();
        _ignores.Dispose();
        _stamped.Dispose();
        _writes.Dispose();
        _unseen.Dispose();
        _least.Dispose();
        _adds.Dispose();
        _mixes.Dispose();
        _covers.Dispose();
        _sharp.Dispose();
        _wrap.Dispose();
        _scene.Dispose();
        _part.Dispose();
        _frame.Dispose();
        _layout.Dispose();
        _straight.Dispose();
        _whole.Dispose();
        _away.Dispose();
        _casting.Dispose();
        _scened.Dispose();
        _added.Dispose();
        _mixed.Dispose();
        _solid.Dispose();
        _rippled.Dispose();
        _ripples.Dispose();
        _swelled.Dispose();
        _swells.Dispose();
        foreach (ShoreTexture one in _shores.Values)
        {
            one.Dispose();
        }

        _shores.Clear();
        _placedWaves.Dispose();
        _placedBeach.Dispose();
        _beachProfiled.Dispose();
        _beachProfile.Dispose();
        _beachNoised.Dispose();
        _beachNoise.Dispose();
        _fieldProfiled.Dispose();
        _fieldProfile.Dispose();
        _placed.Dispose();
        if (_owns)
        {
            _context.Dispose();
            _device.Dispose();
        }
    }

    /// <summary>Which of a program's entry points a run is drawn with.</summary>
    private static ProgramPass Pass(Run run, SceneLight? light) => run.Blend == MaterialBlend.Alpha
        ? light is null ? ProgramPass.Mixed : ProgramPass.MixedScened
        : light is null ? ProgramPass.Solid : ProgramPass.SolidScened;

    /// <summary>One run of triangles with its texture, the cut-out's threshold, and its program's sheets.</summary>
    private void Drawn(Run run, Plan plan)
    {
        Mipmaps? worn = plan.Palette[run.Wears];
        ID3D11ShaderResourceView? view = worn is null ? null : Skin(worn).View;
        _context.PSSetShaderResource(0, view!);
        if (run.Shade >= 0)
        {
            IReadOnlyList<Mipmaps?> sheets = plan.Programs[run.Shade].Sheets;
            for (var sheet = 0; sheet < sheets.Count; sheet++)
            {
                _context.PSSetShaderResource(ModelShaders.FirstSheet + sheet, sheets[sheet] is { } one ? Skin(one).View : null!);
            }

            _sheetsBound = Math.Max(_sheetsBound, sheets.Count);
        }

        Part(new Vector4(view is null ? 0f : 1f, run.Blend == MaterialBlend.Cutout ? MeshPicture.CutoutAlpha : -1f, 0f, 0f), run.Sway);
        _context.DrawIndexed(run.Count * 3, run.First * 3, 0);
    }

    /// <summary>
    /// The part constants a run is drawn with, written only where they change - a room's runs mostly ask for the same two, and each write was a map of the buffer.
    /// </summary>
    /// <param name="flags">ModelShaders' Part.Flags.</param>
    /// <param name="sway">The run's sway group, or -1 - its matrix this frame's, the identity for none.</param>
    private void Part(Vector4 flags, int sway)
    {
        var value = new PartConstants
        {
            Flags = flags,
            Sway = sway >= 0 && _swayed is { } swayed && sway < swayed.Length ? swayed[sway] : Matrix4x4.Identity,
        };

        if (_partHeld is not { } held || held.Flags != value.Flags || held.Sway != value.Sway)
        {
            Written(_part, value);
            _partHeld = value;
        }
    }

    /// <summary>The most bones the card skins by: a vertex names its bones in bytes, so no vertex reaches past them.</summary>
    private const int MostBones = ModelShaders.MostBones;

    /// <summary>The bones' constants' size: a row of four ints, then a matrix a bone - ModelShaders' Bones.</summary>
    private const int BonesBytes = 16 + (MostBones * 64);

    /// <summary>
    /// A skin the card can draw for this mesh, or null: its weighted mesh ready, its vertices within the picture's, and some bone or weight to move them by.
    /// </summary>
    private static CardSkin? Skinnable(CardSkin? skin, SkinnedMesh mesh) =>
        skin is { Weighted: { Ready: true } weighted } && skin.From >= 0 && skin.Matrices is not null
        && skin.From + weighted.Positions.Length <= mesh.Positions.Length
        && weighted.Bones.Length > 0 && weighted.Weights.Length > 0
            ? skin
            : null;

    /// <summary>
    /// The bones the vertex shaders skin by, bound: this frame's matrices written where the pose moved, the weighted mesh's bones and weights uploaded the first time it is drawn - or, with no skin, the word that the card skins nothing.
    /// </summary>
    /// <remarks>
    /// ONE MAP A MOVED POSE, of the bones the rig has and no more: a boss's rig is a hundred-odd bones, seven kilobytes - where a
    /// pose the processor moved sent every vertex and normal of him, megabytes, every frame it played.
    /// </remarks>
    private unsafe void Skinned(CardSkin? skin)
    {
        ID3D11DeviceContext context = _context;
        context.VSSetConstantBuffer(3, _bones);
        if (skin is null)
        {
            if (!_bonesWritten || _bonesHeld is not null)
            {
                MappedSubresource cleared = context.Map(_bones, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                new Span<int>((void*)cleared.DataPointer, 4).Clear();
                context.Unmap(_bones, 0);
                _bonesHeld = null;
                _bonesWritten = true;
            }

            context.VSSetShaderResource(ModelShaders.SkinBonesSlot, null!);
            context.VSSetShaderResource(ModelShaders.SkinWeightsSlot, null!);
            return;
        }

        int bones = Math.Min(Math.Min(skin.Bones, skin.Matrices.Length), MostBones);
        int count = skin.Weighted.Positions.Length;
        (Matrix4x4[], long, int, int, int) held = (skin.Matrices, skin.Key, skin.From, bones, count);
        if (!_bonesWritten || skin.Key <= 0 || _bonesHeld is not { } was || was != held)
        {
            MappedSubresource mapped = context.Map(_bones, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            var header = (int*)mapped.DataPointer;
            header[0] = 1;
            header[1] = skin.From;
            header[2] = bones;
            header[3] = count;
            skin.Matrices.AsSpan(0, Math.Max(0, bones)).CopyTo(new Span<Matrix4x4>((byte*)mapped.DataPointer + 16, Math.Max(0, bones)));
            context.Unmap(_bones, 0);
            _bonesHeld = held;
            _bonesWritten = true;
        }

        WeightBuffers weights = Weighted(skin.Weighted);
        context.VSSetShaderResource(ModelShaders.SkinBonesSlot, weights.Bones.View);
        context.VSSetShaderResource(ModelShaders.SkinWeightsSlot, weights.Weights.View);
    }

    /// <summary>A skinned mesh's bones and weights, uploaded on first use.</summary>
    private WeightBuffers Weighted(SkinnedMesh mesh)
    {
        if (!_weights.TryGetValue(mesh, out WeightBuffers? weights))
        {
            weights = new WeightBuffers(_device, mesh);
            _weights[mesh] = weights;
        }

        weights.Used = _draws;
        return weights;
    }

    /// <summary>
    /// What the drawing bound for reading, taken off again - the shadow map and the target's depth copy are written the next time round.
    /// </summary>
    private void Unbound()
    {
        int last = ModelShaders.FirstSheet + _sheetsBound;
        for (var slot = 1; slot < last; slot++)
        {
            _context.PSSetShaderResource(slot, null!);
        }

        _sheetsBound = 0;
    }

    /// <summary>The float target turned into the straight bytes ImGui shows - see ModelShaders.Resolve.</summary>
    private void Resolved(ModelTarget target)
    {
        ID3D11DeviceContext context = _context;
        context.OMSetRenderTargets(target.ShownTarget, null);
        context.RSSetViewport(new Viewport(0f, 0f, target.Size, target.Size, 0f, 1f));
        context.RSSetState(_raster);
        context.OMSetBlendState(_covers);
        context.OMSetDepthStencilState(_ignores);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_whole);
        context.PSSetShader(_straight);
        context.PSSetShaderResource(0, target.AccumulatedView);
        context.Draw(3, 0);

        // UNBOUND AGAIN, so the float target can be drawn into on the next frame - a texture bound
        // for reading is taken off as a target by the runtime, with a warning, and the other way round.
        context.PSSetShaderResource(0, null!);
        context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
    }

    /// <summary>A constant buffer's contents replaced.</summary>
    private unsafe void Written<T>(ID3D11Buffer buffer, T value)
        where T : unmanaged
    {
        MappedSubresource mapped = _context.Map(buffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        *(T*)mapped.DataPointer = value;
        _context.Unmap(buffer, 0);
    }

    /// <summary>A room's shore on the card, uploaded on first use.</summary>
    private ShoreTexture Shored(ShoreField shore)
    {
        if (!_shores.TryGetValue(shore, out ShoreTexture? texture))
        {
            texture = new ShoreTexture(_device, shore);
            _shores[shore] = texture;
        }

        texture.Used = _draws;
        return texture;
    }

    /// <summary>A mesh's buffers, uploaded on first use.</summary>
    private MeshBuffers Buffers(SkinnedMesh mesh)
    {
        if (!_meshes.TryGetValue(mesh, out MeshBuffers? buffers))
        {
            buffers = new MeshBuffers(_device, mesh);
            _meshes[mesh] = buffers;
        }

        buffers.Used = _draws;
        return buffers;
    }

    /// <summary>
    /// Whether a texture goes to the card as the file's blocks: a format the card samples, and a top level a whole number of blocks
    /// each way, which Direct3D asks of a block-compressed texture.
    /// </summary>
    private bool Blocked(Mipmaps skin) =>
        skin.Blocks is { } blocks && _readsBlocks[(int)blocks.Format] && blocks.Width % 4 == 0 && blocks.Height % 4 == 0;

    /// <summary>Whether a texture would have to be decoded for the card and is not yet - see Can.</summary>
    /// <remarks>
    /// NOT DECODED ON THE DRAWING THREAD: a card below feature level 11 reads no BC7, and a stage's BC7s are seconds of decoding; the
    /// picture waits for the unpacking behind the load instead (MonsterPortrait), as the processor's does.
    /// </remarks>
    private bool Waiting(Mipmaps skin) => !skin.Unpacked && !Blocked(skin);

    /// <summary>The card's format for a block format - unsigned and normalised, as every texture here is read: no sRGB conversion.</summary>
    private static Format CardFormat(BlockFormat format) => format switch
    {
        BlockFormat.Bc1 => Format.BC1_UNorm,
        BlockFormat.Bc2 => Format.BC2_UNorm,
        BlockFormat.Bc3 => Format.BC3_UNorm,
        BlockFormat.Bc4 => Format.BC4_UNorm,
        BlockFormat.Bc5 => Format.BC5_UNorm,
        _ => Format.BC7_UNorm,
    };

    /// <summary>Which block formats this card samples as 2D textures with levels, by BlockFormat - asked of the device once.</summary>
    private static bool[] ReadsBlocks(ID3D11Device device)
    {
        BlockFormat[] formats = Enum.GetValues<BlockFormat>();
        var reads = new bool[formats.Length];
        foreach (BlockFormat one in formats)
        {
            FormatSupport support = device.CheckFormatSupport(CardFormat(one));
            const FormatSupport Wanted = FormatSupport.Texture2D | FormatSupport.ShaderSample | FormatSupport.Mip;
            reads[(int)one] = (support & Wanted) == Wanted;
        }

        return reads;
    }

    /// <summary>How many textures went to the card as the file's blocks - see <see cref="Blocked"/>; for the tests.</summary>
    internal long BlockUploads { get; private set; }

    /// <summary>A texture with all its levels, uploaded on first use.</summary>
    private SkinTexture Skin(Mipmaps skin)
    {
        if (!_skins.TryGetValue(skin, out SkinTexture? texture))
        {
            bool blocked = Blocked(skin);
            texture = new SkinTexture(_device, skin, blocked);
            _skins[skin] = texture;
            BlockUploads += blocked ? 1 : 0;
        }

        texture.Used = _draws;
        return texture;
    }

    /// <summary>Lets go of what has not been drawn for <see cref="Kept"/> draws.</summary>
    private void Forget()
    {
        if (_draws % 64 != 0)
        {
            return;
        }

        foreach (KeyValuePair<SkinnedMesh, MeshBuffers> one in _meshes.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _meshes.Remove(one.Key);

            // NOR KEPT BY THE ANSWERS about it - a stage is millions of vertices.
            _canFor = null;
            _runShadersFor = null;
        }

        foreach (KeyValuePair<SkinnedMesh, WeightBuffers> one in _weights.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _weights.Remove(one.Key);
        }

        foreach (KeyValuePair<ShoreField, ShoreTexture> one in _shores.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _shores.Remove(one.Key);
        }

        foreach (KeyValuePair<Mipmaps, SkinTexture> one in _skins.Where(one => _draws - one.Value.Used > Kept).ToArray())
        {
            one.Value.Dispose();
            _skins.Remove(one.Key);
        }

        ForgetLight();
    }

    /// <summary>Every texture a picture may read.</summary>
    private static IEnumerable<Mipmaps?> Worn(ModelScene scene)
    {
        yield return scene.Skin;
        if (scene.Skins is { } skins)
        {
            foreach (Mipmaps? one in skins)
            {
                yield return one;
            }
        }
    }

    private static Compiled? Compile(ID3D11Device device, out string why)
    {
        (string Source, string Entry, string Profile)[] wanted =
        [
            (ModelShaders.Model, "Placed", "vs_4_0"),
            (ModelShaders.Model, "PlacedWaves", "vs_4_0"),
            (ModelShaders.Model, "Solid", "ps_4_0"),
            (ModelShaders.Model, "Mixed", "ps_4_0"),
            (ModelShaders.Model, "Added", "ps_4_0"),
            (ModelShaders.Model, "Scened", "ps_4_0"),
            (ModelShaders.Model, "Casting", "vs_4_0"),
            (ModelShaders.Model, "Away", "ps_4_0"),
            (ModelShaders.Resolve, "Whole", "vs_4_0"),
            (ModelShaders.Resolve, "Straight", "ps_4_0"),
            (ModelShaders.Model, "PlacedBeach", "vs_4_0"),
        ];

        var blobs = new Blob?[wanted.Length];
        try
        {
            why = string.Empty;
            for (var at = 0; at < wanted.Length; at++)
            {
                blobs[at] = Compiled.Code(wanted[at].Source, wanted[at].Entry, wanted[at].Profile, out why);
                if (blobs[at] is null)
                {
                    return null;
                }
            }

            InputElementDescription[] corners =
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerVertexData, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 0, 2, InputClassification.PerVertexData, 0),
                new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 0, 3, InputClassification.PerVertexData, 0),
            ];

            return new Compiled(
                device.CreateVertexShader(blobs[0]!),
                device.CreateVertexShader(blobs[1]!),
                device.CreatePixelShader(blobs[2]!),
                device.CreatePixelShader(blobs[3]!),
                device.CreatePixelShader(blobs[4]!),
                device.CreatePixelShader(blobs[5]!),
                device.CreateVertexShader(blobs[6]!),
                device.CreatePixelShader(blobs[7]!),
                device.CreateVertexShader(blobs[8]!),
                device.CreatePixelShader(blobs[9]!),
                device.CreateInputLayout(corners, blobs[0]!),
                device.CreateVertexShader(blobs[10]!));
        }
        finally
        {
            foreach (Blob? one in blobs)
            {
                one?.Dispose();
            }
        }
    }

    /// <summary>A run's constants - ModelShaders' Part, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PartConstants
    {
        public Vector4 Flags;
        public Matrix4x4 Sway;
    }

    /// <summary>The frame's constants - ModelShaders' Frame, field for field, sixteen-byte rows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 Clip;
        public Matrix4x4 View;
        public Vector4 Lamp;
        public Vector4 Ink;
        public Vector4 Shade;
        public Vector4 Halfway;
        public Vector4 Clock;
        public Vector4 Eye;
        public Vector4 Depth;
        public Vector4 Dust;
        public Vector4 Water;
        public Vector4 Flow;
        public Vector4 Wind;
        public Vector4 Shore;
        public Vector4 Swell;
        public Vector4 WaterLit;
        public Vector4 Viewer;
    }

    private sealed record Compiled(
        ID3D11VertexShader Placed,
        ID3D11VertexShader PlacedWaves,
        ID3D11PixelShader Solid,
        ID3D11PixelShader Mixed,
        ID3D11PixelShader Added,
        ID3D11PixelShader Scened,
        ID3D11VertexShader Casting,
        ID3D11PixelShader Away,
        ID3D11VertexShader Whole,
        ID3D11PixelShader Straight,
        ID3D11InputLayout Layout,
        ID3D11VertexShader PlacedBeach)
    {
        /// <summary>One entry point compiled, or null and the compiler's words.</summary>
        public static Blob? Code(string source, string entry, string profile, out string why)
        {
            Result result = Compiler.Compile(source, entry, entry, profile, out Blob blob, out Blob errors);
            try
            {
                if (result.Failure || blob is null)
                {
                    why = $"the {entry} shader would not compile: "
                        + (errors is null ? result.ToString() : Marshal.PtrToStringAnsi(errors.BufferPointer) ?? result.ToString());
                    blob?.Dispose();
                    return null;
                }

                why = string.Empty;
                return blob;
            }
            finally
            {
                errors?.Dispose();
            }
        }
    }

    /// <summary>A stretch of triangles in the mesh's order that wear one texture, blend one way, run one program and belong, where translucent, to one shape.</summary>
    /// <param name="First">The first triangle.</param>
    /// <param name="Count">How many.</param>
    /// <param name="Wears">Its texture, in the plan's palette.</param>
    /// <param name="Blend">How it is put over what is behind.</param>
    /// <param name="Shade">Its program, in the plan's list, or below nought for none.</param>
    /// <param name="Owner">Its shape's number among the translucent ones, or below nought for a solid run.</param>
    private readonly record struct Run(int First, int Count, int Wears, MaterialBlend Blend, int Shade = -1, int Owner = -1, int Sway = -1);

    /// <summary>A picture's runs, textures and programs, worked out once per mesh and lists - and the runs that cast the sun's shadow.</summary>
    /// <param name="Runs">Every run in the mesh's order, the shadow-only ones among them - the drawing's passes take the blends they draw.</param>
    /// <param name="Casts">What casts a shadow - see <see cref="Casting"/>.</param>
    /// <param name="Palette">The textures, none at nought.</param>
    /// <param name="Programs">The programs the runs run, as MeshPicture.Programmed finds them.</param>
    /// <param name="Translucent">Whether any run is mixed or added.</param>
    /// <param name="Behind">Whether a mixed run's program measures against the solid depth behind - see ModelTarget.Behind.</param>
    /// <param name="Sways">Whether any run sways - its vertices turned in the vertex shader each frame, and cast again each frame.</param>
    private sealed record Plan(Run[] Runs, Run[] Casts, Mipmaps?[] Palette, ShadeProgram[] Programs, bool Translucent, bool Behind, bool Sways = false);

    /// <summary>A mesh on the card: its vertices, normals, coordinates, colours and indices, and a pose's where it moves.</summary>
    private sealed class MeshBuffers : IDisposable
    {
        private (Mipmaps? Skin, IReadOnlyList<Mipmaps?>? Skins, IReadOnlyList<MaterialBlend>? Blends, IReadOnlyList<ShadeProgram?>? Shades, IReadOnlyList<int>? Sways, Plan Plan)? _planned;
        private readonly SkinnedMesh _mesh;
        private readonly bool _coordinated;

        public MeshBuffers(ID3D11Device device, SkinnedMesh mesh)
        {
            _mesh = mesh;
            _coordinated = mesh.Coordinated;
            Positions = device.CreateBuffer<Vector3>(mesh.Positions, BindFlags.VertexBuffer, ResourceUsage.Immutable);
            Normals = device.CreateBuffer<Vector3>(mesh.Normals, BindFlags.VertexBuffer, ResourceUsage.Immutable);
            Coordinates = device.CreateBuffer<Vector2>(mesh.Coordinates, BindFlags.VertexBuffer, ResourceUsage.Immutable);
            Colours = device.CreateBuffer<byte>(Tints(mesh), BindFlags.VertexBuffer, ResourceUsage.Immutable);
            Indices = device.CreateBuffer<int>(mesh.Indices, BindFlags.IndexBuffer, ResourceUsage.Immutable);
        }

        public long Used { get; set; }

        public ID3D11Buffer Positions { get; }

        public ID3D11Buffer Normals { get; }

        public ID3D11Buffer Coordinates { get; }

        /// <summary>Four bytes of colour a vertex - see <see cref="Tints"/>.</summary>
        public ID3D11Buffer Colours { get; }

        public ID3D11Buffer Indices { get; }

        public ID3D11Buffer? PosedPositions { get; private set; }

        public ID3D11Buffer? PosedNormals { get; private set; }

        /// <summary>The sun's shadow map last drawn for this mesh - see ModelGpu.Lit.</summary>
        public ShadowTarget? Shadow { get; set; }

        /// <summary>
        /// What <see cref="Shadow"/> was drawn for, kept while the vertices hold still - the mesh's own (pose nought) or one pose by its key; the canvas's rule - and the casters are the same runs.
        /// </summary>
        public (Vector3 Direction, int Side, ShadowFrame Frame, long Pose, Plan Plan)? ShadowFor { get; set; }

        /// <summary>The stage's own shadow, without the monster posed on it - see ModelGpu.Staged.</summary>
        public ShadowTarget? StageShadow { get; set; }

        /// <summary>What <see cref="StageShadow"/> was drawn for: the runs, their frame and the first triangle that moves.</summary>
        public (Plan Plan, ShadowFrame Frame, int Split)? StageShadowFor { get; set; }

        /// <summary>The arrays and key last sent to the pose's buffers - see <see cref="Pose"/>.</summary>
        private (Vector3[] Positions, Vector3[] Normals, long Key)? _sent;

        private (int From, int Split)? _split;
        private (Vector3 Direction, int From, ShadowBounds Bounds)? _stageBounds;
        private (Plan Plan, int Split, Run[] Stage, Run[] Moving)? _parted;

        /// <summary>
        /// A pose's vertices and normals on the card: whole the first time and for new arrays, the moved part alone for the same arrays again, and nothing where their key says nothing moved.
        /// </summary>
        /// <returns>Whether anything was sent.</returns>
        /// <remarks>
        /// A STAGE IS MILLIONS OF VERTICES AND THE MONSTER ON IT TENS OF THOUSANDS: both arrays were sent
        /// whole every frame - 55 megabytes a frame at Port's harbour - for the slice that alone had moved.
        /// The buffers are the card's own (default usage), made from the arrays, and a frame writes the
        /// slice from <paramref name="from"/> on (<see cref="ModelScene.PoseFrom"/>'s promise).
        /// </remarks>
        public bool Pose(ID3D11Device device, ID3D11DeviceContext context, Vector3[] positions, Vector3[] normals, int from, long key)
        {
            if (PosedPositions is null || PosedNormals is null)
            {
                PosedPositions = device.CreateBuffer<Vector3>(positions, BindFlags.VertexBuffer, ResourceUsage.Default);
                PosedNormals = device.CreateBuffer<Vector3>(normals, BindFlags.VertexBuffer, ResourceUsage.Default);
                _sent = (positions, normals, key);
                return true;
            }

            bool same = _sent is { } was && ReferenceEquals(was.Positions, positions) && ReferenceEquals(was.Normals, normals);
            if (same && key != 0 && _sent!.Value.Key == key)
            {
                return false;
            }

            int first = same ? Math.Clamp(from, 0, positions.Length) : 0;
            Sent(context, PosedPositions, positions, first);
            Sent(context, PosedNormals, normals, first);
            _sent = (positions, normals, key);
            return true;
        }

        /// <summary>
        /// The first triangle of those a pose from this vertex on moves, where every triangle before it lies wholly before the vertex and every one from it wholly after - or -1 where the mesh does not part so.
        /// </summary>
        /// <remarks>A stage's monster is joined last (StagedModels), his vertices and triangles both - so a stage parts.</remarks>
        public int Split(int from)
        {
            if (_split is { } had && had.From == from)
            {
                return had.Split;
            }

            int[] indices = _mesh.Indices;
            int triangles = indices.Length / 3, split = -1;
            for (var one = 0; one < triangles; one++)
            {
                int at = one * 3;
                bool moves = indices[at] >= from || indices[at + 1] >= from || indices[at + 2] >= from;
                if (split < 0 && moves)
                {
                    split = one;
                }

                if (split >= 0 && (indices[at] < from || indices[at + 1] < from || indices[at + 2] < from))
                {
                    split = -1;
                    break;
                }
            }

            split = split > 0 ? split : -1;
            _split = (from, split);
            return split;
        }

        /// <summary>The mesh's triangles.</summary>
        public int Triangles => _mesh.Indices.Length / 3;

        /// <summary>The bounds of the stage's own vertices - those before the pose's first - along the light, worked out once a direction.</summary>
        public ShadowBounds StageBounds(int from, Vector3 direction)
        {
            if (_stageBounds is { } had && had.From == from && had.Direction == direction)
            {
                return had.Bounds;
            }

            ShadowBounds bounds = ShadowMap.Bounded(_mesh.Positions.AsSpan(0, Math.Min(from, _mesh.Positions.Length)), direction);
            _stageBounds = (direction, from, bounds);
            return bounds;
        }

        /// <summary>A plan's casting runs parted at a triangle: the stage's before it, the moving ones from it on.</summary>
        public (Run[] Stage, Run[] Moving) Parted(Plan plan, int split)
        {
            if (_parted is { } had && ReferenceEquals(had.Plan, plan) && had.Split == split)
            {
                return (had.Stage, had.Moving);
            }

            var stage = new List<Run>();
            var moving = new List<Run>();
            foreach (Run run in plan.Casts)
            {
                // A SWAYING CASTER MOVES EVERY FRAME, wherever it lies - cast again each time with its matrix.
                if (run.Sway >= 0)
                {
                    moving.Add(run);
                    continue;
                }

                int end = run.First + run.Count;
                if (run.First < split)
                {
                    stage.Add(run with { Count = Math.Min(end, split) - run.First });
                }

                if (end > split)
                {
                    int first = Math.Max(run.First, split);
                    moving.Add(run with { First = first, Count = end - first });
                }
            }

            _parted = (plan, split, [.. stage], [.. moving]);
            return (_parted.Value.Stage, _parted.Value.Moving);
        }

        /// <summary>
        /// Which texture, blend and program every triangle has, as MeshPicture's Palette, Worn, Blended and Programmed work them out, in runs.
        /// </summary>
        /// <remarks>
        /// KEPT WHILE THE LISTS ARE THE SAME, compared by reference as the portrait compares them: a
        /// room is a million triangles, and working this out per frame was per-frame garbage.
        /// </remarks>
        public Plan Planned(in ModelScene scene)
        {
            if (_planned is { } had && ReferenceEquals(had.Skin, scene.Skin) && ReferenceEquals(had.Skins, scene.Skins)
                && ReferenceEquals(had.Blends, scene.Blends) && ReferenceEquals(had.Shades, scene.Shades) && ReferenceEquals(had.Sways, scene.Sways))
            {
                return had.Plan;
            }

            Plan plan = Worked(scene);
            _planned = (scene.Skin, scene.Skins, scene.Blends, scene.Shades, scene.Sways, plan);
            return plan;
        }

        private Plan Worked(in ModelScene scene)
        {
            SkinnedMesh mesh = _mesh;
            int triangles = mesh.Triangles;
            Mipmaps? usable = scene.Skin is not null && _coordinated ? scene.Skin : null;

            // THE PALETTE: none at nought, then the shapes' textures, then the model's own.
            var palette = new List<Mipmaps?> { null };
            if (_coordinated)
            {
                foreach (Mipmaps? one in scene.Skins ?? [])
                {
                    if (one is not null && !palette.Contains(one))
                    {
                        palette.Add(one);
                    }
                }

                if (usable is not null && !palette.Contains(usable))
                {
                    palette.Add(usable);
                }
            }

            var wears = new int[triangles];
            Array.Fill(wears, Math.Max(0, palette.IndexOf(usable)));
            if (scene.Skins is { Count: > 0 } skins && _coordinated)
            {
                for (var shape = 0; shape < mesh.Shapes.Count && shape < skins.Count; shape++)
                {
                    int at = palette.IndexOf(skins[shape]);
                    if (at >= 0)
                    {
                        Fill(wears, mesh.Shapes[shape], at, triangles);
                    }
                }
            }

            var blends = new MaterialBlend[triangles];
            var owners = new int[triangles];
            Array.Fill(owners, -1);
            var translucent = false;
            var owner = 0;
            if (scene.Blends is { Count: > 0 } said)
            {
                for (var shape = 0; shape < mesh.Shapes.Count && shape < said.Count; shape++)
                {
                    if (said[shape] != MaterialBlend.Opaque)
                    {
                        Fill(blends, mesh.Shapes[shape], said[shape], triangles);
                        if (said[shape] is MaterialBlend.Alpha or MaterialBlend.Additive && mesh.Shapes[shape].Count > 0)
                        {
                            translucent = true;
                            Fill(owners, mesh.Shapes[shape], owner++, triangles);
                        }
                    }
                }
            }

            // MESHPICTURE.PROGRAMMED: a shape's program where it is bound and fits the registers, on a
            // mesh with coordinates to run it with - and only where the shape is solid, cut out or mixed.
            var shades = new int[triangles];
            Array.Fill(shades, -1);
            var programs = new List<ShadeProgram>();
            if (scene.Shades is { Count: > 0 } wanted && _coordinated)
            {
                for (var shape = 0; shape < mesh.Shapes.Count && shape < wanted.Count; shape++)
                {
                    if (wanted[shape] is not { Bound: true } program || program.Registers > ShadeProgram.MostRegisters)
                    {
                        continue;
                    }

                    int at = programs.IndexOf(program);
                    if (at < 0)
                    {
                        at = programs.Count;
                        programs.Add(program);
                    }

                    Fill(shades, mesh.Shapes[shape], at, triangles);
                }
            }

            for (var one = 0; one < triangles; one++)
            {
                if (blends[one] is MaterialBlend.Additive or MaterialBlend.ShadowOnly)
                {
                    shades[one] = -1;
                }
            }

            // THE SWAY GROUPS: a ship's shapes turned in the vertex shader by their group's matrix - RoomSway; nought groups, none.
            var sways = new int[triangles];
            Array.Fill(sways, -1);
            var swaying = false;
            if (scene.Sways is { Count: > 0 } swayed)
            {
                for (var shape = 0; shape < mesh.Shapes.Count && shape < swayed.Count; shape++)
                {
                    if (swayed[shape] >= 0)
                    {
                        Fill(sways, mesh.Shapes[shape], swayed[shape], triangles);
                        swaying = true;
                    }
                }
            }

            var runs = new List<Run>();
            var behind = false;
            for (int first = 0, at = 1; first < triangles; at++)
            {
                if (at == triangles || wears[at] != wears[first] || blends[at] != blends[first] || shades[at] != shades[first]
                    || owners[at] != owners[first] || sways[at] != sways[first])
                {
                    runs.Add(new Run(first, at - first, wears[first], blends[first], shades[first], owners[first], sways[first]));
                    behind |= blends[first] == MaterialBlend.Alpha && shades[first] >= 0 && programs[shades[first]].UsesDepth;
                    first = at;
                }
            }

            Mipmaps?[] worn = [.. palette];
            return new Plan([.. runs], Casting(runs, worn, programs), worn, [.. programs], translucent, behind, swaying);
        }

        private static void Fill<T>(T[] into, MeshShape shape, T value, int triangles)
        {
            int from = Math.Clamp(shape.From / 3, 0, triangles);
            int upto = Math.Clamp((shape.From + shape.Count) / 3, from, triangles);
            Array.Fill(into, value, from, upto - from);
        }

        /// <summary>
        /// Every vertex's colour as MeshPicture's VertexColourOf reads it: the mesh's own four bytes, and white where it has none.
        /// </summary>
        private static byte[] Tints(SkinnedMesh mesh)
        {
            int count = mesh.Positions.Length;
            var tints = new byte[Math.Max(1, count) * 4];
            Array.Fill(tints, (byte)255);
            byte[] colours = mesh.Colours;
            for (var vertex = 0; vertex < count; vertex++)
            {
                if (mesh.ColourAt(vertex))
                {
                    Array.Copy(colours, vertex * 4, tints, vertex * 4, 4);
                }
            }

            return tints;
        }

        /// <summary>The vertices from one on, written over the card's copy - a box of the buffer, its bytes from the first's to the end.</summary>
        private static void Sent(ID3D11DeviceContext context, ID3D11Buffer buffer, Vector3[] from, int first)
        {
            if (first < from.Length)
            {
                const int stride = 12;
                context.UpdateSubresource<Vector3>(
                    from.AsSpan(first), buffer, 0, 0, 0, new Box(first * stride, 0, 0, from.Length * stride, 1, 1));
            }
        }

        public void Dispose()
        {
            StageShadow?.Dispose();
            Shadow?.Dispose();
            PosedNormals?.Dispose();
            PosedPositions?.Dispose();
            Indices.Dispose();
            Colours.Dispose();
            Coordinates.Dispose();
            Normals.Dispose();
            Positions.Dispose();
        }
    }

    /// <summary>A water lookup on the card - the ripple texture or the swell's cross-section - its texels as they are, read with loads.</summary>
    private static (ID3D11Texture2D Texture, ID3D11ShaderResourceView View) Sheet(ID3D11Device device, WaterSheet sheet, Format format) =>
        Floats(device, sheet.Texels, sheet.Width, sheet.Height, format, sheet.Channels);

    /// <summary>The ripple texture and the swell's made again where ShadeProgram reads another since they were made - two reference checks a drawing.</summary>
    private void Sheets()
    {
        WaterSheet ripples = ShadeProgram.Ripples;
        if (!ReferenceEquals(ripples, _ripplesOf))
        {
            _rippled.Dispose();
            _ripples.Dispose();
            (_ripples, _rippled) = Sheet(_device, ripples, Format.R32G32B32A32_Float);
            _ripplesOf = ripples;
        }

        WaterSheet swells = ShadeProgram.Swells;
        if (!ReferenceEquals(swells, _swellsOf))
        {
            _swelled.Dispose();
            _swells.Dispose();
            (_swells, _swelled) = Sheet(_device, swells, Format.R32G32B32A32_Float);
            _swellsOf = swells;
        }

        WaterSheet profile = ShadeProgram.BeachProfile;
        if (!ReferenceEquals(profile, _beachProfileOf))
        {
            _beachProfiled.Dispose();
            _beachProfile.Dispose();
            (_beachProfile, _beachProfiled) = Sheet(_device, profile, Format.R32G32B32A32_Float);
            _beachProfileOf = profile;
        }

        WaterSheet noise = ShadeProgram.BeachNoise;
        if (!ReferenceEquals(noise, _beachNoiseOf))
        {
            _beachNoised.Dispose();
            _beachNoise.Dispose();
            (_beachNoise, _beachNoised) = Sheet(_device, noise, Format.R32G32B32A32_Float);
            _beachNoiseOf = noise;
        }

        WaterSheet field = ShadeProgram.FieldProfile;
        if (!ReferenceEquals(field, _fieldProfileOf))
        {
            _fieldProfiled.Dispose();
            _fieldProfile.Dispose();
            (_fieldProfile, _fieldProfiled) = Sheet(_device, field, Format.R32G32B32A32_Float);
            _fieldProfileOf = field;
        }
    }

    /// <summary>A texture of floats on the card, one level, the texels as they are.</summary>
    private static (ID3D11Texture2D Texture, ID3D11ShaderResourceView View) Floats(
        ID3D11Device device, ReadOnlySpan<float> source, int width, int height, Format format, int perTexel)
    {
        float[] texels = source.ToArray();
        GCHandle pinned = GCHandle.Alloc(texels, GCHandleType.Pinned);
        try
        {
            ID3D11Texture2D texture = device.CreateTexture2D(
                new Texture2DDescription(format, width, height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable),
                [new SubresourceData(pinned.AddrOfPinnedObject(), width * perTexel * 4, 0)]);
            return (texture, device.CreateShaderResourceView(texture));
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>
    /// A skinned mesh's bones and weights on the card - four bytes of each a vertex, read whole as an R8G8B8A8_UINT element by the vertex shader.
    /// </summary>
    /// <remarks>
    /// THE MESH'S OWN ARRAYS, NOT COPIED, where they are four bytes a vertex as the file carried them; a shorter one is padded with
    /// noughts, which move nothing - as SkeletonPose.Move stops at the end of its weights.
    /// </remarks>
    private sealed class WeightBuffers : IDisposable
    {
        public WeightBuffers(ID3D11Device device, SkinnedMesh mesh)
        {
            int count = mesh.Positions.Length;
            Bones = CardBuffer.Of<byte>(device, Four(mesh.Bones, count), Format.R8G8B8A8_UInt, count);
            Weights = CardBuffer.Of<byte>(device, Four(mesh.Weights, count), Format.R8G8B8A8_UInt, count);
        }

        public CardBuffer Bones { get; }

        public CardBuffer Weights { get; }

        public long Used { get; set; }

        public void Dispose()
        {
            Weights.Dispose();
            Bones.Dispose();
        }

        /// <summary>Four bytes a vertex: the array itself where it is that long, else a copy padded or cut to it.</summary>
        private static byte[] Four(byte[] bytes, int count)
        {
            if (bytes.Length == count * 4)
            {
                return bytes;
            }

            var four = new byte[count * 4];
            bytes.AsSpan(0, Math.Min(bytes.Length, four.Length)).CopyTo(four);
            return four;
        }
    }

    /// <summary>A room's shore on the card, uploaded the first time it is drawn and let go as the meshes are.</summary>
    private sealed class ShoreTexture : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public ShoreTexture(ID3D11Device device, ShoreField shore)
        {
            // FOUR FLOATS A TEXEL: the shore's distance, the way in, and the ground's shore's distance the field waves' break may take.
            (_texture, View) = Floats(device, shore.CardTexels(), shore.Width, shore.Height, Format.R32G32B32A32_Float, 4);
        }

        public ID3D11ShaderResourceView View { get; }

        public long Used { get; set; }

        public void Dispose()
        {
            View.Dispose();
            _texture.Dispose();
        }
    }

    /// <summary>
    /// A texture on the card with every level the processor reads: the file's own blocks as they are where the card reads their
    /// format (<see cref="Blocked"/>), else the decoded levels as bytes.
    /// </summary>
    /// <remarks>
    /// THE FILE'S BLOCKS, NOT A DECODED COPY (v0.1.326, asked for from the live client): a BC7 a byte a texel and a BC1 half of one,
    /// where four bytes went before - Port's stage 0.8 gigabytes of the card's memory for 4.3, and no decoding on the processor for
    /// it. The card decodes them as it reads them, BC7 to the bit as BlockArt does (the format's decoding is exact), BC1's and the
    /// one-channel ramps' in-between values as exact fractions where BlockArt rounds them to a byte - half a level apart at most.
    /// Every level is a row pitch of its blocks, each row four texels high.
    /// </remarks>
    private sealed class SkinTexture : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public SkinTexture(ID3D11Device device, Mipmaps skin, bool blocked)
        {
            _texture = blocked && skin.Blocks is { } blocks ? Blocks(device, blocks) : Bytes(device, skin);
            View = device.CreateShaderResourceView(_texture);
        }

        /// <summary>The file's levels as blocks, straight out of its bytes.</summary>
        private static ID3D11Texture2D Blocks(ID3D11Device device, BlockTexture blocks)
        {
            GCHandle pinned = GCHandle.Alloc(blocks.Bytes, GCHandleType.Pinned);
            try
            {
                IntPtr start = pinned.AddrOfPinnedObject();
                var data = new SubresourceData[blocks.Levels];
                for (var level = 0; level < blocks.Levels; level++)
                {
                    data[level] = new SubresourceData(start + blocks.OffsetAt(level), blocks.PitchAt(level), 0);
                }

                return device.CreateTexture2D(
                    new Texture2DDescription(
                        CardFormat(blocks.Format), blocks.Width, blocks.Height, 1, blocks.Levels, BindFlags.ShaderResource, ResourceUsage.Immutable),
                    data);
            }
            finally
            {
                pinned.Free();
            }
        }

        /// <summary>The decoded levels as four bytes a texel - decoding them here where they are not yet.</summary>
        private static ID3D11Texture2D Bytes(ID3D11Device device, Mipmaps skin)
        {
            int levels = skin.Count;
            var pinned = new GCHandle[levels];
            var data = new SubresourceData[levels];
            try
            {
                for (var level = 0; level < levels; level++)
                {
                    GamePicture one = skin[level];
                    pinned[level] = GCHandle.Alloc(one.Rgba, GCHandleType.Pinned);
                    data[level] = new SubresourceData(pinned[level].AddrOfPinnedObject(), one.Width * 4, 0);
                }

                return device.CreateTexture2D(
                    new Texture2DDescription(
                        Format.R8G8B8A8_UNorm, skin.Width, skin.Height, 1, levels, BindFlags.ShaderResource, ResourceUsage.Immutable),
                    data);
            }
            finally
            {
                foreach (GCHandle one in pinned)
                {
                    if (one.IsAllocated)
                    {
                        one.Free();
                    }
                }
            }
        }

        public long Used { get; set; }

        public ID3D11ShaderResourceView View { get; }

        public void Dispose()
        {
            View.Dispose();
            _texture.Dispose();
        }
    }
}
