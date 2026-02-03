// Water Seams Fix - Synthesis Patcher for Skyrim Special Edition
// Fixes water seams and incorrect water data caused by mods
// that revert water assignments made by Update.esm or USSEP.

using System.Diagnostics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace WaterSeamsFix;

public class Program
{
    private const int CompressionFlag = 0x00040000;

    private static readonly ModKey UpdateEsm = ModKey.FromNameAndExtension("Update.esm");
    private static readonly ModKey USSEP = ModKey.FromNameAndExtension("Unofficial Skyrim Special Edition Patch.esp");

    private static readonly HashSet<string> IgnoredMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
        "Unofficial Skyrim Special Edition Patch.esp"
    };

    public static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetTypicalOpen(GameRelease.SkyrimSE, "WaterSeamsFix.esp")
            .Run(args);
    }

    public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        Console.WriteLine();
        Console.WriteLine("===========================================");
        Console.WriteLine("  WATER SEAMS FIX");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        // Check for truth sources
        bool hasUSSEP = state.LoadOrder.ContainsKey(USSEP);
        bool hasUpdate = state.LoadOrder.ContainsKey(UpdateEsm);

        Console.WriteLine($"  Update.esm: {(hasUpdate ? "OK" : "MISSING")}");
        Console.WriteLine($"  USSEP:      {(hasUSSEP ? "OK (priority)" : "Not installed")}");
        Console.WriteLine();

        if (!hasUpdate && !hasUSSEP)
        {
            Console.WriteLine("ERROR: Update.esm is required.");
            return;
        }

        var patchesByMod = new Dictionary<string, int>();
        int errorCount = 0;
        var skippedMods = new List<string>();

        Console.WriteLine("Scanning cells...");
        Console.WriteLine();

        // Process each mod individually to isolate errors
        foreach (var modListing in state.LoadOrder.PriorityOrder)
        {
            if (modListing.Mod == null) continue;
            
            string modName = modListing.ModKey.FileName.String;
            
            // Skip ignored mods
            if (IgnoredMods.Contains(modName)) continue;

            try
            {
                ProcessMod(modListing.Mod, modName, state, patchesByMod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Skipped {modName} - {ex.Message}");
                skippedMods.Add(modName);
                errorCount++;
            }
        }

        // Decompress records
        int decompressed = DecompressRecords(state.PatchMod);

        // Summary
        int totalPatched = patchesByMod.Values.Sum();
        
        Console.WriteLine("-------------------------------------------");
        Console.WriteLine("  RESULTS");
        Console.WriteLine("-------------------------------------------");
        Console.WriteLine();

        if (patchesByMod.Count > 0)
        {
            Console.WriteLine("Patched cells by mod:");
            foreach (var kvp in patchesByMod.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {kvp.Value,4} - {kvp.Key}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Total cells patched: {totalPatched}");
        
        if (decompressed > 0)
            Console.WriteLine($"Records decompressed: {decompressed}");
        
        if (skippedMods.Count > 0)
        {
            Console.WriteLine($"Skipped mods (corrupted): {skippedMods.Count}");
            foreach (var mod in skippedMods)
            {
                Console.WriteLine($"  - {mod}");
            }
        }

        Console.WriteLine();
        
        if (totalPatched > 0)
            Console.WriteLine("Done! Place the output plugin after your water mods.");
        else
            Console.WriteLine("Done! No patches needed - water data is correct.");
        
        Console.WriteLine();
    }

    private static void ProcessMod(
        ISkyrimModGetter mod, 
        string modName,
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        Dictionary<string, int> patchesByMod)
    {
        Console.Write($"  {modName}... ");
        
        var modStopwatch = Stopwatch.StartNew();
        int cellCount = 0;
        int patchCount = 0;
        var slowCells = new List<string>();

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(state.LinkCache))
        {
            cellCount++;
            var cellTimer = Stopwatch.StartNew();
            
            try
            {
                // Check if this mod is the winning override for this cell
                var winningContext = state.LinkCache.ResolveContext<ICell, ICellGetter>(cellContext.Record.FormKey);
                if (!winningContext.ModKey.Equals(mod.ModKey))
                    continue; // This mod doesn't win the conflict

                var truthSource = FindTruthSource(cellContext.Record.FormKey, state.LinkCache);
                if (truthSource == null)
                    continue;

                var winningWater = cellContext.Record.Water;
                var truthWater = truthSource.Water;

                bool needsPatch = false;
                
                if (truthWater.IsNull != winningWater.IsNull)
                {
                    needsPatch = true;
                }
                else if (!truthWater.IsNull && !winningWater.IsNull && 
                         !truthWater.FormKey.Equals(winningWater.FormKey))
                {
                    needsPatch = true;
                }

                if (needsPatch)
                {
                    var patchedCell = cellContext.GetOrAddAsOverride(state.PatchMod);
                    
                    if (truthWater.IsNull)
                        patchedCell.Water.Clear();
                    else
                        patchedCell.Water.SetTo(truthWater.FormKey);

                    patchCount++;
                }
            }
            catch
            {
                // Skip individual cell errors silently
            }
            finally
            {
                cellTimer.Stop();
                if (cellTimer.ElapsedMilliseconds > 1000)
                {
                    var cellId = cellContext.Record.EditorID ?? cellContext.Record.FormKey.ToString();
                    slowCells.Add($"{cellId} ({cellTimer.ElapsedMilliseconds}ms)");
                }
            }
        }

        modStopwatch.Stop();
        
        // Update patch count
        if (patchCount > 0)
        {
            patchesByMod[modName] = patchCount;
        }

        // Print result
        if (modStopwatch.ElapsedMilliseconds > 5000)
        {
            Console.WriteLine($"{cellCount} cells, {patchCount} patched [SLOW: {modStopwatch.ElapsedMilliseconds}ms]");
        }
        else
        {
            Console.WriteLine($"{cellCount} cells, {patchCount} patched");
        }

        // Print slow cells if any
        foreach (var slowCell in slowCells)
        {
            Console.WriteLine($"    Slow cell: {slowCell}");
        }
    }

    private static ICellGetter? FindTruthSource(FormKey formKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        ICellGetter? ussepRecord = null;
        ICellGetter? updateRecord = null;

        try
        {
            foreach (var context in linkCache.ResolveAllContexts<ICell, ICellGetter>(formKey))
            {
                string modName = context.ModKey.FileName.String;
                
                if (modName.Equals("Unofficial Skyrim Special Edition Patch.esp", StringComparison.OrdinalIgnoreCase))
                    ussepRecord = context.Record;
                else if (modName.Equals("Update.esm", StringComparison.OrdinalIgnoreCase))
                    updateRecord = context.Record;
            }
        }
        catch { return null; }

        return ussepRecord ?? updateRecord;
    }

    private static int DecompressRecords(ISkyrimMod patchMod)
    {
        int count = 0;
        foreach (var record in patchMod.EnumerateMajorRecords())
        {
            if (record is IMajorRecord mutable && (mutable.MajorRecordFlagsRaw & CompressionFlag) != 0)
            {
                mutable.MajorRecordFlagsRaw &= ~CompressionFlag;
                count++;
            }
        }
        return count;
    }
}
