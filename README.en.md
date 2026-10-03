<p align="center"><img src="docs/images/logo.png" alt="HomeWindow logo in all sizes" width="560"></p>

# HomeWindow – for Homey Pro

*[Nederlands](README.md)* · **English** · **[Website and download](https://wnijhof.github.io/homewindow/)**

Control your **Homey Pro** from the Windows taskbar. One click on the icon by the clock and you switch lights, start a flow or pick a mood. The main window shows the rest: devices per room, energy, batteries, notifications and the health of your Homey.

> **Alpha.** HomeWindow is new and under active development. It works well on the Homeys it was tested with, but expect rough edges and changes. Please report what you run into in the [issues](https://github.com/WNijhof/homewindow/issues).

HomeWindow was inspired by [HomeBar](https://www.homebar.pro/) for the Mac, but it is a separate project with no connection to HomeBar or to Athom. Homey is a trademark of Athom.

| Panel by the clock | Main window |
|---|---|
| <img src="docs/images/en/panel-favourites.png" width="300" alt="The panel by the clock with favourite devices and flows"> | <img src="docs/images/en/overview.png" width="520" alt="The overview in the main window with weather, energy, presence and favourites"> |

## Which Homey

| Homey | Works |
|---|---|
| Homey Pro (2023) and Homey Pro mini | Yes |
| Homey Self-Hosted Server | Probably (same API, not tested) |
| Homey (with Homey Bridge, no Pro) | No: it has no API keys |
| Homey Pro from before 2023 | No: an older API without API keys |

HomeWindow uses the Homey Web API with an API key, which you create in my.homey.app.

## What it does

- **Panel by the clock** (click the icon or press **Ctrl + Alt + H**): favourites, rooms, flows and moods, with the weather, energy right now and who is home. As a list or as tiles.
- **Tiles like the Homey app**: a round quick-action button, a coloured state (on, power, music, heating, locked) and up to two measurements you pick per device. Three tile sizes.
- **Control**: on/off, dimming, colour temperature, thermostats, blinds, speakers and locks. Every other setting and measurement of a device is one click away.
- **Main window** with an overview and pages for devices (search, filter by type, collapse rooms), flows per folder, moods, Logic variables, energy, batteries, notifications and system (restart apps).
- **Energy**: use, solar, grid and home battery right now – also on the taskbar next to the clock, with returned power in green – the biggest users, totals for today and this month, and a chart of the last 14 days.
- **At home and away**: local through your Homey's IP address, and automatically through Athom's cloud when you are out. More than one Homey is fine.
- **Looks**: light, dark or following Windows; the *Dashboard* style of the Homey energy dashboard or one of five other backgrounds; your own icons per device; text size. Dutch and English.
- Homey notifications and alarms (smoke, water, and if you like motion and doors) as Windows notifications, start with Windows, silent automatic updates, and a **demo mode** to look around without a Homey.

The full manual is in Dutch: **[HANDLEIDING](docs/HANDLEIDING.md)** (a browser translation works well).

## Install

1. Download `HomeWindow-Setup-<version>.exe` from the latest [release](https://github.com/WNijhof/homewindow/releases/latest).
2. Run it. Windows may warn that the publisher is unknown (the installer is not code-signed): choose **More info → Run anyway**.
3. HomeWindow installs for your Windows account, without admin rights, and adds itself to the Start menu. .NET is included.
4. On first start the settings open. Fill in your Homey's IP address and an API key:
   - Go to [my.homey.app](https://my.homey.app), pick your Homey and open **Settings → API Keys → New API Key**.
   - Give it the rights HomeWindow needs: devices (view and control), zones, flows (view and start), moods, Logic, notifications, users, energy, Insights, system, apps and geolocation (for the weather).
   - The IP address is in the Homey app under **Settings → General**.

HomeWindow then looks for new versions by itself and installs them silently (you can turn that off under Settings → Updates). Uninstall through **Windows Settings → Apps**.

Want to look around first? Run `HomeWindow.exe --demo`.

## Privacy

HomeWindow only talks to your own Homey: directly on your network, or through Athom's cloud relay (`<homey-id>.connect.athom.com`) when you are away. It also asks GitHub whether there is a new version. Nothing about your Homey goes anywhere else. Your settings are in `%APPDATA%\HomeWindow\settings.json`; the API key in it is encrypted with your Windows account.

## License

HomeWindow is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE). The source is open: you may use, change and share HomeWindow for free, but not for commercial purposes. Personal, hobby, study and non-profit use is all fine; making money with HomeWindow or a changed version needs permission. Pass on the license and its `Required Notice` line when you share it.

Versions up to and including 0.3.0 were released under the GNU GPL v3.0; anyone who has those versions keeps those rights for them.
