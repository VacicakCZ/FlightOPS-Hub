================================================================
                         FLIGHTOPS HUB
            Advanced MSFS Application Manager (v1.5)
================================================================

Thank you for downloading FlightOps Hub!

1. WHAT IS IT?
FlightOps Hub is a smart, portable launcher designed exclusively
for Microsoft Flight Simulator (2020 & 2024). It allows you to
organize your pre-flight addons (like vPilot, Volanta, REX Atmos),
delay or smart-time their startup, save different flight profiles,
manage your internal MSFS AutoStart (exe.xml) addons, and now also
your Community folder itself: Scenery packages and Aircraft/Liveries
can be enabled or disabled with one click. It also features Navigraph
AIRAC validation, smart window memory, and smart process detection
to prevent double-launching MSFS.

2. WHAT'S NEW IN v1.5
- NEW "Scenery" tab: lists every scenery package from your Community
  folder, automatically grouped by continent/country (detected from
  the airport ICAO code). Toggle packages on/off with a click -
  disabling simply moves the folder out of Community (and back again
  when re-enabled), so nothing is deleted and exe.xml is untouched.
- NEW "Aircraft (Beta)" tab: same on/off management for aircraft and
  livery packages, grouped by developer with liveries nested under
  their matching aircraft. Marked as Beta - please double-check the
  result after toggling.
- NEW Smart Launch (SimConnect): addons can now wait for the actual
  flight to be loaded in the sim (not just a fixed delay) before
  launching, using a live SimConnect connection. Status and a Cancel
  option are shown via a tray icon.
- NEW background Watch mode: FlightOps Hub can stay minimized in the
  tray after launch, detect when MSFS is closed, and automatically
  shut down every addon it started - with a "Close everything now"
  option from the tray icon.
- NEW languages: the interface is now available in English, Czech,
  German, Spanish, and Chinese.
- Various stability and UI improvements.

3. INSTALLATION
FlightOps Hub is 100% portable. There is no installation required.
- Simply extract the downloaded .zip archive to any folder on your PC.
- Double-click "FlightOps_Hub.exe" to start the application.

4. HOW TO USE
- Go to the "Settings" tab.
- Add your addons by providing a Name, Path to the .exe file, and
  a Delay (in seconds) if needed (e.g., 150 seconds for REX Atmos),
  or let Smart Launch wait for the flight to actually load instead.
- You can now check "Run as Administrator" for specific apps that need it.
- Go to the "AutoStart (exe.xml)" tab to safely enable or disable
  internal simulator addons. Click ✏️ to rename them for easier
  identification (this safely preserves original XML tags).
- Go to the "Scenery" tab to browse and toggle installed scenery
  packages, organized by continent/country.
- Go to the "Aircraft (Beta)" tab to browse and toggle installed
  aircraft and liveries, grouped by developer.
- Manage your profiles using the dedicated buttons: ➕ (Add new),
  🔄 (Update current), and 🗑️ (Delete).
- Go to the "Flight" tab, select your profile, and click "Launch".

5. IMPORTANT UAC NOTE (ADMIN RIGHTS)
In older versions, you had to run the entire Hub as an administrator
if a specific app required it. Since v1.2, this is no longer necessary!
Simply check the "Run as Administrator (UAC)" box next to the
specific app in the Settings tab, and FlightOps Hub will handle the
elevated privileges for you automatically.

6. TROUBLESHOOTING
- Navigraph AIRAC not showing? Go to Settings and make sure your
  MSFS "Community" folder path is set correctly.
- Messed up your exe.xml outside of the app? Don't worry, the Hub
  automatically creates a safe "exe_FlightOpsHub_backup.xml" file
  before making any changes to your files.
- MSFS didn't launch? If FlightOps Hub detects that MSFS is already
  running in the background, it will intentionally skip launching
  the simulator and only launch your selected addons.
- Scenery/Aircraft tab empty? Set the Community folder path in
  Settings first.
- Smart Launch never fires? It requires the SimConnect SDK to be
  available; if it's missing, addons simply won't wait for it.

================================================================
                         FLIGHTOPS HUB
          Pokročilý správce doplňků pro MSFS (v1.5)
================================================================

Díky za stažení FlightOps Hubu!

1. O CO JDE?
FlightOps Hub je chytrý, přenosný spouštěč vytvořený exkluzivně
pro Microsoft Flight Simulator (2020 a 2024). Umožňuje organizovat
předletové doplňky (vPilot, Volanta, REX Atmos), nastavit jejich
spuštění na časovou prodlevu nebo chytře podle stavu letu, ukládat
profily letů, spravovat interní doplňky simulátoru (přes soubor
exe.xml) a nově i přímo obsah složky Community: scenérie a letadla/
liverky lze zapínat a vypínat jedním kliknutím. Obsahuje také
kontrolu AIRAC cyklů, paměť pozice okna a chytrou detekci, která
brání dvojitému spuštění MSFS.

2. CO JE NOVÉHO V 1.5
- NOVÁ záložka "Scenérie": zobrazí všechny scenérie z tvé Community
  složky, automaticky roztříděné podle kontinentu/státu (podle ICAO
  kódu letiště). Balíček zapneš/vypneš jedním kliknutím - vypnutí ho
  jen přesune mimo Community (a zapnutí zpátky), takže se nic
  nemaže a exe.xml zůstává nedotčený.
- NOVÁ záložka "Letadla (Beta)": stejná správa zapnuto/vypnuto pro
  letadla a liverky, řazeno podle vývojáře, liverky zanořené pod
  odpovídajícím letadlem. Označeno jako Beta - po přepnutí si prosím
  zkontroluj výsledek.
- NOVÝ Smart Launch (SimConnect): doplňky teď mohou počkat, až se
  let v simulátoru skutečně načte (ne jen na pevnou prodlevu), díky
  živému SimConnect připojení. Stav a možnost zrušení se zobrazují
  přes ikonu v systémové liště.
- NOVÝ režim hlídání na pozadí: FlightOps Hub může po spuštění zůstat
  minimalizovaný v systémové liště, poznat, kdy se MSFS zavře, a
  automaticky ukončit všechny doplňky, které sám spustil - s
  možností "Ukončit vše nyní" přímo z ikony v liště.
- NOVÉ jazyky: rozhraní je teď dostupné v angličtině, češtině,
  němčině, španělštině a čínštině.
- Různá vylepšení stability a uživatelského rozhraní.

3. INSTALACE
FlightOps Hub je přenosný. Nevyžaduje žádnou instalaci.
- Rozbal stažený .zip archiv do jakékoli složky v PC.
- Spusť program dvojklikem na "FlightOps_Hub.exe".

4. JAK TO POUŽÍVAT
- Přejdi na záložku "Nastavení".
- Přidej doplňky vyplněním názvu, cesty k .exe souboru a zpoždění
  v sekundách (např. 150 s pro REX Atmos), nebo nech Smart Launch
  počkat, až se let skutečně načte.
- U aplikací, které to vyžadují, zaškrtni "Spustit jako správce".
- Na záložce "AutoStart (exe.xml)" můžeš bezpečně zapínat a vypínat
  interní doplňky. Pomocí ikony ✏️ si je můžeš libovolně přejmenovat
  (originální XML soubor zůstane nepoškozen).
- Na záložce "Scenérie" procházej a přepínej nainstalované scenérie,
  roztříděné podle kontinentu/státu.
- Na záložce "Letadla (Beta)" procházej a přepínej nainstalovaná
  letadla a liverky, roztříděné podle vývojáře.
- Profily spravuješ tlačítky: ➕ (Nový), 🔄 (Aktualizovat)
  a 🗑️ (Smazat).
- Na záložce Let vyber profil a klikni na "Spustit".

5. DŮLEŽITÉ UPOZORNĚNÍ K UAC (PRÁVA SPRÁVCE)
Ve starších verzích se musel pouštět celý Hub jako správce, pokud to
vyžadoval byť jen jeden doplněk. Od verze 1.2 už to není nutné!
Nyní stačí v Nastavení zaškrtnout volbu "Spustit jako správce (UAC)"
jen u daného doplňku a Hub se o zvýšená práva postará automaticky.

6. ŘEŠENÍ PROBLÉMŮ
- Nezobrazuje se AIRAC? Běž do Nastavení a zkontroluj, že máš
  správně vybranou cestu ke složce Community.
- Bojíš se úprav exe.xml? Nemusíš. Hub před jakýmkoliv zásahem
  automaticky vytvoří bezpečnostní zálohu s názvem
  "exe_FlightOpsHub_backup.xml" přímo ve složce simulátoru.
- Nespustil se MSFS? Pokud program detekuje, že hra už běží na
  pozadí, záměrně její spuštění přeskočí a odpálí jen doplňky.
- Prázdná záložka Scenérie/Letadla? Nejdřív v Nastavení vyplň
  cestu ke složce Community.
- Smart Launch se nikdy nespustí? Vyžaduje dostupné SimConnect SDK;
  pokud chybí, doplňky na něj prostě nebudou čekat.

Happy flying! / Letu zdar!
