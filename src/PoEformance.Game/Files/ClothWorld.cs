using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>A collision shape of a body, carried by one of its bones (module+0x2862510 hands the physics world each).</summary>
/// <param name="Bone">The bone that carries it, in the model's rig.</param>
/// <param name="Bind">An ellipsoid's frame at the bind in the model's space - its axes as rows, its centre as the place.</param>
/// <param name="Radii">An ellipsoid's three radii.</param>
/// <param name="Sphere">A capsule end's index into the world's spheres, for an ellipsoid -1.</param>
public readonly record struct ClothEllipsoid(int Bone, Matrix4x4 Bind, Vector3 Radii);

/// <summary>A sphere a capsule ends in: its bone, its centre at the bind in the model's space, its radius.</summary>
public readonly record struct ClothSphere(int Bone, Vector3 Centre, float Radius);

/// <summary>
/// The engine's physics world for one model: its cloths, the body's collision shapes, and the system's own clock - stepped once a frame as
/// the game steps it, every simulated bone's matrix worked out after.
/// </summary>
/// <remarks>
/// THE ORDER OF A FRAME, as the code runs it: the controller's phase that skins the targets (module+0x285DA60 -&gt; 0x2868EF0 -&gt;
/// SetTargets 0x13AA8B0: the last targets kept, the new ones set, every distance constraint's rest the targets' distance and every shock
/// link's length too); the physics system's update (module+0x132F6C0: the frame's time smoothed, ten percent a frame, into an accumulator held
/// to a tenth of a second, ONE step a frame of all of it up to a fifteenth where it holds five milliseconds or more - the gravity, drag and
/// animation pull of each object (0x1330160), then the world: integration, collisions and ten iterations of the solver (0x139F150,
/// 0x13A0990)); the controller's next phase, shock propagation and enclosure (0x285DCC0); and the bones from the particles (0x28695B0), each
/// particle where its velocity puts it the time since the step. Which of the controller's phases runs before the system's update in the
/// game's frame is not read: CHOSEN as their vtable's order.
/// NOT DONE: the wind (0x1335690 - nought), the heightmap and the planes, the pre-step callback the step makes of each object
/// (its owner's, not read); a kinematic particle is moved to its target each step through the integration's own path (+0x24 and
/// +0x35 of a body), which call sets it is not read - CHOSEN.
/// </remarks>
public sealed class ClothWorld
{
    /// <summary>The share of a frame's time the smoothed time moves towards it (module+0x132F6C0).</summary>
    public const float Smoothing = 0.1f;

    /// <summary>The most the accumulator holds.</summary>
    public const float MostHeld = 0.1f;

    /// <summary>The longest step.</summary>
    public const float LongestStep = 0.0666667f;

    /// <summary>The least time a step is taken with.</summary>
    public const float LeastStep = 0.005f;

    private readonly Body[] _bodies;
    private readonly ClothEllipsoid[] _ellipsoids;
    private readonly ClothSphere[] _spheres;
    private readonly (int From, int To)[] _capsules;
    private readonly Matrix4x4[] _ellipsoidNow, _ellipsoidBefore;
    private readonly Vector3[] _sphereNow, _sphereBefore;
    private readonly float[] _sphereRadius;
    private float _smoothed = -1f, _held, _since;
    private bool _started;
    private long _frames;

    /// <summary>A world of <paramref name="cloths"/> among the body's shapes.</summary>
    public ClothWorld(IReadOnlyList<ClothRig> cloths, IReadOnlyList<ClothEllipsoid> ellipsoids, IReadOnlyList<ClothSphere> spheres, IReadOnlyList<(int From, int To)> capsules)
    {
        ArgumentNullException.ThrowIfNull(cloths);
        _bodies = [.. cloths.Select(one => new Body(one))];
        _ellipsoids = [.. ellipsoids ?? []];
        _spheres = [.. spheres ?? []];
        _capsules = [.. (capsules ?? []).Where(one => (uint)one.From < (uint)_spheres.Length && (uint)one.To < (uint)_spheres.Length)];
        _ellipsoidNow = new Matrix4x4[_ellipsoids.Length];
        _ellipsoidBefore = new Matrix4x4[_ellipsoids.Length];
        _sphereNow = new Vector3[_spheres.Length];
        _sphereBefore = new Vector3[_spheres.Length];
        _sphereRadius = new float[_spheres.Length];
    }

    /// <summary>The cloths.</summary>
    public IReadOnlyList<ClothRig> Cloths => [.. _bodies.Select(one => one.Rig)];

    /// <summary>How many steps have been taken.</summary>
    public long Steps { get; private set; }

    /// <summary>The last step's seconds.</summary>
    public float LastStep { get; private set; }

    /// <summary>A cloth's particles where they stand now.</summary>
    public ReadOnlySpan<Vector3> Particles(int cloth) => _bodies[cloth].X;

    /// <summary>
    /// One frame of <paramref name="seconds"/>: the targets skinned by <paramref name="skin"/> (each bone's bind-to-pose matrix in the model's
    /// space), the system's step where its clock takes one, shock propagation and enclosure. The first frame stands every particle on its target.
    /// </summary>
    public void Frame(float seconds, ReadOnlySpan<Matrix4x4> skin)
    {
        _frames++;
        foreach (Body body in _bodies)
        {
            body.Target(skin, _started);
        }

        Place(skin);
        if (!_started)
        {
            _started = true;
            Array.Copy(_sphereNow, _sphereBefore, _sphereNow.Length);
            Array.Copy(_ellipsoidNow, _ellipsoidBefore, _ellipsoidNow.Length);
            return;
        }

        if (!(seconds > 0f) || !float.IsFinite(seconds))
        {
            return;
        }

        _smoothed = _smoothed < 0f ? seconds : _smoothed + ((seconds - _smoothed) * Smoothing);
        _held += _smoothed;
        _since += _smoothed;
        if (_held > MostHeld)
        {
            _held = MostHeld;
        }

        float step = MathF.Min(_held, LongestStep);
        if (step >= LeastStep)
        {
            _held -= step;
            _since = 0f;
            LastStep = step;
            Step(step);
            Steps++;
        }

        foreach (Body body in _bodies)
        {
            body.After();
        }
    }

    /// <summary>A simulated bone's matrix in the model's space as its cloth holds it now - each particle where it stands plus its velocity over the time since the step.</summary>
    public Matrix4x4 Bone(int cloth, ClothBoneLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        Body body = _bodies[cloth];
        ReadOnlySpan<Vector3> at = body.Seen(_since, _frames);
        Matrix4x4 frame = link.Kind switch
        {
            ClothBoneKind.Particle => ClothMath.VertexFrame(at, body.Rig, link.Particle),
            ClothBoneKind.Edge => ClothMath.EdgeFrame(at[link.Along.A], at[link.Along.B], at[link.Along.C], link.Along.D >= 0 ? at[link.Along.D] : null, link.T),
            ClothBoneKind.Triangle => Triangle(at, body.Rig.Triangles[link.Triangle]),
            _ => Matrix4x4.Identity,
        };

        return link.Kind == ClothBoneKind.Skinned ? ClothMath.Skinned(at, link) : Matrix4x4.Multiply(link.Offset, frame);
    }

    private static Matrix4x4 Triangle(ReadOnlySpan<Vector3> at, ClothTriangle t) => ClothMath.TriangleFrame(at[t.A], at[t.B], at[t.C], t.Area);

    /// <summary>The shapes where their bones carry them this frame, and where they stood the last step.</summary>
    private void Place(ReadOnlySpan<Matrix4x4> skin)
    {
        for (var one = 0; one < _ellipsoids.Length; one++)
        {
            ClothEllipsoid shape = _ellipsoids[one];
            Matrix4x4 moved = (uint)shape.Bone < (uint)skin.Length ? shape.Bind * skin[shape.Bone] : shape.Bind;
            _ellipsoidNow[one] = moved;
        }

        for (var one = 0; one < _spheres.Length; one++)
        {
            ClothSphere sphere = _spheres[one];
            Matrix4x4 moved = (uint)sphere.Bone < (uint)skin.Length ? skin[sphere.Bone] : Matrix4x4.Identity;
            _sphereNow[one] = Vector3.Transform(sphere.Centre, moved);
            _sphereRadius[one] = sphere.Radius * MathF.Sqrt(MathF.Max(new Vector3(moved.M11, moved.M12, moved.M13).LengthSquared(), 1e-12f));
        }
    }

    private void Step(float dt)
    {
        foreach (Body body in _bodies)
        {
            body.Forces(dt);
        }

        foreach (Body body in _bodies)
        {
            body.Integrate(dt);
            if (body.Rig.Settings.Collisions)
            {
                body.Collide(this);
            }

            body.Solve();
        }

        Array.Copy(_sphereNow, _sphereBefore, _sphereNow.Length);
        Array.Copy(_ellipsoidNow, _ellipsoidBefore, _ellipsoidNow.Length);
    }

    /// <summary>The contacts of one particle (the world step, module+0x139F150): every ellipsoid and capsule whose surface it is within the margin of.</summary>
    private void Contacts(Vector3 x, int particle, List<Contact> into)
    {
        for (var one = 0; one < _ellipsoids.Length; one++)
        {
            (Matrix4x4 axes, Vector3 radii) = Normalised(_ellipsoidNow[one], _ellipsoids[one].Radii);
            Vector3 from = x - axes.Translation;
            Vector3 r0 = new(axes.M11, axes.M12, axes.M13), r1 = new(axes.M21, axes.M22, axes.M23), r2 = new(axes.M31, axes.M32, axes.M33);
            var local = new Vector3(Vector3.Dot(from, r0), Vector3.Dot(from, r1), Vector3.Dot(from, r2));
            (Vector3 point, Vector3 normal) = ClothMath.Ellipsoid(radii, local);
            Vector3 surface = axes.Translation + (point.X * r0) + (point.Y * r1) + (point.Z * r2);
            Vector3 way = (normal.X * r0) + (normal.Y * r1) + (normal.Z * r2);
            if (Vector3.Dot(surface - x, way) > -ClothMath.ContactMargin)
            {
                Vector3 before = Matrix4x4.Invert(_ellipsoidNow[one], out Matrix4x4 back) ? Vector3.Transform(Vector3.Transform(surface, back), _ellipsoidBefore[one]) : surface;
                into.Add(new Contact(particle, surface, way, surface - before));
            }
        }

        foreach ((int from, int to) in _capsules)
        {
            Vector3 a = _sphereNow[from], b = _sphereNow[to];
            (Vector3 surface, Vector3 normal) = ClothMath.RoundCone(a, _sphereRadius[from], b, _sphereRadius[to], x);
            if (Vector3.Dot(surface - x, normal) > -ClothMath.ContactMargin)
            {
                // THE SURFACE'S OWN MOTION, CHOSEN: the two spheres' motions shared by how far along the axis the point stands - the engine maps
                // the point through the capsule's own last frame (its record's +0x40), not read.
                Vector3 axis = b - a;
                float t = axis.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(surface - a, axis) / axis.LengthSquared(), 0f, 1f) : 0f;
                Vector3 motion = Vector3.Lerp(_sphereNow[from] - _sphereBefore[from], _sphereNow[to] - _sphereBefore[to], t);
                into.Add(new Contact(particle, surface, normal, motion));
            }
        }
    }

    /// <summary>An ellipsoid's frame with unit axes, its radii scaled by how long its axes were.</summary>
    private static (Matrix4x4 Axes, Vector3 Radii) Normalised(Matrix4x4 frame, Vector3 radii)
    {
        Vector3 r0 = new(frame.M11, frame.M12, frame.M13), r1 = new(frame.M21, frame.M22, frame.M23), r2 = new(frame.M31, frame.M32, frame.M33);
        float l0 = r0.Length(), l1 = r1.Length(), l2 = r2.Length();
        r0 = ClothMath.Unit(r0);
        r1 = ClothMath.Unit(r1);
        r2 = ClothMath.Unit(r2);
        return (ClothMath.Rows(r0, r1, r2, frame.Translation), new Vector3(radii.X * l0, radii.Y * l1, radii.Z * l2));
    }

    /// <summary>A contact: the particle, the surface point and normal, and the surface's own motion this step.</summary>
    private readonly record struct Contact(int Particle, Vector3 Point, Vector3 Normal, Vector3 Motion);

    /// <summary>One cloth's state: each particle's place, displacement, targets.</summary>
    private sealed class Body(ClothRig rig)
    {
        public readonly ClothRig Rig = rig;
        public readonly Vector3[] X = new Vector3[rig.Count];
        public readonly Vector3[] D = new Vector3[rig.Count];
        private readonly Vector3[] _target = new Vector3[rig.Count];
        private readonly Vector3[] _last = new Vector3[rig.Count];
        private readonly Vector3[] _force = new Vector3[rig.Count];
        private readonly Vector3[] _seen = new Vector3[rig.Count];
        private readonly float[] _rest = new float[rig.Edges.Length];
        private readonly float[] _shock = new float[rig.Shocks.Length];
        private readonly List<Contact> _contacts = [];
        private float _dt;
        private long _seenFrame = -1;
        private float _seenSince = float.NaN;

        /// <summary>The targets skinned anew (SetTargets): the last kept, every rest and shock length the targets' own distance.</summary>
        public void Target(ReadOnlySpan<Matrix4x4> skin, bool started)
        {
            for (var p = 0; p < Rig.Count; p++)
            {
                Int4 bones = Rig.Bones[p];
                Vector4 weights = Rig.Weights[p];
                Vector3 sum = Vector3.Zero;
                for (var k = 0; k < 4; k++)
                {
                    int bone = bones[k];
                    float weight = k switch { 0 => weights.X, 1 => weights.Y, 2 => weights.Z, _ => weights.W };
                    if ((uint)bone < (uint)skin.Length && weight != 0f)
                    {
                        sum += Vector3.Transform(Rig.Rest[p], skin[bone]) * weight;
                    }
                }

                _last[p] = started ? _target[p] : sum;
                _target[p] = sum;
                if (!started)
                {
                    X[p] = sum;
                    D[p] = Vector3.Zero;
                }
            }

            for (var one = 0; one < Rig.Shocks.Length; one++)
            {
                (int parent, int child) = Rig.Shocks[one];
                _shock[one] = MathF.Max(Vector3.Distance(_target[parent], _target[child]), 1f);
            }

            for (var one = 0; one < Rig.Edges.Length; one++)
            {
                ClothEdge edge = Rig.Edges[one];
                _rest[one] = MathF.Max(Vector3.Distance(_target[edge.A], _target[edge.B]), -1f);
            }
        }

        /// <summary>The step's forces (module+0x1330160): gravity, drag, and the pull towards the animation.</summary>
        public void Forces(float dt)
        {
            // A BODY'S FIRST STEP HAS NO STEP BEFORE IT: its time taken as this one's (the engine's container is made with one, not read).
            if (_dt <= 0f)
            {
                _dt = dt;
            }

            ClothSettings s = Rig.Settings;
            for (var p = 0; p < Rig.Count; p++)
            {
                _force[p] = Rig.Pinned[p] ? Vector3.Zero : new Vector3(0f, 0f, s.Gravity);
            }

            Drag(s.NormalViscosity * ClothSettings.ViscosityScale, s.TangentViscosity * ClothSettings.ViscosityScale, dt);
            if (MathF.Abs(s.AnimationVelocityFactor) > 1e-7f || MathF.Abs(s.AnimationForceFactor) > 1e-7f)
            {
                Pull(dt);
            }
        }

        /// <summary>
        /// The drag (module+0x13AB700): the velocity relative to the wind - nought - decayed by exp(-normal · dt) across the particle's own
        /// normal and exp(-tangent · dt) along it, in its frame's three ways; a particle of no triangle has no frame, and the engine's three
        /// ways are then nought, so it stops.
        /// </summary>
        private void Drag(float normal, float tangent, float dt)
        {
            float across = MathF.Exp(-(normal * dt)), along = MathF.Exp(-(tangent * dt));
            for (var p = 0; p < Rig.Count; p++)
            {
                if (Rig.Pinned[p])
                {
                    continue;
                }

                Matrix4x4 frame = ClothMath.VertexFrame(X, Rig, p);
                Vector3 n = new(frame.M21, frame.M22, frame.M23);

                // THE THREE WAYS ORTHONORMAL, so the tangents' choice (module+0x1330A90) does not matter: along the normal one decay, across it the other.
                D[p] = n == Vector3.Zero ? Vector3.Zero : (D[p] * along) + (n * (Vector3.Dot(n, D[p]) * (across - along)));
            }
        }

        /// <summary>The pull towards the animation (module+0x13ABBC0).</summary>
        private void Pull(float dt)
        {
            ClothSettings s = Rig.Settings;
            const float scale = 1f;
            float inverse = MathF.Abs(dt + 0.001f) > 1e-8f ? 1f / (dt + 0.001f) : 0f;
            float keep = MathF.Exp(-(dt * s.AnimationForceFactor * scale));
            float dtNow = _dt;
            for (var p = 0; p < Rig.Count; p++)
            {
                if (Rig.Pinned[p])
                {
                    continue;
                }

                Vector3 own = (_target[p] - _last[p]) * inverse * s.AnimationVelocityMultiplier;
                Vector3 wanted = ((_target[p] - X[p]) * s.AnimationVelocityFactor * scale) + own;
                if (wanted.LengthSquared() > s.AnimationMaxVelocity * s.AnimationMaxVelocity)
                {
                    wanted = ClothMath.Unit(wanted) * s.AnimationMaxVelocity;
                }

                Vector3 velocity = D[p] * (1f / dtNow);
                D[p] = (((velocity - wanted) * keep) + wanted) * dtNow;
            }
        }

        /// <summary>The integration (module+0x13A0640 and the world's next loop): a kinematic particle onto its target, every displacement scaled by the steps' ratio, gravity added, every free particle moved by it.</summary>
        public void Integrate(float dt)
        {
            for (var p = 0; p < Rig.Count; p++)
            {
                if (Rig.Pinned[p])
                {
                    D[p] = _target[p] - X[p];
                    X[p] = _target[p];
                }
            }

            float ratio = MathF.Min(dt / MathF.Max(_dt, 1e-5f), 10f);
            for (var p = 0; p < Rig.Count; p++)
            {
                D[p] *= ratio;
            }

            _dt = dt;
            for (var p = 0; p < Rig.Count; p++)
            {
                if (!Rig.Pinned[p])
                {
                    D[p] += _force[p] * dt * dt;
                    X[p] += D[p];
                }
            }
        }

        public void Collide(ClothWorld world)
        {
            _contacts.Clear();
            for (var p = 0; p < Rig.Count; p++)
            {
                if (!Rig.Pinned[p])
                {
                    world.Contacts(X[p], p, _contacts);
                }
            }
        }

        /// <summary>The solver (module+0x13A0990): ten iterations - the bends on every third, every distance, every contact.</summary>
        public void Solve()
        {
            ClothSettings s = Rig.Settings;
            Span<Vector3> corners = stackalloc Vector3[4];
            Span<Vector3> moves = stackalloc Vector3[4];
            Span<bool> fixedCorners = stackalloc bool[4];
            int third = 0;
            for (var iteration = 0; iteration < ClothMath.Iterations; iteration++)
            {
                if (++third >= 3)
                {
                    third -= 3;
                    for (var one = 0; one < Rig.Bends.Length; one++)
                    {
                        ClothEdge edge = Rig.Bends[one];
                        ReadOnlySpan<int> ids = [edge.A, edge.B, edge.C, edge.D];
                        for (var k = 0; k < 4; k++)
                        {
                            corners[k] = X[ids[k]];
                            moves[k] = D[ids[k]];
                            fixedCorners[k] = Rig.Pinned[ids[k]];
                        }

                        ClothMath.Bend(corners, moves, fixedCorners, s.BendStiffness, Rig.RestFolds[one], s.BendAllowedAngle, _dt, ClothMath.BendPasses);
                        for (var k = 0; k < 4; k++)
                        {
                            X[ids[k]] = corners[k];
                            D[ids[k]] = moves[k];
                        }
                    }
                }

                for (var one = 0; one < Rig.Edges.Length; one++)
                {
                    ClothEdge edge = Rig.Edges[one];
                    ClothMath.Distance(
                        ref X[edge.A], ref D[edge.A], Rig.Pinned[edge.A], ref X[edge.B], ref D[edge.B], Rig.Pinned[edge.B],
                        _rest[one], s.LinearStiffness, s.LinearAllowedContraction, _dt, ClothMath.Iterations);
                }

                foreach (Contact contact in _contacts)
                {
                    ClothMath.Contact(ref X[contact.Particle], ref D[contact.Particle], contact.Point, contact.Normal, contact.Motion);
                }
            }
        }

        /// <summary>After the step (module+0x285DCC0): the shock propagation where on, then the enclosure.</summary>
        public void After()
        {
            ClothSettings s = Rig.Settings;
            if (s.ShockPropagation)
            {
                for (var one = 0; one < Rig.Shocks.Length; one++)
                {
                    (int parent, int child) = Rig.Shocks[one];
                    Vector3 way = X[child] - X[parent];
                    float length = way.Length();
                    if (length - _shock[one] > 0f)
                    {
                        Vector3 placed = X[parent] + (ClothMath.Unit(way) * _shock[one]);
                        D[child] += placed - X[child];
                        X[child] = placed;
                    }
                }
            }

            for (var p = 0; p < Rig.Count; p++)
            {
                int depth = Rig.Depth[p];
                if ((depth < 0 && s.EnclosureRadiusAdditive < 1e-7f) || Rig.Pinned[p])
                {
                    continue;
                }

                float reach = (MathF.Max(depth, 0) * s.EnclosureRadius) + s.EnclosureRadiusAdditive;
                int parent = Rig.Parent[p];
                if (parent != -1)
                {
                    reach += Vector3.Distance(_target[p], _target[parent]) * s.EnclosureAngle;
                }

                Vector3 off = X[p] - _target[p];
                float gap = off.Length();
                if (float.IsNaN(gap) || float.IsNaN(D[p].Length()))
                {
                    X[p] = _target[p];
                    D[p] = Vector3.Zero;
                    continue;
                }

                if (gap > reach)
                {
                    Vector3 placed = _target[p] + (ClothMath.Unit(off) * reach);
                    D[p] += placed - X[p];
                    X[p] = placed;
                }
            }
        }

        /// <summary>
        /// Each particle where its velocity takes it <paramref name="since"/> seconds on (the bones' read of a body, module+0x13AA090) - worked
        /// out once a frame, however many of its bones ask (a skirt's eighty-eight).
        /// </summary>
        public ReadOnlySpan<Vector3> Seen(float since, long frame)
        {
            if (since <= 0f || _dt <= 0f)
            {
                return X;
            }

            if (frame == _seenFrame && since.Equals(_seenSince))
            {
                return _seen;
            }

            _seenFrame = frame;
            _seenSince = since;
            float share = since / _dt;
            for (var p = 0; p < Rig.Count; p++)
            {
                _seen[p] = X[p] + (D[p] * share);
            }

            return _seen;
        }
    }
}
