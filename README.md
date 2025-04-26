# TerraGuide

A helpful plugin for Terraria servers running TShock that provides in-game access to crafting recipes and wiki information.

## Features

- `/recipe <item name>` - Shows crafting information for items
  - Displays required crafting station
  - Lists all required ingredients
  - Shows special conditions (biomes, liquids, etc.)
  - Supports fuzzy search for item names
- `/wiki <search term>` - Searches and displays information from the official Terraria Wiki
  - Shows item descriptions
  - Provides quick access to game information
  - Supports partial matching

## Installation

1. Download the latest release from the releases page
2. Place `TerraGuide.dll` in your server's `ServerPlugins` folder
3. Restart your TShock server

## Commands

| Command     | Permission         | Description                            |
| ----------- | ------------------ | -------------------------------------- |
| `/recipe` | `terraguide.use` | Shows crafting information for items   |
| `/wiki`   | `terraguide.use` | Searches and displays wiki information |

## Permissions

| Permission         | Description                                          |
| ------------------ | ---------------------------------------------------- |
| `terraguide.use` | Allows use of the `/recipe` and `/wiki` commands |

## Configuration

No configuration needed! Just install and use.

## Building from Source

1. Clone the repository
2. Open the solution in Visual Studio
3. Restore NuGet packages
4. Build the solution

## Dependencies

- TShock 5.0 or later
- .NET Framework 4.7.2 or later

## Contributing

Pull requests are welcome! For major changes, please open an issue first to discuss what you would like to change.

## Author

jgranserver

## Credits

- TShock Team for the amazing server mod
- Terraria Wiki contributors for recipe and item information
