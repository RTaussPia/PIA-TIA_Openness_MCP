using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.ModelContextProtocol;

namespace Pia.Custom;

internal static class UsageRules
{
    internal static bool AddReferences(IEnumerable<CrossReferenceEntry> references, bool partial, List<object> rows)
    {
        int uses = 0;
        bool unknown = false;
        foreach (var reference in references)
        {
            switch (reference.ReferenceType)
            {
                case "UsedBy":
                case "TypeInstance":
                    uses++;
                    rows.Add(new { status = "used", caller = reference.ReferenceName,
                        callerPath = reference.ReferencePath, member = reference.SourceHierarchy, relation = reference.ReferenceType,
                        access = reference.Access, location = reference.ReferenceLocation, locationName = reference.LocationName });
                    break;
                case "Uses":
                case "InstanceType":
                case "MemberGroup":
                case "GroupMember":
                case "Scope":
                    break;
                default:
                    unknown = true;
                    break;
            }
        }
        if (partial || unknown)
        {
            rows.Add(new { status = "incomplete", reason = partial ?
                "TIA returned partial cross-references." : "Some reference directions could not be classified." });
            return false;
        }
        if (uses == 0) rows.Add(new { status = "noDirectUse" });
        return true;
    }

    internal static string[] MasterCopyObjectNames(string copyName, IEnumerable<string> contentNames)
    {
        var names = contentNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length > 0 ? names : new[] { copyName };
    }

    internal static string[] ResolveLibraries(IEnumerable<string> openLibraries, string query)
    {
        var names = openLibraries.ToArray();
        if (query.Length == 0) return names;
        var exact = names.Where(name => name.Equals(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return exact;
        var aliases = new[] { "piaIAS", "IAS", "Optimum", "piaOptimum", "AppSuite", "PIA IndustrialAppSuite" };
        var term = aliases.Contains(query, StringComparer.OrdinalIgnoreCase) ? "IndustrialAppSuite" : query;
        return names.Where(name => name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
    }

    internal static bool MatchesTypeVersion(Guid sourceType, Guid projectType, HashSet<Guid> sourceVersions, Guid projectVersion) =>
        sourceType != Guid.Empty && sourceType == projectType && projectVersion != Guid.Empty && sourceVersions.Contains(projectVersion);

    internal static bool Matches(string path, string query)
    {
        if (path.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (query.StartsWith("Types/", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("MasterCopies/", StringComparison.OrdinalIgnoreCase)) return false;
        return path.EndsWith("/" + query, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool InFolder(string path, string folder) =>
        path.Equals(folder, StringComparison.Ordinal) || path.StartsWith(folder + "/", StringComparison.Ordinal);
    internal static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

}
