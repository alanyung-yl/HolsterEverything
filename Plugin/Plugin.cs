using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;

namespace HolsterEverything.F12Config;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class HolsterEverythingClientPlugin : BaseUnityPlugin
{
    private const string PluginGuid = "com.alanyung-yl.holstereverything.f12config";
    private const string PluginName = "HolsterEverything F12 Config Sync";
    private const string PluginVersion = "1.1.0";
    private const string HolsterSlotName = "Holster";

    private static HolsterEverythingClientPlugin? _instance;
    private Harmony? _harmony;

    private readonly Dictionary<string, ConfigEntry<bool>> _categoryToggles = new(StringComparer.OrdinalIgnoreCase);
    private ConfigEntry<bool>? _enableAllWeapons;
    private ConfigEntry<bool>? _enableHolsterSizeLimit;
    private ConfigEntry<int>? _maxHolsterWidth;
    private ConfigEntry<int>? _maxHolsterHeight;
    private ConfigEntry<bool>? _ignoreFoldState;

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
        _instance = this;
        _harmony = new Harmony(PluginGuid);

        _enableAllWeapons = Config.Bind(
            "General (Restart server to apply changes)",
            "EnableAllWeapons",
            true,
            "When true, allows all weapon categories in holster."
        );

        foreach (var categoryName in WeaponCategoryNames)
        {
            var entry = Config.Bind(
                "Weapon Categories (Restart server to apply changes)",
                categoryName,
                false,
                $"Allow {categoryName} weapons in holster when EnableAllWeapons is false."
            );

            entry.SettingChanged += (_, _) => SaveServerConfig();
            _categoryToggles[categoryName] = entry;
        }

        _enableAllWeapons.SettingChanged += (_, _) => SaveServerConfig();

        var sizeSection = "Holster Size (Client-side, applies immediately)";

        _enableHolsterSizeLimit = Config.Bind(
            sizeSection,
            "EnableHolsterSizeLimit",
            false,
            "When true, oversized weapons are rejected before they can be dropped into the holster slot."
        );

        _maxHolsterWidth = Config.Bind(
            sizeSection,
            "MaxHolsterWidth",
            3,
            new ConfigDescription(
                "Maximum holster weapon width when the client-side size restriction is enabled.",
                new AcceptableValueRange<int>(1, 10)
            )
        );

        _maxHolsterHeight = Config.Bind(
            sizeSection,
            "MaxHolsterHeight",
            2,
            new ConfigDescription(
                "Maximum holster weapon height when the client-side size restriction is enabled.",
                new AcceptableValueRange<int>(1, 10)
            )
        );

        _ignoreFoldState = Config.Bind(
            sizeSection,
            "IgnoreFoldState",
            true,
            "When true, folded weapons are checked using their unfolded-equivalent width."
        );

        SaveServerConfig();
        _harmony.PatchAll(typeof(HolsterEverythingClientPlugin).Assembly);
        Logger.LogInfo("HolsterEverything F12 config sync initialized. Restart SPT server after category changes. Holster size settings are client-side.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
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

    internal static bool IsHolsterSizeLimitEnabled()
    {
        return _instance?._enableHolsterSizeLimit?.Value ?? false;
    }

    internal static int GetMaxHolsterWidth()
    {
        return Math.Max(1, _instance?._maxHolsterWidth?.Value ?? 3);
    }

    internal static int GetMaxHolsterHeight()
    {
        return Math.Max(1, _instance?._maxHolsterHeight?.Value ?? 2);
    }

    internal static bool ShouldIgnoreFoldState()
    {
        return _instance?._ignoreFoldState?.Value ?? true;
    }

    internal static void LogPatchIssue(string message)
    {
        _instance?.Logger.LogWarning(message);
    }

    [HarmonyPatch]
    private static class HolsterSlotSizeClientPatch
    {
        private static MethodBase? TargetMethod()
        {
            var slotViewType = AccessTools.TypeByName("EFT.UI.DragAndDrop.SlotView");
            var itemContextType = AccessTools.TypeByName("ItemContextClass");
            var itemContextAbstractType = AccessTools.TypeByName("ItemContextAbstractClass");
            var operationType = AccessTools.TypeByName("GStruct153");

            if (slotViewType is null || itemContextType is null || itemContextAbstractType is null || operationType is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve SlotView.CanAccept types. Drag-drop size validation is disabled.");
                return null;
            }

            return AccessTools.Method(slotViewType, "CanAccept", [itemContextType, itemContextAbstractType, operationType.MakeByRefType()]);
        }

        private static bool Prefix(object __instance, ref bool __result, object[] __args)
        {
            if (!IsHolsterSizeLimitEnabled())
            {
                return true;
            }

            if (!IsHolsterSlot(__instance))
            {
                return true;
            }

            var draggedItem = GetDraggedItem(__args);
            if (draggedItem is null || !IsWeapon(draggedItem))
            {
                return true;
            }

            var (width, height) = GetHolsterSize(draggedItem);
            if (width <= GetMaxHolsterWidth() && height <= GetMaxHolsterHeight())
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool IsHolsterSlot(object slotView)
        {
            var slot = GetMemberValue(slotView, "Slot") ?? GetMemberValue(slotView, "slot_0");
            var slotId = GetStringMemberValue(slot, "ID");
            var slotName = GetStringMemberValue(slot, "Name");
            var parentItem = GetMemberValue(slot, "ParentItem");
            var parentTypeName = parentItem?.GetType().FullName;
            var slotMatches = string.Equals(slotId, HolsterSlotName, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(slotId) && string.Equals(slotName, HolsterSlotName, StringComparison.OrdinalIgnoreCase));

            return slotMatches
                && string.Equals(parentTypeName, "EFT.InventoryLogic.InventoryEquipment", StringComparison.Ordinal);
        }

        private static object? GetDraggedItem(object[] args)
        {
            if (args.Length >= 1 && args[0] is not null)
            {
                var draggedItem = GetMemberValue(args[0], "Item");
                if (draggedItem is not null)
                {
                    return draggedItem;
                }
            }

            if (args.Length >= 2 && args[1] is not null)
            {
                return GetMemberValue(args[1], "Item");
            }

            return null;
        }

        private static bool IsWeapon(object item)
        {
            var weaponType = AccessTools.TypeByName("EFT.InventoryLogic.Weapon");
            return weaponType?.IsInstanceOfType(item) == true;
        }

        private static (int Width, int Height) GetHolsterSize(object item)
        {
            var cellSize = AccessTools.Method(item.GetType(), "CalculateCellSize")?.Invoke(item, null);
            var width = GetIntMemberValue(cellSize, "X");
            var height = GetIntMemberValue(cellSize, "Y");

            if (ShouldIgnoreFoldState() && IsFolded(item))
            {
                width += Math.Max(0, GetFoldedWidthReduction(item));
            }

            return (width, height);
        }

        private static bool IsFolded(object item)
        {
            return GetBoolMemberValue(item, "Folded");
        }

        private static int GetFoldedWidthReduction(object item)
        {
            var foldable = AccessTools.Method(item.GetType(), "GetFoldable")?.Invoke(item, null) ?? GetMemberValue(item, "Foldable");
            return Math.Max(0, GetIntMemberValue(foldable, "SizeReduceRight"));
        }

        private static object? GetMemberValue(object? instance, string memberName)
        {
            if (instance is null)
            {
                return null;
            }

            var instanceType = instance.GetType();
            var property = AccessTools.Property(instanceType, memberName);
            if (property is not null)
            {
                return property.GetValue(instance);
            }

            var field = AccessTools.Field(instanceType, memberName);
            return field?.GetValue(instance);
        }

        private static int GetIntMemberValue(object? instance, string memberName)
        {
            return GetMemberValue(instance, memberName) switch
            {
                int value => value,
                _ => 0,
            };
        }

        private static string? GetStringMemberValue(object? instance, string memberName)
        {
            return GetMemberValue(instance, memberName) as string;
        }

        private static bool GetBoolMemberValue(object? instance, string memberName)
        {
            return GetMemberValue(instance, memberName) switch
            {
                bool value => value,
                _ => false,
            };
        }
    }
}
