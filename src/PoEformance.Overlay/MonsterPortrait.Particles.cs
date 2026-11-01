using System.Globalization;
using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Gpu;

namespace PoEformance.Overlay;

/// <summary>
/// The engine's GPU particles in the model pane: the effects the model carries (MonsterModel.Effects) linked behind the frame once it lands
/// (ParticlePrograms), and every emitter's frame, clock and bones handed to the card each picture (ParticleFrame) - the card's alone.
/// </summary>
/// <remarks>
/// AS DECIDED FROM THE LIVE CLIENT (2026-11-01): the engine's particle system built generically, update and render in one go, on the card
/// alone, the processor's picture saying it leaves them out. Each emitter's clock starts at nought when its program lands and runs with the
/// pane's clock - standing while the clock stands (CHOSEN); its frame and bones are where the pose puts its bone group
/// (CarriedEffects.At), a staged monster's through his place on the stage.
/// </remarks>
public sealed partial class MonsterPortrait
{
    /// <summary>The engine's DX11 prelude, read once from the game's program on disk (ShaderPrelude) - null where none was found.</summary>
    private static readonly Lazy<string?> Prelude = new(() =>
    {
        string? install = GameInstall.Find(null);
        string[] programs = install is null ? [] : [.. GameInstall.Names.Select(name => Path.Combine(install, name + ".exe")).Where(File.Exists)];
        return programs.Length > 0 ? ShaderPrelude.Read(programs)?.Text : null;
    });

    private Task<(IReadOnlyList<(CarriedEffect Effect, ParticleProgram Program)> Programs, IReadOnlyList<string> Said)>? _particling;
    private MonsterModel? _particlesOf;
    private IReadOnlyList<(CarriedEffect Effect, ParticleProgram Program)> _particles = [];
    private IReadOnlyList<string> _particlesBuilt = [];
    private Emitted[] _emitted = [];
    private double _particlesFrom;
    private double _particlesClock;
    private uint _particleFrame;
    private string _particlesLine = string.Empty;
    private string _particlesLineOf = string.Empty;

    /// <summary>Whether the engine's GPU particles are drawn - the light panel's "particles", asked every frame; null for always.</summary>
    public Func<bool>? SceneParticles { get; set; }

    private bool Particling() => SceneParticles?.Invoke() ?? true;

    /// <summary>Whether the model carries particles the card is to draw now - the panel's switch on and their programs in.</summary>
    private bool Particled => _particles.Count > 0 && Particling();

    /// <summary>
    /// The model's effects linked behind the frame, once it lands: every emitter of every effect it carries, its shaders made from the install's
    /// own sources - or nothing where it carries none or the install's sources are not to hand.
    /// </summary>
    private void ParticlesBehind()
    {
        MonsterModel carrier = _model.Actor ?? _model;
        if (ReferenceEquals(_particlesOf, carrier))
        {
            return;
        }

        _particlesOf = carrier;
        _particles = [];
        _particlesBuilt = [];
        _emitted = [];
        _particling = null;
        IReadOnlyList<CarriedEffect> effects = carrier.Effects;
        Func<string, byte[]?>? install = _install;
        IReadOnlyList<string> shaders = Shaders?.Invoke() ?? [];
        if (effects.Count == 0 || install is null)
        {
            return;
        }

        _particling = Task.Run(() => ParticlePrograms.Of(install, shaders, Prelude.Value, effects));
    }

    /// <summary>Takes the linked programs once they land, every emitter starting its clock at nought.</summary>
    private void ParticlesLanded()
    {
        if (_particling is not { IsCompleted: true } done)
        {
            return;
        }

        _particling = null;
        if (done.IsCompletedSuccessfully)
        {
            _particles = done.Result.Programs;
            _particlesBuilt = done.Result.Said;
        }
        else
        {
            _particlesBuilt = [$"particles: the link failed - {done.Exception?.GetBaseException().Message}"];
        }

        _emitted = new Emitted[_particles.Count];
        _particlesFrom = _clock;
        _particlesClock = _clock;
    }

    /// <summary>
    /// Every emitter as it stands this picture - its frame and bones where the pose puts its group, its clock and the frame before - or null
    /// where the model carries none drawn.
    /// </summary>
    private ParticleFrame? ParticlesNow()
    {
        if (!Particled)
        {
            return null;
        }

        bool posed = PoseReady;
        IReadOnlyList<Matrix4x4> skin = posed ? _pose!.Skin : [];
        Matrix4x4 place = _model.Actor is null ? Matrix4x4.Identity : _model.ActorPlace;
        float delta = (float)Math.Max(0.0, _clock - _particlesClock);
        float time = (float)(_clock - _particlesFrom);
        var emitters = new ParticleEmitterNow[_particles.Count];
        Span<Vector4> bones = stackalloc Vector4[64];
        for (var at = 0; at < _particles.Count; at++)
        {
            (CarriedEffect effect, ParticleProgram program) = _particles[at];
            (Matrix4x4 world, int count) = CarriedEffects.At(effect, skin, place, bones);
            Vector4[] now = bones[..count].ToArray();
            Emitted last = _emitted[at];
            bool fresh = last.Bones is null;
            emitters[at] = new ParticleEmitterNow(program, world, fresh ? world : last.World, time, fresh ? time : last.Time, now, last.Bones ?? now, fresh);
            _emitted[at] = new Emitted(world, time, now);
        }

        _particlesClock = _clock;
        return new ParticleFrame(emitters, delta, _particleFrame++);
    }

    /// <summary>The line under the picture: what was linked, what the card drew, and the readings it takes - or empty where the model carries none.</summary>
    private string ParticlesLine()
    {
        if ((_model.Actor ?? _model).Effects.Count == 0)
        {
            return string.Empty;
        }

        string drawn = !Particling() ? "off (the light panel's switch)"
            : _particling is not null ? "linking the effects' shaders from the game's own sources"
            : _particles.Count == 0 ? "none linked"
            : !_onCard ? "not drawn - the processor's picture leaves them out (the card's alone)"
            : Card?.Gpu?.ParticlesSaid is { Length: > 0 } said ? said
            : "waiting for the card";
        string key = drawn + "|" + _particlesBuilt.Count.ToString(CultureInfo.InvariantCulture);
        if (key != _particlesLineOf)
        {
            _particlesLineOf = key;
            _particlesLine = "particles: " + drawn.Replace("particles: ", string.Empty, StringComparison.Ordinal)
                + (_particlesBuilt.Count > 0 ? " · " + string.Join(" · ", _particlesBuilt) : string.Empty)
                + " · CHOSEN, said: instance_count the pool and batch_size one, frame_delta_time (dt, 0, 1, 0), the count and seed by a stable hash in"
                + " the file's range, the quad at plus and minus one, a node's sampler SamplerLinearWrap, the engine's lights and shadows not handed over"
                + " (a lit particle shows its own light), AlphaBlend mixed and Additive added by its alpha, the clock starting at nought as the program lands";
        }

        return _particlesLine;
    }

    /// <summary>An emitter's frame, clock and bones the picture before.</summary>
    private readonly record struct Emitted(Matrix4x4 World, float Time, Vector4[]? Bones);
}
