using System.Collections.Generic;

namespace BeastieBuddy.Data
{
    public class BlueMageSpell
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Rank { get; set; }
        public string Description { get; set; } = string.Empty;
        public List<SpellSource> Sources { get; set; } = new();
    }

    public class SpellSource
    {
        public string Type { get; set; } = string.Empty; // "Open World", "Dungeon", "Trial"
        public string Name { get; set; } = string.Empty; // Mob or Boss name
        public string Location { get; set; } = string.Empty; // Zone or Duty name
        public float X { get; set; }
        public float Y { get; set; }
        public bool IsRecommended { get; set; } = true;
    }
}
