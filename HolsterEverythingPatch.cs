using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace HolsterEverything;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.alanyung-yl.holstereverything";
    public string Name { get; init; } = "HolsterEverything";
    public string Author { get; init; } = "alanyung-yl";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("2.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "GNU GPLv3";
}

public class HolsterEverythingConfig
{
    public bool EnableAllWeapons { get; set; } = true;
    public List<string> EnabledWeaponCategoryNames { get; set; } = [];
    public List<string> EnabledWeaponCategoryIds { get; set; } = [];
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class HolsterEverythingPatch(ISptLogger<HolsterEverythingPatch> logger, TemplateTable templateTable) : IOnLoad
{
    private const string LogPrefix = "HolsterEverything:";
    private const string PmcItemTemplateId = "55d7217a4bdc2d86028b456d";
    private const string HolsterSlotId = "55d729d84bdc2de3098b456b";
    private const string WeaponBaseClassId = "5422acb9af1c889c16000029";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var config = LoadOrCreateConfig();
        var items = templateTable.Items;

        if (!items.TryGetValue(PmcItemTemplateId, out var pmcTemplate) || pmcTemplate.Properties?.Slots == null)
        {
            logger.Error($"{LogPrefix} Could not find PMC template `{PmcItemTemplateId}` or its slots.");
            return Task.CompletedTask;
        }

        var holsterSlot = pmcTemplate.Properties.Slots.FirstOrDefault(slot => slot.Id == HolsterSlotId || slot.Name == "Holster");
        if (holsterSlot?.Properties?.Filters == null)
        {
            logger.Error($"{LogPrefix} Could not find holster slot filters.");
            return Task.CompletedTask;
        }

        var firstFilter = holsterSlot.Properties.Filters.FirstOrDefault();
        if (firstFilter == null)
        {
            logger.Error($"{LogPrefix} Holster slot has no filter entries.");
            return Task.CompletedTask;
        }

        firstFilter.Filter ??= new HashSet<MongoId>();
        var vanillaHolsterWhitelist = new HashSet<MongoId>(firstFilter.Filter);

        var weaponCategoriesByName = BuildWeaponCategoryLookup(items);
        var categoryIdsToAdd = ResolveConfiguredCategoryIds(config, weaponCategoriesByName, items, vanillaHolsterWhitelist);

        if (categoryIdsToAdd.Count == 0)
        {
            logger.Info($"{LogPrefix} No additional categories selected in config. Default holster behavior remains.");
            return Task.CompletedTask;
        }

        var addedCount = 0;
        foreach (var categoryId in categoryIdsToAdd)
        {
            if (firstFilter.Filter.Add(categoryId))
            {
                addedCount++;
            }
        }

        var configuredCategoryLabels = categoryIdsToAdd.Select(categoryId => GetCategoryLabel(items, categoryId)).ToList();
        var categoriesText = string.Join(", ", configuredCategoryLabels);

        if (addedCount > 0)
        {
            logger.Success(
                $"{LogPrefix} Applied holster category config. Added {addedCount} new whitelist entr{(addedCount == 1 ? "y" : "ies")}. " +
                $"Configured categories: {categoriesText}."
            );
        }
        else
        {
            logger.Info($"{LogPrefix} Holster category config already applied. Configured categories: {categoriesText}.");
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, MongoId> BuildWeaponCategoryLookup(Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> items)
    {
        var categories = new Dictionary<string, MongoId>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Values)
        {
            if (item.Parent != WeaponBaseClassId)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            categories[item.Name] = item.Id;
        }

        return categories;
    }

    private HashSet<MongoId> ResolveConfiguredCategoryIds(
        HolsterEverythingConfig config,
        Dictionary<string, MongoId> weaponCategoriesByName,
        Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> items,
        HashSet<MongoId> vanillaHolsterWhitelist
    )
    {
        var result = new HashSet<MongoId>();

        if (config.EnableAllWeapons)
        {
            result.Add(WeaponBaseClassId);
        }

        foreach (var name in config.EnabledWeaponCategoryNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var categoryName = name.Trim();
            if (!weaponCategoriesByName.TryGetValue(categoryName, out var categoryId))
            {
                logger.Warning($"{LogPrefix} Unknown category name in config: `{categoryName}`.");
                continue;
            }

            if (vanillaHolsterWhitelist.Contains(categoryId))
            {
                logger.Info($"{LogPrefix} Category `{categoryName}` is already allowed by vanilla holster whitelist and was ignored.");
                continue;
            }

            result.Add(categoryId);
        }

        foreach (var idText in config.EnabledWeaponCategoryIds)
        {
            if (string.IsNullOrWhiteSpace(idText))
            {
                continue;
            }

            var trimmedId = idText.Trim();
            if (!TryParseMongoId(trimmedId, out var parsedId))
            {
                logger.Warning($"{LogPrefix} Invalid category id in config: `{trimmedId}`.");
                continue;
            }

            if (parsedId == WeaponBaseClassId)
            {
                result.Add(parsedId);
                continue;
            }

            if (!items.TryGetValue(parsedId, out var itemTemplate))
            {
                logger.Warning($"{LogPrefix} Unknown category id in config: `{trimmedId}`.");
                continue;
            }

            if (itemTemplate.Parent != WeaponBaseClassId)
            {
                logger.Warning($"{LogPrefix} Id `{trimmedId}` is not a direct child category of WEAPON and was ignored.");
                continue;
            }

            if (vanillaHolsterWhitelist.Contains(parsedId))
            {
                logger.Info($"{LogPrefix} Category id `{trimmedId}` is already allowed by vanilla holster whitelist and was ignored.");
                continue;
            }

            result.Add(parsedId);
        }

        return result;
    }

    private static bool TryParseMongoId(string value, out MongoId id)
    {
        try
        {
            id = value;
            return true;
        }
        catch
        {
            id = MongoId.Empty();
            return false;
        }
    }

    private HolsterEverythingConfig LoadOrCreateConfig()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            var defaultConfig = new HolsterEverythingConfig();
            SaveConfig(configPath, defaultConfig);
            logger.Info($"{LogPrefix} Created default config at `{configPath}`.");
            return defaultConfig;
        }

        try
        {
            var configContent = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<HolsterEverythingConfig>(configContent, JsonOptions) ?? new HolsterEverythingConfig();

            config.EnabledWeaponCategoryNames ??= [];
            config.EnabledWeaponCategoryIds ??= [];

            return config;
        }
        catch (Exception ex)
        {
            logger.Error($"{LogPrefix} Failed reading config at `{configPath}`. Using defaults. Error: {ex.Message}");
            return new HolsterEverythingConfig();
        }
    }

    private static void SaveConfig(string path, HolsterEverythingConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string GetCategoryLabel(
        Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> items,
        MongoId categoryId
    )
    {
        if (categoryId == WeaponBaseClassId)
        {
            return $"Weapon ({WeaponBaseClassId})";
        }

        if (items.TryGetValue(categoryId, out var itemTemplate) && !string.IsNullOrWhiteSpace(itemTemplate.Name))
        {
            return $"{itemTemplate.Name} ({categoryId})";
        }

        return categoryId.ToString();
    }

    private static string GetConfigPath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var modDirectory = Path.GetDirectoryName(assemblyPath);

        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            return Path.Combine(AppContext.BaseDirectory, "config.json");
        }

        return Path.Combine(modDirectory, "config.json");
    }
}

