"""Checks that every frontend/locales/<code>.json file has exactly the same
set of keys - a key present in one language but missing in another only
fails at runtime, when that specific string is displayed in that specific
language, so it's easy to miss by hand. Run manually after touching any
locale file, or wire into CI:

    python scripts/check_locales.py

Exits 1 (and prints a diff per language) if any file's keys don't match the
union of all keys; exits 0 if they're all in sync.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOCALES_DIR = os.path.join(ROOT, "frontend", "locales")
LANGUAGES_FILE = os.path.join(LOCALES_DIR, "languages.json")


def _load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def main():
    languages = _load_json(LANGUAGES_FILE)
    codes = [entry["code"] for entry in languages]

    keys_by_code = {}
    for code in codes:
        path = os.path.join(LOCALES_DIR, f"{code.lower()}.json")
        if not os.path.exists(path):
            print(f"MISSING FILE: {path} (listed in languages.json as {code!r})")
            keys_by_code[code] = set()
            continue
        keys_by_code[code] = set(_load_json(path).keys())

    all_keys = set()
    for keys in keys_by_code.values():
        all_keys |= keys

    problems = False
    for code in codes:
        missing = sorted(all_keys - keys_by_code[code])
        extra = sorted(keys_by_code[code] - all_keys)
        if missing or extra:
            problems = True
            print(f"\n{code} ({code.lower()}.json):")
            if missing:
                print(f"  missing {len(missing)} key(s) present in other languages:")
                for key in missing:
                    print(f"    - {key}")
            if extra:
                print(f"  has {len(extra)} key(s) no other language defines (typo? dead key?):")
                for key in extra:
                    print(f"    - {key}")

    if problems:
        print(f"\nLocale files are out of sync ({len(all_keys)} total distinct keys).")
        return 1

    print(f"All {len(codes)} locale files ({', '.join(codes)}) have matching keys "
          f"({len(all_keys)} keys each).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
