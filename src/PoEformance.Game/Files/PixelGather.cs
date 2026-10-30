using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// What every solid pixel of a drawing was made of - its triangle, depth, place and normal, and its light term by term
/// (<see cref="SceneLight.LightTerms"/>) - for a capture that holds the picture against the game's, pixel by pixel.
/// </summary>
/// <remarks>
/// WRITTEN WHERE THE DRAWING LIGHTS THE PIXEL (MeshPicture's Scened), from the same numbers it draws with, so what is said of a pixel is what
/// was drawn there - not a second pass that could disagree with it. A nearer triangle later in the mesh writes over a farther one, as its
/// colour does; what lies on top afterwards - a translucent layer, the beach, the lifted water - is read off the canvas
/// (<see cref="MeshPicture.Canvas.Layers"/> and <see cref="MeshPicture.Canvas.Depths"/>), not written here.
///
/// EVERY <see cref="Step"/>-TH PIXEL EACH WAY: a gathered pixel costs some 160 bytes, and a capture's statistics want a few hundred thousand
/// of them, not the screen's millions. Each band writes its own rows, so no two threads touch one entry.
/// </remarks>
public sealed class PixelGather
{
    /// <param name="side">The canvas's side the drawing is made on, pixels.</param>
    /// <param name="step">Every how many pixels each way one is gathered - one for every pixel.</param>
    public PixelGather(int side, int step = 1)
    {
        Side = Math.Max(1, side);
        Step = Math.Max(1, step);
        Width = (Side + Step - 1) / Step;
        int count = Width * Width;
        Triangle = new int[count];
        Depth = new float[count];
        Place = new Vector3[count];
        Normal = new Vector3[count];
        Terms = new SceneLight.LightTerms[count];
        Water = new bool[count];
        Clear();
    }

    /// <summary>The canvas's side the gather was made for, pixels.</summary>
    public int Side { get; }

    /// <summary>Every how many pixels each way one is gathered.</summary>
    public int Step { get; }

    /// <summary>How many gathered pixels each way: entry (x, y) is the canvas's pixel (x, y) times <see cref="Step"/>.</summary>
    public int Width { get; }

    /// <summary>The solid triangle drawn at each gathered pixel, or -1 for none.</summary>
    public int[] Triangle { get; }

    /// <summary>Its depth along the view.</summary>
    public float[] Depth { get; }

    /// <summary>Its place, model space.</summary>
    public Vector3[] Place { get; }

    /// <summary>The normal it was lit with, model space, turned to the eye.</summary>
    public Vector3[] Normal { get; }

    /// <summary>Its light term by term.</summary>
    public SceneLight.LightTerms[] Terms { get; }

    /// <summary>Whether it is water drawn solid, lit by the water pass's own arithmetic - whose terms are not split, only its colour kept in <see cref="SceneLight.LightTerms.Graded"/>.</summary>
    public bool[] Water { get; }

    /// <summary>Forgets every pixel.</summary>
    public void Clear()
    {
        Array.Fill(Triangle, -1);
        Array.Fill(Depth, float.MaxValue);
        Array.Clear(Water);
    }

    /// <summary>The entry a canvas pixel writes, or -1 where it is not one gathered.</summary>
    internal int IndexOf(int x, int y)
    {
        if (x % Step != 0 || y % Step != 0)
        {
            return -1;
        }

        int gx = x / Step, gy = y / Step;
        return gx < Width && gy < Width ? (gy * Width) + gx : -1;
    }

    /// <summary>Writes one gathered pixel.</summary>
    internal void Write(int at, int triangle, float depth, Vector3 place, Vector3 normal, in SceneLight.LightTerms terms, bool water = false)
    {
        Triangle[at] = triangle;
        Depth[at] = depth;
        Place[at] = place;
        Normal[at] = normal;
        Terms[at] = terms;
        Water[at] = water;
    }
}
