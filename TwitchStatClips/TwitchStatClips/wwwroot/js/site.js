const clipCache = {}; // pamięć podręczna w JS

async function fetchClips(gameId, period) {
    const cacheKey = `${gameId}_${period}`;

    // Sprawdź, czy dane są już w cache
    if (clipCache[cacheKey]) {
        console.log("🔁 Ładowanie z cache (JS)");
        displayClips(clipCache[cacheKey]);
        return;
    }

    // Jeśli nie ma — pobierz z serwera
    console.log("🌐 Pobieranie z serwera...");
    try {
        const response = await fetch(`/api/clips?gameId=${gameId}&period=${period}`);
        const data = await response.json();

        clipCache[cacheKey] = data; // zapamiętaj
        displayClips(data);
    } catch (error) {
        console.error("Błąd ładowania klipów:", error);
    }
}
function loadClip(clipId) {
    const container = document.getElementById(`clip-${clipId}`);
    container.innerHTML = `
        <iframe
            src="https://clips.twitch.tv/embed?clip=${clipId}&parent=localhost"
            width="100%" height="270" frameborder="0" allowfullscreen>
        </iframe>
    `;
}

