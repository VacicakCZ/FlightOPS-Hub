"""Reads/writes MSFS's own AutoStart config (exe.xml) - pure logic, ported
from get_exe_xml_path/build_exe_tab's parsing/toggle_exe_xml.

Every write re-parses the file fresh and does a single backup-then-rewrite,
rather than keeping a long-lived ElementTree in memory (the old Tk app held
one on self.current_xml_tree) - simpler, and self-healing if the file
changes on disk between calls.
"""
import os
import shutil
import xml.etree.ElementTree as ET


def get_exe_xml_path(sim_version, sim_platform):
    roaming = os.environ.get("APPDATA", "")
    local = os.environ.get("LOCALAPPDATA", "")

    if sim_version == "MSFS 2020":
        if sim_platform == "Steam":
            return os.path.join(roaming, "Microsoft Flight Simulator", "exe.xml")
        return os.path.join(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "exe.xml")
    else:
        if sim_platform == "Steam":
            return os.path.join(roaming, "Microsoft Flight Simulator 2024", "exe.xml")
        return os.path.join(local, "Packages", "Microsoft.FlightSimulator2024_8wekyb3d8bbwe", "LocalCache", "exe.xml")


def _iter_addons(root):
    """Yields (unique_key, original_name, addon_node) for each <Launch.Addon>.

    The key is the addon's Path when it has one (stable across MSFS
    reordering exe.xml itself); only path-less entries fall back to an
    index-based key.
    """
    for i, addon in enumerate(root.findall("Launch.Addon")):
        path_node = addon.find("Path")
        addon_path = path_node.text if path_node is not None and path_node.text else ""

        name_node = addon.find("Name")
        if name_node is not None and name_node.text:
            original_name = name_node.text
        elif addon_path:
            original_name = os.path.basename(addon_path)
        else:
            continue

        unique_key = addon_path if addon_path else f"{original_name}_{i}"
        yield unique_key, original_name, addon


def list_addons(xml_path, custom_names):
    tree = ET.parse(xml_path)
    root = tree.getroot()
    records = []
    for unique_key, original_name, addon_node in _iter_addons(root):
        disabled_node = addon_node.find("Disabled")
        is_disabled = bool(disabled_node is not None and disabled_node.text and disabled_node.text.lower() == "true")
        records.append({
            "unique_key": unique_key,
            "original_name": original_name,
            "display_name": custom_names.get(unique_key, original_name),
            "enabled": not is_disabled,
        })
    return records


def _backup_once(xml_path):
    backup_path = os.path.join(os.path.dirname(xml_path), "exe_FlightOpsHub_backup.xml")
    if not os.path.exists(backup_path):
        try:
            shutil.copy2(xml_path, backup_path)
        except Exception:
            pass


def set_addons_enabled(xml_path, desired_states):
    """desired_states: {unique_key: bool}. Addons not present in the dict are
    left untouched. Returns True if anything was written."""
    tree = ET.parse(xml_path)
    root = tree.getroot()

    changed = False
    for unique_key, _original_name, addon_node in _iter_addons(root):
        if unique_key not in desired_states:
            continue
        disabled_node = addon_node.find("Disabled")
        if disabled_node is None:
            disabled_node = ET.SubElement(addon_node, "Disabled")
        disabled_node.text = "False" if desired_states[unique_key] else "True"
        changed = True

    if changed:
        _backup_once(xml_path)
        if hasattr(ET, "indent"):
            ET.indent(tree, space="  ", level=0)
        tree.write(xml_path, encoding="utf-8", xml_declaration=True)
    return changed
