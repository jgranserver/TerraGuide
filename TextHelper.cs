using Microsoft.Xna.Framework;
using TShockAPI;

namespace TerraGuide
{
    public static class TextHelper
    {
        public static string ColorHeader(string text) => $"[c/FFD700:{text}]"; // Gold color for headers

        public static string ColorRecipeName(string text) => $"[c/87CEEB:{text}]"; // Sky Blue for recipe names

        public static string ColorStation(string text) => $"[c/98FB98:{text}]"; // Pale Green for stations

        public static string ColorItem(string text) => $"[c/DDA0DD:{text}]"; // Plum for items

        public static string ColorRequirement(string text) => $"[c/FF6B6B:{text}]"; // Light Red for requirements

        public static string MakeListItem(string text) => $"  • {text}"; // Indented bullet point
    }
}
