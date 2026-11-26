using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pia.Custom;

internal static partial class PlcFbdSource
{
    private static readonly Regex Tokens = new(
        @"\s+|//[^\r\n]*|\(\*[\s\S]*?\*\)|/\*[\s\S]*?\*/|""(?:\$[^\r\n]|[^""$\r\n])*""|'(?:\$[^\r\n]|[^'$\r\n])*'|[\p{L}\p{N}_]+|\S",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex LocalName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DataType = new(@"\A(?:[A-Za-z][A-Za-z0-9_]{0,63}|""[^""\r\n;$]{1,64}"")(?:\[[1-9][0-9]{0,2}\])?\z",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> BlockKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "NETWORK", "END_NETWORK", "BEGIN", "END_VAR", "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP",
        "VAR_CONSTANT", "FUNCTION_BLOCK", "END_FUNCTION_BLOCK", "FUNCTION", "END_FUNCTION", "DATA_BLOCK",
        "END_DATA_BLOCK", "ORGANIZATION_BLOCK", "END_ORGANIZATION_BLOCK", "END_TYPE", "INCLUDE"
    };

    internal static bool ValidLocal(string? name, string? dataType) =>
        name != null && dataType != null && LocalName.IsMatch(name) && DataType.IsMatch(dataType);

    internal static string? FormatBlockType(string? blockNamespace, string blockName)
    {
        string qualifier = string.IsNullOrEmpty(blockNamespace) ? "_" : blockNamespace!;
        return LocalName.IsMatch(blockName) && qualifier.Split('.').All(LocalName.IsMatch)
            ? qualifier + "." + blockName
            : null;
    }

    internal static string CopyBlockWithAutomaticNumber(string source, string newName)
    {
        if (!LocalName.IsMatch(newName)) throw new FormatException("The temporary function block name is invalid.");
        var tokens = CodeTokens(source);
        int block = Array.FindIndex(tokens, token => Is(token, "FUNCTION_BLOCK"));
        if (block < 0 || block + 1 >= tokens.Length || !tokens[block + 1].Value.StartsWith("\"", StringComparison.Ordinal))
            throw new FormatException("The SIMATIC SD function block header is unavailable.");
        var name = tokens[block + 1];
        source = source.Remove(name.Index, name.Length).Insert(name.Index, "\"" + newName + "\"");

        int number = Array.FindIndex(tokens, 0, block, token => Is(token, "S7_BlockNumber"));
        if (number < 0) return source;
        if (number + 3 >= block || tokens[number + 1].Value != ":" || tokens[number + 2].Value != "=" ||
            !int.TryParse(tokens[number + 3].Value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new FormatException("The SIMATIC SD function block number is unavailable.");

        int first = number;
        int last = number + 3;
        if (tokens[last + 1].Value == ";") last++;
        else if (first > 0 && tokens[first - 1].Value == ";") first--;
        else if (first > 0 && tokens[first - 1].Value == "{" && tokens[last + 1].Value == "}")
        {
            first--;
            last++;
        }
        return source.Remove(tokens[first].Index, tokens[last].Index + tokens[last].Length - tokens[first].Index);
    }

    internal static bool ValidNetworkBody(string? body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body) || body.Length > 3000 ||
            body.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            return false;
        // SIMATIC SD expects RUNG or LABEL at the start of an FBD network.
        if (!Is(Tokens.Match(body.TrimStart()), "RUNG")) return false;
        var tokens = CodeTokens(body);
        bool inRung = false;
        int parentheses = 0;
        for (int index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Value == "(") parentheses++;
            if (token.Value == ")" && --parentheses < 0) return false;
            if (Is(token, "NOT") && index + 1 < tokens.Length && tokens[index + 1].Value == "{")
                return false;
            if (Is(token, "RUNG") || Is(token, "END_RUNG"))
            {
                bool startsRung = Is(token, "RUNG");
                if (inRung == startsRung) return false;
                inRung = startsRung;
            }
            else if (BlockKeywords.Contains(token.Value) || token.Value == ";" ||
                token.Value == "'" || token.Value == "\"")
                return false;
        }
        return !inRung && parentheses == 0;
    }

    internal static bool ValidSclNetworkBody(string? body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body) || body.Length > 3000 ||
            body.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            return false;
        var tokens = CodeTokens(body);
        if (tokens.Length == 0 || tokens[tokens.Length - 1].Value != ";") return false;
        var delimiters = new Stack<string>();
        foreach (var token in tokens)
        {
            if (BlockKeywords.Contains(token.Value) || Is(token, "RUNG") || Is(token, "END_RUNG") ||
                token.Value == "\"" || token.Value == "'" || token.Value == "{" || token.Value == "}")
                return false;
            if (token.Value == "(" || token.Value == "[") delimiters.Push(token.Value);
            if (token.Value == ")" || token.Value == "]")
            {
                if (delimiters.Count == 0 || delimiters.Pop() != (token.Value == ")" ? "(" : "["))
                    return false;
            }
        }
        return delimiters.Count == 0;
    }

    internal static (string Source, string AddedNetworks, int ExistingNetworks) Append(string source,
        IReadOnlyList<string> bodies, IReadOnlyList<(string Name, string Type)> statics,
        IReadOnlyList<(string Name, string Type)> temps,
        IReadOnlyList<string?>? titleIds = null, IReadOnlyDictionary<string, string>? commentIds = null,
        string language = "FBD", IReadOnlyList<(string Name, string Token)>? staticAttributes = null)
    {
        var tokens = CodeTokens(source);
        if (tokens.Length == 0 || !Is(tokens[tokens.Length - 1], "END_FUNCTION_BLOCK"))
            throw new FormatException("The exported source is not a supported function block.");
        int declarationEnd = DeclarationEnd(tokens);
        var declarations = tokens.Take(declarationEnd).ToArray();
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < declarations.Length; i++)
            if (declarations[i].Value == ":" || declarations[i].Value == ",")
                declaredNames.Add(declarations[i - 1].Value.Trim('"'));
        foreach (var local in statics.Concat(temps))
            if (!declaredNames.Add(local.Name))
                throw new FormatException("A planned local declaration already exists.");

        const string newline = "\r\n";
        string added = string.Join(newline + newline, bodies.Select((body, index) =>
        {
            string? titleId = titleIds?[index];
            string title = titleId == null ? "" : $"; S7_NetworkTitle := \"{titleId}\"";
            return "{ S7_Language := \"" + language + "\"" + title + " }" +
                newline + "NETWORK" + newline + body.Trim() + newline + "END_NETWORK";
        }));
        var insertions = new List<(int Index, string Text)>
        {
            (tokens[tokens.Length - 1].Index, newline + added + newline + newline)
        };
        AddDeclarations(declarations, tokens[declarationEnd].Index, "VAR_TEMP", temps, commentIds, insertions);
        AddDeclarations(declarations, tokens[declarationEnd].Index, "VAR", statics, commentIds, insertions, staticAttributes);
        foreach (var insertion in insertions.OrderByDescending(item => item.Index))
            source = source.Insert(insertion.Index, insertion.Text);
        return (source, added, tokens.Skip(declarationEnd).Count(token => Is(token, "NETWORK")));
    }

    // The caller verifies S_CONV ENO settings separately from token equivalence.
    internal static bool Equivalent(string before, string after,
        PlcFbdResources? beforeResources = null, PlcFbdResources? afterResources = null) =>
        ReadbackDifference(before, after, beforeResources, afterResources) == null;

    private static void AddDeclarations(Match[] tokens, int begin, string section,
        IReadOnlyList<(string Name, string Type)> declarations, IReadOnlyDictionary<string, string>? commentIds,
        List<(int Index, string Text)> insertions,
        IReadOnlyList<(string Name, string Token)>? staticAttributes = null)
    {
        if (declarations.Count == 0) return;
        string content = string.Concat(declarations.Select(item =>
        {
            var attributes = staticAttributes?.ToList() ?? new List<(string Name, string Token)>();
            if (commentIds != null && commentIds.TryGetValue(item.Name, out string? id))
                attributes.Add(("S7_MLC", $"\"{id}\""));
            string pragma = attributes.Count == 0 ? "" : "    { " + string.Join("; ", attributes
                .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
                .Select(attribute => attribute.Name + " := " + attribute.Token)) + " }\r\n";
            return pragma + $"    {item.Name} : {item.Type};\r\n";
        }));
        var range = DeclarationSection(tokens, section);
        if (range == null)
        {
            int nextSection = section == "VAR" ? Array.FindIndex(tokens, token => Is(token, "VAR_TEMP")) : -1;
            int position = nextSection >= 0 ? tokens[nextSection].Index : begin;
            insertions.Add((position, section + "\r\n" + content + "END_VAR\r\n\r\n"));
            return;
        }
        insertions.Add((tokens[range.Value.End].Index, "\r\n" + content));
    }

    private static int DeclarationEnd(Match[] tokens)
    {
        int begin = Array.FindIndex(tokens, token => Is(token, "BEGIN"));
        if (begin >= 0) return begin;
        int network = Array.FindIndex(tokens, token => Is(token, "NETWORK"));
        int end = network < 0 ? tokens.Length - 1 : network;
        if (end > 0 && tokens[end - 1].Value == "}")
        {
            int pragma = Array.FindLastIndex(tokens, end - 1, token => token.Value == "{");
            if (pragma >= 0) return pragma;
        }
        return end;
    }

    private static (int Start, int End)? DeclarationSection(Match[] tokens, string section)
    {
        int index = Array.FindIndex(tokens, token => Is(token, section));
        while (index >= 0 && index + 1 < tokens.Length && (Is(tokens[index + 1], "CONSTANT") || Is(tokens[index + 1], "RETAIN")))
            index = Array.FindIndex(tokens, index + 1, token => Is(token, section));
        if (index < 0) return null;
        int end = Array.FindIndex(tokens, index + 1, token => Is(token, "END_VAR"));
        if (end < 0) throw new FormatException("A declaration section is incomplete.");
        return (index + 1, end);
    }

    private static Match[] CodeTokens(string source) => Tokens.Matches(source).Cast<Match>().Where(token =>
        !char.IsWhiteSpace(token.Value[0]) && !token.Value.StartsWith("//", StringComparison.Ordinal) &&
        !token.Value.StartsWith("(*", StringComparison.Ordinal) && !token.Value.StartsWith("/*", StringComparison.Ordinal)).ToArray();

    private static bool Is(Match token, string keyword) => token.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}
