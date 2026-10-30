using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// Joining a monster's body to the pieces it hangs off itself.
/// </summary>
/// <remarks>
/// WHAT CAN GO SILENTLY WRONG HERE, which is why it is worth a test rather than a look: an index
/// that is not shifted by the vertices already written points at the body's triangles and draws a
/// piece made of somebody else's geometry, and a shape whose From is not shifted the same way
/// paints the wrong stretch of the mesh. Both produce a picture - a wrong one - and neither
/// throws.
/// </remarks>
public class MeshJoinTests
{
    /// <summary>A triangle, offset so the two under test are not the same numbers.</summary>
    private static SkinnedMesh Triangle(float at, string shape)
        => SkinnedMesh.Of(
            [new Vector3(at, 0f, 0f), new Vector3(at + 1f, 0f, 0f), new Vector3(at, 1f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2],
            new Vector3(at, 0f, 0f),
            new Vector3(at + 1f, 1f, 0f),
            shapes: [new MeshShape(shape, 0, 3)]);

    [Fact]
    public void TheIndicesAndShapesOfEveryPieceAreShiftedPastWhatCameBefore()
    {
        SkinnedMesh body = Triangle(0f, "BodyShape");
        SkinnedMesh skirt = Triangle(10f, "SkirtShape");

        SkinnedMesh joined = SkinnedMesh.Joined([new MeshJoin(body), new MeshJoin(skirt)]);

        Assert.Equal(6, joined.Positions.Length);
        Assert.Equal([0, 1, 2, 3, 4, 5], joined.Indices);
        Assert.Equal(["BodyShape", "SkirtShape"], joined.Shapes.Select(one => one.Name));
        Assert.Equal([0, 3], joined.Shapes.Select(one => one.From));

        // Every index still addresses a vertex of the joined mesh, which is the property a
        // renderer relies on and an unshifted index would break without saying so.
        Assert.All(joined.Indices, one => Assert.InRange(one, 0, joined.Positions.Length - 1));
    }

    /// <summary>The box is the union, or the camera frames the body and cuts the skirt off.</summary>
    [Fact]
    public void TheBoxCoversEveryPiece()
    {
        SkinnedMesh joined = SkinnedMesh.Joined(
            [new MeshJoin(Triangle(0f, "a")), new MeshJoin(Triangle(10f, "b"))]);

        Assert.Equal(new Vector3(0f, 0f, 0f), joined.Least);
        Assert.Equal(new Vector3(11f, 1f, 0f), joined.Most);
    }

    /// <summary>One mesh with nothing to remap comes back untouched rather than copied.</summary>
    /// <remarks>
    /// THE ORDINARY MONSTER IS THE ONE WITH NO ATTACHMENTS, and it must not pay for this: the
    /// same instance back means no arrays were allocated and nothing downstream can tell the
    /// joining code was ever in the path.
    /// </remarks>
    [Fact]
    public void ABodyOnItsOwnIsNotCopied()
    {
        SkinnedMesh body = Triangle(0f, "BodyShape");

        Assert.Same(body, SkinnedMesh.Joined([new MeshJoin(body)]));
    }

    /// <summary>Pieces that did not read are left out rather than refused.</summary>
    /// <remarks>
    /// A MONSTER WITH ONE UNREADABLE ATTACHMENT IS STILL A MONSTER. Refusing the whole join over
    /// one missing file would take the body away too, which is the opposite of what a partial
    /// read should cost.
    /// </remarks>
    [Fact]
    public void APieceThatDidNotReadIsSkipped()
    {
        SkinnedMesh body = Triangle(0f, "BodyShape");

        SkinnedMesh joined = SkinnedMesh.Joined(
            [new MeshJoin(body), new MeshJoin(SkinnedMesh.None), new MeshJoin(null)]);

        Assert.Same(body, joined);
    }

    /// <summary>
    /// A piece is put where its socket is before it is joined on.
    /// </summary>
    /// <remarks>
    /// MEASURED, NOT ASSUMED. Every one of Doryani's thirteen pieces has a bounding box a few
    /// tens of units across sitting on the origin, with the left and right shoulder pieces
    /// mirrored in x rather than standing apart - so each is modelled in its OWN space and means
    /// nothing in the monster's until the socket bone's rest transform is on it. Joined without
    /// one they pile up at his feet, which is what the live client showed.
    /// </remarks>
    [Fact]
    public void APieceIsMovedToWhereItsSocketIs()
    {
        SkinnedMesh body = Triangle(0f, "BodyShape");
        SkinnedMesh piece = Triangle(0f, "SkirtShape");

        SkinnedMesh joined = SkinnedMesh.Joined(
            [new MeshJoin(body), new MeshJoin(piece, Place: Matrix4x4.CreateTranslation(0f, 0f, 50f))]);

        // The body stays put and the piece is fifty up, though both were modelled at the origin.
        Assert.Equal(new Vector3(0f, 0f, 0f), joined.Positions[0]);
        Assert.Equal(new Vector3(0f, 0f, 50f), joined.Positions[3]);

        // And the box grew to cover it, or the camera frames the body with the piece outside.
        Assert.Equal(50f, joined.Most.Z);
    }

    /// <summary>
    /// A turned piece keeps its box around its geometry, not around its old corners.
    /// </summary>
    /// <remarks>
    /// A ROTATED BOX'S MIN AND MAX ARE NOT THE TRANSFORMS OF THE OLD MIN AND MAX. Taken that way
    /// the box comes out smaller than the geometry inside it, and the camera cuts the piece off -
    /// which is the kind of wrong that looks like a rendering bug rather than an arithmetic one.
    /// </remarks>
    [Fact]
    public void ATurnedPieceKeepsABoxThatCoversIt()
    {
        SkinnedMesh piece = Triangle(0f, "SkirtShape");

        SkinnedMesh joined = SkinnedMesh.Joined(
            [new MeshJoin(Triangle(0f, "BodyShape")), new MeshJoin(piece, Place: Matrix4x4.CreateRotationZ(MathF.PI / 4f))]);

        foreach (Vector3 one in joined.Positions)
        {
            Assert.InRange(one.X, joined.Least.X, joined.Most.X);
            Assert.InRange(one.Y, joined.Least.Y, joined.Most.Y);
            Assert.InRange(one.Z, joined.Least.Z, joined.Most.Z);
        }
    }

    /// <summary>
    /// A piece moved before it is joined has exactly the box it was moved to, and nothing of its file's.
    /// </summary>
    /// <remarks>
    /// THE FIRST PIECE'S FILE BOX WAS IN EVERY UNION before v0.1.374, and nothing above caught it: each
    /// test joins a body that stays where it is first. A room's pieces all carry a place, the first as
    /// much as any - here a tile's square laid a tile along y and at the Titan Grotto's level, -875.
    /// Joined alone it must come out where it went; beside a second, as the two moved boxes.
    /// </remarks>
    [Fact]
    public void AMOVEDPieceHasExactlyTheBoxItWasMovedToAndNoneOfItsFiles()
    {
        // A tile's square, 250 a side, in its file's frame at the origin.
        SkinnedMesh tile = SkinnedMesh.Of(
            [new Vector3(0f, 0f, 0f), new Vector3(250f, 0f, 0f), new Vector3(250f, 250f, 0f), new Vector3(0f, 250f, 0f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [0, 1, 2, 0, 2, 3],
            Vector3.Zero,
            new Vector3(250f, 250f, 0f));
        Matrix4x4 laid = Matrix4x4.CreateTranslation(0f, 250f, -875f);

        SkinnedMesh alone = SkinnedMesh.Joined([new MeshJoin(tile, Place: laid)]);

        Assert.Equal(new Vector3(0f, 250f, -875f), alone.Least);
        Assert.Equal(new Vector3(250f, 500f, -875f), alone.Most);

        // A second laid beside it adds its own moved box, and still nothing of the file's.
        SkinnedMesh both = SkinnedMesh.Joined(
            [new MeshJoin(tile, Place: laid), new MeshJoin(tile, Place: laid * Matrix4x4.CreateTranslation(250f, 0f, 0f))]);

        Assert.Equal(new Vector3(0f, 250f, -875f), both.Least);
        Assert.Equal(new Vector3(500f, 500f, -875f), both.Most);
    }

    /// <summary>A translation must not tip the normals, which are directions and not places.</summary>
    [Fact]
    public void MovingAPieceLeavesItsNormalsAlone()
    {
        SkinnedMesh joined = SkinnedMesh.Joined(
        [
            new MeshJoin(Triangle(0f, "BodyShape")),
            new MeshJoin(Triangle(0f, "SkirtShape"), Place: Matrix4x4.CreateTranslation(0f, 0f, 90f)),
        ]);

        Assert.Equal(Vector3.UnitZ, joined.Normals[3]);
    }

    [Fact]
    public void NothingUsableGivesNothing()
        => Assert.Same(SkinnedMesh.None, SkinnedMesh.Joined([new MeshJoin(SkinnedMesh.None)]));
}
