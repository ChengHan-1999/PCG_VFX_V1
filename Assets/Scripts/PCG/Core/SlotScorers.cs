using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCG.VFX
{
    public static class WeaponSlotScorer
    {
        public static SlotGenerationResult Generate(PlayerProfile profile, ModuleDefinitionSet modules, AlgorithmConfig config, System.Random random)
        {
            PcgLookup lookup = PcgLookupBuilder.FromProfile(profile, null);
            float totalUse = 0f;
            float totalInvestment = 0f;

            for (int i = 0; i < profile.weaponUsageData.Length; i++)
            {
                WeaponUsageRecord weapon = profile.weaponUsageData[i];
                if (weapon != null && weapon.owned)
                {
                    totalUse += Mathf.Max(0f, weapon.effectiveUseAmount);
                    totalInvestment += Mathf.Max(0f, weapon.activeResourceInvestment);
                }
            }

            List<CandidateEvaluation> evaluations = new List<CandidateEvaluation>();
            for (int i = 0; i < modules.candidates.Length; i++)
            {
                ModuleCandidateDefinition module = modules.candidates[i];
                WeaponUsageRecord weapon;
                bool hasRecord = lookup.WeaponsById.TryGetValue(module.dataSourceId, out weapon);
                bool eligible = hasRecord && weapon.owned && (weapon.effectiveUseAmount > 0f || weapon.activeResourceInvestment > 0f);
                float choiceShare = eligible ? PcgMath.SafeDivide(weapon.effectiveUseAmount, totalUse) : 0f;
                float investmentShare = eligible ? PcgMath.SafeDivide(weapon.activeResourceInvestment, totalInvestment) : 0f;
                float availableMinutes = eligible ? GetAvailableCombatMinutes(weapon) : 0f;
                float useRate = eligible ? Mathf.Clamp01(PcgMath.SafeDivide(weapon.effectiveUseAmount, availableMinutes)) : 0f;
                float confidence = eligible ? ComputeConfidence(availableMinutes, config.weaponConfidenceMinutes) : 0f;
                float score = eligible
                    ? confidence * (
                        config.weaponChoiceShareWeight * choiceShare +
                        config.weaponUseRateWeight * useRate +
                        config.weaponInvestmentShareWeight * investmentShare)
                    : 0f;

                evaluations.Add(new CandidateEvaluation
                {
                    moduleId = module.moduleId,
                    dataSourceId = module.dataSourceId,
                    atlasIndex = module.atlasIndex,
                    eligible = eligible,
                    normalizedInputA = choiceShare,
                    normalizedInputB = useRate,
                    normalizedInputC = investmentShare,
                    confidence = confidence,
                    score = score,
                    reason = eligible
                        ? "Score = confidence * (0.55 choice share + 0.30 use rate + 0.15 investment share)."
                        : "Weapon is not owned, missing, or has no effective use/investment."
                });
            }

            PcgMath.ApplyTemperatureProbabilities(evaluations, config.samplingTemperature, config.epsilon);
            return PcgMath.BuildSlotResult("Weapon", evaluations, random);
        }

        private static float GetAvailableCombatMinutes(WeaponUsageRecord weapon)
        {
            // Keeps older datasets usable: without authored opportunity data, use amount is the minimum known opportunity.
            return weapon.availableCombatMinutes > 0f
                ? weapon.availableCombatMinutes
                : Mathf.Max(0f, weapon.effectiveUseAmount);
        }

        private static float ComputeConfidence(float opportunityMinutes, float confidenceMinutes)
        {
            float scale = Mathf.Max(0.001f, confidenceMinutes);
            return Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(0f, opportunityMinutes) / scale));
        }
    }

    public static class BossSlotScorer
    {
        public static SlotGenerationResult Generate(PlayerProfile profile, ModuleDefinitionSet modules, BossDefinitionSet bossDefinitions, AlgorithmConfig config, System.Random random)
        {
            PcgLookup lookup = PcgLookupBuilder.FromProfile(profile, bossDefinitions);
            List<CandidateEvaluation> evaluations = new List<CandidateEvaluation>();

            for (int i = 0; i < modules.candidates.Length; i++)
            {
                ModuleCandidateDefinition module = modules.candidates[i];
                BossCombatRecord record;
                BossDefinition definition;
                bool defeated = lookup.BossRecordsById.TryGetValue(module.dataSourceId, out record);
                bool hasDefinition = lookup.BossDefinitionsById.TryGetValue(module.dataSourceId, out definition);
                bool eligible = defeated && hasDefinition;

                float bossValueNormalize = 0f;
                float performanceAdjustedPrestige = 0f;
                float winRate = 0f;
                float recencyWeight = 1f;
                float score = 0f;
                string reason;

                if (eligible)
                {
                    bossValueNormalize = Mathf.Clamp01(
                        config.bossDifficultyWeight * definition.challengeDifficulty +
                        config.bossRarityWeight * definition.rarity);

                    float prestige = ComputeBossPrestige(
                        record.playerLevelAtFirstDefeat,
                        record.bossLevel,
                        record.attemptCountAtFirstDefeat,
                        config);

                    int attempts = record.totalAttemptCount > 0
                        ? record.totalAttemptCount
                        : Mathf.Max(1, record.attemptCountAtFirstDefeat);
                    int wins = record.winCount > 0 ? record.winCount : 1;
                    wins = Mathf.Clamp(wins, 0, attempts);
                    winRate = ComputeLaplaceSmoothedWinRate(wins, attempts, config.bossLaplaceAlpha);
                    performanceAdjustedPrestige = prestige * Mathf.Lerp(1f, winRate, Mathf.Clamp01(config.bossWinRateWeight));

                    // Older static JSON files do not contain recencyWeight; their default is treated as 1.
                    recencyWeight = record.recencyWeight > 0f ? Mathf.Clamp01(record.recencyWeight) : 1f;
                    score = bossValueNormalize * performanceAdjustedPrestige * recencyWeight;
                    reason = "Score uses challenge/rarity, relative first-defeat difficulty, attempts, Laplace-smoothed win rate, and dynamic encounter recency.";
                }
                else if (!defeated)
                {
                    reason = "Boss has not been defeated by this player.";
                }
                else
                {
                    reason = "BossDefinition is missing for this boss.";
                }

                evaluations.Add(new CandidateEvaluation
                {
                    moduleId = module.moduleId,
                    dataSourceId = module.dataSourceId,
                    atlasIndex = module.atlasIndex,
                    eligible = eligible,
                    normalizedInputA = bossValueNormalize,
                    normalizedInputB = performanceAdjustedPrestige,
                    normalizedInputC = winRate,
                    confidence = eligible ? 1f : 0f,
                    score = score,
                    semanticContributionWeight = recencyWeight,
                    reason = reason
                });
            }

            PcgMath.ApplyTemperatureProbabilities(evaluations, config.samplingTemperature, config.epsilon);
            return PcgMath.BuildSlotResult("Boss", evaluations, random);
        }

        private static float ComputeBossPrestige(int playerLevelAtFirstDefeat, int bossLevel, int attemptCount, AlgorithmConfig config)
        {
            float p = PcgMath.Sigmoid(config.bossLevelLambda * (playerLevelAtFirstDefeat - bossLevel));
            float effectiveAttempts = 1f + config.bossAttemptGamma * Mathf.Max(0, attemptCount - 1);
            float successAfterAttempts = 1f - Mathf.Pow(1f - p, effectiveAttempts);
            float safeSuccess = Mathf.Clamp(successAfterAttempts, config.epsilon, 1f);
            return -Mathf.Log(safeSuccess);
        }

        private static float ComputeLaplaceSmoothedWinRate(int wins, int attempts, float alpha)
        {
            float safeAlpha = Mathf.Max(0.001f, alpha);
            return Mathf.Clamp01((wins + safeAlpha) / (attempts + 2f * safeAlpha));
        }
    }

    public static class RegionSlotScorer
    {
        public static SlotGenerationResult Generate(PlayerProfile profile, ModuleDefinitionSet modules, AlgorithmConfig config, System.Random random)
        {
            PcgLookup lookup = PcgLookupBuilder.FromProfile(profile, null);
            float totalRecentVisitMinutes = 0f;
            for (int i = 0; i < profile.regionExplorationData.Length; i++)
            {
                RegionExplorationRecord region = profile.regionExplorationData[i];
                if (region != null)
                {
                    totalRecentVisitMinutes += Mathf.Max(0f, region.recentVisitMinutes);
                }
            }

            List<CandidateEvaluation> evaluations = new List<CandidateEvaluation>();
            for (int i = 0; i < modules.candidates.Length; i++)
            {
                ModuleCandidateDefinition module = modules.candidates[i];
                RegionExplorationRecord region;
                bool hasRecord = lookup.RegionsById.TryGetValue(module.dataSourceId, out region);

                float exploration = 0f;
                float quest = 0f;
                float depth = 0f;
                float recentVisitShare = 0f;
                float visitRate = 0f;
                float confidence = 0f;
                bool eligible = false;
                if (hasRecord)
                {
                    exploration = PcgMath.SafeDivide(region.completedExplorationPoints, region.totalExplorationPoints);
                    quest = PcgMath.SafeDivide(region.completedRegionalQuests, region.totalRegionalQuests);
                    depth = config.regionExplorationWeight * exploration + config.regionQuestWeight * quest;
                    eligible = exploration >= config.regionEligibilityThreshold || quest >= config.regionEligibilityThreshold;

                    if (region.availablePlayMinutes > 0f || region.recentVisitMinutes > 0f)
                    {
                        recentVisitShare = PcgMath.SafeDivide(region.recentVisitMinutes, totalRecentVisitMinutes);
                        visitRate = Mathf.Clamp01(PcgMath.SafeDivide(region.recentVisitMinutes, region.availablePlayMinutes));
                        confidence = ComputeConfidence(region.availablePlayMinutes, config.regionConfidenceMinutes);
                    }
                    else
                    {
                        // Legacy Region records have no visit data. Preserve their depth-only behavior.
                        confidence = 1f;
                    }
                }

                float behaviorScore = config.regionRecentVisitWeight * recentVisitShare +
                                      config.regionVisitRateWeight * visitRate +
                                      config.regionDepthWeight * depth;
                float score = eligible ? confidence * behaviorScore : 0f;

                evaluations.Add(new CandidateEvaluation
                {
                    moduleId = module.moduleId,
                    dataSourceId = module.dataSourceId,
                    atlasIndex = module.atlasIndex,
                    eligible = eligible,
                    normalizedInputA = recentVisitShare,
                    normalizedInputB = visitRate,
                    normalizedInputC = depth,
                    confidence = confidence,
                    score = score,
                    reason = eligible
                        ? "Region passed its exploration or quest threshold; score combines recent visit share, opportunity-corrected visit rate, and depth."
                        : "Region missing or below exploration and quest thresholds."
                });
            }

            PcgMath.ApplyTemperatureProbabilities(evaluations, config.samplingTemperature, config.epsilon);
            return PcgMath.BuildSlotResult("Region", evaluations, random);
        }

        private static float ComputeConfidence(float opportunityMinutes, float confidenceMinutes)
        {
            float scale = Mathf.Max(0.001f, confidenceMinutes);
            return Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(0f, opportunityMinutes) / scale));
        }
    }
}
