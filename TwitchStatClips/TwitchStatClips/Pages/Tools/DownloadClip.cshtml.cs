using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Linq;

public class DownloadClipModel : PageModel
{
    // --- ⚠️ WAŻNE: Zmień te ścieżki! ---
    private const string PYTHON_EXECUTABLE = "python";
    private const string PYTHON_SCRIPT_PATH = "C:\\Users\\marci\\source\\repos\\TwitchStatClips\\TwitchStatClips\\skryptyPython\\download_clip.py";
    private const string DOWNLOAD_BASE_DIR = "C:\\Users\\marci\\source\\repos\\TwitchStatClips\\TwitchStatClips\\skryptyPython\\clips\\";

    // --- POLA DANYCH (UI) ---
    [BindProperty]
    public string ClipUrl { get; set; } = string.Empty;

    public string? DownloadedFilePath { get; set; } // Pełna ścieżka na serwerze
    public string? GeneratedFileName { get; set; } // Tylko nazwa pliku (do linku pobierania)
    public bool IsSuccess { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }

    // --- KONSTRUKTOR I GET ---
    public DownloadClipModel() { }
    public void OnGet() { }

    // --- FAZA 1: POBIERANIE NA SERWER ---
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        Directory.CreateDirectory(DOWNLOAD_BASE_DIR);

        string clipSlug = ExtractSlugFromUrl(ClipUrl);
        // Dodajemy datę i czas, aby nazwa była unikalna
        string fileName = $"{clipSlug}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        string finalFilePath = Path.Combine(DOWNLOAD_BASE_DIR, fileName);

        if (finalFilePath.Length > 250)
        {
            ErrorMessage = "Ścieżka do pliku jest zbyt długa.";
            return Page();
        }

        try
        {
            string output = await RunPythonScriptAsync(ClipUrl, finalFilePath);

            if (output.Contains("SUKCES"))
            {
                IsSuccess = true;
                DownloadedFilePath = finalFilePath;
                GeneratedFileName = fileName;
                StatusMessage = $"Klip został pomyślnie pobrany na serwer: {fileName}";
            }
            else
            {
                IsSuccess = false;
                ErrorMessage = "Pobieranie nie powiodło się (niejasny wynik). Wyjście Pythona: " + output;
            }
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            ErrorMessage = $"Krytyczny błąd wykonania skryptu: {ex.Message}";
        }

        return Page();
    }

    // --- FAZA 2: UDOSTĘPNIANIE PLIKU KLIENTOWI ---
    public IActionResult OnGetFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return BadRequest("Brak nazwy pliku.");

        string fullPath = Path.Combine(DOWNLOAD_BASE_DIR, fileName);

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Plik nie został znaleziony na serwerze.");

        // Otwiera plik i zwraca strumień do przeglądarki, wymuszając pobranie
        var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);

        return File(fileStream, "video/mp4", fileName);
    }

    // --- FUNKCJE POMOCNICZE ---

    private async Task<string> RunPythonScriptAsync(string clipUrl, string fullPath)
    {
        var arguments = $"\"{PYTHON_SCRIPT_PATH}\" \"{clipUrl}\" \"{fullPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = PYTHON_EXECUTABLE,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Skrypt Pythona zwrócił błąd ({process.ExitCode}). Wyjście: {output.Trim()}. Błąd: {error.Trim()}");
        }

        return output;
    }

    private static string ExtractSlugFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var lastSegment = uri.Segments.Last().Trim('/');
            var qIndex = lastSegment.IndexOf('?', StringComparison.Ordinal);
            if (qIndex >= 0) lastSegment = lastSegment[..qIndex];
            return string.IsNullOrWhiteSpace(lastSegment) ? "klip" : lastSegment;
        }
        catch { return "klip"; }
    }
}