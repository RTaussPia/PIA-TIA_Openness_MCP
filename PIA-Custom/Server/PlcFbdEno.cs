using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pia.Custom;

internal static partial class PlcFbdSource
{
    internal static string PreserveConverterEno(string source, IReadOnlyList<bool[]> enabledByNetwork)
    {
        var tokens = CodeTokens(source);
        var networks = NetworkRanges(tokens).ToArray();
        if (networks.Length != enabledByNetwork.Count)
            throw new FormatException("SIMATIC SD and SimaticML report different network counts.");

        var insertions = new List<(int Index, string Text)>();
        for (int network = 0; network < networks.Length; network++)
        {
            var range = networks[network];
            var enabled = enabledByNetwork[network];
            if (!FbdNetwork(tokens, range.Start) && enabled.Length == 0) continue;
            var converters = Converters(tokens, range.Start + 1, range.End).ToArray();
            if (converters.Length != enabled.Length)
                throw new FormatException("SIMATIC SD and SimaticML report different S_CONV counts.");

            for (int index = 0; index < converters.Length; index++)
            {
                var converter = converters[index];
                // The SD exporter omits disabled ENO; the importer enables it unless told otherwise.
                if ((converter.Enabled ?? false) != enabled[index])
                    throw new FormatException("SIMATIC SD and SimaticML disagree on an existing S_CONV ENO setting.");
                if (converter.Enabled.HasValue) continue;
                if (converter.PragmaEnd >= 0)
                    insertions.Add((tokens[converter.PragmaEnd].Index, "; S7_GenerateENO := \"FALSE\" "));
                else
                    insertions.Add((tokens[converter.Instruction].Index, "{ S7_GenerateENO := \"FALSE\" } "));
            }
        }

        foreach (var insertion in insertions.OrderByDescending(item => item.Index))
            source = source.Insert(insertion.Index, insertion.Text);
        return source;
    }

    internal static bool[] ExpectedNewConverterEno(string body)
    {
        var tokens = CodeTokens(body);
        return Converters(tokens, 0, tokens.Length).Select(converter => converter.Enabled ?? true).ToArray();
    }

    internal static bool[][] ReadExportedConverterEno(string source)
    {
        var tokens = CodeTokens(source);
        return NetworkRanges(tokens).Select(range => FbdNetwork(tokens, range.Start)
            ? Converters(tokens, range.Start + 1, range.End).Select(converter => converter.Enabled ?? false).ToArray()
            : Array.Empty<bool>()).ToArray();
    }

    private static IEnumerable<string> ReadbackTokens(string source, PlcFbdResources? resources)
    {
        var tokens = Tokens.Matches(source).Cast<Match>().Where(token => !char.IsWhiteSpace(token.Value[0])).ToArray();
        var ignored = new HashSet<int>();
        foreach (var range in NetworkRanges(tokens).Where(range => FbdNetwork(tokens, range.Start)))
        {
            foreach (var converter in Converters(tokens, range.Start + 1, range.End))
            {
                if (converter.EnoAttribute < 0) continue;
                int first = converter.EnoAttribute;
                int last = first + 3;
                if (first == converter.PragmaStart + 1 && last == converter.PragmaEnd - 1)
                {
                    first--;
                    last++;
                }
                else if (tokens[first - 1].Value == ";")
                {
                    first--;
                }
                else if (tokens[last + 1].Value == ";")
                {
                    last++;
                }
                for (int index = first; index <= last; index++) ignored.Add(index);
            }
        }

        // Re-export can quote the exact same array-DB access selector.
        for (int index = 0; index < tokens.Length; index++)
        {
            if (ignored.Contains(index)) continue;
            var value = tokens[index].Value;
            if (resources != null && index >= 3 && value.StartsWith("\"MLC_", StringComparison.Ordinal) &&
                tokens[index - 2].Value == ":" && tokens[index - 1].Value == "=" &&
                IsResourceAttribute(tokens[index - 3].Value))
            {
                string? resource = resources.TokenFor(value.Trim('"'));
                if (resource == null) throw new FormatException("A SIMATIC SD text reference could not be resolved.");
                yield return "\0" + resource;
                continue;
            }
            yield return index > 0 && tokens[index - 1].Value == "." && value == "\"THIS\"" ? "THIS" : value;
        }
    }

    private static bool IsResourceAttribute(string name) => name == "S7_MLC" || name == "S7_NetworkTitle" ||
        name == "S7_NetworkComment" || name == "S7_BlockTitle" || name == "S7_BlockComment";

    private static IEnumerable<(int Start, int End)> NetworkRanges(Match[] tokens)
    {
        int start = -1;
        for (int index = 0; index < tokens.Length; index++)
        {
            if (Is(tokens[index], "NETWORK")) start = index;
            else if (start >= 0 && Is(tokens[index], "END_NETWORK"))
            {
                yield return (start, index);
                start = -1;
            }
        }
    }

    private static bool FbdNetwork(Match[] tokens, int network)
    {
        if (network < 5 || tokens[network - 1].Value != "}") return false;
        int pragma = Array.FindLastIndex(tokens, network - 1, token => token.Value == "{");
        return pragma >= 0 && Enumerable.Range(pragma + 1, network - pragma - 4).Any(index =>
            Is(tokens[index], "S7_Language") && Is(tokens[index + 3], "\"FBD\""));
    }

    private static IEnumerable<(int Instruction, int PragmaStart, int PragmaEnd, int EnoAttribute, bool? Enabled)>
        Converters(Match[] tokens, int start, int end)
    {
        for (int index = start; index + 1 < end; index++)
        {
            if (!Is(tokens[index], "S_CONV") || tokens[index + 1].Value != "(") continue;
            int pragmaEnd = index > start && tokens[index - 1].Value == "}" ? index - 1 : -1;
            int pragmaStart = pragmaEnd >= 0 ? Array.FindLastIndex(tokens, pragmaEnd, token => token.Value == "{") : -1;
            int attribute = -1;
            bool? enabled = null;
            for (int cursor = pragmaStart + 1; pragmaStart >= start && cursor + 3 < pragmaEnd; cursor++)
            {
                if (!Is(tokens[cursor], "S7_GenerateENO")) continue;
                attribute = cursor;
                enabled = Is(tokens[cursor + 3], "\"TRUE\"");
                break;
            }
            yield return (index, pragmaStart, pragmaEnd, attribute, enabled);
        }
    }
}
