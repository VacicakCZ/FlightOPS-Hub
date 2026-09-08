"""
Cistě datová/filesystémová logika pro záložku Scenérie.

Žádná závislost na Tk - dá se testovat samostatně skriptem, co jen čte
(nebo bezpečně přesouvá) reálnou Community složku.

Fyzická poloha složky addonu JE jeho stav (zapnuto = v Community, vypnuto =
v sesterské "disabled" složce vedle Community) - žádný oddělený stavový
soubor, který by se mohl rozjet s realitou.
"""

import os
import re
import json
import shutil
import sys

CONTINENT_ORDER = ["europe", "north_america", "south_america", "asia", "africa", "oceania", "other"]

# --- ICAO prefixy -> stát (2písmenné mají přednost před 1písmennými) ---
TWO_LETTER_ICAO = {
    # Evropa
    "EB": "Belgium", "ED": "Germany", "ET": "Germany", "EE": "Estonia", "EF": "Finland",
    "EG": "United Kingdom", "EH": "Netherlands", "EI": "Ireland", "EK": "Denmark",
    "EL": "Luxembourg", "EN": "Norway", "EP": "Poland", "ES": "Sweden", "EV": "Latvia",
    "EY": "Lithuania",
    "LA": "Albania", "LB": "Bulgaria", "LC": "Cyprus", "LD": "Croatia", "LE": "Spain",
    "LF": "France", "LG": "Greece", "LH": "Hungary", "LI": "Italy", "LJ": "Slovenia",
    "LK": "Czech Republic", "LL": "Israel", "LM": "Malta", "LN": "Monaco", "LO": "Austria",
    "LP": "Portugal", "LQ": "Bosnia and Herzegovina", "LR": "Romania", "LS": "Switzerland",
    "LT": "Turkey", "LU": "Moldova", "LW": "North Macedonia", "LX": "Gibraltar",
    "LY": "Serbia", "LZ": "Slovakia",
    "UK": "Ukraine", "UM": "Belarus",
    "BI": "Iceland", "BG": "Greenland",
    "GC": "Spain",

    # Rusko / SNS / Kavkaz / Stredni Asie
    "UA": "Kazakhstan", "UB": "Azerbaijan", "UD": "Armenia", "UG": "Georgia",
    "UT": "Uzbekistan",
    "UH": "Russia", "UI": "Russia", "UL": "Russia", "UN": "Russia", "UO": "Russia",
    "UR": "Russia", "US": "Russia", "UU": "Russia", "UW": "Russia",

    # Blizky vychod
    "OA": "Afghanistan", "OB": "Bahrain", "OE": "Saudi Arabia", "OI": "Iran",
    "OJ": "Jordan", "OK": "Kuwait", "OL": "Lebanon", "OM": "United Arab Emirates",
    "OO": "Oman", "OP": "Pakistan", "OR": "Iraq", "OS": "Syria", "OT": "Qatar",
    "OY": "Yemen",

    # Jizni / jihovychodni Asie
    "VA": "India", "VE": "India", "VI": "India", "VO": "India", "VC": "Sri Lanka",
    "VD": "Cambodia", "VG": "Bangladesh", "VH": "Hong Kong", "VL": "Laos",
    "VM": "Macau", "VN": "Nepal", "VQ": "Bhutan", "VR": "Maldives", "VT": "Thailand",
    "VV": "Vietnam", "VY": "Myanmar",
    "WA": "Indonesia", "WI": "Indonesia", "WQ": "Indonesia", "WR": "Indonesia",
    "WB": "Malaysia", "WM": "Malaysia", "WP": "East Timor", "WS": "Singapore",

    # Vychodni Asie
    "RC": "Taiwan", "RJ": "Japan", "RO": "Japan", "RK": "South Korea", "RP": "Philippines",
    "ZK": "North Korea", "ZM": "Mongolia",

    # Afrika
    "DA": "Algeria", "DB": "Benin", "DF": "Burkina Faso", "DG": "Ghana",
    "DI": "Ivory Coast", "DN": "Nigeria", "DR": "Niger", "DT": "Tunisia", "DX": "Togo",
    "GA": "Mali", "GB": "Gambia", "GF": "Sierra Leone", "GG": "Guinea-Bissau",
    "GL": "Liberia", "GM": "Morocco", "GO": "Senegal", "GQ": "Mauritania",
    "GU": "Guinea", "GV": "Cape Verde",
    "HA": "Ethiopia", "HB": "Burundi", "HC": "Somalia", "HD": "Djibouti", "HE": "Egypt",
    "HH": "Eritrea", "HK": "Kenya", "HL": "Libya", "HR": "Rwanda", "HS": "Sudan",
    "HT": "Tanzania", "HU": "Uganda",
    "FA": "South Africa", "FB": "Botswana", "FC": "Republic of the Congo",
    "FD": "Eswatini", "FE": "Central African Republic", "FG": "Equatorial Guinea",
    "FH": "Saint Helena", "FI": "Mauritius", "FK": "Cameroon", "FL": "Zambia",
    "FM": "Madagascar", "FN": "Angola", "FO": "Gabon", "FP": "Sao Tome and Principe",
    "FQ": "Mozambique", "FS": "Seychelles", "FT": "Chad", "FV": "Zimbabwe",
    "FW": "Malawi", "FX": "Lesotho", "FY": "Namibia", "FZ": "DR Congo",

    # Oceanie / Pacifik
    "NF": "Fiji", "NG": "Kiribati", "NI": "Niue", "NL": "Wallis and Futuna",
    "NS": "Samoa", "NT": "French Polynesia", "NV": "Vanuatu", "NW": "New Caledonia",
    "NZ": "New Zealand",
    "AG": "Solomon Islands", "AN": "Nauru", "AY": "Papua New Guinea",

    # Severni Amerika / Karibik / Stredni Amerika
    "MM": "Mexico", "MG": "Guatemala", "MH": "Honduras", "MN": "Nicaragua",
    "MP": "Panama", "MR": "Costa Rica", "MS": "El Salvador", "MZ": "Belize",
    "MB": "Turks and Caicos", "MD": "Dominican Republic", "MK": "Jamaica",
    "MT": "Haiti", "MU": "Cuba", "MW": "Cayman Islands", "MY": "Bahamas",
    "TA": "Antigua and Barbuda", "TB": "Barbados", "TD": "Dominica",
    "TF": "French Antilles", "TG": "Grenada", "TI": "US Virgin Islands",
    "TJ": "Puerto Rico", "TK": "Saint Kitts and Nevis", "TL": "Saint Lucia",
    "TN": "Netherlands Antilles", "TQ": "Anguilla", "TR": "Montserrat",
    "TT": "Trinidad and Tobago", "TU": "British Virgin Islands", "TV": "Saint Vincent",
    "TX": "Bermuda",

    # Jizni Amerika
    "SA": "Argentina", "SB": "Brazil", "SC": "Chile", "SD": "Brazil", "SE": "Ecuador",
    "SF": "Chile", "SG": "Paraguay", "SI": "Brazil", "SJ": "Brazil", "SK": "Colombia",
    "SL": "Bolivia", "SM": "Suriname", "SN": "Brazil", "SO": "French Guiana",
    "SP": "Peru", "SS": "Brazil", "SU": "Uruguay", "SV": "Venezuela", "SW": "Brazil",
    "SY": "Guyana",

    # Aljaska / pacificka uzemi USA
    "PA": "United States", "PF": "United States", "PO": "United States",
    "PH": "United States", "PG": "Guam", "PK": "Marshall Islands", "PT": "Micronesia",
    "PW": "Palau", "PC": "Kiribati",
}

ONE_LETTER_ICAO = {
    "K": "United States",
    "C": "Canada",
    "Y": "Australia",
    "Z": "China",
}

# Doplnkove klicova slova pro balicky bez ICAO kodu v nazvu (best-effort)
KEYWORD_TO_COUNTRY = {
    "france": "France", "germany": "Germany", "japan": "Japan",
    "unitedstates": "United States", "usa": "United States",
    "britain": "United Kingdom", "england": "United Kingdom", "scotland": "United Kingdom",
    "italy": "Italy", "spain": "Spain", "australia": "Australia", "canada": "Canada",
    "brazil": "Brazil", "china": "China", "india": "India", "mexico": "Mexico",
    "greece": "Greece", "netherlands": "Netherlands", "switzerland": "Switzerland",
    "austria": "Austria", "portugal": "Portugal", "poland": "Poland",
    "sweden": "Sweden", "norway": "Norway", "finland": "Finland", "denmark": "Denmark",
    "ireland": "Ireland", "iceland": "Iceland", "turkey": "Turkey", "russia": "Russia",
    "indonesia": "Indonesia", "thailand": "Thailand", "vietnam": "Vietnam",
    "philippines": "Philippines", "taiwan": "Taiwan", "newzealand": "New Zealand",
}

COUNTRY_TO_CONTINENT = {}


def _register(countries, continent):
    for c in countries:
        COUNTRY_TO_CONTINENT[c] = continent


_register([
    "Germany", "United Kingdom", "Netherlands", "Ireland", "Denmark", "Luxembourg",
    "Norway", "Poland", "Sweden", "Latvia", "Lithuania", "Belgium", "Estonia",
    "Finland", "Albania", "Bulgaria", "Cyprus", "Croatia", "Spain", "France",
    "Greece", "Hungary", "Italy", "Slovenia", "Czech Republic", "Malta", "Monaco",
    "Austria", "Portugal", "Bosnia and Herzegovina", "Romania", "Switzerland",
    "Moldova", "North Macedonia", "Gibraltar", "Serbia", "Slovakia", "Ukraine",
    "Belarus", "Iceland",
], "europe")

_register([
    "Kazakhstan", "Azerbaijan", "Armenia", "Georgia", "Uzbekistan", "Russia",
    "Turkey", "Israel", "Afghanistan", "Bahrain", "Saudi Arabia", "Iran", "Jordan",
    "Kuwait", "Lebanon", "United Arab Emirates", "Oman", "Pakistan", "Iraq", "Syria",
    "Qatar", "Yemen", "India", "Sri Lanka", "Cambodia", "Bangladesh", "Hong Kong",
    "Laos", "Macau", "Nepal", "Bhutan", "Maldives", "Thailand", "Vietnam", "Myanmar",
    "Indonesia", "Malaysia", "East Timor", "Singapore", "Taiwan", "Japan",
    "South Korea", "Philippines", "North Korea", "Mongolia", "China",
], "asia")

_register([
    "Algeria", "Benin", "Burkina Faso", "Ghana", "Ivory Coast", "Nigeria", "Niger",
    "Tunisia", "Togo", "Mali", "Gambia", "Sierra Leone", "Guinea-Bissau", "Liberia",
    "Morocco", "Senegal", "Mauritania", "Guinea", "Cape Verde", "Ethiopia",
    "Somalia", "Djibouti", "Egypt", "Eritrea", "Kenya", "Libya", "Rwanda", "Sudan",
    "Tanzania", "Uganda", "Burundi", "South Africa", "Botswana",
    "Republic of the Congo", "Eswatini", "Central African Republic",
    "Equatorial Guinea", "Saint Helena", "Mauritius", "Cameroon", "Zambia",
    "Madagascar", "Angola", "Gabon", "Sao Tome and Principe", "Mozambique",
    "Seychelles", "Chad", "Zimbabwe", "Malawi", "Lesotho", "Namibia", "DR Congo",
], "africa")

_register([
    "Fiji", "Kiribati", "Niue", "Wallis and Futuna", "Samoa", "French Polynesia",
    "Vanuatu", "New Caledonia", "New Zealand", "Solomon Islands", "Nauru",
    "Papua New Guinea", "Australia", "Guam", "Marshall Islands", "Micronesia",
    "Palau",
], "oceania")

_register([
    "Mexico", "Guatemala", "Honduras", "Nicaragua", "Panama", "Costa Rica",
    "El Salvador", "Belize", "Turks and Caicos", "Dominican Republic", "Jamaica",
    "Haiti", "Cuba", "Cayman Islands", "Bahamas", "Antigua and Barbuda", "Barbados",
    "Dominica", "French Antilles", "Grenada", "US Virgin Islands", "Puerto Rico",
    "Saint Kitts and Nevis", "Saint Lucia", "Netherlands Antilles", "Anguilla",
    "Montserrat", "Trinidad and Tobago", "British Virgin Islands", "Saint Vincent",
    "Bermuda", "United States", "Canada", "Greenland",
], "north_america")

_register([
    "Argentina", "Brazil", "Chile", "Ecuador", "Paraguay", "Colombia", "Bolivia",
    "Suriname", "French Guiana", "Peru", "Uruguay", "Venezuela", "Guyana",
], "south_america")


_ICAO_TOKEN_RE = re.compile(r'(?<![A-Za-z0-9])([A-Za-z]{4})(?![A-Za-z0-9])')
_KEYWORD_RE_CACHE = {
    kw: re.compile(r'(?<![a-z])' + re.escape(kw) + r'(?![a-z])')
    for kw in KEYWORD_TO_COUNTRY
}


def _lookup_icao(token):
    country = TWO_LETTER_ICAO.get(token[:2].upper())
    if country:
        return country
    return ONE_LETTER_ICAO.get(token[:1].upper())


def _icao_tokens_from_folder(folder_name):
    if not folder_name:
        return []
    # Prvni segment nazvu (pred prvni pomlckou/podtrzitkem) je u komunitnich
    # balicku prakticne vzdy zkratka vyvojare/studia (napr. "orbx-airport-...",
    # "fsdg-airport-..."), ne kod mista - jinak by se 4pismenna znacka jako
    # "ORBX" nebo "FSDG" mohla omylem zamenit za ICAO kod (prefix "OR" =
    # Irak, "FS" = Seychely). Tenhle segment proto ze skenovani vynechame.
    rest = re.split(r'[-_]', folder_name, maxsplit=1)
    remainder = rest[1] if len(rest) > 1 else ""
    return [m.upper() for m in _ICAO_TOKEN_RE.findall(remainder)]


def _icao_tokens_from_title(title):
    # V titulcich akceptujeme jen tokeny psane VELKYMI pismeny v puvodnim
    # textu - skutecne ICAO kody se v titulcich tak pisou, zatimco bezne
    # ctyrpismenne anglicke slovo v "Title Case" by jinak mohlo zplodit
    # falesnou shodu.
    return [m for m in _ICAO_TOKEN_RE.findall(title or "") if m.isupper()]


def find_icao_code(folder_name, title):
    for token in _icao_tokens_from_folder(folder_name):
        if _lookup_icao(token):
            return token
    for token in _icao_tokens_from_title(title):
        if _lookup_icao(token):
            return token
    return None


def categorize_scenery(folder_name, title):
    for token in _icao_tokens_from_folder(folder_name) + _icao_tokens_from_title(title):
        country = _lookup_icao(token)
        if country:
            return country, COUNTRY_TO_CONTINENT.get(country, "other")

    combined = f"{folder_name} {title}".lower().replace("-", "").replace("_", "").replace(" ", "")
    combined_spaced = f"{folder_name} {title}".lower()
    for keyword, country in KEYWORD_TO_COUNTRY.items():
        pattern = _KEYWORD_RE_CACHE[keyword]
        if pattern.search(combined_spaced) or keyword in combined:
            return country, COUNTRY_TO_CONTINENT.get(country, "other")

    return None, "other"


def _continent_sort_key(continent):
    try:
        return CONTINENT_ORDER.index(continent)
    except ValueError:
        return len(CONTINENT_ORDER)


def get_default_disabled_path(community_path):
    """Vychozi sesterska slozka vedle Community, kam se presouvaji vypnute
    balicky, pokud si uzivatel nezvolil vlastni umisteni."""
    parent = os.path.dirname(os.path.normpath(community_path))
    return os.path.join(parent, "Community_disabled_by_FlightOpsHub")


def resolve_disabled_locations(community_path, custom_path=None):
    """Vrati serazeny seznam znamych 'disabled' slozek. Prvni je vzdy
    primarni (aktualne nastavena - bud custom_path, nebo vychozi) a tam se
    presouvaji NOVE vypinane balicky; vytvori se, pokud jeste neexistuje.

    Pokud se od primarni lisi i vychozi slozka (uzivatel drive pouzival
    automatiku a ted prepnul na vlastni cestu) a existuje, prida se na
    konec seznamu - jen ke kontrole, aby balicky vypnute pred zmenou
    nastaveni appce nezmizely z dohledu. Nic noveho se do ni uz nezapisuje."""
    default_path = get_default_disabled_path(community_path)
    custom_path = (custom_path or "").strip()
    primary = os.path.normpath(custom_path) if custom_path else default_path
    os.makedirs(primary, exist_ok=True)

    locations = [primary]
    if os.path.normcase(os.path.normpath(default_path)) != os.path.normcase(primary) and os.path.isdir(default_path):
        locations.append(default_path)
    return locations


def _read_manifest(folder_path):
    manifest_path = os.path.join(folder_path, "manifest.json")
    if not os.path.exists(manifest_path):
        return None
    try:
        with open(manifest_path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception:
        return None


def _prettify_folder_name(folder_name):
    return folder_name.replace("-", " ").replace("_", " ").strip().title()


def _build_record(folder_name, folder_path, enabled):
    manifest = _read_manifest(folder_path)
    if manifest is None or manifest.get("content_type") != "SCENERY":
        return None

    title = (manifest.get("title") or "").strip()
    country, continent = categorize_scenery(folder_name, title)
    icao = find_icao_code(folder_name, title)

    base_name = title if title else _prettify_folder_name(folder_name)
    display_name = f"[{icao}] {base_name}" if icao else base_name

    return {
        "folder_name": folder_name,
        "display_name": display_name,
        "country": country,
        "continent": continent,
        "enabled": enabled,
    }


def scan_scenery_packages(community_path, disabled_paths):
    """Vrati seznam scenerii (jen content_type == SCENERY) z Community i ze
    znamych disabled slozek (viz resolve_disabled_locations). Poloha na
    disku = enabled/disabled stav."""
    if not community_path or not os.path.isdir(community_path):
        return []

    records_by_name = {}
    # Community se prochazi az posledni - pri konfliktu (stejny nazev na
    # vice mistech, coz nastat nemelo) vyhrava "enabled", bezpecnejsi vychozi stav.
    for base_path, enabled in [(p, False) for p in disabled_paths] + [(community_path, True)]:
        try:
            entries = list(os.scandir(base_path))
        except OSError:
            continue
        for entry in entries:
            if not entry.is_dir():
                continue
            record = _build_record(entry.name, entry.path, enabled)
            if record is not None:
                records_by_name[entry.name] = record

    records = list(records_by_name.values())
    records.sort(key=lambda r: (
        _continent_sort_key(r["continent"]),
        r["country"] or "￿",
        r["display_name"].lower(),
    ))
    return records


def _same_drive(a, b):
    return os.path.splitdrive(os.path.normpath(a))[0].lower() == os.path.splitdrive(os.path.normpath(b))[0].lower()


def _directory_size(path):
    total = 0
    for root, _dirs, files in os.walk(path):
        for name in files:
            try:
                total += os.path.getsize(os.path.join(root, name))
            except OSError:
                pass
    return total


def move_package(src, dst, progress_callback=None):
    """Presune src do dst. Na stejnem disku je to bleskove prejmenovani
    (progress_callback se rovnou zavola se 100 %). Mezi disky nejde
    prejmenovat - kopiruje se soubor po souboru, s prubeznym hlasenim
    progress_callback(copied_bytes, total_bytes); zdroj se smaze az PO
    uspesnem dokonceni cele kopie (shutil.move dela totez, jen bez
    prubezneho reportovani)."""
    if _same_drive(src, dst):
        shutil.move(src, dst)
        if progress_callback:
            progress_callback(1, 1)
        return

    total_bytes = _directory_size(src) or 1
    copied_bytes = 0

    def _copy_file(s, d):
        nonlocal copied_bytes
        shutil.copy2(s, d)
        try:
            copied_bytes += os.path.getsize(s)
        except OSError:
            pass
        if progress_callback:
            progress_callback(copied_bytes, total_bytes)

    shutil.copytree(src, dst, copy_function=_copy_file)
    shutil.rmtree(src)


def apply_package_changes(community_path, disabled_paths, desired_states, progress_callback=None):
    """desired_states: {folder_name: True/False (chce enabled?)}.
    Presouva jen to, co se lisi od aktualni polohy. Nikdy nemaze, nikdy
    nepretizi existujici cil. Vraci seznam (folder_name, ok, error_or_None).
    Funguje pro jakykoliv typ obsahu (scenerie, letadla, liverky) - pracuje
    jen s nazvem slozky, na content_type mu nezalezi.

    disabled_paths: serazeny seznam z resolve_disabled_locations - pri
    zapinani se zdroj hleda napric CELYM seznamem (balicek muze lezet ve
    starem i aktualne nastavenem umisteni), pri vypinani se vzdy cili na
    disabled_paths[0] (aktualne nastavene primarni umisteni).

    progress_callback(item_index, item_count, folder_name, copied_bytes,
    total_bytes), volano prubezne behem kazdeho presunu (u presunu na
    stejnem disku jen jednou se 100 %) - pro progress bar v UI."""
    primary_disabled = disabled_paths[0]

    pending = []
    for folder_name, desired_enabled in desired_states.items():
        community_target = os.path.join(community_path, folder_name)
        currently_in_community = os.path.isdir(community_target)

        current_disabled_path = None
        for disabled_base in disabled_paths:
            candidate = os.path.join(disabled_base, folder_name)
            if os.path.isdir(candidate):
                current_disabled_path = candidate
                break

        if desired_enabled and currently_in_community:
            continue
        if not desired_enabled and current_disabled_path is not None:
            continue

        if desired_enabled:
            src, dst = current_disabled_path, community_target
        else:
            src, dst = community_target, os.path.join(primary_disabled, folder_name)

        pending.append((folder_name, src, dst))

    results = []
    item_count = len(pending)
    for index, (folder_name, src, dst) in enumerate(pending):
        if not src or not os.path.isdir(src):
            results.append((folder_name, False, "source not found"))
            continue
        if os.path.exists(dst):
            results.append((folder_name, False, "destination already exists"))
            continue

        def _on_progress(copied, total, idx=index, name=folder_name):
            if progress_callback:
                progress_callback(idx, item_count, name, copied, total)

        try:
            move_package(src, dst, progress_callback=_on_progress)
            results.append((folder_name, True, None))
        except Exception as e:
            results.append((folder_name, False, str(e)))

    return results


def _developer_key(folder_name):
    # Prvni segment nazvu slozky je u komunitnich balicku konzistentne
    # vyvojarska/studiova zkratka (napr. "fnx-", "bksq-", "flybywire-") -
    # stejny princip jako u scenerii, jen tady je to primo klic pro
    # seskupeni, ne neco k vyrazeni.
    rest = re.split(r'[-_]', folder_name, maxsplit=1)
    return rest[0].lower()


def _prettify_dev_key(key):
    return key[:1].upper() + key[1:] if key else key


# Curated folder-prefix -> studio display name, built from real installed
# packages + web research (see data/aircraft_developers.json). Deliberately
# small and only covers prefixes verified to actually be a studio's package
# ID - individual community livery painters (who don't follow a "studio-"
# folder convention) are intentionally left out and fall through to the
# creator-consensus/prettified-prefix heuristic below instead of a guess.
_aircraft_developers_cache = None


def _aircraft_developers():
    global _aircraft_developers_cache
    if _aircraft_developers_cache is None:
        if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
            base_dir = sys._MEIPASS
        else:
            base_dir = os.path.dirname(os.path.abspath(__file__))
        try:
            with open(os.path.join(base_dir, "data", "aircraft_developers.json"), "r", encoding="utf-8") as f:
                _aircraft_developers_cache = json.load(f)
        except (OSError, json.JSONDecodeError):
            _aircraft_developers_cache = {}
    return _aircraft_developers_cache


_BASE_CONTAINER_RE = re.compile(r'base_container\s*=\s*"([^"]+)"', re.IGNORECASE)


def _find_simobject_names(pkg_path):
    """Vrati nazvy slozek primo pod SimObjects/Airplanes/<nazev> - to je
    interni identifikator, kterym na sebe letadlo a jeho liverky odkazuji."""
    names = set()
    simobj = os.path.join(pkg_path, "SimObjects", "Airplanes")
    try:
        entries = os.scandir(simobj)
    except OSError:
        return names
    with entries:
        for entry in entries:
            if entry.is_dir():
                names.add(entry.name)
    return names


def _find_base_containers(pkg_path):
    """Projde SimObjects/Airplanes/*/aircraft.cfg uvnitr balicku a vrati
    mnozinu hodnot 'base_container' (jen nazev cilove slozky, ne cela
    relativni cesta) - to je oficialni odkaz MSFS na zakladni letadlo,
    spolehlivejsi nez hadani podle textu v titulku."""
    bases = set()
    simobj = os.path.join(pkg_path, "SimObjects", "Airplanes")
    try:
        entries = os.scandir(simobj)
    except OSError:
        return bases
    with entries:
        for entry in entries:
            if not entry.is_dir():
                continue
            cfg_path = os.path.join(entry.path, "aircraft.cfg")
            try:
                with open(cfg_path, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read()
            except OSError:
                continue
            match = _BASE_CONTAINER_RE.search(content)
            if match:
                base_name = match.group(1).replace("\\", "/").rstrip("/").split("/")[-1]
                if base_name:
                    bases.add(base_name)
    return bases


def _is_actually_a_livery(pkg_path):
    """True if this package's own aircraft.cfg base_container points outside
    its own SimObjects folder - i.e. it structurally depends on another
    package's aircraft to even render, which makes it a livery/repaint no
    matter what the package declares itself as. Needed because manifest.json
    content_type is self-reported by the addon author and is often wrong in
    practice for registration/repaint packs (content_type "AIRCRAFT" used
    for what is really just a livery) - base_container is MSFS's own,
    non-optional wiring and can't lie the same way."""
    own_names = {n.lower() for n in _find_simobject_names(pkg_path)}
    if not own_names:
        return False
    for base_name in _find_base_containers(pkg_path):
        if base_name.lower() not in own_names:
            return True
    return False


def scan_aircraft_and_liveries(community_path, disabled_paths):
    """Vrati (aircraft, liveries) - dva seznamy zaznamu
    {folder_name, display_name, developer, enabled}, seskupitelne podle
    developera (manifest.json pole "manufacturer" je v praxi nekonzistentni -
    nekdy skutecny vyrobce draku, jindy vyvojarske studio - takze pro
    seskupeni se nepouziva).

    "developer" se odvozuje z prefixu nazvu slozky (spolehlivy - stejny
    vyvojar ho pouziva pro cely svuj sortiment). Zobrazeny nazev se hleda v
    tomto poradi: (1) kurirovany seznam znamych studii viz
    _aircraft_developers() / data/aircraft_developers.json, (2) manifest.json
    pole "creator" - ale jen kdyz se na nem VSECHNA letadla (ne liverky, ty
    casto maji jako creatora jednotliveho malire) se stejnym prefixem
    shodnou, (3) neutralni prettified prefix jako posledni zaloha.

    content_type z manifest.json ("AIRCRAFT"/"LIVERY") je bohuzel casto
    nespolehlivy - registracni/repaint baliky se v praxi casto oznacuji
    jako "AIRCRAFT", i kdyz jde jen o liverku pro jiz existujici letadlo.
    Kdyz balicek deklaruje AIRCRAFT, ale jeho vlastni base_container
    ukazuje mimo jeho vlastni SimObjects slozku (viz _is_actually_a_livery),
    prekvalifikuje se na LIVERY - tomuhle se addon nemuze "priblbnout" tak
    snadno jako manifestu, protoze bez spravneho base_container by v MSFS
    vubec nefungoval.

    Stejny princip jako scan_scenery_packages - poloha na disku = stav.

    Livery zaznamy navic maji "parent_aircraft" - folder_name konkretniho
    letadla, pokud se ho podarilo spolehlive dohledat pres base_container
    v aircraft.cfg (viz _find_base_containers). Kdyz se nenajde (jine
    letadlo neni v Community nainstalovane, nebo balicek base_container
    neobsahuje), zustava None - zadne hadani podle textu, radsi zadny par
    nez spatny."""
    if not community_path or not os.path.isdir(community_path):
        return [], []

    aircraft_by_name = {}
    livery_by_name = {}
    creators_by_dev_key = {}

    for base_path, enabled in [(p, False) for p in disabled_paths] + [(community_path, True)]:
        try:
            entries = list(os.scandir(base_path))
        except OSError:
            continue
        for entry in entries:
            if not entry.is_dir():
                continue
            manifest = _read_manifest(entry.path)
            if manifest is None:
                continue
            content_type = manifest.get("content_type")
            if content_type not in ("AIRCRAFT", "LIVERY"):
                continue
            if content_type == "AIRCRAFT" and _is_actually_a_livery(entry.path):
                content_type = "LIVERY"

            title = (manifest.get("title") or "").strip()
            creator = (manifest.get("creator") or "").strip()
            dev_key = _developer_key(entry.name)

            bucket = creators_by_dev_key.setdefault(dev_key, {"aircraft": set(), "all": set()})
            if creator:
                bucket["all"].add(creator)
                if content_type == "AIRCRAFT":
                    bucket["aircraft"].add(creator)

            display_name = title if title else _prettify_folder_name(entry.name)

            record = {
                "folder_name": entry.name,
                "display_name": display_name,
                "dev_key": dev_key,
                "enabled": enabled,
                "parent_aircraft": None,
                "_current_path": entry.path,
            }
            target = aircraft_by_name if content_type == "AIRCRAFT" else livery_by_name
            target[entry.name] = record

    known_developers = _aircraft_developers()
    dev_display = {}
    for dev_key, bucket in creators_by_dev_key.items():
        if dev_key in known_developers:
            dev_display[dev_key] = known_developers[dev_key]
        elif len(bucket["aircraft"]) == 1:
            dev_display[dev_key] = next(iter(bucket["aircraft"]))
        elif len(bucket["all"]) == 1:
            dev_display[dev_key] = next(iter(bucket["all"]))
        else:
            dev_display[dev_key] = _prettify_dev_key(dev_key)

    for records in (aircraft_by_name, livery_by_name):
        for record in records.values():
            record["developer"] = dev_display.get(record["dev_key"], _prettify_dev_key(record["dev_key"]))

    # Mapa: interni nazev SimObjects slozky letadla -> nazev jeho balicku.
    simobject_to_aircraft = {}
    for folder_name, record in aircraft_by_name.items():
        for name in _find_simobject_names(record["_current_path"]):
            simobject_to_aircraft.setdefault(name.lower(), folder_name)

    for folder_name, record in livery_by_name.items():
        for base_name in _find_base_containers(record["_current_path"]):
            match = simobject_to_aircraft.get(base_name.lower())
            if match:
                record["parent_aircraft"] = match
                break

    def _sort_key(r):
        return (r["developer"] or "￿", r["display_name"].lower())

    for records in (aircraft_by_name, livery_by_name):
        for record in records.values():
            del record["_current_path"]

    aircraft = sorted(aircraft_by_name.values(), key=_sort_key)
    liveries = sorted(livery_by_name.values(), key=_sort_key)
    return aircraft, liveries
