using System;
using System.Collections.Generic;
using System.Linq;
using Pia.Custom;
using TiaMcpServer.ModelContextProtocol;

internal static class UsageTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static CrossReferenceEntry Ref(string relation, string location) =>
        new CrossReferenceEntry { ReferenceType = relation, ReferenceLocation = location };

    public static void Main()
    {
        PlcInspectionReaderTests.Run();
        PlcGraphTests.Run();
        PlcFbdSourceTests.Run();
        var sourceType = Guid.NewGuid();
        var sharedVersion = Guid.NewGuid();
        var versions = new HashSet<Guid> { sharedVersion };
        Require(UsageRules.MatchesTypeVersion(sourceType, sourceType, versions, sharedVersion),
            "Identity remains valid when a linked instance is renamed.");
        Require(!UsageRules.MatchesTypeVersion(sourceType, sourceType, versions, Guid.NewGuid()),
            "Equal type IDs or version labels alone must not prove a version match.");
        Require(!UsageRules.MatchesTypeVersion(sourceType, Guid.NewGuid(), versions, sharedVersion),
            "A different type cannot establish the requested identity.");
        Require(!UsageRules.MatchesTypeVersion(Guid.Empty, Guid.Empty, versions, sharedVersion),
            "Missing type identity is not evidence.");
        Require(!UsageRules.MatchesTypeVersion(sourceType, sourceType, new HashSet<Guid> { Guid.Empty }, Guid.Empty),
            "Missing version identity is not evidence.");

        var contentNames = UsageRules.MasterCopyObjectNames("Package", new[] { "FB_Valve", "FB_VALVE", "UDT_Control" });
        Require(contentNames.SequenceEqual(new[] { "FB_Valve", "UDT_Control" }), "Match the named contents of a master copy.");
        Require(UsageRules.MasterCopyObjectNames("FB_Valve", Array.Empty<string>()).Single() == "FB_Valve",
            "Use the copy name when content names are unavailable.");
        var libraries = new[] { "TIA_LIB_PIA_IndustrialAppSuite_A", "TIA_LIB_PIA_IndustrialAppSuite_B" };
        Require(UsageRules.ResolveLibraries(libraries, "Optimum").Length == 2, "Ambiguous aliases require selection.");
        Require(UsageRules.ResolveLibraries(libraries, libraries[0]).Single() == libraries[0], "Prefer an exact library name.");

        string[] paths = { "Types/A/Parameter", "Types/A/Parameter/Child", "Types/A/Parameter2", "MasterCopies/B/Parameter" };
        Require(paths.Count(path => UsageRules.Matches(path, "parameter")) == 2, "Return all ambiguous folders.");
        Require(paths.Count(path => UsageRules.Matches(path, "Types/A/Parameter")) == 1, "Resolve a qualified path.");
        Require(!UsageRules.Matches("MasterCopies/Types/A/Parameter", "Types/A/Parameter"), "Respect the library section.");
        Require(paths.Count(path => UsageRules.InFolder(path, paths[0])) == 2, "Include descendants and exclude prefix siblings.");
        Require(UsageRules.EditDistance("PARAMETER", "Parametr") == 1, "Suggest a folder after a typo.");

        var rows = new List<object>();
        var references = new[] { Ref("UsedBy", "NW2"), Ref("UsedBy", "NW5"), Ref("Uses", "NW1"), Ref("TypeInstance", "Declaration") };
        bool complete = UsageRules.AddReferences(references, false, rows);
        Require(complete && rows.Count == 3, "Keep both direct calls and the type declaration.");
        Require((string?)rows[1].GetType().GetProperty("location")!.GetValue(rows[1]) == "NW5", "Preserve the second network.");
        rows.Clear();
        Require(UsageRules.AddReferences(Array.Empty<CrossReferenceEntry>(), false, rows) &&
            Status(rows.Single()) == "noDirectUse", "An inspected instance with no uses must remain in the report.");
        rows.Clear();
        Require(!UsageRules.AddReferences(references, true, rows) && rows.Count == 4 &&
            Status(rows.Last()) == "incomplete", "Keep positive evidence when a branch fails.");
        rows.Clear();
        Require(!UsageRules.AddReferences(new[] { Ref("Unknown", "") }, false, rows) &&
            Status(rows.Single()) == "incomplete", "Unknown direction cannot establish absence.");
        Console.WriteLine("All usage rule checks passed.");
    }

    private static string? Status(object row) => (string?)row.GetType().GetProperty("status")!.GetValue(row);
}
