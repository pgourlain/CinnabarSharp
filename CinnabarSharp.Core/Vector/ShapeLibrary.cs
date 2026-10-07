using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>A ready-made shape to place on a drawing: drawn in a 100 × 100 box, stretched to the box the user drags.</summary>
public sealed class LibraryShape
{
    private readonly Func<VectorPath> _build;
    private VectorPath? _path;

    internal LibraryShape(string category, string name, Func<VectorPath> build)
    {
        Category = category;
        Name = name;
        Id = category.ToLowerInvariant().Replace(' ', '-').Replace("&", "and") + "/" + name.ToLowerInvariant().Replace(' ', '-');
        _build = build;
    }

    /// <summary>Stable identifier, e.g. "basic/star".</summary>
    public string Id { get; }

    public string Name { get; }

    public string Category { get; }

    /// <summary>The outline fitted to the unit square (0, 0, 1, 1): built on first use.</summary>
    public VectorPath UnitPath => _path ??= ToUnit(_build());

    /// <summary>The outline as path data in a 24 × 24 box (aspect kept, centered), for the previews in the picker.</summary>
    public string PreviewData => PathDataWriter.Write(Fit(24, 24, keepAspect: true), 2);

    /// <summary>The outline stretched to <paramref name="box"/>.</summary>
    public VectorPath Place(VRect box) =>
        UnitPath.Transformed(Matrix2D.Translate(box.X, box.Y) * Matrix2D.Scale(box.Width, box.Height));

    private VectorPath Fit(double width, double height, bool keepAspect)
    {
        var bounds = _build().Bounds;
        var scale = keepAspect ? Math.Min(width / bounds.Width, height / bounds.Height) : 1;
        var sx = keepAspect ? scale : width / bounds.Width;
        var sy = keepAspect ? scale : height / bounds.Height;
        return _build().Transformed(Matrix2D.Translate((width - bounds.Width * sx) / 2, (height - bounds.Height * sy) / 2)
                                    * Matrix2D.Scale(sx, sy) * Matrix2D.Translate(-bounds.X, -bounds.Y));
    }

    private static VectorPath ToUnit(VectorPath path)
    {
        var b = path.Bounds;
        return path.Transformed(Matrix2D.Scale(1 / Math.Max(b.Width, 1e-9), 1 / Math.Max(b.Height, 1e-9)) * Matrix2D.Translate(-b.X, -b.Y));
    }
}

/// <summary>
/// The shapes the Shape tool offers, by category. Every outline is original: simple geometry (polygons, arcs, Béziers)
/// and unions or differences of those, built when first needed. Adding a shape is one line in <see cref="Build"/>.
/// </summary>
public static class ShapeLibrary
{
    private static readonly Lazy<IReadOnlyList<LibraryShape>> AllShapes = new(Build);

    public static IReadOnlyList<LibraryShape> All => AllShapes.Value;

    public static IReadOnlyList<string> Categories { get; } =
        ["Basic", "Arrows", "Symbols", "Flowchart", "Dialog balloons", "Nature", "Weather", "Objects"];

    public static IEnumerable<LibraryShape> In(string category) => All.Where(s => s.Category == category);

    public static LibraryShape? Find(string? id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>The shape the tool starts with.</summary>
    public static LibraryShape Default => All[0];

    // ---- Building blocks (a 100 × 100 box) ----

    private static VectorPath D(string data) => PathDataParser.Parse(data);

    private static VectorPath Rect(double x, double y, double w, double h, double r = 0) => VectorPath.FromRect(x, y, w, h, r, r);

    private static VectorPath Circle(double cx, double cy, double r) => VectorPath.FromEllipse(cx, cy, r, r);

    private static VectorPath Regular(int corners, double rotation = -90, double radius = 48) => Star(corners, 1, rotation, radius, plain: true);

    private static VectorPath Star(int corners, double inner, double rotation = -90, double radius = 48, bool plain = false)
    {
        var points = new List<VPoint>();
        var count = plain ? corners : corners * 2;
        for (var i = 0; i < count; i++)
        {
            var r = !plain && i % 2 == 1 ? radius * inner : radius;
            var a = (rotation + i * 360.0 / count) * Math.PI / 180;
            points.Add(new VPoint(50 + r * Math.Cos(a), 50 + r * Math.Sin(a)));
        }
        return VectorPath.FromPolyline(points, true);
    }

    private static VectorPath Union(params VectorPath[] parts) =>
        PathBoolean.Combine(parts.Select(p => (p, FillRule.NonZero)).ToList(), BooleanOperation.Union);

    private static VectorPath Minus(VectorPath shape, params VectorPath[] holes) =>
        PathBoolean.Combine([(shape, FillRule.NonZero), (Union(holes), FillRule.NonZero)], BooleanOperation.Difference);

    private static VectorPath Turn(VectorPath path, double degrees) => path.Transformed(Matrix2D.Rotate(degrees, 50, 50));

    private static VectorPath Mirror(VectorPath path) =>
        path.Transformed(Matrix2D.Translate(50, 0) * Matrix2D.Scale(-1, 1) * Matrix2D.Translate(-50, 0));

    private static VectorPath Around(Func<double, VectorPath> make, int count, double first = 0) =>
        Union(Enumerable.Range(0, count).Select(i => Turn(make(first + i * 360.0 / count), first + i * 360.0 / count)).ToArray());

    private static IReadOnlyList<LibraryShape> Build()
    {
        var shapes = new List<LibraryShape>();
        void Add(string category, string name, Func<VectorPath> build) => shapes.Add(new LibraryShape(category, name, build));

        // ---- Basic ----
        const string basic = "Basic";
        Add(basic, "Star", () => Star(5, 0.382));
        Add(basic, "Ring", () => Minus(Circle(50, 50, 47), Circle(50, 50, 29)));
        Add(basic, "Triangle", () => D("M50 8 L95 90 L5 90Z"));
        Add(basic, "Right triangle", () => D("M10 8 L10 90 L92 90Z"));
        Add(basic, "Diamond", () => D("M50 4 L96 50 L50 96 L4 50Z"));
        Add(basic, "Pentagon", () => Regular(5));
        Add(basic, "Hexagon", () => Regular(6, 0));
        Add(basic, "Heptagon", () => Regular(7));
        Add(basic, "Octagon", () => Regular(8, 22.5));
        Add(basic, "Decagon", () => Regular(10));
        Add(basic, "Circle", () => Circle(50, 50, 47));
        Add(basic, "Oval", () => VectorPath.FromEllipse(50, 50, 47, 32));
        Add(basic, "Trapezoid", () => D("M25 15 L75 15 L96 85 L4 85Z"));
        Add(basic, "Parallelogram", () => D("M25 15 L96 15 L75 85 L4 85Z"));
        Add(basic, "Heart", () => D("M50 92 C10 62 2 40 2 27 C2 12 14 4 27 4 C38 4 46 10 50 19 C54 10 62 4 73 4 C86 4 98 12 98 27 C98 40 90 62 50 92Z"));
        Add(basic, "Rounded square", () => Rect(6, 6, 88, 88, 20));
        Add(basic, "Snipped square", () => D("M25 5 L75 5 L95 25 L95 75 L75 95 L25 95 L5 75 L5 25Z"));
        Add(basic, "Pill", () => Rect(4, 28, 92, 44, 22));
        Add(basic, "Frame", () => Minus(Rect(4, 4, 92, 92), Rect(22, 22, 56, 56)));
        Add(basic, "Plus", () => Union(Rect(35, 5, 30, 90), Rect(5, 35, 90, 30)));
        Add(basic, "Minus", () => Rect(5, 38, 90, 24));
        Add(basic, "Multiply", () => Turn(Union(Rect(36, 8, 28, 84), Rect(8, 36, 84, 28)), 45));
        Add(basic, "Divide", () => Union(Rect(5, 40, 90, 20), Circle(50, 17, 11), Circle(50, 83, 11)));
        Add(basic, "Equals", () => Union(Rect(5, 24, 90, 19), Rect(5, 57, 90, 19)));
        Add(basic, "Crescent", () => Minus(Circle(50, 50, 46), Circle(68, 42, 38)));
        Add(basic, "Semicircle", () => D("M4 70 A46 46 0 0 1 96 70Z"));
        Add(basic, "Quarter circle", () => D("M8 92 L8 8 A84 84 0 0 1 92 92Z"));
        Add(basic, "Teardrop", () => D("M50 4 C50 4 88 44 88 62 C88 82 71 96 50 96 C29 96 12 82 12 62 C12 44 50 4 50 4Z"));
        Add(basic, "Chevron", () => D("M10 10 L60 10 L92 50 L60 90 L10 90 L42 50Z"));
        Add(basic, "Half frame", () => D("M6 6 L94 6 L94 28 L28 28 L28 94 L6 94Z"));
        Add(basic, "L shape", () => D("M12 6 L40 6 L40 62 L94 62 L94 94 L12 94Z"));
        Add(basic, "Cross", () => D("M32 4 L68 4 L68 32 L96 32 L96 68 L68 68 L68 96 L32 96 L32 68 L4 68 L4 32 L32 32Z"));

        // ---- Arrows ----
        const string arrows = "Arrows";
        VectorPath Right() => D("M4 36 L56 36 L56 12 L96 50 L56 88 L56 64 L4 64Z");
        Add(arrows, "Right arrow", Right);
        Add(arrows, "Left arrow", () => Mirror(Right()));
        Add(arrows, "Up arrow", () => Turn(Right(), -90));
        Add(arrows, "Down arrow", () => Turn(Right(), 90));
        VectorPath DoubleHorizontal() => D("M4 50 L30 20 L30 38 L70 38 L70 20 L96 50 L70 80 L70 62 L30 62 L30 80Z");
        Add(arrows, "Double arrow", DoubleHorizontal);
        Add(arrows, "Double arrow vertical", () => Turn(DoubleHorizontal(), 90));
        Add(arrows, "Four-way arrow", () => Union(DoubleHorizontal(), Turn(DoubleHorizontal(), 90)));
        Add(arrows, "Bent arrow", () => D("M8 94 L8 44 C8 32 16 24 28 24 L58 24 L58 8 L94 36 L58 64 L58 48 L34 48 L34 94Z"));
        Add(arrows, "U-turn arrow", () => D("M10 94 L10 42 C10 18 28 6 48 6 C68 6 86 18 86 42 L86 52 L98 52 L76 82 L54 52 L66 52 L66 42 C66 32 58 26 48 26 C38 26 30 32 30 42 L30 94Z"));
        Add(arrows, "Pentagon arrow", () => D("M4 25 L66 25 L96 50 L66 75 L4 75Z"));
        Add(arrows, "Notched arrow", () => D("M4 25 L66 25 L96 50 L66 75 L4 75 L26 50Z"));
        Add(arrows, "Play", () => D("M20 6 L92 50 L20 94Z"));
        Add(arrows, "Fast forward", () => D("M4 14 L48 50 L4 86Z M48 14 L92 50 L48 86Z"));
        Add(arrows, "Chevron arrow", () => D("M8 8 L48 8 L92 50 L48 92 L8 92 L52 50Z"));
        Add(arrows, "Curved arrow", () => D("M8 90 C8 40 30 16 64 16 L64 4 L94 26 L64 48 L64 36 C40 36 30 52 30 90Z"));
        Add(arrows, "Block arrow up-right", () => Turn(Right(), -45));
        Add(arrows, "Callout arrow", () => D("M4 30 L52 30 L52 8 L96 50 L52 92 L52 70 L4 70Z M18 42 L18 58 L40 58 L40 42Z"));

        // ---- Symbols ----
        const string symbols = "Symbols";
        Add(symbols, "Check mark", () => D("M6 54 L22 38 L40 56 L78 12 L94 28 L40 88Z"));
        Add(symbols, "Lightning", () => D("M58 4 L16 56 L44 56 L36 96 L84 38 L54 38Z"));
        Add(symbols, "Star 4", () => Star(4, 0.38));
        Add(symbols, "Star 6", () => Star(6, 0.55));
        Add(symbols, "Star 8", () => Star(8, 0.6));
        Add(symbols, "Starburst", () => Star(16, 0.75));
        Add(symbols, "Prohibited", () => Union(Minus(Circle(50, 50, 47), Circle(50, 50, 34)), Turn(Rect(44, 10, 12, 80), 45)));
        Add(symbols, "Warning", () => Minus(D("M50 6 L96 90 L4 90Z"), Rect(46, 34, 8, 30), Circle(50, 77, 5.5)));
        Add(symbols, "Shield", () => D("M50 4 L90 16 L90 50 C90 74 70 90 50 96 C30 90 10 74 10 50 L10 16Z"));
        Add(symbols, "Flag", () => Union(Rect(12, 4, 10, 92), D("M22 8 L90 8 L72 30 L90 52 L22 52Z")));
        Add(symbols, "Bookmark", () => D("M20 4 L80 4 L80 96 L50 70 L20 96Z"));
        Add(symbols, "Location pin", () => Minus(D("M50 96 C50 96 16 58 16 36 C16 16 31 4 50 4 C69 4 84 16 84 36 C84 58 50 96 50 96Z"), Circle(50, 36, 12)));
        Add(symbols, "Gear", () => Minus(Union(Circle(50, 50, 34), Around(a => Rect(43, 6, 14, 22), 8)), Circle(50, 50, 14)));
        Add(symbols, "Target", () => Union(Minus(Circle(50, 50, 47), Circle(50, 50, 38)), Minus(Circle(50, 50, 28), Circle(50, 50, 18)), Circle(50, 50, 8)));
        Add(symbols, "Information", () => Minus(Circle(50, 50, 47), Circle(50, 24, 6.5), Rect(44, 38, 12, 38)));
        Add(symbols, "Question", () => Minus(Circle(50, 50, 47), D("M36 38 C36 28 42 22 51 22 C60 22 66 28 66 36 C66 44 58 46 54 52 L54 58 L46 58 L46 50 C46 44 54 42 56 38 C57 34 54 30 51 30 C47 30 44 33 44 38Z"), Circle(50, 72, 5.5)));
        Add(symbols, "Plus in circle", () => Minus(Circle(50, 50, 47), Rect(44, 22, 12, 56), Rect(22, 44, 56, 12)));
        Add(symbols, "Checked circle", () => Minus(Circle(50, 50, 47), D("M24 52 L34 42 L44 52 L68 28 L78 38 L44 72Z")));
        Add(symbols, "Recycle triangle", () => Minus(D("M50 6 L96 90 L4 90Z"), D("M50 32 L76 80 L24 80Z")));

        // ---- Flowchart ----
        const string flow = "Flowchart";
        Add(flow, "Process", () => Rect(4, 18, 92, 64));
        Add(flow, "Decision", () => D("M50 8 L96 50 L50 92 L4 50Z"));
        Add(flow, "Terminator", () => Rect(4, 25, 92, 50, 25));
        Add(flow, "Data", () => D("M22 20 L96 20 L78 80 L4 80Z"));
        Add(flow, "Document", () => D("M6 12 L94 12 L94 78 C80 70 66 70 50 78 C34 86 20 86 6 78Z"));
        Add(flow, "Predefined process", () => Minus(Rect(4, 18, 92, 64), Rect(18, 18, 3, 64), Rect(79, 18, 3, 64)));
        Add(flow, "Manual input", () => D("M4 38 L96 14 L96 86 L4 86Z"));
        Add(flow, "Connector", () => Circle(50, 50, 40));
        Add(flow, "Off-page connector", () => D("M8 8 L92 8 L92 60 L50 94 L8 60Z"));
        Add(flow, "Preparation", () => D("M20 14 L80 14 L97 50 L80 86 L20 86 L3 50Z"));
        Add(flow, "Delay", () => D("M6 14 L54 14 A36 36 0 0 1 54 86 L6 86Z"));
        Add(flow, "Display", () => D("M4 50 L24 14 L76 14 A36 36 0 0 1 76 86 L24 86Z"));
        Add(flow, "Stored data", () => D("M20 14 L94 14 C80 30 80 70 94 86 L20 86 C6 70 6 30 20 14Z"));
        Add(flow, "Database", () => Minus(D("M10 20 C10 4 90 4 90 20 L90 80 C90 96 10 96 10 80Z"), D("M10 20 C10 34 90 34 90 20 C90 28 10 28 10 20Z")));
        Add(flow, "Merge", () => D("M6 10 L94 10 L50 92Z"));
        Add(flow, "Extract", () => D("M50 8 L94 90 L6 90Z"));
        Add(flow, "Summing junction", () => Minus(Circle(50, 50, 46), Turn(Rect(46, 2, 8, 96), 45), Turn(Rect(46, 2, 8, 96), -45)));
        Add(flow, "Or", () => Minus(Circle(50, 50, 46), Rect(46, 2, 8, 96), Rect(2, 46, 96, 8)));
        Add(flow, "Sort", () => Minus(D("M50 4 L96 50 L50 96 L4 50Z"), Rect(10, 47, 80, 6)));
        Add(flow, "Internal storage", () => Minus(Rect(4, 10, 92, 80), Rect(22, 10, 3, 80), Rect(4, 28, 92, 3)));

        // ---- Dialog balloons ----
        const string balloons = "Dialog balloons";
        Add(balloons, "Rounded speech", () => Union(Rect(4, 6, 92, 62, 14), D("M20 60 L16 94 L50 64Z")));
        Add(balloons, "Rounded speech right", () => Mirror(Union(Rect(4, 6, 92, 62, 14), D("M20 60 L16 94 L50 64Z"))));
        Add(balloons, "Oval speech", () => Union(VectorPath.FromEllipse(50, 40, 47, 34), D("M26 64 L14 94 L50 72Z")));
        Add(balloons, "Rectangular speech", () => Union(Rect(4, 6, 92, 62), D("M20 66 L14 94 L46 66Z")));
        Add(balloons, "Square speech", () => Union(Rect(8, 6, 84, 70, 6), D("M44 74 L50 96 L60 74Z")));
        Add(balloons, "Thought", () => Union(Circle(28, 38, 22), Circle(52, 28, 24), Circle(74, 38, 22), Circle(50, 50, 26), Circle(24, 78, 7), Circle(10, 92, 4)));
        Add(balloons, "Shout", () => Star(12, 0.7, -90, 48));
        Add(balloons, "Spiky shout", () => Star(14, 0.55, -90, 48));
        Add(balloons, "Rounded caption", () => Rect(4, 20, 92, 60, 30));
        Add(balloons, "Heart speech", () => Union(D("M50 80 C14 56 6 38 6 28 C6 14 18 6 30 6 C40 6 47 11 50 19 C53 11 60 6 70 6 C82 6 94 14 94 28 C94 38 86 56 50 80Z"), D("M44 74 L38 96 L58 76Z")));

        // ---- Nature ----
        const string nature = "Nature";
        Add(nature, "Leaf", () => D("M10 90 C10 40 40 8 92 8 C92 60 62 92 10 90Z"));
        Add(nature, "Drop", () => D("M50 4 C50 4 86 42 86 62 C86 82 70 96 50 96 C30 96 14 82 14 62 C14 42 50 4 50 4Z"));
        Add(nature, "Flower", () => Minus(Around(a => Circle(50, 24, 20), 5), Circle(50, 50, 9)));
        Add(nature, "Sun", () => Union(Circle(50, 50, 24), Around(a => Rect(46, 2, 8, 18), 8)));
        Add(nature, "Moon", () => Minus(Circle(50, 50, 46), Circle(68, 42, 38)));
        Add(nature, "Cloud", () => Union(Circle(30, 58, 20), Circle(52, 42, 26), Circle(76, 58, 20), Rect(30, 58, 46, 20)));
        Add(nature, "Mountain", () => D("M2 90 L34 34 L52 62 L66 42 L98 90Z"));
        Add(nature, "Pine tree", () => D("M50 4 L78 40 L64 40 L88 72 L56 72 L56 96 L44 96 L44 72 L12 72 L36 40 L22 40Z"));
        Add(nature, "Snowflake", () => Around(a => Rect(46, 4, 8, 92), 3, 0));
        Add(nature, "Clover", () => Union(Circle(34, 34, 22), Circle(66, 34, 22), Circle(34, 66, 22), Circle(66, 66, 22)));
        Add(nature, "Wave", () => D("M2 56 C18 30 34 30 50 56 C66 82 82 82 98 56 L98 80 C82 106 66 106 50 80 C34 54 18 54 2 80Z"));
        Add(nature, "Paw", () => Union(VectorPath.FromEllipse(50, 66, 28, 24), Circle(18, 42, 11), Circle(38, 22, 11), Circle(62, 22, 11), Circle(82, 42, 11)));

        // ---- Weather ----
        const string weather = "Weather";
        Add(weather, "Cloud", () => Union(Circle(30, 58, 20), Circle(52, 42, 26), Circle(76, 58, 20), Rect(30, 58, 46, 20)));
        Add(weather, "Sun", () => Union(Circle(50, 50, 22), Around(a => Rect(46, 3, 8, 20), 12)));
        Add(weather, "Rain", () => Union(Circle(30, 40, 17), Circle(50, 28, 22), Circle(72, 40, 17), Rect(30, 40, 42, 18),
            D("M26 66 L32 66 L26 84 L20 84Z"), D("M46 66 L52 66 L46 84 L40 84Z"), D("M66 66 L72 66 L66 84 L60 84Z")));
        Add(weather, "Snow", () => Union(Circle(30, 40, 17), Circle(50, 28, 22), Circle(72, 40, 17), Rect(30, 40, 42, 18),
            Circle(28, 74, 5), Circle(50, 82, 5), Circle(72, 74, 5)));
        Add(weather, "Umbrella", () => Union(D("M4 50 C4 24 24 6 50 6 C76 6 96 24 96 50 C86 42 76 42 66 50 C58 42 42 42 34 50 C24 42 14 42 4 50Z"),
            D("M46 50 L54 50 L54 84 C54 96 34 96 34 84 L41 84 C41 88 46 88 46 84Z")));
        Add(weather, "Hourglass", () => D("M20 4 L80 4 L80 18 C80 36 60 44 56 50 C60 56 80 64 80 82 L80 96 L20 96 L20 82 C20 64 40 56 44 50 C40 44 20 36 20 18Z"));
        Add(weather, "Thermometer", () => Union(Rect(40, 4, 20, 66, 10), Circle(50, 78, 18)));
        Add(weather, "Wind", () => D("M4 30 L62 30 C78 30 78 10 62 10 L60 18 C66 18 66 22 62 22 L4 22Z M4 54 L80 54 C94 54 94 36 80 36 L78 44 C84 44 84 46 80 46 L4 46Z M4 78 L52 78 C66 78 66 62 52 62 L50 70 C56 70 56 70 52 70 L4 70Z"));

        // ---- Objects ----
        const string objects = "Objects";
        Add(objects, "House", () => Minus(D("M50 6 L96 50 L82 50 L82 94 L18 94 L18 50 L4 50Z"), Rect(42, 62, 16, 32)));
        Add(objects, "Envelope", () => Minus(Rect(4, 18, 92, 64, 6), D("M4 24 L50 58 L96 24 L96 32 L50 66 L4 32Z")));
        Add(objects, "Folder", () => D("M4 18 L38 18 L46 28 L96 28 L96 88 L4 88Z"));
        Add(objects, "Page", () => D("M16 4 L62 4 L84 26 L84 96 L16 96Z"));
        Add(objects, "Light bulb", () => Union(Circle(50, 38, 30), Rect(36, 62, 28, 18, 4), Rect(40, 82, 20, 12, 4)));
        Add(objects, "Magnifier", () => Union(Minus(Circle(40, 40, 34), Circle(40, 40, 22)), D("M62 68 L72 58 L96 82 L86 92Z")));
        Add(objects, "Crown", () => D("M6 82 L6 28 L28 52 L50 14 L72 52 L94 28 L94 82Z"));
        Add(objects, "Trophy", () => Union(D("M26 6 L74 6 L74 34 C74 52 62 60 50 60 C38 60 26 52 26 34Z"), Rect(44, 58, 12, 22), Rect(28, 80, 44, 14, 3),
            Minus(Circle(22, 26, 14), Circle(22, 26, 8)), Minus(Circle(78, 26, 14), Circle(78, 26, 8))));
        Add(objects, "Key", () => Union(Minus(Circle(28, 50, 24), Circle(28, 50, 10)), Rect(48, 44, 48, 12), Rect(72, 56, 8, 14), Rect(86, 56, 8, 10)));
        Add(objects, "Lock", () => Union(Rect(14, 44, 72, 50, 8), Minus(D("M28 46 L28 30 C28 14 72 14 72 30 L72 46Z"), D("M38 46 L38 32 C38 24 62 24 62 32 L62 46Z"))));
        Add(objects, "Speaker", () => Union(D("M6 36 L30 36 L56 12 L56 88 L30 64 L6 64Z"), D("M66 34 C78 44 78 56 66 66 L72 72 C90 56 90 44 72 28Z")));
        Add(objects, "Bell", () => Union(D("M50 6 C34 6 24 20 24 38 L24 56 L10 74 L90 74 L76 56 L76 38 C76 20 66 6 50 6Z"), Circle(50, 86, 9)));
        Add(objects, "Camera", () => Minus(Union(Rect(4, 24, 92, 64, 8), D("M32 24 L38 10 L62 10 L68 24Z")), Circle(50, 56, 20)));
        Add(objects, "Cup", () => Union(Rect(14, 24, 56, 64, 8), Minus(Circle(76, 52, 20), Circle(76, 52, 11))));
        return shapes;
    }
}
