using System;

namespace PCG.VFX
{
    public class TextureSlotGenerator
    {
        public GenerationResult Generate(PlayerProfile profile, PcgInputData data, int seed)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            AlgorithmConfig config = data.config ?? new AlgorithmConfig();
            System.Random random = new System.Random(seed);

            GenerationResult result = new GenerationResult
            {
                runId = profile.identityId + "_" + seed,
                profileId = profile.identityId,
                profileIntent = profile.profileIntent,
                seed = seed,
                weapon = WeaponSlotScorer.Generate(profile, data.weaponModules, config, random),
                boss = BossSlotScorer.Generate(profile, data.bossModules, data.bossDefinitions, config, random),
                region = RegionSlotScorer.Generate(profile, data.regionModules, config, random)
            };

            result.theme = ThemeInference.Infer(result, data, config);
            return result;
        }
    }
}
