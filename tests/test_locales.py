"""Runs scripts/check_locales.py's logic as part of the normal test suite,
so a missing translation key fails `pytest` the same way a code bug would,
instead of only being caught if someone remembers to run the script by
hand."""
import json
import os

LOCALES_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "frontend", "locales")


def _load_json(name):
    with open(os.path.join(LOCALES_DIR, name), "r", encoding="utf-8") as f:
        return json.load(f)


def test_all_locale_files_have_matching_keys():
    languages = _load_json("languages.json")
    codes = [entry["code"] for entry in languages]

    keys_by_code = {code: set(_load_json(f"{code.lower()}.json").keys()) for code in codes}
    all_keys = set()
    for keys in keys_by_code.values():
        all_keys |= keys

    mismatches = {
        code: {"missing": sorted(all_keys - keys), "extra": sorted(keys - all_keys)}
        for code, keys in keys_by_code.items()
        if keys != all_keys
    }

    assert not mismatches, f"locale key mismatch (see scripts/check_locales.py for a detailed report): {mismatches}"
