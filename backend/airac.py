"""Navigraph AIRAC cycle check - pure logic ported from get_current_airac/
get_installed_airac.
"""
import datetime
import json
import os
import re


def get_current_airac():
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


def get_installed_airac(community_path):
    if not community_path or not os.path.isdir(community_path):
        return ""
    try:
        for item in os.listdir(community_path):
            if "navigraph" not in item.lower():
                continue
            manifest_path = os.path.join(community_path, item, "manifest.json")
            if not os.path.exists(manifest_path):
                continue
            try:
                with open(manifest_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                title = data.get("title", "")
                if "AIRAC" in title or "Navdata" in title:
                    match = re.search(r"\d{4}", title)
                    if match:
                        return match.group(0)
            except Exception:
                pass
    except Exception:
        pass
    return ""
