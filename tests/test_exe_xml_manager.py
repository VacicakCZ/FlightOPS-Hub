import xml.etree.ElementTree as ET

from backend import exe_xml_manager

_SAMPLE_XML = (
    '<?xml version="1.0" encoding="UTF-8"?>'
    "<SimBase.Document>"
    "<Launch.Addon><Name>Test Addon</Name><Path>C:\\test.exe</Path><Disabled>False</Disabled></Launch.Addon>"
    "</SimBase.Document>"
)


def test_set_addons_enabled_toggles_disabled_node(tmp_path):
    xml_path = tmp_path / "exe.xml"
    xml_path.write_text(_SAMPLE_XML, encoding="utf-8")

    changed = exe_xml_manager.set_addons_enabled(str(xml_path), {"C:\\test.exe": False})

    assert changed is True
    root = ET.parse(str(xml_path)).getroot()
    assert root.find("Launch.Addon/Disabled").text == "True"


def test_set_addons_enabled_leaves_no_temp_file_behind(tmp_path):
    xml_path = tmp_path / "exe.xml"
    xml_path.write_text(_SAMPLE_XML, encoding="utf-8")

    exe_xml_manager.set_addons_enabled(str(xml_path), {"C:\\test.exe": False})

    assert not (tmp_path / "exe.xml.tmp").exists()


def test_set_addons_enabled_does_not_corrupt_existing_file_on_write_failure(tmp_path, monkeypatch):
    xml_path = tmp_path / "exe.xml"
    xml_path.write_text(_SAMPLE_XML, encoding="utf-8")
    original_bytes = xml_path.read_bytes()

    monkeypatch.setattr(
        ET.ElementTree, "write", lambda *a, **k: (_ for _ in ()).throw(OSError("disk full"))
    )
    try:
        exe_xml_manager.set_addons_enabled(str(xml_path), {"C:\\test.exe": False})
    except OSError:
        pass

    assert xml_path.read_bytes() == original_bytes
    assert not (tmp_path / "exe.xml.tmp").exists()


def test_backup_path_for_sits_next_to_the_xml_file(tmp_path):
    xml_path = str(tmp_path / "exe.xml")
    assert exe_xml_manager.backup_path_for(xml_path) == str(tmp_path / "exe_FlightOpsHub_backup.xml")


def test_has_backup_false_when_missing(tmp_path):
    xml_path = str(tmp_path / "exe.xml")
    assert exe_xml_manager.has_backup(xml_path) is False


def test_backup_once_creates_backup_only_on_first_call(tmp_path):
    xml_path = tmp_path / "exe.xml"
    xml_path.write_text("<original/>", encoding="utf-8")

    exe_xml_manager._backup_once(str(xml_path))
    backup_path = tmp_path / "exe_FlightOpsHub_backup.xml"
    assert backup_path.read_text(encoding="utf-8") == "<original/>"
    assert exe_xml_manager.has_backup(str(xml_path)) is True

    # A later call must NOT overwrite the backup with the (by then
    # modified) current file - the backup should always stay the
    # pre-first-edit snapshot.
    xml_path.write_text("<modified/>", encoding="utf-8")
    exe_xml_manager._backup_once(str(xml_path))
    assert backup_path.read_text(encoding="utf-8") == "<original/>"


def test_restore_from_backup_returns_false_when_no_backup_exists(tmp_path):
    xml_path = str(tmp_path / "exe.xml")
    assert exe_xml_manager.restore_from_backup(xml_path) is False


def test_restore_from_backup_overwrites_current_file_with_backup(tmp_path):
    xml_path = tmp_path / "exe.xml"
    xml_path.write_text("<original/>", encoding="utf-8")
    exe_xml_manager._backup_once(str(xml_path))

    xml_path.write_text("<modified-by-user-or-app/>", encoding="utf-8")
    restored = exe_xml_manager.restore_from_backup(str(xml_path))

    assert restored is True
    assert xml_path.read_text(encoding="utf-8") == "<original/>"
