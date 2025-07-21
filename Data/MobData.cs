using System.Numerics;

namespace BeastieBuddy.Data;

public class MobData
{
    public uint MobId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Zone { get; set; } = "Unknown Zone";
    public Vector2 Coordinates { get; set; }
    public uint MapID { get; set; }
}
