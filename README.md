<p align="center"><img src="docs/images/logo.png" alt="HomeyBar-logo op alle formaten" width="560"></p>

# HomeyBar voor Windows

Bedien je **Homey Pro** vanuit het Windows-systeemvak. Eén klik op het icoon bij de klok en je zet lampen aan, start een flow of kiest een sfeer. Het hoofdvenster laat de rest zien: apparaten per kamer, energie, batterijen, meldingen en de gezondheid van je Homey.

HomeyBar is geïnspireerd op [HomeBar](https://www.homebar.pro/) voor de Mac, maar is een eigen project zonder band met HomeBar of met Athom.

| Paneel bij de klok | Hoofdvenster |
|---|---|
| <img src="docs/images/paneel-favorieten.png" width="300" alt="Het paneel bij de klok met favoriete apparaten en flows"> | <img src="docs/images/overzicht.png" width="520" alt="Het overzicht in het hoofdvenster met weer, energie, aanwezigheid en favorieten"> |

## Wat kan het

- **Paneel bij de klok** (klik op het icoon of druk op **Ctrl + Alt + H**): favorieten, kamers, flows en sferen, met het weer, het verbruik van nu en wie er thuis is. Als lijst of als tegels.
- **Bedienen**: aan/uit, dimmen, kleurtemperatuur, thermostaat, rolluiken, speakers en sloten. Per apparaat zijn ook alle andere instellingen en metingen te zien.
- **Hoofdvenster** met een overzicht en pagina's voor apparaten (zoeken, filteren op soort, kamers inklappen), flows per map, sferen, variabelen, energie, batterijen, meldingen en systeem (apps herstarten).
- **Energie**: verbruik, zon en net op dit moment, de grootste verbruikers, totalen van vandaag en deze maand, en een grafiek van de laatste 14 dagen.
- **Thuis en onderweg**: lokaal via het IP-adres van je Homey en automatisch via de cloud van Athom als je niet thuis bent. Meerdere Homeys mogelijk.
- **Uiterlijk**: licht, donker of volgens Windows, vijf achtergronden (Papier, Aurora, Schemer, Oceaan, Glas), eigen pictogrammen per apparaat, tekstgrootte. Nederlands en Engels.
- Nieuwe Homey-meldingen als Windows-melding, starten met Windows, en een **demomodus** om alles te bekijken zonder Homey.

Hoe je alles gebruikt staat in de **[handleiding](docs/HANDLEIDING.md)**.

## Installeren

Er is nog geen kant-en-klare download. Bouw HomeyBar zelf; dat duurt een minuut.

1. Installeer de [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows 10 of 11).
2. Haal de code op en bouw:
   ```
   git clone https://github.com/WNijhof/homeybar-windows.git
   cd homeybar-windows\HomeyBar
   dotnet publish -c Release -o ..\publish
   ```
3. Start `publish\HomeyBar.exe`. Bij de eerste start opent Instellingen; vul daar het IP-adres van je Homey en een API-key in. De [handleiding](docs/HANDLEIDING.md#een-api-key-maken) legt uit hoe je die maakt.

De map `publish` kun je naar elke plek of pc kopiëren. Op een andere pc is alleen de [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) nodig, niet de hele SDK.

Eerst rondkijken zonder Homey? Start `HomeyBar.exe --demo`.

## Privacy

HomeyBar praat alleen met je eigen Homey: rechtstreeks in je netwerk, of via de cloud-doorgang van Athom (`<homey-id>.connect.athom.com`) als je niet thuis bent. Er gaat niets naar andere partijen. Je instellingen staan in `%APPDATA%\HomeyBar\settings.json`; de API-key is daarin versleuteld met je Windows-account.

## Ontwikkelen

```
cd HomeyBar
dotnet run                                    # starten
dotnet run -- --demo                          # met het voorbeeldhuis
dotnet run -- --snapshot ..\.shots\x [--dark]  # alle schermen van de demo als PNG
```

| Map | Inhoud |
|---|---|
| `HomeyBar/Core` | Verbinding met Homey (`HomeyClient`), ophalen en bijhouden van gegevens (`HomeyStore`), modellen, thema, systeemvak, vertalingen, demohuis |
| `HomeyBar/Views` | Het paneel (`FlyoutWindow`), het hoofdvenster met de pagina's, stijlen en sjablonen |
| `scripts` | `make-icon.ps1` maakt het logo, `strings.js` toont teksten zonder Engelse vertaling, `screenshot.ps1` maakt een schermafbeelding van een venster |

Teksten staan in het Nederlands in de code; `HomeyBar/Core/Loc.En.cs` vertaalt ze naar het Engels. HomeyBar gebruikt de Web API van Homey Pro (`/api/manager/...`) met een API-key en haalt elke paar seconden nieuwe gegevens op terwijl een venster open is.
