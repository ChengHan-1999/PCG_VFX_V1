using UnityEngine;

namespace PCG.VFX
{
    public static class ThemeInference
    {
        public static ThemeGenerationResult Infer(GenerationResult slotResult, PcgInputData data, AlgorithmConfig config)
        {
            if (slotResult == null || data == null || data.themes == null || data.themes.themes == null)
            {
                return BuildEmptyFallback("Missing generation result or theme definitions.");
            }

            int axisCount = GetAxisCount(data);
            if (axisCount <= 0)
            {
                return BuildEmptyFallback("Theme axis definition is empty.");
            }

            float[] softSemanticVector = new float[axisCount];
            AddSoftSlotVector(slotResult.weapon, data.weaponModules, config.stage2WeaponSemanticWeight, softSemanticVector);
            AddSoftSlotVector(slotResult.boss, data.bossModules, config.stage2BossSemanticWeight, softSemanticVector);
            AddSoftSlotVector(slotResult.region, data.regionModules, config.stage2RegionSemanticWeight, softSemanticVector);

            return InferFromSemanticVector(softSemanticVector, data, config, softSemanticVector, false, 0f);
        }

        /// <summary>
        /// Resolves a theme from an already calculated semantic vector. DynamicTrajectoryProcessor uses this
        /// after temporal smoothing, while the visible slot textures retain their independently sampled indices.
        /// </summary>
        public static ThemeGenerationResult InferFromSemanticVector(
            float[] semanticVector,
            PcgInputData data,
            AlgorithmConfig config,
            float[] rawSemanticVector,
            bool temporalSmoothingApplied,
            float temporalSmoothingAlpha)
        {
            if (data == null || data.themes == null || data.themes.themes == null)
            {
                return BuildEmptyFallback("Theme definitions are missing.");
            }

            int axisCount = GetAxisCount(data);
            if (axisCount <= 0 || semanticVector == null)
            {
                return BuildEmptyFallback("Theme axis definition or semantic vector is empty.");
            }

            float[] combinedVector = CopyToAxisCount(semanticVector, axisCount);
            float[] rawVector = rawSemanticVector != null
                ? CopyToAxisCount(rawSemanticVector, axisCount)
                : CopyToAxisCount(combinedVector, axisCount);

            ThemeDefinition bestTheme = null;
            float bestScore = 0f;
            float secondScore = 0f;

            for (int i = 0; i < data.themes.themes.Length; i++)
            {
                ThemeDefinition theme = data.themes.themes[i];
                if (theme == null || theme.isFallback)
                {
                    continue;
                }

                float score = ComputePrototypeScore(combinedVector, theme.prototypeVector);
                if (bestTheme == null || score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                    bestTheme = theme;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                }
            }

            if (bestTheme == null)
            {
                return BuildFallbackFromDefinitions(
                    data.themes, combinedVector, rawVector, 0f, 0f,
                    temporalSmoothingApplied, temporalSmoothingAlpha,
                    "No non-fallback theme definition exists.");
            }

            float margin = bestScore - secondScore;
            if (bestScore < config.themeConfidenceThreshold)
            {
                return BuildFallbackFromDefinitions(
                    data.themes, combinedVector, rawVector, bestScore, margin,
                    temporalSmoothingApplied, temporalSmoothingAlpha,
                    "Theme confidence is below threshold.");
            }

            if (margin < config.themeMarginThreshold)
            {
                return BuildFallbackFromDefinitions(
                    data.themes, combinedVector, rawVector, bestScore, margin,
                    temporalSmoothingApplied, temporalSmoothingAlpha,
                    "Theme margin is below threshold.");
            }

            return BuildResult(
                bestTheme, false, string.Empty, combinedVector, rawVector, bestScore, margin,
                temporalSmoothingApplied, temporalSmoothingAlpha);
        }

        private static int GetAxisCount(PcgInputData data)
        {
            if (data.themes.themeAxes != null && data.themes.themeAxes.Length > 0)
            {
                return data.themes.themeAxes.Length;
            }

            if (data.weaponModules != null && data.weaponModules.themeAxes != null)
            {
                return data.weaponModules.themeAxes.Length;
            }

            return 0;
        }

        private static void AddSoftSlotVector(SlotGenerationResult slot, ModuleDefinitionSet modules, float weight, float[] combinedVector)
        {
            if (slot == null || modules == null || modules.candidates == null || slot.candidates == null)
            {
                return;
            }

            for (int i = 0; i < slot.candidates.Length; i++)
            {
                CandidateEvaluation evaluation = slot.candidates[i];
                if (evaluation == null || !evaluation.eligible || evaluation.probability <= 0f)
                {
                    continue;
                }

                ModuleCandidateDefinition module = FindModule(modules, evaluation.moduleId);
                if (module == null || module.semanticVector == null)
                {
                    continue;
                }

                float semanticWeight = evaluation.semanticContributionWeight > 0f
                    ? Mathf.Clamp01(evaluation.semanticContributionWeight)
                    : 1f;
                float candidateWeight = weight * evaluation.probability * semanticWeight;
                int length = Mathf.Min(combinedVector.Length, module.semanticVector.Length);
                for (int axis = 0; axis < length; axis++)
                {
                    combinedVector[axis] += candidateWeight * Mathf.Clamp01(module.semanticVector[axis]);
                }
            }
        }

        private static ModuleCandidateDefinition FindModule(ModuleDefinitionSet modules, string moduleId)
        {
            for (int i = 0; i < modules.candidates.Length; i++)
            {
                ModuleCandidateDefinition candidate = modules.candidates[i];
                if (candidate != null && candidate.moduleId == moduleId)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static float ComputePrototypeScore(float[] combinedVector, float[] prototypeVector)
        {
            if (combinedVector == null || prototypeVector == null)
            {
                return 0f;
            }

            int length = Mathf.Min(combinedVector.Length, prototypeVector.Length);
            float score = 0f;
            for (int i = 0; i < length; i++)
            {
                score += combinedVector[i] * Mathf.Clamp01(prototypeVector[i]);
            }

            return score;
        }

        private static ThemeGenerationResult BuildFallbackFromDefinitions(
            ThemeDefinitionSet themes,
            float[] combinedVector,
            float[] rawVector,
            float confidence,
            float margin,
            bool temporalSmoothingApplied,
            float temporalSmoothingAlpha,
            string reason)
        {
            ThemeDefinition fallback = null;
            for (int i = 0; i < themes.themes.Length; i++)
            {
                ThemeDefinition theme = themes.themes[i];
                if (theme != null && theme.isFallback)
                {
                    fallback = theme;
                    break;
                }
            }

            if (fallback == null && themes.themes.Length > 0)
            {
                fallback = themes.themes[0];
            }

            return BuildResult(
                fallback, true, reason, combinedVector, rawVector, confidence, margin,
                temporalSmoothingApplied, temporalSmoothingAlpha);
        }

        private static ThemeGenerationResult BuildEmptyFallback(string reason)
        {
            return new ThemeGenerationResult
            {
                selectedThemeId = "Neutral",
                confidence = 0f,
                margin = 0f,
                rawSemanticVector = new float[0],
                combinedSemanticVector = new float[0],
                fallbackUsed = true,
                fallbackReason = reason
            };
        }

        private static ThemeGenerationResult BuildResult(
            ThemeDefinition theme,
            bool fallbackUsed,
            string reason,
            float[] combinedVector,
            float[] rawVector,
            float confidence,
            float margin,
            bool temporalSmoothingApplied,
            float temporalSmoothingAlpha)
        {
            ThemeGenerationResult result = new ThemeGenerationResult
            {
                selectedThemeId = theme != null ? theme.themeId : "Neutral",
                vfxEventName = theme != null ? theme.vfxEventName : string.Empty,
                confidence = confidence,
                margin = margin,
                rawSemanticVector = rawVector,
                temporalSmoothingApplied = temporalSmoothingApplied,
                temporalSmoothingAlpha = temporalSmoothingAlpha,
                combinedSemanticVector = combinedVector,
                hdrColorRgba = theme != null ? theme.hdrColorRgba : null,
                sizeRange = theme != null ? theme.sizeRange : null,
                lifetime = theme != null ? theme.lifetime : 0f,
                speedRange = theme != null ? theme.speedRange : null,
                magicCircleColorRgba = theme != null ? theme.magicCircleColorRgba : null,
                slotColorRgba = theme != null ? theme.slotColorRgba : null,
                particleVfx = theme != null ? theme.particleVfx : null,
                fallbackUsed = fallbackUsed,
                fallbackReason = reason
            };

            if (theme != null && theme.particleAtlas != null)
            {
                result.selectedAtlasId = theme.particleAtlas.atlasId;
                result.selectedTexturePath = theme.particleAtlas.texturePath;
                result.texIndexMin = GetRangeValue(theme.particleAtlas.texIndexRange, 0, 0);
                result.texIndexMax = GetRangeValue(theme.particleAtlas.texIndexRange, 1, 3);
            }

            return result;
        }

        private static float[] CopyToAxisCount(float[] source, int axisCount)
        {
            float[] copy = new float[axisCount];
            int length = Mathf.Min(axisCount, source != null ? source.Length : 0);
            for (int i = 0; i < length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static int GetRangeValue(int[] values, int index, int fallback)
        {
            return values != null && values.Length > index ? values[index] : fallback;
        }
    }
}
