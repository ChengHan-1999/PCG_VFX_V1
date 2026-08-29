using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCG.VFX
{
    public static class PcgMath
    {
        public static float SafeDivide(float numerator, float denominator, float fallback = 0f)
        {
            return Mathf.Abs(denominator) <= Mathf.Epsilon ? fallback : numerator / denominator;
        }

        public static float Sigmoid(float value)
        {
            return 1f / (1f + Mathf.Exp(-value));
        }

        public static void ApplyTemperatureProbabilities(List<CandidateEvaluation> candidates, float temperature, float epsilon)
        {
            float safeTemperature = Mathf.Max(temperature, epsilon);
            float sum = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                CandidateEvaluation candidate = candidates[i];
                if (!candidate.eligible || candidate.score <= 0f)
                {
                    candidate.probability = 0f;
                    continue;
                }

                candidate.probability = Mathf.Pow(candidate.score + epsilon, 1f / safeTemperature);
                sum += candidate.probability;
            }

            if (sum <= epsilon)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    candidates[i].probability = 0f;
                }
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i].probability = candidates[i].probability / sum;
            }
        }

        public static CandidateEvaluation SampleByProbability(List<CandidateEvaluation> candidates, System.Random random)
        {
            float roll = (float)random.NextDouble();
            float cumulative = 0f;
            CandidateEvaluation lastEligible = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                CandidateEvaluation candidate = candidates[i];
                if (!candidate.eligible || candidate.probability <= 0f)
                {
                    continue;
                }

                lastEligible = candidate;
                cumulative += candidate.probability;
                if (roll <= cumulative)
                {
                    return candidate;
                }
            }

            return lastEligible;
        }

        public static CandidateEvaluation FindTopEligible(List<CandidateEvaluation> candidates)
        {
            CandidateEvaluation top = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                CandidateEvaluation candidate = candidates[i];
                if (!candidate.eligible)
                {
                    continue;
                }

                if (top == null || candidate.score > top.score)
                {
                    top = candidate;
                }
            }

            return top;
        }

        public static SlotGenerationResult BuildSlotResult(string slotType, List<CandidateEvaluation> candidates, System.Random random)
        {
            CandidateEvaluation top = FindTopEligible(candidates);
            CandidateEvaluation selected = SampleByProbability(candidates, random);

            SlotGenerationResult result = new SlotGenerationResult
            {
                slotType = slotType,
                candidates = candidates.ToArray(),
                topModuleId = top != null ? top.moduleId : string.Empty,
                selectedModuleId = selected != null ? selected.moduleId : string.Empty,
                selectedDataSourceId = selected != null ? selected.dataSourceId : string.Empty,
                selectedAtlasIndex = selected != null ? selected.atlasIndex : -1,
                selectedScore = selected != null ? selected.score : 0f,
                selectedProbability = selected != null ? selected.probability : 0f,
                fallbackUsed = selected == null,
                fallbackReason = selected == null ? "No eligible " + slotType + " candidate." : string.Empty
            };

            return result;
        }
    }
}
