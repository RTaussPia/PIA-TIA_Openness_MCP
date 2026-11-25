using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Pia.Custom.PlcGraphReader;
using static Pia.Custom.PlcInterfaceReader;

namespace Pia.Custom;

internal static class PlcGraphFlagChange
{
    internal static (int Number, string Name)[] ReadRange(XElement graph, int sequenceNumber, int first, int last,
        IReadOnlyList<int>? branchSteps = null)
    {
        var sequence = Children(graph, "Sequence").ElementAtOrDefault(sequenceNumber - 1) ??
            throw new FormatException("The selected sequence does not exist.");
        var steps = Elements(sequence, "Steps", "Step");
        var transitions = Elements(sequence, "Transitions", "Transition");
        var branches = Elements(sequence, "Branches", "Branch");
        var edges = Elements(sequence, "Connections", "Connection").Select(Connection).ToLookup(edge => (edge.From.Kind, edge.From.Number));
        var candidates = new Dictionary<int, string>();
        var encounteredBranches = new List<XElement>();
        var active = new HashSet<(string Kind, int? Number)>();
        var requiredFrom = new Dictionary<(string Kind, int? Number), HashSet<int>>();

        HashSet<int> Visit((string Kind, int? Number) current)
        {
            if (requiredFrom.TryGetValue(current, out var completed)) return completed;
            if (!current.Number.HasValue || !active.Add(current) || active.Count + requiredFrom.Count > 256)
                throw new FormatException("The range contains an invalid node, a cycle, or more than 256 nodes.");

            string? branchType = null;
            int? branchWidth = null;
            if (current.Kind == "step")
            {
                var step = ReadNode(steps, current.Number);
                string? name = Attribute(step, "Name");
                if (string.IsNullOrWhiteSpace(name) || Enabled(step, "IsMissing"))
                    throw new FormatException("A selected step is missing or has no symbol.");
                candidates.Add(current.Number.Value, name!);
                if (candidates.Count > 64) throw new FormatException("A step range can contain at most 64 steps.");
            }
            else if (current.Kind == "transition")
            {
                if (Enabled(ReadNode(transitions, current.Number), "IsMissing"))
                    throw new FormatException("A transition in the selected range is missing.");
            }
            else if (current.Kind == "branch")
            {
                var branch = ReadNode(branches, current.Number);
                encounteredBranches.Add(branch);
                branchType = Attribute(branch, "Type");
                branchWidth = (int?)branch.Attribute("Cardinality");
                if (branchType is not ("AltBegin" or "AltEnd" or "SimBegin" or "SimEnd") || branchWidth < 2 || !branchWidth.HasValue)
                    throw new FormatException("The range contains an unsupported branch definition.");
            }
            else
            {
                throw new FormatException("Every path inside the range must reach the selected last step.");
            }

            var required = new HashSet<int>();
            if (current != ("step", (int?)last))
            {
                bool opening = branchType is "AltBegin" or "SimBegin";
                var outgoing = edges[current].OrderBy(edge => edge.From.Output).ToArray();
                if (outgoing.Length != (opening ? branchWidth : 1) || outgoing.Any(edge => edge.Link != "Direct" ||
                    (current.Kind != "branch" && edge.To.Kind != "branch" && edge.To.Kind != (current.Kind == "step" ? "transition" : "step"))) ||
                    (opening && !outgoing.Select(edge => edge.From.Output).SequenceEqual(Enumerable.Range(0, outgoing.Length).Select(port => (int?)port))))
                    throw new FormatException("Every path inside the range must follow direct GRAPH connections to the last step, without jumps.");

                required.UnionWith(Visit((outgoing[0].To.Kind, outgoing[0].To.Number)));
                foreach (var edge in outgoing.Skip(1))
                    required.IntersectWith(Visit((edge.To.Kind, edge.To.Number)));
            }

            if (current.Kind == "step") required.Add(current.Number.Value);
            active.Remove(current);
            requiredFrom.Add(current, required);
            return required;
        }

        HashSet<int> requiredSteps;
        try
        {
            requiredSteps = Visit(("step", first));
        }
        catch (FormatException ex) when (encounteredBranches.Count > 0 && branchSteps == null)
        {
            throw new BranchSelectionRequired(encounteredBranches, candidates, ex.Message);
        }

        if (encounteredBranches.Count == 0)
        {
            if (branchSteps != null && (branchSteps.Count != candidates.Count || !requiredSteps.SetEquals(branchSteps)))
                throw new FormatException("The confirmed step numbers do not match the direct path.");
            return candidates.Select(step => (step.Key, step.Value)).ToArray();
        }
        if (branchSteps == null) throw new BranchSelectionRequired(encounteredBranches, candidates, null);
        if (branchSteps.Count == 0 || branchSteps.Count > 64 || branchSteps.Distinct().Count() != branchSteps.Count ||
            branchSteps.Any(number => !candidates.ContainsKey(number)) || !requiredSteps.IsSubsetOf(branchSteps))
            throw new FormatException("Provide unique step numbers inside the range and include every step shared by all paths, including both endpoints.");
        return branchSteps.Select(number => (number, candidates[number])).ToArray();
    }

    internal sealed class BranchSelectionRequired(IEnumerable<XElement> branches, IReadOnlyDictionary<int, string> candidates, string? reason)
        : FormatException(reason ?? "Ask the user which steps to include for the detected branches.")
    {
        internal object[] Branches { get; } = branches.Select(branch => new { number = Number(branch), type = Attribute(branch, "Type") }).ToArray();
        internal object[] CandidateSteps { get; } = candidates.OrderBy(step => step.Key).Select(step => new { number = step.Key, name = step.Value }).ToArray();
        internal bool Complete { get; } = reason == null;
    }

    private static XElement ReadNode(IEnumerable<XElement> nodes, int? number)
    {
        var candidates = nodes.Where(node => Number(node) == number).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new FormatException("A selected node number does not resolve uniquely.");
    }

    internal static void Append(XElement block, XElement graph, IReadOnlyList<(int Number, string Name)> steps,
        string flag, string title, string comment, IReadOnlyList<string> languages)
    {
        var operations = Child(graph, "PostOperations") ?? throw new FormatException("GRAPH post-operations are unavailable.");
        var knownNetwork = graph.Descendants().FirstOrDefault(element => element.Name.LocalName == "FlgNet");
        if (knownNetwork == null || languages.Count == 0)
            throw new FormatException("The network schema or project languages could not be identified.");
        XNamespace graphNs = graph.Name.Namespace;
        AppendFlagDeclaration(block, flag, comment, languages);
        operations.Add(new XElement(graphNs + "PermanentOperation", new XAttribute("ProgrammingLanguage", "FBD"),
            Text(graphNs, "Title", title, languages), MakeNetwork(knownNetwork.Name.Namespace, steps, flag)));
    }

    internal static void AppendStepActions(XElement block, XElement graph, int sequenceNumber,
        IReadOnlyList<(int Number, string Name)> steps, string flag, string comment, IReadOnlyList<string> languages)
    {
        var actionLists = FindActionLists(graph, sequenceNumber, steps);
        AppendFlagDeclaration(block, flag, comment, languages);
        var ns = graph.Name.Namespace;
        AppendActions(actionLists, new XElement(ns + "Access", new XAttribute("Scope", "LocalVariable"),
            new XElement(ns + "Symbol", new XElement(ns + "Component", new XAttribute("Name", flag)))));
    }

    internal static void AppendGlobalStepActions(XElement graph, int sequenceNumber, IReadOnlyList<(int Number, string Name)> steps,
        string dbName, string[] memberPath)
    {
        var actionLists = FindActionLists(graph, sequenceNumber, steps);
        var prefix = new[] { dbName }.Concat(memberPath.Take(memberPath.Length - 1)).ToArray();
        string qualifiedPrefix = string.Join(".", new[] { "\"" + dbName + "\"" }.Concat(prefix.Skip(1))) + ".";
        var actions = graph.Descendants().Where(node => node.Name.LocalName == "Action" &&
            Attribute(node, "Qualifier") == "N").ToArray();
        foreach (var action in actions)
        {
            var tokens = action.Elements().ToArray();
            if (tokens.Length != 2 || tokens.Any(token => token.Name.LocalName != "Token") ||
                Attribute(tokens[1], "Text") != "\n") continue;
            var operand = Attribute(tokens[0], "Text");
            if (operand == null || !operand.StartsWith(qualifiedPrefix, StringComparison.OrdinalIgnoreCase) ||
                operand.Length == qualifiedPrefix.Length || operand.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0) continue;
            AppendActions(actionLists, new XElement(tokens[0].Name, new XAttribute("Text", qualifiedPrefix + memberPath.Last())));
            return;
        }

        foreach (var access in actions.SelectMany(action => Children(action, "Access")))
        {
            var symbol = Child(access, "Symbol");
            if (symbol == null) continue;
            XNamespace ns = symbol.Name.Namespace;
            var components = Children(symbol, "Component").ToArray();
            string? scope = Attribute(access, "Scope");
            string operand = string.Join(".", components.Select(part => Attribute(part, "Name")));
            if (scope == null || scope == "LocalVariable" || symbol.Descendants().Any(node => node.Name.LocalName == "Access")) continue;

            bool compact = components.Length == 1 && operand.StartsWith(qualifiedPrefix, StringComparison.OrdinalIgnoreCase);
            if (!compact && (components.Length <= prefix.Length || !components.Take(prefix.Length)
                .Select(part => Attribute(part, "Name")?.Trim().Trim('"')).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase))) continue;

            var next = components[compact ? 0 : prefix.Length];
            var leading = symbol.Elements().TakeWhile(element => element != next).ToArray();
            if (leading.Any(element => Attribute(element, "AccessModifier") == "Array")) continue;
            var reference = leading.Where(element => element.Name.LocalName == "Component" ||
                (element.Name.LocalName == "Token" && Attribute(element, "Text")?.Trim() == "."));
            var global = new XElement(access.Name, new XAttribute("Scope", scope),
                new XElement(ns + "Symbol", reference.Select(element => element.Name.LocalName == "Component"
                    ? new XElement(element.Name, new XAttribute("Name", Attribute(element, "Name")!))
                    : new XElement(element.Name, new XAttribute("Text", Attribute(element, "Text")!))),
                    new XElement(ns + "Component", new XAttribute("Name", (compact ? qualifiedPrefix : "") + memberPath.Last()))));
            AppendActions(actionLists, global);
            return;
        }
        throw new FormatException("No simple GRAPH action operand references the selected data block or structure as a template.");
    }

    private static XElement[] FindActionLists(XElement graph, int sequenceNumber, IReadOnlyList<(int Number, string Name)> steps)
    {
        var sequence = Children(graph, "Sequence").ElementAtOrDefault(sequenceNumber - 1) ??
            throw new FormatException("The selected sequence does not exist.");
        var allSteps = Elements(sequence, "Steps", "Step");
        return steps.Select(step => Child(ReadNode(allSteps, step.Number), "Actions") ??
            throw new FormatException("A selected step has no GRAPH action list.")).ToArray();
    }

    private static void AppendActions(IEnumerable<XElement> actionLists, XElement operand)
    {
        foreach (var actions in actionLists)
        {
            XNamespace ns = actions.Name.Namespace;
            var action = new XElement(ns + "Action", new XAttribute("Qualifier", "N"),
                new XElement(operand), new XElement(ns + "Token", new XAttribute("Text", "\n")));
            var last = Children(actions, "Action").LastOrDefault();
            if (last != null && !last.HasElements && string.IsNullOrEmpty(Attribute(last, "Qualifier")) &&
                string.IsNullOrEmpty(Attribute(last, "Event")) && string.IsNullOrWhiteSpace(last.Value))
                last.AddBeforeSelf(action);
            else
                actions.Add(action);
        }
    }

    internal static int RetargetGlobalStepActions(XElement graph, string dbName, string[] memberPath, string temporaryDbName)
    {
        string path = "." + string.Join(".", memberPath);
        string original = "\"" + dbName + "\"" + path;
        string temporary = "\"" + temporaryDbName + "\"" + path;
        int count = 0;
        foreach (var action in graph.Descendants().Where(node => node.Name.LocalName == "Action" && Attribute(node, "Qualifier") == "N"))
        {
            foreach (var token in Children(action, "Token").Where(token => Attribute(token, "Text") == original))
            {
                token.SetAttributeValue("Text", temporary);
                count++;
            }
            foreach (var operand in Children(action, "Access"))
            {
                if (Attribute(operand, "Scope") == "LocalVariable") continue;
                var components = Children(Child(operand, "Symbol"), "Component").ToArray();
                if (components.Length == 1 && Attribute(components[0], "Name") == original)
                {
                    components[0].SetAttributeValue("Name", temporary);
                    count++;
                }
                if (components.Length != memberPath.Length + 1 ||
                    !components.Skip(1).Select(component => Attribute(component, "Name")).SequenceEqual(memberPath)) continue;
                string? name = Attribute(components[0], "Name");
                if (name?.Trim('"') != dbName) continue;
                components[0].SetAttributeValue("Name", name.StartsWith("\"", StringComparison.Ordinal) ? "\"" + temporaryDbName + "\"" : temporaryDbName);
                count++;
            }
        }
        return count;
    }

    internal static XElement ForComparison(XElement block, bool ignoreCopyIdentity = false)
    {
        var copy = new XElement(block);
        var attributes = Child(copy, "AttributeList");
        if (ignoreCopyIdentity)
            attributes?.Elements().Where(attribute => attribute.Name.LocalName is "Name" or "Number" or "AutoNumber").Remove();
        attributes?.DescendantsAndSelf().Where(element => element.HasElements)
            .SelectMany(element => element.Nodes().OfType<XText>()).Where(text => string.IsNullOrWhiteSpace(text.Value)).Remove();
        attributes?.Nodes().OfType<XComment>().Remove();
        if (attributes != null && attributes.Nodes().All(node => node is XElement))
            attributes.ReplaceNodes(attributes.Elements().OrderBy(element => element.Name.ToString(), StringComparer.Ordinal).ToArray());
        var graph = PlcGraphReader.FindGraph(copy);
        graph?.Descendants().Where(node => node.HasElements && node.Name.LocalName is "Actions" or "Action")
            .SelectMany(node => node.Nodes().OfType<XText>()).Where(text => string.IsNullOrWhiteSpace(text.Value)).Remove();
        foreach (var container in copy.Descendants().Where(element => Children(element, "MultiLanguageText").Skip(1).Any()).ToArray())
        {
            var texts = container.Elements().ToArray();
            if (texts.Any(text => text.Name.LocalName != "MultiLanguageText" || Attribute(text, "Lang") == null) ||
                texts.Select(text => Attribute(text, "Lang")).Distinct().Count() != texts.Length ||
                container.Nodes().Any(node => !(node is XElement) && (!(node is XText whitespace) || !string.IsNullOrWhiteSpace(whitespace.Value)))) continue;
            container.ReplaceNodes(texts.OrderBy(text => Attribute(text, "Lang"), StringComparer.Ordinal));
        }
        return copy;
    }

    internal static string[] AppendDataFlagDeclaration(XElement block, string[] parentPath, string flag,
        string comment, IReadOnlyList<string> languages)
    {
        var root = StaticSection(block);
        var actualPath = new List<string>();
        var container = root;
        foreach (string name in parentPath)
        {
            var candidates = Children(container, "Member").Where(member =>
                string.Equals(Attribute(member, "Name"), name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (candidates.Length != 1 || Attribute(candidates[0], "Datatype") != "Struct")
                throw new FormatException("The destination must follow unambiguous, locally declared structures in the data block.");
            container = candidates[0];
            actualPath.Add(Attribute(container, "Name")!);
        }
        if (Children(container, "Member").Any(member => string.Equals(Attribute(member, "Name"), flag, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("The target data block member already exists.");
        AddFlagMember(container, flag, comment, languages);
        actualPath.Add(flag);
        return actualPath.ToArray();
    }

    private static void AppendFlagDeclaration(XElement block, string flag, string comment, IReadOnlyList<string> languages)
    {
        var statics = StaticSection(block);
        var sections = statics.Parent;
        if (Children(sections, "Section").SelectMany(section => Children(section, "Member"))
            .Any(member => string.Equals(Attribute(member, "Name"), flag, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("The flag must have a new, unused local name.");
        AddFlagMember(statics, flag, comment, languages);
    }

    private static XElement StaticSection(XElement block) => Children(InterfaceSections(block), "Section")
        .SingleOrDefault(section => Attribute(section, "Name") == "Static") ??
        throw new FormatException("The block has no static declaration section.");

    private static void AddFlagMember(XElement container, string flag, string comment, IReadOnlyList<string> languages)
    {
        if (languages.Count == 0) throw new FormatException("The active project languages could not be identified.");
        XNamespace interfaceNs = container.Name.Namespace;
        container.Add(new XElement(interfaceNs + "Member", new XAttribute("Name", flag), new XAttribute("Datatype", "Bool"),
            Text(interfaceNs, "Comment", comment, languages)));
    }

    private static XElement MakeNetwork(XNamespace ns, IReadOnlyList<(int Number, string Name)> steps, string flag)
    {
        const int firstId = 21;
        int target = firstId + steps.Count;
        int or = target + 1;
        int coil = steps.Count > 1 ? target + 2 : target + 1;
        var parts = new XElement(ns + "Parts", steps.Select((step, index) => Access(ns, firstId + index, step.Name, "X")), Access(ns, target, flag));
        var wires = new XElement(ns + "Wires");
        int nextWire = coil + 1;
        void Wire(params XElement[] endpoints) => wires.Add(new XElement(ns + "Wire", new XAttribute("UId", nextWire++), endpoints));
        if (steps.Count > 1)
        {
            parts.Add(new XElement(ns + "Part", new XAttribute("Name", "O"), new XAttribute("UId", or),
                new XElement(ns + "TemplateValue", new XAttribute("Name", "Card"), new XAttribute("Type", "Cardinality"), steps.Count)));
            for (int index = 0; index < steps.Count; index++)
                Wire(Identifier(ns, firstId + index), Pin(ns, or, "in" + (index + 1)));
            Wire(Pin(ns, or, "out"), Pin(ns, coil, "in"));
        }
        else
        {
            Wire(Identifier(ns, firstId), Pin(ns, coil, "in"));
        }
        parts.Add(new XElement(ns + "Part", new XAttribute("Name", "Coil"), new XAttribute("UId", coil)));
        Wire(Identifier(ns, target), Pin(ns, coil, "operand"));
        return new XElement(ns + "FlgNet", parts, wires);
    }

    private static XElement Access(XNamespace ns, int id, params string[] components) => new(ns + "Access",
        new XAttribute("Scope", "LocalVariable"), new XAttribute("UId", id),
        new XElement(ns + "Symbol", components.Select(name => new XElement(ns + "Component", new XAttribute("Name", name)))));

    private static XElement Identifier(XNamespace ns, int id) => new(ns + "IdentCon", new XAttribute("UId", id));
    private static XElement Pin(XNamespace ns, int id, string name) => new(ns + "NameCon", new XAttribute("UId", id), new XAttribute("Name", name));
    private static XElement Text(XNamespace ns, string element, string text, IEnumerable<string> languages) => new(ns + element,
        languages.Select(language => new XElement(ns + "MultiLanguageText", new XAttribute("Lang", language), text)));

    internal static bool SameBlock(XElement expected, XElement actual) => XNode.DeepEquals(expected, actual);

    internal static string? DescribeDifference(XElement expected, XElement actual)
    {
        if (SameBlock(expected, actual)) return null;
        var path = new List<string> { expected.Name.LocalName };
        while (expected.Name == actual.Name)
        {
            if (!expected.Attributes().Select(attribute => (attribute.Name, attribute.Value))
                .SequenceEqual(actual.Attributes().Select(attribute => (attribute.Name, attribute.Value))))
            {
                var changed = expected.Attributes().Concat(actual.Attributes()).Select(attribute => attribute.Name).Distinct()
                    .FirstOrDefault(name => expected.Attribute(name)?.Value != actual.Attribute(name)?.Value);
                if (changed?.LocalName == "Lang")
                    return "Exported comment languages differ at " + string.Join("/", path) + ": expected " +
                        DescribeLanguage(expected.Attribute(changed)?.Value) + ", found " + DescribeLanguage(actual.Attribute(changed)?.Value) + ".";
                return changed == null ? "Exported attribute order differs at " + string.Join("/", path) + "."
                    : "Exported attribute '" + changed.LocalName + "' differs at " + string.Join("/", path) + ".";
            }

            var expectedNodes = expected.Nodes().ToArray();
            var actualNodes = actual.Nodes().ToArray();
            int index = 0;
            while (index < expectedNodes.Length && index < actualNodes.Length && XNode.DeepEquals(expectedNodes[index], actualNodes[index]))
                index++;
            if (index >= expectedNodes.Length || index >= actualNodes.Length)
            {
                var extra = expectedNodes.ElementAtOrDefault(index) ?? actualNodes.ElementAtOrDefault(index);
                if (extra is XElement element) path.Add(element.Name.LocalName);
                return "Exported node count differs at " + string.Join("/", path) + ".";
            }
            if (!(expectedNodes[index] is XElement nextExpected) || !(actualNodes[index] is XElement nextActual))
                return "Exported content differs at " + string.Join("/", path) + ": expected " + DescribeNode(expectedNodes[index]) +
                    ", found " + DescribeNode(actualNodes[index]) + ".";
            if (nextExpected.Name != nextActual.Name)
                return "Exported nodes differ at " + string.Join("/", path) + ": expected " + nextExpected.Name.LocalName +
                    ", found " + nextActual.Name.LocalName + ".";
            expected = nextExpected;
            actual = nextActual;
            path.Add(expected.Name.LocalName);
        }
        return "Exported content differs at " + string.Join("/", path) + ".";
    }

    private static string DescribeNode(XNode node) => node is XElement element ? element.Name.LocalName : node.NodeType.ToString();

    private static string DescribeLanguage(string? language) => language == null ? "none" :
        language.Length <= 24 && language.All(character => char.IsLetterOrDigit(character) || character == '-') ? language : "invalid";
}
