using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
        public override Version Version => new Version(1, 3);

        private readonly HttpClient _httpClient;
        private const string WikiBaseUrl = "https://terraria.wiki.gg/wiki/";
        private const string WikiApiUrl = "https://terraria.wiki.gg/api.php";

        // Class setup stage definitions: section index -> (display name, aliases)
        private static readonly (int section, string name, string[] aliases)[] ClassStages = new[]
        {
            (1, "Pre-Bosses",            new[] { "pre-boss", "boss", "preboss", "pre-bosses" }),
            (2, "Pre-Skeletron",         new[] { "skeletron", "pre-skeletron", "preskeletron" }),
            (3, "Pre-Wall of Flesh",     new[] { "wof", "wall", "pre-wof", "pre-wall", "prewof" }),
            (4, "Pre-mechanical bosses", new[] { "mech", "mechanical", "pre-mech", "premech" }),
            (5, "Pre-Plantera",          new[] { "plantera", "pre-plantera", "preplantera" }),
            (6, "Pre-Golem",             new[] { "golem", "pre-golem", "pregolem" }),
            (7, "Pre-Lunatic Cultist",   new[] { "cultist", "lunatic", "pre-cultist", "precultist" }),
            (8, "Pre-Moon Lord",         new[] { "moon", "moonlord", "pre-moon", "premoon" }),
            (9, "Endgame",              new[] { "endgame", "end", "final", "postgame" }),
        };

        // Class name aliases
        private static readonly (string canonical, string[] aliases)[] ClassAliases = new[]
        {
            ("Melee",     new[] { "melee", "warrior" }),
            ("Ranged",    new[] { "ranged", "ranger", "range" }),
            ("Magic",     new[] { "magic", "mage", "mana", "wizard" }),
            ("Summoning", new[] { "summoning", "summoner", "summon", "minion" }),
            ("Mixed",     new[] { "mixed", "general", "all", "universal" }),
        };

        // Simple wikitext cache: section index -> wikitext content
        private readonly Dictionary<int, string> _wikitextCache = new();

        public TerraGuide(Main game)
            : base(game)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "TerraGuide/1.2 (TShock Plugin; terraria.wiki.gg/wiki/User:Jgranserver)"
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

            Commands.ChatCommands.Add(
                new Command("terraguide.use", ClassCommand, "class")
                {
                    HelpText = "Shows class setup recommendations. Usage: /class <class> <stage>",
                }
            );
        }

        #region Wiki Command

        private async void WikiCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /wiki <search term>");
                return;
            }

            string searchTerm = string.Join(" ", args.Parameters);

            try
            {
                args.Player.SendInfoMessage($"Searching wiki for: {searchTerm}...");

                // Step 1: Search for the page title using opensearch
                string pageTitle = await SearchWikiTitle(searchTerm);
                if (pageTitle == null)
                {
                    args.Player.SendErrorMessage(
                        $"No wiki page found for '{searchTerm}'. Try the exact item name (e.g., 'Dirt Block')."
                    );
                    return;
                }

                // Step 2: Fetch the rendered HTML of the intro section via the parse API
                string introHtml = await FetchParsedSection(pageTitle, section: 0);
                if (introHtml == null)
                {
                    args.Player.SendErrorMessage($"Could not load wiki page for '{pageTitle}'.");
                    return;
                }

                // Step 3: Extract clean text from the HTML using HtmlAgilityPack
                string cleanText = ExtractCleanText(introHtml);

                if (string.IsNullOrWhiteSpace(cleanText))
                {
                    args.Player.SendErrorMessage(
                        $"No readable content found for '{pageTitle}'."
                    );
                    return;
                }

                // Step 4: Display to the player
                args.Player.SendInfoMessage($"--- {TextHelper.ColorHeader(pageTitle)} ---");

                // Split into sentences and limit output
                var chunks = SplitTextIntoSentenceChunks(cleanText, maxChars: 450);
                foreach (var chunk in chunks)
                {
                    args.Player.SendInfoMessage(chunk);
                }

                string wikiUrl = $"{WikiBaseUrl}{Uri.EscapeDataString(pageTitle.Replace(' ', '_'))}";
                args.Player.SendInfoMessage($"Read more: {wikiUrl}");
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Error accessing wiki: {ex.Message}");
                TShock.Log.Error($"TerraGuide wiki error for '{searchTerm}': {ex}");
            }
        }

        /// <summary>
        /// Uses the MediaWiki opensearch API to find the best matching page title.
        /// </summary>
        private async Task<string?> SearchWikiTitle(string searchTerm)
        {
            string searchUrl =
                $"{WikiApiUrl}?action=opensearch&format=json&search={HttpUtility.UrlEncode(searchTerm)}&limit=1&namespace=0&profile=fuzzy";

            var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            if (result[1] != null && result[1].Count > 0)
            {
                return result[1][0].ToString();
            }

            return null;
        }

        /// <summary>
        /// Fetches the rendered HTML of a specific section of a wiki page using the parse API.
        /// Section 0 = intro (everything before the first heading).
        /// </summary>
        private async Task<string?> FetchParsedSection(string pageTitle, int section)
        {
            string url =
                $"{WikiApiUrl}?action=parse&format=json&page={HttpUtility.UrlEncode(pageTitle)}&prop=text&section={section}&formatversion=2&disabletoc=true";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            return result?.parse?.text?.ToString();
        }

        /// <summary>
        /// Extracts clean, readable plaintext from rendered wiki HTML using HtmlAgilityPack.
        /// Removes infoboxes, tables, images, navboxes, and other non-content elements.
        /// </summary>
        private string ExtractCleanText(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove elements that are not useful for in-game display
            RemoveNodes(doc, "//table");                              // Infobox, stat tables
            RemoveNodes(doc, "//div[contains(@class,'mw-parser-output')]//div[contains(@class,'notaninfobox')]");
            RemoveNodes(doc, "//div[contains(@class,'infobox')]");
            RemoveNodes(doc, "//div[contains(@class,'navbox')]");
            RemoveNodes(doc, "//div[contains(@class,'toc')]");        // Table of contents
            RemoveNodes(doc, "//figure");                             // Images with captions
            RemoveNodes(doc, "//img");                                // Standalone images
            RemoveNodes(doc, "//sup[contains(@class,'reference')]");  // Citation refs [1]
            RemoveNodes(doc, "//span[contains(@class,'mw-editsection')]"); // [edit] links
            RemoveNodes(doc, "//style");
            RemoveNodes(doc, "//script");
            RemoveNodes(doc, "//div[contains(@class,'hatnote')]");    // "For other uses" notes
            RemoveNodes(doc, "//div[contains(@class,'quote')]");      // NPC quotes
            RemoveNodes(doc, "//blockquote");

            // Get the inner text
            string text = doc.DocumentNode.InnerText;

            // Decode HTML entities
            text = HttpUtility.HtmlDecode(text);

            // Normalize whitespace
            text = Regex.Replace(text, @"\r\n?|\n", " ");
            text = Regex.Replace(text, @"\s{2,}", " ");
            text = Regex.Replace(text, @"\[\d+\]", ""); // Remove leftover citation numbers

            return text.Trim();
        }

        /// <summary>
        /// Helper to remove all nodes matching an XPath query from an HtmlDocument.
        /// </summary>
        private void RemoveNodes(HtmlDocument doc, string xpath)
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes != null)
            {
                foreach (var node in nodes.ToList())
                {
                    node.Remove();
                }
            }
        }

        /// <summary>
        /// Splits text into chunks at sentence boundaries, respecting a max character limit.
        /// </summary>
        private List<string> SplitTextIntoSentenceChunks(string text, int maxChars)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            // Split by sentence-ending punctuation
            var sentences = Regex.Split(text, @"(?<=\.)\s+");
            var current = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (current.Length + sentence.Length + 1 > maxChars && current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(sentence);
            }

            if (current.Length > 0)
                chunks.Add(current.ToString().Trim());

            // Limit to 3 chunks to avoid chat spam
            if (chunks.Count > 3)
                chunks = chunks.Take(3).ToList();

            return chunks;
        }

        #endregion

        #region Recipe Command

        private void RecipeCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /recipe <item name>");
                return;
            }

            string searchTerm = string.Join(" ", args.Parameters);

            try
            {
                var bestMatch = FindBestItemMatch(searchTerm);

                if (bestMatch == null)
                {
                    args.Player.SendErrorMessage($"No items found matching '{searchTerm}'.");
                    return;
                }

                var recipes = FindRecipesForItem(bestMatch.type);

                if (recipes.Count == 0)
                {
                    args.Player.SendErrorMessage(
                        $"No crafting recipe found for {bestMatch.Name}."
                    );
                    return;
                }

                string resultName = TextHelper.ColorRecipeName(bestMatch.Name);
                args.Player.SendInfoMessage($"--- Crafting: {resultName} ---");

                for (int r = 0; r < recipes.Count; r++)
                {
                    var recipe = recipes[r];
                    if (recipes.Count > 1)
                        args.Player.SendInfoMessage($"Recipe {r + 1}/{recipes.Count}:");

                    // Show result amount if > 1
                    var createItem = GetCreateItem(recipe);
                    if (createItem != null && createItem.stack > 1)
                    {
                        args.Player.SendInfoMessage(
                            $"Creates: {createItem.stack}x {bestMatch.Name}"
                        );
                    }

                    // Show crafting station(s)
                    DisplayCraftingStations(args.Player, recipe);

                    // Show ingredients
                    DisplayIngredients(args.Player, recipe);

                    // Show special conditions
                    DisplayConditions(args.Player, recipe);
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"Error looking up recipe: {ex.Message}");
                TShock.Log.Error($"TerraGuide recipe error for '{searchTerm}': {ex}");
            }
        }

        /// <summary>
        /// Finds the best matching Item by name using TShock's built-in item lookup.
        /// </summary>
        private Item? FindBestItemMatch(string searchTerm)
        {
            var items = TShock.Utils.GetItemByIdOrName(searchTerm);
            if (items == null || items.Count == 0)
                return null;

            // If there's an exact match or a single result, use the first
            return items[0];
        }

        /// <summary>
        /// Finds all recipes that produce the given item type.
        /// Uses reflection to access createItem safely.
        /// </summary>
        private List<Recipe> FindRecipesForItem(int itemType)
        {
            var results = new List<Recipe>();
            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var recipe = Main.recipe[i];
                if (recipe == null) continue;

                try
                {
                    var createItem = GetFieldOrProp<Item>(recipe, "createItem");
                    if (createItem != null && createItem.type == itemType)
                        results.Add(recipe);
                }
                catch { }
            }
            return results;
        }

        /// <summary>
        /// Gets the createItem from a recipe, or null if not accessible.
        /// </summary>
        private Item? GetCreateItem(Recipe recipe)
        {
            return GetFieldOrProp<Item>(recipe, "createItem");
        }

        /// <summary>
        /// Displays the crafting station(s) required for a recipe.
        /// Uses reflection to handle both array and List types for requiredTile.
        /// </summary>
        private void DisplayCraftingStations(TSPlayer player, Recipe recipe)
        {
            try
            {
                var stations = new List<string>();

                // Try to get requiredTile via reflection (could be int[] or List<int>)
                var tileData = GetFieldOrPropRaw(recipe, "requiredTile");
                if (tileData == null) return;

                // Handle both int[] and List<int>
                if (tileData is System.Collections.IList tileList)
                {
                    foreach (var tile in tileList)
                    {
                        if (tile is int tileId)
                        {
                            if (tileId < 0) break;

                            string tileName;
                            try
                            {
                                // Try TileID.Search.GetName via reflection
                                var searchType = typeof(Terraria.ID.TileID).GetField("Search")?.GetValue(null);
                                var getNameMethod = searchType?.GetType().GetMethod("GetName");
                                tileName = getNameMethod?.Invoke(searchType, new object[] { tileId })?.ToString() ?? $"Tile #{tileId}";
                            }
                            catch
                            {
                                tileName = $"Tile #{tileId}";
                            }

                            stations.Add(TextHelper.ColorStation(tileName));
                        }
                    }
                }

                if (stations.Count > 0)
                    player.SendInfoMessage($"Station: {string.Join(" + ", stations)}");
            }
            catch (Exception ex)
            {
                TShock.Log.Warn($"TerraGuide: Error reading crafting stations: {ex.Message}");
            }
        }

        /// <summary>
        /// Displays the ingredients required for a recipe.
        /// Uses reflection to handle both array and List types for requiredItem.
        /// </summary>
        private void DisplayIngredients(TSPlayer player, Recipe recipe)
        {
            try
            {
                var itemData = GetFieldOrPropRaw(recipe, "requiredItem");
                if (itemData == null) return;

                var ingredientLines = new List<string>();

                if (itemData is System.Collections.IList itemList)
                {
                    foreach (var obj in itemList)
                    {
                        if (obj is Item reqItem)
                        {
                            if (reqItem.type <= 0) break;
                            ingredientLines.Add($"  • {reqItem.stack}x {TextHelper.ColorItem(reqItem.Name)}");
                        }
                    }
                }

                if (ingredientLines.Count > 0)
                {
                    player.SendInfoMessage("Ingredients:");
                    foreach (var line in ingredientLines)
                        player.SendInfoMessage(line);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Warn($"TerraGuide: Error reading ingredients: {ex.Message}");
            }
        }

        /// <summary>
        /// Displays special conditions required for a recipe.
        /// All access uses reflection for maximum compatibility.
        /// </summary>
        private void DisplayConditions(TSPlayer player, Recipe recipe)
        {
            try
            {
                var conditions = new List<string>();

                // Check boolean condition fields via reflection
                var booleanConditions = new Dictionary<string, string>
                {
                    { "needWater", "Near Water" },
                    { "needLava", "Near Lava" },
                    { "needHoney", "Near Honey" },
                    { "needSnowBiome", "In Snow Biome" },
                    { "needGraveyardBiome", "In Graveyard Biome" },
                    { "needEverything", "Special Crafting" },
                    { "needShimmer", "Near Shimmer" },
                };

                foreach (var kvp in booleanConditions)
                {
                    try
                    {
                        var val = GetFieldOrProp<bool?>(recipe, kvp.Key);
                        if (val == true) conditions.Add(kvp.Value);
                    }
                    catch { }
                }

                // Modern Conditions list (some Terraria versions)
                try
                {
                    var conditionsProp = recipe.GetType().GetProperty("Conditions");
                    if (conditionsProp != null)
                    {
                        var condList = conditionsProp.GetValue(recipe) as System.Collections.IList;
                        if (condList != null)
                        {
                            foreach (var cond in condList)
                            {
                                var descProp = cond?.GetType().GetProperty("Description");
                                string? desc = descProp?.GetValue(cond)?.ToString();
                                if (!string.IsNullOrWhiteSpace(desc) && !conditions.Contains(desc))
                                    conditions.Add(desc);
                            }
                        }
                    }
                }
                catch { }

                // Alchemy table bonus
                try
                {
                    var val = GetFieldOrProp<bool?>(recipe, "alchemy");
                    if (val == true)
                        conditions.Add("Benefits from Alchemy Table (33% chance to not consume ingredients)");
                }
                catch { }

                if (conditions.Count > 0)
                {
                    player.SendInfoMessage("Conditions:");
                    foreach (var condition in conditions)
                        player.SendInfoMessage($"  • {TextHelper.ColorRequirement(condition)}");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Warn($"TerraGuide: Error reading conditions: {ex.Message}");
            }
        }

        #region Reflection helpers

        /// <summary>
        /// Gets a field or property value from an object by name, using reflection.
        /// Returns default(T) if not found.
        /// </summary>
        private T? GetFieldOrProp<T>(object obj, string name)
        {
            var type = obj.GetType();
            var field = type.GetField(name);
            if (field != null) return (T?)field.GetValue(obj);

            var prop = type.GetProperty(name);
            if (prop != null) return (T?)prop.GetValue(obj);

            return default;
        }

        /// <summary>
        /// Gets a field or property value as object (untyped) using reflection.
        /// </summary>
        private object? GetFieldOrPropRaw(object obj, string name)
        {
            var type = obj.GetType();
            var field = type.GetField(name);
            if (field != null) return field.GetValue(obj);

            var prop = type.GetProperty(name);
            if (prop != null) return prop.GetValue(obj);

            return null;
        }

        #endregion

        #endregion

        #region Class Setups Command

        private void ClassCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1 || args.Parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                args.Player.SendMessage("━━━ [c/FFD700:/class Command Guide] ━━━", Microsoft.Xna.Framework.Color.White);
                args.Player.SendInfoMessage("Usage: /class <class> <stage>");
                args.Player.SendInfoMessage("");
                args.Player.SendMessage("[c/FFA500:Classes:]", Microsoft.Xna.Framework.Color.White);
                args.Player.SendInfoMessage("  Melee (warrior), Ranged (ranger), Magic (mage),");
                args.Player.SendInfoMessage("  Summoning (summoner), Mixed (general)");
                args.Player.SendInfoMessage("");
                args.Player.SendMessage("[c/FFA500:Stages:]", Microsoft.Xna.Framework.Color.White);
                args.Player.SendInfoMessage("  Pre-Bosses (boss), Pre-Skeletron, Pre-Wall of Flesh (wof),");
                args.Player.SendInfoMessage("  Pre-mechanical bosses (mech), Pre-Plantera, Pre-Golem,");
                args.Player.SendInfoMessage("  Pre-Lunatic Cultist (cultist), Pre-Moon Lord (moon), Endgame");
                args.Player.SendInfoMessage("");
                args.Player.SendMessage("[c/FFA500:Examples:]", Microsoft.Xna.Framework.Color.White);
                args.Player.SendInfoMessage("  /class melee pre-boss");
                args.Player.SendInfoMessage("  /class mage plantera");
                args.Player.SendInfoMessage("  /class summoner endgame");
                return;
            }

            // Parse class
            string classInput = args.Parameters[0].ToLowerInvariant();
            string? canonicalClass = null;
            foreach (var (canonical, aliases) in ClassAliases)
            {
                if (aliases.Any(a => a == classInput || a.Contains(classInput) || classInput.Contains(a)))
                {
                    canonicalClass = canonical;
                    break;
                }
            }

            if (canonicalClass == null)
            {
                args.Player.SendErrorMessage($"Unknown class '{args.Parameters[0]}'. Available: {string.Join(", ", ClassAliases.Select(c => c.canonical))}");
                return;
            }

            // Parse stage (if provided)
            if (args.Parameters.Count < 2)
            {
                args.Player.SendInfoMessage($"Usage: /class {canonicalClass.ToLower()} <stage>");
                args.Player.SendInfoMessage($"Stages: {string.Join(", ", ClassStages.Select(s => s.name))}");
                return;
            }

            string stageInput = string.Join(" ", args.Parameters.Skip(1)).ToLowerInvariant();
            (int section, string name, string[] aliases)? matchedStage = null;
            foreach (var stage in ClassStages)
            {
                if (stage.aliases.Any(a => a == stageInput || a.Contains(stageInput) || stageInput.Contains(a)))
                {
                    matchedStage = stage;
                    break;
                }
            }

            if (matchedStage == null)
            {
                args.Player.SendErrorMessage($"Unknown stage '{stageInput}'. Available: {string.Join(", ", ClassStages.Select(s => s.name))}");
                return;
            }

            // Fetch and display asynchronously
            Task.Run(async () =>
            {
                try
                {
                    await FetchAndDisplayClassSetup(args.Player, canonicalClass, matchedStage.Value);
                }
                catch (Exception ex)
                {
                    args.Player.SendErrorMessage($"Error fetching class setup: {ex.Message}");
                    TShock.Log.Error($"TerraGuide class command error: {ex}");
                }
            });
        }

        private async Task FetchAndDisplayClassSetup(TSPlayer player, string className, (int section, string name, string[] aliases) stage)
        {
            // Try to get cached wikitext, otherwise fetch
            string wikitext;
            if (_wikitextCache.TryGetValue(stage.section, out var cached))
            {
                wikitext = cached;
            }
            else
            {
                string url = $"{WikiApiUrl}?action=parse&page=Guide:Class_setups&prop=wikitext&section={stage.section}&format=json";
                var response = await _httpClient.GetStringAsync(url);

                // Extract wikitext from JSON
                var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                wikitext = json["parse"]?["wikitext"]?["*"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(wikitext))
                {
                    player.SendErrorMessage("Could not fetch class setup data from the wiki.");
                    return;
                }

                _wikitextCache[stage.section] = wikitext;
            }

            // Extract the class block from wikitext
            var classBlock = ExtractClassBlock(wikitext, className);
            if (string.IsNullOrWhiteSpace(classBlock))
            {
                player.SendErrorMessage($"No {className} setup found for {stage.name}.");
                return;
            }

            // Parse categories and items from the class block
            var categories = ParseClassCategories(classBlock);

            if (categories.Count == 0)
            {
                player.SendErrorMessage($"No equipment data found for {className} in {stage.name}.");
                return;
            }

            // Display header
            player.SendMessage($"━━━ [c/FFD700:{className}] — [c/87CEEB:{stage.name}] ━━━", Microsoft.Xna.Framework.Color.White);

            foreach (var (category, items) in categories)
            {
                if (items.Count == 0) continue;

                // Category header with color
                player.SendMessage($"  [c/FFA500:{category}]", Microsoft.Xna.Framework.Color.White);

                // Items in compact format, max ~4 per line to fit chat width
                var chunks = new List<string>();
                var currentLine = new List<string>();
                foreach (var item in items)
                {
                    currentLine.Add($"[c/AAFFAA:{item}]");
                    if (currentLine.Count >= 4)
                    {
                        chunks.Add("    " + string.Join(", ", currentLine));
                        currentLine.Clear();
                    }
                }
                if (currentLine.Count > 0)
                    chunks.Add("    " + string.Join(", ", currentLine));

                foreach (var chunk in chunks)
                    player.SendMessage(chunk, Microsoft.Xna.Framework.Color.White);
            }

            // Wiki link
            player.SendInfoMessage($"Full guide: {WikiBaseUrl}Guide:Class_setups#{stage.name.Replace(" ", "_")}");
        }

        /// <summary>
        /// Extracts the wikitext block for a specific class from a section's wikitext.
        /// Looks for {{infocard/start|...|name=ClassName|...}} to {{infocard/end}}
        /// </summary>
        private string? ExtractClassBlock(string wikitext, string className)
        {
            // Find the infocard start for this class
            var startPattern = new Regex(
                @"\{\{infocard/start\|[^}]*name=" + Regex.Escape(className) + @"[^}]*\}\}",
                RegexOptions.IgnoreCase
            );
            var startMatch = startPattern.Match(wikitext);
            if (!startMatch.Success) return null;

            int blockStart = startMatch.Index + startMatch.Length;

            // Find the matching infocard/end
            // We need to handle nested templates, so count depth
            int depth = 1;
            int pos = blockStart;
            while (pos < wikitext.Length && depth > 0)
            {
                if (pos + 17 <= wikitext.Length && wikitext.Substring(pos, 17) == "{{infocard/start|")
                    depth++;
                else if (pos + 16 <= wikitext.Length && wikitext.Substring(pos, 16) == "{{infocard/end}}")
                {
                    depth--;
                    if (depth == 0)
                        return wikitext.Substring(blockStart, pos - blockStart);
                }
                pos++;
            }

            // Fallback: take everything from start to end of wikitext
            return wikitext.Substring(blockStart);
        }

        /// <summary>
        /// Parses categories and items from a class block's wikitext.
        /// Uses depth tracking to properly handle nested box structures.
        /// </summary>
        private List<(string category, List<string> items)> ParseClassCategories(string block)
        {
            var result = new List<(string category, List<string> items)>();
            var itemPattern = new Regex(@"\{\{item\|([^|}]+)");

            // Sub-header titles to skip (these are nested inside real categories)
            var skipTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Best", "Defensive", "Glass Cannon", "Minion Mixing"
            };

            // Find all box/start and box/end positions
            var boxStartPattern = new Regex(@"\{\{infocard/box/start\|title=([^}|]+)");
            var boxEndRegex = new Regex(@"\{\{infocard/box/end");

            // Build a list of (position, isStart, title) events
            var events = new List<(int pos, int len, bool isStart, string title)>();
            foreach (Match m in boxStartPattern.Matches(block))
                events.Add((m.Index, m.Length, true, m.Groups[1].Value.Trim()));
            foreach (Match m in boxEndRegex.Matches(block))
                events.Add((m.Index, m.Length, false, ""));
            events.Sort((a, b) => a.pos.CompareTo(b.pos));

            // Walk through events tracking depth to find top-level category ranges
            int depth = 0;
            string? currentCategory = null;
            int categoryContentStart = 0;

            foreach (var evt in events)
            {
                if (evt.isStart)
                {
                    depth++;
                    if (depth == 1 && !skipTitles.Contains(evt.title))
                    {
                        // This is a new top-level category
                        // First, flush the previous category if any
                        if (currentCategory != null)
                        {
                            FlushCategory(result, currentCategory, block, categoryContentStart, evt.pos, itemPattern);
                        }

                        // Clean title
                        string title = evt.title;
                        if (title.Contains('|'))
                            title = title.Split('|').Last();
                        title = title.Replace("[[", "").Replace("]]", "").Trim();

                        currentCategory = title;
                        categoryContentStart = evt.pos + evt.len;
                    }
                }
                else
                {
                    depth--;
                    if (depth < 0) depth = 0;

                    // When depth returns to 0, we've closed the current top-level box
                    if (depth == 0 && currentCategory != null)
                    {
                        FlushCategory(result, currentCategory, block, categoryContentStart, evt.pos, itemPattern);
                        currentCategory = null;
                    }
                }
            }

            // Flush any remaining category
            if (currentCategory != null)
            {
                FlushCategory(result, currentCategory, block, categoryContentStart, block.Length, itemPattern);
            }

            return result;
        }

        /// <summary>
        /// Extracts items from a range of the block and adds them to the result list.
        /// </summary>
        private void FlushCategory(List<(string category, List<string> items)> result,
            string title, string block, int start, int end, Regex itemPattern)
        {
            if (start >= end || start >= block.Length) return;

            string content = block.Substring(start, Math.Min(end, block.Length) - start);
            var items = new List<string>();

            foreach (Match itemMatch in itemPattern.Matches(content))
            {
                string itemName = itemMatch.Groups[1].Value.Trim();
                // Skip image/formatting params and plural markers
                if (itemName.Contains("image=") || itemName.Contains(".png") || itemName.Contains(".gif"))
                    continue;
                if (!items.Contains(itemName))
                    items.Add(itemName);
            }

            if (items.Count == 0) return;

            // Merge with existing category of same name
            var existing = result.FirstOrDefault(r => r.category == title);
            if (existing.category != null)
            {
                foreach (var item in items)
                    if (!existing.items.Contains(item))
                        existing.items.Add(item);
            }
            else
            {
                result.Add((title, items));
            }
        }

        #endregion

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
