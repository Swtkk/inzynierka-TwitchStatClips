import os
import json
import time
from pathlib import Path

import requests

CLIENT_ID = "z4ic9d9hjf7nqc51rccab051stvp65"
CLIENT_SECRET = "p96uxjsego9w8kj3dzourts3ehrams"

# plik cache (opcjonalnie – żeby nie wołać refresh przy każdym starcie)
BASE_DIR = Path(__file__).resolve().parent
TOKEN_CACHE = BASE_DIR / "twitch_tokens1.json"

def _save_cache(data: dict):
    with open(TOKEN_CACHE, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

def _load_cache() -> dict | None:
    try:
        with open(TOKEN_CACHE, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return None

def refresh_access_token(refresh_token: str) -> dict:
    """Zwraca dict z polami: access_token, refresh_token, expires_in, obtained_at."""
    url = "https://id.twitch.tv/oauth2/token"
    data = {
        "grant_type": "refresh_token",
        "refresh_token": refresh_token,
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET
    }
    r = requests.post(url, data=data, timeout=30)
    r.raise_for_status()
    j = r.json()
    j["obtained_at"] = int(time.time())
    return j

def get_user_token_headless() -> str:
    """Headless: korzysta z REFRESH_TOKEN w ENV lub cache i sam odświeża token."""
    env_refresh = os.getenv("TWITCH_REFRESH_TOKEN")

    cache = _load_cache()
    if cache:
        # jeśli token jeszcze ważny ~60s buforu – użyj
        if "access_token" in cache and "expires_in" in cache and "obtained_at" in cache:
            if int(time.time()) < cache["obtained_at"] + int(cache["expires_in"]) - 60:
                return cache["access_token"]
        # jeśli mamy refresh w cache – odśwież
        if "refresh_token" in cache:
            tokens = refresh_access_token(cache["refresh_token"])
            _save_cache(tokens)
            return tokens["access_token"]

    # brak cache – spróbuj z ENV
    if not env_refresh:
        raise RuntimeError(
            "Brak REFRESH_TOKEN. Ustaw zmienną środowiskową TWITCH_REFRESH_TOKEN "
            "albo zapisz plik twitch_tokens.json z refresh_token."
        )

    tokens = refresh_access_token(env_refresh)
    _save_cache(tokens)
    return tokens["access_token"]
