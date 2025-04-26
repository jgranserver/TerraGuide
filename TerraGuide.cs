using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace TerraGuide
{
    [ApiVersion(2, 1)]
    public class TerraGuide : TerrariaPlugin
    {
        public override string Author => "jgranserver";
        public override string Description => "A helpful guide plugin for Terraria servers";
        public override string Name => "TerraGuide";
        public override Version Version => new Version(1, 0, 0);

        private readonly HttpClient _httpClient;
        private const string WikiBaseUrl = "https://terraria.wiki.gg/wiki/";
        private const string WikiApiUrl = "https://terraria.wiki.gg/api.php";

        public TerraGuide(Main game)
            : base(game)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "TerraGuide/1.0 (TShock Plugin; terraria.wiki.gg/wiki/User:Jgranserver)"
            );
        }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(
                new Command("terraguide.use", WikiCommand, "wiki")
                {
                    HelpText = "Searches the Terraria Wiki. Usage: /wiki <search term>",
                }
            );

            Commands.ChatCommands.Add(
                new Command("terraguide.use", RecipeCommand, "recipe")
                {
                    HelpText = "Shows crafting information for items. Usage: /recipe <item name>",
                }
            );
        }

        private async void WikiCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /wiki <search term>");
                return;
            }

            string searchTerm = string.Join(" ", args.Parameters);
            string searchUrl =
                $"{WikiApiUrl}?action=opensearch&format=json&search={HttpUtility.UrlEncode(searchTerm)}&limit=1&namespace=0&profile=fuzzy";

            try
            {
                args.Player.SendInfoMessage($"Searching wiki for: {searchTerm}...");
                TShock.Log.Info($"Accessing search API URL: {searchUrl}");

                var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                searchRequest.Headers.Add("Accept", "application/json");

                using (var searchResponse = await _httpClient.SendAsync(searchRequest))
                {
                    searchResponse.EnsureSuccessStatusCode();
                    var searchJson = await searchResponse.Content.ReadAsStringAsync();
                    TShock.Log.Info($"Search API response: {searchJson}");

                    dynamic searchResult = Newtonsoft.Json.JsonConvert.DeserializeObject(
                        searchJson
                    );

                    if (searchResult[1].Count > 0)
                    {
                        string exactTitle = searchResult[1][0].ToString();
                        // Use the correct API parameters for content
                        string contentUrl =
                            $"{WikiApiUrl}?action=query&format=json&prop=revisions&rvprop=content&rvslots=main&titles={HttpUtility.UrlEncode(exactTitle)}";

                        TShock.Log.Info($"Fetching content from: {contentUrl}");

                        var contentRequest = new HttpRequestMessage(HttpMethod.Get, contentUrl);
                        contentRequest.Headers.Add("Accept", "application/json");

                        using (var contentResponse = await _httpClient.SendAsync(contentRequest))
                        {
                            contentResponse.EnsureSuccessStatusCode();
                            var contentJson = await contentResponse.Content.ReadAsStringAsync();
                            TShock.Log.Info($"Content API response: {contentJson}");

                            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(
                                contentJson
                            );
                            var pages = result.query?.pages;

                            if (pages != null)
                            {
                                var firstPageId = ((Newtonsoft.Json.Linq.JObject)pages)
                                    .Properties()
                                    .First()
                                    .Name;
                                var firstPage = pages[firstPageId];

                                if (firstPage.revisions != null && firstPage.revisions.Count > 0)
                                {
                                    string wikiText = firstPage
                                        .revisions[0]
                                        .slots.main["*"]
                                        .ToString();
                                    wikiText = CleanWikiText(wikiText);

                                    if (!string.IsNullOrWhiteSpace(wikiText))
                                    {
                                        // Split the text into chunks of reasonable size to avoid chat overflow
                                        const int chunkSize = 500;
                                        var chunks = SplitTextIntoChunks(wikiText, chunkSize);

                                        foreach (var chunk in chunks)
                                        {
                                            args.Player.SendInfoMessage(chunk);
                                        }

                                        args.Player.SendInfoMessage(
                                            $"Read more: {WikiBaseUrl}{HttpUtility.UrlEncode(exactTitle)}"
                                        );
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    args.Player.SendErrorMessage(
                        $"No information found for '{searchTerm}'. Try using the exact item name (e.g., 'Dirt Block' instead of 'dirt')."
                    );
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Error accessing wiki: {ex.Message}");
                TShock.Log.Error($"TerraGuide wiki error for term '{searchTerm}': {ex}");
            }
        }

        private void RecipeCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /recipe <item name>");
                return;
            }

            string searchTerm = string.Join(" ", args.Parameters);
            var matchingItems = new List<(Item item, float score)>();

            // Create regex pattern from search term
            var searchPattern = string.Join(
                ".*?",
                searchTerm
                    .Split(' ')
                    .Select(term => System.Text.RegularExpressions.Regex.Escape(term))
            );
            var regex = new System.Text.RegularExpressions.Regex(
                searchPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // Search through all items
            for (int i = 0; i < ItemID.Count; i++)
            {
                Item item = new Item();
                item.SetDefaults(i);

                if (string.IsNullOrEmpty(item.Name))
                    continue;

                // Try exact match first
                if (item.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    matchingItems.Clear();
                    matchingItems.Add((item, 1.0f));
                    break;
                }

                // Check for regex match
                var match = regex.Match(item.Name);
                if (match.Success)
                {
                    // Calculate match quality (0-1)
                    float score =
                        (float)match.Length / Math.Max(item.Name.Length, searchTerm.Length);
                    matchingItems.Add((item, score));
                }
                // Fallback to contains for partial matches
                else if (item.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    matchingItems.Add((item, 0.5f));
                }
            }

            if (matchingItems.Count == 0)
            {
                args.Player.SendErrorMessage($"No items found matching '{searchTerm}'.");
                return;
            }

            // Sort by match quality and get best match
            var bestMatch = matchingItems.OrderByDescending(x => x.score).First();
            var recipes = Main
                .recipe.Where(r => r != null && r.createItem.type == bestMatch.item.type)
                .ToList();

            if (recipes.Count == 0)
            {
                args.Player.SendErrorMessage(
                    $"No crafting recipe found for {bestMatch.item.Name}."
                );
                return;
            }

            args.Player.SendInfoMessage(
                $"Crafting information for {TextHelper.ColorRecipeName(bestMatch.item.Name)}:"
            );

            foreach (var recipe in recipes)
            {
                // Show crafting station
                if (recipe.requiredTile != null && recipe.requiredTile.Length > 0)
                {
                    var stations = recipe
                        .requiredTile.Where(t => t >= 0)
                        .Select(t => TextHelper.ColorStation(TileID.Search.GetName(t)))
                        .ToList();
                    args.Player.SendInfoMessage(
                        $"Crafting Station: {string.Join(" or ", stations)}"
                    );
                }

                // Show ingredients
                args.Player.SendInfoMessage("Required Items:");
                for (int i = 0; i < recipe.requiredItem.Length; i++)
                {
                    if (recipe.requiredItem[i].type > 0)
                    {
                        args.Player.SendInfoMessage(
                            $"• {recipe.requiredItem[i].stack}x {TextHelper.ColorItem(recipe.requiredItem[i].Name)}"
                        );
                    }
                }

                // Check special conditions
                var conditions = new List<string>();

                // Liquid requirements
                if (recipe.needWater)
                    conditions.Add("Must be near Water");
                if (recipe.needLava)
                    conditions.Add("Must be near Lava");
                if (recipe.needHoney)
                    conditions.Add("Must be near Honey");

                // Special locations
                if (recipe.needSnowBiome)
                    conditions.Add("Must be in Snow biome");
                if (recipe.needGraveyardBiome)
                    conditions.Add("Must be in Graveyard biome");

                // Display conditions if any exist
                if (conditions.Count > 0)
                {
                    args.Player.SendInfoMessage("\nSpecial Requirements:");
                    foreach (var condition in conditions)
                    {
                        args.Player.SendInfoMessage($"• {condition}");
                    }
                }
            }
        }

        private string CleanWikiText(string wikiText)
        {
            try
            {
                // Debug the input
                TShock.Log.Info(
                    $"Processing wiki text: {wikiText.Substring(0, Math.Min(200, wikiText.Length))}..."
                );

                // Remove item infobox
                wikiText = System.Text.RegularExpressions.Regex.Replace(
                    wikiText,
                    @"\{\{item infobox[\s\S]*?\}\}",
                    ""
                );

                // Remove other templates
                wikiText = System.Text.RegularExpressions.Regex.Replace(
                    wikiText,
                    @"\{\{[^}]*\}\}",
                    ""
                );

                // Remove file references and images with their captions
                wikiText = System.Text.RegularExpressions.Regex.Replace(
                    wikiText,
                    @"\[\[File:[^\]]*\]\].*?\n",
                    ""
                );

                // Convert wiki links to plain text (keep the readable part)
                wikiText = System.Text.RegularExpressions.Regex.Replace(
                    wikiText,
                    @"\[\[([^|\]]*?)\]\]",
                    "$1"
                );
                wikiText = System.Text.RegularExpressions.Regex.Replace(
                    wikiText,
                    @"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]",
                    "$1"
                );

                // Split into paragraphs
                var paragraphs = wikiText
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p =>
                        !string.IsNullOrWhiteSpace(p)
                        && !p.StartsWith("*")
                        && !p.StartsWith("|")
                        && !p.StartsWith("{{")
                        && !p.StartsWith("}}")
                        && !p.StartsWith("==")
                        && !p.StartsWith(":")
                        && p.Length > 20 // Minimum length for meaningful content
                    );

                // Get the first meaningful paragraph
                var description = paragraphs.FirstOrDefault() ?? "No description available.";

                // Clean up remaining markup and whitespace
                description = System.Text.RegularExpressions.Regex.Replace(
                    description,
                    @"'{2,}",
                    ""
                );
                description = System.Text.RegularExpressions.Regex.Replace(
                    description,
                    @"\s+",
                    " "
                );
                description = HttpUtility.HtmlDecode(description).Trim();

                // Debug the output
                TShock.Log.Info($"Cleaned description: {description}");

                return description;
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"Error cleaning wiki text: {ex}");
                return "Error processing wiki content.";
            }
        }

        private IEnumerable<string> SplitTextIntoChunks(string text, int chunkSize)
        {
            for (int i = 0; i < text.Length; i += chunkSize)
            {
                if (i + chunkSize <= text.Length)
                {
                    yield return text.Substring(i, chunkSize);
                }
                else
                {
                    yield return text.Substring(i);
                }
            }
        }

        private string ExtractCraftingInfo(string wikiText)
        {
            try
            {
                TShock.Log.Info("Starting to extract crafting info...");

                // First find the Recipes section specifically
                var recipesMatch = System.Text.RegularExpressions.Regex.Match(
                    wikiText,
                    @"===\s*Recipes\s*===\s*(.*?)(?===|\z)",
                    System.Text.RegularExpressions.RegexOptions.Singleline
                );

                if (!recipesMatch.Success)
                {
                    TShock.Log.Info("No Recipes section found.");
                    return "No recipe information found.";
                }

                string recipesSection = recipesMatch.Groups[1].Value.Trim();
                TShock.Log.Info($"Found Recipes section: {recipesSection}");
                var craftingInfo = new System.Text.StringBuilder();

                // Look for recipe template with all parameters
                var recipeMatch = System.Text.RegularExpressions.Regex.Match(
                    recipesSection,
                    @"\{\{recipes\|([^}]+)\}\}",
                    System.Text.RegularExpressions.RegexOptions.Singleline
                );

                if (recipeMatch.Success)
                {
                    string recipeParams = recipeMatch.Groups[1].Value;
                    TShock.Log.Info($"Found recipe parameters: {recipeParams}");

                    // Parse recipe parameters
                    var parameters = recipeParams
                        .Split('|')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

                    foreach (var param in parameters)
                    {
                        TShock.Log.Info($"Processing parameter: {param}");

                        if (param.StartsWith("station="))
                        {
                            var station = param.Substring("station=".Length).Trim();
                            station = CleanWikiLinks(station);
                            craftingInfo.AppendLine($"Crafting Station: {station}");
                        }
                        else if (param.StartsWith("i") && char.IsDigit(param[1]))
                        {
                            // Find matching amount parameter
                            var itemMatch = System.Text.RegularExpressions.Regex.Match(
                                param,
                                @"i(\d+)\s*=\s*(.+)"
                            );
                            if (itemMatch.Success)
                            {
                                string index = itemMatch.Groups[1].Value;
                                string item = CleanWikiLinks(itemMatch.Groups[2].Value);

                                // Look for corresponding amount
                                var amountParam = parameters.FirstOrDefault(p =>
                                    p.StartsWith($"a{index}=")
                                );
                                if (amountParam != null)
                                {
                                    var amount = amountParam.Substring($"a{index}=".Length).Trim();
                                    if (!craftingInfo.ToString().Contains("Required Items:"))
                                    {
                                        craftingInfo.AppendLine("\nRequired Items:");
                                    }
                                    craftingInfo.AppendLine($"• {amount}x {item}");
                                    TShock.Log.Info($"Added ingredient: {amount}x {item}");
                                }
                            }
                        }
                    }
                }

                // Add crafting tree note if present
                if (wikiText.Contains("=== Crafting tree ==="))
                {
                    craftingInfo.AppendLine(
                        "\nThis item has a complex crafting tree. Check the wiki for the complete recipe tree."
                    );
                }

                string result = craftingInfo.ToString().Trim();
                TShock.Log.Info($"Final crafting info: {result}");
                return string.IsNullOrWhiteSpace(result)
                    ? "No specific crafting information found."
                    : result;
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"Error extracting crafting info: {ex}");
                return "Error processing crafting information.";
            }
        }

        // Helper method to clean wiki links
        private string CleanWikiLinks(string text)
        {
            // Convert [[Link|Display]] to Display
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]",
                "$1"
            );
            return text.Trim();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
