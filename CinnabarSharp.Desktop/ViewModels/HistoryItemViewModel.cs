namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A step in the History panel; undone steps are shown dimmed, as in Paint.NET.</summary>
public record HistoryItemViewModel(int Index, string Text, bool IsUndone)
{
    public double Opacity => IsUndone ? 0.45 : 1;
}
