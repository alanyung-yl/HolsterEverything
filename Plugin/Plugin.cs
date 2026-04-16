using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Linq.Expressions;
using System.Reflection;

namespace HolsterEverything.F12Config;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class HolsterEverythingClientPlugin : BaseUnityPlugin
{
    private const string PluginGuid = "com.alanyung-yl.holstereverything.f12config";
    private const string PluginName = "HolsterEverything";
    private const string PluginVersion = "1.2.0";
    private const string HolsterSlotName = "Holster";

    private static HolsterEverythingClientPlugin? _instance;
    private Harmony? _harmony;

    private readonly Dictionary<string, ConfigEntry<bool>> _categoryToggles = new(StringComparer.OrdinalIgnoreCase);
    private ConfigEntry<bool>? _enableAllWeapons;
    private ConfigEntry<bool>? _enableHolsterSizeLimit;
    private ConfigEntry<bool>? _ignoreFoldState;
    private ConfigEntry<int>? _maxHolsterWidth;
    private ConfigEntry<int>? _maxHolsterHeight;

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

        var sizeSection = "Holster Size (Apply immediately)";

        _enableHolsterSizeLimit = Config.Bind(
            sizeSection,
            "EnableHolsterSizeLimit",
            false,
            "When true, oversized weapons are rejected before they can be dropped into the holster slot."
        );

        _ignoreFoldState = Config.Bind(
            sizeSection,
            "IgnoreFoldState",
            false,
            "When true, folded weapons are checked against their unfolded size before they can be dropped into the holster slot."
        );

        _maxHolsterWidth = Config.Bind(
            sizeSection,
            "MaxHolsterWidth",
            4,
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

        SaveServerConfig();
        _harmony.PatchAll(typeof(HolsterEverythingClientPlugin).Assembly);
        Logger.LogInfo("HolsterEverything BepInEx F12 Configuration Manager sync initialized. Restart SPT server after category changes. Holster size settings apply immediately.");
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
        return _instance?._ignoreFoldState?.Value ?? false;
    }

    internal static void LogPatchIssue(string message)
    {
        _instance?.Logger.LogWarning(message);
    }

    [HarmonyPatch]
    private static class HolsterSlotSizeClientPatch
    {
        private delegate object? ObjectGetter(object instance);
        private delegate string? StringGetter(object instance);
        private delegate int IntGetter(object instance);
        private delegate bool BoolGetter(object instance);
        private delegate object? ObjectMethodCaller(object instance);
        private delegate object? ObjectMethodWithBoolCaller(object instance, object arg0, object arg1, bool arg2);

        private static readonly Type? SlotViewType = AccessTools.TypeByName("EFT.UI.DragAndDrop.SlotView");
        private static readonly Type? ItemContextType = AccessTools.TypeByName("ItemContextClass");
        private static readonly Type? ItemContextAbstractType = AccessTools.TypeByName("ItemContextAbstractClass");
        private static readonly Type? OperationType = AccessTools.TypeByName("GStruct153");
        private static readonly Type? SlotType = AccessTools.TypeByName("EFT.InventoryLogic.Slot");
        private static readonly Type? WeaponType = AccessTools.TypeByName("EFT.InventoryLogic.Weapon");
        private static readonly Type? InventoryEquipmentType = AccessTools.TypeByName("EFT.InventoryLogic.InventoryEquipment");

        private static readonly MethodBase? CanAcceptMethod = ResolveCanAcceptMethod();

        private static readonly ObjectGetter? SlotGetter = CreateGetter<ObjectGetter>(FindProperty(SlotViewType, "Slot"));
        private static readonly ObjectGetter? SlotFieldGetter = CreateGetter<ObjectGetter>(FindField(SlotViewType, "slot_0"));
        private static readonly StringGetter? SlotIdGetter = CreateGetter<StringGetter>(FindProperty(SlotType, "ID"));
        private static readonly StringGetter? SlotNameGetter = CreateGetter<StringGetter>(FindProperty(SlotType, "Name"));
        private static readonly ObjectGetter? SlotParentItemGetter = CreateGetter<ObjectGetter>(FindProperty(SlotType, "ParentItem"));
        private static readonly ObjectGetter? DraggedItemGetter = CreateGetter<ObjectGetter>(FindField(ItemContextType, "Item"));
        private static readonly ObjectGetter? FallbackDraggedItemGetter = CreateGetter<ObjectGetter>(FindField(ItemContextAbstractType, "Item"));

        private static readonly MethodInfo? CalculateCellSizeMethod = FindMethod(WeaponType, "CalculateCellSize");
        private static readonly ObjectMethodCaller? CalculateCellSizeCaller = CreateMethodCaller(CalculateCellSizeMethod);
        private static readonly MemberInfo? CellSizeXMember = FindFieldOrProperty(CalculateCellSizeMethod?.ReturnType, "X");
        private static readonly MemberInfo? CellSizeYMember = FindFieldOrProperty(CalculateCellSizeMethod?.ReturnType, "Y");
        private static readonly IntGetter? CellSizeXGetter = CreateGetter<IntGetter>(CellSizeXMember);
        private static readonly IntGetter? CellSizeYGetter = CreateGetter<IntGetter>(CellSizeYMember);

        private static readonly BoolGetter? WeaponFoldedGetter = CreateGetter<BoolGetter>(FindProperty(WeaponType, "Folded"));
        private static readonly MemberInfo? CurrentAddressMember = FindFieldOrProperty(WeaponType, "CurrentAddress");
        private static readonly ObjectGetter? WeaponCurrentAddressGetter = CreateGetter<ObjectGetter>(CurrentAddressMember);
        private static readonly MethodInfo? GetFoldableMethod = FindMethod(WeaponType, "GetFoldable");
        private static readonly ObjectMethodCaller? GetFoldableCaller = CreateMethodCaller(GetFoldableMethod);
        private static readonly MethodInfo? GetSizeAfterFoldingMethod = ResolveGetSizeAfterFoldingMethod();
        private static readonly ObjectMethodWithBoolCaller? GetSizeAfterFoldingCaller = CreateMethodWithBoolCaller(GetSizeAfterFoldingMethod);

        private static readonly bool CanValidateHolsterSize =
            WeaponType is not null
            && InventoryEquipmentType is not null
            && (SlotGetter is not null || SlotFieldGetter is not null)
            && SlotIdGetter is not null
            && SlotNameGetter is not null
            && SlotParentItemGetter is not null
            && (DraggedItemGetter is not null || FallbackDraggedItemGetter is not null)
            && CalculateCellSizeCaller is not null
            && CellSizeXGetter is not null
            && CellSizeYGetter is not null;

        private static readonly bool CanResolveUnfoldedSize =
            WeaponFoldedGetter is not null
            && WeaponCurrentAddressGetter is not null
            && GetFoldableCaller is not null
            && GetSizeAfterFoldingCaller is not null
            && CellSizeXGetter is not null
            && CellSizeYGetter is not null;

        private static MethodBase? TargetMethod()
        {
            if (CanAcceptMethod is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve SlotView.CanAccept types. Drag-drop size validation is disabled.");
                return null;
            }

            if (!CanValidateHolsterSize)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve required drag-drop members. Holster size validation is disabled.");
            }

            return CanAcceptMethod;
        }

        private static bool Prefix(object __instance, ref bool __result, object[] __args)
        {
            if (!IsHolsterSizeLimitEnabled() || !CanValidateHolsterSize)
            {
                return true;
            }

            var slot = GetSlot(__instance);
            if (slot is null || !IsHolsterSlot(slot))
            {
                return true;
            }

            var draggedItem = GetDraggedItem(__args);
            if (draggedItem is null || WeaponType?.IsInstanceOfType(draggedItem) != true)
            {
                return true;
            }

            var maxWidth = GetMaxHolsterWidth();
            var maxHeight = GetMaxHolsterHeight();

            if (!TryGetCurrentSize(draggedItem, out var width, out var height))
            {
                return true;
            }

            if (width > maxWidth || height > maxHeight)
            {
                __result = false;
                return false;
            }

            if (!ShouldIgnoreFoldState() || WeaponFoldedGetter?.Invoke(draggedItem) != true)
            {
                return true;
            }

            if (!TryGetUnfoldedSize(draggedItem, out width, out height))
            {
                return true;
            }

            if (width <= maxWidth && height <= maxHeight)
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static MethodBase? ResolveCanAcceptMethod()
        {
            if (SlotViewType is null || ItemContextType is null || ItemContextAbstractType is null || OperationType is null)
            {
                return null;
            }

            return AccessTools.Method(SlotViewType, "CanAccept", [ItemContextType, ItemContextAbstractType, OperationType.MakeByRefType()]);
        }

        private static MethodInfo? ResolveGetSizeAfterFoldingMethod()
        {
            var currentAddressType = GetMemberType(CurrentAddressMember);
            var foldableType = GetFoldableMethod?.ReturnType;

            if (WeaponType is null || currentAddressType is null || foldableType is null)
            {
                return null;
            }

            return AccessTools.Method(WeaponType, "GetSizeAfterFolding", [currentAddressType, foldableType, typeof(bool)]);
        }

        private static object? GetSlot(object slotView)
        {
            return SlotGetter?.Invoke(slotView) ?? SlotFieldGetter?.Invoke(slotView);
        }

        private static bool IsHolsterSlot(object slot)
        {
            var slotId = SlotIdGetter?.Invoke(slot);
            var slotMatches = string.Equals(slotId, HolsterSlotName, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(slotId) && string.Equals(SlotNameGetter?.Invoke(slot), HolsterSlotName, StringComparison.OrdinalIgnoreCase));

            if (!slotMatches)
            {
                return false;
            }

            var parentItem = SlotParentItemGetter?.Invoke(slot);
            return parentItem is not null && InventoryEquipmentType?.IsInstanceOfType(parentItem) == true;
        }

        private static object? GetDraggedItem(object[] args)
        {
            if (args.Length >= 1 && args[0] is not null)
            {
                var draggedItem = DraggedItemGetter?.Invoke(args[0]);
                if (draggedItem is not null)
                {
                    return draggedItem;
                }
            }

            if (args.Length >= 2 && args[1] is not null)
            {
                return FallbackDraggedItemGetter?.Invoke(args[1]);
            }

            return null;
        }

        private static bool TryGetCurrentSize(object weapon, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (CalculateCellSizeCaller is null)
            {
                return false;
            }

            try
            {
                return TryReadCellSize(CalculateCellSizeCaller(weapon), out width, out height);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetUnfoldedSize(object weapon, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!CanResolveUnfoldedSize)
            {
                return false;
            }

            try
            {
                var currentAddress = WeaponCurrentAddressGetter?.Invoke(weapon);
                var foldable = GetFoldableCaller?.Invoke(weapon);
                if (currentAddress is null || foldable is null)
                {
                    return false;
                }

                var cellSize = GetSizeAfterFoldingCaller?.Invoke(weapon, currentAddress, foldable, false);
                return TryReadCellSize(cellSize, out width, out height);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadCellSize(object? cellSize, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (cellSize is null || CellSizeXGetter is null || CellSizeYGetter is null)
            {
                return false;
            }

            width = CellSizeXGetter(cellSize);
            height = CellSizeYGetter(cellSize);
            return true;
        }

        private static Type? GetMemberType(MemberInfo? member)
        {
            return member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => null,
            };
        }

        private static PropertyInfo? FindProperty(Type? type, string name)
        {
            return type is null ? null : AccessTools.Property(type, name);
        }

        private static FieldInfo? FindField(Type? type, string name)
        {
            return type is null ? null : AccessTools.Field(type, name);
        }

        private static MethodInfo? FindMethod(Type? type, string name)
        {
            return type is null ? null : AccessTools.Method(type, name);
        }

        private static MemberInfo? FindFieldOrProperty(Type? type, string name)
        {
            return (MemberInfo?)FindField(type, name) ?? FindProperty(type, name);
        }

        private static TDelegate? CreateGetter<TDelegate>(MemberInfo? member) where TDelegate : Delegate
        {
            if (member is null)
            {
                return null;
            }

            var invokeMethod = typeof(TDelegate).GetMethod("Invoke");
            if (invokeMethod is null)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            Expression memberAccess = member switch
            {
                PropertyInfo property => Expression.Property(Expression.Convert(instanceParameter, property.DeclaringType!), property),
                FieldInfo field => Expression.Field(Expression.Convert(instanceParameter, field.DeclaringType!), field),
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}"),
            };

            if (memberAccess.Type != invokeMethod.ReturnType)
            {
                memberAccess = Expression.Convert(memberAccess, invokeMethod.ReturnType);
            }

            return Expression.Lambda<TDelegate>(memberAccess, instanceParameter).Compile();
        }

        private static ObjectMethodCaller? CreateMethodCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var call = Expression.Call(Expression.Convert(instanceParameter, method.DeclaringType!), method);
            Expression body = call.Type == typeof(void)
                ? Expression.Block(call, Expression.Constant(null, typeof(object)))
                : Expression.Convert(call, typeof(object));
            return Expression.Lambda<ObjectMethodCaller>(body, instanceParameter).Compile();
        }

        private static ObjectMethodWithBoolCaller? CreateMethodWithBoolCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 3)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var arg0Parameter = Expression.Parameter(typeof(object), "arg0");
            var arg1Parameter = Expression.Parameter(typeof(object), "arg1");
            var arg2Parameter = Expression.Parameter(typeof(bool), "arg2");

            var call = Expression.Call(
                Expression.Convert(instanceParameter, method.DeclaringType!),
                method,
                Expression.Convert(arg0Parameter, parameters[0].ParameterType),
                Expression.Convert(arg1Parameter, parameters[1].ParameterType),
                Expression.Convert(arg2Parameter, parameters[2].ParameterType)
            );

            return Expression.Lambda<ObjectMethodWithBoolCaller>(
                Expression.Convert(call, typeof(object)),
                instanceParameter,
                arg0Parameter,
                arg1Parameter,
                arg2Parameter
            ).Compile();
        }
    }
}
