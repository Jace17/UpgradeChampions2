using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ShinyShoe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpgradeChampions2
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class UpgradeChampions2 : BaseUnityPlugin
    {
        public const string pluginGuid = "com.jacelendro.upgradechampions2";
        public const string pluginName = "Upgrade Champions 2";
        public const string pluginVersion = "1.0";

        public void Awake()
        {
            Logger.LogInfo("Loading Upgrade Champions 2...");

            var harmony = new Harmony(pluginGuid);
            harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(DeckScreen), "CollectCardsForMode")]
    public class AddChampionCardsToUpgradeList
    {
        public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("AddChampionCardsToUpgradeList");
        public static void Postfix(SaveManager ___saveManager, DeckScreen.Mode ___mode, CardState ___forceExcludeCard, CardType ___cardTypeFilter, bool ___ignoreUpgradeLimit,
            GrantableRewardData.Source ___rewardSource, AllGameData ___allGameData, RelicManager ___relicManager, object ___cardUpgradesToApply, CardUpgradeMaskData ___cardUpgradeMaskData,
            bool ___excludeFilteredOutCards, object ___defaultFilters, ref List<CardState> __result)
        {
            if (___mode == DeckScreen.Mode.ApplyUpgrade || ___mode == DeckScreen.Mode.SpellMergeSelection)
            {
                Log.LogInfo("Adding champion cards to upgrade list...");

                // Get all champion cards in deck
                List<CardState> championCards = new List<CardState>(___saveManager.GetDeckState());
                championCards = (from x in championCards
                                 where x.IsChampionCard()
                                 select x).ToList<CardState>();

                // No champion cards in deck, do nothing
                if (championCards.IsNullOrEmpty())
                {
                    Log.LogInfo("No champion cards found.");
                    return;
                }
                else
                {
                    Log.LogInfo($"Champion cards found: {string.Join(", ", championCards.Select(c => c.GetAssetName()))}");
                }

                var deckScreenType = AccessTools.Inner(typeof(DeckScreen), "CardUpgradesToApply");
                List<CardUpgradeData> upgradeDatas = ___cardUpgradesToApply == null ? null : AccessTools.Field(deckScreenType, "UpgradeDatas").GetValue(___cardUpgradesToApply) as List<CardUpgradeData>;
                List<CardUpgradeMaskData> cardUpgradeMaskDatas = new List<CardUpgradeMaskData>();

                if (upgradeDatas.IsNullOrEmpty())
                {
                    Log.LogInfo("No upgrade datas found.");
                }
                else
                {
                    foreach (CardUpgradeData upgradeData in upgradeDatas)
                    {
                        Log.LogInfo($"Found upgrade data: {upgradeData.GetUpgradeTitleKey().LocalizeEnglish()}");
                        if (!upgradeData.GetFilters().IsNullOrEmpty())
                        {
                            Log.LogInfo($"Found {upgradeData.GetFilters().Count} filters for upgrade data: {upgradeData.GetUpgradeTitleKey().LocalizeEnglish()}");
                            cardUpgradeMaskDatas.AddRange(upgradeData.GetFilters());
                        }
                        else
                        {
                            Log.LogInfo($"No filters found for upgrade data: {upgradeData.GetUpgradeTitleKey().LocalizeEnglish()}");
                        }
                    }
                }

                if (___cardUpgradeMaskData == null)
                {
                    Log.LogInfo("Card upgrade mask data is null.");
                }
                else
                {
                    Log.LogInfo($"Found card upgrade mask data: {___cardUpgradeMaskData.GetName()}");
                    cardUpgradeMaskDatas.Add(___cardUpgradeMaskData);
                }

                // Add champion cards to upgrade list
                foreach (CardState card in championCards)
                {
                    // Check if card is already in the upgrade list
                    if (__result.Contains(card))
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} is already in the upgrade list, skipping.");
                        continue;
                    }

                    // Check if card is excluded from upgrade
                    if (___forceExcludeCard != null && ___forceExcludeCard.GetID() == card.GetID())
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} is excluded from this ugprade, skipping.");
                        continue;
                    }

                    // Check if card matches the card type filter
                    if (___cardTypeFilter != CardType.Invalid && card.GetCardType() != ___cardTypeFilter)
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} does not match the card type filter {___cardTypeFilter}, skipping.");
                        continue;
                    }
                    else
                    {
                        card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NONE;
                    }

                    // Check if card is eligible for upgrade based on upgrade slots and relic effects
                    if (!___ignoreUpgradeLimit && ___rewardSource != GrantableRewardData.Source.Event)
                    {
                        List<IModifyCardUpgradeSlotCountRelicEffect> relicEffects;
                        using (GenericPools.GetList<IModifyCardUpgradeSlotCountRelicEffect>(out relicEffects))
                        {
                            int visibleUpgradeCount = card.GetVisibleUpgradeCount();
                            BalanceData balanceData = ___allGameData.GetBalanceData();
                            CardState cardState2 = card;
                            RelicManager relicManager = ___relicManager;
                            if (visibleUpgradeCount >= balanceData.GetUpgradeSlots(cardState2, (relicManager != null) ? relicManager.GetRelicEffects<IModifyCardUpgradeSlotCountRelicEffect>(relicEffects) : null, ___relicManager))
                            {
                                Log.LogInfo($"Card {card.GetAssetName()} has reached the maximum upgrade slots ({visibleUpgradeCount}).");
                                if (___excludeFilteredOutCards)
                                {
                                    continue;
                                }
                                else
                                {
                                    card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NoSlots;
                                }
                            }
                        }
                    }

                    // Skip adding card
                    bool skip = false;

                    // Check if card is eligible for upgrade based on upgrade mask data
                    if (cardUpgradeMaskDatas.IsNullOrEmpty())
                    {
                        Log.LogInfo("No card upgrade mask data found, skipping upgrade eligibility check.");
                    }
                    else
                    {
                        foreach (CardUpgradeMaskData cardUpgradeMaskData in cardUpgradeMaskDatas)
                        {
                            // Skip if the mask data is not applicable
                            if (cardUpgradeMaskData.GetName() == "ExcludeConsume" || cardUpgradeMaskData.GetName() == "OnlyEquipment_EquipmentMerge")
                            {   
                                Log.LogInfo($"Skipping {cardUpgradeMaskData.GetName()} card for upgrade eligibility check.");
                                skip = true;
                                break;
                            }

                            if (cardUpgradeMaskData.GetName() == "OnlyUnitExcludingChamps")
                            {
                                Log.LogInfo($"Skipping OnlyUnitExcludingChamps card for upgrade eligibility check.");
                                continue;
                            }

                            Log.LogInfo($"Checking upgrade eligibility for {card.GetAssetName()} with filter {cardUpgradeMaskData.GetName()}.");
                            if (!cardUpgradeMaskData.FilterCard<CardState>(card, ___relicManager))
                            {
                                Log.LogInfo($"Champion {card.GetAssetName()} is not eligible for upgrade due to reason: {cardUpgradeMaskData.GetUpgradeDisabledReason()}.");
                                if (cardUpgradeMaskData.GetUpgradeDisabledReason() == CardState.UpgradeDisabledReason.NONE)
                                {
                                    // If the upgrade mask data has no disabled reason, we assume it's a special case that should be skipped
                                    if (cardUpgradeMaskData.GetName() == "ExcludeQuick" || cardUpgradeMaskData.GetName() == "ExcludeEndless" || cardUpgradeMaskData.GetName() == "ExcludeSmallest"
                                        || cardUpgradeMaskData.GetName() == "ExcludeTitanite" || cardUpgradeMaskData.GetName() == "ExcludeDualism" || cardUpgradeMaskData.GetName() == "ExcludeUnitsWithAbilities")
                                    {
                                        if (!card.IsCurrentlyDisabled())
                                        {
                                            Log.LogInfo($"Champion {card.GetAssetName()} is not eligible for upgrade due to special case: {cardUpgradeMaskData.GetName()}.");
                                            card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NotEligible;
                                        }
                                    }
                                    else
                                    {
                                        skip = true;
                                        break;
                                    }
                                }
                                else if (!card.IsCurrentlyDisabled())
                                {
                                    card.CurrentDisabledReason = cardUpgradeMaskData.GetUpgradeDisabledReason();
                                }
                            }
                            else
                            {
                                Log.LogInfo($"Champion {card.GetAssetName()} passed upgrade filter {cardUpgradeMaskData.GetName()}.");
                            }
                        }
                    }

                    if (!skip)
                    {
                        Log.LogInfo($"Added {card.GetAssetName()} to upgrade list.");
                        __result.Add(card);
                    }
                }
            }
        }
    }
}
