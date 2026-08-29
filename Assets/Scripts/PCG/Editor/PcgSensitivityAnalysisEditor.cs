#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PCG.VFX
{
    /// <summary>
    /// Runs a reproducible one-at-a-time (OAT) parameter sensitivity analysis over the final
    /// synthetic player dataset. Every scenario starts from a deep copy of the same input data,
    /// keeps the static and dynamic seeds at 99, and perturbs exactly one parameter by -10% or
    /// +10%. Weight groups are re-normalised after perturbation so that their total remains one.
    /// </summary>
    public static class PcgSensitivityAnalysisEditor
    {
        private const string DynamicTrajectoryPath = "Dynamic/DynamicPlayerTrajectory_Player01.json";
        private const int StaticSeed = 99;
        private const int DynamicSeedBase = 99;
        private const float MinusTenPercent = 0.90f;
        private const float PlusTenPercent = 1.10f;
        private const float AuditTolerance = 0.00001f;

        private class SensitivityScenario
        {
            public string scenarioId;
            public string parameterGroup;
            public string parameterName;
            public string direction;
            public float multiplier;
            public Action<AlgorithmConfig, DynamicPlayerTrajectory> apply;
            public Func<AlgorithmConfig, DynamicPlayerTrajectory, string> describeGroupValues;
        }

        private class ScenarioOutcome
        {
            public SensitivityScenario scenario;
            public List<GenerationResult> staticResults = new List<GenerationResult>();
            public DynamicTrajectoryGenerationResult dynamicResult;
            public string baselineValues;
            public string testedValues;
        }

        [MenuItem("PCG VFX/Evaluation/Run OAT Sensitivity Analysis")]
        public static void RunOneAtATimeSensitivityAnalysis()
        {
            try
            {
                PcgInputData baseData = PcgDataLoader.LoadFromStreamingAssets();
                DynamicPlayerTrajectory baseTrajectory =
                    PcgDataLoader.LoadDynamicTrajectoryFromStreamingAssets(DynamicTrajectoryPath);
                List<SensitivityScenario> scenarios = BuildScenarios(baseData.config, baseTrajectory);

                string outputDirectory = GetOutputDirectory();
                Directory.CreateDirectory(outputDirectory);
                string generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                string experimentId = "PCG_VFX_OAT_Sensitivity_" +
                                      DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

                ScenarioOutcome baseline = ExecuteScenario(
                    BuildBaselineScenario(), baseData, baseTrajectory);

                List<ScenarioOutcome> outcomes = new List<ScenarioOutcome> { baseline };
                for (int i = 0; i < scenarios.Count; i++)
                {
                    outcomes.Add(ExecuteScenario(scenarios[i], baseData, baseTrajectory));
                }

                WriteSummaryCsv(outcomes, baseline, experimentId, generatedAtUtc, outputDirectory);
                WriteStaticDetailsCsv(outcomes, baseline, experimentId, generatedAtUtc, outputDirectory);
                WriteDynamicDetailsCsv(outcomes, baseline, experimentId, generatedAtUtc, outputDirectory);
                WriteDecayAuditCsv(outcomes, experimentId, generatedAtUtc, outputDirectory);
                WriteReadme(outputDirectory);

                AssetDatabase.Refresh();
                Debug.Log(
                    "[PCG VFX] One-at-a-time sensitivity analysis completed. " +
                    "Scenarios: " + outcomes.Count + ". Results written to: " + outputDirectory +
                    "\n- SensitivitySummary.csv" +
                    "\n- SensitivityStaticDetails.csv" +
                    "\n- SensitivityDynamicDetails.csv" +
                    "\n- SensitivityDecayAudit.csv" +
                    "\n- README.txt");
            }
            catch (Exception exception)
            {
                Debug.LogError("[PCG VFX] Sensitivity analysis failed: " + exception.Message);
                throw;
            }
        }

        [MenuItem("PCG VFX/Evaluation/Open Sensitivity Analysis Folder")]
        public static void OpenSensitivityAnalysisFolder()
        {
            string outputDirectory = GetOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            EditorUtility.RevealInFinder(outputDirectory);
        }

        private static SensitivityScenario BuildBaselineScenario()
        {
            return new SensitivityScenario
            {
                scenarioId = "Baseline",
                parameterGroup = "Baseline",
                parameterName = "No parameter change",
                direction = "Baseline",
                multiplier = 1f,
                apply = delegate { },
                describeGroupValues = delegate { return "Unmodified final configuration"; }
            };
        }

        private static List<SensitivityScenario> BuildScenarios(
            AlgorithmConfig baseConfig,
            DynamicPlayerTrajectory baseTrajectory)
        {
            List<SensitivityScenario> scenarios = new List<SensitivityScenario>();

            // Stage 1: Weapon preference score weights.
            AddNormalisedTripletScenarios(
                scenarios,
                "Weapon slot score weights",
                new[] { "WeaponChoiceShareWeight", "WeaponUseRateWeight", "WeaponInvestmentShareWeight" },
                delegate(AlgorithmConfig c)
                {
                    return new[]
                    {
                        c.weaponChoiceShareWeight,
                        c.weaponUseRateWeight,
                        c.weaponInvestmentShareWeight
                    };
                },
                delegate(AlgorithmConfig c, float[] values)
                {
                    c.weaponChoiceShareWeight = values[0];
                    c.weaponUseRateWeight = values[1];
                    c.weaponInvestmentShareWeight = values[2];
                });

            // Stage 1: Boss value and performance adjustment weights.
            AddNormalisedPairScenarios(
                scenarios,
                "Boss base-value weights",
                new[] { "BossDifficultyWeight", "BossRarityWeight" },
                delegate(AlgorithmConfig c) { return new[] { c.bossDifficultyWeight, c.bossRarityWeight }; },
                delegate(AlgorithmConfig c, float[] values)
                {
                    c.bossDifficultyWeight = values[0];
                    c.bossRarityWeight = values[1];
                });
            AddDirectConfigScenarios(
                scenarios,
                "Boss performance weight",
                "BossWinRateWeight",
                delegate(AlgorithmConfig c) { return c.bossWinRateWeight; },
                delegate(AlgorithmConfig c, float value) { c.bossWinRateWeight = Mathf.Clamp01(value); });

            // Stage 1: Region depth and region behaviour score weights.
            AddNormalisedPairScenarios(
                scenarios,
                "Region depth weights",
                new[] { "RegionExplorationWeight", "RegionQuestWeight" },
                delegate(AlgorithmConfig c) { return new[] { c.regionExplorationWeight, c.regionQuestWeight }; },
                delegate(AlgorithmConfig c, float[] values)
                {
                    c.regionExplorationWeight = values[0];
                    c.regionQuestWeight = values[1];
                });
            AddNormalisedTripletScenarios(
                scenarios,
                "Region behaviour-score weights",
                new[] { "RegionRecentVisitWeight", "RegionVisitRateWeight", "RegionDepthWeight" },
                delegate(AlgorithmConfig c)
                {
                    return new[]
                    {
                        c.regionRecentVisitWeight,
                        c.regionVisitRateWeight,
                        c.regionDepthWeight
                    };
                },
                delegate(AlgorithmConfig c, float[] values)
                {
                    c.regionRecentVisitWeight = values[0];
                    c.regionVisitRateWeight = values[1];
                    c.regionDepthWeight = values[2];
                });
            AddDirectConfigScenarios(
                scenarios,
                "Region eligibility threshold",
                "RegionEligibilityThreshold",
                delegate(AlgorithmConfig c) { return c.regionEligibilityThreshold; },
                delegate(AlgorithmConfig c, float value) { c.regionEligibilityThreshold = Mathf.Clamp01(value); });

            // Dynamic recency half-lives. Weapon and Boss are authored by the trajectory;
            // Region remains a global AlgorithmConfig value in the present implementation.
            AddDirectTrajectoryScenarios(
                scenarios,
                "Weapon recency half-life",
                "WeaponHalfLifeDays",
                delegate(DynamicPlayerTrajectory t) { return t.halfLifeDays; },
                delegate(DynamicPlayerTrajectory t, float value) { t.halfLifeDays = Mathf.Max(0.001f, value); });
            AddDirectTrajectoryScenarios(
                scenarios,
                "Boss recency half-life",
                "BossRecencyHalfLifeDays",
                delegate(DynamicPlayerTrajectory t) { return t.bossRecencyHalfLifeDays; },
                delegate(DynamicPlayerTrajectory t, float value) { t.bossRecencyHalfLifeDays = Mathf.Max(0.001f, value); });
            AddDirectConfigScenarios(
                scenarios,
                "Region recency half-life",
                "RegionRecencyHalfLifeDays",
                delegate(AlgorithmConfig c) { return c.regionRecencyHalfLifeDays; },
                delegate(AlgorithmConfig c, float value) { c.regionRecencyHalfLifeDays = Mathf.Max(0.001f, value); });

            // Stage 2: soft semantic aggregation weights.
            AddNormalisedTripletScenarios(
                scenarios,
                "Stage 2 semantic aggregation weights",
                new[] { "Stage2WeaponSemanticWeight", "Stage2BossSemanticWeight", "Stage2RegionSemanticWeight" },
                delegate(AlgorithmConfig c)
                {
                    return new[]
                    {
                        c.stage2WeaponSemanticWeight,
                        c.stage2BossSemanticWeight,
                        c.stage2RegionSemanticWeight
                    };
                },
                delegate(AlgorithmConfig c, float[] values)
                {
                    c.stage2WeaponSemanticWeight = values[0];
                    c.stage2BossSemanticWeight = values[1];
                    c.stage2RegionSemanticWeight = values[2];
                });

            // Dynamic smoothing uses the trajectory override (0.75 in the final trajectory),
            // rather than AlgorithmConfig.themeTemporalSmoothingAlpha (0.30).
            AddDirectTrajectoryScenarios(
                scenarios,
                "Dynamic temporal smoothing",
                "TrajectoryThemeTemporalSmoothingAlpha",
                delegate(DynamicPlayerTrajectory t) { return ResolveTrajectorySmoothingAlpha(t, baseConfig); },
                delegate(DynamicPlayerTrajectory t, float value) { t.themeTemporalSmoothingAlpha = Mathf.Clamp01(value); });

            return scenarios;
        }

        private static void AddNormalisedTripletScenarios(
            List<SensitivityScenario> scenarios,
            string group,
            string[] parameterNames,
            Func<AlgorithmConfig, float[]> getter,
            Action<AlgorithmConfig, float[]> setter)
        {
            for (int index = 0; index < 3; index++)
            {
                int capturedIndex = index;
                for (int direction = 0; direction < 2; direction++)
                {
                    float multiplier = direction == 0 ? MinusTenPercent : PlusTenPercent;
                    string directionName = direction == 0 ? "Minus10Percent" : "Plus10Percent";
                    scenarios.Add(new SensitivityScenario
                    {
                        scenarioId = BuildScenarioId(parameterNames[capturedIndex], directionName),
                        parameterGroup = group,
                        parameterName = parameterNames[capturedIndex],
                        direction = directionName,
                        multiplier = multiplier,
                        apply = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                        {
                            setter(config, PerturbAndNormalise(getter(config), capturedIndex, multiplier));
                        },
                        describeGroupValues = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                        {
                            return DescribeNamedValues(parameterNames, getter(config));
                        }
                    });
                }
            }
        }

        private static void AddNormalisedPairScenarios(
            List<SensitivityScenario> scenarios,
            string group,
            string[] parameterNames,
            Func<AlgorithmConfig, float[]> getter,
            Action<AlgorithmConfig, float[]> setter)
        {
            for (int index = 0; index < 2; index++)
            {
                int capturedIndex = index;
                for (int direction = 0; direction < 2; direction++)
                {
                    float multiplier = direction == 0 ? MinusTenPercent : PlusTenPercent;
                    string directionName = direction == 0 ? "Minus10Percent" : "Plus10Percent";
                    scenarios.Add(new SensitivityScenario
                    {
                        scenarioId = BuildScenarioId(parameterNames[capturedIndex], directionName),
                        parameterGroup = group,
                        parameterName = parameterNames[capturedIndex],
                        direction = directionName,
                        multiplier = multiplier,
                        apply = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                        {
                            setter(config, PerturbAndNormalise(getter(config), capturedIndex, multiplier));
                        },
                        describeGroupValues = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                        {
                            return DescribeNamedValues(parameterNames, getter(config));
                        }
                    });
                }
            }
        }

        private static void AddDirectConfigScenarios(
            List<SensitivityScenario> scenarios,
            string group,
            string parameterName,
            Func<AlgorithmConfig, float> getter,
            Action<AlgorithmConfig, float> setter)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                float multiplier = direction == 0 ? MinusTenPercent : PlusTenPercent;
                string directionName = direction == 0 ? "Minus10Percent" : "Plus10Percent";
                scenarios.Add(new SensitivityScenario
                {
                    scenarioId = BuildScenarioId(parameterName, directionName),
                    parameterGroup = group,
                    parameterName = parameterName,
                    direction = directionName,
                    multiplier = multiplier,
                    apply = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                    {
                        setter(config, getter(config) * multiplier);
                    },
                    describeGroupValues = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                    {
                        return parameterName + "=" + F(getter(config));
                    }
                });
            }
        }

        private static void AddDirectTrajectoryScenarios(
            List<SensitivityScenario> scenarios,
            string group,
            string parameterName,
            Func<DynamicPlayerTrajectory, float> getter,
            Action<DynamicPlayerTrajectory, float> setter)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                float multiplier = direction == 0 ? MinusTenPercent : PlusTenPercent;
                string directionName = direction == 0 ? "Minus10Percent" : "Plus10Percent";
                scenarios.Add(new SensitivityScenario
                {
                    scenarioId = BuildScenarioId(parameterName, directionName),
                    parameterGroup = group,
                    parameterName = parameterName,
                    direction = directionName,
                    multiplier = multiplier,
                    apply = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                    {
                        setter(trajectory, getter(trajectory) * multiplier);
                    },
                    describeGroupValues = delegate(AlgorithmConfig config, DynamicPlayerTrajectory trajectory)
                    {
                        return parameterName + "=" + F(getter(trajectory));
                    }
                });
            }
        }

        private static ScenarioOutcome ExecuteScenario(
            SensitivityScenario scenario,
            PcgInputData sourceData,
            DynamicPlayerTrajectory sourceTrajectory)
        {
            PcgInputData data = Clone(sourceData);
            DynamicPlayerTrajectory trajectory = Clone(sourceTrajectory);
            AlgorithmConfig config = data.config ?? new AlgorithmConfig();

            ScenarioOutcome outcome = new ScenarioOutcome
            {
                scenario = scenario,
                baselineValues = scenario.describeGroupValues(sourceData.config, sourceTrajectory)
            };

            scenario.apply(config, trajectory);
            data.config = config;
            outcome.testedValues = scenario.describeGroupValues(config, trajectory);

            TextureSlotGenerator generator = new TextureSlotGenerator();
            PlayerProfile[] profiles = data.profiles != null && data.profiles.profiles != null
                ? data.profiles.profiles
                : new PlayerProfile[0];
            for (int i = 0; i < profiles.Length; i++)
            {
                PlayerProfile profile = profiles[i];
                if (profile != null)
                {
                    outcome.staticResults.Add(generator.Generate(profile, data, StaticSeed));
                }
            }

            DynamicTrajectoryProcessor processor = new DynamicTrajectoryProcessor();
            outcome.dynamicResult = processor.Generate(trajectory, data, DynamicSeedBase);
            return outcome;
        }

        private static void WriteSummaryCsv(
            List<ScenarioOutcome> outcomes,
            ScenarioOutcome baseline,
            string experimentId,
            string generatedAtUtc,
            string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            WriteCsvRow(csv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "ScenarioId", "ParameterGroup", "ParameterName", "Direction",
                "Multiplier", "BaselineParameterValues", "TestedParameterValues", "StaticSeed", "DynamicSeedBase",
                "StaticProfileCount", "StaticExplicitThemeCount", "StaticFallbackCount", "StaticFallbackRate",
                "StaticThemeSemanticSeparation", "DeltaStaticThemeSemanticSeparation",
                "StaticTopSlotDifference", "DeltaStaticTopSlotDifference",
                "StaticThemeChangesVsBaseline", "StaticTopSlotChangesVsBaseline",
                "DynamicNodeCount", "DynamicThemeHitCount", "DynamicThemeHitRate",
                "DynamicTransitionNodeCount", "DynamicTransitionHitCount", "DynamicTransitionHitRate",
                "DynamicFallbackCount", "DynamicThemeChangesVsBaseline",
                "DecayAuditRecordCount", "DecayCorrectRecordCount", "DecayCorrectness"
            });

            float baselineThemeSeparation = ComputeThemeSeparation(baseline.staticResults);
            float baselineSlotDifference = ComputeTopSlotDifference(baseline.staticResults);
            for (int i = 0; i < outcomes.Count; i++)
            {
                ScenarioOutcome outcome = outcomes[i];
                StaticMetrics staticMetrics = BuildStaticMetrics(outcome.staticResults);
                DynamicMetrics dynamicMetrics = BuildDynamicMetrics(outcome.dynamicResult);
                WriteCsvRow(csv, new[]
                {
                    experimentId,
                    generatedAtUtc,
                    outcome.scenario.scenarioId,
                    outcome.scenario.parameterGroup,
                    outcome.scenario.parameterName,
                    outcome.scenario.direction,
                    F(outcome.scenario.multiplier),
                    outcome.baselineValues,
                    outcome.testedValues,
                    StaticSeed.ToString(CultureInfo.InvariantCulture),
                    DynamicSeedBase.ToString(CultureInfo.InvariantCulture),
                    staticMetrics.profileCount.ToString(CultureInfo.InvariantCulture),
                    staticMetrics.explicitThemeCount.ToString(CultureInfo.InvariantCulture),
                    staticMetrics.fallbackCount.ToString(CultureInfo.InvariantCulture),
                    F(staticMetrics.fallbackRate),
                    F(staticMetrics.themeSeparation),
                    F(staticMetrics.themeSeparation - baselineThemeSeparation),
                    F(staticMetrics.topSlotDifference),
                    F(staticMetrics.topSlotDifference - baselineSlotDifference),
                    CountStaticThemeChanges(outcome.staticResults, baseline.staticResults).ToString(CultureInfo.InvariantCulture),
                    CountStaticTopSlotChanges(outcome.staticResults, baseline.staticResults).ToString(CultureInfo.InvariantCulture),
                    dynamicMetrics.nodeCount.ToString(CultureInfo.InvariantCulture),
                    dynamicMetrics.themeHitCount.ToString(CultureInfo.InvariantCulture),
                    F(dynamicMetrics.themeHitRate),
                    dynamicMetrics.transitionNodeCount.ToString(CultureInfo.InvariantCulture),
                    dynamicMetrics.transitionHitCount.ToString(CultureInfo.InvariantCulture),
                    F(dynamicMetrics.transitionHitRate),
                    dynamicMetrics.fallbackCount.ToString(CultureInfo.InvariantCulture),
                    CountDynamicThemeChanges(outcome.dynamicResult, baseline.dynamicResult).ToString(CultureInfo.InvariantCulture),
                    dynamicMetrics.auditRecordCount.ToString(CultureInfo.InvariantCulture),
                    dynamicMetrics.correctAuditCount.ToString(CultureInfo.InvariantCulture),
                    F(dynamicMetrics.decayCorrectness)
                });
            }

            WriteUtf8File(Path.Combine(outputDirectory, "SensitivitySummary.csv"), csv);
        }

        private static void WriteStaticDetailsCsv(
            List<ScenarioOutcome> outcomes,
            ScenarioOutcome baseline,
            string experimentId,
            string generatedAtUtc,
            string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            WriteCsvRow(csv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "ScenarioId", "ParameterGroup", "ParameterName", "Direction",
                "ProfileId", "OutputTheme", "FallbackUsed", "ThemeConfidence", "ThemeMargin",
                "Ice", "Forest", "Galaxy", "Ocean", "Holy",
                "WeaponTopModule", "WeaponSelectedModule", "BossTopModule", "BossSelectedModule",
                "RegionTopModule", "RegionSelectedModule", "ThemeChangedVsBaseline", "TopSlotsChangedVsBaseline"
            });

            for (int scenarioIndex = 0; scenarioIndex < outcomes.Count; scenarioIndex++)
            {
                ScenarioOutcome outcome = outcomes[scenarioIndex];
                for (int resultIndex = 0; resultIndex < outcome.staticResults.Count; resultIndex++)
                {
                    GenerationResult result = outcome.staticResults[resultIndex];
                    GenerationResult baselineResult = FindStaticResult(baseline.staticResults, result.profileId);
                    ThemeGenerationResult theme = result.theme;
                    WriteCsvRow(csv, new[]
                    {
                        experimentId,
                        generatedAtUtc,
                        outcome.scenario.scenarioId,
                        outcome.scenario.parameterGroup,
                        outcome.scenario.parameterName,
                        outcome.scenario.direction,
                        result.profileId,
                        GetThemeId(theme),
                        ToBool(theme != null && theme.fallbackUsed),
                        F(theme != null ? theme.confidence : 0f),
                        F(theme != null ? theme.margin : 0f),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 0)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 1)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 2)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 3)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 4)),
                        GetTopModule(result.weapon),
                        GetSelectedModule(result.weapon),
                        GetTopModule(result.boss),
                        GetSelectedModule(result.boss),
                        GetTopModule(result.region),
                        GetSelectedModule(result.region),
                        ToBool(baselineResult != null && GetThemeId(result.theme) != GetThemeId(baselineResult.theme)),
                        ToBool(baselineResult != null && CountTopSlotChanges(result, baselineResult) > 0)
                    });
                }
            }

            WriteUtf8File(Path.Combine(outputDirectory, "SensitivityStaticDetails.csv"), csv);
        }

        private static void WriteDynamicDetailsCsv(
            List<ScenarioOutcome> outcomes,
            ScenarioOutcome baseline,
            string experimentId,
            string generatedAtUtc,
            string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            WriteCsvRow(csv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "ScenarioId", "ParameterGroup", "ParameterName", "Direction",
                "TrajectoryId", "NodeId", "Day", "ExpectedTheme", "OutputTheme", "ThemeHit",
                "ExpectedDirection", "IsTransitionNode", "FallbackUsed", "ThemeConfidence", "ThemeMargin",
                "RawIce", "RawForest", "RawGalaxy", "RawOcean", "RawHoly",
                "SmoothIce", "SmoothForest", "SmoothGalaxy", "SmoothOcean", "SmoothHoly",
                "WeaponTopModule", "BossTopModule", "RegionTopModule", "ThemeChangedVsBaseline"
            });

            for (int scenarioIndex = 0; scenarioIndex < outcomes.Count; scenarioIndex++)
            {
                ScenarioOutcome outcome = outcomes[scenarioIndex];
                DynamicNodeGenerationResult[] nodes = outcome.dynamicResult != null && outcome.dynamicResult.nodes != null
                    ? outcome.dynamicResult.nodes
                    : new DynamicNodeGenerationResult[0];
                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    DynamicNodeGenerationResult node = nodes[nodeIndex];
                    if (node == null || node.generationResult == null)
                    {
                        continue;
                    }

                    DynamicNodeGenerationResult baselineNode = FindDynamicNode(baseline.dynamicResult, node.nodeId);
                    GenerationResult generation = node.generationResult;
                    ThemeGenerationResult theme = generation.theme;
                    string outputTheme = GetThemeId(theme);
                    WriteCsvRow(csv, new[]
                    {
                        experimentId,
                        generatedAtUtc,
                        outcome.scenario.scenarioId,
                        outcome.scenario.parameterGroup,
                        outcome.scenario.parameterName,
                        outcome.scenario.direction,
                        outcome.dynamicResult != null ? outcome.dynamicResult.trajectoryId : string.Empty,
                        node.nodeId,
                        F(node.day),
                        node.expectedTheme,
                        outputTheme,
                        ToBool(string.Equals(node.expectedTheme, outputTheme, StringComparison.OrdinalIgnoreCase)),
                        node.expectedDirection,
                        ToBool(node.isTransitionNode),
                        ToBool(theme != null && theme.fallbackUsed),
                        F(theme != null ? theme.confidence : 0f),
                        F(theme != null ? theme.margin : 0f),
                        F(GetVectorValue(theme != null ? theme.rawSemanticVector : null, 0)),
                        F(GetVectorValue(theme != null ? theme.rawSemanticVector : null, 1)),
                        F(GetVectorValue(theme != null ? theme.rawSemanticVector : null, 2)),
                        F(GetVectorValue(theme != null ? theme.rawSemanticVector : null, 3)),
                        F(GetVectorValue(theme != null ? theme.rawSemanticVector : null, 4)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 0)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 1)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 2)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 3)),
                        F(GetVectorValue(theme != null ? theme.combinedSemanticVector : null, 4)),
                        GetTopModule(generation.weapon),
                        GetTopModule(generation.boss),
                        GetTopModule(generation.region),
                        ToBool(baselineNode != null && baselineNode.generationResult != null &&
                               outputTheme != GetThemeId(baselineNode.generationResult.theme))
                    });
                }
            }

            WriteUtf8File(Path.Combine(outputDirectory, "SensitivityDynamicDetails.csv"), csv);
        }

        private static void WriteDecayAuditCsv(
            List<ScenarioOutcome> outcomes,
            string experimentId,
            string generatedAtUtc,
            string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            WriteCsvRow(csv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "ScenarioId", "ParameterGroup", "ParameterName", "Direction",
                "TrajectoryId", "NodeId", "Day", "SignalType", "EntityId", "PreviousDay", "PreviousValue",
                "HalfLifeDays", "DecayFactor", "ExpectedDecayedValue", "ObservedDecayedValue", "AuditCorrect"
            });

            for (int scenarioIndex = 0; scenarioIndex < outcomes.Count; scenarioIndex++)
            {
                ScenarioOutcome outcome = outcomes[scenarioIndex];
                DynamicDecayAuditRecord[] records = outcome.dynamicResult != null && outcome.dynamicResult.decayAuditRecords != null
                    ? outcome.dynamicResult.decayAuditRecords
                    : new DynamicDecayAuditRecord[0];
                for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
                {
                    DynamicDecayAuditRecord record = records[recordIndex];
                    if (record == null)
                    {
                        continue;
                    }

                    WriteCsvRow(csv, new[]
                    {
                        experimentId,
                        generatedAtUtc,
                        outcome.scenario.scenarioId,
                        outcome.scenario.parameterGroup,
                        outcome.scenario.parameterName,
                        outcome.scenario.direction,
                        record.trajectoryId,
                        record.nodeId,
                        F(record.day),
                        record.signalType,
                        record.entityId,
                        F(record.previousDay),
                        F(record.previousValue),
                        F(record.halfLifeDays),
                        F(record.decayFactor),
                        F(record.expectedDecayedValue),
                        F(record.observedDecayedValue),
                        ToBool(IsAuditCorrect(record))
                    });
                }
            }

            WriteUtf8File(Path.Combine(outputDirectory, "SensitivityDecayAudit.csv"), csv);
        }

        private static StaticMetrics BuildStaticMetrics(List<GenerationResult> results)
        {
            StaticMetrics metrics = new StaticMetrics { profileCount = results != null ? results.Count : 0 };
            if (results == null || results.Count == 0)
            {
                return metrics;
            }

            HashSet<string> explicitThemes = new HashSet<string>();
            for (int i = 0; i < results.Count; i++)
            {
                ThemeGenerationResult theme = results[i] != null ? results[i].theme : null;
                if (theme != null && theme.fallbackUsed)
                {
                    metrics.fallbackCount++;
                }
                else
                {
                    explicitThemes.Add(GetThemeId(theme));
                }
            }

            metrics.explicitThemeCount = explicitThemes.Count;
            metrics.fallbackRate = SafeDivide(metrics.fallbackCount, metrics.profileCount);
            metrics.themeSeparation = ComputeThemeSeparation(results);
            metrics.topSlotDifference = ComputeTopSlotDifference(results);
            return metrics;
        }

        private static DynamicMetrics BuildDynamicMetrics(DynamicTrajectoryGenerationResult result)
        {
            DynamicMetrics metrics = new DynamicMetrics();
            DynamicNodeGenerationResult[] nodes = result != null && result.nodes != null
                ? result.nodes
                : new DynamicNodeGenerationResult[0];
            for (int i = 0; i < nodes.Length; i++)
            {
                DynamicNodeGenerationResult node = nodes[i];
                if (node == null || node.generationResult == null)
                {
                    continue;
                }

                metrics.nodeCount++;
                ThemeGenerationResult theme = node.generationResult.theme;
                string outputTheme = GetThemeId(theme);
                bool hit = string.Equals(node.expectedTheme, outputTheme, StringComparison.OrdinalIgnoreCase);
                if (hit)
                {
                    metrics.themeHitCount++;
                }

                if (node.isTransitionNode)
                {
                    metrics.transitionNodeCount++;
                    if (hit)
                    {
                        metrics.transitionHitCount++;
                    }
                }

                if (theme != null && theme.fallbackUsed)
                {
                    metrics.fallbackCount++;
                }
            }

            metrics.themeHitRate = SafeDivide(metrics.themeHitCount, metrics.nodeCount);
            metrics.transitionHitRate = SafeDivide(metrics.transitionHitCount, metrics.transitionNodeCount);

            DynamicDecayAuditRecord[] audits = result != null && result.decayAuditRecords != null
                ? result.decayAuditRecords
                : new DynamicDecayAuditRecord[0];
            metrics.auditRecordCount = audits.Length;
            for (int i = 0; i < audits.Length; i++)
            {
                if (audits[i] != null && IsAuditCorrect(audits[i]))
                {
                    metrics.correctAuditCount++;
                }
            }

            metrics.decayCorrectness = SafeDivide(metrics.correctAuditCount, metrics.auditRecordCount);
            return metrics;
        }

        private static float ComputeThemeSeparation(List<GenerationResult> results)
        {
            if (results == null || results.Count < 2)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < results.Count - 1; i++)
            {
                for (int j = i + 1; j < results.Count; j++)
                {
                    sum += CosineDistance(
                        results[i] != null && results[i].theme != null ? results[i].theme.combinedSemanticVector : null,
                        results[j] != null && results[j].theme != null ? results[j].theme.combinedSemanticVector : null);
                    count++;
                }
            }

            return SafeDivide(sum, count);
        }

        private static float ComputeTopSlotDifference(List<GenerationResult> results)
        {
            if (results == null || results.Count < 2)
            {
                return 0f;
            }

            float sum = 0f;
            int pairCount = 0;
            for (int i = 0; i < results.Count - 1; i++)
            {
                for (int j = i + 1; j < results.Count; j++)
                {
                    sum += CountTopSlotChanges(results[i], results[j]) / 3f;
                    pairCount++;
                }
            }

            return SafeDivide(sum, pairCount);
        }

        private static int CountStaticThemeChanges(List<GenerationResult> current, List<GenerationResult> baseline)
        {
            int changes = 0;
            if (current == null || baseline == null)
            {
                return changes;
            }

            for (int i = 0; i < current.Count; i++)
            {
                GenerationResult baselineResult = FindStaticResult(baseline, current[i].profileId);
                if (baselineResult != null && GetThemeId(current[i].theme) != GetThemeId(baselineResult.theme))
                {
                    changes++;
                }
            }

            return changes;
        }

        private static int CountStaticTopSlotChanges(List<GenerationResult> current, List<GenerationResult> baseline)
        {
            int changes = 0;
            if (current == null || baseline == null)
            {
                return changes;
            }

            for (int i = 0; i < current.Count; i++)
            {
                GenerationResult baselineResult = FindStaticResult(baseline, current[i].profileId);
                if (baselineResult != null)
                {
                    changes += CountTopSlotChanges(current[i], baselineResult);
                }
            }

            return changes;
        }

        private static int CountDynamicThemeChanges(
            DynamicTrajectoryGenerationResult current,
            DynamicTrajectoryGenerationResult baseline)
        {
            int changes = 0;
            DynamicNodeGenerationResult[] nodes = current != null && current.nodes != null
                ? current.nodes
                : new DynamicNodeGenerationResult[0];
            for (int i = 0; i < nodes.Length; i++)
            {
                DynamicNodeGenerationResult baselineNode = FindDynamicNode(baseline, nodes[i].nodeId);
                if (baselineNode != null && baselineNode.generationResult != null && nodes[i].generationResult != null &&
                    GetThemeId(nodes[i].generationResult.theme) != GetThemeId(baselineNode.generationResult.theme))
                {
                    changes++;
                }
            }

            return changes;
        }

        private static int CountTopSlotChanges(GenerationResult a, GenerationResult b)
        {
            int changes = 0;
            if (GetTopModule(a != null ? a.weapon : null) != GetTopModule(b != null ? b.weapon : null)) changes++;
            if (GetTopModule(a != null ? a.boss : null) != GetTopModule(b != null ? b.boss : null)) changes++;
            if (GetTopModule(a != null ? a.region : null) != GetTopModule(b != null ? b.region : null)) changes++;
            return changes;
        }

        private static GenerationResult FindStaticResult(List<GenerationResult> results, string profileId)
        {
            if (results == null)
            {
                return null;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null && results[i].profileId == profileId)
                {
                    return results[i];
                }
            }

            return null;
        }

        private static DynamicNodeGenerationResult FindDynamicNode(
            DynamicTrajectoryGenerationResult result,
            string nodeId)
        {
            if (result == null || result.nodes == null)
            {
                return null;
            }

            for (int i = 0; i < result.nodes.Length; i++)
            {
                if (result.nodes[i] != null && result.nodes[i].nodeId == nodeId)
                {
                    return result.nodes[i];
                }
            }

            return null;
        }

        private static bool IsAuditCorrect(DynamicDecayAuditRecord record)
        {
            return record != null &&
                   Mathf.Abs(record.expectedDecayedValue - record.observedDecayedValue) <= AuditTolerance;
        }

        private static float[] PerturbAndNormalise(float[] values, int targetIndex, float multiplier)
        {
            float[] adjusted = values != null ? (float[])values.Clone() : new float[0];
            if (targetIndex < 0 || targetIndex >= adjusted.Length)
            {
                return adjusted;
            }

            adjusted[targetIndex] = Mathf.Max(0f, adjusted[targetIndex] * multiplier);
            float sum = 0f;
            for (int i = 0; i < adjusted.Length; i++)
            {
                sum += adjusted[i];
            }

            if (sum <= 0f)
            {
                return adjusted;
            }

            for (int i = 0; i < adjusted.Length; i++)
            {
                adjusted[i] /= sum;
            }

            return adjusted;
        }

        private static float ResolveTrajectorySmoothingAlpha(DynamicPlayerTrajectory trajectory, AlgorithmConfig config)
        {
            if (trajectory != null && trajectory.themeTemporalSmoothingAlpha >= 0f)
            {
                return trajectory.themeTemporalSmoothingAlpha;
            }

            return config != null ? config.themeTemporalSmoothingAlpha : 0f;
        }

        private static float CosineDistance(float[] a, float[] b)
        {
            int length = Mathf.Max(a != null ? a.Length : 0, b != null ? b.Length : 0);
            if (length == 0)
            {
                return 0f;
            }

            float dot = 0f;
            float magnitudeA = 0f;
            float magnitudeB = 0f;
            for (int i = 0; i < length; i++)
            {
                float av = a != null && i < a.Length ? a[i] : 0f;
                float bv = b != null && i < b.Length ? b[i] : 0f;
                dot += av * bv;
                magnitudeA += av * av;
                magnitudeB += bv * bv;
            }

            if (magnitudeA <= 0f || magnitudeB <= 0f)
            {
                return 0f;
            }

            float similarity = dot / Mathf.Sqrt(magnitudeA * magnitudeB);
            return Mathf.Clamp01(1f - similarity);
        }

        private static T Clone<T>(T source) where T : class
        {
            if (source == null)
            {
                return null;
            }

            return JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
        }

        private static string GetOutputDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "EvaluationResults", "SensitivityAnalysis"));
        }

        private static string BuildScenarioId(string parameterName, string direction)
        {
            return parameterName + "_" + direction;
        }

        private static string DescribeNamedValues(string[] names, float[] values)
        {
            StringBuilder builder = new StringBuilder();
            int length = Mathf.Min(names != null ? names.Length : 0, values != null ? values.Length : 0);
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(names[i]);
                builder.Append("=");
                builder.Append(F(values[i]));
            }

            return builder.ToString();
        }

        private static string GetThemeId(ThemeGenerationResult theme)
        {
            return theme != null && !string.IsNullOrEmpty(theme.selectedThemeId) ? theme.selectedThemeId : "Neutral";
        }

        private static string GetTopModule(SlotGenerationResult slot)
        {
            return slot != null ? slot.topModuleId ?? string.Empty : string.Empty;
        }

        private static string GetSelectedModule(SlotGenerationResult slot)
        {
            return slot != null ? slot.selectedModuleId ?? string.Empty : string.Empty;
        }

        private static float GetVectorValue(float[] vector, int index)
        {
            return vector != null && index >= 0 && index < vector.Length ? vector[index] : 0f;
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            return denominator > 0f ? numerator / denominator : 0f;
        }

        private static void WriteUtf8File(string path, StringBuilder content)
        {
            File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
        }

        private static void WriteCsvRow(StringBuilder builder, string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                string value = values[i] ?? string.Empty;
                builder.Append('"');
                builder.Append(value.Replace("\"", "\"\""));
                builder.Append('"');
            }

            builder.AppendLine();
        }

        private static string F(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string ToBool(bool value)
        {
            return value ? "TRUE" : "FALSE";
        }

        private static void WriteReadme(string outputDirectory)
        {
            const string content =
                "PCG-VFX one-at-a-time sensitivity analysis\r\n" +
                "\r\n" +
                "Design:\r\n" +
                "- Static seed = 99; dynamic seed base = 99.\r\n" +
                "- Baseline uses the final, unmodified configuration.\r\n" +
                "- Each non-baseline scenario changes one parameter by -10% or +10%.\r\n" +
                "- Weight groups are renormalised to sum to one after a component is perturbed.\r\n" +
                "- The dynamic trajectory uses its authored smoothing override of 0.75 as its baseline.\r\n" +
                "\r\n" +
                "Files:\r\n" +
                "- SensitivitySummary.csv: one row per scenario for graphing and thesis tables.\r\n" +
                "- SensitivityStaticDetails.csv: one row per static profile and scenario.\r\n" +
                "- SensitivityDynamicDetails.csv: one row per dynamic node and scenario.\r\n" +
                "- SensitivityDecayAudit.csv: all decay checks for all scenarios.\r\n" +
                "\r\n" +
                "Interpretation:\r\n" +
                "- Delta columns compare a scenario against Baseline.\r\n" +
                "- Theme/slot changes count outputs that changed relative to Baseline.\r\n" +
                "- The analysis identifies sensitive parameters; it does not prove a universally optimal parameter value.\r\n";
            File.WriteAllText(Path.Combine(outputDirectory, "README.txt"), content, new UTF8Encoding(false));
        }

        private struct StaticMetrics
        {
            public int profileCount;
            public int explicitThemeCount;
            public int fallbackCount;
            public float fallbackRate;
            public float themeSeparation;
            public float topSlotDifference;
        }

        private struct DynamicMetrics
        {
            public int nodeCount;
            public int themeHitCount;
            public float themeHitRate;
            public int transitionNodeCount;
            public int transitionHitCount;
            public float transitionHitRate;
            public int fallbackCount;
            public int auditRecordCount;
            public int correctAuditCount;
            public float decayCorrectness;
        }
    }
}
#endif
