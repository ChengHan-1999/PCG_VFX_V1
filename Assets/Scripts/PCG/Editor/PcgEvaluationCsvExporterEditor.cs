#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PCG.VFX
{
    /// <summary>
    /// Writes the three reproducible, flat CSV datasets used by the thesis evaluation.
    /// This menu item only reads synthetic player data; it never modifies player or theme JSON.
    /// </summary>
    public static class PcgEvaluationCsvExporterEditor
    {
        private const string DynamicTrajectoryPath = "Dynamic/DynamicPlayerTrajectory_Player01.json";
        // Fixed after the final seed sweep. It keeps any sampled slot display reproducible
        // while theme metrics continue to use the full candidate distributions.
        private const int StaticEvaluationSeed = 99;
        private const int DynamicSeedBase = 99;

        [MenuItem("PCG VFX/Evaluation/Export All Evaluation CSV")]
        public static void ExportAllEvaluationCsv()
        {
            try
            {
                PcgInputData data = PcgDataLoader.LoadFromStreamingAssets();
                string generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                string experimentId = "PCG_VFX_Evaluation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string outputDirectory = GetOutputDirectory();
                Directory.CreateDirectory(outputDirectory);

                WriteStaticResults(data, experimentId, generatedAtUtc, outputDirectory);
                WriteDynamicResults(data, experimentId, generatedAtUtc, outputDirectory);

                AssetDatabase.Refresh();
                Debug.Log(
                    "[PCG VFX] Evaluation CSV export completed. Files written to: " +
                    outputDirectory + "\n- StaticResults.csv\n- DynamicResults.csv\n- DecayAudit.csv");
            }
            catch (Exception exception)
            {
                Debug.LogError("[PCG VFX] Evaluation CSV export failed: " + exception.Message);
                throw;
            }
        }

        [MenuItem("PCG VFX/Evaluation/Open Evaluation Results Folder")]
        public static void OpenEvaluationResultsFolder()
        {
            string outputDirectory = GetOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            EditorUtility.RevealInFinder(outputDirectory);
        }

        private static void WriteStaticResults(PcgInputData data, string experimentId, string generatedAtUtc, string outputDirectory)
        {
            StringBuilder csv = new StringBuilder();
            WriteCsvRow(csv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "ProfileId", "ProfileIntent", "OutputTheme", "FallbackUsed",
                "ThemeConfidence", "ThemeMargin", "Ice", "Forest", "Galaxy", "Ocean", "Holy",
                "WeaponTopModule", "WeaponAtlasIndex", "BossTopModule", "BossAtlasIndex", "RegionTopModule", "RegionAtlasIndex"
            });

            TextureSlotGenerator generator = new TextureSlotGenerator();
            PlayerProfile[] profiles = data.profiles != null && data.profiles.profiles != null
                ? data.profiles.profiles
                : new PlayerProfile[0];

            for (int i = 0; i < profiles.Length; i++)
            {
                PlayerProfile profile = profiles[i];
                if (profile == null)
                {
                    continue;
                }

                GenerationResult result = generator.Generate(profile, data, StaticEvaluationSeed);
                ThemeGenerationResult theme = result.theme;
                WriteCsvRow(csv, new[]
                {
                    experimentId,
                    generatedAtUtc,
                    profile.identityId,
                    profile.profileIntent,
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
                    I(GetTopAtlasIndex(result.weapon)),
                    GetTopModule(result.boss),
                    I(GetTopAtlasIndex(result.boss)),
                    GetTopModule(result.region),
                    I(GetTopAtlasIndex(result.region))
                });
            }

            WriteUtf8File(Path.Combine(outputDirectory, "StaticResults.csv"), csv);
        }

        private static void WriteDynamicResults(PcgInputData data, string experimentId, string generatedAtUtc, string outputDirectory)
        {
            DynamicPlayerTrajectory trajectory = PcgDataLoader.LoadDynamicTrajectoryFromStreamingAssets(DynamicTrajectoryPath);
            DynamicTrajectoryProcessor processor = new DynamicTrajectoryProcessor();
            DynamicTrajectoryGenerationResult result = processor.Generate(trajectory, data, DynamicSeedBase);

            StringBuilder dynamicCsv = new StringBuilder();
            WriteCsvRow(dynamicCsv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "TrajectoryId", "NodeId", "Day", "ExpectedTheme", "OutputTheme",
                "ExpectedDirection", "IsTransitionNode", "FallbackUsed", "ThemeConfidence", "ThemeMargin",
                "RawIce", "RawForest", "RawGalaxy", "RawOcean", "RawHoly",
                "SmoothIce", "SmoothForest", "SmoothGalaxy", "SmoothOcean", "SmoothHoly",
                "WeaponTopModule", "BossTopModule", "RegionTopModule"
            });

            DynamicNodeGenerationResult[] nodes = result.nodes ?? new DynamicNodeGenerationResult[0];
            for (int i = 0; i < nodes.Length; i++)
            {
                DynamicNodeGenerationResult node = nodes[i];
                if (node == null || node.generationResult == null)
                {
                    continue;
                }

                GenerationResult generation = node.generationResult;
                ThemeGenerationResult theme = generation.theme;
                WriteCsvRow(dynamicCsv, new[]
                {
                    experimentId,
                    generatedAtUtc,
                    result.trajectoryId,
                    node.nodeId,
                    F(node.day),
                    node.expectedTheme,
                    GetThemeId(theme),
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
                    GetTopModule(generation.region)
                });
            }

            StringBuilder decayCsv = new StringBuilder();
            WriteCsvRow(decayCsv, new[]
            {
                "ExperimentId", "GeneratedAtUtc", "TrajectoryId", "NodeId", "Day", "SignalType", "EntityId",
                "PreviousDay", "PreviousValue", "HalfLifeDays", "DecayFactor", "ExpectedDecayedValue", "ObservedDecayedValue"
            });

            DynamicDecayAuditRecord[] audits = result.decayAuditRecords ?? new DynamicDecayAuditRecord[0];
            for (int i = 0; i < audits.Length; i++)
            {
                DynamicDecayAuditRecord audit = audits[i];
                if (audit == null)
                {
                    continue;
                }

                WriteCsvRow(decayCsv, new[]
                {
                    experimentId,
                    generatedAtUtc,
                    audit.trajectoryId,
                    audit.nodeId,
                    F(audit.day),
                    audit.signalType,
                    audit.entityId,
                    F(audit.previousDay),
                    F(audit.previousValue),
                    F(audit.halfLifeDays),
                    F(audit.decayFactor),
                    F(audit.expectedDecayedValue),
                    F(audit.observedDecayedValue)
                });
            }

            WriteUtf8File(Path.Combine(outputDirectory, "DynamicResults.csv"), dynamicCsv);
            WriteUtf8File(Path.Combine(outputDirectory, "DecayAudit.csv"), decayCsv);
        }

        private static string GetOutputDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "EvaluationResults"));
        }

        private static string GetThemeId(ThemeGenerationResult theme)
        {
            return theme != null && !string.IsNullOrEmpty(theme.selectedThemeId) ? theme.selectedThemeId : "Neutral";
        }

        private static string GetTopModule(SlotGenerationResult slot)
        {
            return slot != null ? slot.topModuleId : string.Empty;
        }

        private static int GetTopAtlasIndex(SlotGenerationResult slot)
        {
            if (slot == null || slot.candidates == null || string.IsNullOrEmpty(slot.topModuleId))
            {
                return -1;
            }

            for (int i = 0; i < slot.candidates.Length; i++)
            {
                CandidateEvaluation candidate = slot.candidates[i];
                if (candidate != null && candidate.moduleId == slot.topModuleId)
                {
                    return candidate.atlasIndex;
                }
            }

            return -1;
        }

        private static float GetVectorValue(float[] vector, int index)
        {
            return vector != null && index >= 0 && index < vector.Length ? vector[index] : 0f;
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

        private static string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string ToBool(bool value)
        {
            return value ? "TRUE" : "FALSE";
        }
    }
}
#endif
