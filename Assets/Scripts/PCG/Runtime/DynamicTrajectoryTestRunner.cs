using System.Collections;
using System.Text;
using UnityEngine;

namespace PCG.VFX
{
    public class DynamicTrajectoryTestRunner : MonoBehaviour
    {
        [SerializeField] private string trajectoryRelativePath = "Dynamic/DynamicPlayerTrajectory_Player01.json";
        [SerializeField] private int seedBase = 3001;
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool printCandidateDetails = true;
        [SerializeField] private bool printJsonResult = false;

        [Header("Scene Playback")]
        [Tooltip("When enabled, applies each computed trajectory node to the magic-circle scene in order.")]
        [SerializeField] private bool playTrajectoryInScene = true;
        [Tooltip("Seconds each dynamic node remains visible before the next node replaces it.")]
        [SerializeField] private float secondsPerNode = 3.5f;
        [SerializeField] private float delayBeforePlayback = 0.25f;
        [SerializeField] private bool loopPlayback = false;
        [Tooltip("Assign the PcgGenerationTestRunner on VFX_MagicCircle. It owns the renderer and VFX Graph bindings.")]
        [SerializeField] private PcgGenerationTestRunner sceneVisualApplier;
        [Tooltip("Uses each slot's highest-probability candidate for the trajectory presentation. The underlying Stage 2 theme still uses the full soft distribution.")]
        [SerializeField] private bool useTopCandidateForTrajectoryVisuals = true;

        private Coroutine playbackCoroutine;

        private void Start()
        {
            if (runOnStart)
            {
                RunTrajectory();
            }
        }

        [ContextMenu("Run Dynamic PCG Trajectory")]
        public void RunTrajectory()
        {
            StopTrajectoryPlayback();

            PcgInputData data = PcgDataLoader.LoadFromStreamingAssets();
            DynamicPlayerTrajectory trajectory = PcgDataLoader.LoadDynamicTrajectoryFromStreamingAssets(trajectoryRelativePath);
            DynamicTrajectoryProcessor processor = new DynamicTrajectoryProcessor();
            DynamicTrajectoryGenerationResult result = processor.Generate(trajectory, data, seedBase);

            if (useTopCandidateForTrajectoryVisuals)
            {
                ApplyTopCandidatePresentation(result);
            }

            Debug.Log(BuildSummary(result, printCandidateDetails));

            if (printJsonResult)
            {
                Debug.Log(JsonUtility.ToJson(result, true));
            }

            if (playTrajectoryInScene)
            {
                PcgGenerationTestRunner applier = ResolveSceneVisualApplier();
                if (applier == null)
                {
                    Debug.LogWarning(
                        "[PCG VFX] Dynamic trajectory was calculated, but no PcgGenerationTestRunner was assigned for scene playback.",
                        this);
                    return;
                }

                playbackCoroutine = StartCoroutine(PlayNodesInScene(result, data, applier));
            }
        }

        [ContextMenu("Stop Dynamic Trajectory Playback")]
        public void StopTrajectoryPlayback()
        {
            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = null;
            }
        }

        private IEnumerator PlayNodesInScene(
            DynamicTrajectoryGenerationResult result,
            PcgInputData data,
            PcgGenerationTestRunner applier)
        {
            if (delayBeforePlayback > 0f)
            {
                yield return new WaitForSeconds(delayBeforePlayback);
            }

            DynamicNodeGenerationResult[] nodes = result != null ? result.nodes : null;
            if (nodes == null || nodes.Length == 0)
            {
                playbackCoroutine = null;
                yield break;
            }

            do
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    DynamicNodeGenerationResult node = nodes[i];
                    if (node == null || node.generationResult == null)
                    {
                        continue;
                    }

                    applier.ApplyGenerationResultToScene(node.generationResult, data);
                    string themeId = node.generationResult.theme != null
                        ? node.generationResult.theme.selectedThemeId
                        : "None";
                    Debug.Log(
                        "[PCG VFX] Dynamic playback " + (i + 1) + "/" + nodes.Length +
                        ": " + node.nodeId + " (Day " + node.day.ToString("0.##") +
                        ") -> " + themeId + ".",
                        this);

                    yield return new WaitForSeconds(Mathf.Max(0.1f, secondsPerNode));
                }
            }
            while (loopPlayback);

            playbackCoroutine = null;
            Debug.Log("[PCG VFX] Dynamic trajectory playback finished.", this);
        }

        private PcgGenerationTestRunner ResolveSceneVisualApplier()
        {
            if (sceneVisualApplier != null)
            {
                return sceneVisualApplier;
            }

            return GetComponent<PcgGenerationTestRunner>();
        }

        private void OnDisable()
        {
            StopTrajectoryPlayback();
        }

        private static void ApplyTopCandidatePresentation(DynamicTrajectoryGenerationResult result)
        {
            if (result == null || result.nodes == null)
            {
                return;
            }

            for (int i = 0; i < result.nodes.Length; i++)
            {
                GenerationResult generation = result.nodes[i] != null ? result.nodes[i].generationResult : null;
                if (generation == null)
                {
                    continue;
                }

                SetSelectedToTop(generation.weapon);
                SetSelectedToTop(generation.boss);
                SetSelectedToTop(generation.region);
            }
        }

        private static void SetSelectedToTop(SlotGenerationResult slot)
        {
            if (slot == null || slot.candidates == null || string.IsNullOrEmpty(slot.topModuleId))
            {
                return;
            }

            for (int i = 0; i < slot.candidates.Length; i++)
            {
                CandidateEvaluation candidate = slot.candidates[i];
                if (candidate == null || candidate.moduleId != slot.topModuleId)
                {
                    continue;
                }

                slot.selectedModuleId = candidate.moduleId;
                slot.selectedDataSourceId = candidate.dataSourceId;
                slot.selectedAtlasIndex = candidate.atlasIndex;
                slot.selectedScore = candidate.score;
                slot.selectedProbability = candidate.probability;
                slot.fallbackUsed = false;
                slot.fallbackReason = string.Empty;
                return;
            }
        }

        private static string BuildSummary(DynamicTrajectoryGenerationResult result, bool includeCandidates)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[PCG VFX] Dynamic Trajectory Result");
            builder.AppendLine("Trajectory: " + result.trajectoryId);
            builder.AppendLine("Profile: " + result.profileId);
            builder.AppendLine("Weapon Half-Life Days: " + result.halfLifeDays.ToString("0.##"));

            if (result.nodes == null || result.nodes.Length == 0)
            {
                builder.AppendLine("No dynamic event node result was generated.");
                return builder.ToString();
            }

            for (int i = 0; i < result.nodes.Length; i++)
            {
                DynamicNodeGenerationResult node = result.nodes[i];
                builder.AppendLine();
                builder.AppendLine("============================================================");
                builder.AppendLine("Node: " + node.nodeId + " / Day: " + node.day.ToString("0.##") + " / PlayerLevel: " + node.playerLevel);

                if (node.generationResult == null)
                {
                    builder.AppendLine("Generation result is empty.");
                    continue;
                }

                builder.AppendLine("Run: " + node.generationResult.runId + " / Seed: " + node.generationResult.seed);
                AppendSlot(builder, node.generationResult.weapon, includeCandidates);
                AppendSlot(builder, node.generationResult.boss, includeCandidates);
                AppendSlot(builder, node.generationResult.region, includeCandidates);
                AppendTheme(builder, node.generationResult.theme);
            }

            return builder.ToString();
        }

        private static void AppendSlot(StringBuilder builder, SlotGenerationResult slot, bool includeCandidates)
        {
            if (slot == null)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine(slot.slotType + " Slot");
            builder.AppendLine("Top: " + slot.topModuleId);
            builder.AppendLine("Selected: " + slot.selectedModuleId + " / DataSource: " + slot.selectedDataSourceId + " / AtlasIndex: " + slot.selectedAtlasIndex);
            builder.AppendLine("Selected Score: " + slot.selectedScore.ToString("0.####") + " / Probability: " + slot.selectedProbability.ToString("0.####"));

            if (slot.fallbackUsed)
            {
                builder.AppendLine("Fallback: " + slot.fallbackReason);
            }

            if (!includeCandidates || slot.candidates == null)
            {
                return;
            }

            for (int i = 0; i < slot.candidates.Length; i++)
            {
                CandidateEvaluation candidate = slot.candidates[i];
                builder.AppendLine(
                    "  - " + candidate.moduleId +
                    " eligible=" + candidate.eligible +
                    " inputA=" + candidate.normalizedInputA.ToString("0.####") +
                    " inputB=" + candidate.normalizedInputB.ToString("0.####") +
                    " inputC=" + candidate.normalizedInputC.ToString("0.####") +
                    " confidence=" + candidate.confidence.ToString("0.####") +
                    " score=" + candidate.score.ToString("0.####") +
                    " p=" + candidate.probability.ToString("0.####"));
            }
        }

        private static void AppendTheme(StringBuilder builder, ThemeGenerationResult theme)
        {
            if (theme == null)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("Theme");
            builder.AppendLine("Selected: " + theme.selectedThemeId + " / Atlas: " + theme.selectedAtlasId + " / Texture: " + theme.selectedTexturePath);
            builder.AppendLine("TexIndexRange: " + theme.texIndexMin + "-" + theme.texIndexMax + " / Confidence: " + theme.confidence.ToString("0.####") + " / Margin: " + theme.margin.ToString("0.####"));

            if (theme.combinedSemanticVector != null)
            {
                builder.Append("Combined Vector: [");
                for (int i = 0; i < theme.combinedSemanticVector.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(theme.combinedSemanticVector[i].ToString("0.####"));
                }
                builder.AppendLine("]");
            }

            if (theme.fallbackUsed)
            {
                builder.AppendLine("Fallback: " + theme.fallbackReason);
            }
        }
    }
}
