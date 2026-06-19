import sqlite3, sys
db = r"C:\ProgramData\BODA\VMS\BodaVision_demo.db"
c = sqlite3.connect(db)
tables = [r[0] for r in c.execute("select name from sqlite_master where type='table' order by name")]
print(f"{len(tables)} tables:")
print(", ".join(tables))
print("=" * 60)
want = ["Client", "Recipe", "Product", "WorkOrder", "Lot", "DefectCode",
        "Inspection", "Parameter", "EquipmentStatus", "Operator", "Shift",
        "Maintenance", "Alarm"]
for t in tables:
    if any(w.lower() in t.lower() for w in want):
        cols = c.execute(f"pragma table_info('{t}')").fetchall()
        cnt = c.execute(f"select count(*) from '{t}'").fetchone()[0]
        print(f"\n[{t}]  rows={cnt}")
        for cid, name, typ, notnull, dflt, pk in cols:
            flags = []
            if pk: flags.append("PK")
            if notnull: flags.append("NN")
            if dflt is not None: flags.append(f"dflt={dflt}")
            print(f"   {name} {typ} {' '.join(flags)}")
