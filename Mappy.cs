using System.Collections.Generic;
using Newtonsoft.Json;

public class MappyData
{
    [JsonProperty("BNpcID")]
    public uint BNpcID { get; set; }

    [JsonProperty("BNpcNameID")]
    public uint BNpcNameID { get; set; }

    [JsonProperty("MapID")]
    public uint MapID { get; set; }

    [JsonProperty("CoordinateX")]
    public float CoordinateX { get; set; }

    [JsonProperty("CoordinateY")]
    public float CoordinateY { get; set; }

    [JsonProperty("CoordinateZ")]
    public float CoordinateZ { get; set; }

    [JsonProperty("PixelX")]
    public float PixelX { get; set; }

    [JsonProperty("PixelY")]
    public float PixelY { get; set; }
}
