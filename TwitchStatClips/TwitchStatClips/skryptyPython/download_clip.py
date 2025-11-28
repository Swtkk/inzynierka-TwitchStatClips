import sys
import os
import yt_dlp

def downloadClipFromArgs():
    """Pobiera klip wideo używając yt-dlp na podstawie argumentów CLI."""
    
    # 1. SPRAWDZENIE ARGUMENTÓW
    if len(sys.argv) < 3:
        # Zwracamy błąd i wychodzimy, jeśli nie ma dwóch argumentów
        print("BŁĄD: Wymagane są 2 argumenty: [URL_KLIPU] [PELNA_SCIEZKA_ZAPISU]")
        sys.exit(1)
        
    clip_page_url = sys.argv[1]
    output_filename = sys.argv[2] 

    # Ustawienia yt-dlp - Zmieniono quiet na True, by usunąć niepotrzebne komunikaty
    ydl_opts = {
        'format': 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best', 
        'outtmpl': output_filename, 
        'quiet': True, 
        'noplaylist': True,
        'no_warnings': True,
        'postprocessors': [{ 
            'key': 'FFmpegVideoConvertor',
            'preferedformat': 'mp4',
        }],
    }

    # Używamy os.path.dirname, aby upewnić się, że katalog istnieje
    target_dir = os.path.dirname(output_filename)
    if target_dir and not os.path.exists(target_dir):
        os.makedirs(target_dir)

    # 🛑 Usunięto komunikaty startowe, by nie zakłócały parsowania SUKCESU
    # print(f"Pobieranie klipu z: {clip_page_url}")
    # print(f"Ścieżka zapisu: {output_filename}")

    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            # Wypisujemy tylko, jeśli nie jesteśmy w trybie quiet
            # Zapewniamy, że wyjście jest czyste
            ydl.download([clip_page_url])
            
        # KLUCZOWA ZMIANA: Wypisujemy tylko komunikat SUKCES
        print(f"SUKCES: Klip zapisano pomyślnie w {output_filename}.")
        sys.exit(0) # Kod 0 oznacza sukces

    except Exception as e:
        # KLUCZOWA ZMIANA: Wypisujemy tylko komunikat BŁĄD
        print(f"BŁĄD: Wystąpił błąd podczas pobierania yt-dlp: {e}")
        sys.exit(1) # Kod 1 oznacza błąd


if __name__ == "__main__":
    downloadClipFromArgs()