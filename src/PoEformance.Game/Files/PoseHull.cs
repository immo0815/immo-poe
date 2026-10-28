using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// Where a skinned mesh can be in a pose, without skinning it: each bone's box of the vertices it moves, carried by the bone.
/// </summary>
/// <remarks>
/// FOR THE SUN'S SHADOW MAP OF A MONSTER THE GRAPHICS CARD SKINS (v0.1.325, CHOSEN from the live client over skinning on the processor
/// for the map's frame alone). The map is laid over the bounds of everything it holds (ShadowMap.Framed), and with the card moving the
/// vertices nothing on the processor knows where they went. A skinned vertex is the weighted mean of where each of its bones would put
/// it, so it lies within the hull of those places - and each of them lies in that bone's box of its vertices, taken into the bone's
/// own space once (the inverse bind) and carried out by the bone's world matrix each frame: a parallelepiped, whose eight corners
/// bound it. The bounds of every bone's corners bound the mesh, whatever the pose, at the cost of a few hundred points a frame.
///
/// LOOSER THAN THE VERTICES, never tighter: a limb's box turned across its own length reaches further than the limb. So the map is
/// a little coarser than one laid over the posed vertices themselves - MEASURED on a body of bars over a real rig through its real
/// animation (CardSkinTests): the map's longer side 1.24 times the pose's at the median, 1.54 at most; boxes turned to each bone's
/// own spread came to 1.20 and 1.53, not worth their arithmetic. The processor lays its map so too (MeshPicture.Canvas.Hull), so the
/// card and the processor still draw the same shadow. On a stage the frame is mostly the stage's own, the monster well inside it, and
/// nothing changes there at all.
/// </remarks>
public sealed class PoseHull
{
    private readonly int[] _bones;
    private readonly Vector3[] _least;
    private readonly Vector3[] _most;
    private readonly bool _still;
    private readonly Vector3 _stillLeast;
    private readonly Vector3 _stillMost;
    private readonly Vector3[] _corners;

    private PoseHull(int[] bones, Vector3[] least, Vector3[] most, bool still, Vector3 stillLeast, Vector3 stillMost)
    {
        _bones = bones;
        _least = least;
        _most = most;
        _still = still;
        _stillLeast = stillLeast;
        _stillMost = stillMost;
        _corners = new Vector3[(bones.Length + 1) * 8];
    }

    /// <summary>How many bones move any vertex.</summary>
    public int Bones => _bones.Length;

    /// <summary>
    /// The hull of a mesh skinned by a skeleton - every bone's box of the vertices it moves, in the bone's own space, and the box of
    /// those no bone moves, where the file put them. Null where the mesh has nothing.
    /// </summary>
    /// <remarks>A weight of nought and a bone past the rig move nothing, as <see cref="SkeletonPose.Move(SkinnedMesh?, Vector3[], Vector3[])"/> skips them.</remarks>
    public static PoseHull? Of(SkeletonPose? pose, SkinnedMesh? mesh)
    {
        if (pose is null || mesh is not { Ready: true })
        {
            return null;
        }

        IReadOnlyList<Matrix4x4> inverse = pose.InverseBind;
        int bones = inverse.Count;
        var least = new Vector3[bones];
        var most = new Vector3[bones];
        Array.Fill(least, new Vector3(float.MaxValue));
        Array.Fill(most, new Vector3(float.MinValue));
        var used = new bool[bones];
        Vector3 stillLeast = new(float.MaxValue), stillMost = new(float.MinValue);
        var still = false;
        for (var one = 0; one < mesh.Positions.Length; one++)
        {
            Vector3 at = mesh.Positions[one];
            var moved = false;
            int slot = one * 4;
            for (var part = 0; part < 4 && slot + part < mesh.Weights.Length; part++)
            {
                int bone = mesh.Bones[slot + part];
                if (mesh.Weights[slot + part] == 0 || bone >= bones)
                {
                    continue;
                }

                Vector3 own = Vector3.Transform(at, inverse[bone]);
                least[bone] = Vector3.Min(least[bone], own);
                most[bone] = Vector3.Max(most[bone], own);
                used[bone] = true;
                moved = true;
            }

            if (!moved)
            {
                stillLeast = Vector3.Min(stillLeast, at);
                stillMost = Vector3.Max(stillMost, at);
                still = true;
            }
        }

        var listed = new List<int>();
        for (var bone = 0; bone < bones; bone++)
        {
            if (used[bone])
            {
                listed.Add(bone);
            }
        }

        return new PoseHull(
            [.. listed], [.. listed.Select(bone => least[bone])], [.. listed.Select(bone => most[bone])], still, stillLeast, stillMost);
    }

    /// <summary>
    /// The bounds of the mesh in the pose's last frame across a shadow map along a direction - ShadowMap.Bounded's, of the hull's corners.
    /// </summary>
    /// <param name="pose">The pose, at the frame drawn.</param>
    /// <param name="direction">The way the light travels.</param>
    /// <param name="place">Where the mesh is put after it is skinned - a stage's monster's place on it, the identity where it stands alone.</param>
    /// <remarks>On the calling thread, into the hull's own scratch: one hull is one picture's.</remarks>
    public ShadowBounds Bounds(SkeletonPose pose, Vector3 direction, Matrix4x4 place)
    {
        ArgumentNullException.ThrowIfNull(pose);
        IReadOnlyList<Matrix4x4> world = pose.World;
        var count = 0;
        for (var one = 0; one < _bones.Length; one++)
        {
            int bone = _bones[one];
            if (bone >= world.Count)
            {
                continue;
            }

            Corners(_least[one], _most[one], world[bone] * place, ref count);
        }

        if (_still)
        {
            Corners(_stillLeast, _stillMost, place, ref count);
        }

        return ShadowMap.Bounded(_corners.AsSpan(0, count), direction);
    }

    /// <summary>A box's eight corners carried by a matrix, into the scratch.</summary>
    private void Corners(Vector3 least, Vector3 most, Matrix4x4 by, ref int count)
    {
        for (var corner = 0; corner < 8; corner++)
        {
            var at = new Vector3((corner & 1) == 0 ? least.X : most.X, (corner & 2) == 0 ? least.Y : most.Y, (corner & 4) == 0 ? least.Z : most.Z);
            _corners[count++] = Vector3.Transform(at, by);
        }
    }
}
