using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace BeastieBuddy.Data
{
    public class BeastSkill
    {
        public string Name { get; set; } = "Unknown";
        public string Type { get; set; } = "Unknown";
        public string Element { get; set; } = "Unknown";
        public string Effect { get; set; } = "Unknown";
    }

    public class BeastData
    {
        public string Name { get; set; } = "Unknown";
        public string Location { get; set; } = "Unknown";
        public string AutoAttackElement { get; set; } = "Unknown";
        public BeastSkill Trick { get; set; } = new();

        [JsonProperty("TemperedRelease")]
        public BeastSkill TemperedRelease { get; set; } = new();

        [JsonProperty("Nature's Gift")]
        public BeastSkill NaturesGift { get; set; } = new();

        public BeastSkill Finisher { get; set; } = new();
    }

    public class BestiaryResponse
    {
        public int Version { get; set; } = 1;
        public DateTime Updated { get; set; } = DateTime.MinValue;
        public Dictionary<int, BeastData> Beasts { get; set; } = new();
    }
}
