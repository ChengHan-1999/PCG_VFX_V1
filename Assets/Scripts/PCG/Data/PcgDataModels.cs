using System;
using System.Collections.Generic;

namespace PCG.VFX
{
    [Serializable]
    public class AlgorithmConfig
    {
        public string schemaVersion;
        public float weaponUseWeight = 0.6f;
        public float weaponInvestmentWeight = 0.4f;
        public float weaponChoiceShareWeight = 0.55f;
        public float weaponUseRateWeight = 0.30f;
        public float weaponInvestmentShareWeight = 0.15f;
        public float weaponConfidenceMinutes = 30f;
        public float regionExplorationWeight = 0.6f;
        public float regionQuestWeight = 0.4f;
        public float regionEligibilityThreshold = 0.8f;
        public float regionRecentVisitWeight = 0.40f;
        public float regionVisitRateWeight = 0.25f;
        public float regionDepthWeight = 0.35f;
        public float regionConfidenceMinutes = 45f;
        public float regionRecencyHalfLifeDays = 20f;
        public float bossDifficultyWeight = 0.7f;
        public float bossRarityWeight = 0.3f;
        public float bossLevelLambda = 0.2f;
        public float bossAttemptGamma = 0.1f;
        public float bossWinRateWeight = 0.25f;
        public float bossLaplaceAlpha = 1f;
        public float samplingTemperature = 1.0f;
        public float stage2WeaponSemanticWeight = 0.25f;
        public float stage2BossSemanticWeight = 0.25f;
        public float stage2RegionSemanticWeight = 0.50f;
        public float themeTemporalSmoothingAlpha = 0.30f;
        public float themeConfidenceThreshold = 0.45f;
        public float themeMarginThreshold = 0.15f;
        // Presentation-only thresholds for explaining a Neutral fallback. They do not change
        // Stage 1 sampling or Stage 2 theme selection; they decide whether a slot alternates
        // between two competing texture candidates in the rendered magic circle.
        public float themeConflictMarginThreshold = 0.10f;
        public float slotConflictProbabilityRatio = 0.75f;
        public float epsilon = 0.000001f;
    }

    [Serializable]
    public class PlayerProfileDatabase
    {
        public string schemaVersion;
        public string datasetName;
        public string datasetPurpose;
        public bool containsRealPlayerData;
        public bool containsFinalScores;
        public int profileCount;
        public CandidateSets candidateSets;
        public PlayerProfile[] profiles;
    }

    [Serializable]
    public class CandidateSets
    {
        public string[] weapons;
        public string[] bosses;
        public string[] regions;
    }

    [Serializable]
    public class PlayerProfile
    {
        public string identityId;
        public string profileIntent;
        public int playerLevel;
        public WeaponUsageRecord[] weaponUsageData;
        public BossCombatRecord[] bossCombatData;
        public RegionExplorationRecord[] regionExplorationData;
    }

    [Serializable]
    public class WeaponUsageRecord
    {
        public string weaponId;
        public bool owned;
        public int weaponLevel;
        public float effectiveUseAmount;
        public float activeResourceInvestment;
        public float availableCombatMinutes;
    }

    [Serializable]
    public class BossCombatRecord
    {
        public string bossId;
        public int bossLevel;
        public int playerLevelAtFirstDefeat;
        public int attemptCountAtFirstDefeat;
        public int winCount;
        public int totalAttemptCount;
        // Runtime-only dynamic-trajectory memory. Static profile data keeps the default value of 1.
        public float recencyWeight = 1f;
        public float lastDefeatDay;
    }

    [Serializable]
    public class RegionExplorationRecord
    {
        public string regionId;
        public int regionLevel;
        public float completedExplorationPoints;
        public float totalExplorationPoints;
        public float completedRegionalQuests;
        public float totalRegionalQuests;
        public float recentVisitMinutes;
        public float availablePlayMinutes;
    }

    // Synthetic, authored feature data. This project does not collect real player data.
    [Serializable]
    public class PlayerBehaviorFeatureDatabase
    {
        public string schemaVersion;
        public string datasetPurpose;
        public bool containsRealPlayerData;
        public PlayerBehaviorFeatureProfile[] profiles;
    }

    [Serializable]
    public class PlayerBehaviorFeatureProfile
    {
        public string identityId;
        public WeaponBehaviorFeature[] weaponFeatures;
        public BossBehaviorFeature[] bossFeatures;
        public RegionBehaviorFeature[] regionFeatures;
    }

    [Serializable]
    public class WeaponBehaviorFeature
    {
        public string weaponId;
        public float availableCombatMinutes;
    }

    [Serializable]
    public class BossBehaviorFeature
    {
        public string bossId;
        public int winCount;
        public int totalAttemptCount;
    }

    [Serializable]
    public class RegionBehaviorFeature
    {
        public string regionId;
        public float recentVisitMinutes;
        public float availablePlayMinutes;
    }

    [Serializable]
    public class ModuleDefinitionSet
    {
        public string schemaVersion;
        public string slotType;
        public string[] themeAxes;
        public string textureAtlasPath;
        public AtlasLayout atlasLayout;
        public ModuleCandidateDefinition[] candidates;
    }

    [Serializable]
    public class AtlasLayout
    {
        public int columns;
        public int rows;
    }

    [Serializable]
    public class ModuleCandidateDefinition
    {
        public string moduleId;
        public string slotType;
        public string dataSourceId;
        public int atlasIndex;
        public string texturePath;
        public float[] semanticVector;
        public string eligibilityRule;
    }

    [Serializable]
    public class BossDefinitionSet
    {
        public string schemaVersion;
        public string definitionType;
        public BossDefinition[] bosses;
    }

    [Serializable]
    public class BossDefinition
    {
        public string bossId;
        public float challengeDifficulty;
        public float rarity;
    }

    [Serializable]
    public class ThemeDefinitionSet
    {
        public string schemaVersion;
        public string definitionType;
        public string[] themeAxes;
        public ThemeVfxBindings vfxBindings;
        public ThemeMaterialBindings materialBindings;
        public ThemeDefinition[] themes;
    }

    [Serializable]
    public class ThemeDefinition
    {
        public string themeId;
        public bool isFallback;
        // Named Event Context to trigger after this theme's VFX properties have been applied.
        public string vfxEventName;
        public float[] prototypeVector;
        public float[] hdrColorRgba;
        public ParticleAtlasDefinition particleAtlas;
        public float[] sizeRange;
        public float lifetime;
        public float[] speedRange;
        public float[] magicCircleColorRgba;
        public float[] slotColorRgba;
        public ParticleVfxParameters particleVfx;
    }

    [Serializable]
    public class ThemeVfxBindings
    {
        public string hdrColorProperty;
        public string sizeProperty;
        public string lifetimeProperty;
        public string yOffsetProperty;
        public string speedProperty;
        public string linearDragProperty;
        public string turbulenceIntensityProperty;
        public string turbulenceDragProperty;
        public string turbulenceFrequencyProperty;
        public string turbulenceOctavesProperty;
        public string turbulenceRoughnessProperty;
        public string turbulenceLacunarityProperty;
        public string particleTextureProperty;
    }

    [Serializable]
    public class ThemeMaterialBindings
    {
        public string baseCircleColorProperty;
        public string slotColorProperty;
    }

    [Serializable]
    public class ParticleVfxParameters
    {
        public float[] sparkColorRgba;
        public float sparkSize;
        public float sparkLife;
        public float yOffset;
        public float sparkSpeed;
        public float linearDrag;
        public bool useTurbulence;
        public float turbulenceIntensity;
        public float turbulenceDrag;
        public float turbulenceFrequency;
        public int turbulenceOctaves = 1;
        public float turbulenceRoughness;
        public float turbulenceLacunarity;
        public bool applyParticleTexture = true;
    }

    [Serializable]
    public class ParticleAtlasDefinition
    {
        public string atlasId;
        public string texturePath;
        public int columns;
        public int rows;
        public int[] texIndexRange;
    }

    [Serializable]
    public class PcgInputData
    {
        public PlayerProfileDatabase profiles;
        public ModuleDefinitionSet weaponModules;
        public ModuleDefinitionSet bossModules;
        public ModuleDefinitionSet regionModules;
        public BossDefinitionSet bossDefinitions;
        public ThemeDefinitionSet themes;
        public AlgorithmConfig config;
        public PlayerBehaviorFeatureDatabase behaviorFeatures;
    }

    [Serializable]
    public class CandidateEvaluation
    {
        public string moduleId;
        public string dataSourceId;
        public int atlasIndex;
        public bool eligible;
        public float normalizedInputA;
        public float normalizedInputB;
        public float normalizedInputC;
        public float confidence;
        public float score;
        public float probability;
        // Lets a dynamic event fade from the Stage 2 semantic contribution without removing
        // its record from the player's persistent achievement history.
        public float semanticContributionWeight = 1f;
        public string reason;
    }

    [Serializable]
    public class SlotGenerationResult
    {
        public string slotType;
        public CandidateEvaluation[] candidates;
        public string topModuleId;
        public string selectedModuleId;
        public string selectedDataSourceId;
        public int selectedAtlasIndex;
        public float selectedScore;
        public float selectedProbability;
        public bool fallbackUsed;
        public string fallbackReason;
    }

    [Serializable]
    public class ThemeGenerationResult
    {
        public string selectedThemeId;
        public string vfxEventName;
        public string selectedAtlasId;
        public string selectedTexturePath;
        public int texIndexMin;
        public int texIndexMax;
        public float confidence;
        public float margin;
        public float[] rawSemanticVector;
        public bool temporalSmoothingApplied;
        public float temporalSmoothingAlpha;
        public float[] combinedSemanticVector;
        public float[] hdrColorRgba;
        public float[] sizeRange;
        public float lifetime;
        public float[] speedRange;
        public float[] magicCircleColorRgba;
        public float[] slotColorRgba;
        public ParticleVfxParameters particleVfx;
        public bool fallbackUsed;
        public string fallbackReason;
    }

    [Serializable]
    public class GenerationResult
    {
        public string runId;
        public string profileId;
        public string profileIntent;
        public int seed;
        public SlotGenerationResult weapon;
        public SlotGenerationResult boss;
        public SlotGenerationResult region;
        public ThemeGenerationResult theme;
    }

    [Serializable]
    public class DynamicPlayerTrajectory
    {
        public string schemaVersion;
        public string trajectoryId;
        public string profileId;
        public string profileIntent;
        public float halfLifeDays = 15f;
        public float bossRecencyHalfLifeDays = 12f;
        // Optional trajectory-specific Stage 2 response settings. Negative values use AlgorithmConfig.
        // This keeps static-profile validation strict while allowing an authored longitudinal scenario
        // to show meaningful changes between sparse, discrete event nodes.
        public float themeTemporalSmoothingAlpha = -1f;
        public float themeConfidenceThreshold = -1f;
        public float themeMarginThreshold = -1f;
        public int initialPlayerLevel = 1;
        public DynamicInitialWeaponState[] initialWeapons;
        public DynamicBossDefeatEvent[] initialBossDefeatEvents;
        public DynamicInitialRegionState[] initialRegions;
        public DynamicEventNode[] eventNodes;
    }

    [Serializable]
    public class DynamicInitialWeaponState
    {
        public string weaponId;
        public bool owned;
        public int weaponLevel;
        public float initialEffectiveUseAmount;
        public float initialActiveResourceInvestment;
        public float initialAvailableCombatMinutes;
    }

    [Serializable]
    public class DynamicInitialRegionState
    {
        public string regionId;
        public int regionLevel;
        public float completedExplorationPoints;
        public float totalExplorationPoints;
        public float completedRegionalQuests;
        public float totalRegionalQuests;
        public float initialRecentVisitMinutes;
        public float initialAvailablePlayMinutes;
    }

    [Serializable]
    public class DynamicEventNode
    {
        public string nodeId;
        public float day;
        public int playerLevel;
        // Researcher-authored evaluation labels for synthetic trajectories.
        public string expectedTheme;
        public string expectedDirection;
        public bool isTransitionNode;
        public DynamicWeaponUseEvent[] weaponUseEvents;
        public DynamicWeaponInvestmentEvent[] weaponInvestmentEvents;
        public DynamicBossDefeatEvent[] bossDefeatEvents;
        public DynamicRegionProgressEvent[] regionProgressEvents;
    }

    [Serializable]
    public class DynamicWeaponUseEvent
    {
        public string weaponId;
        public float combatMinutes;
        public float availableCombatMinutes;
    }

    [Serializable]
    public class DynamicWeaponInvestmentEvent
    {
        public string weaponId;
        public float resourceAmount;
        public int weaponLevelAfterEvent;
    }

    [Serializable]
    public class DynamicBossDefeatEvent
    {
        public string bossId;
        public int bossLevel;
        public int playerLevelAtFirstDefeat;
        public int attemptCountAtFirstDefeat;
        public int winCount = 1;
        public int totalAttemptCount;
    }

    [Serializable]
    public class DynamicRegionProgressEvent
    {
        public string regionId;
        public int regionLevel;
        public float completedExplorationPoints;
        public float totalExplorationPoints;
        public float completedRegionalQuests;
        public float totalRegionalQuests;
        public float visitMinutes;
        public float availablePlayMinutes;
    }

    [Serializable]
    public class DynamicNodeGenerationResult
    {
        public string nodeId;
        public float day;
        public int playerLevel;
        public string expectedTheme;
        public string expectedDirection;
        public bool isTransitionNode;
        public GenerationResult generationResult;
    }

    [Serializable]
    public class DynamicDecayAuditRecord
    {
        public string trajectoryId;
        public string nodeId;
        public float day;
        public string signalType;
        public string entityId;
        public float previousDay;
        public float previousValue;
        public float halfLifeDays;
        public float decayFactor;
        public float expectedDecayedValue;
        public float observedDecayedValue;
    }

    [Serializable]
    public class DynamicTrajectoryGenerationResult
    {
        public string trajectoryId;
        public string profileId;
        public float halfLifeDays;
        public DynamicNodeGenerationResult[] nodes;
        public DynamicDecayAuditRecord[] decayAuditRecords;
    }

    public class PcgLookup
    {
        public readonly Dictionary<string, WeaponUsageRecord> WeaponsById = new Dictionary<string, WeaponUsageRecord>();
        public readonly Dictionary<string, BossCombatRecord> BossRecordsById = new Dictionary<string, BossCombatRecord>();
        public readonly Dictionary<string, RegionExplorationRecord> RegionsById = new Dictionary<string, RegionExplorationRecord>();
        public readonly Dictionary<string, BossDefinition> BossDefinitionsById = new Dictionary<string, BossDefinition>();
    }
}
