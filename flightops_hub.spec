# -*- mode: python ; coding: utf-8 -*-
from PyInstaller.utils.hooks import collect_all

datas = []
binaries = []
hiddenimports = []
for pkg in ('webview', 'clr_loader', 'pythonnet', 'SimConnect', 'pystray', 'PIL'):
    tmp_ret = collect_all(pkg)
    datas += tmp_ret[0]; binaries += tmp_ret[1]; hiddenimports += tmp_ret[2]

# OIG3.ico se čte i za běhu (ikona okna, tray ikonka) - nejen jako ikona .exe souboru
datas += [('OIG3.ico', '.')]

# Celý frontend (HTML/CSS/JS/locales) musí jít do balíčku jako data - na
# rozdíl od customtkinter, které si svoje UI zdroje táhlo samo přes
# collect_all, tohle jsou naše vlastní soubory mimo Python balíčky.
datas += [('frontend', 'frontend')]

# ICAO -> souradnice pro mapu scenerii (M7), generovano offline skriptem
# scripts/build_airports_json.py - viz jeho docstring.
datas += [('data', 'data')]


a = Analysis(
    ['flightops_hub.pyw'],
    pathex=[],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='flightops_hub',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=['OIG3.ico'],
)
