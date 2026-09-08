from backend import exe_xml_manager


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
