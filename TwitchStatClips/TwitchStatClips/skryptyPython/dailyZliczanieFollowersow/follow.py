#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import asyncio
import time
import logging
import pyodbc
from typing import Optional, List, Tuple

import aiohttp
from twitch_authDaneFollow import get_user_token_headless

# ===== KONFIG =====
SQL_CONN_STR = "Driver={ODBC Driver 17 for SQL Server};Server=MARCIN;Database=TwitchStats;Trusted_Connection=Yes;"
CLIENT_ID = "z4ic9d9hjf7nqc51rccab051stvp65"

# Współbieżność i limity
CONCURRENCY = 12         # liczba równoległych workerów
RATE_RPS = 12.0          # średnie tempo dosypywania tokenów na sekundę
BURST = 16               # maksymalny krótkotrwały burst żądań
MAX_RETRIES = 5          # maksymalna liczba ponowień na błąd 5xx lub sieciowy
DEFAULT_TIMEOUT = aiohttp.ClientTimeout(total=20)
BATCH_SIZE = 500
BACKOFF_BASE = 1.7       # baza backoffu wykładniczego
BACKOFF_CAP = 15         # maksymalny czas pojedynczego sleepa przy retry, sekundy

# ===== LOGOWANIE: tylko konsola =====
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(message)s",
    handlers=[logging.StreamHandler()]
)
log = logging.getLogger("FollowersUpdaterAsync")

# ===== BAZA DANYCH =====
def open_conn():
    return pyodbc.connect(SQL_CONN_STR, autocommit=False)

def fetch_all_channel_ids(conn) -> List[str]:
    """
    Zwraca listę ChannelId do aktualizacji:
    - bierzemy kanały z widoku GetStats_AllTime
    - łączymy po ChannelLogin ze StreamCurrent
    - tylko takie, które mają ChannelId
    - NAJPIERW te, które mają FollowersTotal = NULL
    - sortujemy malejąco po MaxViewers
    """
    cur = conn.cursor()
    cur.execute("""
         SELECT ChannelId
        FROM dbo.GetStats_AllTime_WithId WITH (NOLOCK)
        WHERE ChannelId IS NOT NULL
        ORDER BY ISNULL(MaxViewers, 0) DESC;
    """)
    rows = [r[0] for r in cur.fetchall()]
    cur.close()
    return rows

def batch_update_channel_meta(conn, rows: List[Tuple[Optional[int], Optional[str], str]]):
    """
    rows: (followers_total, avatar_url, channel_id)
    """
    if not rows:
        return
    cur = conn.cursor()
    try:
        cur.fast_executemany = True
    except Exception:
        pass
    cur.executemany("""
        UPDATE dbo.StreamCurrent
           SET FollowersTotal = COALESCE(?, FollowersTotal),
               AvatarUrl      = COALESCE(?, AvatarUrl)
         WHERE ChannelId = ?
    """, rows)
    cur.close()


async def db_writer(results_q: asyncio.Queue, batch_size: int):
    """
    Czyta z kolejki (followers, avatar, channel_id) i zapisuje w paczkach do DB.
    Commit po każdym batchu.
    """
    conn = open_conn()
    buffer: List[Tuple[Optional[int], Optional[str], str]] = []
    try:
        while True:
            item = await results_q.get()
            if item is None:
                # sygnał zakończenia
                results_q.task_done()
                break

            buffer.append(item)
            if len(buffer) >= batch_size:
                batch_update_channel_meta(conn, buffer)
                conn.commit()
                log.info(f"💾 COMMIT (batch {batch_size}) — zapisano {batch_size} rekordów")
                buffer.clear()
            results_q.task_done()

        # resztka po wyjściu z pętli
        if buffer:
            batch_update_channel_meta(conn, buffer)
            conn.commit()
    finally:
        conn.close()
        log.info("💾 DB writer zakończony.")


# ===== LIMITOWANIE: TOKEN BUCKET Z BURSTEM I ADAPTACJĄ =====
class TokenBucketLimiter:
    """
    Token bucket z refillem w tempie 'rate_per_sec' i pojemnością 'burst'.
    Umożliwia krótkie serie żądań (burst) bez sztywnego odstępu jak w prostym "sleep N".
    Posiada prostą adaptację po 429 poprzez mnożnik throttle < 1, który wraca powoli do 1.
    """
    def __init__(self, rate_per_sec: float, burst: int):
        self.rate = float(rate_per_sec)
        self.capacity = max(1, int(burst))
        self._tokens = float(burst)
        self._last = time.monotonic()
        self._lock = asyncio.Lock()
        self._throttle = 1.0
        self._last_penalty = 0.0

    def _refill(self):
        now = time.monotonic()
        delta = now - self._last
        self._last = now
        # powolny powrót przepustowości do 1.0 po karze
        if self._throttle < 1.0:
            self._throttle = min(1.0, self._throttle + 0.10 * delta)
        self._tokens = min(self.capacity, self._tokens + self.rate * self._throttle * delta)

    async def acquire(self):
        while True:
            async with self._lock:
                self._refill()
                if self._tokens >= 1.0:
                    self._tokens -= 1.0
                    return
                need = 1.0 - self._tokens
                eff_rate = max(1e-6, self.rate * self._throttle)
                wait_for = need / eff_rate
            await asyncio.sleep(min(wait_for, 0.05))

    async def penalize_on_429(self, retry_after_header: Optional[str]):
        """
        Po 429 obniż tymczasowo throttle oraz odczekaj Retry-After albo backoff.
        """
        if retry_after_header and retry_after_header.isdigit():
            sleep_s = int(retry_after_header)
        else:
            sleep_s = 2
        async with self._lock:
            self._throttle = max(0.2, self._throttle * 0.5)
            self._last_penalty = time.monotonic()
        await asyncio.sleep(sleep_s)

# ===== ZARZĄDZANIE TOKENEM =====
class TokenManager:
    """
    Współdzielony menedżer tokenu:
    - get(): zwraca aktualny token
    - refresh(): odświeża token z blokadą, aby uniknąć burzy odświeżeń
    """
    def __init__(self):
        self._token: Optional[str] = None
        self._lock = asyncio.Lock()
        self._initialized = False

    async def _fetch_new_token(self) -> str:
        # get_user_token_headless jest synchroniczne
        return await asyncio.to_thread(get_user_token_headless)

    async def ensure_initialized(self):
        if not self._initialized:
            async with self._lock:
                if not self._initialized:
                    self._token = await self._fetch_new_token()
                    self._initialized = True
                    log.info("🔑 Token Twitch użytkownika pobrany.")

    async def get(self) -> str:
        await self.ensure_initialized()
        return self._token  # type: ignore

    async def refresh(self) -> str:
        async with self._lock:
            new_token = await self._fetch_new_token()
            self._token = new_token
            log.info("🔁 Token odświeżony po 401.")
            return self._token

# ===== TWITCH API =====
async def fetch_followers_total(
    session: aiohttp.ClientSession,
    broadcaster_id: str,
    token_mgr: TokenManager,
    limiter: TokenBucketLimiter
) -> Optional[int]:
    """
    Pobiera total followersów z /helix/channels/followers.
    """
    url = "https://api.twitch.tv/helix/channels/followers"

    async def _single_call(tok: str):
        headers = {
            "Accept": "application/json",
            "Client-ID": CLIENT_ID,
            "Authorization": f"Bearer {tok}",
        }
        await limiter.acquire()
        return await session.get(url, headers=headers, params={"broadcaster_id": broadcaster_id})

    retries_left = MAX_RETRIES
    refreshed_once = False
    attempt = 0

    while retries_left > 0:
        attempt += 1
        tok = await token_mgr.get()
        try:
            async with await _single_call(tok) as r:
                if r.status == 429:
                    retry_after = r.headers.get("Retry-After")
                    log.warning(f"429 dla {broadcaster_id} — ograniczam tempo i usypiam wg Retry-After")
                    await limiter.penalize_on_429(retry_after)
                    retries_left -= 1
                    continue

                if r.status == 401 and not refreshed_once:
                    await token_mgr.refresh()
                    refreshed_once = True
                    continue

                if r.status in (401, 403):
                    log.warning(f"Brak autoryzacji {r.status} dla {broadcaster_id} — pomijam.")
                    return None

                if 500 <= r.status < 600:
                    sleep_s = min(BACKOFF_CAP, BACKOFF_BASE ** attempt)
                    log.warning(f"{r.status} dla {broadcaster_id} — retry za {sleep_s:.1f}s")
                    await asyncio.sleep(sleep_s)
                    retries_left -= 1
                    continue

                r.raise_for_status()
                data = await r.json()
                return data.get("total")
        except Exception as e:
            sleep_s = min(BACKOFF_CAP, BACKOFF_BASE ** attempt)
            log.warning(f"Błąd sieci dla {broadcaster_id}: {e} — retry za {sleep_s:.1f}s")
            await asyncio.sleep(sleep_s)
            retries_left -= 1

    log.error(f"Nie udało się pobrać followersów dla {broadcaster_id} po {MAX_RETRIES} próbach")
    return None

async def fetch_avatar_url(
    session: aiohttp.ClientSession,
    user_id: str,
    token_mgr: TokenManager,
    limiter: TokenBucketLimiter
) -> Optional[str]:
    """
    Pobiera profile_image_url z /helix/users.
    """
    url = "https://api.twitch.tv/helix/users"

    async def _single_call(tok: str):
        headers = {
            "Accept": "application/json",
            "Client-ID": CLIENT_ID,
            "Authorization": f"Bearer {tok}",
        }
        await limiter.acquire()
        return await session.get(url, headers=headers, params={"id": user_id})

    retries_left = MAX_RETRIES
    attempt = 0

    while retries_left > 0:
        attempt += 1
        tok = await token_mgr.get()
        try:
            async with await _single_call(tok) as r:
                if r.status == 429:
                    retry_after = r.headers.get("Retry-After")
                    log.warning(f"429 (users) dla {user_id} — ograniczam tempo")
                    await limiter.penalize_on_429(retry_after)
                    retries_left -= 1
                    continue

                if r.status in (401, 403):
                    log.warning(f"Brak autoryzacji {r.status} w /users dla {user_id} — pomijam.")
                    return None

                if 500 <= r.status < 600:
                    sleep_s = min(BACKOFF_CAP, BACKOFF_BASE ** attempt)
                    log.warning(f"{r.status} w /users dla {user_id} — retry za {sleep_s:.1f}s")
                    await asyncio.sleep(sleep_s)
                    retries_left -= 1
                    continue

                r.raise_for_status()
                data = await r.json()
                arr = data.get("data") or []
                if not arr:
                    return None
                return arr[0].get("profile_image_url")
        except Exception as e:
            sleep_s = min(BACKOFF_CAP, BACKOFF_BASE ** attempt)
            log.warning(f"Błąd sieci w /users dla {user_id}: {e} — retry za {sleep_s:.1f}s")
            await asyncio.sleep(sleep_s)
            retries_left -= 1

    log.error(f"Nie udało się pobrać avatara dla {user_id} po {MAX_RETRIES} próbach")
    return None

async def worker(
    name: int,
    q: asyncio.Queue,
    session: aiohttp.ClientSession,
    token_mgr: TokenManager,
    limiter: TokenBucketLimiter,
    results_q: asyncio.Queue,
    stats: dict
):
    while True:
        ch_id = await q.get()
        if ch_id is None:
            q.task_done()
            return

        followers, avatar = await asyncio.gather(
            fetch_followers_total(session, ch_id, token_mgr, limiter),
            fetch_avatar_url(session, ch_id, token_mgr, limiter),
        )

        if followers is not None or avatar is not None:
            await results_q.put((followers, avatar, ch_id))
            stats["ok"] += 1
        else:
            stats["fail"] += 1

        stats["done"] += 1
        if stats["done"] % 50 == 0:
            log.info(f"Postęp: {stats['done']}/{stats['total']} (OK={stats['ok']}, FAIL={stats['fail']})")
        q.task_done()


async def run_async(channel_ids: List[str]):
    token_mgr = TokenManager()
    await token_mgr.ensure_initialized()

    limiter = TokenBucketLimiter(RATE_RPS, BURST)
    connector = aiohttp.TCPConnector(
        limit=CONCURRENCY * 2,
        ttl_dns_cache=300
    )
    stats = {"done": 0, "ok": 0, "fail": 0, "total": len(channel_ids)}

    results_q: asyncio.Queue = asyncio.Queue()

    async with aiohttp.ClientSession(timeout=DEFAULT_TIMEOUT, connector=connector) as session:
        q: asyncio.Queue = asyncio.Queue()
        for ch in channel_ids:
            q.put_nowait(ch)
        for _ in range(CONCURRENCY):
            q.put_nowait(None)

        writer_task = asyncio.create_task(db_writer(results_q, BATCH_SIZE))

        workers = [
            asyncio.create_task(worker(i, q, session, token_mgr, limiter, results_q, stats))
            for i in range(CONCURRENCY)
        ]

        start = time.time()

        await q.join()
        for w in workers:
            await w

        await results_q.put(None)
        await writer_task

        elapsed = time.time() - start
        rps_eff = stats['done'] / max(1e-6, elapsed)
        log.info(f"✅ Zakończono pobieranie: {stats['done']}/{stats['total']} | OK={stats['ok']}, FAIL={stats['fail']} | {rps_eff:.1f} req/s")

    log.info("🎉 Wszystkie batche zapisane do bazy.")


def main():
    log.info("=== Followers Updater (async, token-refresh, token-bucket, po MaxViewers z GetStats_AllTime) ===")
    conn = open_conn()
    try:
        channel_ids = fetch_all_channel_ids(conn)
    finally:
        conn.close()
    log.info(f"📦 Kanałów do uzupełnienia (FollowersTotal IS NULL): {len(channel_ids)}")
    asyncio.run(run_async(channel_ids))

if __name__ == "__main__":
    main()
