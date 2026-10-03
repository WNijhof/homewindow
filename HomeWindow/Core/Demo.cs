using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeWindow.Core;

// A made-up home that answers like the Homey Web API, for trying HomeWindow without a Homey
// (HomeWindow.exe --demo). Commands change the values in memory.
public sealed class Demo
{
    public static HomeyConfig Config() => new()
    {
        Id = "demo",
        Name = "Demo Homey",
        Mode = "demo",
        FavoriteDevices = ["light-living", "light-dining", "thermostat", "blinds-living", "speaker", "lock-front"],
        FavoriteFlows = ["flow-movie", "flow-away", "flow-goodnight"],
    };

    readonly JsonObject devices = [];
    readonly JsonObject variables = [];
    readonly Random random = new(7);

    public Demo()
    {
        Device("light-living", "Plafondlamp", "living", "light", [OnOff(true), Dim(0.7), LightTemp(0.4)]);
        Device("light-dining", "Eettafel", "living", "light", [OnOff(false), Dim(0.5)]);
        Device("light-reading", "Leeslamp", "living", "light", [OnOff(true), Dim(0.3)]);
        Device("blinds-living", "Rolluik", "living", "windowcoverings", [Cover(0.6), CoverState()]);
        Device("speaker", "Sonos", "living", "speaker", [Playing(true), Volume(0.35), Text("speaker_track", "Track", "So What"), Text("speaker_artist", "Artist", "Miles Davis"), Button("speaker_next", "Next"), Button("speaker_prev", "Previous")]);
        Device("tv", "Televisie", "living", "tv", [OnOff(false)]);
        Device("thermostat", "Thermostaat", "living", "thermostat", [Target(20.5), Measure("measure_temperature", "Temperature", 20.1, "°C", 1), Measure("measure_humidity", "Humidity", 48, "%", 0)]);
        Device("motion-living", "Bewegingssensor", "living", "sensor", [Alarm("alarm_motion", "Motion", false), Measure("measure_battery", "Battery", 64, "%", 0), Measure("measure_luminance", "Light", 220, "lx", 0)], ["CR2450"]);
        Device("light-kitchen", "Keukenspots", "kitchen", "light", [OnOff(false), Dim(1)]);
        Device("coffee", "Koffiemachine", "kitchen", "socket", [OnOff(true), Measure("measure_power", "Power", 1240, "W", 0), Measure("meter_power", "Energy", 312.4, "kWh", 2)]);
        Device("fridge", "Koelkast", "kitchen", "socket", [OnOff(true), Measure("measure_power", "Power", 85, "W", 0), Measure("meter_power", "Energy", 410.3, "kWh", 2)]);
        // Set to "Always on" in Homey: turning it off is refused
        devices["fridge"]!["settings"] = new JsonObject { ["energy_alwayson"] = true };
        Device("dishwasher", "Vaatwasser", "kitchen", "socket", [OnOff(true), Measure("measure_power", "Power", 1870, "W", 0), Measure("meter_power", "Energy", 802.1, "kWh", 2)]);
        Device("contact-back", "Achterdeur", "kitchen", "sensor", [Alarm("alarm_contact", "Contact", false), Measure("measure_battery", "Battery", 12, "%", 0)], ["CR2032"]);
        Device("light-bed", "Bedlampje", "bedroom", "light", [OnOff(false), Dim(0.2)]);
        Device("heater-bed", "Radiatorkraan", "bedroom", "thermostat", [Target(18), Measure("measure_temperature", "Temperature", 17.4, "°C", 1), Measure("measure_battery", "Battery", 81, "%", 0)], ["AA", "AA"]);
        Device("lock-front", "Voordeur", "hall", "lock", [Bool("locked", "Locked", true, true), Measure("measure_battery", "Battery", 35, "%", 0)], ["AA", "AA", "AA", "AA"]);
        Device("doorbell", "Deurbel", "hall", "doorbell", [Alarm("alarm_generic", "Ring", false), Measure("measure_battery", "Battery", 90, "%", 0)]);
        Device("light-garden", "Tuinverlichting", "garden", "light", [OnOff(false)]);
        Device("p1", "Slimme meter", "meterkast", "sensor", [Measure("measure_power", "Power", -1450, "W", 0), Measure("meter_power.imported", "Imported", 5123.4, "kWh", 2), Measure("meter_power.exported", "Exported", 2210.8, "kWh", 2), Measure("meter_gas", "Gas", 3011.2, "m³", 3)],
            energy: new JsonObject { ["cumulative"] = true, ["cumulativeImportedCapability"] = "meter_power.imported", ["cumulativeExportedCapability"] = "meter_power.exported" });
        Device("solar", "Omvormer", "meterkast", "solarpanel", [Measure("measure_power", "Power", 3480, "W", 0), Measure("meter_power", "Energy", 9811.0, "kWh", 2)]);
        Device("home-battery", "Thuisbatterij", "meterkast", "battery", [Measure("measure_power", "Power", 850, "W", 0), Measure("measure_battery", "Battery", 62, "%", 0),
                Measure("meter_power.charged", "Charged", 1210.5, "kWh", 2), Measure("meter_power.discharged", "Discharged", 1088.2, "kWh", 2)],
            energy: new JsonObject { ["homeBattery"] = true, ["meterPowerImportedCapability"] = "meter_power.charged", ["meterPowerExportedCapability"] = "meter_power.discharged" });
        Device("washer", "Wasmachine", "attic", "socket", [OnOff(false), Measure("measure_power", "Power", 0, "W", 0)]);
        Device("smoke", "Rookmelder", "attic", "sensor", [Alarm("alarm_smoke", "Smoke", false), Bool("alarm_battery", "Battery alarm", false, false)], ["9V"]);

        foreach (var (id, name, type, value) in new (string, string, string, JsonNode)[]
        {
            ("var-guests", "Gasten aanwezig", "boolean", false), ("var-holiday", "Vakantiemodus", "boolean", false),
            ("var-temp", "Comforttemperatuur", "number", 20.5), ("var-scene", "Laatste scène", "string", "Film"),
        })
            variables[id] = new JsonObject { ["id"] = id, ["name"] = name, ["type"] = type, ["value"] = value };
    }

    static readonly (string id, string name, string? parent)[] Zones =
    [
        ("home", "Thuis", null), ("ground", "Begane grond", "home"), ("living", "Woonkamer", "ground"),
        ("kitchen", "Keuken", "ground"), ("hall", "Hal", "ground"), ("meterkast", "Meterkast", "ground"),
        ("first", "Eerste verdieping", "home"), ("bedroom", "Slaapkamer", "first"), ("attic", "Zolder", "first"),
        ("garden", "Tuin", "home"),
    ];

    void Device(string id, string name, string zone, string cls, JsonObject[] caps, string[]? batteries = null, JsonObject? energy = null)
    {
        var obj = new JsonObject();
        foreach (var c in caps) obj[J.Str(c, "id")!] = c;
        energy ??= [];
        if (batteries != null) energy["batteries"] = new JsonArray(batteries.Select(b => (JsonNode)b!).ToArray());
        devices[id] = new JsonObject
        {
            ["id"] = id, ["name"] = name, ["zone"] = zone, ["class"] = cls, ["available"] = true, ["ready"] = true,
            ["capabilities"] = new JsonArray(caps.Select(c => (JsonNode)J.Str(c, "id")!).ToArray()),
            ["capabilitiesObj"] = obj, ["energyObj"] = energy, ["ui"] = new JsonObject(),
        };
    }

    static JsonObject Cap(string id, string title, string type, JsonNode? value, bool setable, bool getable = true) => new()
    {
        ["id"] = id, ["title"] = title, ["type"] = type, ["value"] = value, ["setable"] = setable, ["getable"] = getable,
        ["lastUpdated"] = DateTime.UtcNow.AddMinutes(-3).ToString("o"),
    };

    static JsonObject OnOff(bool on) => Cap("onoff", "Turned on", "boolean", on, true);
    static JsonObject Bool(string id, string title, bool v, bool setable) => Cap(id, title, "boolean", v, setable);
    static JsonObject Alarm(string id, string title, bool v) => Cap(id, title, "boolean", v, false);
    static JsonObject Button(string id, string title) => Cap(id, title, "boolean", null, true, false);
    static JsonObject Text(string id, string title, string v) => Cap(id, title, "string", v, false);
    static JsonObject Playing(bool v) => Cap("speaker_playing", "Playing", "boolean", v, true);

    static JsonObject Range(string id, string title, double v, double min, double max, double step, string? units = null)
    {
        var c = Cap(id, title, "number", v, true);
        c["min"] = min; c["max"] = max; c["step"] = step;
        if (units != null) c["units"] = units;
        return c;
    }

    static JsonObject Dim(double v) => Range("dim", "Dim level", v, 0, 1, 0.01, "%");
    static JsonObject LightTemp(double v) => Range("light_temperature", "Color temperature", v, 0, 1, 0.01);
    static JsonObject Volume(double v) => Range("volume_set", "Volume", v, 0, 1, 0.01, "%");
    static JsonObject Cover(double v) => Range("windowcoverings_set", "Position", v, 0, 1, 0.01, "%");
    static JsonObject Target(double v) => Range("target_temperature", "Target temperature", v, 5, 30, 0.5, "°C");

    static JsonObject CoverState()
    {
        var c = Cap("windowcoverings_state", "State", "enum", "idle", true);
        c["values"] = new JsonArray(
            new JsonObject { ["id"] = "up", ["title"] = "Up" },
            new JsonObject { ["id"] = "idle", ["title"] = "Stop" },
            new JsonObject { ["id"] = "down", ["title"] = "Down" });
        return c;
    }

    static JsonObject Measure(string id, string title, double v, string units, int decimals)
    {
        var c = Cap(id, title, "number", v, false);
        c["units"] = units;
        c["decimals"] = decimals;
        return c;
    }

    // Simple app icons, so the demo shows how real ones look
    public byte[]? File(string url)
    {
        var shape = url switch
        {
            _ when url.Contains("sonos") => "<circle cx='32' cy='32' r='20' fill='none' stroke='#000' stroke-width='6'/><circle cx='32' cy='32' r='6'/>",
            _ when url.Contains("hue") => "<path d='M32 6a18 18 0 0 0-10 33v7h20v-7A18 18 0 0 0 32 6zM24 50h16v4H24zm4 6h8v3h-8z'/>",
            _ when url.Contains("somfy") => "<path d='M8 12h48v8H8zm0 12h48v6H8zm0 10h48v6H8zm0 10h48v6H8z'/>",
            _ when url.Contains("danalock") => "<path d='M20 28v-8a12 12 0 0 1 24 0v8h4v28H16V28zm6 0h12v-8a6 6 0 0 0-12 0z'/>",
            _ => null,
        };
        return shape == null ? null : System.Text.Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'>{shape}</svg>");
    }

    public JsonNode? Handle(HttpMethod method, string path, object? body)
    {
        var p = path.Split('?')[0];
        if (method == HttpMethod.Put && p.StartsWith("/api/manager/devices/device/"))
        {
            var parts = p.Split('/');
            var cap = devices[parts[5]]?["capabilitiesObj"]?[Uri.UnescapeDataString(parts[7])];
            if (cap != null && body != null)
            {
                var value = JsonSerializer.SerializeToNode(body)?["value"]?.DeepClone();
                if (J.Str(cap, "id") == "onoff" && J.Bool(J.Obj(devices[parts[5]], "settings"), "energy_alwayson") == true && J.Raw(value) is false)
                    throw new HomeyApiException(400, "This device is set to Always On and cannot be turned off");
                if (J.Str(cap, "type") == "boolean" && J.Bool(cap, "getable") == false) return null;
                cap["value"] = value;
                cap["lastUpdated"] = DateTime.UtcNow.ToString("o");
            }
            return null;
        }
        if (method == HttpMethod.Put && p.StartsWith("/api/manager/logic/variable/"))
        {
            if (variables[p.Split('/').Last()] is JsonObject v && body != null) v["value"] = JsonSerializer.SerializeToNode(body)?["value"]?.DeepClone();
            return null;
        }
        if (method != HttpMethod.Get) return null;

        Wiggle();
        var result = p switch
        {
            "/api/manager/system/name" => "Demo Homey",
            "/api/manager/devices/device" => devices.DeepClone(),
            "/api/manager/zones/zone" => new JsonObject(Zones.Select(z => KeyValuePair.Create(z.id, (JsonNode?)new JsonObject { ["id"] = z.id, ["name"] = z.name, ["parent"] = z.parent }))),
            "/api/manager/flow/flowfolder" => Folders(),
            "/api/manager/flow/flow" => Flows(false),
            "/api/manager/flow/advancedflow" => Flows(true),
            "/api/manager/moods/mood" => Moods(),
            "/api/manager/logic/variable" => variables.DeepClone(),
            "/api/manager/users/user" => new JsonObject
            {
                ["u1"] = new JsonObject { ["id"] = "u1", ["name"] = "Sanne de Vries", ["present"] = true, ["asleep"] = false },
                ["u2"] = new JsonObject { ["id"] = "u2", ["name"] = "Joris", ["present"] = false, ["asleep"] = false },
            },
            "/api/manager/notifications/notification" => Notifications(),
            "/api/manager/weather/weather" => new JsonObject { ["temperature"] = 17.2, ["state"] = "Clouds", ["humidity"] = 0.71, ["pressure"] = 1.016 },
            "/api/manager/weather/forecast/hourly" => new JsonArray(Enumerable.Range(1, 8).Select(i => (JsonNode)new JsonObject
            {
                ["date"] = DateTime.Now.AddHours(i).ToString("o"), ["temperature"] = 17 + Math.Sin(i / 2.0) * 2, ["state"] = i % 3 == 0 ? "Clear" : "Clouds",
            }).ToArray()),
            "/api/manager/energy/live" => new JsonObject
            {
                ["items"] = new JsonArray(
                    new JsonObject { ["type"] = "device", ["id"] = "light-living", ["values"] = new JsonObject { ["W"] = 9.5 } },
                    new JsonObject { ["type"] = "device", ["id"] = "light-reading", ["values"] = new JsonObject { ["W"] = 4 } },
                    new JsonObject { ["type"] = "device", ["id"] = "speaker", ["values"] = new JsonObject { ["W"] = 12 } }),
            },
            "/api/manager/system/" => new JsonObject
            {
                ["homeyModelName"] = "Homey Pro (Early 2023)", ["homeyVersion"] = "12.4.1", ["address"] = "192.168.1.50", ["wifiSsid"] = "Thuisnetwerk",
                ["uptime"] = 1_036_800 + Environment.TickCount64 / 1000.0,
            },
            "/api/manager/system/memory" => new JsonObject { ["total"] = 4_000_000_000.0, ["free"] = 2_350_000_000.0 },
            "/api/manager/system/storage" => new JsonObject { ["total"] = 32_000_000_000.0, ["free"] = 21_400_000_000.0 },
            "/api/manager/updates/update" => new JsonArray(),
            "/api/manager/apps/app" => Apps(),
            "/api/manager/insights/log" => Logs(),
            _ when p.StartsWith("/api/manager/insights/log/") => Entries(p, path),
            _ => throw new HomeyApiException(404, "Not found"),
        };
        return Loc.Lang == "en" ? English(result) : result;
    }

    // The demo home in English, for screenshots of the English app
    static readonly Dictionary<string, string> EnglishNames = new()
    {
        ["Plafondlamp"] = "Ceiling light", ["Eettafel"] = "Dining table", ["Leeslamp"] = "Reading lamp", ["Rolluik"] = "Blinds",
        ["Televisie"] = "TV", ["Thermostaat"] = "Thermostat", ["Bewegingssensor"] = "Motion sensor", ["Keukenspots"] = "Kitchen spots",
        ["Koffiemachine"] = "Coffee machine", ["Vaatwasser"] = "Dishwasher", ["Koelkast"] = "Fridge", ["Achterdeur"] = "Back door", ["Bedlampje"] = "Bedside lamp",
        ["Radiatorkraan"] = "Radiator valve", ["Voordeur"] = "Front door", ["Deurbel"] = "Doorbell", ["Tuinverlichting"] = "Garden lights",
        ["Slimme meter"] = "Smart meter", ["Omvormer"] = "Inverter", ["Wasmachine"] = "Washing machine", ["Thuisbatterij"] = "Home battery", ["Rookmelder"] = "Smoke alarm",
        ["Gasten aanwezig"] = "Guests over", ["Vakantiemodus"] = "Holiday mode", ["Comforttemperatuur"] = "Comfort temperature",
        ["Laatste scène"] = "Last scene", ["Film"] = "Movie",
        ["Thuis"] = "Home", ["Begane grond"] = "Ground floor", ["Woonkamer"] = "Living room", ["Keuken"] = "Kitchen", ["Hal"] = "Hall",
        ["Meterkast"] = "Utility cupboard", ["Eerste verdieping"] = "First floor", ["Slaapkamer"] = "Bedroom", ["Zolder"] = "Attic", ["Tuin"] = "Garden",
        ["Verlichting"] = "Lighting", ["Klimaat"] = "Climate", ["Beveiliging"] = "Security",
        ["Goedemorgen"] = "Good morning", ["Slim verwarmen"] = "Smart heating", ["Filmavond"] = "Movie night", ["Eten"] = "Dinner",
        ["Welterusten"] = "Good night", ["Iedereen weg"] = "Everyone away", ["Alarm aan"] = "Arm alarm", ["Deur open melding"] = "Door open alert",
        ["Eco-stand"] = "Eco mode", ["Gezellig"] = "Cosy", ["Helder"] = "Bright", ["Koken"] = "Cooking", ["Nachtlampje"] = "Night light",
        ["**Achterdeur** is geopend"] = "**Back door** was opened", ["Vaatwasser is klaar"] = "The dishwasher is done",
        ["De batterij van **Achterdeur** is bijna leeg"] = "The battery of **Back door** is almost empty",
        ["Iedereen is vertrokken"] = "Everyone has left", ["Zonnepanelen leverden vandaag 18,2 kWh"] = "Solar panels made 18.2 kWh today",
        ["Sanne de Vries"] = "Emma Brown", ["Joris"] = "Jack",
    };

    static JsonNode? English(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList()) o[key] = English(o[key]?.DeepClone());
                return o;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) a[i] = English(a[i]?.DeepClone());
                return a;
            case JsonValue v when v.TryGetValue<string>(out var text) && EnglishNames.TryGetValue(text, out var en):
                return JsonValue.Create(en);
            default:
                return node;
        }
    }

    // Values move a little, so the demo looks alive
    void Wiggle()
    {
        void Nudge(string device, string cap, double amount)
        {
            if (devices[device]?["capabilitiesObj"]?[cap] is JsonObject c && J.Num(c, "value") is { } v)
                c["value"] = Math.Round(v + (random.NextDouble() - 0.5) * amount, 1);
        }
        Nudge("p1", "measure_power", 120);
        Nudge("solar", "measure_power", 90);
        Nudge("coffee", "measure_power", 30);
        Nudge("dishwasher", "measure_power", 40);
        Nudge("home-battery", "measure_power", 60);

        // The back door opens for a short while now and then, to show what an alarm looks like
        if (devices["contact-back"]?["capabilitiesObj"]?["alarm_contact"] is JsonObject door)
        {
            var open = DateTime.Now.Second is >= 40 and < 50 && DateTime.Now.Minute % 3 == 0;
            if (J.Bool(door, "value") != open)
            {
                door["value"] = open;
                door["lastUpdated"] = DateTime.UtcNow.ToString("o");
            }
        }
    }

    static JsonObject Folders() => new()
    {
        ["f-light"] = new JsonObject { ["id"] = "f-light", ["name"] = "Verlichting" },
        ["f-climate"] = new JsonObject { ["id"] = "f-climate", ["name"] = "Klimaat" },
        ["f-security"] = new JsonObject { ["id"] = "f-security", ["name"] = "Beveiliging" },
    };

    static JsonObject Flows(bool advanced)
    {
        var list = advanced
            ? new (string id, string name, string? folder, bool trig)[] { ("flow-morning", "Goedemorgen", "f-light", true), ("flow-heat", "Slim verwarmen", "f-climate", false) }
            : [("flow-movie", "Filmavond", "f-light", true), ("flow-dinner", "Eten", "f-light", true), ("flow-goodnight", "Welterusten", null, true),
               ("flow-away", "Iedereen weg", "f-security", true), ("flow-alarm", "Alarm aan", "f-security", true), ("flow-door", "Deur open melding", "f-security", false),
               ("flow-eco", "Eco-stand", "f-climate", true)];
        return new JsonObject(list.Select(f => KeyValuePair.Create(f.id, (JsonNode?)new JsonObject
        {
            ["id"] = f.id, ["name"] = f.name, ["folder"] = f.folder, ["enabled"] = true, ["triggerable"] = f.trig, ["broken"] = false,
        })));
    }

    static JsonObject Moods() => new()
    {
        ["m1"] = new JsonObject { ["id"] = "m1", ["name"] = "Gezellig", ["zone"] = "living" },
        ["m2"] = new JsonObject { ["id"] = "m2", ["name"] = "Helder", ["zone"] = "living" },
        ["m3"] = new JsonObject { ["id"] = "m3", ["name"] = "Koken", ["zone"] = "kitchen" },
        ["m4"] = new JsonObject { ["id"] = "m4", ["name"] = "Nachtlampje", ["zone"] = "bedroom" },
    };

    static JsonObject Notifications()
    {
        var items = new (int minutes, string text, string owner)[]
        {
            (12, "**Achterdeur** is geopend", "homey:manager:flow"), (55, "Vaatwasser is klaar", "homey:manager:flow"),
            (180, "De batterij van **Achterdeur** is bijna leeg", "homey:manager:devices"), (1500, "Iedereen is vertrokken", "homey:manager:presence"),
            (1700, "Zonnepanelen leverden vandaag 18,2 kWh", "homey:manager:energy"),
        };
        return new JsonObject(items.Select((n, i) => KeyValuePair.Create($"n{i}", (JsonNode?)new JsonObject
        {
            ["id"] = $"n{i}", ["excerpt"] = n.text, ["ownerUri"] = n.owner, ["dateCreated"] = DateTime.UtcNow.AddMinutes(-n.minutes).ToString("o"),
        })));
    }

    static JsonObject Apps()
    {
        var list = new (string id, string name, string version, string state, string? update)[]
        {
            ("com.sonos", "Sonos", "8.1.0", "running", null), ("com.philips.hue.zigbee", "Philips Hue", "4.8.2", "running", "4.9.0"),
            ("nl.p1meter", "P1 Meter", "2.3.1", "crashed", null), ("com.somfy", "Somfy", "3.0.4", "running", null),
            ("com.danalock", "Danalock", "1.6.0", "running", null),
        };
        return new JsonObject(list.Select(a => KeyValuePair.Create(a.id, (JsonNode?)new JsonObject
        {
            ["id"] = a.id, ["name"] = a.name, ["version"] = a.version, ["state"] = a.state, ["enabled"] = true, ["crashed"] = a.state == "crashed",
            ["updateAvailable"] = a.update == null ? null : new JsonObject { ["version"] = a.update },
            ["brandColor"] = a.id switch { "com.sonos" => "#000000", "com.philips.hue.zigbee" => "#0065D3", "com.somfy" => "#F7A41D", "com.danalock" => "#00B2A9", _ => null },
        })));
    }

    static JsonObject Logs()
    {
        var logs = new JsonObject();
        foreach (var (device, cap) in new[] { ("p1", "meter_power.imported"), ("p1", "meter_power.exported"), ("p1", "meter_gas"), ("solar", "meter_power"), ("home-battery", "meter_power.charged"), ("home-battery", "meter_power.discharged"), ("coffee", "meter_power"), ("dishwasher", "meter_power") })
        {
            var id = $"homey:device:{device}:{cap}";
            logs[id] = new JsonObject { ["id"] = id, ["ownerUri"] = $"homey:device:{device}", ["ownerId"] = cap };
        }
        return logs;
    }

    // A rising counter with a daily pattern
    static JsonObject Entries(string p, string full)
    {
        var cap = p.Split('/')[^2].Split(':').Last();
        var device = p.Split('/')[^2].Split(':')[2];
        var today = full.Contains("resolution=today");
        var start = today ? DateTime.Today : DateTime.Today.AddDays(-30);
        var perDay = (device, cap) switch
        {
            ("p1", "meter_power.imported") => 7.5,
            ("p1", "meter_power.exported") => 9.0,
            ("p1", "meter_gas") => 2.1,
            ("solar", _) => 14.0,
            ("home-battery", "meter_power.charged") => 4.2,
            ("home-battery", "meter_power.discharged") => 3.6,
            ("coffee", _) => 0.4,
            _ => 1.1,
        };
        var values = new JsonArray();
        var v = 1000.0;
        var rnd = new Random(cap.Length * 31 + device.Length);
        for (var t = start; t <= DateTime.Now; t = t.AddHours(1))
        {
            var shape = cap.Contains("exported") || cap.Contains(".charged") || device == "solar"
                ? Math.Max(0, Math.Sin((t.Hour - 6) / 14.0 * Math.PI)) * 2.4
                : t.Hour is >= 7 and <= 22 ? 1.4 : 0.35;
            v += perDay / 24 * shape * (0.6 + rnd.NextDouble() * 0.8);
            values.Add(new JsonObject { ["t"] = t.ToUniversalTime().ToString("o"), ["v"] = Math.Round(v, 3) });
        }
        return new JsonObject { ["values"] = values };
    }
}
