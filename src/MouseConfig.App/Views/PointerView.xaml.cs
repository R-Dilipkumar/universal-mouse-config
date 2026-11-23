using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MouseConfig.App.Views;

/// <summary>The Pointer tab. The try-it area is plain view code: it changes no settings.</summary>
public partial class PointerView : UserControl
{
    private const int TargetCount = 5;
    private const double TargetSize = 22;
    private readonly Random _random = new();
    private int _hits;
    private bool _folderOpen;

    public PointerView()
    {
        InitializeComponent();
    }

    private void OnTargetAreaLoaded(object sender, RoutedEventArgs e)
    {
        if (TargetArea.Children.Count == 0)
        {
            PlaceTargets();
        }
    }

    private void PlaceTargets()
    {
        TargetArea.Children.Clear();
        double width = Math.Max(TargetArea.ActualWidth, 200);
        double height = Math.Max(TargetArea.ActualHeight, 150);
        for (int i = 0; i < TargetCount; i++)
        {
            var dot = new Ellipse
            {
                Width = TargetSize,
                Height = TargetSize,
                Cursor = Cursors.Hand,
                Fill = (Brush)FindResource("AccentFillColorDefaultBrush"),
            };
            dot.MouseLeftButtonDown += OnTargetHit;
            Canvas.SetLeft(dot, 8 + _random.NextDouble() * (width - TargetSize - 16));
            Canvas.SetTop(dot, 30 + _random.NextDouble() * (height - TargetSize - 38));
            TargetArea.Children.Add(dot);
        }
        TargetScore.Text = $"{TargetCount} dots left";
    }

    private void OnTargetHit(object sender, MouseButtonEventArgs e)
    {
        TargetArea.Children.Remove((UIElement)sender);
        _hits++;
        int left = TargetArea.Children.Count;
        if (left == 0)
        {
            PlaceTargets();
            TargetScore.Text = $"All hit! ({_hits} so far) New dots placed.";
        }
        else
        {
            TargetScore.Text = $"{left} dots left";
        }
        e.Handled = true;
    }

    // WPF counts clicks using Windows' double-click time, so this follows the slider right away.
    private void OnFolderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }
        _folderOpen = !_folderOpen;
        FolderIcon.Text = _folderOpen ? "" : "";
        FolderText.Text = _folderOpen ? "Opened! Double-click again to close." : "Closed. Double-click to open.";
    }
}
