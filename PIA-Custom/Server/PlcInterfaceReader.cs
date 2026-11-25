using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Pia.Custom;

internal static class PlcInterfaceReader
{
    internal sealed class Declaration
    {
        public string Section { get; }
        public string Path { get; }
        public string DataType { get; }
        public string? StartValue { get; }
        public bool HasInlineMembers { get; }
        public string[] TypeChain { get; }

        internal Declaration(string section, string path, XElement member, string[] typeChain)
        {
            Section = section;
            Path = path;
            DataType = member.Attribute("Datatype")?.Value ?? "";
            StartValue = Child(member, "StartValue")?.Value;
            HasInlineMembers = Members(member).Any();
            TypeChain = typeChain;
        }
    }

    internal static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(child => child.Name.LocalName == name);

    internal static IEnumerable<XElement> Children(XElement? element, string name) =>
        element?.Elements().Where(child => child.Name.LocalName == name) ?? Enumerable.Empty<XElement>();

    internal static XElement? InterfaceSections(XElement item) => Child(Child(Child(item, "AttributeList"), "Interface"), "Sections");

    internal static IEnumerable<Declaration> ReadDeclarations(XElement sections, string sectionFilter)
    {
        foreach (var section in Children(sections, "Section"))
        {
            string name = section.Attribute("Name")?.Value ?? "";
            if (sectionFilter.Length != 0 && !Same(name, sectionFilter)) continue;
            foreach (var member in Children(section, "Member"))
            {
                string path = member.Attribute("Name")?.Value ?? "";
                if (path.Length != 0) yield return new Declaration(name, path, member, Array.Empty<string>());
            }
        }
    }

    internal static object ResolveMember(XElement sections, string[] path, string sectionFilter,
        Func<string, (XElement? sections, string? issue)> resolveType)
    {
        var matchingSections = Children(sections, "Section")
            .Where(section => sectionFilter.Length == 0 || Same(section.Attribute("Name")?.Value, sectionFilter))
            .SelectMany(section => Children(section, "Member")
                .Where(member => Same(member.Attribute("Name")?.Value, path[0]))
                .Select(member => (section: section.Attribute("Name")?.Value ?? "", member)))
            .ToArray();
        if (matchingSections.Length == 0) return new { status = "memberNotFound", complete = true, segment = path[0] };
        if (matchingSections.Length > 1)
            return new { status = "ambiguousMember", candidates = matchingSections.Select(match => match.section).ToArray() };

        var (sectionName, current) = matchingSections[0];
        var chain = new List<string>();
        var resolvedPath = new List<string> { current.Attribute("Name")!.Value };
        foreach (var segment in path.Skip(1))
        {
            var children = Members(current).ToArray();
            if (children.Length == 0)
            {
                var type = current.Attribute("Datatype")?.Value ?? "";
                if (type.StartsWith("Array", StringComparison.OrdinalIgnoreCase))
                    return new { status = "unsupportedArrayPath", complete = false, segment };
                if (type.IndexOf('"') >= 0)
                {
                    var result = resolveType(type);
                    if (result.sections == null)
                        return new { status = "typeResolutionIncomplete", complete = false, type, reason = result.issue };
                    chain.Add(type);
                    children = Children(result.sections, "Section").SelectMany(item => Children(item, "Member")).ToArray();
                }
            }
            var matches = children.Where(member => Same(member.Attribute("Name")?.Value, segment)).ToArray();
            if (matches.Length == 0) return new { status = "memberNotFound", complete = true, segment };
            if (matches.Length > 1) return new { status = "ambiguousMember", complete = false, segment };
            current = matches[0];
            resolvedPath.Add(current.Attribute("Name")!.Value);
        }
        var declaration = new Declaration(sectionName, string.Join(".", resolvedPath), current, chain.ToArray());
        return new { status = "found", complete = declaration.DataType.Length != 0, declaration };
    }

    private static IEnumerable<XElement> Members(XElement member) =>
        Children(member, "Member").Concat(Children(Child(member, "Sections"), "Section").SelectMany(section => Children(section, "Member")));

    private static bool Same(string? first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
