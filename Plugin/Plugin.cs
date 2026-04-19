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
    private const string PluginVersion = "1.3.1";
    private const string HolsterSlotName = "Holster";
    private const string PistolCategoryId = "5447b5cf4bdc2d65278b4567";
    private const string RevolverCategoryId = "617f1ef5e8b54b0998387733";
    private const string StockCategoryId = "55818a594bdc2db9688b456a";
    private const string SignalPistolTemplateId = "620109578d82e67e7911abf2";
    private const string NoFreeSlotForThatItemMessage = "No free slot for that item";

    private static HolsterEverythingClientPlugin? _instance;
    private Harmony? _harmony;

    private readonly Dictionary<string, ConfigEntry<bool>> _categoryToggles = new(StringComparer.OrdinalIgnoreCase);
    private ConfigEntry<bool>? _enableAllWeapons;
    private ConfigEntry<bool>? _enableHolsterSizeLimit;
    private ConfigEntry<bool>? _onlyLimitNonVanillaWeapons;
    private ConfigEntry<bool>? _ignoreFoldState;
    private ConfigEntry<int>? _maxHolsterWidth;
    private ConfigEntry<int>? _maxHolsterHeight;
    private ConfigEntry<bool>? _enableHandlingPenalty;
    private ConfigEntry<bool>? _onlyLimitAdditionalWeaponsForHandling;
    private ConfigEntry<int>? _handlingErgoPenalty;
    private ConfigEntry<bool>? _includeNonFoldableWeaponsForHandling;

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
            "General (Restart Server)",
            "Enable All Weapons",
            true,
            "When true, all weapon categories can be equipped in the holster slot."
        );

        foreach (var categoryName in WeaponCategoryNames)
        {
            var displayName = GetCategoryDisplayName(categoryName);
            var entry = Config.Bind(
                "Weapon Categories (Restart Server)",
                displayName,
                false,
                $"When true, allows {displayName} weapons in the holster when Enable All Weapons is off."
            );

            entry.SettingChanged += (_, _) => SaveServerConfig();
            _categoryToggles[categoryName] = entry;
        }

        _enableAllWeapons.SettingChanged += (_, _) => SaveServerConfig();

        var sizeSection = "Holster Size (Apply Immediately)";

        _enableHolsterSizeLimit = Config.Bind(
            sizeSection,
            "Enable Size Limit",
            false,
            "When true, oversized weapons are blocked from being dropped into the holster slot."
        );

        _onlyLimitNonVanillaWeapons = Config.Bind(
            sizeSection,
            "Limit Additional Weapons Only",
            true,
            "When true, the size limit does not apply to vanilla holster weapons."
        );

        _ignoreFoldState = Config.Bind(
            sizeSection,
            "Use Unfolded Size",
            false,
            "When true, folded weapons are checked using their unfolded size."
        );

        _maxHolsterWidth = Config.Bind(
            sizeSection,
            "Max Holster Width",
            4,
            new ConfigDescription(
                "Maximum holster weapon width when Enable Size Limit is on.",
                new AcceptableValueRange<int>(1, 10)
            )
        );

        _maxHolsterHeight = Config.Bind(
            sizeSection,
            "Max Holster Height",
            2,
            new ConfigDescription(
                "Maximum holster weapon height when Enable Size Limit is on.",
                new AcceptableValueRange<int>(1, 10)
            )
        );

        var handlingSection = "Holster Handling (Apply Immediately)";

        _enableHandlingPenalty = Config.Bind(
            handlingSection,
            "Enable Handling Penalty",
            false,
            "When true, an eligible holstered weapon reduces the ergonomics of the firearm currently in hands."
        );

        _onlyLimitAdditionalWeaponsForHandling = Config.Bind(
            handlingSection,
            "Limit Additional Weapons Only",
            true,
            "When true, the handling penalty only applies to additional holster weapon categories."
        );

        _handlingErgoPenalty = Config.Bind(
            handlingSection,
            "Handling Ergo Penalty",
            10,
            new ConfigDescription(
                "Ergonomics penalty applied while an eligible holstered weapon would interfere with handling.",
                new AcceptableValueRange<int>(1, 90)
            )
        );

        _includeNonFoldableWeaponsForHandling = Config.Bind(
            handlingSection,
            "Include Non-Foldable Weapons",
            false,
            "When true, non-foldable holstered weapons can also trigger the handling penalty."
        );

        SaveServerConfig();
        _harmony.PatchAll(typeof(HolsterEverythingClientPlugin).Assembly);
        Logger.LogInfo("HolsterEverything BepInEx F12 Configuration Manager sync initialized. Restart SPT server after General or Weapon Categories changes. Holster Size and Holster Handling settings apply immediately.");
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

    private static string GetCategoryDisplayName(string categoryName)
    {
        return categoryName switch
        {
            "AssaultCarbine" => "Assault Carbine",
            "AssaultRifle" => "Assault Rifle",
            "GrenadeLauncher" => "Grenade Launcher",
            "MachineGun" => "Machine Gun",
            "MarksmanRifle" => "Marksman Rifle",
            "RocketLauncher" => "Rocket Launcher",
            "Shotgun" => "Shotgun",
            "Smg" => "SMG",
            "SniperRifle" => "Sniper Rifle",
            "SpecialWeapon" => "Special Weapon",
            _ => categoryName,
        };
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

    internal static bool ShouldOnlyLimitNonVanillaWeapons()
    {
        return _instance?._onlyLimitNonVanillaWeapons?.Value ?? false;
    }

    internal static bool IsHandlingPenaltyEnabled()
    {
        return _instance?._enableHandlingPenalty?.Value ?? false;
    }

    internal static bool ShouldOnlyLimitAdditionalWeaponsForHandling()
    {
        return _instance?._onlyLimitAdditionalWeaponsForHandling?.Value ?? false;
    }

    internal static int GetHandlingErgoPenalty()
    {
        return Math.Max(1, _instance?._handlingErgoPenalty?.Value ?? 10);
    }

    internal static bool ShouldIncludeNonFoldableWeaponsForHandling()
    {
        return _instance?._includeNonFoldableWeaponsForHandling?.Value ?? false;
    }

    internal static void LogPatchIssue(string message)
    {
        _instance?.Logger.LogWarning(message);
    }

    internal static void ShowNoFreeSlotForThatItemWarning()
    {
        NotificationWarning.Show(NoFreeSlotForThatItemMessage);
    }

    private static class NotificationWarning
    {
        private delegate void WarningNotificationCaller(string message, object duration);

        private static readonly Type? NotificationManagerType = AccessTools.TypeByName("NotificationManagerClass");
        private static readonly Type? NotificationDurationType = AccessTools.TypeByName("EFT.Communications.ENotificationDurationType");
        private static readonly MethodInfo? DisplayWarningNotificationMethod = ResolveDisplayWarningNotificationMethod();
        private static readonly WarningNotificationCaller? DisplayWarningNotificationCaller = CreateWarningNotificationCaller();
        private static readonly object? DefaultNotificationDuration = ResolveDefaultNotificationDuration();
        private static DateTime _lastWarningAt = DateTime.MinValue;

        internal static void Show(string message)
        {
            if (DisplayWarningNotificationCaller is null || DefaultNotificationDuration is null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastWarningAt).TotalMilliseconds < 1500)
            {
                return;
            }

            _lastWarningAt = now;

            try
            {
                DisplayWarningNotificationCaller(message, DefaultNotificationDuration);
            }
            catch
            {
            }
        }

        private static MethodInfo? ResolveDisplayWarningNotificationMethod()
        {
            if (NotificationManagerType is null || NotificationDurationType is null)
            {
                return null;
            }

            return AccessTools.Method(NotificationManagerType, "DisplayWarningNotification", [typeof(string), NotificationDurationType]);
        }

        private static object? ResolveDefaultNotificationDuration()
        {
            if (NotificationDurationType is null)
            {
                return null;
            }

            try
            {
                return Enum.Parse(NotificationDurationType, "Default");
            }
            catch
            {
                return null;
            }
        }

        private static WarningNotificationCaller? CreateWarningNotificationCaller()
        {
            if (DisplayWarningNotificationMethod is null || NotificationDurationType is null)
            {
                return null;
            }

            var messageParameter = Expression.Parameter(typeof(string), "message");
            var durationParameter = Expression.Parameter(typeof(object), "duration");
            var call = Expression.Call(
                DisplayWarningNotificationMethod,
                messageParameter,
                Expression.Convert(durationParameter, NotificationDurationType)
            );

            return Expression.Lambda<WarningNotificationCaller>(call, messageParameter, durationParameter).Compile();
        }
    }

    [HarmonyPatch]
    private static class HolsterSlotSizeClientPatch
    {
        private delegate object? ObjectGetter(object instance);
        private delegate string? StringGetter(object instance);
        private delegate int IntGetter(object instance);
        private delegate bool BoolGetter(object instance);
        private delegate object? ObjectMethodCaller(object instance);
        private delegate object? ObjectMethodWithTwoObjectsCaller(object instance, object arg0, object arg1);
        private delegate object? ObjectMethodWithBoolCaller(object instance, object arg0, object arg1, bool arg2);
        private delegate object? ObjectMethodWithObjectBoolTwoObjectsCaller(object instance, object? arg0, bool arg1, object? arg2, object? arg3);
        private delegate object? ObjectMethodWithTwoIntsCaller(object instance, int arg0, int arg1);

        private static readonly Type? SlotViewType = AccessTools.TypeByName("EFT.UI.DragAndDrop.SlotView");
        private static readonly Type? ItemContextType = AccessTools.TypeByName("ItemContextClass");
        private static readonly Type? ItemContextAbstractType = AccessTools.TypeByName("ItemContextAbstractClass");
        private static readonly Type? OperationType = AccessTools.TypeByName("GStruct153");
        private static readonly Type? ItemType = AccessTools.TypeByName("EFT.InventoryLogic.Item");
        private static readonly Type? SlotType = AccessTools.TypeByName("EFT.InventoryLogic.Slot");
        private static readonly Type? ItemAddressType = AccessTools.TypeByName("EFT.InventoryLogic.ItemAddress");
        private static readonly Type? CompoundItemType = AccessTools.TypeByName("EFT.InventoryLogic.CompoundItem");
        private static readonly Type? ExtraSizeType = AccessTools.TypeByName("EFT.InventoryLogic.ExtraSize");
        private static readonly Type? IncompatibleItemErrorType = AccessTools.TypeByName("GClass1585");
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
        private static readonly ObjectGetter? ItemCurrentAddressGetter = CreateGetter<ObjectGetter>(FindProperty(ItemType, "CurrentAddress"));
        private static readonly ObjectGetter? ItemAddressContainerGetter = CreateGetter<ObjectGetter>(FindField(ItemAddressType, "Container"));
        private static readonly MemberInfo? ItemTemplateMember = FindProperty(ItemType, "Template");
        private static readonly ObjectGetter? ItemTemplateGetter = CreateGetter<ObjectGetter>(ItemTemplateMember);
        private static readonly Type? ItemTemplateType = GetMemberType(ItemTemplateMember);
        private static readonly ObjectGetter? ItemTemplateParentGetter = CreateGetter<ObjectGetter>(FindProperty(ItemTemplateType, "Parent"));
        private static readonly StringGetter? ItemTemplateStringIdGetter = CreateGetter<StringGetter>(FindProperty(ItemTemplateType, "StringId"));
        private static readonly IntGetter? ItemWidthGetter = CreateGetter<IntGetter>(FindProperty(ItemType, "Width"));
        private static readonly IntGetter? ItemHeightGetter = CreateGetter<IntGetter>(FindProperty(ItemType, "Height"));

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
        private static readonly ObjectMethodCaller? CreateItemAddressCaller = CreateMethodCaller(FindMethod(SlotType, "CreateItemAddress"));
        private static readonly MethodInfo? GetSizeAfterAttachmentMethod = ResolveGetSizeAfterAttachmentMethod();
        private static readonly ObjectMethodWithTwoObjectsCaller? GetSizeAfterAttachmentCaller = CreateMethodWithTwoObjectsCaller(GetSizeAfterAttachmentMethod);
        private static readonly MethodInfo? CalculateExtraSizeMethod = ResolveCalculateExtraSizeMethod();
        private static readonly ObjectMethodWithObjectBoolTwoObjectsCaller? CalculateExtraSizeCaller = CreateMethodWithObjectBoolTwoObjectsCaller(CalculateExtraSizeMethod);
        private static readonly ObjectMethodWithTwoIntsCaller? ExtraSizeApplyCaller = CreateMethodWithTwoIntsCaller(ResolveExtraSizeApplyMethod());

        private static readonly bool CanValidateWeaponSize =
            WeaponType is not null
            && InventoryEquipmentType is not null
            && SlotIdGetter is not null
            && SlotNameGetter is not null
            && SlotParentItemGetter is not null
            && CalculateCellSizeCaller is not null
            && CellSizeXGetter is not null
            && CellSizeYGetter is not null;

        private static readonly bool CanValidateHolsterSize =
            CanValidateWeaponSize
            && (SlotGetter is not null || SlotFieldGetter is not null)
            && (DraggedItemGetter is not null || FallbackDraggedItemGetter is not null);

        internal static readonly bool CanValidateInventoryMoveSize =
            CanValidateWeaponSize
            && SlotType is not null
            && ItemCurrentAddressGetter is not null
            && ItemAddressContainerGetter is not null;

        private static readonly bool CanValidateHolsteredAttachmentSize =
            CanValidateInventoryMoveSize
            && CompoundItemType is not null
            && CreateItemAddressCaller is not null
            && GetSizeAfterAttachmentCaller is not null;

        private static readonly bool CanClassifyVanillaHolsterWeapon =
            ItemTemplateGetter is not null
            && ItemTemplateParentGetter is not null
            && ItemTemplateStringIdGetter is not null;

        private static readonly bool CanResolveUnfoldedSize =
            WeaponFoldedGetter is not null
            && WeaponCurrentAddressGetter is not null
            && GetFoldableCaller is not null
            && GetSizeAfterFoldingCaller is not null
            && CellSizeXGetter is not null
            && CellSizeYGetter is not null;

        private static readonly bool CanResolveUnfoldedAttachmentSize =
            CanValidateHolsteredAttachmentSize
            && WeaponFoldedGetter is not null
            && GetFoldableCaller is not null
            && CalculateExtraSizeCaller is not null
            && ExtraSizeApplyCaller is not null
            && ItemWidthGetter is not null
            && ItemHeightGetter is not null
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

            if (!CanValidateHolsteredAttachmentSize)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve required attachment members. Holstered weapon attachment size validation is disabled.");
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
            if (draggedItem is null)
            {
                return true;
            }

            if (WeaponType?.IsInstanceOfType(draggedItem) == true && IsHolsterSlot(slot))
            {
                if (ShouldBlockHolsterWeapon(draggedItem))
                {
                    SetIncompatibleOperation(__args, draggedItem);
                    __result = false;
                    return false;
                }

                return true;
            }

            if (ShouldBlockHolsteredWeaponAttachment(slot, draggedItem))
            {
                SetIncompatibleOperation(__args, draggedItem);
                __result = false;
                return false;
            }

            return true;
        }

        internal static bool ShouldBlockMoveToAddress(object? item, object? targetAddress)
        {
            if (!IsHolsterSizeLimitEnabled() || !CanValidateInventoryMoveSize || item is null || targetAddress is null)
            {
                return false;
            }

            var targetContainer = ItemAddressContainerGetter?.Invoke(targetAddress);
            if (targetContainer is null || SlotType?.IsInstanceOfType(targetContainer) != true)
            {
                return false;
            }

            if (WeaponType?.IsInstanceOfType(item) == true && IsHolsterSlot(targetContainer))
            {
                return ShouldBlockHolsterWeapon(item);
            }

            return ShouldBlockHolsteredWeaponAttachment(targetContainer, item);
        }

        internal static bool ShouldBlockWeaponMoveToHolsterAddress(object? item, object? targetAddress)
        {
            if (!IsHolsterSizeLimitEnabled() || !CanValidateInventoryMoveSize || item is null || targetAddress is null)
            {
                return false;
            }

            if (WeaponType?.IsInstanceOfType(item) != true)
            {
                return false;
            }

            var targetContainer = ItemAddressContainerGetter?.Invoke(targetAddress);
            return targetContainer is not null
                && SlotType?.IsInstanceOfType(targetContainer) == true
                && IsHolsterSlot(targetContainer)
                && ShouldBlockHolsterWeapon(item);
        }

        internal static bool ShouldBlockSwap(object? item, object? targetAddress, object? otherItem, object? otherTargetAddress)
        {
            return ShouldBlockMoveToAddress(item, targetAddress) || ShouldBlockMoveToAddress(otherItem, otherTargetAddress);
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

        private static MethodInfo? ResolveGetSizeAfterAttachmentMethod()
        {
            if (CompoundItemType is null || ItemAddressType is null || ItemType is null)
            {
                return null;
            }

            return AccessTools.Method(CompoundItemType, "GetSizeAfterAttachment", [ItemAddressType, ItemType]);
        }

        private static MethodInfo? ResolveCalculateExtraSizeMethod()
        {
            var foldableType = GetFoldableMethod?.ReturnType;
            if (CompoundItemType is null || foldableType is null || SlotType is null || ItemType is null)
            {
                return null;
            }

            return AccessTools.Method(CompoundItemType, "CalculateExtraSize", [foldableType, typeof(bool), SlotType, ItemType]);
        }

        private static MethodInfo? ResolveExtraSizeApplyMethod()
        {
            if (ExtraSizeType is null)
            {
                return null;
            }

            return AccessTools.Method(ExtraSizeType, "Apply", [typeof(int), typeof(int)]);
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

        private static bool IsVanillaHolsterWeapon(object weapon)
        {
            if (!CanClassifyVanillaHolsterWeapon)
            {
                return false;
            }

            var template = ItemTemplateGetter?.Invoke(weapon);
            if (template is null)
            {
                return false;
            }

            var templateId = ItemTemplateStringIdGetter?.Invoke(template);
            if (string.Equals(templateId, SignalPistolTemplateId, StringComparison.Ordinal))
            {
                return true;
            }

            var parentTemplate = ItemTemplateParentGetter?.Invoke(template);
            var parentId = parentTemplate is null ? null : ItemTemplateStringIdGetter?.Invoke(parentTemplate);

            return string.Equals(parentId, PistolCategoryId, StringComparison.Ordinal)
                || string.Equals(parentId, RevolverCategoryId, StringComparison.Ordinal);
        }

        private static void SetIncompatibleOperation(object[] args, object item)
        {
            if (args.Length < 3 || OperationType is null || IncompatibleItemErrorType is null)
            {
                return;
            }

            try
            {
                var error = Activator.CreateInstance(IncompatibleItemErrorType, item, null);
                if (error is null)
                {
                    return;
                }

                args[2] = Activator.CreateInstance(OperationType, error);
            }
            catch
            {
            }
        }

        private static bool ShouldBlockHolsterWeapon(object weapon)
        {
            if (ShouldOnlyLimitNonVanillaWeapons() && IsVanillaHolsterWeapon(weapon))
            {
                return false;
            }

            var maxWidth = GetMaxHolsterWidth();
            var maxHeight = GetMaxHolsterHeight();

            if (!TryGetCurrentSize(weapon, out var width, out var height))
            {
                return false;
            }

            if (width > maxWidth || height > maxHeight)
            {
                return true;
            }

            if (!ShouldIgnoreFoldState() || WeaponFoldedGetter?.Invoke(weapon) != true)
            {
                return false;
            }

            if (!TryGetUnfoldedSize(weapon, out width, out height))
            {
                return false;
            }

            return width > maxWidth || height > maxHeight;
        }

        private static bool ShouldBlockHolsteredWeaponAttachment(object targetSlot, object attachedItem)
        {
            if (!CanValidateHolsteredAttachmentSize)
            {
                return false;
            }

            var holsterWeapon = FindHolsteredWeaponForSlot(targetSlot);
            if (holsterWeapon is null)
            {
                return false;
            }

            if (ShouldOnlyLimitNonVanillaWeapons() && IsVanillaHolsterWeapon(holsterWeapon))
            {
                return false;
            }

            var maxWidth = GetMaxHolsterWidth();
            var maxHeight = GetMaxHolsterHeight();

            if (!TryGetSizeAfterAttachment(holsterWeapon, targetSlot, attachedItem, out var width, out var height))
            {
                return false;
            }

            if (width > maxWidth || height > maxHeight)
            {
                return true;
            }

            if (!ShouldIgnoreFoldState() || WeaponFoldedGetter?.Invoke(holsterWeapon) != true)
            {
                return false;
            }

            if (!TryGetUnfoldedSizeAfterAttachment(holsterWeapon, targetSlot, attachedItem, out width, out height))
            {
                return false;
            }

            return width > maxWidth || height > maxHeight;
        }

        private static object? FindHolsteredWeaponForSlot(object targetSlot)
        {
            var parentItem = SlotParentItemGetter?.Invoke(targetSlot);
            while (parentItem is not null)
            {
                if (WeaponType?.IsInstanceOfType(parentItem) == true && IsItemInHolsterSlot(parentItem))
                {
                    return parentItem;
                }

                parentItem = GetParentItem(parentItem);
            }

            return null;
        }

        private static object? GetParentItem(object item)
        {
            var currentAddress = ItemCurrentAddressGetter?.Invoke(item);
            var parentContainer = currentAddress is null ? null : ItemAddressContainerGetter?.Invoke(currentAddress);
            return parentContainer is not null && SlotType?.IsInstanceOfType(parentContainer) == true
                ? SlotParentItemGetter?.Invoke(parentContainer)
                : null;
        }

        private static bool IsItemInHolsterSlot(object item)
        {
            var currentAddress = ItemCurrentAddressGetter?.Invoke(item);
            var container = currentAddress is null ? null : ItemAddressContainerGetter?.Invoke(currentAddress);
            return container is not null && SlotType?.IsInstanceOfType(container) == true && IsHolsterSlot(container);
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

        private static bool TryGetSizeAfterAttachment(object weapon, object targetSlot, object attachedItem, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!CanValidateHolsteredAttachmentSize)
            {
                return false;
            }

            try
            {
                var targetAddress = CreateItemAddressCaller?.Invoke(targetSlot);
                if (targetAddress is null)
                {
                    return false;
                }

                var cellSize = GetSizeAfterAttachmentCaller?.Invoke(weapon, targetAddress, attachedItem);
                return TryReadCellSize(cellSize, out width, out height);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetUnfoldedSizeAfterAttachment(object weapon, object targetSlot, object attachedItem, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!CanResolveUnfoldedAttachmentSize)
            {
                return false;
            }

            try
            {
                var foldable = GetFoldableCaller?.Invoke(weapon);
                if (foldable is null || ItemWidthGetter is null || ItemHeightGetter is null)
                {
                    return false;
                }

                var extraSize = CalculateExtraSizeCaller?.Invoke(weapon, foldable, false, targetSlot, attachedItem);
                if (extraSize is null)
                {
                    return false;
                }

                var cellSize = ExtraSizeApplyCaller?.Invoke(extraSize, ItemWidthGetter(weapon), ItemHeightGetter(weapon));
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

        private static ObjectMethodWithTwoObjectsCaller? CreateMethodWithTwoObjectsCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var arg0Parameter = Expression.Parameter(typeof(object), "arg0");
            var arg1Parameter = Expression.Parameter(typeof(object), "arg1");
            var call = Expression.Call(
                Expression.Convert(instanceParameter, method.DeclaringType!),
                method,
                Expression.Convert(arg0Parameter, parameters[0].ParameterType),
                Expression.Convert(arg1Parameter, parameters[1].ParameterType)
            );

            return Expression.Lambda<ObjectMethodWithTwoObjectsCaller>(
                Expression.Convert(call, typeof(object)),
                instanceParameter,
                arg0Parameter,
                arg1Parameter
            ).Compile();
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

        private static ObjectMethodWithObjectBoolTwoObjectsCaller? CreateMethodWithObjectBoolTwoObjectsCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 4)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var arg0Parameter = Expression.Parameter(typeof(object), "arg0");
            var arg1Parameter = Expression.Parameter(typeof(bool), "arg1");
            var arg2Parameter = Expression.Parameter(typeof(object), "arg2");
            var arg3Parameter = Expression.Parameter(typeof(object), "arg3");
            var call = Expression.Call(
                Expression.Convert(instanceParameter, method.DeclaringType!),
                method,
                Expression.Convert(arg0Parameter, parameters[0].ParameterType),
                Expression.Convert(arg1Parameter, parameters[1].ParameterType),
                Expression.Convert(arg2Parameter, parameters[2].ParameterType),
                Expression.Convert(arg3Parameter, parameters[3].ParameterType)
            );

            return Expression.Lambda<ObjectMethodWithObjectBoolTwoObjectsCaller>(
                Expression.Convert(call, typeof(object)),
                instanceParameter,
                arg0Parameter,
                arg1Parameter,
                arg2Parameter,
                arg3Parameter
            ).Compile();
        }

        private static ObjectMethodWithTwoIntsCaller? CreateMethodWithTwoIntsCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var arg0Parameter = Expression.Parameter(typeof(int), "arg0");
            var arg1Parameter = Expression.Parameter(typeof(int), "arg1");
            var call = Expression.Call(
                Expression.Convert(instanceParameter, method.DeclaringType!),
                method,
                Expression.Convert(arg0Parameter, parameters[0].ParameterType),
                Expression.Convert(arg1Parameter, parameters[1].ParameterType)
            );

            return Expression.Lambda<ObjectMethodWithTwoIntsCaller>(
                Expression.Convert(call, typeof(object)),
                instanceParameter,
                arg0Parameter,
                arg1Parameter
            ).Compile();
        }
    }

    [HarmonyPatch]
    private static class HolsterInventoryMoveSizePatch
    {
        private static readonly Type? InteractionsHandlerType = AccessTools.TypeByName("InteractionsHandlerClass");
        private static readonly Type? ItemType = AccessTools.TypeByName("EFT.InventoryLogic.Item");
        private static readonly Type? ItemAddressType = AccessTools.TypeByName("EFT.InventoryLogic.ItemAddress");
        private static readonly Type? TraderControllerType = AccessTools.TypeByName("TraderControllerClass");

        private static readonly MethodBase? MoveMethod = ResolveMoveMethod();
        private static readonly MethodBase? SwapMethod = ResolveSwapMethod();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (MoveMethod is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve InteractionsHandlerClass.Move. Alt-click holster size validation is disabled.");
            }
            else
            {
                yield return MoveMethod;
            }

            if (SwapMethod is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve InteractionsHandlerClass.Swap. Swap holster size validation is disabled.");
            }
            else
            {
                yield return SwapMethod;
            }

            if (!HolsterSlotSizeClientPatch.CanValidateInventoryMoveSize)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve required inventory move members. Alt-click holster size validation is disabled.");
            }
        }

        private static bool Prefix(MethodBase __originalMethod, object[] __args, ref object __result)
        {
            if (__originalMethod == MoveMethod)
            {
                var item = __args.Length > 0 ? __args[0] : null;
                var targetAddress = __args.Length > 1 ? __args[1] : null;
                if (!HolsterSlotSizeClientPatch.ShouldBlockMoveToAddress(item, targetAddress))
                {
                    return true;
                }

                if (HolsterSlotSizeClientPatch.ShouldBlockWeaponMoveToHolsterAddress(item, targetAddress))
                {
                    ShowNoFreeSlotForThatItemWarning();
                }

                __result = CreateFailureResult(__originalMethod);
                return false;
            }

            if (__originalMethod == SwapMethod)
            {
                var item = __args.Length > 0 ? __args[0] : null;
                var targetAddress = __args.Length > 1 ? __args[1] : null;
                var otherItem = __args.Length > 2 ? __args[2] : null;
                var otherTargetAddress = __args.Length > 3 ? __args[3] : null;
                if (!HolsterSlotSizeClientPatch.ShouldBlockSwap(item, targetAddress, otherItem, otherTargetAddress))
                {
                    return true;
                }

                if (HolsterSlotSizeClientPatch.ShouldBlockWeaponMoveToHolsterAddress(item, targetAddress)
                    || HolsterSlotSizeClientPatch.ShouldBlockWeaponMoveToHolsterAddress(otherItem, otherTargetAddress))
                {
                    ShowNoFreeSlotForThatItemWarning();
                }

                __result = CreateFailureResult(__originalMethod);
                return false;
            }

            return true;
        }

        private static MethodBase? ResolveMoveMethod()
        {
            if (InteractionsHandlerType is null || ItemType is null || ItemAddressType is null || TraderControllerType is null)
            {
                return null;
            }

            return AccessTools.Method(InteractionsHandlerType, "Move", [ItemType, ItemAddressType, TraderControllerType, typeof(bool)]);
        }

        private static MethodBase? ResolveSwapMethod()
        {
            if (InteractionsHandlerType is null || ItemType is null || ItemAddressType is null || TraderControllerType is null)
            {
                return null;
            }

            return AccessTools.Method(InteractionsHandlerType, "Swap", [ItemType, ItemAddressType, ItemType, ItemAddressType, TraderControllerType, typeof(bool)]);
        }

        private static object CreateFailureResult(MethodBase method)
        {
            var returnType = (method as MethodInfo)?.ReturnType;
            var errorType = AccessTools.TypeByName("GClass1522");
            if (returnType is null || errorType is null)
            {
                return returnType is null ? new object() : Activator.CreateInstance(returnType)!;
            }

            var error = Activator.CreateInstance(errorType, NoFreeSlotForThatItemMessage);
            return Activator.CreateInstance(returnType, error)!;
        }
    }

    [HarmonyPatch]
    private static class HolsterHandlingPenaltyClientPatch
    {
        private delegate object? ObjectGetter(object instance);
        private delegate string? StringGetter(object instance);
        private delegate bool BoolGetter(object instance);
        private delegate object? ObjectMethodCaller(object instance);
        private delegate object? ObjectMethodWithObjectCaller(object instance, object arg0);

        private static readonly Type? PlayerType = AccessTools.TypeByName("EFT.Player");
        private static readonly Type? FirearmControllerType = ResolveFirearmControllerType();
        private static readonly Type? WeaponType = AccessTools.TypeByName("EFT.InventoryLogic.Weapon");
        private static readonly Type? ItemType = AccessTools.TypeByName("EFT.InventoryLogic.Item");
        private static readonly Type? InventoryEquipmentType = AccessTools.TypeByName("EFT.InventoryLogic.InventoryEquipment");
        private static readonly Type? EquipmentSlotType = AccessTools.TypeByName("EFT.InventoryLogic.EquipmentSlot");
        private static readonly Type? SlotType = AccessTools.TypeByName("EFT.InventoryLogic.Slot");

        private static readonly MethodBase? TotalErgonomicsGetter = FindProperty(FirearmControllerType, "TotalErgonomics")?.GetMethod;

        private static readonly ObjectGetter? FirearmControllerPlayerGetter = CreateGetter<ObjectGetter>(FindField(FirearmControllerType, "_player"));
        private static readonly ObjectGetter? FirearmControllerWeaponGetter = CreateGetter<ObjectGetter>(FindProperty(FirearmControllerType, "Weapon"));
        private static readonly BoolGetter? PlayerIsYourPlayerGetter = CreateGetter<BoolGetter>(FindProperty(PlayerType, "IsYourPlayer"));
        private static readonly ObjectGetter? PlayerEquipmentGetter = CreateGetter<ObjectGetter>(FindProperty(PlayerType, "Equipment"));
        private static readonly MethodInfo? GetEquipmentSlotMethod = ResolveGetEquipmentSlotMethod();
        private static readonly ObjectMethodWithObjectCaller? GetEquipmentSlotCaller = CreateMethodWithObjectCaller(GetEquipmentSlotMethod);
        private static readonly object? HolsterEquipmentSlotValue = ResolveHolsterEquipmentSlotValue();
        private static readonly ObjectGetter? SlotContainedItemGetter = CreateGetter<ObjectGetter>(FindProperty(SlotType, "ContainedItem"));
        private static readonly StringGetter? ItemIdGetter = CreateGetter<StringGetter>(FindProperty(ItemType, "Id"));
        private static readonly MemberInfo? ItemTemplateMember = FindProperty(ItemType, "Template");
        private static readonly ObjectGetter? ItemTemplateGetter = CreateGetter<ObjectGetter>(ItemTemplateMember);
        private static readonly Type? ItemTemplateType = GetMemberType(ItemTemplateMember);
        private static readonly ObjectGetter? ItemTemplateParentGetter = CreateGetter<ObjectGetter>(FindProperty(ItemTemplateType, "Parent"));
        private static readonly StringGetter? ItemTemplateStringIdGetter = CreateGetter<StringGetter>(FindProperty(ItemTemplateType, "StringId"));
        private static readonly MethodInfo? GetAllItemsMethod = FindMethod(ItemType, "GetAllItems");
        private static readonly ObjectMethodCaller? GetAllItemsCaller = CreateMethodCaller(GetAllItemsMethod);
        private static readonly MethodInfo? GetFoldableMethod = FindMethod(WeaponType, "GetFoldable");
        private static readonly ObjectMethodCaller? GetFoldableCaller = CreateMethodCaller(GetFoldableMethod);
        private static readonly BoolGetter? WeaponFoldedGetter = CreateGetter<BoolGetter>(FindProperty(WeaponType, "Folded"));

        private static readonly bool CanApplyHandlingPenalty =
            FirearmControllerPlayerGetter is not null
            && FirearmControllerWeaponGetter is not null
            && PlayerIsYourPlayerGetter is not null
            && PlayerEquipmentGetter is not null
            && GetEquipmentSlotCaller is not null
            && HolsterEquipmentSlotValue is not null
            && SlotContainedItemGetter is not null
            && ItemIdGetter is not null
            && ItemTemplateGetter is not null
            && ItemTemplateParentGetter is not null
            && ItemTemplateStringIdGetter is not null
            && GetFoldableCaller is not null
            && WeaponFoldedGetter is not null
            && WeaponType is not null;

        private static readonly bool CanInspectWeaponAttachments =
            GetAllItemsCaller is not null
            && ItemIdGetter is not null
            && ItemTemplateGetter is not null
            && ItemTemplateParentGetter is not null
            && ItemTemplateStringIdGetter is not null;

        private static MethodBase? TargetMethod()
        {
            if (TotalErgonomicsGetter is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve FirearmController.TotalErgonomics. Handling penalty is disabled.");
                return null;
            }

            if (!CanApplyHandlingPenalty)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve required handling penalty members. Handling penalty is disabled.");
            }

            return TotalErgonomicsGetter;
        }

        private static void Postfix(object __instance, ref float __result)
        {
            if (__result <= 0f || !IsHandlingPenaltyEnabled() || !CanApplyHandlingPenalty)
            {
                return;
            }

            var player = FirearmControllerPlayerGetter?.Invoke(__instance);
            if (player is null || PlayerIsYourPlayerGetter?.Invoke(player) != true)
            {
                return;
            }

            var weaponInHands = FirearmControllerWeaponGetter?.Invoke(__instance);
            if (weaponInHands is null || WeaponType?.IsInstanceOfType(weaponInHands) != true)
            {
                return;
            }

            var holsterWeapon = GetHolsterWeapon(player);
            if (holsterWeapon is null || WeaponType?.IsInstanceOfType(holsterWeapon) != true)
            {
                return;
            }

            if (ReferenceEquals(holsterWeapon, weaponInHands) || AreSameItem(holsterWeapon, weaponInHands))
            {
                return;
            }

            var isVanillaHolsterWeapon = IsVanillaHolsterWeapon(holsterWeapon);
            if (ShouldOnlyLimitAdditionalWeaponsForHandling() && isVanillaHolsterWeapon)
            {
                return;
            }

            if (isVanillaHolsterWeapon && !HasInstalledStockAttachment(holsterWeapon))
            {
                return;
            }

            if (!ShouldApplyHandlingPenalty(holsterWeapon))
            {
                return;
            }

            __result = Math.Max(0f, __result - GetHandlingErgoPenalty());
        }

        private static Type? ResolveFirearmControllerType()
        {
            return AccessTools.TypeByName("EFT.Player+FirearmController")
                ?? AccessTools.Inner(PlayerType, "FirearmController");
        }

        private static MethodInfo? ResolveGetEquipmentSlotMethod()
        {
            if (InventoryEquipmentType is null || EquipmentSlotType is null)
            {
                return null;
            }

            return AccessTools.Method(InventoryEquipmentType, "GetSlot", [EquipmentSlotType]);
        }

        private static object? ResolveHolsterEquipmentSlotValue()
        {
            if (EquipmentSlotType is null)
            {
                return null;
            }

            return Enum.Parse(EquipmentSlotType, "Holster");
        }

        private static object? GetHolsterWeapon(object player)
        {
            var equipment = PlayerEquipmentGetter?.Invoke(player);
            if (equipment is null)
            {
                return null;
            }

            var holsterSlot = GetEquipmentSlotCaller?.Invoke(equipment, HolsterEquipmentSlotValue!);
            return holsterSlot is null ? null : SlotContainedItemGetter?.Invoke(holsterSlot);
        }

        private static bool AreSameItem(object left, object right)
        {
            var leftId = ItemIdGetter?.Invoke(left);
            var rightId = ItemIdGetter?.Invoke(right);
            return !string.IsNullOrWhiteSpace(leftId)
                && !string.IsNullOrWhiteSpace(rightId)
                && string.Equals(leftId, rightId, StringComparison.Ordinal);
        }

        private static bool ShouldApplyHandlingPenalty(object holsterWeapon)
        {
            var foldable = GetFoldableCaller?.Invoke(holsterWeapon);
            if (foldable is not null)
            {
                return WeaponFoldedGetter?.Invoke(holsterWeapon) != true;
            }

            return ShouldIncludeNonFoldableWeaponsForHandling();
        }

        private static bool IsVanillaHolsterWeapon(object weapon)
        {
            var template = ItemTemplateGetter?.Invoke(weapon);
            if (template is null)
            {
                return false;
            }

            var templateId = ItemTemplateStringIdGetter?.Invoke(template);
            if (string.Equals(templateId, SignalPistolTemplateId, StringComparison.Ordinal))
            {
                return true;
            }

            var parentTemplate = ItemTemplateParentGetter?.Invoke(template);
            var parentId = parentTemplate is null ? null : ItemTemplateStringIdGetter?.Invoke(parentTemplate);

            return string.Equals(parentId, PistolCategoryId, StringComparison.Ordinal)
                || string.Equals(parentId, RevolverCategoryId, StringComparison.Ordinal);
        }

        private static bool HasInstalledStockAttachment(object weapon)
        {
            if (!CanInspectWeaponAttachments)
            {
                return false;
            }

            System.Collections.IEnumerable? allItems;
            try
            {
                allItems = GetAllItemsCaller?.Invoke(weapon) as System.Collections.IEnumerable;
            }
            catch
            {
                return false;
            }

            if (allItems is null)
            {
                return false;
            }

            var weaponId = ItemIdGetter?.Invoke(weapon);
            foreach (var item in allItems)
            {
                if (item is null || ReferenceEquals(item, weapon))
                {
                    continue;
                }

                var itemId = ItemIdGetter?.Invoke(item);
                if (!string.IsNullOrWhiteSpace(weaponId) && string.Equals(itemId, weaponId, StringComparison.Ordinal))
                {
                    continue;
                }

                var template = ItemTemplateGetter?.Invoke(item);
                if (template is null)
                {
                    continue;
                }

                var parentTemplate = ItemTemplateParentGetter?.Invoke(template);
                var parentId = parentTemplate is null ? null : ItemTemplateStringIdGetter?.Invoke(parentTemplate);
                if (string.Equals(parentId, StockCategoryId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

        private static ObjectMethodWithObjectCaller? CreateMethodWithObjectCaller(MethodInfo? method)
        {
            if (method is null)
            {
                return null;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return null;
            }

            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var arg0Parameter = Expression.Parameter(typeof(object), "arg0");
            var call = Expression.Call(
                Expression.Convert(instanceParameter, method.DeclaringType!),
                method,
                Expression.Convert(arg0Parameter, parameters[0].ParameterType)
            );

            return Expression.Lambda<ObjectMethodWithObjectCaller>(
                Expression.Convert(call, typeof(object)),
                instanceParameter,
                arg0Parameter
            ).Compile();
        }
    }

    [HarmonyPatch]
    private static class HolsterHandlingInventoryRefreshPatch
    {
        private delegate object? ObjectGetter(object instance);
        private delegate bool BoolGetter(object instance);
        private delegate object? ObjectMethodCaller(object instance);

        private static readonly Type? PlayerType = AccessTools.TypeByName("EFT.Player");
        private static readonly Type? FirearmControllerType = ResolveFirearmControllerType();
        private static readonly MethodBase? SetInventoryOpenedMethod = AccessTools.Method(FirearmControllerType, "SetInventoryOpened", [typeof(bool)]);
        private static readonly ObjectGetter? FirearmControllerPlayerGetter = CreateGetter<ObjectGetter>(FindField(FirearmControllerType, "_player"));
        private static readonly BoolGetter? PlayerIsYourPlayerGetter = CreateGetter<BoolGetter>(FindProperty(PlayerType, "IsYourPlayer"));
        private static readonly ObjectMethodCaller? WeaponModifiedCaller = CreateMethodCaller(FindMethod(FirearmControllerType, "WeaponModified"));

        private static readonly bool CanRefreshHandlingPenalty =
            FirearmControllerPlayerGetter is not null
            && PlayerIsYourPlayerGetter is not null
            && WeaponModifiedCaller is not null;

        private static MethodBase? TargetMethod()
        {
            if (SetInventoryOpenedMethod is null)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve FirearmController.SetInventoryOpened. Holster handling refresh is disabled.");
                return null;
            }

            if (!CanRefreshHandlingPenalty)
            {
                LogPatchIssue("HolsterEverything client patch could not resolve required inventory refresh members. Holster handling refresh is disabled.");
            }

            return SetInventoryOpenedMethod;
        }

        private static void Postfix(object __instance, bool __0)
        {
            if (__0 || !CanRefreshHandlingPenalty)
            {
                return;
            }

            var player = FirearmControllerPlayerGetter?.Invoke(__instance);
            if (player is null || PlayerIsYourPlayerGetter?.Invoke(player) != true)
            {
                return;
            }

            try
            {
                WeaponModifiedCaller?.Invoke(__instance);
            }
            catch
            {
            }
        }

        private static Type? ResolveFirearmControllerType()
        {
            return AccessTools.TypeByName("EFT.Player+FirearmController")
                ?? AccessTools.Inner(PlayerType, "FirearmController");
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
    }
}
