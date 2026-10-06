using System.Text.RegularExpressions;
using System.Xml.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// The only way the UI and MCP edit an SVG document: each action changes the tree and pushes one history entry that can
/// undo and redo it (and whose undo restores the XML exactly: the nodes it touched become clean again). Nodes are found
/// again by internal id, not by XML id, which can be missing or duplicated. Moves and resizes keep the element's natural
/// form where they can (see <see cref="SvgTransformer"/>). Without an explicit list, actions work on the selection.
/// </summary>
public sealed partial class SvgActions
{
    private readonly SvgDocument _document;

    internal SvgActions(SvgDocument document) => _document = document;

    private SvgRoot Root => _document.Root;

    private IGlyphOutlineProvider? Provider => _document.GlyphProvider;

    // ---- Transaction: performs primitive edits and records a step for each ----

    private sealed class Transaction(SvgDocument document, string name)
    {
        private readonly List<VectorStepItem> _steps = [];
        private readonly List<long> _selectionBefore = document.Selection.Nodes.Select(n => n.InternalId).ToList();

        public bool HasSteps => _steps.Count > 0;

        public void Insert(SvgContainer parent, int index, SvgNode node)
        {
            index = Math.Clamp(index, 0, parent.Children.Count);
            parent.InsertChild(index, node);
            _steps.Add(new VectorInsertItem(document, name, [new NodePlace(parent.InternalId, index, node)]));
        }

        public void Remove(SvgNode node)
        {
            var parent = node.Parent ?? throw new InvalidOperationException("The root cannot be removed.");
            var index = parent.RemoveChild(node);
            _steps.Add(new VectorDeleteItem(document, name, [new NodePlace(parent.InternalId, index, node)]));
        }

        /// <summary>Moves a node to <paramref name="newIndex"/> (its index once it has been taken out of its old place).</summary>
        public void Move(SvgNode node, SvgContainer newParent, int newIndex)
        {
            var oldParent = node.Parent ?? throw new InvalidOperationException("The root cannot be moved.");
            var oldIndex = oldParent.RemoveChild(node);
            newIndex = Math.Clamp(newIndex, 0, newParent.Children.Count);
            newParent.InsertChild(newIndex, node);
            _steps.Add(new VectorReorderItem(document, name, [new NodeMove(node.InternalId, oldParent.InternalId, oldIndex, newParent.InternalId, newIndex)]));
        }

        /// <summary>Runs <paramref name="edit"/> and records the elements whose attributes (or text) it changed.</summary>
        public void Edit(IEnumerable<SvgElement> nodes, Action edit)
        {
            var list = nodes.Distinct().ToList();
            var before = list.Select(n => (Node: n, Snapshot: NodeSnapshot.Capture(n), Bounds: SvgBounds.Visual(n, document.GlyphProvider))).ToList();
            edit();
            var changes = new List<NodeChange>();
            foreach (var (node, snapshot, bounds) in before)
            {
                var after = NodeSnapshot.Capture(node);
                if (after.SameAs(snapshot))
                    continue;
                var boundsAfter = SvgBounds.Visual(node, document.GlyphProvider);
                changes.Add(new NodeChange(node.InternalId, snapshot, after, bounds, boundsAfter));
                document.NotifyNodeChanged(node, bounds is { } b ? boundsAfter is { } a ? b.Union(a) : b : boundsAfter);
            }
            if (changes.Count > 0)
                _steps.Add(new VectorNodeChangeItem(document, name, changes));
        }

        /// <summary>
        /// Pushes what was recorded as one history entry (false when nothing changed). <paramref name="select"/> becomes the
        /// selection; undo and redo restore the selection from before and after.
        /// </summary>
        public bool Commit(IEnumerable<SvgElement>? select = null)
        {
            if (_steps.Count == 0)
            {
                if (select is not null)
                    document.Selection.Set(select);
                return false;
            }
            if (select is not null)
                document.Selection.Set(select);
            document.Selection.Prune();
            VectorStepItem item = _steps.Count == 1 ? _steps[0] : new VectorCompositeItem(document, name, _steps);
            item.SelectionBefore = _selectionBefore;
            item.SelectionAfter = document.Selection.Nodes.Select(n => n.InternalId).ToList();
            document.Workspace.History.PushNewItem(item);
            if (_steps.Any(s => s is not VectorNodeChangeItem))
                document.NotifyTreeChanged();
            return true;
        }
    }

    private Transaction Begin(string name) => new(_document, name);

    // ---- Helpers ----

    private List<SvgElement> Targets(IEnumerable<SvgElement>? nodes) =>
        (nodes ?? _document.Selection.Nodes).Where(n => n.Parent is not null && n.DocumentRoot == Root).Distinct().ToList();

    /// <summary>Document order of the nodes, and only those that are not inside another node of the list.</summary>
    private List<SvgElement> TopLevel(IEnumerable<SvgElement>? nodes)
    {
        var set = Targets(nodes).ToHashSet();
        var order = DocumentOrder();
        return set.Where(n => !n.Ancestors().Any(set.Contains)).OrderBy(n => order[n]).ToList();
    }

    private Dictionary<SvgNode, int> DocumentOrder()
    {
        var order = new Dictionary<SvgNode, int>();
        foreach (var node in Root.SelfAndDescendants())
            order[node] = order.Count;
        return order;
    }

    private static Matrix2D WorldOf(SvgContainer? parent) => parent is null ? Matrix2D.Identity : SvgBounds.ToDocument(parent);

    /// <summary>The same document-space change written in the coordinates of the node's parent.</summary>
    private static Matrix2D ToParentSpace(SvgElement node, Matrix2D documentMatrix)
    {
        var world = WorldOf(node.Parent);
        return world.Invert() is { } inverse ? inverse * documentMatrix * world : Matrix2D.Identity;
    }

    private void EnsureNamespace(Transaction tx, string prefix, XNamespace ns)
    {
        var name = XNamespace.Xmlns + prefix;
        if (Root.HasAttribute(name))
            return;
        tx.Edit([Root], () => Root.SetAttribute(name, ns.NamespaceName));
    }

    // ---- Add, delete, duplicate ----

    /// <summary>Adds an element (new id if it has none, and unique ids inside it) to the layer or <paramref name="parent"/> and selects it.</summary>
    public SvgElement AddNode(SvgElement node, SvgContainer? parent = null, int? index = null, string name = "Add Object")
    {
        parent ??= SvgDocumentFactory.DefaultParent(Root);
        if (string.IsNullOrEmpty(node.Id))
            node.Id = Root.NewId(IdPrefix(node));
        MakeIdsUnique(node);
        var tx = Begin(name);
        tx.Insert(parent, index ?? parent.Children.Count, node);
        tx.Commit([node]);
        return node;
    }

    private static string IdPrefix(SvgElement node) => node.ElementName;

    public void DeleteSelection() => Delete(null);

    public void Delete(IEnumerable<SvgElement>? nodes)
    {
        var targets = TopLevel(nodes);
        if (targets.Count == 0)
            return;
        var tx = Begin(targets.Count == 1 ? $"Delete {targets[0].Label}" : "Delete Objects");
        // Last first: every index is still right at the time it is removed.
        foreach (var node in Enumerable.Reverse(targets))
            tx.Remove(node);
        tx.Commit([]);
    }

    /// <summary>Copies the elements in place (just above the originals, new ids) and selects the copies.</summary>
    public IReadOnlyList<SvgElement> Duplicate(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes);
        if (targets.Count == 0)
            return [];
        var tx = Begin("Duplicate");
        var copies = new List<SvgElement>();
        foreach (var node in targets)
        {
            var copy = (SvgElement)node.DeepClone();
            MakeIdsUnique(copy);
            var parent = node.Parent!;
            tx.Insert(parent, parent.IndexOf(node) + 1, copy);
            copies.Add(copy);
        }
        tx.Commit(copies);
        return copies;
    }

    // ---- Moving, resizing, rotating ----

    /// <summary>Moves by (<paramref name="dx"/>, <paramref name="dy"/>) user units of the document.</summary>
    public void MoveBy(IEnumerable<SvgElement>? nodes, double dx, double dy) =>
        Transform(nodes, Matrix2D.Translate(dx, dy), "Move");

    /// <summary>Applies a transformation given in the document's user space to each element, in the most natural form.</summary>
    public void Transform(IEnumerable<SvgElement>? nodes, Matrix2D documentMatrix, string name = "Transform")
    {
        var targets = TopLevel(nodes);
        if (targets.Count == 0 || documentMatrix.IsIdentity)
            return;
        var tx = Begin(name);
        tx.Edit(targets, () =>
        {
            foreach (var node in targets)
                SvgTransformer.Apply(node, ToParentSpace(node, documentMatrix), Provider);
        });
        tx.Commit();
    }

    /// <summary>Sets the transform attribute itself.</summary>
    public void SetTransform(SvgElement node, Matrix2D transform)
    {
        var tx = Begin("Set Transform");
        tx.Edit([node], () => node.Transform = transform);
        tx.Commit();
    }

    /// <summary>The geometric bounding box (no stroke) of the elements together, in the document's user space.</summary>
    public VRect? BoundsOf(IEnumerable<SvgElement>? nodes = null)
    {
        VRect? box = null;
        foreach (var node in Targets(nodes))
            if (SvgBounds.InDocument(node, Provider) is { } b)
                box = box is { } current ? current.Union(b) : b;
        return box;
    }

    /// <summary>Scales and moves the elements so that their bounding box becomes <paramref name="newBox"/> (the Properties panel's X, Y, W, H).</summary>
    public void Resize(IEnumerable<SvgElement>? nodes, VRect newBox)
    {
        var targets = TopLevel(nodes);
        if (BoundsOf(targets) is not { } old || newBox.Width <= 0 || newBox.Height <= 0)
            return;
        var sx = old.Width > 1e-9 ? newBox.Width / old.Width : 1;
        var sy = old.Height > 1e-9 ? newBox.Height / old.Height : 1;
        var matrix = Matrix2D.Translate(newBox.X, newBox.Y) * Matrix2D.Scale(sx, sy) * Matrix2D.Translate(-old.X, -old.Y);
        Transform(targets, matrix, "Resize");
    }

    /// <summary>Rotates clockwise (degrees) around <paramref name="center"/>, or the middle of the selection's box.</summary>
    public void Rotate(IEnumerable<SvgElement>? nodes, double degrees, VPoint? center = null)
    {
        var targets = TopLevel(nodes);
        var around = center ?? BoundsOf(targets)?.Center;
        if (around is not { } c || Math.Abs(degrees % 360) < 1e-12)
            return;
        Transform(targets, Matrix2D.Rotate(degrees, c.X, c.Y), "Rotate");
    }

    /// <summary>Mirrors around the vertical or horizontal axis through the middle of the box of the elements.</summary>
    public void Flip(IEnumerable<SvgElement>? nodes, bool horizontal)
    {
        var targets = TopLevel(nodes);
        if (BoundsOf(targets)?.Center is not { } c)
            return;
        var matrix = horizontal
            ? Matrix2D.Translate(c.X, 0) * Matrix2D.Scale(-1, 1) * Matrix2D.Translate(-c.X, 0)
            : Matrix2D.Translate(0, c.Y) * Matrix2D.Scale(1, -1) * Matrix2D.Translate(0, -c.Y);
        Transform(targets, matrix, horizontal ? "Flip Horizontal" : "Flip Vertical");
    }

    // ---- Style and attributes ----

    public void SetStyle(IEnumerable<SvgElement>? nodes, string property, string? value, string? name = null) =>
        SetStyle(nodes, new Dictionary<string, string?> { [property] = value }, name ?? $"Set {property}");

    /// <summary>Sets (or with a null value removes) several properties on every element as one step.</summary>
    public void SetStyle(IEnumerable<SvgElement>? nodes, IReadOnlyDictionary<string, string?> properties, string name)
    {
        var targets = Targets(nodes);
        if (targets.Count == 0)
            return;
        var tx = Begin(name);
        tx.Edit(targets, () =>
        {
            foreach (var node in targets)
                foreach (var (property, value) in properties)
                    node.Style.Set(property, value);
        });
        tx.Commit();
    }

    public void SetFill(IEnumerable<SvgElement>? nodes, SvgPaint? paint) => SetStyle(nodes, "fill", paint?.ToText(), "Set Fill");

    public void SetStroke(IEnumerable<SvgElement>? nodes, SvgPaint? paint) => SetStyle(nodes, "stroke", paint?.ToText(), "Set Stroke");

    public void SetAttribute(SvgElement node, XName name, string? value, string actionName = "Set Attribute")
    {
        var tx = Begin(actionName);
        tx.Edit([node], () => node.SetAttribute(name, value));
        tx.Commit();
    }

    /// <summary>Runs an edit of the given elements as one undoable step (used by tools that change attributes or text).</summary>
    public void Edit(string name, IEnumerable<SvgElement> nodes, Action edit)
    {
        var tx = Begin(name);
        tx.Edit(nodes, edit);
        tx.Commit();
    }

    /// <summary>Replaces the content of a text element with plain text.</summary>
    public void SetText(SvgText text, string content) => Edit("Edit Text", [text], () => text.SetPlainText(content));

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex IdPattern();

    /// <summary>Renames the XML id; references to it (url(#id), href) follow. The new id must be unused and a valid name.</summary>
    public void SetId(SvgElement node, string id)
    {
        id = id.Trim();
        if (!IdPattern().IsMatch(id))
            throw new ArgumentException($"'{id}' is not a valid id: use letters, digits, '_', '-' and '.', starting with a letter or '_'.");
        if (Root.FindById(id) is { } other && other != node)
            throw new ArgumentException($"The id '{id}' is already used by another object.");
        var old = node.Id;
        if (old == id)
            return;
        var references = old is null ? [] : ReferencesTo(old).ToList();
        var tx = Begin("Rename");
        tx.Edit([node, .. references.Select(r => r.Element)], () =>
        {
            node.Id = id;
            foreach (var (element, attribute) in references)
                element.SetAttribute(attribute.Name, ReplaceReference(attribute.Value, old!, id));
        });
        tx.Commit();
    }

    /// <summary>The label shown in the Objects panel (Inkscape's <c>inkscape:label</c>); empty removes it.</summary>
    public void SetLabel(SvgElement node, string label)
    {
        var tx = Begin("Rename");
        if (!string.IsNullOrEmpty(label))
            EnsureNamespace(tx, "inkscape", SvgElement.Inkscape);
        tx.Edit([node], () => node.SetAttribute(SvgElement.Inkscape + "label", string.IsNullOrEmpty(label) ? null : label));
        tx.Commit();
    }

    public void SetVisible(SvgElement node, bool visible)
    {
        var tx = Begin(visible ? "Show" : "Hide");
        tx.Edit([node], () => node.Style.Set("display", visible ? null : "none"));
        tx.Commit();
    }

    public void SetLocked(SvgElement node, bool locked)
    {
        var tx = Begin(locked ? "Lock" : "Unlock");
        if (locked)
            EnsureNamespace(tx, "sodipodi", SvgElement.Sodipodi);
        tx.Edit([node], () => node.IsLocked = locked);
        tx.Commit();
    }

    /// <summary>Replaces the data of a path (the node tool).</summary>
    public void SetPathData(SvgPath path, VectorPath data)
    {
        var tx = Begin("Edit Path");
        tx.Edit([path], () => path.SetPath(data));
        tx.Commit();
    }

    // ---- Order, groups ----

    public void Raise(IEnumerable<SvgElement>? nodes = null) => Step(nodes, up: true, "Raise");

    public void Lower(IEnumerable<SvgElement>? nodes = null) => Step(nodes, up: false, "Lower");

    private void Step(IEnumerable<SvgElement>? nodes, bool up, string name)
    {
        var targets = TopLevel(nodes);
        var selected = targets.ToHashSet();
        var tx = Begin(name);
        foreach (var node in up ? Enumerable.Reverse(targets) : targets)
        {
            var parent = node.Parent!;
            var siblings = parent.Children;
            var index = parent.IndexOf(node);
            var step = up ? 1 : -1;
            // The next element (comments and other raw content do not count) in that direction.
            var j = index + step;
            while (j >= 0 && j < siblings.Count && siblings[j] is not SvgElement)
                j += step;
            if (j < 0 || j >= siblings.Count || selected.Contains(siblings[j]))
                continue;
            tx.Move(node, parent, j);
        }
        tx.Commit();
    }

    public void RaiseToTop(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes);
        var tx = Begin("Raise to Top");
        foreach (var node in targets)
            if (node.Parent!.Children[^1] != node || targets.Count > 1)
                tx.Move(node, node.Parent!, node.Parent!.Children.Count - 1);
        tx.Commit();
    }

    public void LowerToBottom(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes);
        var tx = Begin("Lower to Bottom");
        foreach (var node in Enumerable.Reverse(targets))
            if (node.Parent!.Children[0] != node || targets.Count > 1)
                tx.Move(node, node.Parent!, 0);
        tx.Commit();
    }

    /// <summary>Wraps the elements in a new group, above the topmost of them; each keeps its place on the page.</summary>
    public SvgGroup? Group(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n is not SvgDefs).ToList();
        if (targets.Count == 0)
            return null;
        var top = targets[^1];
        var group = new SvgGroup { Id = Root.NewId("g") };
        var tx = Begin("Group");
        tx.Insert(top.Parent!, top.Parent!.IndexOf(top) + 1, group);
        var groupWorld = WorldOf(group);
        foreach (var node in targets)
        {
            var oldWorld = WorldOf(node.Parent) * node.Transform;
            tx.Move(node, group, group.Children.Count);
            var local = groupWorld.Invert() is { } inverse ? inverse * oldWorld : node.Transform;
            if (local != node.Transform)
                tx.Edit([node], () => node.Transform = local);
        }
        tx.Commit([group]);
        return group;
    }

    private static readonly string[] BlockedForUngroup = ["clip-path", "mask", "filter"];

    /// <summary>Dissolves groups: children take the group's place with its transform and style (opacity multiplied).</summary>
    public void Ungroup(IEnumerable<SvgElement>? nodes = null)
    {
        var groups = TopLevel(nodes).OfType<SvgGroup>().ToList();
        if (groups.Count == 0)
            return;
        foreach (var group in groups)
            foreach (var property in BlockedForUngroup)
                if (group.Style.Get(property) is { } value && value != "none")
                    throw new NotSupportedException($"A group with a {property} cannot be ungrouped: the effect would be lost.");

        var tx = Begin("Ungroup");
        var released = new List<SvgElement>();
        foreach (var group in groups)
        {
            var parent = group.Parent!;
            var index = parent.IndexOf(group);
            var groupTransform = group.Transform;
            var groupStyle = group.Style.Specified.ToList();
            foreach (var child in group.Children.ToList())
            {
                if (child is SvgElement element)
                {
                    tx.Edit([element], () =>
                    {
                        PushStyle(element, groupStyle);
                        SvgTransformer.Apply(element, groupTransform, Provider);
                    });
                    released.Add(element);
                }
                tx.Move(child, parent, index++);
            }
            tx.Remove(group);
        }
        tx.Commit(released);
    }

    private static void PushStyle(SvgElement child, IEnumerable<StyleDeclaration> groupStyle)
    {
        foreach (var (name, value, _) in groupStyle)
        {
            if (name == "opacity")
            {
                var childOpacity = child.Style.GetNumber("opacity") ?? 1;
                if (NumberFormat.TryParse(value, out var groupOpacity) && groupOpacity < 1)
                    child.Style.SetNumber("opacity", childOpacity * groupOpacity);
            }
            else if (name is "display" or "clip-path" or "mask" or "filter")
            {
                continue;
            }
            else if (child.Style.Get(name) is null)
            {
                child.Style.Set(name, value);
            }
        }
    }

    /// <summary>Moves elements into another group (or layer), keeping their place on the page; <paramref name="index"/> defaults to the end.</summary>
    public void MoveToParent(IEnumerable<SvgElement>? nodes, SvgContainer parent, int? index = null)
    {
        var targets = TopLevel(nodes).Where(n => n != parent && !parent.Ancestors().Contains(n)).ToList();
        if (targets.Count == 0)
            return;
        var tx = Begin("Move to Group");
        var at = index ?? parent.Children.Count;
        var newWorld = WorldOf(parent);
        foreach (var node in targets)
        {
            var oldWorld = WorldOf(node.Parent) * node.Transform;
            // Taking a node out of the same parent before the index shifts the index.
            if (node.Parent == parent && parent.IndexOf(node) < at)
                at--;
            tx.Move(node, parent, at++);
            var local = newWorld.Invert() is { } inverse ? inverse * oldWorld : node.Transform;
            if (local != node.Transform)
                tx.Edit([node], () => node.Transform = local);
        }
        tx.Commit();
    }

    // ---- Object to path ----

    private static readonly Dictionary<string, string[]> GeometryAttributes = new()
    {
        ["rect"] = ["x", "y", "width", "height", "rx", "ry"],
        ["circle"] = ["cx", "cy", "r"],
        ["ellipse"] = ["cx", "cy", "rx", "ry"],
        ["line"] = ["x1", "y1", "x2", "y2"],
        ["polyline"] = ["points"],
        ["polygon"] = ["points"],
    };

    private static readonly string[] TextAttributes = ["x", "y", "dx", "dy", "font-family", "font-size", "font-weight", "font-style", "text-anchor", "xml:space"];

    /// <summary>Replaces shapes with paths of the same outline (text with a group of paths, one per piece of text; needs fonts).</summary>
    public IReadOnlyList<SvgElement> ObjectToPath(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n is SvgShape or SvgText).ToList();
        if (targets.Count == 0)
            return [];
        var tx = Begin("Object to Path");
        var results = new List<SvgElement>();
        foreach (var node in targets)
        {
            var replacement = node switch
            {
                SvgPath => null,
                SvgShape shape => PathFromShape(shape),
                SvgText text => PathsFromText(text),
                _ => null,
            };
            if (replacement is null)
            {
                results.Add(node);
                continue;
            }
            var parent = node.Parent!;
            tx.Insert(parent, parent.IndexOf(node) + 1, replacement);
            tx.Remove(node);
            results.Add(replacement);
        }
        tx.Commit(results);
        return results;
    }

    private static SvgPath PathFromShape(SvgShape shape)
    {
        var path = new SvgPath(XName.Get("path", shape.XmlName.NamespaceName));
        var geometry = GeometryAttributes.GetValueOrDefault(shape.ElementName) ?? [];
        foreach (var attribute in shape.Attributes)
            if (!(attribute.Name.Namespace == XNamespace.None && geometry.Contains(attribute.Name.LocalName)))
                path.SetAttribute(attribute.Name, attribute.Value);
        path.SetPath(shape.CreatePath(), 4);
        return path;
    }

    private SvgElement PathsFromText(SvgText text)
    {
        var provider = Provider ?? throw new InvalidOperationException("Text can only be converted to paths where fonts are available (the desktop app).");
        var group = new SvgGroup(XName.Get("g", text.XmlName.NamespaceName));
        foreach (var attribute in text.Attributes)
        {
            var local = attribute.Name.LocalName;
            if (attribute.Name.Namespace == XNamespace.None && TextAttributes.Contains(local))
                continue;
            group.SetAttribute(attribute.Name, attribute.Value);
        }
        var computed = StyleResolver.ComputeFor(text);
        foreach (var run in TextLayout.Layout(text, computed, provider))
        {
            if (run.Outline.IsEmpty)
                continue;
            var path = new SvgPath();
            path.SetPath(run.Outline, 3);
            var style = run.Style;
            path.SetAttribute("fill", style.Fill.ToText());
            if (style.FillOpacity < 1)
                path.SetAttribute("fill-opacity", NumberFormat.Format(style.FillOpacity, 4));
            if (style.Stroke.Kind != PaintKind.None)
            {
                path.SetAttribute("stroke", style.Stroke.ToText());
                path.SetAttribute("stroke-width", NumberFormat.Format(style.StrokeWidth, 4));
            }
            group.AddChild(path);
        }
        // The text's style properties that the paths do not carry as attributes are on the group already (inherited).
        return group;
    }

    // ---- Selection ----

    /// <summary>
    /// Records a selection change made by a tool gesture (call once, at pointer-up) as an undoable step. <paramref name="before"/>
    /// is the selection when the gesture started.
    /// </summary>
    public void RecordSelectionChange(IEnumerable<SvgElement> before, string name = "Select")
    {
        var from = before.Select(n => n.InternalId).ToList();
        var to = _document.Selection.Nodes.Select(n => n.InternalId).ToList();
        if (from.SequenceEqual(to))
            return;
        _document.Workspace.History.PushNewItem(new VectorSelectionItem(_document, name, from, to));
    }

    // ---- Ids ----

    /// <summary>
    /// Gives every element of a subtree that is not in the document yet ids the document does not use, and updates references
    /// to the changed ids inside the subtree. Ids that are free are kept.
    /// </summary>
    internal void MakeIdsUnique(SvgElement subtree)
    {
        var map = new Dictionary<string, string>();
        var taken = new HashSet<string>();
        foreach (var element in subtree.SelfAndDescendants().OfType<SvgElement>())
        {
            if (element.Id is not { Length: > 0 } id)
                continue;
            if (Root.FindById(id) is null && taken.Add(id))
                continue;
            var fresh = Root.NewId(BaseOfId(id));
            while (taken.Contains(fresh))
                fresh = Root.NewId(BaseOfId(fresh) + "_");
            taken.Add(fresh);
            map.TryAdd(id, fresh);
            element.Id = fresh;
        }
        if (map.Count == 0)
            return;
        foreach (var element in subtree.SelfAndDescendants().OfType<SvgElement>())
            foreach (var attribute in element.Attributes.ToList())
            {
                var rewritten = attribute.Value;
                foreach (var (old, fresh) in map)
                    rewritten = ReplaceReference(rewritten, old, fresh);
                if (rewritten != attribute.Value)
                    element.SetAttribute(attribute.Name, rewritten);
            }
    }

    private static string BaseOfId(string id)
    {
        var end = id.Length;
        while (end > 0 && char.IsAsciiDigit(id[end - 1]))
            end--;
        return end == 0 ? "id" : id[..end];
    }

    private IEnumerable<(SvgElement Element, SvgAttribute Attribute)> ReferencesTo(string id)
    {
        foreach (var element in Root.SelfAndDescendants().OfType<SvgElement>())
            foreach (var attribute in element.Attributes)
                if (ReplaceReference(attribute.Value, id, "\0") != attribute.Value)
                    yield return (element, attribute);
    }

    /// <summary>Replaces <c>url(#old)</c> and a whole-value <c>#old</c> (href) with the new id.</summary>
    internal static string ReplaceReference(string value, string old, string fresh)
    {
        if (value == "#" + old)
            return "#" + fresh;
        return value.Contains("url(#" + old + ")", StringComparison.Ordinal) || value.Contains("url('#" + old + "')", StringComparison.Ordinal)
            || value.Contains("url(\"#" + old + "\")", StringComparison.Ordinal)
            ? value.Replace("url(#" + old + ")", "url(#" + fresh + ")", StringComparison.Ordinal)
                .Replace("url('#" + old + "')", "url('#" + fresh + "')", StringComparison.Ordinal)
                .Replace("url(\"#" + old + "\")", "url(\"#" + fresh + "\")", StringComparison.Ordinal)
            : value;
    }
}
