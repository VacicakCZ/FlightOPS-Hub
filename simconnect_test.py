from SimConnect import *
import time

def main():
    print("🚀 Startuji SimConnect Průzkumníka v2 (Debug Mód)...")
    
    sm = None
    while sm is None:
        try:
            sm = SimConnect()
        except Exception:
            time.sleep(2)
            
    print("✅ Úspěšně připojeno k MSFS jádru!")
    
    # Inicializace požadavků na data
    aq = AircraftRequests(sm, _time=2000)
    
    while True:
        # Přečteme název letadla a také jestli jsme na zemi
        title = aq.get("TITLE")
        on_ground = aq.get("SIM ON GROUND")
        
        # DEBUG VÝPIS: Ukáže nám přesně, co nám MSFS aktuálně posílá
        print(f"DEBUG -> TITLE: {repr(title)} | ON_GROUND: {repr(on_ground)}")
        
        # Ošetření: Pokud SimConnect vrací proměnnou jako bajty (b'Text')
        if title is not None and title != b'':
            if isinstance(title, bytes):
                try:
                    title = title.decode('utf-8', errors='ignore')
                except Exception:
                    pass
                    
            # Pokud máme text a není prázdný
            if isinstance(title, str) and title.strip():
                print("\n==================================================")
                print(" BINGO! 🎯")
                print(f" Detekováno načtení letadla: {title}")
                print("==================================================\n")
                break
                
        time.sleep(2)

    sm.exit()
    print("Skript úspěšně ukončen.")
    
    # Podrží okno konzole otevřené, abys viděl výsledek
    input("\nStiskni Enter pro zavření okna...")

if __name__ == "__main__":
    main()