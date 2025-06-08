using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ShinyShoe;

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
        public static void Postfix(SaveManager ___saveManager, DeckScreen.Mode ___mode, RelicEffectData ___relicEffectData, object ___cardUpgradesToApply, CardUpgradeMaskData ___cardUpgradeMaskData, List<CardState> __result,
             CardState ___forceExcludeCard, CardType ___cardTypeFilter, bool ___ignoreUpgradeLimit, GrantableRewardData.Source ___rewardSource, RelicManager ___relicManager, bool ___excludeFilteredOutCards)
        {
            if (___mode == DeckScreen.Mode.ApplyUpgrade || ___mode == DeckScreen.Mode.SpellMergeSelection)
            {
                Log.LogInfo($"Adding champion cards to upgrade list for mode: {___mode}...");

                // Get all champion cards in deck
                List<CardState> championCards = new List<CardState>(___saveManager.GetDeckState());
                championCards = [.. (from x in championCards
                                 where x.IsChampionCard()
                                 select x)];

                // No champion cards in deck, do nothing
                if (championCards.IsNullOrEmpty())
                {
                    Log.LogInfo("No champion cards found.");
                    return;
                }

                // Filter by relic effect
                if (___relicEffectData != null)
                {
                    Log.LogInfo($"Filtering by relic effect data...");
                    if (___relicEffectData.GetParamCardType() != CardType.Invalid)
                    {
                        championCards.RemoveAll((CardState c) => c.GetCardType() != ___relicEffectData.GetParamCardType());
                    }
                    if (!___relicEffectData.GetParamCharacterSubtype().IsNone)
                    {
                        for (int i = championCards.Count - 1; i >= 0; i--)
                        {
                            foreach (CardEffectState cardEffectState in championCards[i].GetEffectStates())
                            {
                                if (cardEffectState.GetCardEffect() is CardEffectSpawnMonster && !cardEffectState.GetParamCharacterData().GetSubtypes().Contains(___relicEffectData.GetParamCharacterSubtype()))
                                {
                                    championCards.RemoveAt(i);
                                }
                            }
                        }
                    }
                    if (___relicEffectData.GetUseIntRange())
                    {
                        championCards.RemoveAll((CardState c) => c.GetCostWithoutAnyModifications() < ___relicEffectData.GetParamMinInt() || c.GetCostWithoutAnyModifications() > ___relicEffectData.GetParamMaxInt());
                    }
                }

                // No champion cards in deck, do nothing
                if (championCards.IsNullOrEmpty())
                {
                    Log.LogInfo("No champion cards left after checking relic effect data.");
                    return;
                }


                // Get filters from upgrade data
                List<CardUpgradeMaskData> cardUpgradeMaskDatas = [];
                if (___cardUpgradesToApply != null)
                {
                    List<CardUpgradeData> upgradeDatas = (List<CardUpgradeData>)Traverse.Create(___cardUpgradesToApply).Field("UpgradeDatas").GetValue();
                    if (!upgradeDatas.IsNullOrEmpty())
                    {
                        foreach (CardUpgradeData upgradeData in upgradeDatas)
                        {
                            Log.LogInfo($"Found upgrade data: {upgradeData.Cheat_GetNameEnglish()} ({upgradeData.GetDebugName()}).");
                            cardUpgradeMaskDatas.AddRange(upgradeData.GetFilters());
                        }
                    }
                }

                // Get filter from deck screen
                if (___cardUpgradeMaskData != null)
                {
                    cardUpgradeMaskDatas.Add(___cardUpgradeMaskData);
                }

                // Log all the collected filters
                if (!cardUpgradeMaskDatas.IsNullOrEmpty())
                {
                    Log.LogInfo($"Found {cardUpgradeMaskDatas.Count} filter(s) to check :");
                    foreach (CardUpgradeMaskData cardUpgradeMaskData in cardUpgradeMaskDatas)
                    {
                        Log.LogInfo($"{cardUpgradeMaskData.name}");
                    }
                }

                // Validate each card and then add them to result if they passed
                foreach (CardState card in championCards)
                {
                    // Check if card is already in the upgrade list
                    if (__result.Contains(card))
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} is already in the upgrade list, skipping.");
                        continue;
                    }

                    // Check if force exclued
                    if (___forceExcludeCard == card)
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} is force excluded, skipping.");
                        continue;
                    }

                    // Check if card matches the card type filter
                    if (___cardTypeFilter != CardType.Invalid && card.GetCardType() != ___cardTypeFilter)
                    {
                        Log.LogInfo($"Card {card.GetAssetName()} does not match the card type filter {___cardTypeFilter}, skipping.");
                        continue;
                    }
                    card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NONE;

                    // Check if card is eligible for upgrade based on upgrade slots
                    if (!___ignoreUpgradeLimit && ___rewardSource != GrantableRewardData.Source.Event)
                    {
                        using (GenericPools.GetList<IModifyCardUpgradeSlotCountRelicEffect>(out List<IModifyCardUpgradeSlotCountRelicEffect> relicEffects))
                        {
                            int visibleUpgradeCount = card.GetVisibleUpgradeCount();
                            int maximumUpgradeCount = ___saveManager.GetBalanceData().GetUpgradeSlots(card, (___relicManager != null) ? ___relicManager.GetRelicEffects<IModifyCardUpgradeSlotCountRelicEffect>(relicEffects) : null, ___relicManager);
                            if (visibleUpgradeCount >= maximumUpgradeCount)
                            {
                                Log.LogInfo($"Card {card.GetAssetName()} has reached the maximum upgrade slots ({visibleUpgradeCount}/{maximumUpgradeCount}).");
                                if (!___excludeFilteredOutCards)
                                {
                                    card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NoSlots;
                                    __result.Add(card);
                                }
                                continue;
                            }
                        }
                    }

                    // If there are no filters, add card to the result
                    if (cardUpgradeMaskDatas.IsNullOrEmpty())
                    {
                        __result.Add(card);
                        continue;
                    }

                    bool addCardToResult = true;
                    // Validate card for each filter
                    foreach (CardUpgradeMaskData cardUpgradeMaskData in cardUpgradeMaskDatas)
                    {
                        Log.LogInfo($"Checking upgrade eligibility for card {card.GetAssetName()} with filter {cardUpgradeMaskData.GetName()}.");

                        // Skip specific filters
                        if (cardUpgradeMaskData.GetName() == "OnlyUnitExcludingChamps")
                        {
                            continue;
                        }
                        else if (cardUpgradeMaskData.GetName() == "OnlyEquipment" || cardUpgradeMaskData.GetName() == "OnlySpells" || cardUpgradeMaskData.GetName() == "OnlyEquipment_EquipmentMerge")
                        {
                            addCardToResult = false;
                            break;
                        }

                        if (!cardUpgradeMaskData.FilterCard<CardState>(card, ___relicManager))
                        {
                            Log.LogInfo($"Card {card.GetAssetName()} is not eligible for upgrade due to reason: {cardUpgradeMaskData.GetUpgradeDisabledReason()}.");
                            if (___excludeFilteredOutCards)
                            {
                                addCardToResult = false;
                            }
                            else if (cardUpgradeMaskData.GetUpgradeDisabledReason() != CardState.UpgradeDisabledReason.NONE)
                            {
                                card.CurrentDisabledReason = cardUpgradeMaskData.GetUpgradeDisabledReason();
                            }
                            else
                            {
                                card.CurrentDisabledReason = CardState.UpgradeDisabledReason.NotEligible;
                            }
                            break;
                        }
                    }
                    if (addCardToResult)
                    {
                        __result.Add(card);
                    }
                }
            }
        }
    }
}
