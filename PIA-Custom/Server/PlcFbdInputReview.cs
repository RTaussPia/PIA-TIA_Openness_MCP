using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pia.Custom;

internal static partial class PlcFbdSource
{
    private static readonly HashSet<string> SclNonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "ELSIF", "WHILE", "UNTIL", "CASE", "FOR", "RETURN", "AND", "OR", "XOR", "NOT", "MOD"
    };

    internal sealed class CallInput
    {
        internal string Pin { get; }
        internal string? Reason { get; }

        internal CallInput(string pin, string? reason)
        {
            Pin = pin;
            Reason = reason;
        }
    }

    internal sealed class InputCall
    {
        internal int Network { get; }
        internal string Name { get; }
        internal bool IsLocal { get; }
        internal bool HasSymbolicName { get; }
        internal string[] Symbols { get; }
        internal CallInput[] Inputs { get; }

        internal InputCall(int network, string name, bool isLocal, bool hasSymbolicName, string[] symbols, CallInput[] inputs)
        {
            Network = network;
            Name = name;
            IsLocal = isLocal;
            HasSymbolicName = hasSymbolicName;
            Symbols = symbols;
            Inputs = inputs;
        }
    }

    internal static IEnumerable<InputCall> ReadInputCalls(string body, int network)
    {
        var tokens = CodeTokens(body);
        for (int open = 1; open < tokens.Length; open++)
        {
            if (tokens[open].Value != "(" || !CallIdentifier(tokens[open - 1].Value)) continue;
            int start = open - 1;
            while (start >= 2 && tokens[start - 1].Value == "." && CallIdentifier(tokens[start - 2].Value))
                start -= 2;
            bool local = start > 0 && tokens[start - 1].Value == "#";
            bool symbolic = local || start < open - 1 || tokens[open - 1].Value.StartsWith("\"", StringComparison.Ordinal);
            string name = string.Concat(tokens.Skip(start).Take(open - start).Select(token => token.Value));
            if (!symbolic && SclNonCallKeywords.Contains(name)) continue;
            var symbols = tokens.Skip(start).Take(open - start).Where((_, index) => index % 2 == 0)
                .Select(token => token.Value.Trim('"')).ToArray();
            int end = FindClosingParenthesis(tokens, open);
            if (end < 0) yield break;
            yield return new InputCall(network, name, local, symbolic, symbols, ReadArguments(tokens, open + 1, end).ToArray());
            open = end;
        }
    }

    internal static IEnumerable<(string Pin, string Reason)> OpenInputs(InputCall call, IReadOnlyList<string>? declaredInputs)
    {
        var assigned = new HashSet<string>(call.Inputs.Select(input => input.Pin), StringComparer.OrdinalIgnoreCase);
        foreach (var input in call.Inputs)
            if (input.Reason != null)
                yield return (input.Pin, input.Reason);
        if (declaredInputs == null) yield break;
        foreach (string pin in declaredInputs)
            if (!assigned.Contains(pin))
                yield return (pin, "unconnected");
    }

    private static IEnumerable<CallInput> ReadArguments(Match[] tokens, int first, int end)
    {
        int parentheses = 0;
        int brackets = 0;
        int start = first;
        for (int index = first; index <= end; index++)
        {
            string token = index == end ? "," : tokens[index].Value;
            if (token == "(") parentheses++;
            if (token == ")") parentheses--;
            if (token == "[") brackets++;
            if (token == "]") brackets--;
            if (token != "," || parentheses != 0 || brackets != 0) continue;

            if (index - start >= 3 && CallIdentifier(tokens[start].Value) &&
                tokens[start + 1].Value == ":" && tokens[start + 2].Value == "=")
            {
                int value = start + 3;
                bool empty = index - value == 1 && tokens[value].Value == "''" ||
                    index - value == 3 && tokens[value + 1].Value == "#" && tokens[value + 2].Value == "''" &&
                    (Is(tokens[value], "STRING") || Is(tokens[value], "WSTRING"));
                string? reason = value == index ? "unconnected" : empty ? "emptyValue" : null;
                yield return new CallInput(tokens[start].Value.Trim('"'), reason);
            }
            start = index + 1;
        }
    }

    private static int FindClosingParenthesis(Match[] tokens, int open)
    {
        int depth = 1;
        for (int index = open + 1; index < tokens.Length; index++)
        {
            if (tokens[index].Value == "(") depth++;
            if (tokens[index].Value == ")" && --depth == 0) return index;
        }
        return -1;
    }

    private static bool CallIdentifier(string value) =>
        LocalName.IsMatch(value) || (value.Length > 2 && value[0] == '"' && value[value.Length - 1] == '"');
}
