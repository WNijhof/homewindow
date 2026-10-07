using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace HomeWindow.Core;

// Everything known about the active Homey. Polls the Web API (fast while a window is open) and
// keeps the view models in place, so bindings and scroll positions survive each update.
public sealed class HomeyStore : ObservableObject
{
    public static HomeyStore I { get; } = new();

    HomeyClient? client;
    CancellationTokenSource? loop;
    TaskCompletionSource kick = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly DispatcherTimer errorTimer;

    readonly Dictionary<string, DeviceVM> devices = [];
    readonly Dictionary<string, ZoneVM> zones = [];
    readonly Dictionary<string, FlowVM> flows = [];
    readonly Dictionary<string, FolderVM> folders = [];
    readonly Dictionary<string, MoodVM> moods = [];
    readonly Dictionary<string, VariableVM> variables = [];
    readonly Dictionary<string, UserVM> users = [];
    readonly Dictionary<string, AppVM> apps = [];
    readonly Dictionary<string, NotificationVM> notifications = [];
    DateTime? lastNotification;
    JsonNode? insightLogs;
    DateTime insightLogsAt;

    HomeyStore()
    {
        errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        errorTimer.Tick += (_, _) => { errorTimer.Stop(); Error = null; };
        Theme.Changed += () => { foreach (var d in Devices) d.Refresh(); };
    }

    public ObservableCollection<DeviceVM> Devices { get; } = [];
    public ObservableCollection<ZoneVM> Zones { get; } = [];
    public ObservableCollection<FlowVM> Flows { get; } = [];
    public ObservableCollection<FolderVM> Folders { get; } = [];
    public ObservableCollection<MoodVM> Moods { get; } = [];
    public ObservableCollection<VariableVM> Variables { get; } = [];
    public ObservableCollection<UserVM> Users { get; } = [];
    public ObservableCollection<AppVM> Apps { get; } = [];
    public ObservableCollection<NotificationVM> Notifications { get; } = [];
    public ObservableCollection<DeviceVM> FavoriteDevices { get; } = [];
    public ObservableCollection<FlowVM> FavoriteFlows { get; } = [];
    public ObservableCollection<FlowGroup> FlowGroups { get; } = [];
    public ObservableCollection<DeviceVM> BatteryDevices { get; } = [];
    public WeatherVM Weather { get; } = new();
    public EnergyVM Energy { get; } = new();
    public SystemVM System { get; } = new();

    // Fired when devices are added, removed, renamed or moved, so lists regroup
    public event Action? DevicesChanged;
    public event Action<NotificationVM>? NewNotification;
    // An alarm of a device that went on since the last poll; not for devices seen for the first time
    public event Action<DeviceVM, CapabilityVM>? AlarmStarted;

    public HomeyConfig? Config { get; private set; }

    string status = "offline";
    // offline, connecting, local, cloud, error
    public string Status { get => status; private set { if (Set(ref status, value)) OnPropertyChanged(nameof(IsConnected)); } }
    string statusText = "";
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public bool IsConnected => Status is "local" or "cloud";

    string homeyName = "Homey";
    public string HomeyName { get => homeyName; private set => Set(ref homeyName, value); }

    string? error;
    public string? Error { get => error; private set => Set(ref error, value); }

    // Messages about parts the API key may not see, per feature
    readonly Dictionary<string, string> featureErrors = [];
    public string? FeatureError(string feature) => featureErrors.GetValueOrDefault(feature);
    public string? FlowsError => FeatureError("flows");
    public string? MoodsError => FeatureError("moods");
    public string? VariablesError => FeatureError("variables");
    public string? NotificationsError => FeatureError("notifications");
    public string? EnergyError => FeatureError("energy");
    public string? SystemError => FeatureError("system");
    public string? AppsError => FeatureError("apps");

    int visibleWindows;
    public bool EnergyPageVisible { get; set; }
    public bool SystemPageVisible { get; set; }
    public bool EnergyReportRequested { get; set; }

    public void WindowShown() { visibleWindows++; Kick(); }
    public void WindowHidden() => visibleWindows = Math.Max(0, visibleWindows - 1);

    public void Kick() => kick.TrySetResult();

    public void ShowError(string message)
    {
        Error = message;
        errorTimer.Stop();
        errorTimer.Start();
    }

    public void Start(HomeyConfig? config)
    {
        loop?.Cancel();
        Clear();
        Config = config;
        if (config == null)
        {
            Status = "offline";
            StatusText = Loc.T("Nog geen Homey ingesteld");
            HomeyName = "HomeWindow";
            return;
        }
        HomeyName = config.Name;
        client = new HomeyClient(config);
        client.CredentialsChanged += () => App.Current.Dispatcher.BeginInvoke(() => App.Settings.Save());
        loop = new CancellationTokenSource();
        _ = RunAsync(client, loop.Token);
    }

    public void Stop() => loop?.Cancel();

    void Clear()
    {
        foreach (var d in new System.Collections.IDictionary[] { devices, zones, flows, folders, moods, variables, users, apps, notifications }) d.Clear();
        Devices.Clear(); Zones.Clear(); Flows.Clear(); Folders.Clear(); Moods.Clear(); Variables.Clear();
        Users.Clear(); Apps.Clear(); Notifications.Clear(); FavoriteDevices.Clear(); FavoriteFlows.Clear();
        FlowGroups.Clear(); BatteryDevices.Clear(); Energy.Consumers.Clear(); Energy.Days.Clear(); Energy.TodayDevices.Clear();
        Energy.ReportLoaded = false;
        Theme.SetMood(null);
        featureErrors.Clear();
        lastNotification = null;
        insightLogs = null;
        DevicesChanged?.Invoke();
    }

    async Task RunAsync(HomeyClient c, CancellationToken ct)
    {
        DateTime slow = DateTime.MinValue, mid = DateTime.MinValue, weather = DateTime.MinValue, system = DateTime.MinValue,
            report = DateTime.MinValue, localProbe = DateTime.MinValue;
        var backoff = 2;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (c.BaseUrl == null)
                {
                    Status = "connecting";
                    StatusText = Loc.T("Verbinden…");
                    await c.ConnectAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    App.Settings.Save();
                    SetConnected(c);
                    backoff = 2;
                    slow = mid = weather = system = DateTime.MinValue;
                    localProbe = DateTime.UtcNow;
                    // A Homey account may not read the name; then the one from the settings stays
                    if (!c.Config.UsesAccount)
                        await Feature("name", async () => HomeyName = (await c.GetAsync("/api/manager/system/name", ct))?.GetValue<string>() ?? HomeyName);
                }

                var now = DateTime.UtcNow;
                if (now - slow > TimeSpan.FromMinutes(2))
                {
                    await LoadZones(c, ct);
                    await Feature("flows", () => LoadFlows(c, ct));
                    await Feature("moods", () => LoadMoods(c, ct));
                    slow = now;
                }

                await LoadDevices(c, ct);
                await Feature("favorites", () => TakeOverFavoritesOnce(c, ct));

                if (now - mid > TimeSpan.FromSeconds(30))
                {
                    await Feature("notifications", () => LoadNotifications(c, ct));
                    await Feature("variables", () => LoadVariables(c, ct));
                    await Feature("users", () => LoadUsers(c, ct));
                    await Feature("energy", () => LoadEnergyLive(c, ct));
                    mid = now;
                }
                if (now - weather > TimeSpan.FromMinutes(10))
                {
                    await Feature("weather", () => LoadWeather(c, ct));
                    weather = now;
                }
                if (SystemPageVisible && now - system > TimeSpan.FromSeconds(10))
                {
                    await Feature("system", () => LoadSystem(c, ct));
                    await Feature("apps", () => LoadApps(c, ct));
                    system = now;
                }
                if ((EnergyPageVisible && now - report > TimeSpan.FromMinutes(5)) || EnergyReportRequested)
                {
                    EnergyReportRequested = false;
                    await Feature("energy", () => LoadEnergyReport(c, ct));
                    report = now;
                }

                // On the cloud, check now and then whether the local address works again
                if (c.IsCloud && c.Config.Mode == "auto" && now - localProbe > TimeSpan.FromMinutes(2))
                {
                    localProbe = now;
                    if (await c.TryLocalAsync(ct)) SetConnected(c);
                }

                await Wait(visibleWindows > 0 ? 2500 : 15000, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (HomeyOfflineException e)
            {
                c.Disconnect();
                Status = "offline";
                StatusText = e.Message;
                await Wait(backoff * 1000, ct, kickable: true);
                backoff = Math.Min(backoff * 2, 30);
            }
            catch (HomeyApiException e) when (e.Status == 401)
            {
                c.Disconnect();
                Status = "error";
                StatusText = c.Config.UsesAccount ? e.Message : Loc.T("De API-key is ongeldig of ingetrokken");
                await Wait(30000, ct, kickable: true);
            }
            catch (HomeyApiException e) when (e.Status == 403)
            {
                c.Disconnect();
                Status = "error";
                StatusText = Loc.T(c.Config.UsesAccount ? "Dit Homey-account mag de apparaten niet bekijken" : "De API-key mag de apparaten niet bekijken");
                await Wait(30000, ct, kickable: true);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                ShowError(e.Message);
                await Wait(5000, ct);
            }
        }
    }

    void SetConnected(HomeyClient c)
    {
        Status = c.IsCloud ? "cloud" : "local";
        StatusText = Loc.T(c.Config.Mode == "demo" ? "Demomodus" : c.IsCloud ? "Verbonden via de cloud" : "Lokaal verbonden");
    }

    async Task Wait(int ms, CancellationToken ct, bool kickable = true)
    {
        if (kick.Task.IsCompleted)
        {
            // Someone asked for fresh data while this poll ran
            kick = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (kickable) return;
        }
        var delay = Task.Delay(ms, ct);
        await Task.WhenAny(delay, kickable ? kick.Task : delay);
        ct.ThrowIfCancellationRequested();
    }

    // Runs a part that may be missing (an older Homey) or not allowed (the API key's rights)
    async Task Feature(string name, Func<Task> load)
    {
        try
        {
            await load();
            if (featureErrors.Remove(name)) OnPropertyChanged(string.Empty);
        }
        catch (HomeyApiException e) when (e.Status is 401 or 403 or 404)
        {
            featureErrors[name] = e.Status == 404
                ? Loc.T("Deze Homey ondersteunt dit niet")
                : NoRights();
            OnPropertyChanged(string.Empty);
        }
    }

    // Signed in with a Homey account, HomeWindow gets the rights Athom allows a third-party client, which do not
    // cover everything (the system, updates, deleting notifications); more rights cannot be given there
    string NoRights() => Loc.T(Config?.UsesAccount == true
        ? "Niet beschikbaar bij inloggen met een Homey-account. Gebruik een API-key als je Homey die kan maken."
        : "Geen toegang. Geef de API-key meer rechten in my.homey.app.");

    // ---- Loading ----

    async Task LoadZones(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/zones/zone", ct);
        ct.ThrowIfCancellationRequested();
        var all = J.Items(node).Select(z => (id: J.Str(z, "id"), name: J.Str(z, "name"), parent: J.Str(z, "parent"))).Where(z => z.id != null).ToList();
        var ordered = new List<(string id, string name, string? parent, int depth)>();
        void Walk(string? parent, int depth)
        {
            foreach (var z in all.Where(z => z.parent == parent).OrderBy(z => z.name, StringComparer.CurrentCultureIgnoreCase))
            {
                ordered.Add((z.id!, z.name ?? "", z.parent, depth));
                Walk(z.id, depth + 1);
            }
        }
        Walk(null, 0);
        // Zones whose parent is missing still show up
        foreach (var z in all.Where(z => !ordered.Any(o => o.id == z.id))) ordered.Add((z.id!, z.name ?? "", z.parent, 0));

        // Unchanged zones (the usual case every two minutes) leave the lists alone
        if (Zones.Select(z => (z.Id, z.Name, z.ParentId, z.Depth)).SequenceEqual(ordered.Select(z => (z.id, z.name, z.parent, z.depth)))) return;

        Zones.Clear();
        zones.Clear();
        var order = 0;
        foreach (var z in ordered)
        {
            var vm = new ZoneVM(z.id) { Name = z.name, ParentId = z.parent, Depth = z.depth, Order = order++ };
            zones[z.id] = vm;
            Zones.Add(vm);
        }
        foreach (var d in Devices) d.ZoneName = ZoneName(d.ZoneId);
        foreach (var m in Moods) m.ZoneName = ZoneName(m.ZoneId);
        DevicesChanged?.Invoke();
    }

    public string ZoneName(string? id) => id != null && zones.TryGetValue(id, out var z) ? z.Name : "";
    public ZoneVM? Zone(string? id) => id != null ? zones.GetValueOrDefault(id) : null;

    async Task LoadDevices(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/devices/device", ct);
        ct.ThrowIfCancellationRequested();
        var seen = new HashSet<string>();
        var membership = false;
        var cfg = Config!;
        var started = new List<(DeviceVM device, string capability)>();
        foreach (var d in J.Items(node))
        {
            if (J.Str(d, "id") is not { } id) continue;
            seen.Add(id);
            IReadOnlySet<string>? alarmsBefore = null;
            if (devices.TryGetValue(id, out var vm)) alarmsBefore = vm.ActiveAlarms;
            else
            {
                vm = new DeviceVM(id)
                {
                    IsFavorite = cfg.FavoriteDevices.Contains(id),
                    CustomGlyph = cfg.CustomIcons.GetValueOrDefault(id),
                    TileDetails = cfg.TileDetails.GetValueOrDefault(id),
                };
                devices[id] = vm;
                Devices.Add(vm);
                membership = true;
            }
            var (zone, name) = (vm.ZoneId, vm.Name);
            vm.Update(d);
            vm.ZoneName = ZoneName(vm.ZoneId);
            membership |= zone != vm.ZoneId || name != vm.Name;
            if (alarmsBefore != null) started.AddRange(Alarms.Started(alarmsBefore, vm.ActiveAlarms).Select(a => (vm, a)));
        }
        foreach (var gone in devices.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Devices.Remove(devices[gone]);
            devices.Remove(gone);
            membership = true;
        }

        if (membership)
        {
            RebuildFavorites();
            DevicesChanged?.Invoke();
        }
        RebuildBatteries();
        ComputeLiveEnergy(null);
        foreach (var (device, capability) in started)
            if (device.Cap(capability) is { } cap) AlarmStarted?.Invoke(device, cap);
    }

    // A new HomeWindow starts with the favourites of the Homey app, so the panel is not empty. Only once, and only
    // while there are none of its own; after that the user decides.
    async Task TakeOverFavoritesOnce(HomeyClient c, CancellationToken ct)
    {
        var cfg = Config;
        if (cfg == null || cfg.FavoritesChecked || devices.Count == 0) return;
        cfg.FavoritesChecked = true;
        App.Settings.Save();
        if (cfg.FavoriteDevices.Count > 0 || cfg.FavoriteFlows.Count > 0) return;
        await TakeOverFavorites(c, ct);
    }

    async Task<(int devices, int flows)> TakeOverFavorites(HomeyClient c, CancellationToken ct)
    {
        var cfg = Config!;
        var (favDevices, favFlows) = Favorites.FromHomey(await c.GetAsync("/api/manager/users/user/me", ct));
        var addedDevices = Favorites.Merge(cfg.FavoriteDevices, favDevices, devices.ContainsKey);
        var addedFlows = Favorites.Merge(cfg.FavoriteFlows, favFlows, flows.ContainsKey);
        foreach (var id in addedDevices) devices[id].IsFavorite = true;
        foreach (var id in addedFlows) flows[id].IsFavorite = true;
        if (addedDevices.Count + addedFlows.Count > 0)
        {
            App.Settings.Save();
            RebuildFavorites();
        }
        return (addedDevices.Count, addedFlows.Count);
    }

    // The button: adds what is in the Homey app and not yet here; nothing is removed. Null when it failed.
    public async Task<(int devices, int flows)?> ImportHomeyFavoritesAsync()
    {
        (int, int)? result = null;
        await Act(async c => result = await TakeOverFavorites(c, CancellationToken.None));
        return result;
    }

    void RebuildFavorites()
    {
        var cfg = Config;
        if (cfg == null) return;
        Sync(FavoriteDevices, cfg.FavoriteDevices.Select(id => devices.GetValueOrDefault(id)).OfType<DeviceVM>().ToList());
        Sync(FavoriteFlows, cfg.FavoriteFlows.Select(id => flows.GetValueOrDefault(id)).OfType<FlowVM>().ToList());
    }

    public int LowBatteryCount => BatteryDevices.Count(d => d.BatteryLow);
    public string LowBatteryText => Loc.F(LowBatteryCount == 1 ? "{0} batterij bijna leeg" : "{0} batterijen bijna leeg", LowBatteryCount);

    int shownLowBatteries;

    void RebuildBatteries()
    {
        Sync(BatteryDevices, Devices.Where(d => d.HasBattery).OrderBy(d => d.BatteryLevel ?? (d.BatteryAlarm ? 5 : 101)).ThenBy(d => d.Name).ToList());
        if (shownLowBatteries != LowBatteryCount)
        {
            shownLowBatteries = LowBatteryCount;
            OnPropertyChanged(nameof(LowBatteryCount));
            OnPropertyChanged(nameof(LowBatteryText));
        }
    }

    // Replaces the contents only when they differ, so lists do not flicker on every poll
    static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items)) return;
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    async Task LoadFlows(HomeyClient c, CancellationToken ct)
    {
        var folderNode = await c.GetAsync("/api/manager/flow/flowfolder", ct);
        var flowNode = await c.GetAsync("/api/manager/flow/flow", ct);
        JsonNode? advancedNode = null;
        try { advancedNode = await c.GetAsync("/api/manager/flow/advancedflow", ct); }
        catch (HomeyApiException e) when (e.Status is 403 or 404) { }
        ct.ThrowIfCancellationRequested();

        var all = J.Items(folderNode).Select(f => (id: J.Str(f, "id"), name: J.Str(f, "name"), parent: J.Str(f, "parent"))).Where(f => f.id != null).ToList();
        Folders.Clear();
        folders.Clear();
        void Walk(string? parent, int depth)
        {
            foreach (var f in all.Where(f => f.parent == parent).OrderBy(f => f.name, StringComparer.CurrentCultureIgnoreCase))
            {
                var vm = new FolderVM(f.id!) { Name = f.name ?? "", ParentId = f.parent, Depth = depth };
                folders[vm.Id] = vm;
                Folders.Add(vm);
                Walk(f.id, depth + 1);
            }
        }
        Walk(null, 0);
        foreach (var f in all.Where(f => !folders.ContainsKey(f.id!)))
        {
            var vm = new FolderVM(f.id!) { Name = f.name ?? "" };
            folders[vm.Id] = vm;
            Folders.Add(vm);
        }

        var seen = new HashSet<string>();
        var changed = false;
        void Take(JsonNode? node, bool advanced)
        {
            foreach (var f in J.Items(node))
            {
                if (J.Str(f, "id") is not { } id) continue;
                seen.Add(id);
                if (!flows.TryGetValue(id, out var vm))
                {
                    vm = new FlowVM(id, advanced) { IsFavorite = Config!.FavoriteFlows.Contains(id) };
                    flows[id] = vm;
                    Flows.Add(vm);
                    changed = true;
                }
                changed |= vm.Update(f);
                vm.FolderName = vm.FolderId != null && folders.TryGetValue(vm.FolderId, out var folder) ? folder.Name : "";
            }
        }
        Take(flowNode, false);
        Take(advancedNode, true);
        foreach (var gone in flows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Flows.Remove(flows[gone]);
            flows.Remove(gone);
            changed = true;
        }
        if (changed || FlowGroups.Count == 0)
        {
            var sorted = Flows.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Sync(Flows, sorted);
            RebuildFlowGroups();
            RebuildFavorites();
        }
    }

    void RebuildFlowGroups()
    {
        var collapsed = Config?.CollapsedFolders ?? [];
        void Persist(FlowGroup g)
        {
            if (Config == null) return;
            if (g.IsExpanded) Config.CollapsedFolders.Remove(g.Key);
            else if (!Config.CollapsedFolders.Contains(g.Key)) Config.CollapsedFolders.Add(g.Key);
            App.Settings.Save();
        }
        var groups = new List<FlowGroup>();
        foreach (var folder in Folders)
        {
            var items = Flows.Where(f => f.FolderId == folder.Id).ToList();
            if (items.Count == 0) continue;
            var title = folder.Depth > 0 ? string.Join(" › ", Ancestors(folder).Select(a => a.Name)) : folder.Name;
            groups.Add(new FlowGroup(folder.Id, title, items, !collapsed.Contains(folder.Id), Persist));
        }
        var loose = Flows.Where(f => f.FolderId == null || !folders.ContainsKey(f.FolderId)).ToList();
        if (loose.Count > 0) groups.Add(new FlowGroup("", Loc.T("Zonder map"), loose, !collapsed.Contains(""), Persist));
        FlowGroups.Clear();
        foreach (var g in groups) FlowGroups.Add(g);
    }

    IEnumerable<FolderVM> Ancestors(FolderVM folder)
    {
        var chain = new List<FolderVM>();
        for (var f = folder; f != null; f = f.ParentId != null ? folders.GetValueOrDefault(f.ParentId) : null)
        {
            chain.Insert(0, f);
            if (chain.Count > 10) break;
        }
        return chain;
    }

    async Task LoadMoods(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/moods/mood", ct);
        ct.ThrowIfCancellationRequested();
        var list = new List<MoodVM>();
        foreach (var m in J.Items(node))
        {
            if (J.Str(m, "id") is not { } id) continue;
            if (!moods.TryGetValue(id, out var vm)) moods[id] = vm = new MoodVM(id);
            vm.Name = J.Str(m, "name") ?? id;
            vm.ZoneId = J.Str(m, "zone");
            vm.ZoneName = ZoneName(vm.ZoneId);
            list.Add(vm);
        }
        Sync(Moods, list.OrderBy(m => Zone(m.ZoneId)?.Order ?? int.MaxValue).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    async Task LoadVariables(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/logic/variable", ct);
        ct.ThrowIfCancellationRequested();
        var list = new List<VariableVM>();
        foreach (var v in J.Items(node))
        {
            if (J.Str(v, "id") is not { } id) continue;
            if (!variables.TryGetValue(id, out var vm)) variables[id] = vm = new VariableVM(id);
            vm.Update(v);
            list.Add(vm);
        }
        Sync(Variables, list.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    async Task LoadUsers(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/users/user", ct);
        ct.ThrowIfCancellationRequested();
        var list = new List<UserVM>();
        foreach (var u in J.Items(node))
        {
            if (J.Str(u, "id") is not { } id) continue;
            if (J.Str(u, "role") == "guest" && J.Bool(u, "present") != true) continue;
            if (!users.TryGetValue(id, out var vm)) users[id] = vm = new UserVM(id);
            vm.Name = J.Str(u, "name") ?? "?";
            vm.Present = J.Bool(u, "present") == true;
            vm.Asleep = J.Bool(u, "asleep") == true;
            list.Add(vm);
        }
        Sync(Users, list.OrderBy(u => u.Name).ToList());
    }

    async Task LoadNotifications(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/notifications/notification", ct);
        ct.ThrowIfCancellationRequested();
        var list = new List<NotificationVM>();
        foreach (var n in J.Items(node))
        {
            if (J.Str(n, "id") is not { } id) continue;
            if (!notifications.TryGetValue(id, out var vm))
            {
                vm = new NotificationVM(id)
                {
                    Text = Clean(J.Str(n, "excerpt") ?? ""),
                    Owner = J.Str(n, "ownerName") ?? Owner(J.Str(n, "ownerUri")),
                    Date = J.Date(n, "dateCreated") ?? DateTime.Now,
                };
                notifications[id] = vm;
            }
            list.Add(vm);
        }
        list = list.OrderByDescending(n => n.Date).Take(300).ToList();
        var newest = list.FirstOrDefault()?.Date;
        if (lastNotification != null)
            foreach (var n in list.Where(n => n.Date > lastNotification).Reverse()) NewNotification?.Invoke(n);
        if (newest != null && (lastNotification == null || newest > lastNotification)) lastNotification = newest;
        lastNotification ??= DateTime.Now;
        Sync(Notifications, list);
    }

    // Homey marks names in a notification with **
    static string Clean(string s) => s.Replace("**", "").Trim();

    static string Owner(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return "Homey";
        var last = uri.Split(':').Last();
        return last switch { "flow" => "Flow", "energy" => Loc.T("Energie"), "updates" => "Updates", "apps" => "Apps", _ => last };
    }

    async Task LoadWeather(HomeyClient c, CancellationToken ct)
    {
        var now = await c.GetAsync("/api/manager/weather/weather", ct);
        JsonNode? hourly = null;
        try { hourly = await c.GetAsync("/api/manager/weather/forecast/hourly", ct); }
        catch (HomeyApiException) { }
        ct.ThrowIfCancellationRequested();
        Weather.Update(now, hourly);
    }

    async Task LoadEnergyLive(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/energy/live", ct);
        ct.ThrowIfCancellationRequested();
        ComputeLiveEnergy(node);
    }

    JsonNode? lastLive;

    // Power right now: the grid meter, solar panels and home batteries, then the devices.
    // Homey Energy's live report adds devices that only have an estimated use (lights).
    void ComputeLiveEnergy(JsonNode? live)
    {
        if (live != null) lastLive = live;
        var e = Energy;
        var grid = Devices.Where(d => d.IsGridMeter && d.PowerW != null).ToList();
        var solar = Devices.Where(d => d.IsSolar).ToList();
        var batteries = Devices.Where(d => d.IsHomeBattery).ToList();
        e.GridW = grid.Count > 0 ? grid.Sum(d => d.PowerW!.Value) : null;
        e.HasSolar = solar.Count > 0;
        e.SolarW = solar.Sum(d => Math.Abs(d.PowerW ?? 0));
        e.HasBattery = batteries.Count > 0;
        e.BatteryW = batteries.Sum(d => d.PowerW ?? 0);
        e.BatteryPercent = EnergyMath.StateOfCharge(batteries.Select(d => d.BatteryLevel));

        var consumers = Devices
            .Where(d => !d.IsGridMeter && !d.IsSolar && !d.IsHomeBattery && d.Class != "battery" && d.PowerW is > 0.5)
            .Select(d => (name: d.Name, watts: d.PowerW!.Value, estimated: false, glyph: d.Glyph))
            .ToList();
        var items = J.Arr(lastLive, "items") ?? (lastLive as JsonArray);
        foreach (var item in items?.OfType<JsonObject>() ?? [])
        {
            if (J.Str(item, "type") != "device" || J.Num(J.Obj(item, "values"), "W") is not > 0.5) continue;
            var id = J.Str(item, "id");
            if (id == null || !devices.TryGetValue(id, out var d) || d.Has("measure_power") || d.IsGridMeter || d.IsSolar || d.IsHomeBattery) continue;
            consumers.Add((d.Name, J.Num(J.Obj(item, "values"), "W")!.Value, true, d.Glyph));
        }
        consumers = consumers.OrderByDescending(x => x.watts).ToList();
        e.HomeW = e.GridW is { } g ? Math.Max(0, g + e.SolarW - e.BatteryW) : consumers.Count > 0 ? consumers.Sum(x => x.watts) : null;

        var max = consumers.Count > 0 ? consumers[0].watts : 1;
        var list = consumers.Take(12).Select(x => new ConsumerVM
        {
            Name = x.name,
            Value = x.watts,
            Fraction = x.watts / max,
            Estimated = x.estimated,
            Glyph = x.glyph,
            ValueText = EnergyVM.Watts(x.watts) + (x.estimated ? " *" : ""),
        }).ToList();
        if (!e.Consumers.Select(x => (x.Name, x.ValueText)).SequenceEqual(list.Select(x => (x.Name, x.ValueText))))
        {
            e.Consumers.Clear();
            foreach (var x in list) e.Consumers.Add(x);
        }
        e.Changed();
        Theme.SetMood(e.Mood);
    }

    // ---- Energy history from Insights ----

    async Task<List<(DateTime t, double v)>> Entries(HomeyClient c, string deviceId, string capability, string resolution, CancellationToken ct)
    {
        if (insightLogs == null || DateTime.UtcNow - insightLogsAt > TimeSpan.FromMinutes(10))
        {
            insightLogs = await c.GetAsync("/api/manager/insights/log", ct);
            insightLogsAt = DateTime.UtcNow;
        }
        var log = J.Items(insightLogs).FirstOrDefault(l => J.Str(l, "ownerUri") == $"homey:device:{deviceId}" && J.Str(l, "ownerId") == capability)
            ?? J.Items(insightLogs).FirstOrDefault(l => J.Str(l, "id") == $"homey:device:{deviceId}:{capability}");
        if (log == null || J.Str(log, "id") is not { } id) return [];
        // Same addressing as homey-api: /log/<owner uri>/<full log id>/entry
        var uri = string.Join(':', id.Split(':').Take(3));
        var node = await c.GetAsync($"/api/manager/insights/log/{uri}/{id}/entry?resolution={resolution}", ct);
        return (J.Arr(node, "values") ?? [])
            .OfType<JsonObject>()
            .Select(v => (t: J.Date(v, "t"), v: J.Num(v, "v")))
            .Where(x => x.t != null && x.v != null)
            .Select(x => (x.t!.Value, x.v!.Value))
            .OrderBy(x => x.Item1)
            .ToList();
    }

    static Dictionary<DateTime, double> PerDay(List<(DateTime t, double v)> entries) => EnergyMath.PerDay(entries);
    static double Sum(Dictionary<DateTime, double> days, Func<DateTime, bool> pick) => EnergyMath.Sum(days, pick);

    async Task LoadEnergyReport(HomeyClient c, CancellationToken ct)
    {
        var e = Energy;
        e.ReportStatus = Loc.T("Rapport wordt geladen…");
        e.Changed();

        var meter = Devices.FirstOrDefault(d => d.IsGridMeter && (d.ImportCapability != null || d.Has("meter_power.imported") || d.Has("meter_power")));
        var importCap = meter?.ImportCapability ?? (meter?.Has("meter_power.imported") == true ? "meter_power.imported" : meter?.Has("meter_power") == true ? "meter_power" : null);
        var exportCap = meter?.ExportCapability ?? (meter?.Has("meter_power.exported") == true ? "meter_power.exported" : null);

        Dictionary<DateTime, double> import = [], export = [], solar = [], gas = [];
        if (meter != null && importCap != null) import = PerDay(await Entries(c, meter.Id, importCap, "last31Days", ct));
        if (meter != null && exportCap != null) export = PerDay(await Entries(c, meter.Id, exportCap, "last31Days", ct));
        if (meter != null && meter.Has("meter_gas")) gas = PerDay(await Entries(c, meter.Id, "meter_gas", "last31Days", ct));
        foreach (var panel in Devices.Where(d => d.IsSolar && d.Has("meter_power")))
            foreach (var (day, kwh) in PerDay(await Entries(c, panel.Id, "meter_power", "last31Days", ct)))
                solar[day] = solar.GetValueOrDefault(day) + kwh;
        // Home batteries count their charged and discharged kWh; without them, charging would read as use
        Dictionary<DateTime, double> charged = [], discharged = [];
        var homeBatteries = Devices.Where(d => d.IsHomeBattery && d.ChargedCapability != null && d.DischargedCapability != null).ToList();
        foreach (var battery in homeBatteries)
        {
            foreach (var (day, kwh) in PerDay(await Entries(c, battery.Id, battery.ChargedCapability!, "last31Days", ct)))
                charged[day] = charged.GetValueOrDefault(day) + kwh;
            foreach (var (day, kwh) in PerDay(await Entries(c, battery.Id, battery.DischargedCapability!, "last31Days", ct)))
                discharged[day] = discharged.GetValueOrDefault(day) + kwh;
        }
        ct.ThrowIfCancellationRequested();

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        bool IsToday(DateTime d) => d == today;
        bool IsMonth(DateTime d) => d >= monthStart;
        var hasMeter = meter != null;
        e.TodayImport = hasMeter ? EnergyVM.Kwh(Sum(import, IsToday)) : "–";
        e.TodayExport = exportCap != null ? EnergyVM.Kwh(Sum(export, IsToday)) : "–";
        e.TodaySolar = solar.Count > 0 ? EnergyVM.Kwh(Sum(solar, IsToday)) : "–";
        double Use(Func<DateTime, bool> pick) =>
            EnergyMath.Use(Sum(import, pick), Sum(solar, pick), Sum(export, pick), Sum(charged, pick), Sum(discharged, pick));
        e.TodayUse = hasMeter ? EnergyVM.Kwh(Use(IsToday)) : "–";
        e.MonthImport = hasMeter ? EnergyVM.Kwh(Sum(import, IsMonth)) : "–";
        e.MonthExport = exportCap != null ? EnergyVM.Kwh(Sum(export, IsMonth)) : "–";
        e.MonthSolar = solar.Count > 0 ? EnergyVM.Kwh(Sum(solar, IsMonth)) : "–";
        e.MonthUse = hasMeter ? EnergyVM.Kwh(Use(IsMonth)) : "–";
        e.HasBatteryTotals = homeBatteries.Count > 0;
        e.TodayCharged = EnergyVM.Kwh(Sum(charged, IsToday));
        e.TodayDischarged = EnergyVM.Kwh(Sum(discharged, IsToday));
        e.MonthCharged = EnergyVM.Kwh(Sum(charged, IsMonth));
        e.MonthDischarged = EnergyVM.Kwh(Sum(discharged, IsMonth));
        e.TodayGas = gas.Count > 0 ? Sum(gas, IsToday).ToString("0.00", Loc.Culture) + " m³" : "";
        e.MonthGas = gas.Count > 0 ? Sum(gas, IsMonth).ToString("0.0", Loc.Culture) + " m³" : "";

        e.Days.Clear();
        var span = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).ToList();
        var maxDay = span.Select(d => Math.Max(import.GetValueOrDefault(d), solar.GetValueOrDefault(d))).DefaultIfEmpty(0).Max();
        if (maxDay <= 0) maxDay = 1;
        foreach (var day in span)
        {
            var imp = import.GetValueOrDefault(day);
            var sol = solar.GetValueOrDefault(day);
            var tooltip = $"{day.ToString("ddd d MMM", Loc.Culture)}\n{Loc.T("Van het net")}: {EnergyVM.Kwh(imp)}\n{Loc.T("Zon")}: {EnergyVM.Kwh(sol)}";
            if (homeBatteries.Count > 0)
                tooltip += $"\n{Loc.T("Batterij geladen")}: {EnergyVM.Kwh(charged.GetValueOrDefault(day))}\n{Loc.T("Batterij ontladen")}: {EnergyVM.Kwh(discharged.GetValueOrDefault(day))}";
            e.Days.Add(new BarVM
            {
                Label = day.ToString("dd", Loc.Culture),
                Tooltip = tooltip,
                Import = imp,
                Solar = sol,
                ImportHeight = imp / maxDay * 120,
                SolarHeight = sol / maxDay * 120,
            });
        }

        // Use per device today, from their own kWh counters
        var consumers = Devices.Where(d => d.Has("meter_power") && !d.IsGridMeter && !d.IsSolar && !d.IsHomeBattery && d.Class != "battery").Take(40).ToList();
        var perDevice = new List<(DeviceVM d, double kwh)>();
        foreach (var d in consumers)
        {
            try
            {
                var kwh = Sum(PerDay(await Entries(c, d.Id, "meter_power", "today", ct)), IsToday);
                if (kwh > 0.005) perDevice.Add((d, kwh));
            }
            catch (HomeyApiException) { }
        }
        ct.ThrowIfCancellationRequested();
        perDevice = perDevice.OrderByDescending(x => x.kwh).ToList();
        e.TodayDevices.Clear();
        var top = perDevice.Count > 0 ? perDevice[0].kwh : 1;
        foreach (var (d, kwh) in perDevice.Take(15))
            e.TodayDevices.Add(new ConsumerVM { Name = d.Name, Value = kwh, Fraction = kwh / top, Glyph = d.Glyph, ValueText = kwh.ToString("0.00", Loc.Culture) + " kWh" });

        e.ReportLoaded = true;
        e.ReportStatus = meter == null ? Loc.T("Geen slimme meter gevonden; alleen apparaten met een kWh-meter worden geteld.") : "";
        e.Changed();
    }

    // ---- System ----

    async Task LoadSystem(HomeyClient c, CancellationToken ct)
    {
        var info = await c.GetAsync("/api/manager/system/", ct);
        JsonNode? memory = null, storage = null, updates = null;
        try { memory = await c.GetAsync("/api/manager/system/memory", ct); } catch (HomeyApiException) { }
        try { storage = await c.GetAsync("/api/manager/system/storage", ct); } catch (HomeyApiException) { }
        try { updates = await c.GetAsync("/api/manager/updates/update", ct); } catch (HomeyApiException) { }
        ct.ThrowIfCancellationRequested();

        var s = System;
        s.Model = J.Str(info, "homeyModelName") ?? J.Str(info, "modelName") ?? J.Str(info, "homeyModelId") ?? "Homey";
        s.Version = J.Str(info, "homeyVersion") ?? J.Str(info, "version") ?? "";
        s.Address = J.Str(info, "address") ?? J.Str(info, "ipAddress") ?? "";
        s.Wifi = J.Str(info, "wifiSsid") ?? "";
        if (J.Num(info, "uptime") is { } up)
        {
            var t = TimeSpan.FromSeconds(up);
            s.Uptime = t.TotalDays >= 1 ? Loc.F("{0} d {1} u", (int)t.TotalDays, t.Hours) : Loc.F("{0} u {1} min", t.Hours, t.Minutes);
        }

        (s.MemoryUsed, s.MemoryTotal) = Usage(memory);
        (s.StorageUsed, s.StorageTotal) = Usage(storage);

        s.UpdateVersion = J.Items(updates).Select(u => J.Str(u, "version")).FirstOrDefault(v => v != null)
            ?? (updates as JsonArray)?.OfType<JsonObject>().Select(u => J.Str(u, "version")).FirstOrDefault(v => v != null);
        s.Loaded = true;
        s.Changed();
    }

    static (double? used, double? total) Usage(JsonNode? n)
    {
        if (n == null) return (null, null);
        var total = J.Num(n, "total") ?? J.Num(n, "size");
        var free = J.Num(n, "free") ?? J.Num(n, "available");
        var used = J.Num(n, "used");
        if (used == null && total != null && free != null) used = total - free;
        if (used == null && J.Obj(n, "types") is { } types)
            used = types.Sum(p => J.Num(p.Value, "size") ?? (J.Raw(p.Value) as double?) ?? 0);
        return (used, total);
    }

    async Task LoadApps(HomeyClient c, CancellationToken ct)
    {
        var node = await c.GetAsync("/api/manager/apps/app", ct);
        ct.ThrowIfCancellationRequested();
        var list = new List<AppVM>();
        foreach (var a in J.Items(node))
        {
            if (J.Str(a, "id") is not { } id) continue;
            if (!apps.TryGetValue(id, out var vm)) apps[id] = vm = new AppVM(id);
            vm.Update(a);
            list.Add(vm);
        }
        Sync(Apps, list.OrderByDescending(a => a.IsCrashed).ThenByDescending(a => a.HasUpdate).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        _ = LoadAppIcons(c, list.Where(a => !a.IconRequested).ToList());
    }

    // Each icon once per app, one after the other so Homey is not flooded
    static async Task LoadAppIcons(HomeyClient c, List<AppVM> list)
    {
        // Claimed up front, so the next poll does not start on the same apps while this one runs
        foreach (var app in list) app.IconRequested = true;
        for (var i = 0; i < list.Count; i++)
        {
            foreach (var url in list[i].IconUrls)
            {
                byte[]? data;
                try { data = await c.GetBytesAsync(url); }
                catch (HomeyOfflineException)
                {
                    // Gone offline: the rest get another try after reconnecting
                    for (var j = i; j < list.Count; j++) list[j].IconRequested = false;
                    return;
                }
                catch (Exception e)
                {
                    Log.Error("App icon " + url, e);
                    continue;
                }
                if (data is { Length: > 0 } && Icons.FromFile(data) is { } image) { list[i].Icon = image; break; }
            }
        }
    }

    // ---- Actions ----

    // refused: a chance to explain a refusal better than Homey's own words; true when it did
    async Task Act(Func<HomeyClient, Task> action, Action? onError = null, Func<HomeyApiException, bool>? refused = null)
    {
        if (client is not { BaseUrl: not null } c)
        {
            ShowError(Loc.T("Niet verbonden met Homey"));
            onError?.Invoke();
            return;
        }
        try { await action(c); }
        catch (HomeyApiException e) when (refused?.Invoke(e) == true)
        {
            onError?.Invoke();
        }
        catch (HomeyApiException e) when (e.Status == 403)
        {
            // Homey also answers 403 when it refuses one action; only speak of rights when it is about them
            ShowError(AlwaysOn.IsMissingRights(e.Message) ? NoRights() : e.Message);
            onError?.Invoke();
        }
        catch (Exception e) when (e is HomeyApiException or HomeyOfflineException)
        {
            ShowError(e.Message);
            onError?.Invoke();
        }
    }

    public Task SetCapabilityAsync(CapabilityVM cap, object? value)
    {
        if (value == null) return Task.CompletedTask;
        return Act(async c =>
        {
            await c.PutAsync($"/api/manager/devices/device/{cap.Device.Id}/capability/{Uri.EscapeDataString(cap.Id)}", new { value });
            Kick();
        }, () => { cap.Release(); Kick(); }, e =>
        {
            // Homey keeps this device always on: remember it, so the switch is no longer offered
            if (cap.Id != "onoff" || !AlwaysOn.IsRefusal(e.Message)) return false;
            cap.Device.LearnAlwaysOn();
            ShowError(AlwaysOn.Message(cap.Device.Name));
            return true;
        });
    }

    public Task RunFlowAsync(FlowVM flow) => Act(async c =>
    {
        flow.State = "running";
        try
        {
            await c.PostAsync($"/api/manager/flow/{(flow.IsAdvanced ? "advancedflow" : "flow")}/{flow.Id}/trigger");
            flow.State = "done";
        }
        catch { flow.State = "failed"; throw; }
        finally { _ = ResetLater(flow); }
    });

    static async Task ResetLater(FlowVM flow)
    {
        await Task.Delay(2000);
        flow.State = "";
    }

    public Task SetMoodAsync(MoodVM mood) => Act(async c =>
    {
        mood.Busy = true;
        try { await c.PostAsync($"/api/manager/moods/mood/{mood.Id}/set"); }
        finally { mood.Busy = false; }
        Kick();
    });

    public Task SetVariableAsync(VariableVM variable, object value) =>
        Act(c => c.PutAsync($"/api/manager/logic/variable/{variable.Id}", new { value }));

    public Task DeleteNotificationAsync(NotificationVM n) => Act(async c =>
    {
        await c.DeleteAsync($"/api/manager/notifications/notification/{n.Id}");
        notifications.Remove(n.Id);
        Notifications.Remove(n);
    });

    public Task RestartAppAsync(AppVM app) => Act(async c =>
    {
        app.Busy = true;
        try { await c.PostAsync($"/api/manager/apps/app/{Uri.EscapeDataString(app.Id)}/restart"); }
        finally { app.Busy = false; }
        await LoadApps(c, CancellationToken.None);
    });

    public void ToggleFavorite(DeviceVM d)
    {
        if (Config == null) return;
        d.IsFavorite = !d.IsFavorite;
        if (d.IsFavorite) Config.FavoriteDevices.Add(d.Id); else Config.FavoriteDevices.Remove(d.Id);
        App.Settings.Save();
        RebuildFavorites();
    }

    public void ToggleFavorite(FlowVM f)
    {
        if (Config == null) return;
        f.IsFavorite = !f.IsFavorite;
        if (f.IsFavorite) Config.FavoriteFlows.Add(f.Id); else Config.FavoriteFlows.Remove(f.Id);
        App.Settings.Save();
        RebuildFavorites();
    }

    public void MoveFavorite(DeviceVM d, int delta)
    {
        if (Config == null || !Favorites.Move(Config.FavoriteDevices, devices.ContainsKey, d.Id, delta)) return;
        App.Settings.Save();
        RebuildFavorites();
    }

    public void MoveFavorite(FlowVM f, int delta)
    {
        if (Config == null || !Favorites.Move(Config.FavoriteFlows, flows.ContainsKey, f.Id, delta)) return;
        App.Settings.Save();
        RebuildFavorites();
    }

    public bool CanMoveFavorite(DeviceVM d, int delta) => Config != null && Favorites.CanMove(Config.FavoriteDevices, devices.ContainsKey, d.Id, delta);
    public bool CanMoveFavorite(FlowVM f, int delta) => Config != null && Favorites.CanMove(Config.FavoriteFlows, flows.ContainsKey, f.Id, delta);

    public void SetCustomIcon(DeviceVM d, string? glyph)
    {
        if (Config == null) return;
        if (string.IsNullOrEmpty(glyph)) Config.CustomIcons.Remove(d.Id); else Config.CustomIcons[d.Id] = glyph;
        d.CustomGlyph = string.IsNullOrEmpty(glyph) ? null : glyph;
        App.Settings.Save();
    }

    // The user's choice of tile details; an empty list means none at all
    public void SetTileDetails(DeviceVM d, List<string> ids)
    {
        if (Config == null) return;
        Config.TileDetails[d.Id] = ids;
        d.TileDetails = ids;
        App.Settings.Save();
    }

    public void Reconnect()
    {
        client?.Disconnect();
        Kick();
    }
}
