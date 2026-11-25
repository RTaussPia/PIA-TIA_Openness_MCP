using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Pia.Custom.PlcInterfaceReader;

namespace Pia.Custom;

internal static partial class PlcGraphReader
{
    internal static XElement? FindGraph(XElement block) => Children(Child(block, "ObjectList"), "SW.Blocks.CompileUnit")
        .Select(unit => Child(Child(Child(unit, "AttributeList"), "NetworkSource"), "Graph"))
        .SingleOrDefault(graph => graph != null);

    internal static object Read(XElement graph, int sequenceNumber, string section, int? number, int offset, int limit)
    {
        var sequences = Children(graph, "Sequence").ToArray();
        if (sequenceNumber > sequences.Length) return new { status = "sequenceNotFound", sequenceCount = sequences.Length };
        var sequence = sequences[sequenceNumber - 1];
        var steps = Elements(sequence, "Steps", "Step");
        var transitions = Elements(sequence, "Transitions", "Transition");
        var branches = Elements(sequence, "Branches", "Branch");
        var connections = Elements(sequence, "Connections", "Connection").Select(Connection).ToArray();
        var issues = new List<string>();
        var nodes = steps.Select(step => (kind: "step", number: Number(step))).Concat(
            transitions.Select(transition => (kind: "transition", number: Number(transition)))).Concat(
            branches.Select(branch => (kind: "branch", number: Number(branch)))).ToArray();
        if (steps.Length == 0 || nodes.Any(node => !node.number.HasValue) || nodes.Distinct().Count() != nodes.Length ||
            steps.Concat(transitions).Any(node => Enabled(node, "IsMissing")))
            issues.Add("The sequence has missing nodes or non-unique node numbers.");
        if (steps.Length > 0 && (connections.Length == 0 ||
            connections.Any(connection => !KnownNode(connection.From, nodes) || !KnownNode(connection.To, nodes) ||
                (connection.Link != "Direct" && connection.Link != "Jump"))))
            issues.Add("The sequence has missing or unreadable connections.");

        IEnumerable<object> items;
        switch (section)
        {
            case "steps":
                items = steps.Where(step => number == null || Number(step) == number).Select(step => new
                {
                    number = Number(step), name = Attribute(step, "Name"), labels = Texts(Child(step, "StepName")),
                    initial = Enabled(step, "Init"), maximumStepTime = Attribute(step, "MaximumStepTime"),
                    warningTime = Attribute(step, "WarningTime"),
                    comments = Texts(Child(step, "Comment")),
                    actionCount = Elements(step, "Actions", "Action").Length,
                    actions = number.HasValue ? Elements(step, "Actions", "Action").Select(Action).ToArray() : null,
                    interlock = number.HasValue ? Logic(Child(Child(step, "Interlocks"), "Interlock")) : null,
                    supervision = number.HasValue ? Logic(Child(Child(step, "Supervisions"), "Supervision")) : null
                });
                break;
            case "transitions":
                items = transitions.Where(transition => number == null || Number(transition) == number).Select(transition => new
                {
                    number = Number(transition), name = Attribute(transition, "Name"), labels = Texts(Child(transition, "TransitionName")),
                    language = Attribute(transition, "ProgrammingLanguage"), comments = Texts(Child(transition, "Comment")),
                    condition = number.HasValue ? Logic(transition) : null
                });
                break;
            case "branches":
                items = branches.Select(branch => new { number = Number(branch), type = Attribute(branch, "Type"), cardinality = (int?)branch.Attribute("Cardinality") });
                break;
            case "operations":
                items = Operations(graph, "PreOperations", "before", number).Concat(Operations(graph, "PostOperations", "after", number));
                break;
            default:
                items = connections;
                break;
        }
        var page = items.Skip(offset).Take(limit + 1).ToArray();
        return new
        {
            status = number.HasValue && page.Length == 0 && offset == 0 ? "nodeNotFound" : "report", complete = issues.Count == 0,
            issues, sequence = sequenceNumber, sequenceCount = sequences.Length, title = Texts(Child(sequence, "Title")),
            initialSteps = steps.Where(step => Enabled(step, "Init")).Select(Number).ToArray(),
            counts = new { steps = steps.Length, transitions = transitions.Length, branches = branches.Length, connections = connections.Length },
            section, items = page.Take(limit).ToArray(), nextOffset = page.Length > limit ? offset + limit : (int?)null
        };
    }

    internal static XElement[] Elements(XElement? item, string container, string element) => Children(Child(item, container), element).ToArray();
    internal static string? Attribute(XElement? item, string name) => item?.Attribute(name)?.Value;
    internal static int? Number(XElement item) => int.TryParse(Attribute(item, "Number"), out int number) ? number : (int?)null;
    internal static bool Enabled(XElement item, string name) => Attribute(item, name) is "true" or "1";

    internal static Edge Connection(XElement item) => new(Node(Child(item, "NodeFrom")), Node(Child(item, "NodeTo")), Child(item, "LinkType")?.Value);

    private static Endpoint Node(XElement? wrapper)
    {
        var item = wrapper?.Elements().SingleOrDefault();
        return new Endpoint(item?.Name.LocalName switch { "StepRef" => "step", "TransitionRef" => "transition", "BranchRef" => "branch",
            "EndConnection" => "end", _ => "unknown" }, item == null ? null : Number(item),
            (int?)item?.Attribute("In"), (int?)item?.Attribute("Out"));
    }

    private static bool KnownNode(Endpoint node, (string kind, int? number)[] nodes) =>
        node.Kind == "end" || (node.Number.HasValue && nodes.Contains((node.Kind, node.Number)));

    private static object[] Texts(XElement? item) => Children(item, "MultiLanguageText")
        .Select(text => new { language = Attribute(text, "Lang"), text = text.Value }).ToArray();

    private static object Action(XElement action) => new
    {
        qualifier = Attribute(action, "Qualifier"), trigger = Attribute(action, "Event"), interlock = Enabled(action, "Interlock"),
        tokens = action.Elements().Where(item => item.Name.LocalName is "Token" or "Access")
            .Select(item => item.Name.LocalName == "Token" ? Attribute(item, "Text") ?? item.Value : Operand(item)).ToArray()
    };

    private static string Operand(XElement item)
    {
        var symbol = Child(item, "Symbol");
        var components = Children(symbol, "Component").Select(component => Attribute(component, "Name")).ToArray();
        if (components.Length > 0 && !symbol!.Descendants().Any(element => element.Name.LocalName == "Access"))
            return (Attribute(item, "Scope") == "LocalVariable" ? "#" : "") + string.Join(".", components);
        var constant = Child(item, "Constant");
        return Attribute(constant, "Name") ?? Child(constant, "ConstantValue")?.Value ?? item.ToString(SaveOptions.DisableFormatting);
    }

    private static object? Logic(XElement? item)
    {
        var diagram = Child(item, "FlgNet");
        if (diagram == null) return null;
        return new
        {
            language = Attribute(item, "ProgrammingLanguage"),
            schema = diagram.Name.NamespaceName,
            parts = Child(diagram, "Parts")?.Elements().Select(part => new
            {
                id = Attribute(part, "UId"), kind = part.Name.LocalName,
                name = Attribute(part, "Name") ?? Attribute(Child(part, "CallInfo"), "Name"),
                operand = part.Name.LocalName == "Access" ? Operand(part) : null,
                parameters = part.Elements().Where(child => child.Name.LocalName is "Negated" or "TemplateValue")
                    .Select(child => new { kind = child.Name.LocalName, name = Attribute(child, "Name"), value = child.Value }).ToArray()
            }).ToArray(),
            wires = Elements(diagram, "Wires", "Wire").Select(wire => wire.Elements().Select(pin => new
            {
                kind = pin.Name.LocalName, id = Attribute(pin, "UId"), pin = Attribute(pin, "Name")
            }).ToArray()).ToArray()
        };
    }

    private static IEnumerable<object> Operations(XElement graph, string container, string phase, int? filter) =>
        Elements(graph, container, "PermanentOperation").Select((operation, index) => (operation, number: index + 1))
            .Where(item => filter == null || item.number == filter).Select(item => new
            {
                phase, item.number, language = Attribute(item.operation, "ProgrammingLanguage"),
                title = Texts(Child(item.operation, "Title")), logic = filter.HasValue ? Logic(item.operation) : null
            });

    internal sealed class Endpoint(string kind, int? number, int? input, int? output)
    {
        public string Kind { get; } = kind;
        public int? Number { get; } = number;
        public int? Input { get; } = input;
        public int? Output { get; } = output;
    }

    internal sealed class Edge(Endpoint from, Endpoint to, string? link)
    {
        public Endpoint From { get; } = from;
        public Endpoint To { get; } = to;
        public string? Link { get; } = link;
    }
}
