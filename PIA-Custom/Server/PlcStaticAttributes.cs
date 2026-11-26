using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pia.Custom;

internal sealed class PlcStaticAttributes(string? predecessor,
    (bool ExternalAccessible, bool ExternalWritable, bool ExternalVisible, bool Setpoint) values,
    (string Name, string Token)[] sourceAttributes)
{
    internal static readonly PlcStaticAttributes Disabled = new(null, (false, false, false, false), new[]
    {
        ("S7_Access", "\"ReadOnly := External\""),
        ("S7_Setpoint", "\"False\""),
        ("S7_Visibility", "\"Hidden := External\"")
    });

    internal string? Predecessor { get; } = predecessor;
    internal (bool ExternalAccessible, bool ExternalWritable, bool ExternalVisible, bool Setpoint) Values { get; } = values;
    internal (string Name, string Token)[] SourceAttributes { get; } = sourceAttributes;

    internal static (bool ExternalAccessible, bool ExternalWritable, bool ExternalVisible, bool Setpoint)?
        ReadValues(XElement? sections, string name)
    {
        var matches = PlcInterfaceReader.Children(sections, "Section")
            .Where(section => Same((string?)section.Attribute("Name"), "Static"))
            .SelectMany(section => PlcInterfaceReader.Children(section, "Member"))
            .Where(member => Same((string?)member.Attribute("Name"), name)).Take(2).ToArray();
        if (matches.Length != 1) return null;

        var attributes = PlcInterfaceReader.Children(PlcInterfaceReader.Child(matches[0], "AttributeList"), "BooleanAttribute").ToArray();
        bool? accessible = ReadBoolean(attributes, "ExternalAccessible", "HmiAccessible");
        bool? writable = ReadBoolean(attributes, "ExternalWritable");
        bool? visible = ReadBoolean(attributes, "ExternalVisible", "HmiVisible");
        bool? setpoint = ReadBoolean(attributes, "SetPoint");
        if (accessible.HasValue && writable.HasValue && visible.HasValue && setpoint.HasValue)
            return (accessible.Value, writable.Value, visible.Value, setpoint.Value);
        return null;
    }

    private static bool? ReadBoolean(XElement[] attributes, string name, string? alias = null)
    {
        var matches = attributes.Where(attribute =>
        {
            string? attributeName = (string?)attribute.Attribute("Name");
            return Same(attributeName, name) || (alias != null && Same(attributeName, alias));
        }).Take(2).ToArray();
        return matches.Length == 1 && bool.TryParse(matches[0].Value, out bool value) ? value : null;
    }

    private static bool Same(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal static partial class PlcFbdSource
{
    private static readonly string[] StaticAttributeNames = { "S7_Access", "S7_Setpoint", "S7_Visibility" };
    private static readonly Regex ExternalSelector = new(@"\A""[A-Za-z][A-Za-z0-9_]*\s*:=\s*External""\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool IsStaticAttributeValue(string name, string value) =>
        name.Equals("S7_Setpoint", StringComparison.OrdinalIgnoreCase)
            ? value.Length > 2 && value[0] == '"' && value[value.Length - 1] == '"' &&
                bool.TryParse(value.Substring(1, value.Length - 2), out _)
            : ExternalSelector.IsMatch(value);

    internal static (string Name, (string Name, string Token)[] Attributes)? StaticPredecessor(string source)
    {
        var tokens = CodeTokens(source);
        if (tokens.Length == 0) throw new FormatException("The exported source is empty.");
        var declarations = tokens.Take(DeclarationEnd(tokens)).ToArray();
        var range = DeclarationSection(declarations, "VAR");
        if (range.HasValue && range.Value.Start < range.Value.End)
            return LastStaticDeclaration(declarations, range.Value.Start, range.Value.End);

        int insertBefore = range?.Start ?? Array.FindIndex(declarations, token => Is(token, "VAR_TEMP"));
        if (insertBefore < 0) insertBefore = declarations.Length;
        for (int index = insertBefore - 1; index >= 0; index--)
        {
            if (!Is(declarations[index], "VAR") || index + 1 >= insertBefore || !Is(declarations[index + 1], "RETAIN")) continue;
            int end = Array.FindIndex(declarations, index + 2, token => Is(token, "END_VAR"));
            if (end < 0 || end >= insertBefore) throw new FormatException("A retained static declaration section is incomplete.");
            if (index + 2 < end) return LastStaticDeclaration(declarations, index + 2, end);
        }
        return null;
    }

    private static (string Name, (string Name, string Token)[] Attributes) LastStaticDeclaration(Match[] declarations, int start, int end)
    {
        var entry = StaticDeclarations(declarations, start, end).Last();
        string name = declarations[entry.Colon - 1].Value.Trim('"');
        return (name, StaticSourceAttributes(declarations, entry.Start, entry.Colon));
    }

    private static IEnumerable<(int Start, int Colon)> StaticDeclarations(Match[] declarations, int start, int end)
    {
        int colon = -1;
        int pragmaDepth = 0;
        int structDepth = 0;
        for (int index = start; index < end; index++)
        {
            var token = declarations[index];
            if (token.Value == "{") pragmaDepth++;
            if (token.Value == "}") pragmaDepth--;
            if (pragmaDepth != 0) continue;
            if (Is(token, "STRUCT")) structDepth++;
            if (Is(token, "END_STRUCT")) structDepth--;
            if (structDepth != 0) continue;
            if (colon < 0 && token.Value == ":" && declarations[index + 1].Value != "=") colon = index;
            if (token.Value != ";") continue;
            if (colon <= start) throw new FormatException("Cannot identify a static declaration at the append position.");
            yield return (start, colon);
            start = index + 1;
            colon = -1;
        }
        if (start != end || pragmaDepth != 0 || structDepth != 0)
            throw new FormatException("The static declarations could not be read through the append position.");
    }

    private static (string Name, string Token)[] StaticSourceAttributes(Match[] tokens, int start, int end)
    {
        var attributes = new List<(string Name, string Token)>();
        for (int index = start; index < end; index++)
        {
            string? name = Array.Find(StaticAttributeNames, candidate => Is(tokens[index], candidate));
            if (name == null) continue;
            if (index + 3 >= end || tokens[index + 1].Value != ":" || tokens[index + 2].Value != "=" ||
                !IsStaticAttributeValue(name, tokens[index + 3].Value) ||
                attributes.Any(attribute => attribute.Name == name))
                throw new FormatException("Cannot read a static variable attribute from the exported declaration.");
            attributes.Add((name, tokens[index + 3].Value));
            index += 3;
        }
        return attributes.ToArray();
    }

    internal static Dictionary<string, (string Name, string Token)[]> ReadStaticAttributeSyntax(string source, IEnumerable<string> names)
    {
        var selected = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, (string Name, string Token)[]>(StringComparer.OrdinalIgnoreCase);
        var tokens = CodeTokens(source);
        var declarations = tokens.Take(DeclarationEnd(tokens)).ToArray();
        var range = DeclarationSection(declarations, "VAR");
        if (range == null) return result;

        foreach (var entry in StaticDeclarations(declarations, range.Value.Start, range.Value.End))
        {
            string name = declarations[entry.Colon - 1].Value.Trim('"');
            if (selected.Contains(name)) result.Add(name, StaticSourceAttributes(declarations, entry.Start, entry.Colon));
        }
        return result;
    }

    // Called only after SimaticML confirms all four values for these new declarations.
    internal static string ExcludeVerifiedStaticAttributes(string source, IEnumerable<string> verifiedNames)
    {
        var names = new HashSet<string>(verifiedNames, StringComparer.OrdinalIgnoreCase);
        var tokens = CodeTokens(source);
        var declarations = tokens.Take(DeclarationEnd(tokens)).ToArray();
        var range = DeclarationSection(declarations, "VAR");
        if (range == null) return source;

        var replacements = new List<(int Start, int Length, string Retained)>();
        foreach (var entry in StaticDeclarations(declarations, range.Value.Start, range.Value.End))
        {
            if (!names.Contains(declarations[entry.Colon - 1].Value.Trim('"'))) continue;
            for (int open = entry.Start; open < entry.Colon; open++)
            {
                if (declarations[open].Value != "{") continue;
                int close = Array.FindIndex(declarations, open + 1, token => token.Value == "}");
                if (close < 0 || close >= entry.Colon) throw new FormatException("A static declaration pragma is incomplete.");
                var retained = new List<string>();
                bool removed = false;
                int first = open + 1;
                for (int index = first; index <= close; index++)
                {
                    if (index != close && declarations[index].Value != ";") continue;
                    if (first < index)
                    {
                        bool isVerified = index == first + 4 && StaticAttributeNames.Any(name => Is(declarations[first], name)) &&
                            declarations[first + 1].Value == ":" && declarations[first + 2].Value == "=" &&
                            IsStaticAttributeValue(declarations[first].Value, declarations[first + 3].Value);
                        if (isVerified) removed = true;
                        else retained.Add(source.Substring(declarations[first].Index,
                            declarations[index - 1].Index + declarations[index - 1].Length - declarations[first].Index));
                    }
                    first = index + 1;
                }
                if (removed)
                {
                    int start = declarations[open].Index;
                    int length = declarations[close].Index + 1 - start;
                    string original = source.Substring(start, length);
                    if (Tokens.Matches(original).Cast<Match>().Any(token => token.Value.StartsWith("//", StringComparison.Ordinal) ||
                        token.Value.StartsWith("(*", StringComparison.Ordinal) || token.Value.StartsWith("/*", StringComparison.Ordinal)))
                    {
                        open = close;
                        continue;
                    }
                    string pragma = retained.Count == 0 ? "" : "{ " + string.Join("; ", retained) + " }";
                    int missingNewlines = original.Count(character => character == '\n') -
                        pragma.Count(character => character == '\n');
                    replacements.Add((start, length, pragma + new string('\n', missingNewlines)));
                }
                open = close;
            }
        }
        foreach (var replacement in replacements.OrderByDescending(edit => edit.Start))
            source = source.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Retained);
        return source;
    }
}
