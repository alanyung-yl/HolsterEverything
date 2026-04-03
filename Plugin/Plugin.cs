using BepInEx;
using BepInEx.Configuration;

namespace HolsterEverything.F12Config;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class HolsterEverythingClientPlugin : BaseUnityPlugin
{
    private const string PluginGuid = "com.alanyung-yl.holstereverything.f12config";
    private const string PluginName = "HolsterEverything F12 Config Sync";
    private const string PluginVersion = "1.1.0";

    private readonly Dictionary<string, ConfigEntry<bool>> _categoryToggles = new(StringComparer.OrdinalIgnoreCase);
    private ConfigEntry<bool>? _enableAllWeapons;

    // Intentionally excludes Pistol and Revolver from toggles.
    private static readonly string[] WeaponCategoryNames =
    [
        "AssaultCarbine",
        "AssaultRifle",
        "GrenadeLauncher",
        "MachineGun",
        "MarksmanRifle",
        "RocketLauncher",
        "Shotgun",
        "Smg",
        "SniperRifle",
        "SpecialWeapon",
    ];

    private void Awake()
    {
        _enableAllWeapons = Config.Bind(
            "General",
            "EnableAllWeapons",
            true,
            "When true, allows all weapon categories in holster."
        );

        foreach (var categoryName in WeaponCategoryNames)
        {
            var entry = Config.Bind(
                "Weapon Categories",
                categoryName,
                false,
                $"Allow {categoryName} weapons in holster when EnableAllWeapons is false."
            );

            entry.SettingChanged += (_, _) => SaveServerConfig();
            _categoryToggles[categoryName] = entry;
        }

        _enableAllWeapons.SettingChanged += (_, _) => SaveServerConfig();

        SaveServerConfig();
        Logger.LogInfo("HolsterEverything F12 config sync initialized. Restart SPT server after changing settings.");
    }

    private void SaveServerConfig()
    {
        try
        {
            var enabledNames = _categoryToggles
                .Where(pair => pair.Value.Value)
                .Select(pair => pair.Key)
                .OrderBy(name => name)
                .ToList();

            var enableAllWeapons = _enableAllWeapons?.Value ?? true;
            var json = BuildServerConfigJson(enableAllWeapons, enabledNames);

            var configPath = Path.Combine(Paths.GameRootPath, "SPT", "user", "mods", "HolsterEverything", "config.json");
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to write HolsterEverything server config: {ex.Message}");
        }
    }

    private static string BuildServerConfigJson(bool enableAllWeapons, List<string> enabledCategoryNames)
    {
        var lines = new List<string>
        {
            "{",
            $"  \"EnableAllWeapons\": {ToJsonBoolean(enableAllWeapons)},",
            "  \"EnabledWeaponCategoryNames\": [",
        };

        for (var i = 0; i < enabledCategoryNames.Count; i++)
        {
            var suffix = i == enabledCategoryNames.Count - 1 ? string.Empty : ",";
            lines.Add($"    {ToJsonString(enabledCategoryNames[i])}{suffix}");
        }

        lines.Add("  ],");
        lines.Add("  \"EnabledWeaponCategoryIds\": []");
        lines.Add("}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string ToJsonBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static string ToJsonString(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}

