using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Siemens;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

internal sealed class PlcFbdPlanReview
{
    private static readonly HashSet<string> BuiltInOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "O", "X", "Coil", "I_Coil", "S_Coil", "R_Coil", "S_CONV", "INT_TO_WSTRING"
    };
    private static readonly HashSet<string> TimerOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "TON", "TOF", "TP", "TONR"
    };
    private readonly string softwarePath;
    private readonly Dictionary<string, ResolvedCall?> cachedCalls = new(StringComparer.OrdinalIgnoreCase);

    internal PlcFbdPlanReview(string softwarePath)
    {
        this.softwarePath = softwarePath;
    }

    internal Review Read(PlcFbdChange change, PlcBlock target, int previousNetworks)
    {
        var calls = change.Networks.SelectMany((network, index) =>
            PlcFbdSource.ReadInputCalls(network, previousNetworks + index + 1)).ToArray();
        var localTypes = calls.Any(call => call.IsLocal && call.Symbols.Length == 1)
            ? ReadLocalTypes(target, change)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targets = calls.Select(call => (call, builtin: IsBuiltIn(call), symbol: ResolveSymbols(call, localTypes))).ToArray();
        string? unit = SoftwareUnit(change.BlockPath);
        var newNames = targets.Where(item => !item.builtin && item.symbol != null && !cachedCalls.ContainsKey(CacheKey(unit, item.symbol)))
            .Select(item => item.symbol!).ToArray();
        if (newNames.Length > 0)
            LoadInputs(unit, newNames);

        var openInputs = new List<object>();
        var unresolvedCalls = new List<object>();
        var newStatics = new HashSet<string>(change.Statics.Select(local => local.Name), StringComparer.OrdinalIgnoreCase);
        var bindings = new Dictionary<string, StaticBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in targets)
        {
            string display = (item.call.IsLocal ? "#" : "") + item.call.Name;
            var resolved = item.builtin || item.symbol == null ? null : cachedCalls[CacheKey(unit, item.symbol)];
            string[]? knownInputs = resolved?.Inputs;
            if (knownInputs == null && !item.builtin)
                unresolvedCalls.Add(new { network = item.call.Network, block = display });
            if (resolved?.Block is FB functionBlock && item.call.IsLocal && item.call.Symbols.Length == 1 &&
                newStatics.Contains(item.call.Symbols[0]))
            {
                string? importType = PlcFbdSource.FormatBlockType(functionBlock.Namespace, functionBlock.Name);
                if (importType != null)
                    bindings[item.call.Symbols[0]] = new StaticBinding(functionBlock, importType);
            }
            foreach (var input in PlcFbdSource.OpenInputs(item.call, knownInputs))
                openInputs.Add(new { network = item.call.Network, block = display, pin = input.Pin,
                    reason = input.Reason, manualReviewRequired = true });
            if (openInputs.Count > 64 || unresolvedCalls.Count > 32)
                throw new FormatException("Too many input review items. Plan fewer networks at a time.");
        }
        return new Review(new { openInputs, complete = unresolvedCalls.Count == 0, unresolvedCalls }, bindings);
    }

    private static Dictionary<string, string> ReadLocalTypes(PlcBlock target, PlcFbdChange change)
    {
        var localTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = ReadInterface(target);
        if (sections != null)
            foreach (var declaration in PlcInterfaceReader.ReadDeclarations(sections, "Static"))
                localTypes[declaration.Path] = declaration.DataType;
        foreach (var local in change.Statics)
            localTypes[local.Name] = local.Type;
        return localTypes;
    }

    private static string[]? ResolveSymbols(PlcFbdSource.InputCall call, Dictionary<string, string> localTypes)
    {
        if (!call.IsLocal) return call.Symbols;
        if (call.Symbols.Length != 1 || !localTypes.TryGetValue(call.Symbols[0], out string? type) || type.IndexOf('[') >= 0)
            return null;
        return type.Replace("\"", "").Split('.');
    }

    private void LoadInputs(string? unit, string[][] symbols)
    {
        string names = string.Join("|", symbols.Select(path => path.Last()).Distinct(StringComparer.OrdinalIgnoreCase).Select(Regex.Escape));
        string pattern = "^(?:" + names + ")$";
        var candidates = new List<PlcBlock>();
        try
        {
            candidates.AddRange(Core.Portal.GetBlocks(softwarePath, pattern) ?? Enumerable.Empty<PlcBlock>());
            if (unit != null)
                candidates.AddRange(Core.Portal.GetBlocksForExport(softwarePath, pattern, unit, "PlanPlcFbdAppend") ?? Enumerable.Empty<PlcBlock>());
        }
        catch (Exception ex) when (ex is EngineeringException || ex is PortalException)
        {
            foreach (var symbol in symbols) cachedCalls[CacheKey(unit, symbol)] = null;
            return;
        }

        var unique = candidates.Where(block => block is FC || block is FB).ToLookup(block => block.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            string key = CacheKey(unit, symbol);
            if (cachedCalls.ContainsKey(key)) continue;
            var matches = unique[symbol.Last()].Where(block => NamespaceMatches(block, symbol)).Take(2).ToArray();
            if (matches.Length != 1)
            {
                cachedCalls.Add(key, null);
                continue;
            }
            var sections = ReadInterface(matches[0]);
            var inputs = sections == null ? null : PlcInterfaceReader.ReadDeclarations(sections, "")
                .Where(declaration => declaration.Section == "Input" || declaration.Section == "InOut")
                .Select(declaration => declaration.Path).ToArray();
            cachedCalls.Add(key, new ResolvedCall(matches[0], inputs));
        }
    }

    private static bool NamespaceMatches(PlcBlock block, string[] symbol)
    {
        if (symbol.Length == 1) return true;
        string requested = string.Join(".", symbol.Take(symbol.Length - 1));
        string actual = block.Namespace ?? "";
        return requested == "_" ? actual.Length == 0 || actual == "_" : requested.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    private static System.Xml.Linq.XElement? ReadInterface(PlcBlock block)
    {
        try
        {
            if (!block.IsConsistent || block.IsKnowHowProtected) return null;
            var exported = PlcInspectionTools.ReadExport(file => block.Export(file, ExportOptions.None), "SW.Blocks.");
            return exported == null ? null : PlcInterfaceReader.InterfaceSections(exported);
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    private static bool IsBuiltIn(PlcFbdSource.InputCall call) =>
        (!call.HasSymbolicName && BuiltInOperations.Contains(call.Name)) ||
        (call.IsLocal && call.Symbols.Length == 2 && TimerOperations.Contains(call.Symbols[1]));

    private static string? SoftwareUnit(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 2 && parts[0].Equals("Software units", StringComparison.OrdinalIgnoreCase) ? parts[1] : null;
    }

    private static string CacheKey(string? unit, string[] symbol) => (unit ?? "") + "\0" + string.Join("\0", symbol);

    internal sealed class Review(object inputReview, IReadOnlyDictionary<string, StaticBinding> bindings)
    {
        internal object InputReview { get; } = inputReview;

        internal (string Name, string Type)[] ImportStatics(IEnumerable<(string Name, string Type)> declarations) =>
            declarations.Select(local => Bindings.TryGetValue(local.Name, out var binding)
                ? (local.Name, binding.ImportType) : local).ToArray();

        internal bool HasSameBindings(Review other) => Bindings.Count == other.Bindings.Count &&
            Bindings.All(pair => other.Bindings.TryGetValue(pair.Key, out var binding) &&
                pair.Value.Block.Equals(binding.Block) && pair.Value.ImportType == binding.ImportType);

        private IReadOnlyDictionary<string, StaticBinding> Bindings { get; } = bindings;
    }

    internal sealed class StaticBinding(FB block, string importType)
    {
        internal FB Block { get; } = block;
        internal string ImportType { get; } = importType;
    }

    private sealed class ResolvedCall(PlcBlock block, string[]? inputs)
    {
        internal PlcBlock Block { get; } = block;
        internal string[]? Inputs { get; } = inputs;
    }
}
