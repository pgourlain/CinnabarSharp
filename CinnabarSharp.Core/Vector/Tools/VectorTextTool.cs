using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Text tool (T). A click on empty space puts a caret there and typing creates a <c>&lt;text&gt;</c>; a click on a text edits it
/// (a text with several spans is left alone). Left, Right, Home and End move the caret (Shift selects), Backspace and Delete erase,
/// Enter or Escape finish. Font family, size, bold, italic and alignment (<c>text-anchor</c>) come from the tool settings and
/// follow them while editing. The text is shown live and becomes one history step when finished.
/// </summary>
public sealed class VectorTextTool(ToolSettings settings) : IVectorTextTool, IGridSnappingTool
{
    private SvgText? _text;
    private bool _isNew;
    private NodeSnapshot? _before;
    private VPoint _anchor;
    private string _content = "";
    private int _caret;
    private int _selectionStart = -1;
    private int _pointer;

    public string Name => "Text";

    public bool IsEditing(SvgDocument document)
    {
        if (_text is null && !_isNew)
            return false;
        if (_pointer != document.History.Pointer)
        {
            Abandon(document);
            return false;
        }
        return true;
    }

    public bool IsTyping(SvgDocument document) => IsEditing(document);

    // ---- Pointer ----

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        var p = pointer.Position.ToVector();
        if (IsEditing(document) && _text is not null && SvgHitTester.Hits(_text, p, document.ScreenToUser(3)))
        {
            _caret = IndexAt(document, p);
            _selectionStart = -1;
            return;
        }
        Finish(document);
        var hit = SvgHitTester.HitTest(document, p, document.ScreenToUser(3), enterGroups: true);
        if (hit is SvgText existing && !existing.Children.OfType<SvgTextSpan>().Any())
        {
            document.Selection.Set(existing);
            Begin(document, existing, isNew: false, p);
            _caret = IndexAt(document, p);
            return;
        }
        // Empty space: a caret, and the text exists once something is typed.
        _isNew = true;
        _anchor = p;
        _content = "";
        _caret = 0;
        _pointer = document.History.Pointer;
        document.NotifySelectionChanged();
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
    }

    private void Begin(SvgDocument document, SvgText text, bool isNew, VPoint at)
    {
        _text = text;
        _isNew = false;
        _before = isNew ? null : NodeSnapshot.Capture(text);
        _anchor = new VPoint(text.X, text.Y);
        _content = text.Content;
        _caret = _content.Length;
        _selectionStart = -1;
        _pointer = document.History.Pointer;
        _ = at;
    }

    // ---- Text ----

    public void OnTextInput(SvgDocument document, string input)
    {
        if (!IsEditing(document) || string.IsNullOrEmpty(input))
            return;
        var clean = input.Replace("\r", "").Replace("\n", " ");
        ReplaceSelection(clean);
        Apply(document);
    }

    private void ReplaceSelection(string insert)
    {
        var (start, end) = SelectionRange();
        _content = _content[..start] + insert + _content[end..];
        _caret = start + insert.Length;
        _selectionStart = -1;
    }

    private (int Start, int End) SelectionRange() =>
        _selectionStart < 0 ? (_caret, _caret) : (Math.Min(_selectionStart, _caret), Math.Max(_selectionStart, _caret));

    private string SelectedText => _content[SelectionRange().Start..SelectionRange().End];

    // Writes the content (and creates the element at the first character).
    private void Apply(SvgDocument document)
    {
        if (_text is null)
        {
            if (_content.Length == 0)
                return;
            _text = new SvgText { Id = document.Root.NewId("text") };
            _text.X = Math.Round(_anchor.X, 4);
            _text.Y = Math.Round(_anchor.Y, 4);
            ApplyStyle(document, _text);
            SvgDocumentFactory.DefaultParent(document.Root).AddChild(_text);
            _isNew = true;
            document.NotifyTreeChanged();
        }
        var before = SvgBounds.Visual(_text, document.GlyphProvider);
        _text.SetPlainText(_content);
        var after = SvgBounds.Visual(_text, document.GlyphProvider);
        document.NotifyNodeChanged(_text, before is { } b && after is { } a ? b.Union(a) : before ?? after);
    }

    private void ApplyStyle(SvgDocument document, SvgText text)
    {
        var fill = settings.PrimaryColor;
        text.SetAttribute("fill", SvgPaint.FromColor(VColor.FromRgb(fill.R, fill.G, fill.B)).ToText());
        text.SetAttribute("fill-opacity", fill.A < 255 ? NumberFormat.Format(fill.A / 255.0, 3) : null);
        if (!string.IsNullOrEmpty(settings.FontFamily))
            text.SetAttribute("font-family", settings.FontFamily);
        text.SetAttribute("font-size", NumberFormat.Format(Math.Max(settings.FontSize, 1) * document.PixelsToUser(1), 4));
        text.SetAttribute("font-weight", settings.Bold ? "bold" : null);
        text.SetAttribute("font-style", settings.Italic ? "italic" : null);
        text.SetAttribute("text-anchor", settings.TextAlignment switch
        {
            Models.TextAlignment.Center => "middle",
            Models.TextAlignment.Right => "end",
            _ => null,
        });
    }

    public void Refresh(SvgDocument document)
    {
        if (!IsEditing(document) || _text is null)
            return;
        ApplyStyle(document, _text);
        document.NotifyNodeChanged(_text, SvgBounds.Visual(_text, document.GlyphProvider));
    }

    // ---- Finish ----

    public void Finish(SvgDocument document)
    {
        var text = _text;
        var isNew = _isNew;
        var before = _before;
        Reset();
        if (text is null)
            return;
        if (isNew)
        {
            text.Parent?.RemoveChild(text);
            document.NotifyTreeChanged();
            if (text.Content.Length > 0)
                document.Actions.AddNode(text, name: "Text");
            return;
        }
        if (before is null)
            return;
        var after = NodeSnapshot.Capture(text);
        if (after.SameAs(before))
            return;
        before.Restore(text);
        document.Actions.Edit("Edit Text", [text], () => after.Restore(text));
    }

    private void Abandon(SvgDocument document)
    {
        var text = _text;
        var isNew = _isNew;
        var before = _before;
        Reset();
        if (text is null)
            return;
        if (isNew)
        {
            text.Parent?.RemoveChild(text);
            document.NotifyTreeChanged();
        }
    }

    private void Reset()
    {
        _text = null;
        _isNew = false;
        _before = null;
        _content = "";
        _caret = 0;
        _selectionStart = -1;
    }

    // ---- Keys and caret ----

    public bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (!IsEditing(document))
            return false;
        var shift = modifiers.HasFlag(ToolModifiers.Shift);
        void Move(int to)
        {
            if (shift && _selectionStart < 0)
                _selectionStart = _caret;
            else if (!shift)
                _selectionStart = -1;
            _caret = Math.Clamp(to, 0, _content.Length);
        }
        switch (key)
        {
            case ToolKey.Enter or ToolKey.Escape:
                Finish(document);
                return true;
            case ToolKey.Left:
                Move(_selectionStart >= 0 && !shift ? SelectionRange().Start : _caret - 1);
                return true;
            case ToolKey.Right:
                Move(_selectionStart >= 0 && !shift ? SelectionRange().End : _caret + 1);
                return true;
            case ToolKey.Home:
                Move(0);
                return true;
            case ToolKey.End:
                Move(_content.Length);
                return true;
            case ToolKey.Backspace:
                if (_selectionStart < 0 && _caret > 0)
                    _selectionStart = _caret - 1;
                if (_selectionStart >= 0 || _content.Length > 0)
                {
                    ReplaceSelection("");
                    Apply(document);
                }
                return true;
            case ToolKey.Delete:
                if (_selectionStart < 0 && _caret < _content.Length)
                    _selectionStart = _caret + 1;
                if (_selectionStart >= 0)
                {
                    var (start, end) = SelectionRange();
                    _content = _content[..start] + _content[end..];
                    _caret = start;
                    _selectionStart = -1;
                    Apply(document);
                }
                return true;
            default:
                return false;
        }
    }

    public void SelectAll(SvgDocument document)
    {
        _selectionStart = 0;
        _caret = _content.Length;
    }

    public Task Copy(IClipboardService clipboard) => SelectedText.Length > 0 ? clipboard.SetTextAsync(SelectedText) : Task.CompletedTask;

    public async Task Cut(SvgDocument document, IClipboardService clipboard)
    {
        if (SelectedText.Length == 0)
            return;
        await clipboard.SetTextAsync(SelectedText);
        ReplaceSelection("");
        Apply(document);
    }

    public async Task Paste(SvgDocument document, IClipboardService clipboard)
    {
        if (await clipboard.GetTextAsync() is { Length: > 0 } text)
            OnTextInput(document, text);
    }

    // ---- Geometry of the caret ----

    private double FontSize => _text is not null ? StyleResolver.ComputeFor(_text).FontSize : Math.Max(settings.FontSize, 1);

    private CinnabarSharp.Vector.TextStyle FontOf(SvgDocument document)
    {
        var style = _text is not null ? StyleResolver.ComputeFor(_text) : null;
        return new CinnabarSharp.Vector.TextStyle(style?.FontFamily ?? settings.FontFamily, FontSize * (style is null ? document.PixelsToUser(1) : 1),
            style?.FontWeight ?? (settings.Bold ? 700 : 400), style?.Italic ?? settings.Italic);
    }

    private double Advance(SvgDocument document, string text)
    {
        if (text.Length == 0)
            return 0;
        var font = FontOf(document);
        if (document.GlyphProvider is { } provider)
        {
            provider.Outline(text, font, out var advance);
            return advance;
        }
        return text.Length * font.Size * TextLayout.EstimatedEm;
    }

    private double StartX(SvgDocument document)
    {
        var anchor = _text is not null ? StyleResolver.ComputeFor(_text).TextAnchor : settings.TextAlignment switch
        {
            Models.TextAlignment.Center => TextAnchor.Middle,
            Models.TextAlignment.Right => TextAnchor.End,
            _ => TextAnchor.Start,
        };
        var total = Advance(document, _content);
        return anchor switch { TextAnchor.Middle => _anchor.X - total / 2, TextAnchor.End => _anchor.X - total, _ => _anchor.X };
    }

    private int IndexAt(SvgDocument document, VPoint p)
    {
        var x = StartX(document);
        var best = 0;
        var distance = double.MaxValue;
        for (var i = 0; i <= _content.Length; i++)
        {
            var d = Math.Abs(x + Advance(document, _content[..i]) - p.X);
            if (d < distance)
            {
                distance = d;
                best = i;
            }
        }
        return best;
    }

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        if (!IsEditing(document))
            return null;
        var to = document.UserToImage;
        PointD I(double x, double y) => to.Transform(new VPoint(x, y)).ToCore();
        var size = FontOf(document).Size;
        var x0 = StartX(document);
        var top = _anchor.Y - size * 0.8;
        var bottom = _anchor.Y + size * 0.2;
        var caretX = x0 + Advance(document, _content[..Math.Min(_caret, _content.Length)]);
        var highlights = new List<RectangleD>();
        if (_selectionStart >= 0 && _selectionStart != _caret)
        {
            var (start, end) = SelectionRange();
            var a = I(x0 + Advance(document, _content[..start]), top);
            var b = I(x0 + Advance(document, _content[..end]), bottom);
            highlights.Add(new RectangleD(a.X, a.Y, b.X - a.X, b.Y - a.Y));
        }
        var left = I(x0, top);
        var right = I(x0 + Math.Max(Advance(document, _content), size / 2), bottom);
        return new ToolOverlay
        {
            Frame = new RectangleD(left.X, left.Y, right.X - left.X, right.Y - left.Y),
            Lines = [(I(caretX, top), I(caretX, bottom))],
            Highlights = highlights,
        };
    }

    public ToolCursor CursorAt(SvgDocument document, PointD userPoint) =>
        SvgHitTester.HitTest(document, userPoint.ToVector(), document.ScreenToUser(3), enterGroups: true) is SvgText
            ? ToolCursor.Text
            : ToolCursor.Text;
}
