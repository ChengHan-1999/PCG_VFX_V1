using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCG.VFX
{
    public class DynamicTrajectoryProcessor
    {
        private class WeaponRuntimeState
        {
            public string weaponId;
            public bool owned;
            public int weaponLevel;
            public float effectiveUseAmount;
            public float activeResourceInvestment;
            public float availableCombatMinutes;
            public float lastUpdatedDay;
        }

        private class RegionRuntimeState
        {
            public string regionId;
            public int regionLevel;
            public float completedExplorationPoints;
            public float totalExplorationPoints;
            public float completedRegionalQuests;
            public float totalRegionalQuests;
            public float recentVisitMinutes;
            public float availablePlayMinutes;
            public float lastUpdatedDay;
        }

        public DynamicTrajectoryGenerationResult Generate(DynamicPlayerTrajectory trajectory, PcgInputData data, int seedBase)
        {
            if (trajectory == null)
            {
                throw new ArgumentNullException("trajectory");
            }

            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            Dictionary<string, WeaponRuntimeState> weapons = BuildInitialWeaponStates(trajectory, data);
            Dictionary<string, BossCombatRecord> bosses = new Dictionary<string, BossCombatRecord>();
            ApplyBossDefeatEvents(bosses, trajectory.initialBossDefeatEvents, 0f);
            Dictionary<string, RegionRuntimeState> regions = BuildInitialRegionStates(trajectory, data);
            TextureSlotGenerator generator = new TextureSlotGenerator();
            AlgorithmConfig config = CreateTrajectoryConfig(data.config, trajectory);
            List<DynamicNodeGenerationResult> nodeResults = new List<DynamicNodeGenerationResult>();
            List<DynamicDecayAuditRecord> decayAuditRecords = new List<DynamicDecayAuditRecord>();
            int currentPlayerLevel = Mathf.Max(1, trajectory.initialPlayerLevel);
            float[] previousSmoothedThemeVector = null;

            DynamicEventNode[] nodes = trajectory.eventNodes ?? new DynamicEventNode[0];
            for (int i = 0; i < nodes.Length; i++)
            {
                DynamicEventNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                float day = Mathf.Max(0f, node.day);
                DecayWeaponsToDay(weapons, day, Mathf.Max(0.001f, trajectory.halfLifeDays), trajectory.trajectoryId, node.nodeId, decayAuditRecords);
                DecayBossesToDay(bosses, day, Mathf.Max(0.001f, trajectory.bossRecencyHalfLifeDays), trajectory.trajectoryId, node.nodeId, decayAuditRecords);
                DecayRegionsToDay(regions, day, Mathf.Max(0.001f, config.regionRecencyHalfLifeDays), trajectory.trajectoryId, node.nodeId, decayAuditRecords);
                currentPlayerLevel = node.playerLevel > 0 ? node.playerLevel : currentPlayerLevel;

                ApplyWeaponUseEvents(weapons, node.weaponUseEvents, day);
                ApplyWeaponInvestmentEvents(weapons, node.weaponInvestmentEvents);
                ApplyBossDefeatEvents(bosses, node.bossDefeatEvents, day);
                ApplyRegionProgressEvents(regions, node.regionProgressEvents, day);

                PlayerProfile snapshot = BuildSnapshotProfile(trajectory, node, currentPlayerLevel, weapons, bosses, regions, data);
                GenerationResult generationResult = generator.Generate(snapshot, data, seedBase + i);
                ApplyTemporalThemeSmoothing(generationResult, previousSmoothedThemeVector, data, config);
                if (generationResult.theme != null && generationResult.theme.combinedSemanticVector != null)
                {
                    previousSmoothedThemeVector = CopyVector(generationResult.theme.combinedSemanticVector);
                }

                nodeResults.Add(new DynamicNodeGenerationResult
                {
                    nodeId = node.nodeId,
                    day = day,
                    playerLevel = currentPlayerLevel,
                    expectedTheme = node.expectedTheme,
                    expectedDirection = node.expectedDirection,
                    isTransitionNode = node.isTransitionNode,
                    generationResult = generationResult
                });
            }

            return new DynamicTrajectoryGenerationResult
            {
                trajectoryId = trajectory.trajectoryId,
                profileId = trajectory.profileId,
                halfLifeDays = trajectory.halfLifeDays,
                nodes = nodeResults.ToArray(),
                decayAuditRecords = decayAuditRecords.ToArray()
            };
        }

        private static void ApplyTemporalThemeSmoothing(
            GenerationResult generationResult,
            float[] previousSmoothedVector,
            PcgInputData data,
            AlgorithmConfig config)
        {
            if (generationResult == null || generationResult.theme == null || generationResult.theme.combinedSemanticVector == null)
            {
                return;
            }

            float[] rawVector = generationResult.theme.combinedSemanticVector;
            float alpha = Mathf.Clamp01(config.themeTemporalSmoothingAlpha);
            bool hasPreviousVector = previousSmoothedVector != null && previousSmoothedVector.Length > 0;
            float[] smoothedVector = hasPreviousVector
                ? LerpVectors(previousSmoothedVector, rawVector, alpha)
                : CopyVector(rawVector);

            generationResult.theme = ThemeInference.InferFromSemanticVector(
                smoothedVector,
                data,
                config,
                rawVector,
                hasPreviousVector,
                alpha);
        }

        private static AlgorithmConfig CreateTrajectoryConfig(AlgorithmConfig source, DynamicPlayerTrajectory trajectory)
        {
            // Clone rather than mutate the shared data config: static profile runs must retain their
            // original validation thresholds after a dynamic trajectory has played.
            AlgorithmConfig config = source != null
                ? JsonUtility.FromJson<AlgorithmConfig>(JsonUtility.ToJson(source))
                : new AlgorithmConfig();

            if (trajectory.themeTemporalSmoothingAlpha >= 0f)
            {
                config.themeTemporalSmoothingAlpha = Mathf.Clamp01(trajectory.themeTemporalSmoothingAlpha);
            }

            if (trajectory.themeConfidenceThreshold >= 0f)
            {
                config.themeConfidenceThreshold = Mathf.Clamp01(trajectory.themeConfidenceThreshold);
            }

            if (trajectory.themeMarginThreshold >= 0f)
            {
                config.themeMarginThreshold = Mathf.Clamp01(trajectory.themeMarginThreshold);
            }

            return config;
        }

        private static float[] LerpVectors(float[] previous, float[] current, float alpha)
        {
            int length = Mathf.Max(previous != null ? previous.Length : 0, current != null ? current.Length : 0);
            float[] result = new float[length];
            for (int i = 0; i < length; i++)
            {
                float previousValue = previous != null && i < previous.Length ? previous[i] : 0f;
                float currentValue = current != null && i < current.Length ? current[i] : 0f;
                result[i] = Mathf.Lerp(previousValue, currentValue, alpha);
            }

            return result;
        }

        private static float[] CopyVector(float[] source)
        {
            if (source == null)
            {
                return new float[0];
            }

            float[] copy = new float[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static Dictionary<string, WeaponRuntimeState> BuildInitialWeaponStates(DynamicPlayerTrajectory trajectory, PcgInputData data)
        {
            Dictionary<string, WeaponRuntimeState> states = new Dictionary<string, WeaponRuntimeState>();

            if (data.weaponModules != null && data.weaponModules.candidates != null)
            {
                for (int i = 0; i < data.weaponModules.candidates.Length; i++)
                {
                    ModuleCandidateDefinition module = data.weaponModules.candidates[i];
                    if (module != null && !string.IsNullOrEmpty(module.dataSourceId) && !states.ContainsKey(module.dataSourceId))
                    {
                        states[module.dataSourceId] = new WeaponRuntimeState
                        {
                            weaponId = module.dataSourceId,
                            owned = false,
                            weaponLevel = 0,
                            effectiveUseAmount = 0f,
                            activeResourceInvestment = 0f,
                            availableCombatMinutes = 0f,
                            lastUpdatedDay = 0f
                        };
                    }
                }
            }

            DynamicInitialWeaponState[] initialWeapons = trajectory.initialWeapons ?? new DynamicInitialWeaponState[0];
            for (int i = 0; i < initialWeapons.Length; i++)
            {
                DynamicInitialWeaponState initial = initialWeapons[i];
                if (initial == null || string.IsNullOrEmpty(initial.weaponId))
                {
                    continue;
                }

                WeaponRuntimeState state = GetOrCreateWeapon(states, initial.weaponId);
                state.owned = initial.owned;
                state.weaponLevel = initial.weaponLevel;
                state.effectiveUseAmount = Mathf.Max(0f, initial.initialEffectiveUseAmount);
                state.activeResourceInvestment = Mathf.Max(0f, initial.initialActiveResourceInvestment);
                state.availableCombatMinutes = Mathf.Max(0f, initial.initialAvailableCombatMinutes);
            }

            return states;
        }

        private static Dictionary<string, RegionRuntimeState> BuildInitialRegionStates(DynamicPlayerTrajectory trajectory, PcgInputData data)
        {
            Dictionary<string, RegionRuntimeState> states = new Dictionary<string, RegionRuntimeState>();

            if (data.regionModules != null && data.regionModules.candidates != null)
            {
                for (int i = 0; i < data.regionModules.candidates.Length; i++)
                {
                    ModuleCandidateDefinition module = data.regionModules.candidates[i];
                    if (module != null && !string.IsNullOrEmpty(module.dataSourceId) && !states.ContainsKey(module.dataSourceId))
                    {
                        states[module.dataSourceId] = new RegionRuntimeState
                        {
                            regionId = module.dataSourceId,
                            regionLevel = 0
                        };
                    }
                }
            }

            DynamicInitialRegionState[] initialRegions = trajectory.initialRegions ?? new DynamicInitialRegionState[0];
            for (int i = 0; i < initialRegions.Length; i++)
            {
                DynamicInitialRegionState initial = initialRegions[i];
                if (initial == null || string.IsNullOrEmpty(initial.regionId))
                {
                    continue;
                }

                RegionRuntimeState state = GetOrCreateRegion(states, initial.regionId);
                state.regionLevel = initial.regionLevel;
                state.completedExplorationPoints = Mathf.Max(0f, initial.completedExplorationPoints);
                state.totalExplorationPoints = Mathf.Max(0f, initial.totalExplorationPoints);
                state.completedRegionalQuests = Mathf.Max(0f, initial.completedRegionalQuests);
                state.totalRegionalQuests = Mathf.Max(0f, initial.totalRegionalQuests);
                state.recentVisitMinutes = Mathf.Max(0f, initial.initialRecentVisitMinutes);
                state.availablePlayMinutes = Mathf.Max(0f, initial.initialAvailablePlayMinutes);
            }

            return states;
        }

        private static void DecayWeaponsToDay(
            Dictionary<string, WeaponRuntimeState> weapons,
            float targetDay,
            float halfLifeDays,
            string trajectoryId,
            string nodeId,
            List<DynamicDecayAuditRecord> auditRecords)
        {
            foreach (KeyValuePair<string, WeaponRuntimeState> pair in weapons)
            {
                WeaponRuntimeState state = pair.Value;
                float deltaDays = Mathf.Max(0f, targetDay - state.lastUpdatedDay);
                if (deltaDays > 0f && state.effectiveUseAmount > 0f)
                {
                    float previousValue = state.effectiveUseAmount;
                    float factor = Mathf.Pow(2f, -deltaDays / halfLifeDays);
                    float expectedValue = previousValue * factor;
                    state.effectiveUseAmount = expectedValue;
                    AddDecayAudit(
                        auditRecords, trajectoryId, nodeId, targetDay,
                        "WeaponEffectiveUse", state.weaponId, state.lastUpdatedDay,
                        previousValue, halfLifeDays, factor, expectedValue, state.effectiveUseAmount);
                    state.lastUpdatedDay = targetDay;
                }
                else if (deltaDays > 0f)
                {
                    state.lastUpdatedDay = targetDay;
                }
            }
        }

        private static void DecayRegionsToDay(
            Dictionary<string, RegionRuntimeState> regions,
            float targetDay,
            float halfLifeDays,
            string trajectoryId,
            string nodeId,
            List<DynamicDecayAuditRecord> auditRecords)
        {
            foreach (KeyValuePair<string, RegionRuntimeState> pair in regions)
            {
                RegionRuntimeState state = pair.Value;
                float deltaDays = Mathf.Max(0f, targetDay - state.lastUpdatedDay);
                if (deltaDays > 0f && state.recentVisitMinutes > 0f)
                {
                    float previousValue = state.recentVisitMinutes;
                    float factor = Mathf.Pow(2f, -deltaDays / halfLifeDays);
                    float expectedValue = previousValue * factor;
                    state.recentVisitMinutes = expectedValue;
                    AddDecayAudit(
                        auditRecords, trajectoryId, nodeId, targetDay,
                        "RegionRecentVisit", state.regionId, state.lastUpdatedDay,
                        previousValue, halfLifeDays, factor, expectedValue, state.recentVisitMinutes);
                    state.lastUpdatedDay = targetDay;
                }
                else if (deltaDays > 0f)
                {
                    state.lastUpdatedDay = targetDay;
                }
            }
        }

        private static void DecayBossesToDay(
            Dictionary<string, BossCombatRecord> bosses,
            float targetDay,
            float halfLifeDays,
            string trajectoryId,
            string nodeId,
            List<DynamicDecayAuditRecord> auditRecords)
        {
            foreach (KeyValuePair<string, BossCombatRecord> pair in bosses)
            {
                BossCombatRecord record = pair.Value;
                if (record == null)
                {
                    continue;
                }

                float deltaDays = Mathf.Max(0f, targetDay - record.lastDefeatDay);
                if (deltaDays > 0f)
                {
                    float currentWeight = record.recencyWeight > 0f ? record.recencyWeight : 1f;
                    float factor = Mathf.Pow(2f, -deltaDays / halfLifeDays);
                    float expectedValue = currentWeight * factor;
                    record.recencyWeight = expectedValue;
                    AddDecayAudit(
                        auditRecords, trajectoryId, nodeId, targetDay,
                        "BossRecencyWeight", record.bossId, record.lastDefeatDay,
                        currentWeight, halfLifeDays, factor, expectedValue, record.recencyWeight);
                    record.lastDefeatDay = targetDay;
                }
            }
        }

        private static void AddDecayAudit(
            List<DynamicDecayAuditRecord> auditRecords,
            string trajectoryId,
            string nodeId,
            float day,
            string signalType,
            string entityId,
            float previousDay,
            float previousValue,
            float halfLifeDays,
            float decayFactor,
            float expectedValue,
            float observedValue)
        {
            auditRecords.Add(new DynamicDecayAuditRecord
            {
                trajectoryId = trajectoryId,
                nodeId = nodeId,
                day = day,
                signalType = signalType,
                entityId = entityId,
                previousDay = previousDay,
                previousValue = previousValue,
                halfLifeDays = halfLifeDays,
                decayFactor = decayFactor,
                expectedDecayedValue = expectedValue,
                observedDecayedValue = observedValue
            });
        }

        private static void ApplyWeaponUseEvents(Dictionary<string, WeaponRuntimeState> weapons, DynamicWeaponUseEvent[] events, float currentDay)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                DynamicWeaponUseEvent useEvent = events[i];
                if (useEvent == null || string.IsNullOrEmpty(useEvent.weaponId))
                {
                    continue;
                }

                WeaponRuntimeState state = GetOrCreateWeapon(weapons, useEvent.weaponId);
                state.owned = true;
                state.lastUpdatedDay = currentDay;
                state.effectiveUseAmount += Mathf.Max(0f, useEvent.combatMinutes);
                state.availableCombatMinutes += Mathf.Max(
                    Mathf.Max(0f, useEvent.combatMinutes),
                    Mathf.Max(0f, useEvent.availableCombatMinutes));
            }
        }

        private static void ApplyWeaponInvestmentEvents(Dictionary<string, WeaponRuntimeState> weapons, DynamicWeaponInvestmentEvent[] events)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                DynamicWeaponInvestmentEvent investmentEvent = events[i];
                if (investmentEvent == null || string.IsNullOrEmpty(investmentEvent.weaponId))
                {
                    continue;
                }

                WeaponRuntimeState state = GetOrCreateWeapon(weapons, investmentEvent.weaponId);
                state.owned = true;
                state.activeResourceInvestment += Mathf.Max(0f, investmentEvent.resourceAmount);
                if (investmentEvent.weaponLevelAfterEvent > 0)
                {
                    state.weaponLevel = investmentEvent.weaponLevelAfterEvent;
                }
            }
        }

        private static void ApplyBossDefeatEvents(Dictionary<string, BossCombatRecord> bosses, DynamicBossDefeatEvent[] events, float currentDay)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                DynamicBossDefeatEvent bossEvent = events[i];
                if (bossEvent == null || string.IsNullOrEmpty(bossEvent.bossId))
                {
                    continue;
                }

                BossCombatRecord record;
                if (!bosses.TryGetValue(bossEvent.bossId, out record))
                {
                    record = new BossCombatRecord
                    {
                        bossId = bossEvent.bossId,
                        bossLevel = bossEvent.bossLevel,
                        playerLevelAtFirstDefeat = bossEvent.playerLevelAtFirstDefeat,
                        attemptCountAtFirstDefeat = bossEvent.attemptCountAtFirstDefeat,
                        recencyWeight = 1f,
                        lastDefeatDay = currentDay
                    };
                }

                int attempts = bossEvent.totalAttemptCount > 0
                    ? bossEvent.totalAttemptCount
                    : Mathf.Max(1, bossEvent.attemptCountAtFirstDefeat);
                int wins = bossEvent.winCount > 0 ? bossEvent.winCount : 1;
                record.bossLevel = bossEvent.bossLevel;
                record.totalAttemptCount += attempts;
                record.winCount += Mathf.Clamp(wins, 0, attempts);
                record.recencyWeight = 1f;
                record.lastDefeatDay = currentDay;
                bosses[bossEvent.bossId] = record;
            }
        }

        private static void ApplyRegionProgressEvents(Dictionary<string, RegionRuntimeState> regions, DynamicRegionProgressEvent[] events, float currentDay)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                DynamicRegionProgressEvent progressEvent = events[i];
                if (progressEvent == null || string.IsNullOrEmpty(progressEvent.regionId))
                {
                    continue;
                }

                RegionRuntimeState state = GetOrCreateRegion(regions, progressEvent.regionId);
                state.regionLevel = progressEvent.regionLevel;
                state.completedExplorationPoints = Mathf.Max(state.completedExplorationPoints, progressEvent.completedExplorationPoints);
                state.totalExplorationPoints = Mathf.Max(state.totalExplorationPoints, progressEvent.totalExplorationPoints);
                state.completedRegionalQuests = Mathf.Max(state.completedRegionalQuests, progressEvent.completedRegionalQuests);
                state.totalRegionalQuests = Mathf.Max(state.totalRegionalQuests, progressEvent.totalRegionalQuests);
                state.recentVisitMinutes += Mathf.Max(0f, progressEvent.visitMinutes);
                state.availablePlayMinutes += Mathf.Max(
                    Mathf.Max(0f, progressEvent.visitMinutes),
                    Mathf.Max(0f, progressEvent.availablePlayMinutes));
                state.lastUpdatedDay = currentDay;
            }
        }

        private static PlayerProfile BuildSnapshotProfile(
            DynamicPlayerTrajectory trajectory,
            DynamicEventNode node,
            int playerLevel,
            Dictionary<string, WeaponRuntimeState> weapons,
            Dictionary<string, BossCombatRecord> bosses,
            Dictionary<string, RegionRuntimeState> regions,
            PcgInputData data)
        {
            return new PlayerProfile
            {
                identityId = trajectory.profileId + "_" + node.nodeId,
                profileIntent = trajectory.profileIntent,
                playerLevel = playerLevel,
                weaponUsageData = BuildWeaponRecords(weapons, data),
                bossCombatData = BuildBossRecords(bosses),
                regionExplorationData = BuildRegionRecords(regions, data)
            };
        }

        private static WeaponUsageRecord[] BuildWeaponRecords(Dictionary<string, WeaponRuntimeState> weapons, PcgInputData data)
        {
            List<WeaponUsageRecord> records = new List<WeaponUsageRecord>();

            if (data.weaponModules != null && data.weaponModules.candidates != null)
            {
                for (int i = 0; i < data.weaponModules.candidates.Length; i++)
                {
                    ModuleCandidateDefinition module = data.weaponModules.candidates[i];
                    if (module != null && !string.IsNullOrEmpty(module.dataSourceId))
                    {
                        AddWeaponRecord(records, GetOrCreateWeapon(weapons, module.dataSourceId));
                    }
                }

                return records.ToArray();
            }

            foreach (KeyValuePair<string, WeaponRuntimeState> pair in weapons)
            {
                AddWeaponRecord(records, pair.Value);
            }

            return records.ToArray();
        }

        private static BossCombatRecord[] BuildBossRecords(Dictionary<string, BossCombatRecord> bosses)
        {
            List<BossCombatRecord> records = new List<BossCombatRecord>();
            foreach (KeyValuePair<string, BossCombatRecord> pair in bosses)
            {
                records.Add(pair.Value);
            }

            return records.ToArray();
        }

        private static RegionExplorationRecord[] BuildRegionRecords(Dictionary<string, RegionRuntimeState> regions, PcgInputData data)
        {
            List<RegionExplorationRecord> records = new List<RegionExplorationRecord>();

            if (data.regionModules != null && data.regionModules.candidates != null)
            {
                for (int i = 0; i < data.regionModules.candidates.Length; i++)
                {
                    ModuleCandidateDefinition module = data.regionModules.candidates[i];
                    if (module != null && !string.IsNullOrEmpty(module.dataSourceId))
                    {
                        AddRegionRecord(records, GetOrCreateRegion(regions, module.dataSourceId));
                    }
                }

                return records.ToArray();
            }

            foreach (KeyValuePair<string, RegionRuntimeState> pair in regions)
            {
                AddRegionRecord(records, pair.Value);
            }

            return records.ToArray();
        }

        private static void AddWeaponRecord(List<WeaponUsageRecord> records, WeaponRuntimeState state)
        {
            records.Add(new WeaponUsageRecord
            {
                weaponId = state.weaponId,
                owned = state.owned,
                weaponLevel = state.weaponLevel,
                effectiveUseAmount = state.effectiveUseAmount,
                activeResourceInvestment = state.activeResourceInvestment,
                availableCombatMinutes = state.availableCombatMinutes
            });
        }

        private static void AddRegionRecord(List<RegionExplorationRecord> records, RegionRuntimeState state)
        {
            records.Add(new RegionExplorationRecord
            {
                regionId = state.regionId,
                regionLevel = state.regionLevel,
                completedExplorationPoints = state.completedExplorationPoints,
                totalExplorationPoints = state.totalExplorationPoints,
                completedRegionalQuests = state.completedRegionalQuests,
                totalRegionalQuests = state.totalRegionalQuests,
                recentVisitMinutes = state.recentVisitMinutes,
                availablePlayMinutes = state.availablePlayMinutes
            });
        }

        private static WeaponRuntimeState GetOrCreateWeapon(Dictionary<string, WeaponRuntimeState> weapons, string weaponId)
        {
            WeaponRuntimeState state;
            if (!weapons.TryGetValue(weaponId, out state))
            {
                state = new WeaponRuntimeState
                {
                    weaponId = weaponId,
                    owned = false,
                    weaponLevel = 0,
                    effectiveUseAmount = 0f,
                    activeResourceInvestment = 0f,
                    availableCombatMinutes = 0f,
                    lastUpdatedDay = 0f
                };
                weapons[weaponId] = state;
            }

            return state;
        }

        private static RegionRuntimeState GetOrCreateRegion(Dictionary<string, RegionRuntimeState> regions, string regionId)
        {
            RegionRuntimeState state;
            if (!regions.TryGetValue(regionId, out state))
            {
                state = new RegionRuntimeState
                {
                    regionId = regionId,
                    regionLevel = 0,
                    recentVisitMinutes = 0f,
                    availablePlayMinutes = 0f,
                    lastUpdatedDay = 0f
                };
                regions[regionId] = state;
            }

            return state;
        }
    }
}
