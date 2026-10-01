# HomeyBar voor Windows

Bedien je Homey Pro vanuit het Windows-systeemvak, zoals HomeBar dat op de Mac doet.

- **Paneel bij de klok** (klik op het icoon of druk op Ctrl+Alt+H): favorieten, kamers, flows en sferen, met weer, energie nu en wie er thuis is. Lijst of raster.
- **Hoofdvenster**: overzicht, apparaten per zone (zoeken, filteren op soort, inklappen), alle bediening per apparaat, flows per map, sferen, variabelen, energie (nu, vandaag, maand, 14 dagen, per apparaat), batterijen, meldingen, systeem en apps (herstarten).
- **Verbinding**: lokaal via IP-adres + API-key, en automatisch via de cloud (`https://<homey-id>.connect.athom.com`) als je niet thuis bent. Meerdere Homeys.
- **Uiterlijk**: licht/donker/systeem, vijf achtergronden (Papier, Aurora, Schemer, Oceaan, Glas), dekking, schaduwen, tekstgrootte. Nederlands en Engels.
- Windows-meldingen voor nieuwe Homey-meldingen, starten met Windows. De API-key wordt versleuteld opgeslagen (Windows DPAPI) in `%APPDATA%\HomeyBar\settings.json`.

## Bouwen en starten

Vereist: .NET 10 SDK.

```
cd HomeyBar
dotnet run                 # normaal
dotnet run -- --demo       # voorbeeldhuis, zonder Homey
dotnet run -- --snapshot ..\.shots\x [--dark]   # rendert alle schermen van de demo naar PNG
```

Uitrollen: `dotnet publish -c Release -o ..\publish` geeft een map van ongeveer 600 kB (vereist de .NET 10 Desktop Runtime). Eén los exe-bestand (`-r win-x64 -p:PublishSingleFile=true`) vraagt een NuGet-bron met de runtime-pakketten, zoals nuget.org.

## Ontwikkelen

- `scripts/strings.js` toont Nederlandse teksten zonder Engelse vertaling in `Core/Loc.En.cs`.
- `scripts/make-icon.ps1` maakt het app-icoon opnieuw.
