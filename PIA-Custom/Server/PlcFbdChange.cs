using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Pia.Custom;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcFbdBlockChange
{
    [JsonRequired, JsonPropertyName("blockPath"), Description("Exact program-block path.")]
    public string BlockPath { get; set; } = "";

    [JsonRequired, JsonPropertyName("networks"), Description("One to eight FBD network bodies, each containing complete RUNG sections.")]
    public string[] Networks { get; set; } = Array.Empty<string>();

    [JsonPropertyName("networkTitles"), Description("Optional titles in network order; if supplied, provide one per network.")]
    public string[]? NetworkTitles { get; set; }

    [JsonPropertyName("statics"), Description("Optional new static declarations.")]
    public PlcFbdLocal[] Statics { get; set; } = Array.Empty<PlcFbdLocal>();

    [JsonPropertyName("temps"), Description("Optional new temporary declarations.")]
    public PlcFbdLocal[] Temps { get; set; } = Array.Empty<PlcFbdLocal>();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcFbdLocal
{
    [JsonRequired, JsonPropertyName("name"), Description("New ASCII local identifier.")]
    public string Name { get; set; } = "";

    [JsonRequired, JsonPropertyName("dataType"), Description("STEP 7 type, optionally with a string length.")]
    public string DataType { get; set; } = "";

    [JsonPropertyName("comment"), Description("Optional comment for the new interface declaration.")]
    public string? Comment { get; set; }
}

internal sealed class PlcFbdChange
{
    internal string BlockPath { get; }
    internal string[] Networks { get; }
    internal string[]? NetworkTitles { get; }
    internal (string Name, string Type)[] Statics { get; }
    internal (string Name, string Type)[] Temps { get; }
    internal IReadOnlyDictionary<string, string> Comments { get; }
    internal bool HasDocumentation => NetworkTitles != null || Comments.Count > 0;

    private PlcFbdChange(PlcFbdBlockChange input, string language)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.BlockPath) || input.BlockPath.Length > 2048 ||
            input.BlockPath.Any(char.IsControl))
            throw new FormatException("Specify an exact program-block path.");
        BlockPath = input.BlockPath.Trim();
        if (input.Networks == null || input.Networks.Length < 1 || input.Networks.Length > 8)
            throw new FormatException("Specify 1 to 8 FBD network bodies per block.");
        if (language == "FBD")
        {
            if (input.Networks.Any(network => !PlcFbdSource.ValidNetworkBody(network)))
                throw new FormatException("Each FBD network body must start with RUNG, contain complete RUNG sections, and omit enclosing NETWORK or block declarations.");
        }
        else if (input.Networks.Length != 1 || !PlcFbdSource.ValidSclNetworkBody(input.Networks[0]))
        {
            throw new FormatException("Supply one SCL network body with complete statements and no enclosing NETWORK or block declarations.");
        }
        Networks = input.Networks.ToArray();
        Statics = ReadLocals(input.Statics);
        Temps = ReadLocals(input.Temps);
        if (input.NetworkTitles != null)
        {
            if (input.NetworkTitles.Length != Networks.Length)
                throw new FormatException("Supply one title per planned network.");
            NetworkTitles = input.NetworkTitles.Select(title => ReadText(title, 120, "Network title")).ToArray();
        }
        var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in input.Statics.Concat(input.Temps))
        {
            if (declaration.Comment != null)
            {
                if (comments.ContainsKey(declaration.Name))
                    throw new FormatException("A declaration with a comment appears more than once.");
                comments.Add(declaration.Name, ReadText(declaration.Comment, 500, "Declaration comment"));
            }
        }
        Comments = comments;
    }

    internal static PlcFbdChange[] Read(PlcFbdBlockChange[] changes, string language)
    {
        if (changes == null || changes.Length < 1 || changes.Length > 8)
            throw new FormatException("Specify 1 to 8 block changes.");
        var result = changes.Select(item => new PlcFbdChange(item, language)).ToArray();
        if (result.Select(change => change.BlockPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new FormatException("Each block may appear once per plan.");
        if (result.Sum(change => change.Networks.Sum(network => network.Length)) > 12000)
            throw new FormatException("Split changes into plans of at most 12,000 network source characters.");
        if (result.Sum(change => change.Comments.Values.Sum(text => text.Length) +
            (change.NetworkTitles?.Sum(text => text.Length) ?? 0)) > 12000)
            throw new FormatException("Split changes into plans of at most 12,000 documentation characters.");
        return result;
    }

    private static (string Name, string Type)[] ReadLocals(PlcFbdLocal[] declarations)
    {
        if (declarations == null || declarations.Length > 16)
            throw new FormatException("Supply at most 16 declarations per section.");
        if (declarations.Any(local => local == null || !PlcFbdSource.ValidLocal(local.Name, local.DataType)))
            throw new FormatException("A local declaration requires an identifier and a data type.");
        return declarations.Select(local => (local.Name, local.DataType)).ToArray();
    }

    private static string ReadText(string? value, int limit, string field)
    {
        if (value == null || string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl))
            throw new FormatException(field + " must be nonempty single-line text of at most " + limit + " characters.");
        return value;
    }
}
