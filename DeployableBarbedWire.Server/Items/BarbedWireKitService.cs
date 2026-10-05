using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace DeployableBarbedWire.Server.Items;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload)]
public sealed class BarbedWireKitService(
    CustomItemService customItemService,
    TemplateTable templates,
    TradersTable traders,
    GlobalTable globals,
    DeployableBarbedWireServerConfig config,
    ISptLogger<BarbedWireKitService> logger) : IOnLoad
{
    public static readonly MongoId ItemId = new("6aa4e71b2f4c8d0935e6a201");

    private const string LegacyModuleTypeName = "SPTEssentials.Server.Items.BarbedWireKitService";
    private static readonly MongoId BasePlantingKitId = new("666b11055a706400b717cfa5");
    private static readonly MongoId PlantingKitParentId = new("6672e40ebb23210ae87d39eb");
    private static readonly MongoId AssortmentItemId = new("6aa4e71b2f4c8d0935e6a202");
    private const string HandbookParentId = "5b47574386f77428ca22b345";
    private const double Price = 25_000;
    private const int MaximumKits = 2;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (LegacyEssentialsContainsModule())
        {
            logger.Error($"{ModInfo.LogPrefix} The installed SPT Essentials build still contains Deployable Barbed Wire. The standalone server component has been disabled.");
            return Task.CompletedTask;
        }

        if (!config.Enabled)
        {
            logger.Info($"{ModInfo.LogPrefix} Deployable Barbed Wire is disabled.");
            return Task.CompletedTask;
        }

        if (templates.Items.ContainsKey(ItemId))
        {
            logger.Warning($"{ModInfo.LogPrefix} Barbed Wire Kit already exists; registration skipped.");
            return Task.CompletedTask;
        }

        var locale = new LocaleDetails
        {
            Name = "Barbed Wire Kit",
            ShortName = "Wire Kit",
            Description = "A portable coil of barbed wire. Use the configured placement key in a raid to deploy it. Up to two kits may be carried at once."
        };

        var result = customItemService.CreateItemFromClone(
            new NewItemFromCloneDetails
            {
                ItemTplToClone = BasePlantingKitId,
                ParentId = PlantingKitParentId,
                NewId = ItemId,
                NewItemName = "deployable_barbed_wire_kit",
                HandbookParentId = HandbookParentId,
                HandbookPriceRoubles = Price,
                FleaPriceRoubles = Price,
                AddToHandbook = true,
                AddToFleaPriceDb = true,
                OverrideProperties = new TemplateItemProperties
                {
                    BackgroundColor = "yellow",
                    CanRequireOnRagfair = false,
                    CanSellOnRagfair = true,
                    ExaminedByDefault = true,
                    Height = 1,
                    Name = "deployable_barbed_wire_kit",
                    RarityPvE = "Rare",
                    Weight = 2.5,
                    Width = 1
                },
                Locales = new Dictionary<string, LocaleDetails>
                {
                    ["en"] = locale,
                    ["de"] = locale
                }
            },
            Assembly.GetExecutingAssembly());

        if (!result.Success)
        {
            logger.Error($"{ModInfo.LogPrefix} Barbed Wire Kit registration failed: {string.Join("; ", result.Errors)}");
            return Task.CompletedTask;
        }

        AddToSpecialSlotFilters();
        AddRaidRestriction();
        AddPraporOffer();
        logger.Success($"{ModInfo.LogPrefix} Barbed Wire Kit registered at Prapor LL1 with a two-kit raid limit.");
        return Task.CompletedTask;
    }

    private static bool LegacyEssentialsContainsModule()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetType(LegacyModuleTypeName, false) is not null);
    }

    private void AddRaidRestriction()
    {
        if (globals.Configuration.RestrictionsInRaid.Any(restriction => restriction.TemplateId == ItemId))
        {
            return;
        }

        globals.Configuration.RestrictionsInRaid =
        [
            .. globals.Configuration.RestrictionsInRaid,
            new RestrictionsInRaid
            {
                MaxInLobby = MaximumKits,
                MaxInRaid = MaximumKits,
                TemplateId = ItemId
            }
        ];
    }

    private void AddPraporOffer()
    {
        var prapor = traders.GetTrader(Traders.PRAPOR);
        if (prapor?.Assort is null || prapor.Assort.Items.Any(item => item.Id == AssortmentItemId))
        {
            return;
        }

        prapor.Assort.Items.Add(new Item
        {
            Id = AssortmentItemId,
            Template = ItemId,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                StackObjectsCount = MaximumKits,
                UnlimitedCount = true,
                BuyRestrictionMax = MaximumKits,
                BuyRestrictionCurrent = 0
            }
        });

        prapor.Assort.BarterScheme[AssortmentItemId] =
        [
            [new BarterScheme { Count = Price, Template = Money.ROUBLES }]
        ];
        prapor.Assort.LoyalLevelItems[AssortmentItemId] = 1;
    }

    private void AddToSpecialSlotFilters()
    {
        foreach (var pockets in templates.Items.Values.Where(IsPocketTemplate))
        {
            foreach (var slot in pockets.Properties!.Slots!)
            {
                if (slot?.Name?.StartsWith("SpecialSlot", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                foreach (var filter in slot.Properties?.Filters ?? [])
                {
                    filter.Filter ??= [];
                    filter.Filter.Add(ItemId);
                }
            }
        }
    }

    private static bool IsPocketTemplate(TemplateItem item)
    {
        return string.Equals(item.Properties?.Name, "Pockets", StringComparison.OrdinalIgnoreCase)
            && item.Properties?.Slots is not null;
    }
}
