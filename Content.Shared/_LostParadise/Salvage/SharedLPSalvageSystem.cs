using System.Linq;
using Content.Shared.Dataset;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared._LostParadise.Salvage.Expeditions;
using Content.Shared._LostParadise.Salvage.Expeditions.Modifiers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._LostParadise.Salvage;

public abstract partial class SharedLPSalvageSystem : EntitySystem
{
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    #region Descriptions

    public string GetMissionDescription(LPSalvageMission mission)
    {
        // Hardcoded in coooooz it's dynamic based on difficulty and I'm lazy.
        switch (mission.Mission)
        {
            case LPSalvageMissionType.Mining:
                // Taxation: , ("tax", $"{GetMiningTax(mission.Difficulty) * 100f:0}")
                return Loc.GetString("salvage-expedition-desc-mining");
            case LPSalvageMissionType.Destruction:
                var proto = _proto.Index<LPSalvageFactionPrototype>(mission.Faction).Configs["DefenseStructure"];

                return Loc.GetString("salvage-expedition-desc-structure",
                    ("count", GetStructureCount(mission.Difficulty)),
                    ("structure", _loc.GetEntityData(proto).Name));
            case LPSalvageMissionType.Elimination:
                return Loc.GetString("salvage-expedition-desc-elimination");
            default:
                throw new NotImplementedException();
        }
    }

    public float GetMiningTax(DifficultyRating baseRating)
    {
        return 0.6f + (int) baseRating * 0.05f;
    }

    /// <summary>
    /// Gets the amount of structures to destroy.
    /// </summary>
    public int GetStructureCount(DifficultyRating baseRating)
    {
        return 1 + (int) baseRating * 2;
    }

    #endregion

    public int GetDifficulty(DifficultyRating rating)
    {
        switch (rating)
        {
            case DifficultyRating.Minimal:
                return 4;
            case DifficultyRating.Minor:
                return 6;
            case DifficultyRating.Moderate:
                return 8;
            case DifficultyRating.Hazardous:
                return 10;
            case DifficultyRating.Extreme:
                return 12;
            default:
                throw new ArgumentOutOfRangeException(nameof(rating), rating, null);
        }
    }

    /// <summary>
    /// How many groups of mobs to spawn for a mission.
    /// </summary>
    public float GetSpawnCount(DifficultyRating difficulty)
    {
        return (int) difficulty * 2;
    }

    public static string GetFTLName(DatasetPrototype dataset, int seed)
    {
        var random = new System.Random(seed);
        return $"{dataset.Values[random.Next(dataset.Values.Count)]}-{random.Next(10, 100)}-{(char) (65 + random.Next(26))}";
    }

    public LPSalvageMission GetMission(LPSalvageMissionType config, DifficultyRating difficulty, int seed)
    {
        // This is on shared to ensure the client display for missions and what the server generates are consistent
        var rating = (float) GetDifficulty(difficulty);
        // Don't want easy missions to have any negative modifiers but also want
        // easy to be a 1 for difficulty.
        rating -= 1f;
        var rand = new System.Random(seed);

        // Run budget in order of priority
        // - Biome
        // - Lighting
        // - Atmos
        var faction = GetMod<LPSalvageFactionPrototype>(rand, ref rating);
        var biome = GetMod<LPSalvageBiomeMod>(rand, ref rating);
        var air = GetBiomeMod<LPSalvageAirMod>(biome.ID, rand, ref rating);
        var dungeon = GetBiomeMod<LPSalvageDungeonModPrototype>(biome.ID, rand, ref rating);

        var mods = new List<string>();

        if (air.Description != string.Empty)
        {
            mods.Add(Loc.GetString(air.Description));
        }

        // only show the description if there is an atmosphere since wont matter otherwise
        var temp = GetBiomeMod<LPSalvageTemperatureMod>(biome.ID, rand, ref rating);
        if (temp.Description != string.Empty && !air.Space)
        {
            mods.Add(Loc.GetString(temp.Description));
        }

        var light = GetBiomeMod<LPSalvageLightMod>(biome.ID, rand, ref rating);
        if (light.Description != string.Empty)
        {
            mods.Add(Loc.GetString(light.Description));
        }

        var time = GetMod<LPSalvageTimeMod>(rand, ref rating);
        // Round the duration to nearest 15 seconds.
        var exactDuration = MathHelper.Lerp(time.MinDuration, time.MaxDuration, rand.NextFloat());
        exactDuration = MathF.Round(exactDuration / 15f) * 15f;
        var duration = TimeSpan.FromSeconds(exactDuration);

        if (!time.Hidden && time.Description != string.Empty)
        {
            mods.Add(Loc.GetString(time.Description));
        }

        var rewards = GetRewards(difficulty, rand);
        return new LPSalvageMission(seed, difficulty, dungeon.ID, faction.ID, config, biome.ID, air.ID, temp.Temperature, light.Color, duration, rewards, mods);
    }

    public T GetBiomeMod<T>(string biome, System.Random rand, ref float rating) where T : class, IPrototype, ILPBiomeSpecificMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating || (mod.Biomes != null && !mod.Biomes.Contains(biome)))
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    public T GetMod<T>(System.Random rand, ref float rating) where T : class, IPrototype, ILPSalvageMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating)
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    private List<string> GetRewards(DifficultyRating difficulty, System.Random rand)
    {
        var rewards = new List<string>(3);
        var ids = RewardsForDifficulty(difficulty);
        foreach (var id in ids)
        {
            // pick a random reward to give
            var weights = _proto.Index<WeightedRandomEntityPrototype>(id);
            rewards.Add(weights.Pick(rand));
        }

        return rewards;
    }

    /// <summary>
    /// Get a list of WeightedRandomEntityPrototype IDs with the rewards for a certain difficulty.
    /// Frontier: added uncommon and legendary reward tiers, limited amount of rewards to 1 per difficulty rating
    /// </summary>
    private string[] RewardsForDifficulty(DifficultyRating rating)
    {
        var t1 = "ExpeditionRewardT1"; // Frontier - Update tiers
        var t2 = "ExpeditionRewardT2"; // Frontier - Update tiers
        var t3 = "ExpeditionRewardT3"; // Frontier - Update tiers
        var t4 = "ExpeditionRewardT4"; // Frontier - Update tiers
        var t5 = "ExpeditionRewardT5"; // Frontier - Update tiers
        switch (rating)
        {
            case DifficultyRating.Minimal:
                return new string[] { t1 }; // Frontier - Update tiers
            case DifficultyRating.Minor:
                return new string[] { t2 }; // Frontier - Update tiers
            case DifficultyRating.Moderate:
                return new string[] { t3 }; // Frontier - Update tiers
            case DifficultyRating.Hazardous:
                return new string[] { t4 }; // Frontier - Update tiers
            case DifficultyRating.Extreme:
                return new string[] { t5 }; // Frontier - Update tiers
            default:
                throw new NotImplementedException();
        }
    }
}

[Serializable, NetSerializable]
public enum LPSalvageMissionType : byte
{
    /// <summary>
    /// No dungeon, just ore loot and random mob spawns.
    /// </summary>
    Mining,

    /// <summary>
    /// Destroy the specified structures in a dungeon.
    /// </summary>
    Destruction,

    /// <summary>
    /// Kill a large creature in a dungeon.
    /// </summary>
    Elimination,
}

[Serializable, NetSerializable]
public enum DifficultyRating : byte
{
    Minimal,
    Minor,
    Moderate,
    Hazardous,
    Extreme,
}