using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Pia.Custom;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcSclBlockChange
{
    [JsonRequired, JsonPropertyName("blockPath"), Description("Exact program-block path of the existing FBD function block.")]
    public string BlockPath { get; set; } = "";

    [JsonRequired, JsonPropertyName("network"), Description("Source statements for one SCL network, without NETWORK or block declarations.")]
    public string Network { get; set; } = "";

    [JsonPropertyName("networkTitle"), Description("Optional title for the planned SCL network.")]
    public string? NetworkTitle { get; set; }

    [JsonPropertyName("statics"), Description("Optional new static declarations.")]
    public PlcFbdLocal[] Statics { get; set; } = Array.Empty<PlcFbdLocal>();

    [JsonPropertyName("temps"), Description("Optional new temporary declarations.")]
    public PlcFbdLocal[] Temps { get; set; } = Array.Empty<PlcFbdLocal>();

    internal PlcFbdBlockChange ToAppendChange() => new()
    {
        BlockPath = BlockPath,
        Networks = new[] { Network },
        NetworkTitles = NetworkTitle == null ? null : new[] { NetworkTitle },
        Statics = Statics,
        Temps = Temps
    };
}
