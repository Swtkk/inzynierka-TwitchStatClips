// Models/ViewModels/StatsPageViewModel.cs
using System.Collections.Generic;

namespace TwitchStatClips.Models.ViewModels
{
    public class StatsPageViewModel
    {
        public List<GetStats> Items { get; set; } = new();

        public string Range { get; set; } = "24h";   // 24h, 7d, 30d, all

        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }

        public string SortBy { get; set; } = "avg";   // avg, max, hours, followers
        public string SortDir { get; set; } = "desc"; // asc / desc

        public string Language { get; set; }
        public string Game { get; set; }
        public List<string> LanguageOptions { get; set; } = new();
        public List<string> GameOptions { get; set; } = new();
    }
}
