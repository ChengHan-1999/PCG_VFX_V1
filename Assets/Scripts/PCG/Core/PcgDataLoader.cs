using System;
using System.IO;
using UnityEngine;

namespace PCG.VFX
{
    public static class PcgDataLoader
    {
        public static PcgInputData LoadFromStreamingAssets()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "Data");

            PcgInputData data = new PcgInputData
            {
                profiles = LoadJson<PlayerProfileDatabase>(Path.Combine(root, "PlayerProfile.json")),
                weaponModules = LoadJson<ModuleDefinitionSet>(Path.Combine(root, "Modules", "WeaponModuleDefinitions.json")),
                bossModules = LoadJson<ModuleDefinitionSet>(Path.Combine(root, "Modules", "BossModuleDefinitions.json")),
                regionModules = LoadJson<ModuleDefinitionSet>(Path.Combine(root, "Modules", "RegionModuleDefinitions.json")),
                bossDefinitions = LoadJson<BossDefinitionSet>(Path.Combine(root, "Definitions", "BossDefinitions.json")),
                themes = LoadJson<ThemeDefinitionSet>(Path.Combine(root, "Themes", "ThemeDefinitions.json")),
                config = LoadJson<AlgorithmConfig>(Path.Combine(root, "Config", "AlgorithmConfig.json")),
                behaviorFeatures = LoadJson<PlayerBehaviorFeatureDatabase>(Path.Combine(root, "Behavior", "PlayerBehaviorFeatures.json"))
            };

            MergeBehaviorFeatures(data.profiles, data.behaviorFeatures);
            ValidateRequiredData(data);
            return data;
        }

        public static DynamicPlayerTrajectory LoadDynamicTrajectoryFromStreamingAssets(string relativePath)
        {
            string dataRoot = Path.Combine(Application.streamingAssetsPath, "Data");
            string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return LoadJson<DynamicPlayerTrajectory>(Path.Combine(dataRoot, normalizedRelativePath));
        }

        private static T LoadJson<T>(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required PCG data file is missing.", path);
            }

            string json = File.ReadAllText(path);
            T result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                throw new InvalidOperationException("Failed to parse JSON file: " + path);
            }

            return result;
        }

        private static void ValidateRequiredData(PcgInputData data)
        {
            if (data.profiles == null || data.profiles.profiles == null || data.profiles.profiles.Length == 0)
            {
                throw new InvalidOperationException("PlayerProfile data is empty.");
            }

            if (data.weaponModules == null || data.weaponModules.candidates == null || data.weaponModules.candidates.Length == 0)
            {
                throw new InvalidOperationException("Weapon module definitions are empty.");
            }

            if (data.bossModules == null || data.bossModules.candidates == null || data.bossModules.candidates.Length == 0)
            {
                throw new InvalidOperationException("Boss module definitions are empty.");
            }

            if (data.regionModules == null || data.regionModules.candidates == null || data.regionModules.candidates.Length == 0)
            {
                throw new InvalidOperationException("Region module definitions are empty.");
            }

            if (data.themes == null || data.themes.themes == null || data.themes.themes.Length == 0)
            {
                throw new InvalidOperationException("Theme definitions are empty.");
            }

            if (data.behaviorFeatures == null || data.behaviorFeatures.profiles == null || data.behaviorFeatures.profiles.Length == 0)
            {
                throw new InvalidOperationException("Synthetic player behavior features are empty.");
            }
        }

        private static void MergeBehaviorFeatures(PlayerProfileDatabase profiles, PlayerBehaviorFeatureDatabase behaviorFeatures)
        {
            if (profiles == null || profiles.profiles == null || behaviorFeatures == null || behaviorFeatures.profiles == null)
            {
                return;
            }

            for (int i = 0; i < profiles.profiles.Length; i++)
            {
                PlayerProfile profile = profiles.profiles[i];
                if (profile == null)
                {
                    continue;
                }

                PlayerBehaviorFeatureProfile features = FindFeatureProfile(behaviorFeatures.profiles, profile.identityId);
                if (features == null)
                {
                    continue;
                }

                MergeWeaponFeatures(profile.weaponUsageData, features.weaponFeatures);
                MergeBossFeatures(profile.bossCombatData, features.bossFeatures);
                MergeRegionFeatures(profile.regionExplorationData, features.regionFeatures);
            }
        }

        private static PlayerBehaviorFeatureProfile FindFeatureProfile(PlayerBehaviorFeatureProfile[] profiles, string identityId)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].identityId == identityId)
                {
                    return profiles[i];
                }
            }

            return null;
        }

        private static void MergeWeaponFeatures(WeaponUsageRecord[] records, WeaponBehaviorFeature[] features)
        {
            if (records == null || features == null)
            {
                return;
            }

            for (int i = 0; i < records.Length; i++)
            {
                for (int j = 0; j < features.Length; j++)
                {
                    if (records[i] != null && features[j] != null && records[i].weaponId == features[j].weaponId)
                    {
                        records[i].availableCombatMinutes = features[j].availableCombatMinutes;
                        break;
                    }
                }
            }
        }

        private static void MergeBossFeatures(BossCombatRecord[] records, BossBehaviorFeature[] features)
        {
            if (records == null || features == null)
            {
                return;
            }

            for (int i = 0; i < records.Length; i++)
            {
                for (int j = 0; j < features.Length; j++)
                {
                    if (records[i] != null && features[j] != null && records[i].bossId == features[j].bossId)
                    {
                        records[i].winCount = features[j].winCount;
                        records[i].totalAttemptCount = features[j].totalAttemptCount;
                        break;
                    }
                }
            }
        }

        private static void MergeRegionFeatures(RegionExplorationRecord[] records, RegionBehaviorFeature[] features)
        {
            if (records == null || features == null)
            {
                return;
            }

            for (int i = 0; i < records.Length; i++)
            {
                for (int j = 0; j < features.Length; j++)
                {
                    if (records[i] != null && features[j] != null && records[i].regionId == features[j].regionId)
                    {
                        records[i].recentVisitMinutes = features[j].recentVisitMinutes;
                        records[i].availablePlayMinutes = features[j].availablePlayMinutes;
                        break;
                    }
                }
            }
        }
    }
}
