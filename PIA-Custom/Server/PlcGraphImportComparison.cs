using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Pia.Custom;

internal static class PlcGraphImportComparison
{
    internal sealed class CommentAddition(string path, XElement text)
    {
        internal string Path { get; } = path;
        internal XElement Text { get; } = new(text);
    }

    internal static CommentAddition[] FindCommentAdditions(XElement original, XElement control)
    {
        var controlComments = InterfaceComments(control);
        var additions = new List<CommentAddition>();
        foreach (var originalComment in InterfaceComments(original))
        {
            if (!controlComments.TryGetValue(originalComment.Key, out var controlComment)) continue;
            var languages = new HashSet<string?>(PlcInterfaceReader.Children(originalComment.Value, "MultiLanguageText")
                .Select(text => (string?)text.Attribute("Lang")), StringComparer.Ordinal);
            foreach (var text in PlcInterfaceReader.Children(controlComment, "MultiLanguageText"))
            {
                string? language = (string?)text.Attribute("Lang");
                if (!string.IsNullOrWhiteSpace(language) && languages.Add(language))
                    additions.Add(new CommentAddition(originalComment.Key, text));
            }
        }
        return additions.ToArray();
    }

    internal static string? Difference(XElement expected, XElement actual, bool ignoreCopyIdentity = false,
        IReadOnlyList<CommentAddition>? controlAdditions = null)
    {
        var expectedComparison = PlcGraphFlagChange.ForComparison(expected, ignoreCopyIdentity);
        var actualComparison = PlcGraphFlagChange.ForComparison(actual, ignoreCopyIdentity);
        if (controlAdditions is { Count: > 0 })
        {
            var expectedComments = InterfaceComments(expectedComparison);
            var actualComments = InterfaceComments(actualComparison);
            foreach (var addition in controlAdditions)
            {
                if (!expectedComments.TryGetValue(addition.Path, out var expectedComment) ||
                    !actualComments.TryGetValue(addition.Path, out var actualComment)) continue;
                string? language = (string?)addition.Text.Attribute("Lang");
                if (PlcInterfaceReader.Children(expectedComment, "MultiLanguageText").Any(text => (string?)text.Attribute("Lang") == language)) continue;
                PlcInterfaceReader.Children(actualComment, "MultiLanguageText").FirstOrDefault(text => XNode.DeepEquals(text, addition.Text))?.Remove();
            }
        }
        return PlcGraphFlagChange.DescribeDifference(expectedComparison, actualComparison);
    }

    private static Dictionary<string, XElement> InterfaceComments(XElement block)
    {
        var sections = PlcInterfaceReader.InterfaceSections(block);
        if (sections == null) return new Dictionary<string, XElement>(StringComparer.Ordinal);
        return sections.Descendants().Where(node => node.Name.LocalName == "Comment" &&
            PlcInterfaceReader.Children(node, "MultiLanguageText").Any())
            .ToDictionary(node => string.Concat(node.AncestorsAndSelf().TakeWhile(parent => parent != sections).Reverse()
                .Select(PathSegment)), StringComparer.Ordinal);
    }

    private static string PathSegment(XElement node)
    {
        string label = (string?)node.Attribute("Name") ?? (string?)node.Attribute("Path") ?? node.Name.LocalName;
        return node.Name.LocalName + ":" + label.Length + ":" + label + "/";
    }
}
