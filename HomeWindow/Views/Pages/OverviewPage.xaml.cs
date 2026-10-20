using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
        var store = HomeyStore.I;
        store.Notifications.CollectionChanged += (_, _) => Recent.ItemsSource = store.Notifications.Take(5).ToList();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        clock.Tick += (_, _) => UpdateGreeting();
        clock.Start();
        UpdateGreeting();
    }

    // A camera picture large, in its own window; it follows the picture while it is refreshed
    void Camera_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CameraVM camera) return;
        var image = new Image { Stretch = Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new Binding(nameof(CameraVM.Image)) { Source = camera });
        var area = SystemParameters.WorkArea;
        var window = new Window
        {
            Title = camera.Name,
            Content = image,
            Owner = Window.GetWindow(this),
            Width = area.Width * 0.7,
            Height = area.Height * 0.7,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        window.Background = Brushes.Black;
        window.SourceInitialized += (_, _) => Theme.ApplyToWindow(window, transient: true);
        window.KeyDown += (_, k) => { if (k.Key == Key.Escape) window.Close(); };
        window.MouseLeftButtonUp += (_, _) => window.Close();
        window.Show();
    }

    void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        Greeting.Text = Loc.T(hour < 6 ? "Goedenacht" : hour < 12 ? "Goedemorgen" : hour < 18 ? "Goedemiddag" : "Goedenavond");
        DateText.Text = DateTime.Now.ToString("dddd d MMMM", Loc.Culture);
    }
}
