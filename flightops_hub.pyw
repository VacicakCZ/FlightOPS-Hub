import customtkinter as ctk
import os
import json
import datetime
import re
import xml.etree.ElementTree as ET
from tkinter import filedialog, messagebox
import traceback
import tkinter as tk
import shutil
import ctypes
import time
import threading
import subprocess
import scenery_data

# Nastavení vzhledu okna
ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

CONFIG_FILE = "msfs_launcher_config.json"
APPS_FILE = "msfs_apps.json"

# --- Zamek jedne instance (pojmenovany mutex) ---
# "Global\" prefix = viditelny napric vsemi uzivatelskymi session, ne jen tou
# aktualni - kdyby appku nekdo pustil 2x pod ruznymi uzivateli/RDP session.
_SINGLE_INSTANCE_MUTEX_NAME = "Global\\FlightOpsHub_SingleInstance_Mutex"
_ERROR_ALREADY_EXISTS = 183
_kernel32_last_error = ctypes.WinDLL("kernel32", use_last_error=True)


def acquire_single_instance_lock():
    """Vytvori pojmenovany mutex a vrati (handle, uz_bezi). Handle se musi
    drzet nazivu po celou dobu behu appky (jinak by se mutex uvolnil) -
    volajici by si ho mel ulozit napr. do globalni promenne v main()."""
    handle = _kernel32_last_error.CreateMutexW(None, False, _SINGLE_INSTANCE_MUTEX_NAME)
    already_running = ctypes.get_last_error() == _ERROR_ALREADY_EXISTS
    return handle, already_running

# --- ctypes struktura pro ShellExecuteExW (potřebujeme PID spuštěného procesu) ---
SEE_MASK_NOCLOSEPROCESS = 0x00000040
SW_SHOWNORMAL = 1


class SHELLEXECUTEINFOW(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_ulong),
        ("fMask", ctypes.c_ulong),
        ("hwnd", ctypes.c_void_p),
        ("lpVerb", ctypes.c_wchar_p),
        ("lpFile", ctypes.c_wchar_p),
        ("lpParameters", ctypes.c_wchar_p),
        ("lpDirectory", ctypes.c_wchar_p),
        ("nShow", ctypes.c_int),
        ("hInstApp", ctypes.c_void_p),
        ("lpIDList", ctypes.c_void_p),
        ("lpClass", ctypes.c_wchar_p),
        ("hKeyClass", ctypes.c_void_p),
        ("dwHotKey", ctypes.c_ulong),
        ("hIcon", ctypes.c_void_p),
        ("hProcess", ctypes.c_void_p),
    ]


ctypes.windll.kernel32.GetProcessId.argtypes = [ctypes.c_void_p]
ctypes.windll.kernel32.GetProcessId.restype = ctypes.c_ulong
ctypes.windll.kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
ctypes.windll.kernel32.CloseHandle.restype = ctypes.c_int

# --- Třída pro Tooltipy (Bubliny po najetí myší) ---
class ToolTip:
    def __init__(self, widget, text):
        self.widget = widget
        self.text = text
        self.tooltip_window = None
        self.widget.bind("<Enter>", self.enter)
        self.widget.bind("<Leave>", self.leave)

    def enter(self, event=None):
        x, y, cx, cy = self.widget.bbox("insert")
        x += self.widget.winfo_rootx() + 25
        y += self.widget.winfo_rooty() + 25
        self.tooltip_window = tw = tk.Toplevel(self.widget)
        tw.wm_overrideredirect(True)
        tw.wm_geometry(f"+{x}+{y}")
        
        label = tk.Label(tw, text=self.text, justify='left',
                         background="#2b2b2b", foreground="white", relief='solid', borderwidth=1,
                         font=("Segoe UI", 11, "normal"), padx=5, pady=2)
        label.pack(ipadx=1)

    def leave(self, event=None):
        if self.tooltip_window:
            self.tooltip_window.destroy()
            self.tooltip_window = None

class MSFSLauncher(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.app_version = "v1.5"

        self.title(f"FlightOps Hub {self.app_version}")
        self.resizable(False, False)

        # Odchycení zavření okna křížkem pro uložení pozice
        self.protocol("WM_DELETE_WINDOW", self.on_closing)

        try:
            current_dir = os.path.dirname(os.path.abspath(__file__))
            icon_path = os.path.join(current_dir, "OIG3.ico")
            self.iconbitmap(icon_path)
        except Exception:
            pass

        self.translations = {
            "CZ": {
                "tab_let": "       Let       ",
                "tab_set": "   Nastavení   ",
                "tab_exe": "   AutoStart (exe.xml)   ",
                "tab_scenery": "   Scenérie   ",
                "tab_aircraft": "   Letadla (Beta)   ",
                "main_title": "Vyber aplikace pro tento let",
                "nav_not_found": "Složka Navigraph nenalezena (Aktuální: {})",
                "nav_ok": "✓ AIRAC {} je aktuální",
                "nav_old": "⚠️ AIRAC ZASTARALÝ (Máš: {} | Aktuální: {})",
                "btn_launch": "Spustit vybrané a MSFS",
                "lang_label": "Jazyk aplikace:",
                "theme_label": "Vzhled okna:",
                "sim_label": "Verze a platforma MSFS:",
                "set_nav_title": "Community složka MSFS",
                "set_nav_hint": "Používá se pro kontrolu AIRAC cyklu (Navigraph) i pro záložku Scenérie.",
                "set_nav_placeholder": "Cesta ke složce Community",
                "disabled_path_title": "Kam se přesouvají vypnuté doplňky",
                "disabled_path_hint": "Prázdné = výchozí umístění vedle Community. Jde nastavit i na jiný disk.",
                "disabled_path_placeholder": "Výchozí umístění (vedle Community)",
                "disabled_path_reset_btn": "Výchozí",
                "disabled_path_invalid_nested": "Tahle složka nejde použít - je stejná jako Community nebo je její součástí (případně naopak).",
                "disabled_path_cross_drive_warning": "Zvolená složka je na jiném disku než Community. Přesun tam pak není okamžité přejmenování, ale skutečné kopírování dat - u velkých balíčků to může trvat minuty a appka bude po tu dobu nereagovat.",
                "dialog_disabled_path": "Vyber složku pro vypnuté doplňky",
                "btn_browse": "Procházet...",
                "set_add_title": "Přidat nebo upravit doplněk",
                "add_name_placeholder": "Název (např. Volanta)",
                "add_path_placeholder": "Cesta k .exe souboru",
                "delay_label": "sekund zpoždění po startu MSFS",
                "mode_label": "Režim spuštění:",
                "mode_immediate": "Ihned",
                "mode_timer": "Časovač",
                "mode_smart": "Smart Launch",
                "mode_smart_short": "Smart",
                "tray_tooltip": "FlightOps Hub – čeká na načtení letu...",
                "tray_status": "Čeká na načtení letu...",
                "tray_cancel": "Zrušit čekání",
                "tray_watch_status": "Hlídám běh simulátoru...",
                "tray_force_stop": "Ukončit vše nyní",
                "post_launch_label": "Po dokončení spouštění:",
                "post_launch_exit": "Ukončit aplikaci",
                "post_launch_watch": "Zůstat a hlídat konec simulátoru",
                "run_admin_label": "Spustit jako správce (UAC)",
                "btn_add": "Přidat do seznamu",
                "btn_save_edit": "Uložit úpravy",
                "set_manage_title": "Spravovat existující doplňky",
                "btn_remove": "Odebrat",
                "btn_edit": "Upravit",
                "err_empty": "Chyba: Musíš vyplnit název i cestu!",
                "err_delay": "Chyba: Zpoždění musí být kladné číslo!",
                "dialog_community": "Vyber hlavní složku Community pro MSFS",
                "profile_label": "Profil letu:",
                "exe_profile_label": "Profil simulátoru:",
                "profile_prompt_title": "Uložit profil",
                "profile_prompt_text": "Zadej název nového profilu:",
                "default_profile": "Výchozí",
                "theme_dark": "Tmavý",
                "theme_light": "Světlý",
                "theme_system": "Systém",
                "exe_title": "Správa doplňků v exe.xml",
                "exe_not_found": "Soubor exe.xml nebyl na dané cestě nalezen!",
                "exe_error": "Chyba při čtení XML souboru:",
                "tt_update": "Aktualizovat vybraný profil",
                "tt_add": "Přidat nový profil",
                "tt_delete": "Smazat vybraný profil",
                "exe_rename_prompt": "Zadej vlastní název:",
                "exe_rename_title": "Vlastní název",
                "exe_rename_tt": "Přejmenovat",
                "exe_rename_hint": "💡 Tip: Přejmenuj doplněk kliknutím na ✏️. Pro vrácení na originál nechej pole prázdné.",
                "scenery_no_community": "Nejdřív v Nastavení vyplň cestu ke složce Community.",
                "scenery_select_all_on": "Zapnout vše",
                "scenery_select_all_off": "Vypnout vše",
                "scenery_group_toggle_tt": "Zapnout/vypnout vše v této skupině",
                "scenery_apply_btn": "Použít",
                "scenery_refresh_btn": "Obnovit",
                "scenery_applying": "Přesouvám...",
                "scenery_applying_progress": "Přesouvám ({0}/{1}): {2}",
                "scenery_apply_result": "Hotovo: {} zapnuto, {} vypnuto",
                "scenery_apply_errors": ", {} chyb (viz konzole)",
                "scenery_other_country": "Ostatní / Neurčeno",
                "continent_europe": "Evropa",
                "continent_north_america": "Severní Amerika",
                "continent_south_america": "Jižní Amerika",
                "continent_asia": "Asie",
                "continent_africa": "Afrika",
                "continent_oceania": "Austrálie a Oceánie",
                "continent_other": "Ostatní / Neurčeno",
                "aircraft_section_aircraft": "Letadla",
                "aircraft_section_liveries": "Liverky (nepřiřazené k letadlu)",
                "aircraft_other_developer": "Ostatní / Neurčený vývojář",
                "aircraft_grouping_hint": "Řadí se podle vývojáře (odvozeno z názvu balíčku), ne podle pole \"výrobce\" - to bývá v addonech nekonzistentní. Liverky, u kterých se podařilo spolehlivě dohledat konkrétní letadlo, se zobrazí přímo pod ním, zbytek zůstává v sekci \"nepřiřazené\" pod vývojářem.",
                "aircraft_experimental_notice": "⚠️ Experimentální funkce - je nová, zkontroluj si prosím výsledek po vypnutí/zapnutí letadel.",
                "addon_apply_busy": "Právě probíhá přesun souborů - počkej prosím, než se dokončí."
            },
            "EN": {
                "tab_let": "      Flight      ",
                "tab_set": "    Settings    ",
                "tab_exe": "   AutoStart (exe.xml)   ",
                "tab_scenery": "   Scenery   ",
                "tab_aircraft": "   Aircraft (Beta)   ",
                "main_title": "Select applications for this flight",
                "nav_not_found": "Navigraph folder not found (Current: {})",
                "nav_ok": "✓ AIRAC {} is up to date",
                "nav_old": "⚠️ AIRAC OUTDATED (Installed: {} | Current: {})",
                "btn_launch": "Launch selected & MSFS",
                "lang_label": "App Language:",
                "theme_label": "App Theme:",
                "sim_label": "MSFS Version & Platform:",
                "set_nav_title": "MSFS Community Folder",
                "set_nav_hint": "Used for the Navigraph AIRAC check and the Scenery tab.",
                "set_nav_placeholder": "Path to Community folder",
                "disabled_path_title": "Where disabled add-ons are moved",
                "disabled_path_hint": "Empty = default location next to Community. Can also be set to a different drive.",
                "disabled_path_placeholder": "Default location (next to Community)",
                "disabled_path_reset_btn": "Default",
                "disabled_path_invalid_nested": "This folder can't be used - it's the same as Community or contained within it (or the other way around).",
                "disabled_path_cross_drive_warning": "The chosen folder is on a different drive than Community. Moving there won't be an instant rename anymore, but an actual data copy - for large packages this can take minutes and the app will be unresponsive during that time.",
                "dialog_disabled_path": "Select the folder for disabled add-ons",
                "btn_browse": "Browse...",
                "set_add_title": "Add or Edit Addon",
                "add_name_placeholder": "Name (e.g. Volanta)",
                "add_path_placeholder": "Path to .exe file",
                "delay_label": "seconds delay after MSFS starts",
                "mode_label": "Launch Mode:",
                "mode_immediate": "Immediate",
                "mode_timer": "Timer",
                "mode_smart": "Smart Launch",
                "mode_smart_short": "Smart",
                "tray_tooltip": "FlightOps Hub – waiting for flight to load...",
                "tray_status": "Waiting for flight to load...",
                "tray_cancel": "Cancel waiting",
                "tray_watch_status": "Watching for sim exit...",
                "tray_force_stop": "Close everything now",
                "post_launch_label": "After launch completes:",
                "post_launch_exit": "Exit application",
                "post_launch_watch": "Stay and watch for sim exit",
                "run_admin_label": "Run as Administrator (UAC)",
                "btn_add": "Add to list",
                "btn_save_edit": "Save changes",
                "set_manage_title": "Manage existing Addons",
                "btn_remove": "Remove",
                "btn_edit": "Edit",
                "err_empty": "Error: Name and path must be filled!",
                "err_delay": "Error: Delay must be a positive number!",
                "dialog_community": "Select main Community folder for MSFS",
                "profile_label": "Flight Profile:",
                "exe_profile_label": "Simulator Profile:",
                "profile_prompt_title": "Save Profile",
                "profile_prompt_text": "Enter new profile name:",
                "default_profile": "Default",
                "theme_dark": "Dark",
                "theme_light": "Light",
                "theme_system": "System",
                "exe_title": "Manage Addons via exe.xml",
                "exe_not_found": "The exe.xml file was not found at this path!",
                "exe_error": "Error reading XML file:",
                "tt_update": "Update selected profile",
                "tt_add": "Add new profile",
                "tt_delete": "Delete selected profile",
                "exe_rename_prompt": "Enter custom name:",
                "exe_rename_title": "Custom Name",
                "exe_rename_tt": "Rename",
                "exe_rename_hint": "💡 Tip: Rename an add-on by clicking ✏️. Leave the field blank to reset to the original name.",
                "scenery_no_community": "First set the Community folder path in Settings.",
                "scenery_select_all_on": "Enable all",
                "scenery_select_all_off": "Disable all",
                "scenery_group_toggle_tt": "Enable/disable everything in this group",
                "scenery_apply_btn": "Apply",
                "scenery_refresh_btn": "Refresh",
                "scenery_applying": "Moving...",
                "scenery_applying_progress": "Moving ({0}/{1}): {2}",
                "scenery_apply_result": "Done: {} enabled, {} disabled",
                "scenery_apply_errors": ", {} errors (see console)",
                "scenery_other_country": "Other / Unclassified",
                "continent_europe": "Europe",
                "continent_north_america": "North America",
                "continent_south_america": "South America",
                "continent_asia": "Asia",
                "continent_africa": "Africa",
                "continent_oceania": "Australia & Oceania",
                "continent_other": "Other / Unclassified",
                "aircraft_section_aircraft": "Aircraft",
                "aircraft_section_liveries": "Liveries (not linked to an aircraft)",
                "aircraft_other_developer": "Other / Unknown developer",
                "aircraft_grouping_hint": "Grouped by developer (derived from the package name), not the \"manufacturer\" field - add-ons fill that in inconsistently. Liveries reliably matched to a specific aircraft are shown nested under it; the rest stay in the \"unassigned\" section under the developer.",
                "aircraft_experimental_notice": "⚠️ Experimental feature - it's new, please double-check the result after enabling/disabling aircraft.",
                "addon_apply_busy": "A file move is in progress - please wait for it to finish."
            },
            "DE": {
                "tab_let": "      Flug      ",
                "tab_set": " Einstellungen ",
                "tab_exe": "   AutoStart (exe.xml)   ",
                "tab_scenery": "   Szenerie   ",
                "tab_aircraft": "   Flugzeuge (Beta)   ",
                "main_title": "Wähle Anwendungen für diesen Flug",
                "nav_not_found": "Navigraph-Ordner nicht gefunden (Aktuell: {})",
                "nav_ok": "✓ AIRAC {} ist aktuell",
                "nav_old": "⚠️ AIRAC VERALTET (Installiert: {} | Aktuell: {})",
                "btn_launch": "Ausgewählte & MSFS starten",
                "lang_label": "App-Sprache:",
                "theme_label": "App-Thema:",
                "sim_label": "MSFS Version & Plattform:",
                "set_nav_title": "MSFS Community-Ordner",
                "set_nav_hint": "Wird für die Navigraph-AIRAC-Prüfung und den Szenerie-Tab verwendet.",
                "set_nav_placeholder": "Pfad zum Community-Ordner",
                "disabled_path_title": "Wohin deaktivierte Add-ons verschoben werden",
                "disabled_path_hint": "Leer = Standardort neben Community. Kann auch auf ein anderes Laufwerk gesetzt werden.",
                "disabled_path_placeholder": "Standardort (neben Community)",
                "disabled_path_reset_btn": "Standard",
                "disabled_path_invalid_nested": "Dieser Ordner kann nicht verwendet werden - er ist identisch mit Community oder darin enthalten (oder umgekehrt).",
                "disabled_path_cross_drive_warning": "Der gewählte Ordner liegt auf einem anderen Laufwerk als Community. Das Verschieben dorthin ist dann kein sofortiges Umbenennen mehr, sondern echtes Kopieren der Daten - bei großen Paketen kann das Minuten dauern, und die App reagiert währenddessen nicht.",
                "dialog_disabled_path": "Ordner für deaktivierte Add-ons wählen",
                "btn_browse": "Durchsuchen...",
                "set_add_title": "Addon hinzufügen/bearbeiten",
                "add_name_placeholder": "Name (z.B. Volanta)",
                "add_path_placeholder": "Pfad zur .exe-Datei",
                "delay_label": "Sekunden Verzögerung nach MSFS-Start",
                "mode_label": "Startmodus:",
                "mode_immediate": "Sofort",
                "mode_timer": "Timer",
                "mode_smart": "Smart Launch",
                "mode_smart_short": "Smart",
                "tray_tooltip": "FlightOps Hub – wartet auf Flugstart...",
                "tray_status": "Wartet auf Flugstart...",
                "tray_cancel": "Warten abbrechen",
                "tray_watch_status": "Überwacht Simulator-Ende...",
                "tray_force_stop": "Alles jetzt beenden",
                "post_launch_label": "Nach Abschluss des Starts:",
                "post_launch_exit": "Anwendung beenden",
                "post_launch_watch": "Bleiben und Simulator-Ende überwachen",
                "run_admin_label": "Als Administrator ausführen",
                "btn_add": "Zur Liste hinzufügen",
                "btn_save_edit": "Änderungen speichern",
                "set_manage_title": "Vorhandene Addons verwalten",
                "btn_remove": "Entfernen",
                "btn_edit": "Bearbeiten",
                "err_empty": "Fehler: Name und Pfad müssen ausgefüllt sein!",
                "err_delay": "Fehler: Verzögerung muss eine positive Zahl sein!",
                "dialog_community": "Wähle den Haupt-Community-Ordner für MSFS",
                "profile_label": "Flugprofil:",
                "exe_profile_label": "Simulator-Profil:",
                "profile_prompt_title": "Profil speichern",
                "profile_prompt_text": "Neuen Profilnamen eingeben:",
                "default_profile": "Standard",
                "theme_dark": "Dunkel",
                "theme_light": "Hell",
                "theme_system": "System",
                "exe_title": "Addons über exe.xml verwalten",
                "exe_not_found": "Die Datei exe.xml wurde in diesem Pfad nicht gefunden!",
                "exe_error": "Fehler beim Lesen der XML-Datei:",
                "tt_update": "Ausgewähltes Profil aktualisieren",
                "tt_add": "Neues Profil hinzufügen",
                "tt_delete": "Ausgewähltes Profil löschen",
                "exe_rename_prompt": "Benutzerdefinierten Namen eingeben:",
                "exe_rename_title": "Eigener Name",
                "exe_rename_tt": "Umbenennen",
                "exe_rename_hint": "💡 Tipp: Benenne ein Add-on mit ✏️ um. Lass das Feld leer, um den Originalnamen wiederherzustellen.",
                "scenery_no_community": "Lege zuerst den Community-Ordnerpfad in den Einstellungen fest.",
                "scenery_select_all_on": "Alle aktivieren",
                "scenery_select_all_off": "Alle deaktivieren",
                "scenery_group_toggle_tt": "Alles in dieser Gruppe aktivieren/deaktivieren",
                "scenery_apply_btn": "Anwenden",
                "scenery_refresh_btn": "Aktualisieren",
                "scenery_applying": "Verschiebe...",
                "scenery_applying_progress": "Verschiebe ({0}/{1}): {2}",
                "scenery_apply_result": "Fertig: {} aktiviert, {} deaktiviert",
                "scenery_apply_errors": ", {} Fehler (siehe Konsole)",
                "scenery_other_country": "Sonstige / Unbekannt",
                "continent_europe": "Europa",
                "continent_north_america": "Nordamerika",
                "continent_south_america": "Südamerika",
                "continent_asia": "Asien",
                "continent_africa": "Afrika",
                "continent_oceania": "Australien & Ozeanien",
                "continent_other": "Sonstige / Unbekannt",
                "aircraft_section_aircraft": "Flugzeuge",
                "aircraft_section_liveries": "Lackierungen (ohne Flugzeugzuordnung)",
                "aircraft_other_developer": "Sonstige / Unbekannter Entwickler",
                "aircraft_grouping_hint": "Gruppierung nach Entwickler (aus dem Paketnamen abgeleitet), nicht nach dem Feld \"Hersteller\" - das wird von Add-ons uneinheitlich befüllt. Lackierungen mit zuverlässig erkanntem Flugzeug werden direkt darunter angezeigt, der Rest bleibt im Abschnitt \"nicht zugeordnet\" beim Entwickler.",
                "aircraft_experimental_notice": "⚠️ Experimentelle Funktion - sie ist neu, bitte überprüfe das Ergebnis nach dem Aktivieren/Deaktivieren von Flugzeugen.",
                "addon_apply_busy": "Es läuft gerade eine Dateiverschiebung - bitte warte, bis sie abgeschlossen ist."
            },
            "ES": {
                "tab_let": "     Vuelo     ",
                "tab_set": "    Ajustes    ",
                "tab_exe": "   AutoStart (exe.xml)   ",
                "tab_scenery": "   Escenarios   ",
                "tab_aircraft": "   Aeronaves (Beta)   ",
                "main_title": "Selecciona aplicaciones para este vuelo",
                "nav_not_found": "Carpeta Navigraph no encontrada (Actual: {})",
                "nav_ok": "✓ AIRAC {} está actualizado",
                "nav_old": "⚠️ AIRAC OBSOLETO (Instalado: {} | Actual: {})",
                "btn_launch": "Iniciar seleccionadas y MSFS",
                "lang_label": "Idioma de la App:",
                "theme_label": "Tema de la App:",
                "sim_label": "Versión y Plataforma MSFS:",
                "set_nav_title": "Carpeta Community de MSFS",
                "set_nav_hint": "Se usa para la comprobación AIRAC (Navigraph) y para la pestaña Escenarios.",
                "set_nav_placeholder": "Ruta a la carpeta Community",
                "disabled_path_title": "Dónde se mueven los addons desactivados",
                "disabled_path_hint": "Vacío = ubicación por defecto junto a Community. También se puede poner en otro disco.",
                "disabled_path_placeholder": "Ubicación por defecto (junto a Community)",
                "disabled_path_reset_btn": "Por defecto",
                "disabled_path_invalid_nested": "No se puede usar esta carpeta - es la misma que Community o está contenida en ella (o al revés).",
                "disabled_path_cross_drive_warning": "La carpeta elegida está en un disco distinto al de Community. Mover ahí ya no será un renombrado instantáneo, sino una copia real de datos - en paquetes grandes puede tardar minutos y la app no responderá durante ese tiempo.",
                "dialog_disabled_path": "Selecciona la carpeta para addons desactivados",
                "btn_browse": "Examinar...",
                "set_add_title": "Añadir o editar Addon",
                "add_name_placeholder": "Nombre (ej. Volanta)",
                "add_path_placeholder": "Ruta al archivo .exe",
                "delay_label": "segundos de retraso tras iniciar MSFS",
                "mode_label": "Modo de inicio:",
                "mode_immediate": "Inmediato",
                "mode_timer": "Temporizador",
                "mode_smart": "Smart Launch",
                "mode_smart_short": "Smart",
                "tray_tooltip": "FlightOps Hub – esperando a que cargue el vuelo...",
                "tray_status": "Esperando a que cargue el vuelo...",
                "tray_cancel": "Cancelar espera",
                "tray_watch_status": "Vigilando el cierre del simulador...",
                "tray_force_stop": "Cerrar todo ahora",
                "post_launch_label": "Después de completar el inicio:",
                "post_launch_exit": "Salir de la aplicación",
                "post_launch_watch": "Permanecer y vigilar el cierre del simulador",
                "run_admin_label": "Ejecutar como administrador",
                "btn_add": "Añadir a la lista",
                "btn_save_edit": "Guardar cambios",
                "set_manage_title": "Gestionar Addons existentes",
                "btn_remove": "Eliminar",
                "btn_edit": "Editar",
                "err_empty": "Error: ¡El nombre y la ruta son obligatorios!",
                "err_delay": "Error: ¡El retraso debe ser un número positivo!",
                "dialog_community": "Selecciona la carpeta Community de MSFS",
                "profile_label": "Perfil de Vuelo:",
                "exe_profile_label": "Perfil del Simulador:",
                "profile_prompt_title": "Guardar Perfil",
                "profile_prompt_text": "Introduce el nombre del perfil:",
                "default_profile": "Por defecto",
                "theme_dark": "Oscuro",
                "theme_light": "Claro",
                "theme_system": "Sistema",
                "exe_title": "Gestionar Addons vía exe.xml",
                "exe_not_found": "¡No se encontró el archivo exe.xml en esta ruta!",
                "exe_error": "Error al leer el archivo XML:",
                "tt_update": "Actualizar perfil seleccionado",
                "tt_add": "Añadir nuevo perfil",
                "tt_delete": "Eliminar perfil seleccionado",
                "exe_rename_prompt": "Introduce un nombre personalizado:",
                "exe_rename_title": "Nombre personalizado",
                "exe_rename_tt": "Renombrar",
                "exe_rename_hint": "💡 Consejo: Renombra un addon haciendo clic en ✏️. Deja el campo en blanco para restaurar el original.",
                "scenery_no_community": "Primero define la ruta de la carpeta Community en Ajustes.",
                "scenery_select_all_on": "Activar todo",
                "scenery_select_all_off": "Desactivar todo",
                "scenery_group_toggle_tt": "Activar/desactivar todo en este grupo",
                "scenery_apply_btn": "Aplicar",
                "scenery_refresh_btn": "Actualizar",
                "scenery_applying": "Moviendo...",
                "scenery_applying_progress": "Moviendo ({0}/{1}): {2}",
                "scenery_apply_result": "Listo: {} activados, {} desactivados",
                "scenery_apply_errors": ", {} errores (ver consola)",
                "scenery_other_country": "Otros / Sin clasificar",
                "continent_europe": "Europa",
                "continent_north_america": "Norteamérica",
                "continent_south_america": "Sudamérica",
                "continent_asia": "Asia",
                "continent_africa": "África",
                "continent_oceania": "Australia y Oceanía",
                "continent_other": "Otros / Sin clasificar",
                "aircraft_section_aircraft": "Aeronaves",
                "aircraft_section_liveries": "Libreas (sin aeronave asignada)",
                "aircraft_other_developer": "Otros / Desarrollador desconocido",
                "aircraft_grouping_hint": "Agrupado por desarrollador (deducido del nombre del paquete), no por el campo \"fabricante\" - los addons lo rellenan de forma inconsistente. Las libreas emparejadas de forma fiable con una aeronave concreta se muestran debajo de ella; el resto queda en la sección \"sin asignar\" bajo el desarrollador.",
                "aircraft_experimental_notice": "⚠️ Función experimental - es nueva, por favor comprueba el resultado tras activar/desactivar aeronaves.",
                "addon_apply_busy": "Se está moviendo archivos - espera a que termine."
            },
            "CN": {
                "tab_let": "       飞行       ",
                "tab_set": "       设置       ",
                "tab_exe": "   自动启动 (exe.xml)   ",
                "tab_scenery": "   场景   ",
                "tab_aircraft": "   飞机 (Beta)   ",
                "main_title": "请选择本次飞行的应用程序",
                "nav_not_found": "未找到 Navigraph 文件夹 (当前: {})",
                "nav_ok": "✓ AIRAC {} 是最新的",
                "nav_old": "⚠️ AIRAC 已过期 (已安装: {} | 当前: {})",
                "btn_launch": "启动所选程序 & MSFS",
                "lang_label": "应用语言:",
                "theme_label": "应用主题:",
                "sim_label": "MSFS 版本和平台:",
                "set_nav_title": "MSFS Community 文件夹",
                "set_nav_hint": "用于 Navigraph AIRAC 检查和场景标签页。",
                "set_nav_placeholder": "Community 文件夹路径",
                "disabled_path_title": "禁用的插件移动到哪里",
                "disabled_path_hint": "留空 = Community 旁边的默认位置。也可以设置到其他磁盘。",
                "disabled_path_placeholder": "默认位置（Community 旁边）",
                "disabled_path_reset_btn": "默认",
                "disabled_path_invalid_nested": "无法使用此文件夹 - 它与 Community 相同或包含在其中（或相反）。",
                "disabled_path_cross_drive_warning": "所选文件夹与 Community 位于不同的磁盘。移动到那里将不再是瞬间重命名，而是真正的数据复制——对于大型插件，这可能需要几分钟，期间应用程序将无响应。",
                "dialog_disabled_path": "选择禁用插件的文件夹",
                "btn_browse": "浏览...",
                "set_add_title": "添加或编辑插件",
                "add_name_placeholder": "名称 (例如 Volanta)",
                "add_path_placeholder": ".exe 文件路径",
                "delay_label": "MSFS 启动后的延迟秒数",
                "mode_label": "启动模式:",
                "mode_immediate": "立即",
                "mode_timer": "定时器",
                "mode_smart": "Smart Launch",
                "mode_smart_short": "Smart",
                "tray_tooltip": "FlightOps Hub – 等待飞行加载...",
                "tray_status": "等待飞行加载...",
                "tray_cancel": "取消等待",
                "tray_watch_status": "正在监视模拟器退出...",
                "tray_force_stop": "立即关闭全部",
                "post_launch_label": "启动完成后:",
                "post_launch_exit": "退出应用程序",
                "post_launch_watch": "保持运行并监视模拟器退出",
                "run_admin_label": "以管理员身份运行",
                "btn_add": "添加到列表",
                "btn_save_edit": "保存更改",
                "set_manage_title": "管理现有插件",
                "btn_remove": "移除",
                "btn_edit": "编辑",
                "err_empty": "错误: 必须填写名称和路径！",
                "err_delay": "错误: 延迟必须是正数！",
                "dialog_community": "选择 MSFS 的主 Community 文件夹",
                "profile_label": "飞行配置:",
                "exe_profile_label": "模拟器配置:",
                "profile_prompt_title": "保存配置",
                "profile_prompt_text": "输入新配置名称:",
                "default_profile": "默认",
                "theme_dark": "暗色",
                "theme_light": "亮色",
                "theme_system": "系统",
                "exe_title": "通过 exe.xml 管理插件",
                "exe_not_found": "在此路径下未找到 exe.xml 文件！",
                "exe_error": "读取 XML 文件时出错:",
                "tt_update": "更新选定的配置",
                "tt_add": "添加新配置",
                "tt_delete": "删除选定的配置",
                "exe_rename_prompt": "输入自定义名称:",
                "exe_rename_title": "自定义名称",
                "exe_rename_tt": "重命名",
                "exe_rename_hint": "💡 提示: 点击 ✏️ 重命名插件。留空以恢复原始名称。",
                "scenery_no_community": "请先在设置中填写 Community 文件夹路径。",
                "scenery_select_all_on": "全部启用",
                "scenery_select_all_off": "全部禁用",
                "scenery_group_toggle_tt": "启用/禁用此组中的全部内容",
                "scenery_apply_btn": "应用",
                "scenery_refresh_btn": "刷新",
                "scenery_applying": "正在移动...",
                "scenery_applying_progress": "正在移动 ({0}/{1}): {2}",
                "scenery_apply_result": "完成：已启用 {} 个，已禁用 {} 个",
                "scenery_apply_errors": "，{} 个错误（详见控制台）",
                "scenery_other_country": "其他 / 未分类",
                "continent_europe": "欧洲",
                "continent_north_america": "北美洲",
                "continent_south_america": "南美洲",
                "continent_asia": "亚洲",
                "continent_africa": "非洲",
                "continent_oceania": "澳大利亚和大洋洲",
                "continent_other": "其他 / 未分类",
                "aircraft_section_aircraft": "飞机",
                "aircraft_section_liveries": "涂装（未关联飞机）",
                "aircraft_other_developer": "其他 / 未知开发商",
                "aircraft_grouping_hint": "按开发商分组（从包名推断），而非「制造商」字段——插件对该字段的填写并不一致。能可靠匹配到具体飞机的涂装会显示在该飞机下方，其余的保留在开发商下的「未关联」部分。",
                "aircraft_experimental_notice": "⚠️ 实验性功能 - 这是新功能，启用/禁用飞机后请仔细检查结果。",
                "addon_apply_busy": "正在移动文件 - 请等待完成后再试。"
            }
        }

        self.apps = {}
        self.editing_app = None 
        self.saved_states = {} 
        self.profiles = {}
        self.exe_profiles = {}
        self.exe_custom_names = {}
        
        self.checkboxes = {}
        self.exe_checkboxes = {}
        self.current_xml_path = ""
        self.current_xml_tree = None
        self._launched_pids = []
        self._tray_icon = None
        self._scenery_expanded = set()
        self._scenery_expanded_countries = set()
        self._scenery_pending = {}
        self._scenery_all_records = []
        self._scenery_checkbox_vars = {}
        self._aircraft_expanded = set()
        self._aircraft_expanded_sections = set()
        self._aircraft_pending = {}
        self._aircraft_all_records = []
        self._aircraft_checkbox_vars = {}
        self._addon_apply_active = False

        self.load_config()
        self.load_apps()

        # Načtení paměti pozice okna z konfiguráku
        saved_geometry = self.saved_states.get("_window_geometry", "500x860")
        self.geometry(saved_geometry)
        
        self.current_lang = self.saved_states.get("_language", "EN")
        if self.current_lang not in ["EN", "CZ", "DE", "ES", "CN"]: 
            self.current_lang = "EN"
            
        self.current_theme = self.saved_states.get("_theme", "System")
        
        self.apply_theme(self.current_theme)
        self.setup_ui()

    def on_closing(self):
        # Uložení velikosti a pozice okna při ručním zavření křížkem
        self.saved_states["_window_geometry"] = self.geometry()
        self.save_config()
        self.destroy()

    def tr(self, key):
        return self.translations.get(self.current_lang, self.translations["EN"]).get(key, key)

    def apply_theme(self, theme_name):
        if theme_name in ["Dark", "Tmavý", "Dunkel", "Oscuro", "暗色"]:
            ctk.set_appearance_mode("dark")
            self.saved_states["_theme"] = "Dark"
        elif theme_name in ["Light", "Světlý", "Hell", "Claro", "亮色"]:
            ctk.set_appearance_mode("light")
            self.saved_states["_theme"] = "Light"
        else:
            ctk.set_appearance_mode("system")
            self.saved_states["_theme"] = "System"

    def change_theme(self, new_theme):
        self.apply_theme(new_theme)
        self.save_config()

    def save_post_launch_setting(self, selected_label):
        self.saved_states["_post_launch_behavior"] = self.post_launch_label_to_key.get(selected_label, "exit")
        self.save_config()

    def _resolve_disabled_locations(self, community_path):
        custom_path = self.saved_states.get("_disabled_holding_path", "")
        return scenery_data.resolve_disabled_locations(community_path, custom_path)

    def setup_ui(self):
        if hasattr(self, 'tabview'):
            self.tabview.destroy()

        self.tabview = ctk.CTkTabview(self)
        self.tabview.pack(padx=15, pady=10, fill="both", expand=True)

        self.tab_main = self.tabview.add(self.tr("tab_let"))
        self.tab_exe = self.tabview.add(self.tr("tab_exe"))
        self.tab_scenery = self.tabview.add(self.tr("tab_scenery"))
        self.tab_aircraft = self.tabview.add(self.tr("tab_aircraft"))
        self.tab_set = self.tabview.add(self.tr("tab_set"))

        self.build_main_tab()
        self.build_exe_tab()
        self.build_scenery_tab()
        self.build_aircraft_tab()
        self.build_settings_tab()

    def change_language(self, new_lang):
        self.current_lang = new_lang
        self.saved_states["_language"] = new_lang
        self.save_config()
        self.setup_ui()
        self.tabview.set(self.tr("tab_set"))

    def save_sim_settings(self, _=None):
        self.saved_states["_sim_version"] = self.sim_v_var.get()
        self.saved_states["_sim_platform"] = self.sim_p_var.get()
        self.save_config()
        self.build_exe_tab()

    def load_config(self):
        if os.path.exists(CONFIG_FILE):
            try:
                with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                    self.saved_states = json.load(f)
            except Exception:
                pass 
        self.profiles = self.saved_states.get("_profiles", {})
        self.exe_profiles = self.saved_states.get("_exe_profiles", {})
        self.exe_custom_names = self.saved_states.get("_exe_custom_names", {})

    def save_config(self):
        self.saved_states["_profiles"] = self.profiles
        self.saved_states["_exe_profiles"] = self.exe_profiles
        self.saved_states["_exe_custom_names"] = self.exe_custom_names
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(self.saved_states, f)

    def load_apps(self):
        if not os.path.exists(APPS_FILE):
            default_apps = {}
            with open(APPS_FILE, "w", encoding="utf-8") as f:
                json.dump(default_apps, f, indent=4)
            self.apps = default_apps.copy()
        else:
            with open(APPS_FILE, "r", encoding="utf-8") as f:
                loaded_apps = json.load(f)
            
            self.apps = {}
            for app_name, data in loaded_apps.items():
                if isinstance(data, str):
                    delay = 150 if app_name == "REX Atmos" else 0
                    launch_mode = "timer" if delay > 0 else "immediate"
                    self.apps[app_name] = {"path": data, "delay": delay, "admin": False, "launch_mode": launch_mode}
                else:
                    if "admin" not in data:
                        data["admin"] = False
                    if "launch_mode" not in data:
                        data["launch_mode"] = "timer" if data.get("delay", 0) > 0 else "immediate"
                    self.apps[app_name] = data
            self.save_apps()

    def save_apps(self):
        with open(APPS_FILE, "w", encoding="utf-8") as f:
            json.dump(self.apps, f, indent=4)

    def move_app_up(self, app_name):
        keys = list(self.apps.keys())
        idx = keys.index(app_name)
        if idx > 0:
            keys[idx], keys[idx-1] = keys[idx-1], keys[idx]
            self.apps = {k: self.apps[k] for k in keys}
            self.save_apps()
            self.setup_ui()
            self.tabview.set(self.tr("tab_set"))

    def move_app_down(self, app_name):
        keys = list(self.apps.keys())
        idx = keys.index(app_name)
        if idx < len(keys) - 1:
            keys[idx], keys[idx+1] = keys[idx+1], keys[idx]
            self.apps = {k: self.apps[k] for k in keys}
            self.save_apps()
            self.setup_ui()
            self.tabview.set(self.tr("tab_set"))

    def open_folder(self, path):
        folder = os.path.dirname(path)
        if os.path.exists(folder):
            try:
                os.startfile(folder)
            except Exception:
                pass

    def get_current_airac(self):
        try:
            base_date = datetime.datetime(2024, 1, 25)
            now = datetime.datetime.utcnow()
            days_diff = (now - base_date).days
            cycles_diff = days_diff // 28
            curr_date = base_date + datetime.timedelta(days=cycles_diff * 28)
            
            year = curr_date.year
            temp_date = curr_date
            cycle_num = 1
            while True:
                temp_date -= datetime.timedelta(days=28)
                if temp_date.year < year:
                    break
                cycle_num += 1
            return f"{str(year)[-2:]}{cycle_num:02d}"
        except Exception:
            return "N/A"

    def get_installed_airac(self):
        community_path = self.saved_states.get("_community_path", "")
        if os.path.exists(community_path) and os.path.isdir(community_path):
            try:
                for item in os.listdir(community_path):
                    if "navigraph" in item.lower():
                        manifest_path = os.path.join(community_path, item, "manifest.json")
                        if os.path.exists(manifest_path):
                            try:
                                with open(manifest_path, "r", encoding="utf-8") as f:
                                    data = json.load(f)
                                    title = data.get("title", "")
                                    if "AIRAC" in title or "Navdata" in title:
                                        match = re.search(r'\d{4}', title)
                                        if match:
                                            return match.group(0)
                            except Exception:
                                pass
            except Exception:
                pass
        return ""

    # --- LOGIKA PROFILŮ (EXTERNÍ APLIKACE) ---
    def apply_profile(self, profile_name):
        if profile_name == self.tr("default_profile") or profile_name not in self.profiles:
            return
        
        saved_apps_in_profile = self.profiles[profile_name]
        for app_name, var in self.checkboxes.items():
            if app_name in saved_apps_in_profile:
                var.set("on")
            else:
                var.set("off")
        
        self.saved_states["_last_profile"] = profile_name
        self.save_config()

    def update_profile(self):
        curr_prof = self.profile_var.get()
        if curr_prof != self.tr("default_profile") and curr_prof in self.profiles:
            active_apps = [name for name, var in self.checkboxes.items() if var.get() == "on"]
            self.profiles[curr_prof] = active_apps
            self.save_config()
            
            self.prof_update_btn.configure(text="✓", fg_color="#32a852")
            self.after(1000, lambda: self.prof_update_btn.configure(text="🔄", fg_color="#d98218"))

    def save_profile(self):
        dialog = ctk.CTkInputDialog(text=self.tr("profile_prompt_text"), title=self.tr("profile_prompt_title"))
        prof_name = dialog.get_input()
        if prof_name:
            prof_name = prof_name.strip()
            if prof_name:
                active_apps = [name for name, var in self.checkboxes.items() if var.get() == "on"]
                self.profiles[prof_name] = active_apps
                self.saved_states["_last_profile"] = prof_name
                self.save_config()
                self.setup_ui()

    def delete_profile(self):
        curr_prof = self.profile_var.get()
        if curr_prof != self.tr("default_profile") and curr_prof in self.profiles:
            del self.profiles[curr_prof]
            self.saved_states["_last_profile"] = self.tr("default_profile")
            self.save_config()
            self.setup_ui()

    # --- LOGIKA PROFILŮ (EXE.XML) ---
    def apply_exe_profile(self, profile_name):
        if profile_name == self.tr("default_profile") or profile_name not in self.exe_profiles:
            return
            
        saved_addons = self.exe_profiles[profile_name]
        for unique_key, data in self.exe_checkboxes.items():
            var = data["var"]
            node = data["node"]
            
            if unique_key in saved_addons:
                var.set("on")
            else:
                var.set("off")
                
            if self.current_xml_tree is not None and self.current_xml_path != "":
                self.toggle_exe_xml(node, var, self.current_xml_path, self.current_xml_tree)
            
        self.saved_states["_last_exe_profile"] = profile_name
        self.save_config()

    def update_exe_profile(self):
        curr_prof = self.exe_profile_var.get()
        if curr_prof != self.tr("default_profile") and curr_prof in self.exe_profiles:
            active_addons = [key for key, data in self.exe_checkboxes.items() if data["var"].get() == "on"]
            self.exe_profiles[curr_prof] = active_addons
            self.save_config()
            
            self.exe_update_btn.configure(text="✓", fg_color="#32a852")
            self.after(1000, lambda: self.exe_update_btn.configure(text="🔄", fg_color="#d98218"))

    def save_exe_profile(self):
        dialog = ctk.CTkInputDialog(text=self.tr("profile_prompt_text"), title=self.tr("profile_prompt_title"))
        prof_name = dialog.get_input()
        if prof_name:
            prof_name = prof_name.strip()
            if prof_name:
                active_addons = [key for key, data in self.exe_checkboxes.items() if data["var"].get() == "on"]
                self.exe_profiles[prof_name] = active_addons
                self.saved_states["_last_exe_profile"] = prof_name
                self.save_config()
                self.build_exe_tab()

    def delete_exe_profile(self):
        curr_prof = self.exe_profile_var.get()
        if curr_prof != self.tr("default_profile") and curr_prof in self.exe_profiles:
            del self.exe_profiles[curr_prof]
            self.saved_states["_last_exe_profile"] = self.tr("default_profile")
            self.save_config()
            self.build_exe_tab()

    def get_exe_xml_path(self):
        sim_v = self.saved_states.get("_sim_version", "MSFS 2024")
        sim_p = self.saved_states.get("_sim_platform", "Steam")
        
        roaming = os.environ.get('APPDATA', '')
        local = os.environ.get('LOCALAPPDATA', '')

        if sim_v == "MSFS 2020":
            if sim_p == "Steam":
                return os.path.join(roaming, "Microsoft Flight Simulator", "exe.xml")
            else:
                return os.path.join(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "exe.xml")
        else:
            if sim_p == "Steam":
                return os.path.join(roaming, "Microsoft Flight Simulator 2024", "exe.xml")
            else:
                return os.path.join(local, "Packages", "Microsoft.FlightSimulator2024_8wekyb3d8bbwe", "LocalCache", "exe.xml")

    # --- PŘEJMENOVÁNÍ EXE DOPLŇKU (Pouze v aplikaci) ---
    def rename_exe_addon(self, unique_key, original_name):
        dialog = ctk.CTkInputDialog(text=self.tr("exe_rename_prompt"), title=self.tr("exe_rename_title"))
        new_name = dialog.get_input()
        
        if new_name is not None:
            new_name = new_name.strip()
            if new_name == "" or new_name == original_name:
                if unique_key in self.exe_custom_names:
                    del self.exe_custom_names[unique_key]
            else:
                self.exe_custom_names[unique_key] = new_name
                
            self.save_config()
            self.build_exe_tab()

    # --- ZÁLOŽKY UI ---
    def build_exe_tab(self):
        for widget in self.tab_exe.winfo_children():
            widget.destroy()
            
        xml_path = self.get_exe_xml_path()
        self.current_xml_path = xml_path
        
        title_lbl = ctk.CTkLabel(self.tab_exe, text=self.tr("exe_title"), font=("Segoe UI", 20, "bold"))
        title_lbl.pack(pady=(10, 5))
        
        prof_frame = ctk.CTkFrame(self.tab_exe, fg_color="transparent")
        prof_frame.pack(pady=(0, 10))
        
        prof_lbl = ctk.CTkLabel(prof_frame, text=self.tr("exe_profile_label"), font=("Segoe UI", 13, "bold"))
        prof_lbl.pack(side="left", padx=(0, 10))

        prof_list = [self.tr("default_profile")] + list(self.exe_profiles.keys())
        last_prof = self.saved_states.get("_last_exe_profile", self.tr("default_profile"))
        if last_prof not in prof_list:
            last_prof = self.tr("default_profile")

        self.exe_profile_var = ctk.StringVar(value=last_prof)
        prof_menu = ctk.CTkOptionMenu(prof_frame, values=prof_list, variable=self.exe_profile_var, command=self.apply_exe_profile, width=140)
        prof_menu.pack(side="left", padx=(0, 10))

        self.exe_update_btn = ctk.CTkButton(prof_frame, text="🔄", width=30, fg_color="#d98218", hover_color="#b36b12", command=self.update_exe_profile)
        self.exe_update_btn.pack(side="left", padx=(0, 5))
        ToolTip(self.exe_update_btn, self.tr("tt_update"))

        prof_save_btn = ctk.CTkButton(prof_frame, text="➕", width=30, fg_color="#1f6aa5", command=self.save_exe_profile)
        prof_save_btn.pack(side="left", padx=(0, 5))
        ToolTip(prof_save_btn, self.tr("tt_add"))

        prof_del_btn = ctk.CTkButton(prof_frame, text="🗑", width=30, fg_color="#c93434", hover_color="#992626", command=self.delete_exe_profile)
        prof_del_btn.pack(side="left")
        ToolTip(prof_del_btn, self.tr("tt_delete"))
        
        path_lbl = ctk.CTkLabel(self.tab_exe, text=xml_path, font=("Segoe UI", 10), text_color="gray")
        path_lbl.pack(pady=(0, 5))
        
        hint_lbl = ctk.CTkLabel(self.tab_exe, text=self.tr("exe_rename_hint"), font=("Segoe UI", 11, "italic"), text_color="#a0a0a0")
        hint_lbl.pack(pady=(0, 10))
        
        if not os.path.exists(xml_path):
            err_lbl = ctk.CTkLabel(self.tab_exe, text=self.tr("exe_not_found"), text_color="#ff4d4d", font=("Segoe UI", 14, "bold"))
            err_lbl.pack(pady=20)
            return
            
        scroll_frame = ctk.CTkScrollableFrame(self.tab_exe, height=600)
        scroll_frame.pack(fill="x", padx=10, pady=5)
        
        self.exe_checkboxes = {}
        
        try:
            tree = ET.parse(xml_path)
            self.current_xml_tree = tree
            root = tree.getroot()
            
            for i, addon in enumerate(root.findall('Launch.Addon')):
                path_node = addon.find('Path')
                addon_path = path_node.text if path_node is not None and path_node.text else ""

                name_node = addon.find('Name')
                if name_node is not None and name_node.text:
                    original_name = name_node.text
                elif addon_path:
                    original_name = os.path.basename(addon_path)
                else:
                    continue

                # Klíč odvozený z cesty je stabilní i po přeuspořádání exe.xml samotným MSFS.
                # Na index v souboru se spoléháme jen u položek bez cesty.
                unique_key = addon_path if addon_path else f"{original_name}_{i}"
                
                disabled_node = addon.find('Disabled')
                is_disabled = False
                if disabled_node is not None and disabled_node.text and disabled_node.text.lower() == 'true':
                    is_disabled = True
                    
                var = ctk.StringVar(value="off" if is_disabled else "on")
                self.exe_checkboxes[unique_key] = {"var": var, "node": addon}
                
                display_name = self.exe_custom_names.get(unique_key, original_name)
                
                row_frame = ctk.CTkFrame(scroll_frame, fg_color="transparent")
                row_frame.pack(fill="x", pady=5)
                
                switch = ctk.CTkSwitch(row_frame, text=display_name, variable=var, onvalue="on", offvalue="off", font=("Segoe UI", 14),
                                       command=lambda a=addon, v=var: self.toggle_exe_xml(a, v, self.current_xml_path, self.current_xml_tree))
                switch.pack(side="left", padx=10)
                
                edit_btn = ctk.CTkButton(row_frame, text="✏️", width=30, fg_color="transparent", hover_color="#333333", 
                                         command=lambda k=unique_key, o=original_name: self.rename_exe_addon(k, o))
                edit_btn.pack(side="left", padx=(5, 0))
                ToolTip(edit_btn, self.tr("exe_rename_tt"))
                
            if last_prof != self.tr("default_profile"):
                self.apply_exe_profile(last_prof)
                
        except Exception as e:
            err_lbl = ctk.CTkLabel(self.tab_exe, text=f"{self.tr('exe_error')}\n{str(e)}", text_color="#ff4d4d")
            err_lbl.pack(pady=20)

    def toggle_exe_xml(self, addon_node, var, xml_path, tree):
        if tree is None or not xml_path:
            return
            
        dir_name = os.path.dirname(xml_path)
        backup_path = os.path.join(dir_name, "exe_FlightOpsHub_backup.xml")
        
        if not os.path.exists(backup_path):
            try:
                shutil.copy2(xml_path, backup_path)
            except Exception:
                pass

        disabled_node = addon_node.find('Disabled')
        if disabled_node is None:
            disabled_node = ET.SubElement(addon_node, 'Disabled')
            
        disabled_node.text = 'False' if var.get() == "on" else 'True'
        
        try:
            if hasattr(ET, 'indent'):
                ET.indent(tree, space="  ", level=0)
            tree.write(xml_path, encoding="utf-8", xml_declaration=True)
        except Exception:
            pass

    # --- ZÁLOŽKA SCENÉRIE ---
    def build_scenery_tab(self):
        for widget in self.tab_scenery.winfo_children():
            widget.destroy()

        self._scenery_checkbox_vars = {}

        if self._addon_apply_active:
            # Sken slozek behem probihajiciho presunu (viz _scenery_apply/_aircraft_apply)
            # by mohl narazit na soubory, ktere prave mizi/vznikaji na pozadi - misto
            # prekreslení seznamu jen ukazeme, ze appka je zaneprazdnena. Widgety pro
            # progress bar/stavovy popisek musi existovat vzdy, jinak by na ne
            # _on_scenery_apply_progress / _finish_scenery_apply nemely kam sahnout.
            busy_lbl = ctk.CTkLabel(self.tab_scenery, text=self.tr("addon_apply_busy"), font=("Segoe UI", 13))
            busy_lbl.pack(pady=(20, 5))
            self.scenery_status_label = ctk.CTkLabel(self.tab_scenery, text="", font=("Segoe UI", 12), text_color="#a0a0a0")
            self.scenery_status_label.pack(pady=(0, 5))
            self.scenery_progress_bar = ctk.CTkProgressBar(self.tab_scenery)
            self.scenery_progress_bar.pack(fill="x", padx=20, pady=(0, 10))
            return

        community_path = self.saved_states.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            msg = ctk.CTkLabel(self.tab_scenery, text=self.tr("scenery_no_community"), font=("Segoe UI", 13))
            msg.pack(pady=20)
            return

        disabled_locations = self._resolve_disabled_locations(community_path)
        self._scenery_all_records = scenery_data.scan_scenery_packages(community_path, disabled_locations)

        toolbar = ctk.CTkFrame(self.tab_scenery, fg_color="transparent")
        toolbar.pack(fill="x", padx=10, pady=(10, 5))

        self.scenery_select_on_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_select_all_on"), width=110,
                                                    fg_color="#454545", hover_color="#333333",
                                                    command=lambda: self._scenery_set_all(True))
        self.scenery_select_on_btn.pack(side="left", padx=(0, 5))

        self.scenery_select_off_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_select_all_off"), width=110,
                                                     fg_color="#454545", hover_color="#333333",
                                                     command=lambda: self._scenery_set_all(False))
        self.scenery_select_off_btn.pack(side="left", padx=(0, 5))

        self.scenery_refresh_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_refresh_btn"), width=90,
                                                  fg_color="#454545", hover_color="#333333",
                                                  command=self._scenery_refresh)
        self.scenery_refresh_btn.pack(side="left", padx=(0, 5))

        self.scenery_apply_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_apply_btn"), width=90,
                                               fg_color="#1f6aa5", hover_color="#144870",
                                               command=self._scenery_apply)
        self.scenery_apply_btn.pack(side="left")

        self.scenery_status_label = ctk.CTkLabel(self.tab_scenery, text="", font=("Segoe UI", 12), text_color="#a0a0a0")
        self.scenery_status_label.pack(pady=(0, 5))

        self.scenery_progress_bar = ctk.CTkProgressBar(self.tab_scenery)
        self.scenery_progress_bar.set(0)

        if not self._scenery_all_records:
            empty_lbl = ctk.CTkLabel(self.tab_scenery, text="—", font=("Segoe UI", 13))
            empty_lbl.pack(pady=20)
            return

        scroll = ctk.CTkScrollableFrame(self.tab_scenery, height=500)
        scroll.pack(fill="both", expand=True, padx=10, pady=5)

        by_continent = {}
        for record in self._scenery_all_records:
            by_continent.setdefault(record["continent"], []).append(record)

        for continent_key in scenery_data.CONTINENT_ORDER:
            items = by_continent.get(continent_key)
            if items:
                self._build_continent_section(scroll, continent_key, items)

    def _scenery_group_counts(self, items):
        enabled_count = sum(
            1 for r in items if self._scenery_pending.get(r["folder_name"], r["enabled"])
        )
        return enabled_count, len(items)

    def _build_continent_section(self, parent, continent_key, items):
        continent_label = self.tr(f"continent_{continent_key}")
        expanded = continent_key in self._scenery_expanded
        enabled_count, total = self._scenery_group_counts(items)

        header_frame = ctk.CTkFrame(parent, fg_color="transparent")
        header_frame.pack(fill="x", pady=(8, 0))

        self._build_group_checkbox(header_frame, items)

        header_btn = ctk.CTkButton(
            header_frame, text=f"{'▼' if expanded else '▶'} {continent_label} ({enabled_count}/{total})",
            anchor="w", fg_color="#333333", hover_color="#444444",
            command=lambda: self._toggle_continent(continent_key),
        )
        header_btn.pack(side="left", fill="x", expand=True)

        if not expanded:
            return

        body = ctk.CTkFrame(parent, fg_color="transparent")
        body.pack(fill="x", padx=(15, 0))

        by_country = {}
        for record in items:
            by_country.setdefault(record["country"] or self.tr("scenery_other_country"), []).append(record)

        for country in sorted(by_country.keys()):
            country_items = by_country[country]
            country_key = f"{continent_key}::{country}"
            country_expanded = country_key in self._scenery_expanded_countries
            country_enabled, country_total = self._scenery_group_counts(country_items)

            country_frame = ctk.CTkFrame(body, fg_color="transparent")
            country_frame.pack(fill="x", pady=(6, 0))

            self._build_group_checkbox(country_frame, country_items)

            country_btn = ctk.CTkButton(
                country_frame, text=f"{'▼' if country_expanded else '▶'} {country} ({country_enabled}/{country_total})",
                anchor="w", fg_color="#2b2b2b", hover_color="#3a3a3a", font=("Segoe UI", 13, "bold"),
                command=lambda k=country_key: self._toggle_country(k),
            )
            country_btn.pack(side="left", fill="x", expand=True)

            if not country_expanded:
                continue

            for record in sorted(country_items, key=lambda r: r["display_name"].lower()):
                folder_name = record["folder_name"]
                desired = self._scenery_pending.get(folder_name, record["enabled"])
                var = ctk.StringVar(value="on" if desired else "off")
                self._scenery_checkbox_vars[folder_name] = var

                cb = ctk.CTkCheckBox(body, text=record["display_name"], variable=var,
                                     onvalue="on", offvalue="off", font=("Segoe UI", 13),
                                     command=lambda name=folder_name: self._scenery_on_toggle(name))
                cb.pack(anchor="w", padx=25, pady=1)

    def _scenery_on_toggle(self, folder_name):
        var = self._scenery_checkbox_vars[folder_name]
        self._scenery_pending[folder_name] = (var.get() == "on")

    def _toggle_continent(self, continent_key):
        if continent_key in self._scenery_expanded:
            self._scenery_expanded.discard(continent_key)
        else:
            self._scenery_expanded.add(continent_key)
        self.build_scenery_tab()

    def _toggle_country(self, country_key):
        if country_key in self._scenery_expanded_countries:
            self._scenery_expanded_countries.discard(country_key)
        else:
            self._scenery_expanded_countries.add(country_key)
        self.build_scenery_tab()

    def _scenery_set_all(self, enabled):
        for record in self._scenery_all_records:
            self._scenery_pending[record["folder_name"]] = enabled
        self.build_scenery_tab()

    def _build_group_checkbox(self, parent, items):
        # Jeden checkbox misto dvou tlacitek - hromadne zapne/vypne vse ve
        # skupine (kontinent/stat), barva rovnou ukazuje aktualni stav:
        # zelena = vse zapnuto, zluta = castecne, cervena = vse vypnuto.
        enabled_count, total = self._scenery_group_counts(items)
        all_on = total > 0 and enabled_count == total
        all_off = enabled_count == 0

        if all_on:
            color = "#2d8659"
            var_value = "on"
        elif all_off:
            color = "#8a2e2e"
            var_value = "off"
        else:
            color = "#c9a227"
            var_value = "on"

        var = ctk.StringVar(value=var_value)
        cb = ctk.CTkCheckBox(
            parent, text="", variable=var, onvalue="on", offvalue="off", width=20,
            fg_color=color, hover_color=color, border_color=color,
            command=lambda: self._scenery_set_group(items, not all_on),
        )
        cb.pack(side="left", padx=(0, 5))
        ToolTip(cb, self.tr("scenery_group_toggle_tt"))

    def _scenery_set_group(self, items, enabled):
        for record in items:
            self._scenery_pending[record["folder_name"]] = enabled
        self.build_scenery_tab()

    def _scenery_refresh(self):
        self._scenery_pending = {}
        self.build_scenery_tab()

    def _run_apply_in_background(self, worker_fn, on_progress, on_finish):
        # worker_fn(progress_callback) bezi ve vlastnim vlaknu (presuny
        # mezi disky mohou trvat minuty - viz scenery_data.move_package).
        # Kazdy progress_callback i vysledek se marshaluji zpet na Tk hlavni
        # vlakno pres self.after(0, ...), stejny vzor jako u Smart Launch.
        def _worker():
            def _progress(idx, total, name, copied, total_bytes):
                self.after(0, lambda: on_progress(idx, total, name, copied, total_bytes))

            try:
                results = worker_fn(_progress)
            except Exception as e:
                results = [("?", False, str(e))]
            self.after(0, lambda: on_finish(results))

        threading.Thread(target=_worker, daemon=True).start()

    def _scenery_apply(self):
        if self._addon_apply_active:
            return

        community_path = self.saved_states.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return

        self._addon_apply_active = True
        disabled_locations = self._resolve_disabled_locations(community_path)

        # Bereme zamer pro VSECHNY zname balicky, ne jen prave vykreslene
        # checkboxy - ve sbalenem kontinentu zadne widgety nejsou, ale
        # pripadna zmena tam uz je zaznamenana v self._scenery_pending.
        desired_states = {}
        for record in self._scenery_all_records:
            name = record["folder_name"]
            desired_states[name] = self._scenery_pending.get(name, record["enabled"])

        for btn in (self.scenery_apply_btn, self.scenery_select_on_btn,
                    self.scenery_select_off_btn, self.scenery_refresh_btn):
            btn.configure(state="disabled")

        self.scenery_progress_bar.set(0)
        self.scenery_progress_bar.pack(fill="x", padx=10, pady=(0, 5))
        self.scenery_status_label.configure(text=self.tr("scenery_applying"))

        def _worker_fn(progress_callback):
            return scenery_data.apply_package_changes(community_path, disabled_locations, desired_states,
                                                       progress_callback=progress_callback)

        self._run_apply_in_background(_worker_fn, self._on_scenery_apply_progress,
                                      lambda results: self._finish_scenery_apply(results, desired_states))

    def _on_scenery_apply_progress(self, idx, total, name, copied, total_bytes):
        fraction = (idx + (copied / total_bytes if total_bytes else 1)) / max(total, 1)
        self.scenery_progress_bar.set(min(1.0, fraction))
        self.scenery_status_label.configure(text=self.tr("scenery_applying_progress").format(idx + 1, total, name))

    def _finish_scenery_apply(self, results, desired_states):
        enabled_count = sum(1 for name, ok, _ in results if ok and desired_states.get(name))
        disabled_count = sum(1 for name, ok, _ in results if ok and not desired_states.get(name))
        errors = [(name, err) for name, ok, err in results if not ok]

        if errors:
            print("Scenery apply errors:", errors)

        status = self.tr("scenery_apply_result").format(enabled_count, disabled_count)
        if errors:
            status += self.tr("scenery_apply_errors").format(len(errors))

        self._addon_apply_active = False
        self._scenery_pending = {}
        self.build_scenery_tab()
        self.scenery_status_label.configure(text=status)

    # --- ZÁLOŽKA LETADLA ---
    def build_aircraft_tab(self):
        for widget in self.tab_aircraft.winfo_children():
            widget.destroy()

        self._aircraft_checkbox_vars = {}

        if self._addon_apply_active:
            # Stejny duvod jako v build_scenery_tab - nescanovat souborovy strom,
            # zatimco na pozadi bezi presun (scenerie i letadla sdili _addon_apply_active,
            # protoze obe pracuji nad stejnou Community slozkou).
            busy_lbl = ctk.CTkLabel(self.tab_aircraft, text=self.tr("addon_apply_busy"), font=("Segoe UI", 13))
            busy_lbl.pack(pady=(20, 5))
            self.aircraft_status_label = ctk.CTkLabel(self.tab_aircraft, text="", font=("Segoe UI", 12), text_color="#a0a0a0")
            self.aircraft_status_label.pack(pady=(0, 5))
            self.aircraft_progress_bar = ctk.CTkProgressBar(self.tab_aircraft)
            self.aircraft_progress_bar.pack(fill="x", padx=20, pady=(0, 10))
            return

        community_path = self.saved_states.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            msg = ctk.CTkLabel(self.tab_aircraft, text=self.tr("scenery_no_community"), font=("Segoe UI", 13))
            msg.pack(pady=20)
            return

        disabled_locations = self._resolve_disabled_locations(community_path)
        aircraft_records, livery_records = scenery_data.scan_aircraft_and_liveries(community_path, disabled_locations)
        self._aircraft_all_records = aircraft_records + livery_records

        notice_lbl = ctk.CTkLabel(self.tab_aircraft, text=self.tr("aircraft_experimental_notice"),
                                  font=("Segoe UI", 12, "bold"), text_color="#e0a52c", wraplength=440)
        notice_lbl.pack(pady=(10, 0), padx=10)

        toolbar = ctk.CTkFrame(self.tab_aircraft, fg_color="transparent")
        toolbar.pack(fill="x", padx=10, pady=(10, 5))

        self.aircraft_select_on_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_select_all_on"), width=110,
                                                     fg_color="#454545", hover_color="#333333",
                                                     command=lambda: self._aircraft_set_all(True))
        self.aircraft_select_on_btn.pack(side="left", padx=(0, 5))

        self.aircraft_select_off_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_select_all_off"), width=110,
                                                      fg_color="#454545", hover_color="#333333",
                                                      command=lambda: self._aircraft_set_all(False))
        self.aircraft_select_off_btn.pack(side="left", padx=(0, 5))

        self.aircraft_refresh_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_refresh_btn"), width=90,
                                                   fg_color="#454545", hover_color="#333333",
                                                   command=self._aircraft_refresh)
        self.aircraft_refresh_btn.pack(side="left", padx=(0, 5))

        self.aircraft_apply_btn = ctk.CTkButton(toolbar, text=self.tr("scenery_apply_btn"), width=90,
                                                fg_color="#1f6aa5", hover_color="#144870",
                                                command=self._aircraft_apply)
        self.aircraft_apply_btn.pack(side="left")

        hint_lbl = ctk.CTkLabel(self.tab_aircraft, text=self.tr("aircraft_grouping_hint"),
                                font=("Segoe UI", 11, "italic"), text_color="#a0a0a0", wraplength=440)
        hint_lbl.pack(pady=(0, 5), padx=10)

        self.aircraft_status_label = ctk.CTkLabel(self.tab_aircraft, text="", font=("Segoe UI", 12), text_color="#a0a0a0")
        self.aircraft_status_label.pack(pady=(0, 5))

        self.aircraft_progress_bar = ctk.CTkProgressBar(self.tab_aircraft)
        self.aircraft_progress_bar.set(0)

        if not self._aircraft_all_records:
            empty_lbl = ctk.CTkLabel(self.tab_aircraft, text="—", font=("Segoe UI", 13))
            empty_lbl.pack(pady=20)
            return

        scroll = ctk.CTkScrollableFrame(self.tab_aircraft, height=460)
        scroll.pack(fill="both", expand=True, padx=10, pady=5)

        aircraft_folders = {r["folder_name"] for r in aircraft_records}

        # Liverky, ktere se povedlo spolehlive dohledat k letadlu pres
        # base_container (viz scenery_data.scan_aircraft_and_liveries), se
        # zobrazi vnorene pod něj - zbytek zustava v sekci "nepriřazené"
        # pod vyvojarem (žádné hádání podle textu).
        liveries_by_aircraft = {}
        unassigned_liveries = []
        for r in livery_records:
            parent = r.get("parent_aircraft")
            if parent and parent in aircraft_folders:
                liveries_by_aircraft.setdefault(parent, []).append(r)
            else:
                unassigned_liveries.append(r)

        other_key = self.tr("aircraft_other_developer")
        by_developer = {}
        for r in aircraft_records:
            by_developer.setdefault(r["developer"] or other_key, {"aircraft": [], "unassigned_liveries": []})["aircraft"].append(r)
        for r in unassigned_liveries:
            by_developer.setdefault(r["developer"] or other_key, {"aircraft": [], "unassigned_liveries": []})["unassigned_liveries"].append(r)

        developers = sorted(by_developer.keys(), key=lambda d: (d == other_key, d.lower()))

        for developer in developers:
            self._build_developer_section(scroll, developer, by_developer[developer], liveries_by_aircraft)

    def _build_developer_section(self, parent, developer, groups, liveries_by_aircraft):
        nested_livery_count = sum(len(liveries_by_aircraft.get(a["folder_name"], [])) for a in groups["aircraft"])
        total = len(groups["aircraft"]) + len(groups["unassigned_liveries"]) + nested_livery_count
        expanded = developer in self._aircraft_expanded

        header_btn = ctk.CTkButton(
            parent, text=f"{'▼' if expanded else '▶'} {developer} ({total})",
            anchor="w", fg_color="#333333", hover_color="#444444",
            command=lambda: self._toggle_developer(developer),
        )
        header_btn.pack(fill="x", pady=(8, 0))

        if not expanded:
            return

        body = ctk.CTkFrame(parent, fg_color="transparent")
        body.pack(fill="x", padx=(15, 0))

        if groups["aircraft"]:
            section_id = f"{developer}::aircraft"
            section_expanded = section_id in self._aircraft_expanded_sections

            section_btn = ctk.CTkButton(
                body, text=f"{'▼' if section_expanded else '▶'} {self.tr('aircraft_section_aircraft')} ({len(groups['aircraft'])})",
                anchor="w", fg_color="#2b2b2b", hover_color="#3a3a3a", font=("Segoe UI", 13, "bold"),
                command=lambda sid=section_id: self._toggle_aircraft_section(sid),
            )
            section_btn.pack(fill="x", pady=(6, 0))

            if section_expanded:
                for record in sorted(groups["aircraft"], key=lambda r: r["display_name"].lower()):
                    self._build_aircraft_checkbox(body, record, padx=25)

                    for livery in sorted(liveries_by_aircraft.get(record["folder_name"], []), key=lambda r: r["display_name"].lower()):
                        self._build_aircraft_checkbox(body, livery, padx=45)

        if groups["unassigned_liveries"]:
            section_id = f"{developer}::liveries"
            section_expanded = section_id in self._aircraft_expanded_sections

            section_btn = ctk.CTkButton(
                body, text=f"{'▼' if section_expanded else '▶'} {self.tr('aircraft_section_liveries')} ({len(groups['unassigned_liveries'])})",
                anchor="w", fg_color="#2b2b2b", hover_color="#3a3a3a", font=("Segoe UI", 13, "bold"),
                command=lambda sid=section_id: self._toggle_aircraft_section(sid),
            )
            section_btn.pack(fill="x", pady=(6, 0))

            if section_expanded:
                for record in sorted(groups["unassigned_liveries"], key=lambda r: r["display_name"].lower()):
                    self._build_aircraft_checkbox(body, record, padx=25)

    def _build_aircraft_checkbox(self, parent, record, padx):
        folder_name = record["folder_name"]
        desired = self._aircraft_pending.get(folder_name, record["enabled"])
        var = ctk.StringVar(value="on" if desired else "off")
        self._aircraft_checkbox_vars[folder_name] = var

        cb = ctk.CTkCheckBox(parent, text=record["display_name"], variable=var,
                             onvalue="on", offvalue="off", font=("Segoe UI", 13),
                             command=lambda name=folder_name: self._aircraft_on_toggle(name))
        cb.pack(anchor="w", padx=padx, pady=1)

    def _aircraft_on_toggle(self, folder_name):
        var = self._aircraft_checkbox_vars[folder_name]
        self._aircraft_pending[folder_name] = (var.get() == "on")

    def _toggle_developer(self, developer):
        if developer in self._aircraft_expanded:
            self._aircraft_expanded.discard(developer)
        else:
            self._aircraft_expanded.add(developer)
        self.build_aircraft_tab()

    def _toggle_aircraft_section(self, section_id):
        if section_id in self._aircraft_expanded_sections:
            self._aircraft_expanded_sections.discard(section_id)
        else:
            self._aircraft_expanded_sections.add(section_id)
        self.build_aircraft_tab()

    def _aircraft_set_all(self, enabled):
        for record in self._aircraft_all_records:
            self._aircraft_pending[record["folder_name"]] = enabled
        self.build_aircraft_tab()

    def _aircraft_refresh(self):
        self._aircraft_pending = {}
        self.build_aircraft_tab()

    def _aircraft_apply(self):
        if self._addon_apply_active:
            return

        community_path = self.saved_states.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return

        self._addon_apply_active = True
        disabled_locations = self._resolve_disabled_locations(community_path)

        desired_states = {}
        for record in self._aircraft_all_records:
            name = record["folder_name"]
            desired_states[name] = self._aircraft_pending.get(name, record["enabled"])

        for btn in (self.aircraft_apply_btn, self.aircraft_select_on_btn,
                    self.aircraft_select_off_btn, self.aircraft_refresh_btn):
            btn.configure(state="disabled")

        self.aircraft_progress_bar.set(0)
        self.aircraft_progress_bar.pack(fill="x", padx=10, pady=(0, 5))
        self.aircraft_status_label.configure(text=self.tr("scenery_applying"))

        def _worker_fn(progress_callback):
            return scenery_data.apply_package_changes(community_path, disabled_locations, desired_states,
                                                       progress_callback=progress_callback)

        self._run_apply_in_background(_worker_fn, self._on_aircraft_apply_progress,
                                      lambda results: self._finish_aircraft_apply(results, desired_states))

    def _on_aircraft_apply_progress(self, idx, total, name, copied, total_bytes):
        fraction = (idx + (copied / total_bytes if total_bytes else 1)) / max(total, 1)
        self.aircraft_progress_bar.set(min(1.0, fraction))
        self.aircraft_status_label.configure(text=self.tr("scenery_applying_progress").format(idx + 1, total, name))

    def _finish_aircraft_apply(self, results, desired_states):
        enabled_count = sum(1 for name, ok, _ in results if ok and desired_states.get(name))
        disabled_count = sum(1 for name, ok, _ in results if ok and not desired_states.get(name))
        errors = [(name, err) for name, ok, err in results if not ok]

        if errors:
            print("Aircraft apply errors:", errors)

        status = self.tr("scenery_apply_result").format(enabled_count, disabled_count)
        if errors:
            status += self.tr("scenery_apply_errors").format(len(errors))

        self._addon_apply_active = False
        self._aircraft_pending = {}
        self.build_aircraft_tab()
        self.aircraft_status_label.configure(text=status)

    def build_main_tab(self):
        for widget in self.tab_main.winfo_children():
            widget.destroy()

        self.label = ctk.CTkLabel(self.tab_main, text=self.tr("main_title"), font=("Segoe UI", 20, "bold"))
        self.label.pack(pady=(10, 5))

        prof_frame = ctk.CTkFrame(self.tab_main, fg_color="transparent")
        prof_frame.pack(pady=(0, 10))
        
        prof_lbl = ctk.CTkLabel(prof_frame, text=self.tr("profile_label"), font=("Segoe UI", 13, "bold"))
        prof_lbl.pack(side="left", padx=(0, 10))

        prof_list = [self.tr("default_profile")] + list(self.profiles.keys())
        last_prof = self.saved_states.get("_last_profile", self.tr("default_profile"))
        if last_prof not in prof_list:
            last_prof = self.tr("default_profile")

        self.profile_var = ctk.StringVar(value=last_prof)
        prof_menu = ctk.CTkOptionMenu(prof_frame, values=prof_list, variable=self.profile_var, command=self.apply_profile, width=140)
        prof_menu.pack(side="left", padx=(0, 10))

        self.prof_update_btn = ctk.CTkButton(prof_frame, text="🔄", width=30, fg_color="#d98218", hover_color="#b36b12", command=self.update_profile)
        self.prof_update_btn.pack(side="left", padx=(0, 5))
        ToolTip(self.prof_update_btn, self.tr("tt_update"))

        prof_save_btn = ctk.CTkButton(prof_frame, text="➕", width=30, fg_color="#1f6aa5", command=self.save_profile)
        prof_save_btn.pack(side="left", padx=(0, 5))
        ToolTip(prof_save_btn, self.tr("tt_add"))

        prof_del_btn = ctk.CTkButton(prof_frame, text="🗑", width=30, fg_color="#c93434", hover_color="#992626", command=self.delete_profile)
        prof_del_btn.pack(side="left")
        ToolTip(prof_del_btn, self.tr("tt_delete"))

        current_airac = self.get_current_airac()
        installed_airac = self.get_installed_airac()
        
        airac_frame = ctk.CTkFrame(self.tab_main, fg_color="transparent")
        airac_frame.pack(pady=(0, 10))

        if not installed_airac:
            status_text = self.tr("nav_not_found").format(current_airac)
            status_color = "gray"
        elif installed_airac == current_airac:
            status_text = self.tr("nav_ok").format(installed_airac)
            status_color = "#32a852"
        else:
            status_text = self.tr("nav_old").format(installed_airac, current_airac)
            status_color = "#ff4d4d"

        self.airac_label = ctk.CTkLabel(airac_frame, text=status_text, text_color=status_color, font=("Segoe UI", 13, "bold"))
        self.airac_label.pack()

        self.scroll_frame = ctk.CTkScrollableFrame(self.tab_main, height=360)
        self.scroll_frame.pack(fill="x", padx=10, pady=5)

        self.checkboxes = {}

        for app_name in self.apps.keys():
            initial_state = self.saved_states.get(app_name, "off")
            var = ctk.StringVar(value=initial_state)
            
            cb = ctk.CTkCheckBox(self.scroll_frame, text=app_name, variable=var, onvalue="on", offvalue="off", font=("Segoe UI", 14))
            cb.pack(pady=8, padx=10, anchor="w")
            self.checkboxes[app_name] = var

        if last_prof != self.tr("default_profile"):
            self.apply_profile(last_prof)

        self.launch_btn = ctk.CTkButton(self.tab_main, text=self.tr("btn_launch"), font=("Segoe UI", 16, "bold"), height=40, command=self.launch_all)
        self.launch_btn.pack(pady=(20, 10))

    def build_settings_tab(self):
        self.editing_app = None 

        for widget in self.tab_set.winfo_children():
            widget.destroy()

        top_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        top_frame.pack(pady=(5, 5), fill="x", padx=10)

        lang_lbl = ctk.CTkLabel(top_frame, text=self.tr("lang_label"), font=("Segoe UI", 13, "bold"))
        lang_lbl.grid(row=0, column=0, sticky="w", padx=(0, 10), pady=2)
        
        lang_var = ctk.StringVar(value=self.current_lang)
        lang_menu = ctk.CTkOptionMenu(top_frame, values=["EN", "CZ", "DE", "ES", "CN"], variable=lang_var, command=self.change_language, width=80)
        lang_menu.grid(row=0, column=1, sticky="w", pady=2)

        theme_lbl = ctk.CTkLabel(top_frame, text=self.tr("theme_label"), font=("Segoe UI", 13, "bold"))
        theme_lbl.grid(row=1, column=0, sticky="w", padx=(0, 10), pady=2)

        theme_val = self.saved_states.get("_theme", "System")
        if theme_val == "Dark": t_str = self.tr("theme_dark")
        elif theme_val == "Light": t_str = self.tr("theme_light")
        else: t_str = self.tr("theme_system")

        theme_var = ctk.StringVar(value=t_str)
        theme_menu = ctk.CTkOptionMenu(top_frame, values=[self.tr("theme_dark"), self.tr("theme_light"), self.tr("theme_system")], 
                                       variable=theme_var, command=self.change_theme, width=100)
        theme_menu.grid(row=1, column=1, sticky="w", pady=2)

        sim_lbl = ctk.CTkLabel(top_frame, text=self.tr("sim_label"), font=("Segoe UI", 13, "bold"))
        sim_lbl.grid(row=2, column=0, sticky="w", padx=(0, 10), pady=2)

        self.sim_v_var = ctk.StringVar(value=self.saved_states.get("_sim_version", "MSFS 2024"))
        sim_v_menu = ctk.CTkOptionMenu(top_frame, values=["MSFS 2020", "MSFS 2024"], variable=self.sim_v_var, command=self.save_sim_settings, width=110)
        sim_v_menu.grid(row=2, column=1, sticky="w", padx=(0, 5), pady=2)

        self.sim_p_var = ctk.StringVar(value=self.saved_states.get("_sim_platform", "Steam"))
        sim_p_menu = ctk.CTkOptionMenu(top_frame, values=["Steam", "MS Store"], variable=self.sim_p_var, command=self.save_sim_settings, width=100)
        sim_p_menu.grid(row=2, column=2, sticky="w", pady=2)

        post_launch_lbl = ctk.CTkLabel(top_frame, text=self.tr("post_launch_label"), font=("Segoe UI", 13, "bold"))
        post_launch_lbl.grid(row=3, column=0, sticky="w", padx=(0, 10), pady=2)

        post_launch_labels = [self.tr("post_launch_exit"), self.tr("post_launch_watch")]
        post_launch_keys = ["exit", "stay_and_watch"]
        self.post_launch_label_to_key = dict(zip(post_launch_labels, post_launch_keys))
        post_launch_key_to_label = dict(zip(post_launch_keys, post_launch_labels))

        current_post_launch = self.saved_states.get("_post_launch_behavior", "exit")
        post_launch_var = ctk.StringVar(value=post_launch_key_to_label.get(current_post_launch, post_launch_labels[0]))
        post_launch_menu = ctk.CTkOptionMenu(top_frame, values=post_launch_labels, variable=post_launch_var,
                                             command=self.save_post_launch_setting, width=220)
        post_launch_menu.grid(row=3, column=1, columnspan=2, sticky="w", pady=2)

        ctk.CTkFrame(self.tab_set, height=2, fg_color="#333333").pack(fill="x", padx=20, pady=5)

        title_nav = ctk.CTkLabel(self.tab_set, text=self.tr("set_nav_title"), font=("Segoe UI", 16, "bold"))
        title_nav.pack(pady=(5, 0))

        nav_hint = ctk.CTkLabel(self.tab_set, text=self.tr("set_nav_hint"), font=("Segoe UI", 11), text_color="#a0a0a0")
        nav_hint.pack(pady=(0, 5))

        nav_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        nav_frame.pack(pady=5)
        
        self.nav_path = ctk.CTkEntry(nav_frame, placeholder_text=self.tr("set_nav_placeholder"), width=240)
        self.nav_path.pack(side="left", padx=(0, 10))
        
        nav_path_saved = self.saved_states.get("_community_path", "")
        if nav_path_saved:
            self.nav_path.insert(0, nav_path_saved)

        nav_browse_btn = ctk.CTkButton(nav_frame, text=self.tr("btn_browse"), width=90, fg_color="#454545", hover_color="#333333", command=self.browse_community)
        nav_browse_btn.pack(side="left")

        title_disabled = ctk.CTkLabel(self.tab_set, text=self.tr("disabled_path_title"), font=("Segoe UI", 14, "bold"))
        title_disabled.pack(pady=(15, 0))

        disabled_hint = ctk.CTkLabel(self.tab_set, text=self.tr("disabled_path_hint"), font=("Segoe UI", 11),
                                     text_color="#a0a0a0", wraplength=440)
        disabled_hint.pack(pady=(0, 5))

        disabled_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        disabled_frame.pack(pady=5)

        self.disabled_path_entry = ctk.CTkEntry(disabled_frame, placeholder_text=self.tr("disabled_path_placeholder"),
                                                 width=240, state="readonly")
        self.disabled_path_entry.pack(side="left", padx=(0, 10))
        self._refresh_disabled_path_entry()

        disabled_browse_btn = ctk.CTkButton(disabled_frame, text=self.tr("btn_browse"), width=90,
                                            fg_color="#454545", hover_color="#333333", command=self.browse_disabled_path)
        disabled_browse_btn.pack(side="left", padx=(0, 5))

        disabled_reset_btn = ctk.CTkButton(disabled_frame, text=self.tr("disabled_path_reset_btn"), width=80,
                                           fg_color="#454545", hover_color="#333333", command=self.reset_disabled_path)
        disabled_reset_btn.pack(side="left")

        ctk.CTkFrame(self.tab_set, height=2, fg_color="#333333").pack(fill="x", padx=20, pady=10)

        title_add = ctk.CTkLabel(self.tab_set, text=self.tr("set_add_title"), font=("Segoe UI", 16, "bold"))
        title_add.pack(pady=(5, 5))

        self.new_name = ctk.CTkEntry(self.tab_set, placeholder_text=self.tr("add_name_placeholder"), width=300)
        self.new_name.pack(pady=5)

        path_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        path_frame.pack(pady=5)

        self.new_path = ctk.CTkEntry(path_frame, placeholder_text=self.tr("add_path_placeholder"), width=200)
        self.new_path.pack(side="left", padx=(0, 10))

        browse_btn = ctk.CTkButton(path_frame, text=self.tr("btn_browse"), width=90, command=self.browse_file)
        browse_btn.pack(side="left")

        mode_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        mode_frame.pack(pady=5)

        mode_lbl = ctk.CTkLabel(mode_frame, text=self.tr("mode_label"), font=("Segoe UI", 13, "bold"))
        mode_lbl.pack(side="left", padx=(0, 10))

        self.mode_keys = ["immediate", "timer", "smart"]
        mode_labels = [self.tr("mode_immediate"), self.tr("mode_timer"), self.tr("mode_smart")]
        self.mode_label_to_key = dict(zip(mode_labels, self.mode_keys))
        self.mode_key_to_label = dict(zip(self.mode_keys, mode_labels))

        self.new_mode_var = ctk.StringVar(value=mode_labels[0])
        mode_menu = ctk.CTkOptionMenu(mode_frame, values=mode_labels, variable=self.new_mode_var,
                                      command=self.on_launch_mode_change, width=140)
        mode_menu.pack(side="left")

        # Kontejner zůstává v layoutu napevno zapackovaný - jen jeho obsah se
        # podle režimu zobrazuje/skrývá, aby se neposouvalo pořadí ostatních prvků.
        # width/height=1: CTkFrame bez explicitní velikosti si i prázdný (bez
        # packnutých potomků) nárokuje defaultních 250x250 px místa.
        self.delay_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent", width=1, height=1)
        self.delay_frame.pack(pady=5)

        self.new_delay = ctk.CTkEntry(self.delay_frame, placeholder_text="0", width=60)
        self.delay_lbl = ctk.CTkLabel(self.delay_frame, text=self.tr("delay_label"), font=("Segoe UI", 13))
        self.on_launch_mode_change()

        admin_frame = ctk.CTkFrame(self.tab_set, fg_color="transparent")
        admin_frame.pack(pady=(0, 5))
        self.new_admin_var = ctk.StringVar(value="off")
        self.new_admin_cb = ctk.CTkCheckBox(admin_frame, text=self.tr("run_admin_label"), variable=self.new_admin_var, onvalue="on", offvalue="off", font=("Segoe UI", 13))
        self.new_admin_cb.pack(side="left")

        self.error_label = ctk.CTkLabel(self.tab_set, text="", text_color="#ff4d4d", font=("Segoe UI", 13, "bold"))
        self.error_label.pack(pady=(0, 0))

        self.add_btn = ctk.CTkButton(self.tab_set, text=self.tr("btn_add"), fg_color="#1f6aa5", hover_color="#144870", command=self.add_new_app)
        self.add_btn.pack(pady=(5, 10))

        title_remove = ctk.CTkLabel(self.tab_set, text=self.tr("set_manage_title"), font=("Segoe UI", 16, "bold"))
        title_remove.pack(pady=(5, 5))

        self.set_scroll_frame = ctk.CTkScrollableFrame(self.tab_set, height=150)
        self.set_scroll_frame.pack(fill="x", padx=10, pady=5)

        for app_name, app_data in self.apps.items():
            row_frame = ctk.CTkFrame(self.set_scroll_frame, fg_color="transparent")
            row_frame.pack(fill="x", pady=2)
            
            admin_icon = "🛡️ " if app_data.get('admin', False) else ""
            mode_key = app_data.get("launch_mode", "timer" if app_data.get("delay", 0) > 0 else "immediate")
            if mode_key == "timer":
                mode_suffix = f" ({app_data['delay']}s)"
            elif mode_key == "smart":
                mode_suffix = f" [🎯 {self.tr('mode_smart_short')}]"
            else:
                mode_suffix = ""
            display_text = f"{admin_icon}{app_name}{mode_suffix}"
            
            lbl = ctk.CTkLabel(row_frame, text=display_text, font=("Segoe UI", 14))
            lbl.pack(side="left", padx=10)
            
            del_btn = ctk.CTkButton(row_frame, text=self.tr("btn_remove"), width=55, fg_color="#c93434", hover_color="#992626", 
                                    command=lambda name=app_name: self.remove_app(name))
            del_btn.pack(side="right", padx=(5, 10))

            edit_btn = ctk.CTkButton(row_frame, text=self.tr("btn_edit"), width=55, fg_color="#d98218", hover_color="#b36b12", 
                                     command=lambda name=app_name: self.start_edit(name))
            edit_btn.pack(side="right", padx=(5, 0))
            
            folder_btn = ctk.CTkButton(row_frame, text="📁", width=30, fg_color="#454545", hover_color="#333333",
                                       command=lambda p=app_data["path"]: self.open_folder(p))
            folder_btn.pack(side="right", padx=(10, 0))

            down_btn = ctk.CTkButton(row_frame, text="▼", width=30, fg_color="#4a4a4a", hover_color="#333333",
                                     command=lambda name=app_name: self.move_app_down(name))
            down_btn.pack(side="right", padx=(2, 0))
            
            up_btn = ctk.CTkButton(row_frame, text="▲", width=30, fg_color="#4a4a4a", hover_color="#333333",
                                   command=lambda name=app_name: self.move_app_up(name))
            up_btn.pack(side="right", padx=(10, 0))

        version_label = ctk.CTkLabel(self.tab_set, text=f"FlightOps Hub {self.app_version}", font=("Segoe UI", 11), text_color="gray")
        version_label.pack(pady=(10, 0))

    def browse_community(self):
        folderpath = filedialog.askdirectory(title=self.tr("dialog_community"))
        if folderpath:
            folderpath = os.path.normpath(folderpath)
            self.nav_path.delete(0, 'end')
            self.nav_path.insert(0, folderpath)
            
            self.saved_states["_community_path"] = folderpath
            self.save_config()
            
            self.setup_ui()
            self.tabview.set(self.tr("tab_set"))

    def _refresh_disabled_path_entry(self):
        custom_path = self.saved_states.get("_disabled_holding_path", "")
        self.disabled_path_entry.configure(state="normal")
        self.disabled_path_entry.delete(0, 'end')
        if custom_path:
            self.disabled_path_entry.insert(0, custom_path)
        self.disabled_path_entry.configure(state="readonly")

    def browse_disabled_path(self):
        folderpath = filedialog.askdirectory(title=self.tr("dialog_disabled_path"))
        if not folderpath:
            return
        folderpath = os.path.normpath(folderpath)

        community_path = self.saved_states.get("_community_path", "")
        if community_path:
            community_norm = os.path.normpath(community_path)
            a = os.path.normcase(folderpath)
            b = os.path.normcase(community_norm)
            nested = a == b or a.startswith(b + os.sep) or b.startswith(a + os.sep)
            if nested:
                messagebox.showerror(self.tr("disabled_path_title"), self.tr("disabled_path_invalid_nested"))
                return

            if os.path.splitdrive(a)[0] != os.path.splitdrive(b)[0]:
                messagebox.showwarning(self.tr("disabled_path_title"), self.tr("disabled_path_cross_drive_warning"))

        self.saved_states["_disabled_holding_path"] = folderpath
        self.save_config()
        self._refresh_disabled_path_entry()

    def reset_disabled_path(self):
        if "_disabled_holding_path" in self.saved_states:
            del self.saved_states["_disabled_holding_path"]
            self.save_config()
        self._refresh_disabled_path_entry()

    def browse_file(self):
        filepath = filedialog.askopenfilename(filetypes=[("Spustitelné soubory", "*.exe"), ("Všechny soubory", "*.*")])
        if filepath:
            filepath = os.path.normpath(filepath)
            self.new_path.delete(0, 'end')
            self.new_path.insert(0, filepath)

    def on_launch_mode_change(self, _=None):
        if self.get_selected_mode_key() == "timer":
            self.new_delay.pack(side="left", padx=(0, 10))
            self.delay_lbl.pack(side="left")
        else:
            self.new_delay.pack_forget()
            self.delay_lbl.pack_forget()

    def get_selected_mode_key(self):
        return self.mode_label_to_key.get(self.new_mode_var.get(), "immediate")

    def start_edit(self, app_name):
        self.editing_app = app_name
        app_data = self.apps[app_name]

        self.new_name.delete(0, 'end')
        self.new_name.insert(0, app_name)

        self.new_path.delete(0, 'end')
        self.new_path.insert(0, app_data["path"])

        self.new_delay.delete(0, 'end')
        self.new_delay.insert(0, str(app_data["delay"]))

        mode_key = app_data.get("launch_mode", "timer" if app_data.get("delay", 0) > 0 else "immediate")
        self.new_mode_var.set(self.mode_key_to_label.get(mode_key, self.mode_key_to_label["immediate"]))
        self.on_launch_mode_change()

        self.new_admin_var.set("on" if app_data.get("admin", False) else "off")

        self.add_btn.configure(text=self.tr("btn_save_edit"))
        self.error_label.configure(text="")

    def add_new_app(self):
        name = self.new_name.get().strip()
        path = self.new_path.get().strip()
        delay_str = self.new_delay.get().strip()
        is_admin = (self.new_admin_var.get() == "on")
        mode_key = self.get_selected_mode_key()

        if not name or not path:
            self.error_label.configure(text=self.tr("err_empty"))
            return

        delay = 0
        if mode_key == "timer":
            try:
                delay = int(delay_str)
                if delay <= 0:
                    raise ValueError
            except ValueError:
                self.error_label.configure(text=self.tr("err_delay"))
                return

        self.error_label.configure(text="")

        new_data = {"path": path, "delay": delay, "admin": is_admin, "launch_mode": mode_key}

        if self.editing_app and self.editing_app != name and self.editing_app in self.apps:
            # Přejmenování zachová pozici appky v seznamu místo přesunu na konec
            self.apps = {
                (name if key == self.editing_app else key): (new_data if key == self.editing_app else value)
                for key, value in self.apps.items()
            }
        else:
            self.apps[name] = new_data

        self.save_apps()
        
        self.setup_ui()
        self.tabview.set(self.tr("tab_set"))

    def remove_app(self, app_name):
        if app_name in self.apps:
            del self.apps[app_name]
            self.save_apps()
            
            self.setup_ui()
            self.tabview.set(self.tr("tab_set"))

    def run_app(self, path, as_admin=False):
        # Vrací PID spuštěného procesu (nebo None při selhání), aby appku
        # šlo později dohledat a ukončit (viz _terminate_launched_apps).
        if not os.path.exists(path):
            return None
        try:
            if as_admin:
                return self._run_as_admin(path)

            proc = subprocess.Popen([path], cwd=os.path.dirname(path) or None)
            return proc.pid
        except OSError as e:
            if getattr(e, "winerror", None) == 740:
                # ERROR_ELEVATION_REQUIRED - exe si žádá admina přes vlastní manifest
                return self._run_as_admin(path)
            return None
        except Exception:
            return None

    def _run_as_admin(self, path):
        try:
            sei = SHELLEXECUTEINFOW()
            sei.cbSize = ctypes.sizeof(sei)
            sei.fMask = SEE_MASK_NOCLOSEPROCESS
            sei.lpVerb = "runas"
            sei.lpFile = path
            sei.lpDirectory = os.path.dirname(path) or None
            sei.nShow = SW_SHOWNORMAL

            if not ctypes.windll.shell32.ShellExecuteExW(ctypes.byref(sei)):
                return None
            if not sei.hProcess:
                return None

            pid = ctypes.windll.kernel32.GetProcessId(sei.hProcess)
            ctypes.windll.kernel32.CloseHandle(sei.hProcess)
            return pid
        except Exception:
            return None

    def launch_all(self):
        self.saved_states["_last_profile"] = self.profile_var.get()
        for app_name, var in self.checkboxes.items():
            self.saved_states[app_name] = var.get()

        # Uložíme i pozici okna předtím, než se program "schová" a začne spouštět appky
        self.saved_states["_window_geometry"] = self.geometry()
        self.save_config()

        # PID appek, co launcher sám spustil - potřeba pro pozdější automatický úklid
        self._launched_pids = []
        self._tray_icon = None

        immediate_apps = []
        delayed_apps = []
        smart_apps = []

        for app_name, var in self.checkboxes.items():
            if var.get() == "on":
                app_data = self.apps[app_name]
                mode_key = app_data.get("launch_mode", "timer" if app_data.get("delay", 0) > 0 else "immediate")
                if mode_key == "smart":
                    smart_apps.append((app_data["path"], app_data.get("admin", False)))
                elif mode_key == "timer" and app_data["delay"] > 0:
                    delayed_apps.append((app_data["delay"], app_data["path"], app_data.get("admin", False)))
                else:
                    immediate_apps.append((app_data["path"], app_data.get("admin", False)))

        try:
            current_tasks = os.popen('tasklist').read().lower()
        except Exception:
            current_tasks = ""

        for path, as_admin in immediate_apps:
            if os.path.exists(path):
                exe_name = os.path.basename(path).lower()
                if exe_name not in current_tasks:
                    pid = self.run_app(path, as_admin)
                    if pid:
                        self._launched_pids.append(pid)

        msfs_running = "flightsimulator.exe" in current_tasks or "flightsimulator2024.exe" in current_tasks or "flightsimulator" in current_tasks

        if not msfs_running:
            sim_v = self.saved_states.get("_sim_version", "MSFS 2024")
            sim_p = self.saved_states.get("_sim_platform", "Steam")

            if sim_v == "MSFS 2020" and sim_p == "Steam":
                os.system('start steam://rungameid/1250410')
            elif sim_v == "MSFS 2020" and sim_p == "MS Store":
                os.system('start shell:AppsFolder\\Microsoft.FlightSimulator_8wekyb3d8bbwe!App')
            elif sim_v == "MSFS 2024" and sim_p == "Steam":
                os.system('start steam://rungameid/2537590')
            elif sim_v == "MSFS 2024" and sim_p == "MS Store":
                os.system('start shell:AppsFolder\\Microsoft.FlightSimulator2024_8wekyb3d8bbwe!App')
            else:
                os.system('start steam://rungameid/2537590')

        self.withdraw()
        self.update()

        # Appka se ukončí až po doběhnutí VŠECH rozjetých úloh (časovač i smart launch),
        # ne jen té první, která skončí.
        self._pending_launch_tasks = 0

        if delayed_apps:
            self._pending_launch_tasks += 1
            delayed_apps.sort(key=lambda x: x[0])
            self._launch_delayed_apps(delayed_apps, 0, 0)

        if smart_apps:
            self._pending_launch_tasks += 1
            self._start_smart_launch(smart_apps)

        if self._pending_launch_tasks == 0:
            self.destroy()

    def _task_finished(self):
        self._pending_launch_tasks -= 1
        if self._pending_launch_tasks <= 0:
            self._all_launches_done()

    def _all_launches_done(self):
        behavior = self.saved_states.get("_post_launch_behavior", "exit")
        if behavior == "stay_and_watch" and self._launched_pids:
            self._start_sim_watch()
        else:
            self._stop_tray_icon()
            self.destroy()

    def _launch_delayed_apps(self, delayed_apps, index, elapsed_time):
        # Neblokující obdoba předchozí smyčky s time.sleep() - appka tak
        # zůstane responzivní i během čekání na zpožděné spuštění doplňků.
        if index >= len(delayed_apps):
            self._task_finished()
            return

        delay, path, as_admin = delayed_apps[index]
        wait_ms = max(0, (delay - elapsed_time) * 1000)

        def do_launch():
            if os.path.exists(path):
                exe_name = os.path.basename(path).lower()
                try:
                    fresh_tasks = os.popen('tasklist').read().lower()
                except Exception:
                    fresh_tasks = ""

                if exe_name not in fresh_tasks:
                    pid = self.run_app(path, as_admin)
                    if pid:
                        self._launched_pids.append(pid)

            self._launch_delayed_apps(delayed_apps, index + 1, max(elapsed_time, delay))

        self.after(wait_ms, do_launch)

    # --- TRAY IKONKA (sdílená mezi Smart Launch čekáním a hlídáním konce simulátoru) ---
    def _ensure_tray_icon(self):
        if self._tray_icon is not None:
            return self._tray_icon

        # Volitelná vizuální indikace - pokud pystray/PIL chybí nebo selžou,
        # appka prostě poběží dál bez tray ikonky.
        try:
            import pystray
            from PIL import Image

            current_dir = os.path.dirname(os.path.abspath(__file__))
            image = Image.open(os.path.join(current_dir, "OIG3.ico"))

            icon = pystray.Icon("FlightOpsHub", image, self.tr("tray_tooltip"))
            icon.menu = pystray.Menu(pystray.MenuItem(self.tr("tray_status"), None, enabled=False))
            threading.Thread(target=icon.run, daemon=True).start()
            self._tray_icon = icon
        except Exception:
            self._tray_icon = None

        return self._tray_icon

    def _set_tray_menu(self, status_text, action_text, action_callback):
        if self._tray_icon is None:
            return
        try:
            import pystray
            self._tray_icon.title = status_text
            self._tray_icon.menu = pystray.Menu(
                pystray.MenuItem(status_text, None, enabled=False),
                pystray.MenuItem(action_text, lambda icon, item: action_callback()),
            )
        except Exception:
            pass

    def _stop_tray_icon(self):
        if self._tray_icon is not None:
            try:
                self._tray_icon.stop()
            except Exception:
                pass
            self._tray_icon = None

    # --- SMART LAUNCH (SimConnect) ---
    def _start_smart_launch(self, smart_apps):
        deadline = time.time() + 20 * 60  # 20minutová pojistka
        cancel_event = threading.Event()

        self._ensure_tray_icon()
        self._set_tray_menu(self.tr("tray_status"), self.tr("tray_cancel"), cancel_event.set)

        threading.Thread(
            target=self._smart_launch_worker,
            args=(smart_apps, deadline, cancel_event),
            daemon=True,
        ).start()

    def _fire_smart_apps(self, smart_apps):
        try:
            current_tasks = os.popen('tasklist').read().lower()
        except Exception:
            current_tasks = ""

        for path, as_admin in smart_apps:
            if os.path.exists(path):
                exe_name = os.path.basename(path).lower()
                if exe_name not in current_tasks:
                    pid = self.run_app(path, as_admin)
                    if pid:
                        self._launched_pids.append(pid)

    def _smart_launch_worker(self, smart_apps, deadline, cancel_event):
        # Běží na pozadí - žádné volání Tk widgetů kromě self.after() na konci.
        sm = None
        try:
            try:
                from SimConnect import SimConnect, AircraftRequests
            except Exception:
                return

            while sm is None:
                if cancel_event.is_set() or time.time() >= deadline:
                    return
                try:
                    sm = SimConnect()
                except Exception:
                    time.sleep(2)

            aq = AircraftRequests(sm, _time=2000)

            while True:
                if cancel_event.is_set() or time.time() >= deadline:
                    return

                title = aq.get("TITLE")
                if title is not None and title != b'':
                    if isinstance(title, bytes):
                        try:
                            title = title.decode('utf-8', errors='ignore')
                        except Exception:
                            pass

                    if isinstance(title, str) and title.strip():
                        self._fire_smart_apps(smart_apps)
                        return

                time.sleep(2)
        finally:
            if sm is not None:
                try:
                    sm.exit()
                except Exception:
                    pass
            self.after(0, self._task_finished)

    # --- HLÍDÁNÍ KONCE SIMULÁTORU (volitelný "stay_and_watch" režim) ---
    def _start_sim_watch(self):
        self._sim_confirmed_running = False
        self._watch_confirm_deadline = time.time() + 5 * 60  # 5 min grace, ať MSFS stihne naběhnout
        self._watch_job_id = None

        self._ensure_tray_icon()
        self._set_tray_menu(
            self.tr("tray_watch_status"),
            self.tr("tray_force_stop"),
            lambda: self.after(0, self._force_stop_watch),
        )

        self._poll_sim_running()

    def _poll_sim_running(self):
        try:
            current_tasks = os.popen('tasklist').read().lower()
        except Exception:
            current_tasks = ""

        msfs_running = ("flightsimulator.exe" in current_tasks
                         or "flightsimulator2024.exe" in current_tasks
                         or "flightsimulator" in current_tasks)

        if msfs_running:
            self._sim_confirmed_running = True
            self._watch_job_id = self.after(15000, self._poll_sim_running)
        elif not self._sim_confirmed_running and time.time() < self._watch_confirm_deadline:
            # MSFS ještě nenaběhl do tasklistu - dáme mu čas, neukončujeme appky předčasně
            self._watch_job_id = self.after(15000, self._poll_sim_running)
        else:
            self._terminate_launched_apps()
            self._stop_tray_icon()
            self.destroy()

    def _force_stop_watch(self):
        if self._watch_job_id is not None:
            try:
                self.after_cancel(self._watch_job_id)
            except Exception:
                pass
        self._terminate_launched_apps()
        self._stop_tray_icon()
        self.destroy()

    def _terminate_launched_apps(self):
        for pid in self._launched_pids:
            try:
                os.system(f'taskkill /PID {pid} /F /T >nul 2>&1')
            except Exception:
                pass

_ALREADY_RUNNING_MESSAGES = {
    "CZ": "FlightOps Hub už běží.\nPodívej se do systémové lišty nebo na hlavní panel.",
    "EN": "FlightOps Hub is already running.\nCheck your system tray or the taskbar.",
    "DE": "FlightOps Hub läuft bereits.\nSchau im Infobereich (Tray) oder in der Taskleiste nach.",
    "ES": "FlightOps Hub ya se está ejecutando.\nRevisa la bandeja del sistema o la barra de tareas.",
    "CN": "FlightOps Hub 已经在运行。\n请查看系统托盘或任务栏。",
}


def _get_saved_language():
    # Cte se primo ze souboru, protoze v tomto bode jeste neexistuje zadna
    # instance MSFSLauncher (a tudiz ani jeji self.tr()).
    try:
        with open(CONFIG_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        lang = data.get("_language", "EN")
        if lang in _ALREADY_RUNNING_MESSAGES:
            return lang
    except Exception:
        pass
    return "EN"


def main():
    # Handle se musi drzet po celou dobu behu appky - pri predcasnem uvolneni
    # (napr. garbage collectorem) by mutex zmizel a druha instance by se pak
    # dala spustit i pres bezici tu prvni.
    mutex_handle, already_running = acquire_single_instance_lock()
    if already_running:
        messagebox.showwarning("FlightOps Hub", _ALREADY_RUNNING_MESSAGES[_get_saved_language()])
        return

    try:
        app = MSFSLauncher()
        app.mainloop()
    except Exception as e:
        with open("crash_log.txt", "w", encoding="utf-8") as f:
            f.write("CRASH LOG:\n")
            f.write(traceback.format_exc())

if __name__ == "__main__":
    main()