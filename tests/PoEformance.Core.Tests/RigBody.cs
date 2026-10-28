using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A body for a real rig and its real keys, made of its own bones - for the tests that hold a skinned picture against itself.
/// </summary>
/// <remarks>
/// A BAR FROM EACH BONE'S PARENT TO IT: the four corners at the parent's end weighed between the two (128 and 127, a joint's blend),
/// the four at the bone's end the bone's alone - so every bone moves something, and the joints bend as a body's do. Beside the bars,
/// a triangle nothing weighs, and one whose corners name a bone past the rig beside a real one: SkeletonPose.Move leaves the first where
/// the file put it and skips the second's bad bone, and whatever skins it elsewhere must do the same. Linked into the card's tests too.
/// </remarks>
internal static class RigBody
{
    /// <summary>Art/Models/MONSTERS/blackguardminorinquisitor/rig.ast's header, bones and one animation - see SkeletonPoseFromTheGameTests.</summary>
    private const string Fixture = "blackguard-v6.one.ast";

    /// <summary>The fixture's rig, read.</summary>
    public static AnimationSkeleton Rig()
    {
        AnimationSkeleton rig = AnimationSkeleton.Read(Bytes());
        Assert.True(rig.Ready, rig.Why);
        return rig;
    }

    /// <summary>The fixture's rig as the install would hand it over.</summary>
    public static byte[] Bytes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir.FullName, "tests", "fixtures", Fixture));
    }

    /// <summary>The fixture's rig posed at rest, its one animation's keys, and a body of its bones.</summary>
    public static (SkeletonPose Pose, AnimationTracks Tracks, SkinnedMesh Body) Ready(float thick = 6f)
    {
        AnimationSkeleton rig = Rig();
        SkeletonPose? pose = SkeletonPose.Of(rig);
        Assert.NotNull(pose);
        SkeletonAnimation only = rig.Animations[0];
        byte[]? frames = rig.Tracks(only, (_, _) => null);
        Assert.NotNull(frames);
        AnimationTracks tracks = AnimationTracks.Read(frames, only.Tracks, rig.Version);
        Assert.True(tracks.Ready, tracks.Why);
        return (pose, tracks, Of(pose, thick));
    }

    /// <summary>The body: a bar a bone, then the still triangle and the one naming a bone past the rig.</summary>
    public static SkinnedMesh Of(SkeletonPose pose, float thick)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var bones = new List<byte>();
        var weights = new List<byte>();
        var indices = new List<int>();
        IReadOnlyList<Matrix4x4> bind = pose.BindModel;
        for (var bone = 0; bone < bind.Count; bone++)
        {
            int parent = pose.Parents[bone];
            Vector3 from = parent >= 0 ? bind[parent].Translation : Vector3.Zero, to = bind[bone].Translation;
            if (parent < 0 || (to - from).Length() < 1f)
            {
                continue;
            }

            Vector3 along = Vector3.Normalize(to - from);
            Vector3 side = Vector3.Normalize(Vector3.Cross(along, MathF.Abs(along.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
            Vector3 up = Vector3.Cross(along, side);
            int at = positions.Count;
            for (var end = 0; end < 2; end++)
            {
                Vector3 middle = end == 0 ? from : to;
                for (var corner = 0; corner < 4; corner++)
                {
                    Vector3 reach = (((corner & 1) == 0 ? -side : side) + ((corner & 2) == 0 ? -up : up)) * thick;
                    positions.Add(middle + reach);
                    normals.Add(Vector3.Normalize(reach + (along * (end == 0 ? -thick : thick) * 0.5f)));
                    bones.AddRange(end == 0 ? new byte[] { (byte)parent, (byte)bone, 0, 0 } : new byte[] { (byte)bone, 0, 0, 0 });
                    weights.AddRange(end == 0 ? new byte[] { 128, 127, 0, 0 } : new byte[] { 255, 0, 0, 0 });
                }
            }

            // THE BAR'S FOUR SIDES AND TWO ENDS, corners numbered (side bit, up bit) at each end.
            int[] faces = [0, 1, 5, 4, 1, 3, 7, 5, 3, 2, 6, 7, 2, 0, 4, 6, 0, 2, 3, 1, 4, 5, 7, 6];
            for (var face = 0; face < faces.Length; face += 4)
            {
                indices.AddRange([at + faces[face], at + faces[face + 1], at + faces[face + 2], at + faces[face], at + faces[face + 2], at + faces[face + 3]]);
            }
        }

        // A TRIANGLE NOTHING WEIGHS, and one naming bone 250 - past this rig's 43 - at full weight beside a real bone.
        Vector3 low = new(-60f, -60f, 0f);
        int still = positions.Count;
        positions.AddRange([low, low + new Vector3(30f, 0f, 0f), low + new Vector3(0f, 30f, 0f)]);
        normals.AddRange([-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ]);
        bones.AddRange(new byte[12]);
        weights.AddRange(new byte[12]);
        indices.AddRange([still, still + 1, still + 2]);
        Vector3 past = bind[^1].Translation;
        int bad = positions.Count;
        positions.AddRange([past, past + new Vector3(12f, 0f, 0f), past + new Vector3(0f, 12f, 0f)]);
        normals.AddRange([-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ]);
        for (var one = 0; one < 3; one++)
        {
            bones.AddRange(new byte[] { 250, (byte)(bind.Count - 1), 0, 0 });
            weights.AddRange(new byte[] { 255, 100, 0, 0 });
        }

        indices.AddRange([bad, bad + 1, bad + 2]);
        Vector3[] places = [.. positions];
        Vector3 least = places.Aggregate(new Vector3(float.MaxValue), Vector3.Min), most = places.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        SkinnedMesh plain = SkinnedMesh.Of(places, [.. normals], [.. indices], least, most);
        return SkinnedMesh.Joined([new MeshJoin(plain, [.. bones], [.. weights])]);
    }
}
