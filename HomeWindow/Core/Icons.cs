using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace HomeWindow.Core;

// Glyphs from Segoe Fluent Icons (Windows 11), with Segoe MDL2 Assets as fallback
public static class Icons
{
    public const string Light = "", Socket = "", Thermostat = "", Heater = "", Blinds = "",
        Speaker = "", Tv = "", Lock = "", Unlock = "", Bell = "", Sensor = "",
        Chip = "", Sun = "", Moon = "", Cloud = "", Home = "", Power = "",
        Folder = "", Flow = "", Play = "", Pause = "", Stop = "", Prev = "",
        Next = "", Up = "", Down = "", Star = "", StarFill = "", Search = "",
        Settings = "", Refresh = "", OpenWindow = "", Close = "", More = "",
        Warning = "", Battery = "", BatteryLow = "", Energy = "", Shield = "",
        Drop = "", Car = "", Cup = "", Phone = "", People = "", Person = "",
        Variable = "", History = "", Apps = "", Health = "", Grid = "",
        List = "", Mood = "", Leaf = "", Game = "", Check = "", Delete = "",
        Filter = "", Back = "", Info = "", Plus = "", Minus = "", Volume = "",
        Fan = "", Camera = "", Door = "", Cube = "", Plug = "";

    public static string ForClass(string? cls, Func<string, bool> has) => cls switch
    {
        "light" => Light,
        "socket" => Socket,
        "thermostat" or "airconditioning" or "airtreatment" => Thermostat,
        "heater" or "heatpump" or "boiler" => Heater,
        "windowcoverings" or "blinds" or "curtain" or "sunshade" or "awning" or "garagedoor" => Blinds,
        "speaker" or "amplifier" => Speaker,
        "tv" or "settopbox" or "mediaplayer" => Tv,
        "lock" => Lock,
        "doorbell" => Bell,
        "sensor" => has("measure_temperature") && !has("alarm_motion") && !has("alarm_contact") ? Thermostat : Sensor,
        "solarpanel" => Sun,
        "battery" => Battery,
        "homealarm" => Shield,
        "evcharger" or "car" => Car,
        "kettle" or "coffeemachine" => Cup,
        "waterheater" or "waterpump" or "sprinkler" => Drop,
        "fan" or "vacuumcleaner" => Fan,
        "remote" or "button" => Phone,
        "camera" => Camera,
        "gameconsole" => Game,
        _ => has("onoff") ? Power : Chip,
    };

    public static string ForWeather(string? state, bool night)
    {
        var s = (state ?? "").ToLowerInvariant();
        if (s.Length == 0 || s.Contains("clear") || s.Contains("sun")) return night ? Moon : Sun;
        return Cloud;
    }

    // Icons a user can pick for a device
    public static readonly string[] Choices =
    [
        Light, Power, Socket, Thermostat, Heater, Fan, Blinds, Door, Lock, Bell, Sensor, Camera, Shield,
        Speaker, Tv, Game, Phone, Sun, Moon, Drop, Leaf, Car, Cup, Battery, Home, Chip, Cube, Star, Mood,
    ];

    // PNG, JPEG, GIF or ICO, by their first bytes
    static bool LooksLikeBitmap(byte[] d) =>
        d.Length > 4 && ((d[0] == 0x89 && d[1] == 0x50) || (d[0] == 0xFF && d[1] == 0xD8) || (d[0] == 0x47 && d[1] == 0x49) || (d[0] == 0 && d[1] == 0 && d[2] == 1 && d[3] == 0));

    // An icon file from Homey (usually SVG) as an image; null when it cannot be read
    public static ImageSource? FromFile(byte[] data)
    {
        try
        {
            var head = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 512));
            if (head.Contains("<svg", StringComparison.OrdinalIgnoreCase) || head.TrimStart().StartsWith("<?xml"))
            {
                using var reader = new FileSvgReader(new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true });
                if (reader.Read(new MemoryStream(data)) is not { } drawing) return null;
                var image = new DrawingImage(drawing);
                image.Freeze();
                return image;
            }
            if (!LooksLikeBitmap(data)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e)
        {
            Log.Error("App icon", e);
            return null;
        }
    }
}
