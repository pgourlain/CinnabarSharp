using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

// Scissors: take out the run of an outline between two crossings with other shapes.
public sealed partial class SvgActions
{
    /// <summary>
    /// Removes the run of <paramref name="hit"/> (see <see cref="SvgScissors.Find"/>) from its shape. A closed outline becomes one
    /// open path; an open one becomes the part before and the part after the run (a second path when both exist). Shapes that are
    /// not paths become paths. Returns the objects that are left of the shape, selected; one undo step.
    /// </summary>
    public IReadOnlyList<SvgElement> RemoveSegment(ScissorsHit hit)
    {
        var shape = hit.Shape;
        if (shape.Parent is not { } parent || shape.DocumentRoot != Root)
            throw new InvalidOperationException("The shape is not in the drawing.");
        var rest = hit.Figures[hit.Figure].Without(hit.Piece);

        // The sub-paths the cut did not touch stay with the first part, in their place.
        var main = new VectorPath();
        for (var i = 0; i < hit.Figures.Count; i++)
        {
            if (i == hit.Figure)
            {
                if (rest.Count > 0)
                    main.Append(rest[0]);
            }
            else
            {
                main.Append(hit.Figures[i].Original);
            }
        }

        var tx = Begin("Cut Segment");
        var created = new List<SvgElement>();
        if (main.IsEmpty)
        {
            tx.Remove(shape);
        }
        else if (shape is SvgPath existing)
        {
            tx.Edit([existing], () => existing.SetPath(main, 4));
            created.Add(existing);
        }
        else
        {
            var path = PathFromShape(shape);
            path.SetPath(main, 4);
            tx.Insert(parent, parent.IndexOf(shape) + 1, path);
            tx.Remove(shape);
            created.Add(path);
        }
        if (rest.Count > 1)
        {
            var second = PathFromShape(shape);
            second.Id = Root.NewId("path");
            second.SetPath(rest[1], 4);
            var anchor = created.Count > 0 ? created[0] : shape;
            tx.Insert(parent, parent.IndexOf(anchor) + 1, second);
            created.Add(second);
        }
        tx.Commit(created);
        return created;
    }
}
