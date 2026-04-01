using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace HolsterEverything;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.alanyung-yl.holstereverything";
    public override string Name { get; init; } = "HolsterEverything";
    public override string Author { get; init; } = "alanyung-yl";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class HolsterEverythingPatch(ISptLogger<HolsterEverythingPatch> logger, DatabaseService databaseService) : IOnLoad
{
    private const string PmcItemTemplateId = "55d7217a4bdc2d86028b456d";
    private const string HolsterSlotId = "55d729d84bdc2de3098b456b";
    private const string ItemToAllowInHolster = "5422acb9af1c889c16000029";

    public Task OnLoad()
    {
        var items = databaseService.GetItems();
        if (!items.TryGetValue(PmcItemTemplateId, out var pmcTemplate) || pmcTemplate.Properties?.Slots == null)
        {
            logger.Error($"HolsterEverything: Could not find PMC template `{PmcItemTemplateId}` or its slots.");
            return Task.CompletedTask;
        }

        var holsterSlot = pmcTemplate.Properties.Slots.FirstOrDefault(slot => slot.Id == HolsterSlotId || slot.Name == "Holster");
        if (holsterSlot?.Properties?.Filters == null)
        {
            logger.Error("HolsterEverything: Could not find holster slot filters.");
            return Task.CompletedTask;
        }

        var firstFilter = holsterSlot.Properties.Filters.FirstOrDefault();
        if (firstFilter == null)
        {
            logger.Error("HolsterEverything: Holster slot has no filter entries.");
            return Task.CompletedTask;
        }

        firstFilter.Filter ??= new HashSet<MongoId>();

        if (firstFilter.Filter.Add(ItemToAllowInHolster))
        {
            logger.Success($"HolsterEverything: Holster unlocked. Go pack every gun you want.");
        }
        else
        {
            logger.Info($"HolsterEverything: Already unlocked. Your holster was ready for chaos.");
        }

        return Task.CompletedTask;
    }
}
