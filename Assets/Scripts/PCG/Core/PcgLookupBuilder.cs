namespace PCG.VFX
{
    public static class PcgLookupBuilder
    {
        public static PcgLookup FromProfile(PlayerProfile profile, BossDefinitionSet bossDefinitions)
        {
            PcgLookup lookup = new PcgLookup();

            if (profile.weaponUsageData != null)
            {
                for (int i = 0; i < profile.weaponUsageData.Length; i++)
                {
                    WeaponUsageRecord record = profile.weaponUsageData[i];
                    if (!string.IsNullOrEmpty(record.weaponId))
                    {
                        lookup.WeaponsById[record.weaponId] = record;
                    }
                }
            }

            if (profile.bossCombatData != null)
            {
                for (int i = 0; i < profile.bossCombatData.Length; i++)
                {
                    BossCombatRecord record = profile.bossCombatData[i];
                    if (!string.IsNullOrEmpty(record.bossId))
                    {
                        lookup.BossRecordsById[record.bossId] = record;
                    }
                }
            }

            if (profile.regionExplorationData != null)
            {
                for (int i = 0; i < profile.regionExplorationData.Length; i++)
                {
                    RegionExplorationRecord record = profile.regionExplorationData[i];
                    if (!string.IsNullOrEmpty(record.regionId))
                    {
                        lookup.RegionsById[record.regionId] = record;
                    }
                }
            }

            if (bossDefinitions != null && bossDefinitions.bosses != null)
            {
                for (int i = 0; i < bossDefinitions.bosses.Length; i++)
                {
                    BossDefinition definition = bossDefinitions.bosses[i];
                    if (!string.IsNullOrEmpty(definition.bossId))
                    {
                        lookup.BossDefinitionsById[definition.bossId] = definition;
                    }
                }
            }

            return lookup;
        }
    }
}
