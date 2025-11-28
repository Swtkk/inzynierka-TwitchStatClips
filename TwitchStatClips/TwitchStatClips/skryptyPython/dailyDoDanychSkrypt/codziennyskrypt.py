#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import time
import logging
from pathlib import Path
from typing import Any, Dict, Optional, Tuple, List
import datetime as dt

import requests
import pyodbc
from zoneinfo import ZoneInfo

# ===== Konfig =====
CLIENT_ID = 'fva05m0s6j7avb17cg1k8c8t82qya6'
CLIENT_SECRET = 'yfjo30lzcz1ustzthag3ugs89gr7sv'
SQL_CONN_STR = "Driver={ODBC Driver 17 for SQL Server};Server=MARCIN;Database=TwitchStats;Trusted_Connection=Yes;"

DEFAULT_TIMEOUT  = 20
RETRY_MAX        = 3
RETRY_BACKOFF    = 1.5
INTERVAL_MINUTES = 5
STALE_MINUTES_OFFLINE = 15

KEEP_HOURLY_DAYS = 3
KEEP_DAILY_DAYS  = 365

TZ_PL = ZoneInfo("Europe/Warsaw")

LOG_DIR = Path("../logs"); LOG_DIR.mkdir(exist_ok=True)
LOG_FILE = LOG_DIR / "upsert_streams_PL_fast.log"

logger = logging.getLogger("upsert_pl_fast")
logger.setLevel(logging.INFO)
fmt = logging.Formatter("%(asctime)s | %(levelname)s | %(message)s")
sh = logging.StreamHandler(); sh.setFormatter(fmt); logger.addHandler(sh)
fh = logging.FileHandler(LOG_FILE, encoding="utf-8"); fh.setFormatter(fmt); logger.addHandler(fh)

session = requests.Session()
session.headers.update({"Accept": "application/json"})

# ===== Czas PL =====
def now_pl() -> dt.datetime:
    return dt.datetime.now(TZ_PL).replace(tzinfo=None, microsecond=0)

def to_pl_naive(aware_utc: Optional[dt.datetime]) -> Optional[dt.datetime]:
    if aware_utc is None: return None
    return aware_utc.astimezone(TZ_PL).replace(tzinfo=None, microsecond=0)

def parse_iso_utc(s: Optional[str]) -> Optional[dt.datetime]:
    if not s: return None
    try:
        return dt.datetime.fromisoformat(s.replace('Z', '+00:00')).astimezone(dt.timezone.utc)
    except: return None

def floor_to_hour_local(x: dt.datetime) -> dt.datetime:
    return x.replace(minute=0, second=0, microsecond=0)

def fmt_k(n: int) -> str:
    try: n = int(n)
    except: return str(n)
    if n >= 1000:
        s = f"{n/1000:.1f}".rstrip('0').rstrip('.'); return f"{s}k"
    return str(n)

# ===== DB =====
def open_conn():
    return pyodbc.connect(SQL_CONN_STR, autocommit=False)

def load_last_seen_by_login(cur) -> Dict[str, Optional[dt.datetime]]:
    cur.execute("SELECT ChannelLogin, LastSeenAtUtc FROM dbo.StreamCurrent WITH (NOLOCK)")
    return {str(l): v for (l, v) in cur.fetchall()}

def compute_delta(now_local: dt.datetime, last_seen: Optional[dt.datetime],
                  started_local: Optional[dt.datetime], default_min: int) -> int:
    base = last_seen
    if started_local:
        base = started_local if base is None else max(base, started_local)
    if base is None:
        d = default_min
    else:
        d = (now_local - base).total_seconds() / 60.0
    d = max(1.0, min(d, 30.0))
    return int(round(d))

# ===== Prepared SQL (bez LOWER) =====
SQL_UPSERT_CURRENT = """
MERGE dbo.StreamCurrent AS t
USING (VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)) AS s
 (ChannelLogin, ChannelId, CapturedAt, IsLive, ViewerCount, GameId, GameName, [Language], LastSeenAtUtc, StartedAtUtc)
 ON t.ChannelLogin = s.ChannelLogin
WHEN MATCHED THEN UPDATE SET
  ChannelId      = COALESCE(t.ChannelId, s.ChannelId),
  CapturedAt     = s.CapturedAt,
  IsLive         = s.IsLive,
  ViewerCount    = s.ViewerCount,
  GameId         = s.GameId,
  GameName       = s.GameName,
  [Language]     = s.[Language],
  LastSeenAtUtc  = s.LastSeenAtUtc,
  StartedAtUtc   = CASE WHEN s.StartedAtUtc IS NOT NULL THEN s.StartedAtUtc ELSE t.StartedAtUtc END
WHEN NOT MATCHED THEN
  INSERT (ChannelLogin, ChannelId, CapturedAt, IsLive, ViewerCount, GameId, GameName, [Language], LastSeenAtUtc, StartedAtUtc)
  VALUES (s.ChannelLogin, s.ChannelId, s.CapturedAt, s.IsLive, s.ViewerCount, s.GameId, s.GameName, s.[Language], s.LastSeenAtUtc, s.StartedAtUtc);
"""

SQL_UPSERT_HOURLY = """
MERGE dbo.StreamAggHourly AS t
USING (VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)) AS s
 (ChannelLogin, ChannelId, BucketStartUtc, AddSumViewers, AddSample, NewMax, AddMinutes, AddWatchedMin, NewFollowers)
 ON t.ChannelLogin = s.ChannelLogin AND t.BucketStartUtc = s.BucketStartUtc
WHEN MATCHED THEN UPDATE SET
  ChannelId           = COALESCE(s.ChannelId, t.ChannelId),
  SumViewers          = t.SumViewers + s.AddSumViewers,
  SampleCount         = t.SampleCount + s.AddSample,
  MaxViewers          = CASE WHEN s.NewMax > t.MaxViewers THEN s.NewMax ELSE t.MaxViewers END,
  MinutesStreamed     = t.MinutesStreamed + s.AddMinutes,
  HoursWatchedMinutes = t.HoursWatchedMinutes + s.AddWatchedMin,
  LastSeenAtUtc       = s.BucketStartUtc
WHEN NOT MATCHED THEN INSERT
  (ChannelLogin, ChannelId, BucketStartUtc, SumViewers, SampleCount, MaxViewers, MinutesStreamed, HoursWatchedMinutes, FollowersLatest, LastSeenAtUtc)
  VALUES (s.ChannelLogin, s.ChannelId, s.BucketStartUtc, s.AddSumViewers, s.AddSample, s.NewMax, s.AddMinutes, s.AddWatchedMin, NULL, s.BucketStartUtc);
"""

SQL_UPSERT_DAILY = """
MERGE dbo.StreamAggDaily AS t
USING (VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)) AS s
 (ChannelLogin, ChannelId, BucketDateUtc, AddSumViewers, AddSample, NewMax, AddMinutes, AddWatchedMin, NewFollowers)
 ON t.ChannelLogin = s.ChannelLogin AND t.BucketDateUtc = s.BucketDateUtc
WHEN MATCHED THEN UPDATE SET
  ChannelId           = COALESCE(s.ChannelId, t.ChannelId),
  SumViewers          = t.SumViewers + s.AddSumViewers,
  SampleCount         = t.SampleCount + s.AddSample,
  MaxViewers          = CASE WHEN s.NewMax > t.MaxViewers THEN s.NewMax ELSE t.MaxViewers END,
  MinutesStreamed     = t.MinutesStreamed + s.AddMinutes,
  HoursWatchedMinutes = t.HoursWatchedMinutes + s.AddWatchedMin,
  LastSeenAtUtc       = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT
  (ChannelLogin, ChannelId, BucketDateUtc, SumViewers, SampleCount, MaxViewers, MinutesStreamed, HoursWatchedMinutes, FollowersLatest, LastSeenAtUtc)
  VALUES (s.ChannelLogin, s.ChannelId, s.BucketDateUtc, s.AddSumViewers, s.AddSample, s.NewMax, s.AddMinutes, s.AddWatchedMin, NULL, SYSDATETIME());
"""

def upsert_current(cur, login, channel_id, captured_at, is_live, viewers, game_id, game_name, language, started_at_local):
    cur.execute(SQL_UPSERT_CURRENT, (
        login, channel_id, captured_at, 1 if is_live else 0, int(viewers or 0),
        game_id, game_name, language, captured_at, started_at_local
    ))

def upsert_hourly(cur, login, channel_id, now_local, viewers, add_minutes, add_wmin):
    bucket = floor_to_hour_local(now_local)
    cur.execute(SQL_UPSERT_HOURLY, (
        login, channel_id, bucket, int(viewers or 0), 1, int(viewers or 0), int(add_minutes), int(add_wmin), None
    ))

def upsert_daily(cur, login, channel_id, now_local, viewers, add_minutes, add_wmin):
    bucket_date = now_local.date()
    cur.execute(SQL_UPSERT_DAILY, (
        login, channel_id, bucket_date, int(viewers or 0), 1, int(viewers or 0), int(add_minutes), int(add_wmin), None
    ))

def mark_offline_if_stale(cur, now_local: dt.datetime, stale_min: int):
    cur.execute("""
        UPDATE dbo.StreamCurrent
           SET IsLive = 0, ViewerCount = 0
         WHERE IsLive = 1 AND LastSeenAtUtc < DATEADD(MINUTE, ?, ?)
    """, (-stale_min, now_local))

def cleanup(conn):
    try:
        cur = conn.cursor()
        cur.execute("DELETE FROM dbo.StreamAggHourly WHERE BucketStartUtc < DATEADD(DAY, ?, SYSDATETIME());", (-KEEP_HOURLY_DAYS,))
        cur.execute("DELETE FROM dbo.StreamAggDaily  WHERE BucketDateUtc  < DATEADD(DAY, ?, CAST(SYSDATETIME() AS date));", (-KEEP_DAILY_DAYS,))
        conn.commit(); cur.close()
        logger.info("🧹 Cleanup OK.")
    except Exception as e:
        logger.warning(f"⚠️ cleanup: {e}")

# ===== Game spans =====
def load_open_game_spans(cur):
    cur.execute("""
        SELECT ChannelLogin, GameId, GameName, StartAtLocal
        FROM dbo.StreamGameSpans WITH (NOLOCK)
        WHERE EndAtLocal IS NULL
    """)
    return {row[0]: (row[1], row[2], row[3]) for row in cur.fetchall()}

def touch_game_span(cur, open_spans: Dict[str, Tuple[Optional[str], Optional[str], dt.datetime]],
                    login: str, uid: Optional[str], gid: Optional[str], gname: Optional[str],
                    start_hint: Optional[dt.datetime], now_local: dt.datetime):
    """
    Utrzymuje ciągłość spanu:
    - jeśli brak spanu -> otwórz od start_hint (StartedAt) lub now_local
    - jeśli zmiana gry -> domknij poprzedni na now_local i otwórz nowy od now_local
    """
    prev = open_spans.get(login)

    if prev is None:
        start_at = start_hint or now_local
        try:
            cur.execute("""
                INSERT INTO dbo.StreamGameSpans(ChannelLogin, ChannelId, GameId, GameName, StartAtLocal, EndAtLocal)
                VALUES(?, ?, ?, ?, ?, NULL)
            """, (login, uid, gid, gname, start_at))
        except pyodbc.IntegrityError:
            # skrajny przypadek – spróbuj „teraz” + 1s
            cur.execute("""
                INSERT INTO dbo.StreamGameSpans(ChannelLogin, ChannelId, GameId, GameName, StartAtLocal, EndAtLocal)
                VALUES(?, ?, ?, ?, ?, NULL)
            """, (login, uid, gid, gname, now_local + dt.timedelta(seconds=1)))
            start_at = now_local + dt.timedelta(seconds=1)
        open_spans[login] = (gid, gname, start_at)
        return

    prev_gid, _, _ = prev
    if prev_gid != gid:
        # zamknij poprzedni na TERAZ
        cur.execute("""
            UPDATE dbo.StreamGameSpans
               SET EndAtLocal = ?
             WHERE ChannelLogin = ? AND EndAtLocal IS NULL
        """, (now_local, login))

        # otwórz nowy OD TERAZ (zmiana kategorii = nowy odcinek)
        start_at = now_local
        cur.execute("""
            INSERT INTO dbo.StreamGameSpans(ChannelLogin, ChannelId, GameId, GameName, StartAtLocal, EndAtLocal)
            VALUES(?, ?, ?, ?, ?, NULL)
        """, (login, uid, gid, gname, start_at))
        open_spans[login] = (gid, gname, start_at)

def close_spans_for_offline(cur, now_local: dt.datetime):
    cur.execute("""
        UPDATE sgs
           SET EndAtLocal = ?
        FROM dbo.StreamGameSpans sgs
        JOIN dbo.StreamCurrent sc
          ON sc.ChannelLogin = sgs.ChannelLogin
        WHERE sgs.EndAtLocal IS NULL AND sc.IsLive = 0
    """, (now_local,))

# ===== API =====
def get_app_access_token() -> str:
    r = session.post("https://id.twitch.tv/oauth2/token",
                     params={"client_id": CLIENT_ID, "client_secret": CLIENT_SECRET, "grant_type": "client_credentials"},
                     timeout=DEFAULT_TIMEOUT)
    r.raise_for_status()
    token = r.json()["access_token"]
    logger.info("🔑 App token OK.")
    return token

def helix_headers(token: str):
    return {"Client-ID": CLIENT_ID, "Authorization": f"Bearer {token}"}

def fetch_streams_page(app_token: str, cursor: Optional[str] = None) -> Tuple[List[Dict[str, Any]], Optional[str]]:
    url = "https://api.twitch.tv/helix/streams"
    params = {"first": 100}
    if cursor: params["after"] = cursor
    attempt = 0
    while True:
        attempt += 1
        r = session.get(url, headers=helix_headers(app_token), params=params, timeout=DEFAULT_TIMEOUT)
        if r.status_code in (429, 500, 502, 503, 504) and attempt <= RETRY_MAX:
            wait = RETRY_BACKOFF * attempt
            logger.warning(f"⏳ {r.status_code} /streams retry {attempt}/{RETRY_MAX} za {wait:.1f}s")
            time.sleep(wait); continue
        r.raise_for_status()
        j = r.json()
        return j.get("data", []), j.get("pagination", {}).get("cursor")

# ===== Cykl =====
_runs = 0
def process_cycle(app_token: str):
    global _runs
    conn = open_conn()
    cur = conn.cursor()
    cur.fast_executemany = True

    total = 0
    try:
        t0 = time.perf_counter()
        last_seen_cache = load_last_seen_by_login(cur)
        open_spans = load_open_game_spans(cur)
        logger.info(f"📦 Cache LastSeen: {len(last_seen_cache):,} | open spans: {len(open_spans):,} | w {time.perf_counter()-t0:.2f}s")

        cursor = None
        page_no = 0
        while True:
            t_api0 = time.perf_counter()
            data, cursor = fetch_streams_page(app_token, cursor)
            api_s = time.perf_counter() - t_api0

            page_no += 1
            if not data:
                logger.info(f"📄 Strona #{page_no}: 0 kanałów (koniec)")
                break

            page_now = now_pl()
            t_db0 = time.perf_counter()

            for s in data:
                login = s.get("user_login") or ""
                uid   = s.get("user_id")
                vc    = int(s.get("viewer_count", 0))
                gid   = s.get("game_id")
                gname = s.get("game_name")
                lang  = s.get("language")

                started_local = to_pl_naive(parse_iso_utc(s.get("started_at")))

                last_seen = last_seen_cache.get(login)
                delta_min = compute_delta(page_now, last_seen, started_local, INTERVAL_MINUTES)
                watched   = vc * delta_min

                # upserty statystyk
                upsert_current(cur, login, uid, page_now, True, vc, gid, gname, lang, started_local)
                upsert_hourly(cur,  login, uid, page_now, vc, delta_min, watched)
                upsert_daily(cur,   login, uid, page_now, vc, delta_min, watched)

                # spany gier (start/zmiana)
                touch_game_span(cur, open_spans, login, uid, gid, gname, started_local, page_now)

                last_seen_cache[login] = page_now
                total += 1

            conn.commit()
            db_s = time.perf_counter() - t_db0
            logger.info(f"📄 Strona #{page_no}: {len(data)} kanałów | ⏱ API={api_s:.2f}s DB={db_s:.2f}s | zapisanych={fmt_k(total)}")

            if not cursor:
                break

        # oznacz offline i domknij spany offline
        now_local = now_pl()
        mark_offline_if_stale(cur, now_local, STALE_MINUTES_OFFLINE)
        close_spans_for_offline(cur, now_local)
        cur.execute("""
        UPDATE ah
           SET FollowersLatest = sc.FollowersTotal
        FROM dbo.StreamAggHourly ah
        JOIN dbo.StreamCurrent sc ON sc.ChannelLogin = ah.ChannelLogin
        WHERE ah.BucketStartUtc >= DATEADD(HOUR,-48, SYSDATETIME());
        """)
        cur.execute("""
        UPDATE ad
           SET FollowersLatest = sc.FollowersTotal
        FROM dbo.StreamAggDaily ad
        JOIN dbo.StreamCurrent sc ON sc.ChannelLogin = ad.ChannelLogin
        WHERE ad.BucketDateUtc >= CAST(DATEADD(DAY,-60, SYSDATETIME()) AS date);
        """)
        conn.commit()
        logger.info("👥 FollowersLatest zsynchronizowane w agregatach.")
        logger.info(f"🔻 Offline update + close spans OK. Upserty w cyklu: {fmt_k(total)}")

        _runs += 1
        if _runs % 12 == 0:  # sprzątanie rzadziej
            cleanup(conn)

    finally:
        try: conn.commit()
        except: pass
        cur.close(); conn.close()

def main():
    logger.info("=== Twitch Upsert (czas PL, szybki, z historią gier) ===")
    token = get_app_access_token()
    while True:
        try:
            process_cycle(token)
        except Exception:
            logger.exception("❌ Błąd cyklu — odświeżam token.")
            try:
                token = get_app_access_token()
            except Exception:
                logger.exception("❌ Odświeżenie tokenu nieudane.")
        logger.info(f"🕒 Sleep {INTERVAL_MINUTES} min…\n")
        time.sleep(INTERVAL_MINUTES * 60)

if __name__ == "__main__":
    main()
