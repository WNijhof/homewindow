using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class SettingsPage : UserControl
{
    static AppSettings S => App.Settings;
    HomeyConfig? editing;
    bool loading;

    public SettingsPage()
    {
        InitializeComponent();
        Load();
        VersionText.Text = Loc.F("HomeWindow versie {0}", Updater.CurrentText);
        CheckButton.IsEnabled = Updater.IsInstalled;
    }

    void Update_Click(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.AutoUpdate = UpdateSwitch.IsChecked == true;
        S.Save();
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        await Updater.I.CheckAsync(manual: true);
        CheckButton.IsEnabled = true;
    }

    // Shows the log in Explorer, selected, so it can be attached to an issue
    void Log_Click(object sender, RoutedEventArgs e)
    {
        var folder = System.IO.Path.GetDirectoryName(Log.FilePath)!;
        System.IO.Directory.CreateDirectory(folder);
        if (System.IO.File.Exists(Log.FilePath)) Process.Start("explorer.exe", $"/select,\"{Log.FilePath}\"");
        else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    void Load()
    {
        loading = true;
        RefreshHomeys();
        Check(ThemeChoice, S.Theme);
        Check(BackdropChoice, S.Backdrop);
        Check(FlyoutViewChoice, S.FlyoutView);
        Check(MainViewChoice, S.MainView);
        Check(TileSizeChoice, S.TileSize);
        Check(LanguageChoice, S.Language ?? "");
        IntensitySlider.Value = S.Intensity;
        ScaleSlider.Value = S.TextScale;
        ScaleText.Text = $"{S.TextScale * 100:0} %";
        ShadowSwitch.IsChecked = S.Shadows;
        WeatherSwitch.IsChecked = S.FlyoutWeather;
        EnergySwitch.IsChecked = S.FlyoutEnergy;
        TaskbarSwitch.IsChecked = S.TaskbarEnergy;
        StartupSwitch.IsChecked = AppSettings.StartWithWindows;
        HotkeySwitch.IsChecked = S.Hotkey;
        ToastSwitch.IsChecked = S.Toasts;
        AlarmSwitch.IsChecked = S.AlarmToasts;
        ActivitySwitch.IsChecked = S.ActivityToasts;
        UpdateSwitch.IsChecked = S.AutoUpdate;
        loading = false;
        if (S.Homeys.Count == 0) OpenEditor(null);
    }

    static void Check(Panel panel, string value)
    {
        foreach (var child in panel.Children)
            if (child is RadioButton rb) rb.IsChecked = (rb.Tag as string ?? "") == value;
    }

    void RefreshHomeys()
    {
        HomeyCards.ItemsSource = null;
        HomeyCards.ItemsSource = S.Homeys;
    }

    // ---- Homeys ----

    void Add_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HomeyConfig h }) OpenEditor(h);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HomeyConfig h }) return;
        if (MessageBox.Show(Window.GetWindow(this)!, Loc.F("{0} verwijderen uit HomeWindow?", h.Name), "HomeWindow", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        S.Homeys.Remove(h);
        if (S.ActiveHomeyId == h.Id) S.ActiveHomeyId = S.Homeys.FirstOrDefault()?.Id;
        S.Save();
        RefreshHomeys();
        if (HomeyStore.I.Config?.Id == h.Id) HomeyStore.I.Start(S.ActiveHomey);
    }

    void OpenEditor(HomeyConfig? h)
    {
        editing = h;
        EditorTitle.Text = Loc.T(h == null ? "Homey toevoegen" : "Homey wijzigen");
        NameBox.Text = h?.Name ?? "Homey";
        AddressBox.Text = h?.LocalAddress ?? "";
        TokenBox.Password = h?.Token ?? "";
        CloudBox.Text = h?.CloudId ?? "";
        (h?.Mode switch { "local" => ModeLocal, "cloud" => ModeCloud, _ => ModeAuto }).IsChecked = true;
        TestResult.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        AddButton.Visibility = Visibility.Collapsed;
        Editor.BringIntoView();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => CloseEditor();

    void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        AddButton.Visibility = Visibility.Visible;
        TokenBox.Password = "";
        editing = null;
    }

    HomeyConfig FromForm(HomeyConfig target)
    {
        target.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "Homey";
        target.LocalAddress = AddressBox.Text.Trim();
        target.CloudId = CloudBox.Text.Trim();
        target.Mode = ModeLocal.IsChecked == true ? "local" : ModeCloud.IsChecked == true ? "cloud" : "auto";
        target.Token = TokenBox.Password;
        return target;
    }

    string? Validate()
    {
        if (TokenBox.Password.Trim().Length == 0) return Loc.T("Vul de API-key in.");
        if (AddressBox.Text.Trim().Length == 0 && CloudBox.Text.Trim().Length == 0) return Loc.T("Vul het IP-adres of het Homey-ID in.");
        if (ModeLocal.IsChecked == true && AddressBox.Text.Trim().Length == 0) return Loc.T("Vul het IP-adres in.");
        if (ModeCloud.IsChecked == true && CloudBox.Text.Trim().Length == 0) return Loc.T("Vul het Homey-ID in.");
        return null;
    }

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Validate() is { } problem) { ShowResult(problem, false); return; }
        var probe = FromForm(new HomeyConfig());
        TestButton.IsEnabled = false;
        ShowResult(Loc.T("Bezig met testen…"), null);
        try
        {
            var client = new HomeyClient(probe);
            await client.ConnectAsync(CancellationToken.None);
            var devices = J.Items(await client.GetAsync("/api/manager/devices/device")).Count();
            string? name = null;
            try { name = (await client.GetAsync("/api/manager/system/name"))?.GetValue<string>(); } catch { }
            if (probe.CloudId.Length > 0 && CloudBox.Text.Trim().Length == 0) CloudBox.Text = probe.CloudId;
            if (name != null && (NameBox.Text.Trim() is "" or "Homey")) NameBox.Text = name;
            ShowResult(Loc.F(client.IsCloud ? "Verbonden via de cloud: {0} apparaten gevonden." : "Lokaal verbonden: {0} apparaten gevonden.", devices), true);
        }
        catch (HomeyApiException ex) when (ex.Status is 401 or 403)
        {
            ShowResult(Loc.T(ex.Status == 401 ? "De API-key wordt niet geaccepteerd." : "De API-key mag de apparaten niet bekijken."), false);
        }
        catch (Exception ex)
        {
            ShowResult(Loc.F("Geen verbinding: {0}", ex.Message), false);
        }
        finally { TestButton.IsEnabled = true; }
    }

    void ShowResult(string text, bool? ok)
    {
        TestResult.Text = text;
        TestResult.Foreground = (System.Windows.Media.Brush)FindResource(ok switch { true => "GoodBrush", false => "AlarmBrush", _ => "SubTextBrush" });
        TestResult.Visibility = Visibility.Visible;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Validate() is { } problem) { ShowResult(problem, false); return; }
        var target = editing ?? new HomeyConfig();
        FromForm(target);
        if (editing == null) S.Homeys.Add(target);
        var restart = editing == null || HomeyStore.I.Config?.Id == target.Id || S.Homeys.Count == 1;
        if (S.ActiveHomeyId == null || editing == null) S.ActiveHomeyId = target.Id;
        S.Save();
        CloseEditor();
        RefreshHomeys();
        if (restart) HomeyStore.I.Start(S.ActiveHomey);
    }

    void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ---- Appearance ----

    void Theme_Checked(object sender, RoutedEventArgs e) => Choose(sender, v => S.Theme = v);
    void Backdrop_Checked(object sender, RoutedEventArgs e) => Choose(sender, v => S.Backdrop = v);
    void FlyoutView_Checked(object sender, RoutedEventArgs e) => Choose(sender, v => S.FlyoutView = v);
    void MainView_Checked(object sender, RoutedEventArgs e) => Choose(sender, v => S.MainView = v);
    void TileSize_Checked(object sender, RoutedEventArgs e) => Choose(sender, v => S.TileSize = v);

    void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not RadioButton { Tag: string v }) return;
        S.Language = v.Length == 0 ? null : v;
        S.Save();
        RestartHint.Visibility = Visibility.Visible;
    }

    void Choose(object sender, Action<string> set)
    {
        if (loading || sender is not RadioButton { Tag: string v }) return;
        set(v);
        Apply();
    }

    void Appearance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading) return;
        S.Intensity = IntensitySlider.Value;
        Apply();
    }

    void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.Shadows = ShadowSwitch.IsChecked == true;
        Apply();
    }

    // The text size applies when the slider is let go, so the page does not jump while dragging
    void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleText != null) ScaleText.Text = $"{ScaleSlider.Value * 100:0} %";
    }

    void Scale_Commit(object sender, RoutedEventArgs e)
    {
        if (loading || Math.Abs(S.TextScale - ScaleSlider.Value) < 0.001) return;
        S.TextScale = Math.Round(ScaleSlider.Value, 2);
        Apply();
    }

    static void Apply()
    {
        S.Save();
        Theme.Apply();
    }

    void General_Click(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.FlyoutWeather = WeatherSwitch.IsChecked == true;
        S.FlyoutEnergy = EnergySwitch.IsChecked == true;
        S.TaskbarEnergy = TaskbarSwitch.IsChecked == true;
        S.Toasts = ToastSwitch.IsChecked == true;
        S.AlarmToasts = AlarmSwitch.IsChecked == true;
        S.ActivityToasts = ActivitySwitch.IsChecked == true;
        var hotkeyChanged = S.Hotkey != (HotkeySwitch.IsChecked == true);
        S.Hotkey = HotkeySwitch.IsChecked == true;
        S.Save();
        if (hotkeyChanged) App.Current.RegisterHotkey();
    }

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        try { AppSettings.StartWithWindows = StartupSwitch.IsChecked == true; }
        catch (Exception ex) { HomeyStore.I.ShowError(ex.Message); }
        StartupSwitch.IsChecked = AppSettings.StartWithWindows;
    }
}
