# twitch_auth_init.py — JEDNORAZOWO
import os, json, webbrowser, requests
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs

CLIENT_ID = "fva05m0s6j7avb17cg1k8c8t82qya6"
CLIENT_SECRET = "yfjo30lzcz1ustzthag3ugs89gr7sv"
SCOPES = "moderator:read:followers"

HOST = "localhost"         # <— DOKŁADNIE jak w konsoli
PORT = 5227                # <— DOKŁADNIE jak w konsoli
PATH = "/auth/callback"    # <— DOKŁADNIE jak w konsoli
REDIRECT_URI = f"http://{HOST}:{PORT}{PATH}"

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TOKEN_FILE = os.path.join(BASE_DIR, "twitch_tokens.json")

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        u = urlparse(self.path)
        if u.path == PATH and "code" in parse_qs(u.query):
            code = parse_qs(u.query)["code"][0]
            data = {
                "client_id": CLIENT_ID,
                "client_secret": CLIENT_SECRET,
                "code": code,
                "grant_type": "authorization_code",
                "redirect_uri": REDIRECT_URI,
            }
            r = requests.post("https://id.twitch.tv/oauth2/token", data=data, timeout=30)
            r.raise_for_status()
            token_data = r.json()
            with open(TOKEN_FILE, "w", encoding="utf-8") as f:
                json.dump(token_data, f, indent=2, ensure_ascii=False)
            self.send_response(200); self.send_header("Content-Type","text/html; charset=utf-8"); self.end_headers()
            self.wfile.write("<h2>Token zapisany. Możesz zamknąć okno.</h2>".encode("utf-8"))
        else:
            self.send_response(404); self.end_headers()

if __name__ == "__main__":
    server = HTTPServer((HOST, PORT), Handler)   # <— bindowanie na 'localhost'
    auth_url = f"https://id.twitch.tv/oauth2/authorize?client_id={CLIENT_ID}&redirect_uri={REDIRECT_URI}&response_type=code&scope={SCOPES}"
    print("🌐", auth_url)
    webbrowser.open(auth_url)
    print(f"⏳ Czekam na {REDIRECT_URI} ...")
    server.handle_request()
    print(f"✅ Szukaj pliku: {TOKEN_FILE}")
