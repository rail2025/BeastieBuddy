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
        public uint IconId { get; set; }
        public string Location { get; set; } = "Unknown";
        public string AutoAttackElement { get; set; } = "Unknown";
        public string Classification { get; set; } = "Unknown";
        public BeastSkill Trick { get; set; } = new();

        [JsonProperty("TemperedRelease")]
        public BeastSkill TemperedRelease { get; set; } = new();

        [JsonProperty("Borrow")]
        public BeastSkill Borrow { get; set; } = new();

        public BeastSkill PartingBlow { get; set; } = new();
        public CrucibleAttributes? Rank25Attributes { get; set; }
    }

    public class CrucibleAttributes
    {
        public int Strength { get; set; }
        public int Intelligence { get; set; }
        public int Constitution { get; set; }
        public int PhysicalResistance { get; set; }
        public int MagicalResistance { get; set; }
        public int Satiety { get; set; }
    }

public class BestiaryResponse
    {
        public int Version { get; set; } = 1;
        public DateTime Updated { get; set; } = DateTime.MinValue;
        public Dictionary<int, BeastData> Beasts { get; set; } = new();
    }
}
