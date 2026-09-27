using Avalonia.Controls;
using Avalonia.Layout;

namespace CinnabarSharp.Desktop.Views;

/// <summary>
/// Modal message with a heading, text and buttons; the dialog result is the clicked button's index,
/// or <c>cancelIndex</c> when closed with Escape or the window's close button.
/// </summary>
public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
    }

    public MessageWindow(string title, string heading, string message, string[] buttons, int defaultIndex, int cancelIndex)
        : this()
    {
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = i == defaultIndex,
                IsCancel = i == cancelIndex,
            };
            button.Click += (_, _) => Close(index);
            ButtonsPanel.Children.Add(button);
        }
        CancelIndex = cancelIndex;
    }

    public int CancelIndex { get; }
}
