# Water Seams Fix

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition that fixes water seams caused by mods.

## The Problem

Many mods accidentally revert water fixes from `Update.esm` and USSEP. This causes visible water seams at cell boundaries.

## How It Works

The patcher follows this process:

1. **Scan**: Iterates through every CELL record in your load order
2. **Filter**: Skips cells where the winning override is from Bethesda masters (Skyrim.esm, Update.esm, Dawnguard.esm, HearthFires.esm, Dragonborn.esm) or USSEP
3. **Find Truth Source**: For each remaining cell, looks for the same cell in USSEP first, then Update.esm - this is the "correct" water data
4. **Compare**: Compares the winning override's water (XCWT) field against the truth source
5. **Patch**: If they differ, creates an override that restores the correct water reference
6. **Decompress**: Clears compression flags on all patched records (required for Mutagen to write the plugin)

The patcher only modifies the Water (XCWT) subrecord. All other cell data (placed objects, lighting, navmeshes, etc.) remains untouched.

## Installation

1. Open Synthesis
2. Click **Git Repository**
3. Search for "Water Seams" or paste:
   ```
   https://github.com/Oliphantaupe/WaterSeamsFix-Vanilla
   ```
4. Add the patcher and run

## Requirements

- Skyrim Special Edition
- [Synthesis](https://github.com/Mutagen-Modding/Synthesis)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- USSEP (optional)

## License

MIT
