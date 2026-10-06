using System.Xml.Linq;

namespace CinnabarSharp.Vector;

/// <summary>An element that draws a path of its own (path, rect, circle…).</summary>
public abstract class SvgShape : SvgElement
{
    protected SvgShape(XName name) : base(name)
    {
    }

    /// <summary>The geometry in the element's own coordinates (its <c>transform</c> not applied).</summary>
    public abstract VectorPath CreatePath();

    /// <summary>Bounding box of the geometry in the element's own coordinates (stroke not included).</summary>
    public VRect GeometryBounds => CreatePath().Bounds;
}

public sealed class SvgPath : SvgShape
{
    private string? _parsedFrom;
    private VectorPath? _path;

    public SvgPath(XName? name = null) : base(name ?? Ns + "path")
    {
    }

    /// <summary>The <c>d</c> attribute text.</summary>
    public string Data
    {
        get => GetAttribute("d") ?? "";
        set => SetAttribute("d", value);
    }

    public override VectorPath CreatePath()
    {
        var text = Data;
        if (_path is null || text != _parsedFrom)
        {
            _path = PathDataParser.Parse(text);
            _parsedFrom = text;
        }
        return _path.Clone();
    }

    /// <summary>Replaces the geometry (compact absolute data).</summary>
    public void SetPath(VectorPath path, int decimals = 3) => Data = PathDataWriter.Write(path, decimals);
}

public sealed class SvgRect : SvgShape
{
    public SvgRect(XName? name = null) : base(name ?? Ns + "rect")
    {
    }

    public double X { get => GetLength("x", 0, LengthAxis.X); set => SetNumber("x", value); }
    public double Y { get => GetLength("y", 0, LengthAxis.Y); set => SetNumber("y", value); }
    public double Width { get => GetLength("width", 0, LengthAxis.X); set => SetNumber("width", value); }
    public double Height { get => GetLength("height", 0, LengthAxis.Y); set => SetNumber("height", value); }

    /// <summary>The <c>rx</c> attribute as written (0 when absent; see <see cref="EffectiveRadii"/> for what is drawn).</summary>
    public double Rx { get => GetLength("rx", 0, LengthAxis.X); set => SetNumber("rx", value); }
    public double Ry { get => GetLength("ry", 0, LengthAxis.Y); set => SetNumber("ry", value); }

    /// <summary>Corner radii as drawn: a missing one copies the other, both are limited to half the size.</summary>
    public (double Rx, double Ry) EffectiveRadii
    {
        get
        {
            var hasRx = HasAttribute("rx") && Rx > 0;
            var hasRy = HasAttribute("ry") && Ry > 0;
            var rx = hasRx ? Rx : hasRy ? Ry : 0;
            var ry = hasRy ? Ry : hasRx ? Rx : 0;
            return (Math.Min(rx, Width / 2), Math.Min(ry, Height / 2));
        }
    }

    public override VectorPath CreatePath()
    {
        if (Width <= 0 || Height <= 0)
            return new VectorPath();
        var (rx, ry) = EffectiveRadii;
        return VectorPath.FromRect(X, Y, Width, Height, rx, ry);
    }
}

public sealed class SvgCircle : SvgShape
{
    public SvgCircle(XName? name = null) : base(name ?? Ns + "circle")
    {
    }

    public double Cx { get => GetLength("cx", 0, LengthAxis.X); set => SetNumber("cx", value); }
    public double Cy { get => GetLength("cy", 0, LengthAxis.Y); set => SetNumber("cy", value); }
    public double R { get => GetLength("r", 0, LengthAxis.Diagonal); set => SetNumber("r", value); }

    public override VectorPath CreatePath() => VectorPath.FromEllipse(Cx, Cy, R, R);
}

public sealed class SvgEllipse : SvgShape
{
    public SvgEllipse(XName? name = null) : base(name ?? Ns + "ellipse")
    {
    }

    public double Cx { get => GetLength("cx", 0, LengthAxis.X); set => SetNumber("cx", value); }
    public double Cy { get => GetLength("cy", 0, LengthAxis.Y); set => SetNumber("cy", value); }
    public double Rx { get => GetLength("rx", 0, LengthAxis.X); set => SetNumber("rx", value); }
    public double Ry { get => GetLength("ry", 0, LengthAxis.Y); set => SetNumber("ry", value); }

    public override VectorPath CreatePath()
    {
        // A missing radius copies the other (SVG 2).
        var rx = HasAttribute("rx") ? Rx : Ry;
        var ry = HasAttribute("ry") ? Ry : Rx;
        return VectorPath.FromEllipse(Cx, Cy, rx, ry);
    }
}

public sealed class SvgLine : SvgShape
{
    public SvgLine(XName? name = null) : base(name ?? Ns + "line")
    {
    }

    public double X1 { get => GetLength("x1", 0, LengthAxis.X); set => SetNumber("x1", value); }
    public double Y1 { get => GetLength("y1", 0, LengthAxis.Y); set => SetNumber("y1", value); }
    public double X2 { get => GetLength("x2", 0, LengthAxis.X); set => SetNumber("x2", value); }
    public double Y2 { get => GetLength("y2", 0, LengthAxis.Y); set => SetNumber("y2", value); }

    public override VectorPath CreatePath() => new VectorPath().MoveTo(X1, Y1).LineTo(X2, Y2);
}

/// <summary>The common part of polyline and polygon: a <c>points</c> list.</summary>
public abstract class SvgPoly : SvgShape
{
    protected SvgPoly(XName name) : base(name)
    {
    }

    public IReadOnlyList<VPoint> Points
    {
        get
        {
            var numbers = GetNumberList("points");
            var points = new List<VPoint>(numbers.Length / 2);
            for (var i = 0; i + 1 < numbers.Length; i += 2)
                points.Add(new VPoint(numbers[i], numbers[i + 1]));
            return points;
        }
        set => SetAttribute("points", string.Join(' ', value.Select(p => $"{NumberFormat.Format(p.X, 6)},{NumberFormat.Format(p.Y, 6)}")));
    }

    protected abstract bool Closed { get; }

    public override VectorPath CreatePath()
    {
        var points = Points;
        return points.Count < 2 ? new VectorPath() : VectorPath.FromPolyline(points, Closed);
    }
}

public sealed class SvgPolyline : SvgPoly
{
    public SvgPolyline(XName? name = null) : base(name ?? Ns + "polyline")
    {
    }

    protected override bool Closed => false;
}

public sealed class SvgPolygon : SvgPoly
{
    public SvgPolygon(XName? name = null) : base(name ?? Ns + "polygon")
    {
    }

    protected override bool Closed => true;
}
