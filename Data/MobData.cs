// BeastieBuddy/Data/MobData.cs

using System.Numerics;

namespace BeastieBuddy.Data
{
    public class MobData
    {
        public string Name { get; set; } = string.Empty;
        public string Zone { get; set; } = "Unknown Zone";
        public float X { get; set; }
        public float Y { get; set; }
        public uint TerritoryTypeID { get; set; }
        public uint MapID { get; set; }
    }
}
