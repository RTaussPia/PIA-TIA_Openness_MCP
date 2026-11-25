using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Pia.Custom.PlcInterfaceReader;

namespace Pia.Custom;

internal static class PlcCallReader
{
    internal sealed class Call
    {
        public int Network { get; }
        public string? NetworkTitle { get; }
        public string Name { get; }
        public string? BlockType { get; }
        public object? Instance { get; }
        public List<object> Pins { get; } = new List<object>();

        internal Call(int network, string? title, XElement info)
        {
            Network = network;
            NetworkTitle = title;
            Name = info.Attribute("Name")?.Value ?? "";
            BlockType = info.Attribute("BlockType")?.Value;
            var instance = Child(info, "Instance");
            if (instance != null)
                Instance = new { scope = instance.Attribute("Scope")?.Value, components = Components(instance) };
        }
    }

    internal static List<Call> Read(XElement item, int networkFilter, string callName,
        List<int> unreadNetworks, out int networkCount)
    {
        var calls = new List<Call>();
        var units = Children(Child(item, "ObjectList"), "SW.Blocks.CompileUnit").ToArray();
        networkCount = units.Length;
        for (int index = 0; index < units.Length; index++)
        {
            int number = index + 1;
            if (networkFilter != 0 && networkFilter != number) continue;
            var unit = units[index];
            var source = Child(Child(unit, "AttributeList"), "NetworkSource");
            var diagram = Child(source, "FlgNet");
            if (diagram == null)
            {
                if (source?.Elements().Any() == true) unreadNetworks.Add(number);
                continue;
            }

            string? title = ReadTitle(unit);
            var elements = Child(diagram, "Parts")?.Elements().ToLookup(element => element.Attribute("UId")?.Value ?? "");
            var wires = Children(Child(diagram, "Wires"), "Wire").ToArray();
            foreach (var element in Children(Child(diagram, "Parts"), "Call"))
            {
                var info = Child(element, "CallInfo");
                string? name = info?.Attribute("Name")?.Value;
                string? id = element.Attribute("UId")?.Value;
                if (info == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                {
                    if (!unreadNetworks.Contains(number)) unreadNetworks.Add(number);
                    continue;
                }
                if (callName.Length != 0 && !name!.Equals(callName, StringComparison.OrdinalIgnoreCase)) continue;

                var call = new Call(number, title, info);
                var parameters = Children(info, "Parameter").ToLookup(pin => pin.Attribute("Name")?.Value ?? "");
                var endpoints = wires.SelectMany(wire => Children(wire, "NameCon")
                    .Where(endpoint => endpoint.Attribute("UId")?.Value == id)
                    .Select(endpoint => (wire, endpoint)))
                    .ToLookup(pair => pair.endpoint.Attribute("Name")?.Value ?? "");
                foreach (string pin in parameters.Select(p => p.Key).Concat(endpoints.Select(e => e.Key)).Distinct())
                {
                    var parameter = parameters[pin].FirstOrDefault();
                    var connections = endpoints[pin].SelectMany(pair => pair.wire.Elements()
                        .Where(endpoint => endpoint != pair.endpoint)
                        .Select(endpoint => ResolveEndpoint(endpoint, elements))).ToArray();
                    if (connections.Any(value => !value.complete) && !unreadNetworks.Contains(number))
                        unreadNetworks.Add(number);
                    call.Pins.Add(new { name = pin, section = parameter?.Attribute("Section")?.Value,
                        dataType = parameter?.Attribute("Type")?.Value,
                        connections = connections.Select(value => value.connection).ToArray() });
                }
                calls.Add(call);
            }
        }
        return calls;
    }

    private static string? ReadTitle(XElement unit)
    {
        var title = Children(Child(unit, "ObjectList"), "MultilingualText")
            .FirstOrDefault(text => text.Attribute("CompositionName")?.Value == "Title");
        var texts = Children(Child(title, "ObjectList"), "MultilingualTextItem")
            .Select(item => Child(item, "AttributeList")).ToArray();
        var language = texts.FirstOrDefault(text => Child(text, "Culture")?.Value == "de-DE") ?? texts.FirstOrDefault();
        return Child(language, "Text")?.Value;
    }

    private static (object connection, bool complete) ResolveEndpoint(XElement endpoint, ILookup<string, XElement>? elements)
    {
        string name = endpoint.Name.LocalName;
        string? uid = endpoint.Attribute("UId")?.Value;
        string? pin = endpoint.Attribute("Name")?.Value;
        if (name == "OpenCon") return (new { kind = "open" }, true);
        if (name == "Powerrail") return (new { kind = "powerRail" }, true);
        var related = uid == null ? Array.Empty<XElement>() : elements?[uid].ToArray() ?? Array.Empty<XElement>();
        if (related.Length != 1) return (new { kind = "unresolved", endpoint = name, uid, pin }, false);

        var element = related[0];
        if (name == "IdentCon" && element.Name.LocalName == "Access")
        {
            var constant = Child(element, "Constant");
            var components = Components(Child(element, "Symbol"));
            var indexed = Children(Child(element, "Symbol"), "Component").Any(component => component.HasElements);
            var constantName = constant?.Attribute("Name")?.Value;
            var constantValue = Child(constant, "ConstantValue")?.Value;
            var scope = element.Attribute("Scope")?.Value;
            if (!indexed && components.Length > 0) return (new { kind = "symbol", scope, components }, true);
            if (indexed) return (new { kind = "indexedSymbol", scope, components }, false);
            if (constantName != null || constantValue != null)
                return (new { kind = "constant", scope, name = constantName, value = constantValue,
                    dataType = Child(constant, "ConstantType")?.Value }, true);
            return (new { kind = "unresolved", endpoint = name, uid, scope }, false);
        }
        if (name == "NameCon")
            return (new { kind = "pin", uid, pin,
                part = Child(element, "CallInfo")?.Attribute("Name")?.Value ?? element.Attribute("Name")?.Value }, true);
        return (new { kind = "unresolved", endpoint = name, uid, pin }, false);
    }

    private static string[] Components(XElement? container) =>
        Children(container, "Component").Select(component => component.Attribute("Name")?.Value ?? "").ToArray();
}
