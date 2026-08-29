using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;

namespace PCG.VFX
{
    public class PcgGenerationTestRunner : MonoBehaviour
    {
        [SerializeField] private string profileId = "Player_01";
        [SerializeField] private int seed = 1001;
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool printCandidateDetails = true;

        [Header("Slot Material Binding")]
        [SerializeField] private bool applySelectedAtlasIndices = true;
        [SerializeField] private Renderer weaponSlotRenderer;
        [SerializeField] private Renderer bossSlotRenderer;
        [SerializeField] private Renderer regionSlotRenderer;
        [Tooltip("Shader Graph reference for the primary slot texture. Existing scenes using the old Slot_Index field keep working through automatic property fallback.")]
        [FormerlySerializedAs("slotIndexProperty")]
        [SerializeField] private string slotIndexFirstProperty = "_Slot_Index_First";
        [Tooltip("Shader Graph reference for the competing slot texture.")]
        [SerializeField] private string slotIndexSecondProperty = "_Slot_Index_Second";
        [Tooltip("Shader Graph reference that blends from the first texture (0) to the second texture (1).")]
        [SerializeField] private string themeConflictBlendProperty = "_ThemeConflictBlend";

        [Header("Slot Conflict Presentation")]
        [Tooltip("Only a Neutral fallback with a small Stage 2 theme margin may animate competing slot textures.")]
        [SerializeField] private bool animateStrongSlotConflicts = true;
        [Tooltip("Seconds for which each competing texture remains fully visible before cross-fading.")]
        [SerializeField, Min(0.1f)] private float conflictHoldSeconds = 3f;
        [Tooltip("Cross-fade duration between the two competing textures.")]
        [SerializeField, Min(0.05f)] private float conflictCrossfadeSeconds = 0.35f;

        [Header("Theme Visual Binding")]
        [SerializeField] private bool applyInferredTheme = true;
        [SerializeField] private Renderer baseCircleRenderer;
        [SerializeField] private VisualEffect themeVisualEffect;
        [SerializeField] private string vfxThemeObjectName = "VFX_Theme";
        [SerializeField] private string baseCircleColorProperty = "Color";
        [SerializeField] private string slotColorProperty = "Color";
        [SerializeField] private bool restartVfxWhenThemeIsApplied = true;

        private MaterialPropertyBlock slotPropertyBlock;
        private Coroutine pendingThemeEventCoroutine;
        private readonly List<Coroutine> slotConflictCoroutines = new List<Coroutine>();

        private struct SlotConflictPair
        {
            public CandidateEvaluation first;
            public CandidateEvaluation second;
            public float probabilityRatio;
        }

        /// <summary>
        /// Read-only access for reproducible evaluation tools such as the DreamSim screenshot runner.
        /// </summary>
        public string ProfileId => profileId;

        private void Start()
        {
            if (runOnStart)
            {
                RunOnce();
            }
        }

        private void OnDisable()
        {
            StopSlotConflictAnimations();
        }

        [ContextMenu("Run PCG Texture Slot Generation")]
        public void RunOnce()
        {
            PcgInputData data = PcgDataLoader.LoadFromStreamingAssets();
            PlayerProfile profile = FindProfile(data, profileId);
            TextureSlotGenerator generator = new TextureSlotGenerator();
            GenerationResult result = generator.Generate(profile, data, seed);

            Debug.Log(BuildSummary(result, printCandidateDetails));
            ApplyGenerationResultToScene(result, data);
            Debug.Log(JsonUtility.ToJson(result, true));
        }

        /// <summary>
        /// Applies an already calculated PCG result to the three slot renderers and VFX_Theme.
        /// DynamicTrajectoryTestRunner uses this so static and dynamic generation share exactly
        /// the same material, texture, and named-event application path.
        /// </summary>
        public void ApplyGenerationResultToScene(GenerationResult result, PcgInputData data)
        {
            StopSlotConflictAnimations();
            ApplySelectedAtlasIndices(result, data != null ? data.config : null);
            ApplyInferredTheme(result, data);
        }

        private void ApplyInferredTheme(GenerationResult result, PcgInputData data)
        {
            if (!applyInferredTheme || result == null || result.theme == null)
            {
                return;
            }

            ThemeMaterialBindings materialBindings = data != null && data.themes != null
                ? data.themes.materialBindings
                : null;
            string resolvedBaseCircleColorProperty = ResolveBinding(
                materialBindings != null ? materialBindings.baseCircleColorProperty : null,
                baseCircleColorProperty);
            string resolvedSlotColorProperty = ResolveBinding(
                materialBindings != null ? materialBindings.slotColorProperty : null,
                slotColorProperty);

            ApplyRendererColor(
                ResolveSlotRenderer(baseCircleRenderer, "BaseCircle"),
                "BaseCircle",
                resolvedBaseCircleColorProperty,
                result.theme.magicCircleColorRgba);
            ApplyRendererColor(
                ResolveSlotRenderer(weaponSlotRenderer, "Weapon_slot"),
                "Weapon",
                resolvedSlotColorProperty,
                result.theme.slotColorRgba);
            ApplyRendererColor(
                ResolveSlotRenderer(bossSlotRenderer, "Boss_slot"),
                "Boss",
                resolvedSlotColorProperty,
                result.theme.slotColorRgba);
            ApplyRendererColor(
                ResolveSlotRenderer(regionSlotRenderer, "Region_slot"),
                "Region",
                resolvedSlotColorProperty,
                result.theme.slotColorRgba);

            ApplyThemeToVisualEffect(result.theme, data != null ? data.themes : null);
        }

        private void ApplySelectedAtlasIndices(GenerationResult result, AlgorithmConfig config)
        {
            if (!applySelectedAtlasIndices || result == null)
            {
                return;
            }

            bool useConflictPresentation = IsThemeConflictFallback(result, config);

            ApplySlotPresentation(
                ResolveSlotRenderer(weaponSlotRenderer, "Weapon_slot"),
                "Weapon",
                result.weapon,
                useConflictPresentation,
                config);
            ApplySlotPresentation(
                ResolveSlotRenderer(bossSlotRenderer, "Boss_slot"),
                "Boss",
                result.boss,
                useConflictPresentation,
                config);
            ApplySlotPresentation(
                ResolveSlotRenderer(regionSlotRenderer, "Region_slot"),
                "Region",
                result.region,
                useConflictPresentation,
                config);
        }

        private Renderer ResolveSlotRenderer(Renderer configuredRenderer, string childName)
        {
            if (configuredRenderer != null)
            {
                return configuredRenderer;
            }

            Transform child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogWarning(
                    "[PCG VFX] Could not find child '" + childName +
                    "'. Assign its MeshRenderer in PcgGenerationTestRunner.",
                    this);
                return null;
            }

            return child.GetComponent<Renderer>();
        }

        private void ApplySlotPresentation(
            Renderer targetRenderer,
            string slotLabel,
            SlotGenerationResult slotResult,
            bool useConflictPresentation,
            AlgorithmConfig config)
        {
            if (slotResult == null)
            {
                Debug.LogWarning("[PCG VFX] " + slotLabel + " generation result is missing.", this);
                return;
            }

            if (targetRenderer == null)
            {
                Debug.LogWarning(
                    "[PCG VFX] " + slotLabel +
                    " slot renderer is missing; AtlasIndex " + slotResult.selectedAtlasIndex +
                    " was generated but could not be displayed.",
                    this);
                return;
            }

            Material sharedMaterial = targetRenderer.sharedMaterial;
            if (sharedMaterial == null)
            {
                Debug.LogWarning("[PCG VFX] " + slotLabel + " slot renderer has no material.", targetRenderer);
                return;
            }

            int firstAtlasIndex = slotResult.selectedAtlasIndex;
            int secondAtlasIndex = firstAtlasIndex;
            SlotConflictPair conflictPair = new SlotConflictPair();
            bool shouldAlternate = useConflictPresentation &&
                                   TryGetStrongConflictPair(slotResult, config, out conflictPair);
            if (shouldAlternate)
            {
                // A conflict must display the two candidates that actually triggered it:
                // the highest- and second-highest-probability candidates. The independently
                // sampled slot result is not used here because it can be a low-probability item.
                firstAtlasIndex = conflictPair.first.atlasIndex;
                secondAtlasIndex = conflictPair.second.atlasIndex;
            }

            if (!SetSlotShaderProperties(targetRenderer, firstAtlasIndex, secondAtlasIndex, 0f))
            {
                return;
            }

            Debug.Log(
                "[PCG VFX] Applied " + slotLabel + " AtlasIndex " +
                firstAtlasIndex + " (" + (shouldAlternate ? conflictPair.first.moduleId : slotResult.selectedModuleId) + ") to " +
                targetRenderer.gameObject.name + ".",
                targetRenderer);

            if (!shouldAlternate)
            {
                return;
            }

            Coroutine coroutine = StartCoroutine(AnimateSlotConflict(
                targetRenderer,
                slotLabel,
                firstAtlasIndex,
                secondAtlasIndex,
                conflictPair));
            slotConflictCoroutines.Add(coroutine);
        }

        private bool IsThemeConflictFallback(GenerationResult result, AlgorithmConfig config)
        {
            if (!animateStrongSlotConflicts || result == null || result.theme == null || !result.theme.fallbackUsed)
            {
                return false;
            }

            float marginThreshold = config != null
                ? Mathf.Clamp01(config.themeConflictMarginThreshold)
                : 0.10f;
            return result.theme.margin <= marginThreshold;
        }

        private static bool TryGetStrongConflictPair(
            SlotGenerationResult slotResult,
            AlgorithmConfig config,
            out SlotConflictPair pair)
        {
            pair = new SlotConflictPair();
            if (slotResult == null || slotResult.candidates == null)
            {
                return false;
            }

            CandidateEvaluation first = null;
            CandidateEvaluation second = null;
            for (int i = 0; i < slotResult.candidates.Length; i++)
            {
                CandidateEvaluation candidate = slotResult.candidates[i];
                if (candidate == null || !candidate.eligible || candidate.probability <= 0f)
                {
                    continue;
                }

                if (first == null || candidate.probability > first.probability)
                {
                    second = first;
                    first = candidate;
                }
                else if (second == null || candidate.probability > second.probability)
                {
                    second = candidate;
                }
            }

            if (first == null || second == null || first.probability <= 0f)
            {
                return false;
            }

            float ratio = Mathf.Clamp01(second.probability / first.probability);
            float requiredRatio = config != null
                ? Mathf.Clamp01(config.slotConflictProbabilityRatio)
                : 0.75f;
            if (ratio < requiredRatio)
            {
                return false;
            }

            pair = new SlotConflictPair
            {
                first = first,
                second = second,
                probabilityRatio = ratio
            };
            return true;
        }

        private bool SetSlotShaderProperties(
            Renderer targetRenderer,
            int firstAtlasIndex,
            int secondAtlasIndex,
            float conflictBlend)
        {
            if (targetRenderer == null || targetRenderer.sharedMaterial == null)
            {
                return false;
            }

            Material material = targetRenderer.sharedMaterial;
            string firstProperty = ResolveMaterialProperty(
                material,
                slotIndexFirstProperty,
                "_Slot_Index_First",
                "Slot_Index_First",
                "_Slot_Index",
                "Slot_Index");
            string secondProperty = ResolveMaterialProperty(
                material,
                slotIndexSecondProperty,
                "_Slot_Index_Second",
                "Slot_Index_Second");
            string blendProperty = ResolveMaterialProperty(
                material,
                themeConflictBlendProperty,
                "_ThemeConflictBlend",
                "ThemeConflictBlend");

            if (string.IsNullOrEmpty(firstProperty) || string.IsNullOrEmpty(secondProperty) || string.IsNullOrEmpty(blendProperty))
            {
                Debug.LogWarning(
                    "[PCG VFX] Material '" + material.name +
                    "' is missing one of the conflict shader properties. Expected references are " +
                    "_Slot_Index_First, _Slot_Index_Second, and _ThemeConflictBlend.",
                    targetRenderer);
                return false;
            }

            if (slotPropertyBlock == null)
            {
                slotPropertyBlock = new MaterialPropertyBlock();
            }

            slotPropertyBlock.Clear();
            targetRenderer.GetPropertyBlock(slotPropertyBlock);
            slotPropertyBlock.SetFloat(firstProperty, firstAtlasIndex);
            slotPropertyBlock.SetFloat(secondProperty, secondAtlasIndex);
            slotPropertyBlock.SetFloat(blendProperty, Mathf.Clamp01(conflictBlend));
            targetRenderer.SetPropertyBlock(slotPropertyBlock);
            return true;
        }

        private IEnumerator AnimateSlotConflict(
            Renderer targetRenderer,
            string slotLabel,
            int firstAtlasIndex,
            int secondAtlasIndex,
            SlotConflictPair pair)
        {
            Debug.Log(
                "[PCG VFX] " + slotLabel + " conflict presentation: " +
                pair.first.moduleId + " (p=" + pair.first.probability.ToString("0.####") + ") <-> " +
                pair.second.moduleId + " (p=" + pair.second.probability.ToString("0.####") +
                "), ratio=" + pair.probabilityRatio.ToString("0.####") + ".",
                targetRenderer);

            while (targetRenderer != null)
            {
                yield return new WaitForSeconds(Mathf.Max(0.1f, conflictHoldSeconds));
                yield return CrossfadeSlotConflict(targetRenderer, firstAtlasIndex, secondAtlasIndex, 0f, 1f);
                yield return new WaitForSeconds(Mathf.Max(0.1f, conflictHoldSeconds));
                yield return CrossfadeSlotConflict(targetRenderer, firstAtlasIndex, secondAtlasIndex, 1f, 0f);
            }
        }

        private IEnumerator CrossfadeSlotConflict(
            Renderer targetRenderer,
            int firstAtlasIndex,
            int secondAtlasIndex,
            float from,
            float to)
        {
            float duration = Mathf.Max(0.05f, conflictCrossfadeSeconds);
            float elapsed = 0f;
            while (targetRenderer != null && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float blend = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, normalizedTime));
                if (!SetSlotShaderProperties(targetRenderer, firstAtlasIndex, secondAtlasIndex, blend))
                {
                    yield break;
                }

                yield return null;
            }

            if (targetRenderer != null)
            {
                SetSlotShaderProperties(targetRenderer, firstAtlasIndex, secondAtlasIndex, to);
            }
        }

        private void ApplyRendererColor(Renderer targetRenderer, string targetLabel, string propertyName, float[] rgba)
        {
            if (targetRenderer == null || !TryGetColor(rgba, out Color color))
            {
                return;
            }

            Material sharedMaterial = targetRenderer.sharedMaterial;
            if (sharedMaterial == null || !sharedMaterial.HasProperty(propertyName))
            {
                Debug.LogWarning(
                    "[PCG VFX] " + targetLabel + " material does not expose color property '" +
                    propertyName + "'. Check the Shader Graph property Reference field.",
                    targetRenderer);
                return;
            }

            if (slotPropertyBlock == null)
            {
                slotPropertyBlock = new MaterialPropertyBlock();
            }

            slotPropertyBlock.Clear();
            targetRenderer.GetPropertyBlock(slotPropertyBlock);
            slotPropertyBlock.SetColor(propertyName, color);
            targetRenderer.SetPropertyBlock(slotPropertyBlock);
        }

        private static string ResolveMaterialProperty(Material material, string configuredProperty, params string[] fallbackProperties)
        {
            if (material == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(configuredProperty) && material.HasProperty(configuredProperty))
            {
                return configuredProperty;
            }

            if (fallbackProperties == null)
            {
                return null;
            }

            for (int i = 0; i < fallbackProperties.Length; i++)
            {
                string candidate = fallbackProperties[i];
                if (!string.IsNullOrEmpty(candidate) && material.HasProperty(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void StopSlotConflictAnimations()
        {
            for (int i = 0; i < slotConflictCoroutines.Count; i++)
            {
                Coroutine coroutine = slotConflictCoroutines[i];
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
            }

            slotConflictCoroutines.Clear();
        }

        private void ApplyThemeToVisualEffect(ThemeGenerationResult theme, ThemeDefinitionSet definitions)
        {
            VisualEffect visualEffect = ResolveThemeVisualEffect();
            if (visualEffect == null)
            {
                Debug.LogWarning(
                    "[PCG VFX] VFX_Theme was not found. Assign the Visual Effect component in PcgGenerationTestRunner.",
                    this);
                return;
            }

            ThemeVfxBindings bindings = definitions != null ? definitions.vfxBindings : null;
            ParticleVfxParameters parameters = theme.particleVfx;
            if (parameters == null)
            {
                Debug.LogWarning(
                    "[PCG VFX] Theme '" + theme.selectedThemeId +
                    "' has no tuned particleVfx block. The graph keeps its current motion settings.",
                    visualEffect);
            }
            else
            {
                SetVfxColor(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.hdrColorProperty : null, "SparkColor"),
                    parameters.sparkColorRgba);
                SetVfxFloat(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.sizeProperty : null, "SparkSize"),
                    parameters.sparkSize);
                SetVfxFloat(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.lifetimeProperty : null, "SparkLife"),
                    parameters.sparkLife);
                SetVfxFloat(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.yOffsetProperty : null, "YOffset"),
                    parameters.yOffset);
                SetVfxFloat(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.speedProperty : null, "SparkSpeed"),
                    parameters.sparkSpeed);
                SetVfxFloat(
                    visualEffect,
                    ResolveBinding(bindings != null ? bindings.linearDragProperty : null, "LinearDrag"),
                    parameters.linearDrag);
                ApplyThemeTurbulence(visualEffect, bindings, parameters);
            }

            bool shouldApplyParticleTexture = parameters == null || parameters.applyParticleTexture;
            if (shouldApplyParticleTexture)
            {
                Texture2D particleTexture = Resources.Load<Texture2D>(theme.selectedTexturePath);
                string textureProperty = ResolveBinding(
                    bindings != null ? bindings.particleTextureProperty : null,
                    "Particle_Texture");
                if (particleTexture == null)
                {
                    Debug.LogWarning(
                        "[PCG VFX] Could not load particle texture from Resources path '" +
                        theme.selectedTexturePath + "'.",
                        visualEffect);
                }
                else if (!visualEffect.HasTexture(textureProperty))
                {
                    Debug.LogWarning(
                        "[PCG VFX] VFX Graph does not expose texture property '" + textureProperty + "'.",
                        visualEffect);
                }
                else
                {
                    visualEffect.SetTexture(textureProperty, particleTexture);
                }
            }
            else
            {
                Debug.Log(
                    "[PCG VFX] Theme '" + theme.selectedThemeId +
                    "' keeps the VFX Graph's currently assigned particle texture (atlas pending).",
                    visualEffect);
            }

            if (restartVfxWhenThemeIsApplied)
            {
                visualEffect.Reinit();
                string eventName = string.IsNullOrEmpty(theme.vfxEventName)
                    ? "OnBurst"
                    : theme.vfxEventName;

                // Reinit resets the graph. Dispatching the named event on the following frame prevents
                // the reset from consuming it before its target Event Context is ready.
                if (pendingThemeEventCoroutine != null)
                {
                    StopCoroutine(pendingThemeEventCoroutine);
                }

                pendingThemeEventCoroutine = StartCoroutine(
                    SendThemeEventAfterReinitialization(visualEffect, eventName, theme.selectedThemeId));
            }

            Debug.Log(
                "[PCG VFX] Applied theme '" + theme.selectedThemeId +
                "' to " + visualEffect.gameObject.name + ".",
                visualEffect);
        }

        private IEnumerator SendThemeEventAfterReinitialization(
            VisualEffect visualEffect,
            string eventName,
            string themeId)
        {
            yield return null;

            if (visualEffect == null || !visualEffect.isActiveAndEnabled)
            {
                Debug.LogWarning(
                    "[PCG VFX] Could not send event '" + eventName +
                    "' because VFX_Theme is disabled or missing.",
                    this);
                pendingThemeEventCoroutine = null;
                yield break;
            }

            visualEffect.SendEvent(eventName);
            Debug.Log(
                "[PCG VFX] Sent event '" + eventName + "' for theme '" +
                themeId + "' after VFX reinitialization.",
                visualEffect);
            pendingThemeEventCoroutine = null;
        }

        private VisualEffect ResolveThemeVisualEffect()
        {
            if (themeVisualEffect != null)
            {
                return themeVisualEffect;
            }

            VisualEffect childEffect = GetComponentInChildren<VisualEffect>();
            if (childEffect != null)
            {
                return childEffect;
            }

            GameObject vfxObject = GameObject.Find(vfxThemeObjectName);
            return vfxObject != null ? vfxObject.GetComponent<VisualEffect>() : null;
        }

        private static string ResolveBinding(string dataBinding, string fallbackBinding)
        {
            return string.IsNullOrEmpty(dataBinding) ? fallbackBinding : dataBinding;
        }

        private static void SetVfxColor(VisualEffect visualEffect, string propertyName, float[] rgba)
        {
            if (!TryGetColor(rgba, out Color color))
            {
                return;
            }

            if (!visualEffect.HasVector4(propertyName))
            {
                Debug.LogWarning("[PCG VFX] VFX Graph does not expose color property '" + propertyName + "'.", visualEffect);
                return;
            }

            visualEffect.SetVector4(propertyName, color);
        }

        private static void SetVfxFloat(VisualEffect visualEffect, string propertyName, float value)
        {
            if (!visualEffect.HasFloat(propertyName))
            {
                Debug.LogWarning("[PCG VFX] VFX Graph does not expose float property '" + propertyName + "'.", visualEffect);
                return;
            }

            visualEffect.SetFloat(propertyName, value);
        }

        private static void SetVfxOctaves(VisualEffect visualEffect, string propertyName, int value)
        {
            int safeValue = Mathf.Max(1, value);
            if (visualEffect.HasUInt(propertyName))
            {
                visualEffect.SetUInt(propertyName, (uint)safeValue);
                return;
            }

            if (visualEffect.HasInt(propertyName))
            {
                visualEffect.SetInt(propertyName, safeValue);
                return;
            }

            Debug.LogWarning(
                "[PCG VFX] VFX Graph does not expose integer/uint property '" + propertyName + "'.",
                visualEffect);
        }

        private static void ApplyThemeTurbulence(VisualEffect visualEffect, ThemeVfxBindings bindings, ParticleVfxParameters parameters)
        {
            float intensity = parameters.useTurbulence ? Mathf.Max(0f, parameters.turbulenceIntensity) : 0f;
            SetVfxFloat(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceIntensityProperty : null, "TurbulenceIntensity"),
                intensity);

            if (!parameters.useTurbulence)
            {
                return;
            }

            SetVfxFloat(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceDragProperty : null, "TurbulenceDrag"),
                parameters.turbulenceDrag);
            SetVfxFloat(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceFrequencyProperty : null, "TurbulenceFrequency"),
                parameters.turbulenceFrequency);
            SetVfxOctaves(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceOctavesProperty : null, "TurbulenceOctaves"),
                parameters.turbulenceOctaves);
            SetVfxFloat(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceRoughnessProperty : null, "TurbulenceRoughness"),
                parameters.turbulenceRoughness);
            SetVfxFloat(
                visualEffect,
                ResolveBinding(bindings != null ? bindings.turbulenceLacunarityProperty : null, "TurbulenceLacunarity"),
                parameters.turbulenceLacunarity);
        }

        private static bool TryGetColor(float[] rgba, out Color color)
        {
            color = Color.white;
            if (rgba == null || rgba.Length < 4)
            {
                return false;
            }

            color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
            return true;
        }

        private static PlayerProfile FindProfile(PcgInputData data, string targetProfileId)
        {
            for (int i = 0; i < data.profiles.profiles.Length; i++)
            {
                PlayerProfile profile = data.profiles.profiles[i];
                if (profile.identityId == targetProfileId)
                {
                    return profile;
                }
            }

            Debug.LogWarning("Profile '" + targetProfileId + "' was not found. Falling back to first profile.");
            return data.profiles.profiles[0];
        }

        private static string BuildSummary(GenerationResult result, bool includeCandidates)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[PCG VFX] Texture Slot Generation Result");
            builder.AppendLine("Run: " + result.runId);
            builder.AppendLine("Profile: " + result.profileId + " (" + result.profileIntent + ")");
            builder.AppendLine("Seed: " + result.seed);
            AppendSlot(builder, result.weapon, includeCandidates);
            AppendSlot(builder, result.boss, includeCandidates);
            AppendSlot(builder, result.region, includeCandidates);
            AppendTheme(builder, result.theme);
            return builder.ToString();
        }

        private static void AppendSlot(StringBuilder builder, SlotGenerationResult slot, bool includeCandidates)
        {
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

            if (theme.fallbackUsed)
            {
                builder.AppendLine("Fallback: " + theme.fallbackReason);
            }
        }
    }
}
