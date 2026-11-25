using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Pia.Custom;

internal sealed class PlcFbdResources
{
    private static readonly Regex Header = new(@"(?m)^MultiLingualTexts\s*:", RegexOptions.CultureInvariant);
    private readonly string prefix;
    private readonly YamlStream document;
    private readonly YamlSequenceNode items;
    private readonly Dictionary<string, Dictionary<string, string>> texts;
    private int nextId = 1;

    internal PlcFbdResources(string? content)
    {
        content ??= "MultiLingualTexts: []\r\n";
        var header = Header.Match(content);
        if (!header.Success) throw new InvalidDataException("The SIMATIC SD resource document has no text collection.");
        prefix = content.Substring(0, header.Index);
        document = new YamlStream();
        try
        {
            using var reader = new StringReader(content.Substring(header.Index));
            document.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException("Cannot read SIMATIC SD text resources.", ex);
        }
        if (document.Documents.Count != 1 || !(document.Documents[0].RootNode is YamlMappingNode root) ||
            !root.Children.TryGetValue(new YamlScalarNode("MultiLingualTexts"), out var collection) ||
            !(collection is YamlSequenceNode sequence))
            throw new InvalidDataException("The SIMATIC SD text collection is not a list.");

        items = sequence;
        texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var item in items.Children)
        {
            if (!(item is YamlMappingNode entry) ||
                !entry.Children.TryGetValue(new YamlScalarNode("id"), out var idNode) ||
                !(idNode is YamlScalarNode id) || string.IsNullOrEmpty(id.Value))
                throw new InvalidDataException("A SIMATIC SD text entry has no ID.");

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in entry.Children)
            {
                if (!(pair.Key is YamlScalarNode key) || !(pair.Value is YamlScalarNode value) || key.Value == null)
                    throw new InvalidDataException("A SIMATIC SD text entry contains an unsupported value.");
                if (key.Value != "id") values.Add(key.Value, value.Value ?? "");
            }
            if (texts.ContainsKey(id.Value!))
                throw new InvalidDataException("The SIMATIC SD resource document contains duplicate text IDs.");
            texts.Add(id.Value!, values);
        }
    }

    internal string Add(string text, IEnumerable<string> cultures)
    {
        string id;
        do { id = "MLC_Pia" + nextId++; } while (texts.ContainsKey(id));
        var entry = new YamlMappingNode { { "id", id } };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string culture in cultures.Distinct(StringComparer.Ordinal))
        {
            entry.Add(new YamlScalarNode(culture), new YamlScalarNode(text) { Style = ScalarStyle.SingleQuoted });
            values.Add(culture, text);
        }
        items.Add(entry);
        texts.Add(id, values);
        return id;
    }

    internal string Write()
    {
        using var writer = new StringWriter { NewLine = "\r\n" };
        document.Save(writer, assignAnchors: false);
        return prefix + writer.ToString();
    }

    internal string? TokenFor(string id)
    {
        if (!texts.TryGetValue(id, out var values)) return null;
        return string.Concat(values.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key.Length + ":" + item.Key + item.Value.Length + ":" + item.Value));
    }

    internal bool HasSameTexts(PlcFbdResources other) =>
        texts.Keys.Select(TokenFor).OrderBy(text => text, StringComparer.Ordinal)
            .SequenceEqual(other.texts.Keys.Select(other.TokenFor).OrderBy(text => text, StringComparer.Ordinal));
}
