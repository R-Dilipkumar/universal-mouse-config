using System.Windows;
using System.Windows.Controls;

namespace MouseConfig.App.Views;

public partial class ScrollView : UserControl
{
    private const int TryLineCount = 300;

    public ScrollView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TryLines.Children.Count > 0)
        {
            return;
        }
        for (int i = 1; i <= TryLineCount; i++)
        {
            TryLines.Children.Add(new TextBlock
            {
                Text = $"Line {i} — a long line so sideways scrolling has somewhere to go → → → → → → → → → end",
            });
        }
    }
}
