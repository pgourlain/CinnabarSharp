namespace CinnabarSharp.Core.Models;

public enum GuideOrientation
{
    /// <summary>A vertical line at x = <see cref="Guide.Position"/>.</summary>
    Vertical,

    /// <summary>A horizontal line at y = <see cref="Guide.Position"/>.</summary>
    Horizontal,
}

/// <summary>A guide line: dragged out of a ruler, things snap to it. Position is in picture pixels (100 % zoom).</summary>
public sealed class Guide
{
    internal Guide(GuideOrientation orientation, double position)
    {
        Orientation = orientation;
        Position = position;
    }

    public GuideOrientation Orientation { get; }

    public double Position { get; internal set; }
}

/// <summary>The guides of a document. Not part of the history: moving a guide is not an edit of the picture.</summary>
public sealed class GuideSet
{
    private readonly List<Guide> _guides = [];

    public IReadOnlyList<Guide> Items => _guides;

    public int Count => _guides.Count;

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    public Guide Add(GuideOrientation orientation, double position)
    {
        var guide = new Guide(orientation, position);
        _guides.Add(guide);
        Changed?.Invoke();
        return guide;
    }

    public void Move(Guide guide, double position)
    {
        if (guide.Position == position || !_guides.Contains(guide))
            return;
        guide.Position = position;
        Changed?.Invoke();
    }

    public void Remove(Guide guide)
    {
        if (_guides.Remove(guide))
            Changed?.Invoke();
    }

    public void Clear()
    {
        if (_guides.Count == 0)
            return;
        _guides.Clear();
        Changed?.Invoke();
    }

    /// <summary>The nearest guide of this orientation within <paramref name="reach"/> picture pixels of <paramref name="position"/>.</summary>
    public Guide? Nearest(GuideOrientation orientation, double position, double reach) =>
        _guides.Where(g => g.Orientation == orientation && Math.Abs(g.Position - position) <= reach)
            .OrderBy(g => Math.Abs(g.Position - position)).FirstOrDefault();

    /// <summary>The position moved onto a guide of this orientation when one is within reach; null otherwise.</summary>
    public double? Snap(GuideOrientation orientation, double position, double reach) =>
        Nearest(orientation, position, reach)?.Position;
}
