#!/usr/bin/env python
"""One-time copy of the old SQLite data into PostgreSQL.

    python copy_sqlite_data.py --dry-run      # does everything, then rolls back
    python copy_sqlite_data.py                # copies for real

This is a plain, manual, one-off copy. It is NOT a sync: it does not watch anything, does not run
by itself, and running it a second time refuses to do anything (the tables are no longer empty).

The SQLite files are never changed or deleted. They are copied to a temporary folder and the copies
are read, so the originals are not even opened. The old branch keeps working on them.

What it copies
  orchestrator.db    -> schema orchestrator   (all 16 tables, column by column)
  sheet-service.db   -> schema sheets, ms_tokens        (the saved Microsoft sign-in; the tokens
                                                         are ENCRYPTED on the way in with TRACTO_SECRET_KEY)
  sheet-batches.db   -> schema sheets, process_batches and process_items   (row -> row_no)

Conversions: text times and unix times become timestamptz, 0/1 flags become boolean, blobs stay
bytes, '' in a time column becomes NULL. Everything is done in one transaction: if anything fails,
nothing is written.

Needs: Python 3.10+, `psycopg` (v3) and `cryptography`. The database and the key come from
DATABASE_URL / TRACTO_SECRET_KEY in the environment, or from the services' .env files.
"""
import argparse
import base64
import datetime as dt
import os
import re
import secrets
import shutil
import sqlite3
import sys
import tempfile
from pathlib import Path

import psycopg
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

TRACTO = Path(__file__).resolve().parents[2]

# source file, target schema, [(source table, target table)] in an order that respects foreign keys
PLAN = [
    ("orchestrator.db", "orchestrator", [
        ("RunRecords", "runrecords"), ("WorkerRecords", "workerrecords"), ("JournalEntries", "journalentries"),
        ("LeaseRecords", "leaserecords"), ("ArtifactRecords", "artifactrecords"), ("ArtifactContents", "artifactcontents"),
        ("ScheduleRecords", "schedulerecords"), ("FinalResultRecords", "finalresultrecords"), ("RunOutputs", "runoutputs"),
        ("CallbackRecords", "callbackrecords"), ("ContinuationMappings", "continuationmappings"),
        ("ContinuationRunRecords", "continuationrunrecords"), ("TelemetryEvents", "telemetryevents"),
        ("RunLogs", "runlogs"), ("SecurityEvents", "securityevents"), ("ResolutionRecords", "resolutionrecords"),
    ]),
    ("sheet-service.db", "sheets", [("ms_tokens", "ms_tokens")]),
    ("sheet-batches.db", "sheets", [("process_batches", "process_batches"), ("process_items", "process_items")]),
]

COLUMN_RENAMES = {("process_items", "row"): "row_no"}
ENCRYPTED = {("ms_tokens", "refresh_token"), ("ms_tokens", "access_token")}


def read_env_file(path: Path) -> dict:
    out = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8-sig").splitlines():
            m = re.match(r"\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$", line)
            if m and not line.lstrip().startswith("#"):
                out[m.group(1)] = m.group(2).strip("\"'")
    return out


def setting(name: str, *env_files: Path):
    if os.environ.get(name):
        return os.environ[name]
    for f in env_files:
        v = read_env_file(f).get(name)
        if v:
            return v
    return None


class Box:
    """AES-256-GCM in the format of sheet-service/internal/secretbox: "v1:" + base64url(nonce|ciphertext)."""

    def __init__(self, key_b64: str):
        raw = base64.b64decode(key_b64)
        if len(raw) != 32:
            raise SystemExit("TRACTO_SECRET_KEY must be 32 random bytes, base64 encoded")
        self.aead = AESGCM(raw)

    def seal(self, plain):
        if plain is None or plain == "":
            return plain
        nonce = secrets.token_bytes(12)
        out = nonce + self.aead.encrypt(nonce, plain.encode("utf-8"), None)
        return "v1:" + base64.urlsafe_b64encode(out).decode().rstrip("=")


def convert(value, dtype: str):
    if dtype == "timestamp with time zone":
        if value is None or value == "":
            return None
        if isinstance(value, (int, float)):
            return dt.datetime.fromtimestamp(value, dt.timezone.utc)
        return str(value)
    if dtype == "boolean":
        return None if value is None else bool(int(value))
    if dtype in ("integer", "bigint", "smallint"):
        return None if value is None else int(value)
    if dtype == "bytea":
        return None if value is None else bytes(value) if not isinstance(value, str) else value.encode("utf-8")
    if value is None:
        return None
    return value.decode("utf-8") if isinstance(value, bytes) else str(value)


def main() -> int:
    ap = argparse.ArgumentParser(description="One-time copy of the SQLite data into PostgreSQL (never changes the SQLite files).")
    ap.add_argument("--dry-run", action="store_true", help="do everything, then roll back")
    ap.add_argument("--orchestrator-db", default=str(TRACTO / "orchestrator-go" / "orchestrator.db"))
    ap.add_argument("--sheet-service-db", default=str(TRACTO / "sheet-service" / "sheet-service.db"))
    ap.add_argument("--sheet-batches-db", default=str(TRACTO / "sheet-service" / "sheet-batches.db"))
    args = ap.parse_args()

    url = setting("DATABASE_URL", TRACTO / "orchestrator-go" / ".env", TRACTO / "sheet-service" / ".env")
    if not url:
        raise SystemExit("DATABASE_URL is not set (environment or orchestrator-go/.env)")
    key = setting("TRACTO_SECRET_KEY", TRACTO / "sheet-service" / ".env")
    box = Box(key) if key else None

    sources = {"orchestrator.db": args.orchestrator_db, "sheet-service.db": args.sheet_service_db, "sheet-batches.db": args.sheet_batches_db}
    for name, path in sources.items():
        if not os.path.exists(path):
            raise SystemExit(f"{name} not found at {path}")
    if box is None and any(os.path.getsize(sources["sheet-service.db"]) for _ in [0]):
        raise SystemExit("TRACTO_SECRET_KEY is needed to encrypt the saved Microsoft tokens (sheet-service/.env)")

    tmp = tempfile.mkdtemp(prefix="sqlite-copy-")
    report = []
    try:
        # work on copies: the original files are only read by the file copy
        for name, path in sources.items():
            for suffix in ("", "-wal", "-shm"):
                if os.path.exists(path + suffix):
                    shutil.copy2(path + suffix, os.path.join(tmp, name + suffix))
        before = {n: os.path.getmtime(p) for n, p in sources.items()}

        with psycopg.connect(url) as conn:
            cur = conn.cursor()

            # refuse to copy into tables that already hold data
            for _, schema, tables in PLAN:
                for _, target in tables:
                    cur.execute(f'select count(*) from "{schema}"."{target}"')
                    n = cur.fetchone()[0]
                    if n:
                        raise SystemExit(f"{schema}.{target} already has {n} rows: this copy only fills empty tables "
                                         f"(nothing was written). It is not a sync.")

            for fname, schema, tables in PLAN:
                src = sqlite3.connect(os.path.join(tmp, fname))
                src.row_factory = sqlite3.Row
                for s_table, t_table in tables:
                    cur.execute("select column_name, data_type from information_schema.columns "
                                "where table_schema=%s and table_name=%s order by ordinal_position", (schema, t_table))
                    target_cols = {c: d for c, d in cur.fetchall()}
                    if not target_cols:
                        raise SystemExit(f"{schema}.{t_table} does not exist: start the service once so it creates its tables")

                    s_cols = [r[1] for r in src.execute(f'pragma table_info("{s_table}")')]
                    pairs = []   # (source column, target column)
                    for sc in s_cols:
                        tc = COLUMN_RENAMES.get((t_table, sc.lower()), sc.lower())
                        if tc in target_cols:
                            pairs.append((sc, tc))
                    ignored = [sc for sc in s_cols if sc not in [p[0] for p in pairs]]

                    skipped = 0
                    valid_batches = None
                    if t_table == "process_items":
                        valid_batches = {r[0] for r in src.execute("select id from process_batches")}

                    rows = src.execute(f'select {", ".join(chr(34) + p[0] + chr(34) for p in pairs)} from "{s_table}"')
                    cols_sql = ", ".join(f'"{p[1]}"' for p in pairs)
                    copied = 0
                    with cur.copy(f'COPY "{schema}"."{t_table}" ({cols_sql}) FROM STDIN') as copy:
                        for row in rows:
                            if valid_batches is not None and row["batch_id"] not in valid_batches:
                                skipped += 1
                                continue
                            out = []
                            for (sc, tc), v in zip(pairs, row):
                                v = convert(v, target_cols[tc])
                                if (t_table, tc) in ENCRYPTED:
                                    v = box.seal(v)
                                out.append(v)
                            copy.write_row(out)
                            copied += 1
                    total = src.execute(f'select count(*) from "{s_table}"').fetchone()[0]
                    report.append((fname, f"{schema}.{t_table}", total, copied, skipped, ignored))
                src.close()

            # compare what is in PostgreSQL with what was read
            ok = True
            for _, qual, total, copied, skipped, _ in report:
                cur.execute(f"select count(*) from {qual}")
                got = cur.fetchone()[0]
                if got != copied or copied + skipped != total:
                    ok = False
                    print(f"MISMATCH {qual}: source {total}, copied {copied}, skipped {skipped}, in PostgreSQL {got}")
            if not ok:
                conn.rollback()
                print("Rolled back: nothing was written.")
                return 1
            if args.dry_run:
                conn.rollback()
                print("DRY RUN: everything copied cleanly and was rolled back (nothing was written).")
            else:
                conn.commit()
                print("Committed.")

        after = {n: os.path.getmtime(p) for n, p in sources.items()}
        if before != after:
            print("WARNING: a SQLite file changed while copying (the old service may be running).")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    print(f"{'source file':18} {'table':34} {'in SQLite':>9} {'copied':>7} {'skipped':>8}")
    for fname, qual, total, copied, skipped, ignored in report:
        extra = f"   (columns not in PostgreSQL, not copied: {', '.join(ignored)})" if ignored else ""
        print(f"{fname:18} {qual:34} {total:9d} {copied:7d} {skipped:8d}{extra}")
    print("\nThe SQLite files were not changed or deleted.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
