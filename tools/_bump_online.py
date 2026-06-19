import sqlite3, datetime
db = r"C:\ProgramData\BODA\VMS\BodaVision_demo.db"
c = sqlite3.connect(db)
c.execute("PRAGMA busy_timeout=8000")
future = (datetime.datetime.utcnow() + datetime.timedelta(minutes=30)).strftime('%Y-%m-%dT%H:%M:%S')
c.execute("UPDATE Clients SET LastSeenAt=?", (future,))
c.commit()
print("bumped LastSeenAt ->", future, "rows:", c.total_changes)
c.close()
