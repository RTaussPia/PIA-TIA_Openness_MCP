using System;
using System.Linq;
using Pia.Custom;

internal static class PlcFbdSourceTests
{
    private const string Body = "RUNG\n    #Trigger \"FC_Build\"(Type := WSTRING#'', Message => #Message)\nEND_RUNG";
    private const string Original = "{ S7_PreferredLanguage := \"FBD\" }\nFUNCTION_BLOCK \"TestBlock\"\n" +
        "VAR_INPUT\n    Start : Bool;\nEND_VAR\nVAR\n    Existing : Int;\nEND_VAR\n" +
        "{ S7_Language := \"FBD\" }\nNETWORK\n// END_FUNCTION_BLOCK or NETWORK\nRUNG #Start Coil(#Old) END_RUNG\nEND_NETWORK\nEND_FUNCTION_BLOCK\n";
    private static readonly (string Name, string Type)[] NoLocals = Array.Empty<(string, string)>();

    internal static void Run()
    {
        using (var workspace = new PlcSourceWorkspace())
        {
            Require(workspace.Directory.Exists && workspace.Directory.ToString() == workspace.Directory.FullName,
                "The export and import directory must carry the original absolute path for Openness.");
        }
        Require(PlcFbdSource.ValidNetworkBody(Body), "Named pins and empty WSTRING literals are accepted.");
        Require(!PlcFbdSource.ValidNetworkBody("// Invalid before RUNG\n" + Body),
            "Reject comments before the first FBD rung, which the SIMATIC SD importer cannot parse.");
        Require(!PlcFbdSource.ValidNetworkBody("RUNG #Due NOT { S7_Templates := \"time_type := Time\" } #Timer.TON(pt := T#5s, et => ) END_RUNG"),
            "Reject an instruction template immediately after NOT.");
        Require(PlcFbdSource.ValidNetworkBody("RUNG #Due I_Coil(#Enable) END_RUNG\n" +
            "RUNG #Enable { S7_Templates := \"time_type := Time\" } #Timer.TON(pt := T#5s, et => ) Coil(#Due) END_RUNG"),
            "Accept a separate inverted coil feeding a timer rung.");
        Require(PlcFbdSource.ValidNetworkBody("RUNG \"END_NETWORK\" Coil(#Signal) END_RUNG"), "Quoted symbol names are not delimiters.");
        Require(!PlcFbdSource.ValidNetworkBody("RUNG #Signal Coil(#Out) END_RUNG END_NETWORK"), "Reject network delimiters inside fragments.");
        Require(!PlcFbdSource.ValidNetworkBody("RUNG #Signal Coil(#Out)"), "Reject incomplete rungs.");
        Require(!PlcFbdSource.ValidLocal("Injected; END_VAR", "Int"), "Reject an invalid declaration name.");

        var result = PlcFbdSource.Append(Original, new[] { Body }, new[] { ("Timer", "IEC_TIMER") }, new[] { ("Message", "WString[254]") });
        Require(result.ExistingNetworks == 1, "Count complete networks and ignore comment text.");
        Require(result.Source.Contains("Existing : Int;\n\r\n    Timer : IEC_TIMER;"), "Extend the existing static declaration section.");
        Require(result.Source.Contains("VAR_TEMP\r\n    Message : WString[254];\r\nEND_VAR"), "Create the temp declaration section.");
        Require(result.Source.Contains("END_VAR\r\n\r\n{ S7_Language := \"FBD\" }\nNETWORK"),
            "Keep the added declarations before the first network pragma.");
        Require(result.Source.Contains("// END_FUNCTION_BLOCK or NETWORK\nRUNG #Start Coil(#Old) END_RUNG\nEND_NETWORK"),
            "Preserve existing source and comments byte for byte.");
        Require(result.Source.IndexOf(result.AddedNetworks, StringComparison.Ordinal) <
            result.Source.LastIndexOf("END_FUNCTION_BLOCK", StringComparison.Ordinal), "Append before the block terminator.");
        Require(PlcFbdSource.Equivalent(Body, Body.Replace("\n    ", "\r\n        ")), "Normalize spacing between tokens.");
        Require(!PlcFbdSource.Equivalent(Body, Body.Replace("WSTRING#''", "WSTRING#' '")), "Spaces inside literals remain significant.");
        var converter = "RUNG #Due { S7_Templates := \"[src_type := Int, dest_type := WString]\" } " +
            "S_CONV(in := \"DB\".Value, out => #Text) \"Send\"(Buffer := \"DB\".THIS) END_RUNG";
        string converterBlock = PlcFbdSource.Append(Original, new[] { converter }, NoLocals, NoLocals).Source;
        string preserved = PlcFbdSource.PreserveConverterEno(converterBlock, new[] { Array.Empty<bool>(), new[] { false } });
        Require(preserved.Contains("S7_GenerateENO := \"FALSE\""), "Carry the original disabled ENO into the import.");
        Require(PlcFbdSource.Equivalent(preserved, converterBlock.Replace(".THIS", ".\"THIS\"")),
            "Compare the code independently of re-exported ENO annotations and the same quoted selector.");
        Require(!PlcFbdSource.Equivalent(preserved, converterBlock.Replace(".THIS", ".OTHER")),
            "Changing the destination member remains a failed verification.");
        Require(PlcFbdSource.ExpectedNewConverterEno(converter)[0], "The planned cascade enables ENO on a new converter by default.");
        Require(!PlcFbdSource.ReadExportedConverterEno(converterBlock)[1][0], "In the exported source, omitted ENO means disabled.");
        string enabled = converterBlock.Replace("dest_type := WString]\" }", "dest_type := WString]\"; S7_GenerateENO := \"TRUE\" }");
        Require(PlcFbdSource.ReadExportedConverterEno(enabled)[1][0], "Read explicit enabled ENO on a re-exported instruction.");

        var call = PlcFbdSource.ReadInputCalls(
            "RUNG \"FC_Send\"(SensorGroup := WSTRING#'', Source := , Job := \"DB\".Items[2, 4], Message => ) END_RUNG", 12)
            .Single();
        Require(call.Network == 12 && call.Name == "\"FC_Send\"", "Identify the exact call and network.");
        var open = PlcFbdSource.OpenInputs(call, new[] { "SensorGroup", "Source", "Job", "Mode" }).ToArray();
        Require(open.SequenceEqual(new[] { ("SensorGroup", "emptyValue"), ("Source", "unconnected"), ("Mode", "unconnected") }),
            "List empty, explicitly open, and omitted inputs; ignore connected indexed operands and open outputs.");

        var resources = new PlcFbdResources("MultiLingualTexts:\n  - id: MLC_Pia1\n    en-US: Original\n");
        string titleId = resources.Add("Sample: operator's request #1", new[] { "en-US", "en-US", "de-DE" });
        Require(titleId != "MLC_Pia1", "Do not reuse existing comment IDs.");
        var readback = new PlcFbdResources(resources.Write());
        Require(resources.HasSameTexts(readback) && resources.TokenFor(titleId) == readback.TokenFor(titleId),
            "Preserve existing text and characters that need YAML quoting.");
        string commentId = resources.Add("Temporary sensor payload", new[] { "en-US" });
        var documented = PlcFbdSource.Append(Original, new[] { Body }, NoLocals, new[] { ("Message", "WString") },
            new[] { titleId }, new System.Collections.Generic.Dictionary<string, string> { ["Message"] = commentId });
        Require(documented.AddedNetworks.Contains($"S7_NetworkTitle := \"{titleId}\""), "Connect the new resource to the network title.");
        Require(documented.Source.Contains($"{{ S7_MLC := \"{commentId}\" }}\r\n    Message : WString;"),
            "Connect the comment to the exact interface declaration.");
        string rewritten = documented.Source.Replace(titleId, "MLC_TiaTitle").Replace(commentId, "MLC_TiaComment");
        var renamedResources = new PlcFbdResources(resources.Write().Replace(titleId, "MLC_TiaTitle").Replace(commentId, "MLC_TiaComment"));
        Require(resources.HasSameTexts(renamedResources) &&
            PlcFbdSource.Equivalent(documented.Source, rewritten, resources, renamedResources),
            "Accept TIA Portal reassigning text IDs while the resource text and attachment stay the same.");
        var wrongTitle = new PlcFbdResources(renamedResources.Write().Replace("Sample: operator''s request #1", "Changed title"));
        Require(!PlcFbdSource.Equivalent(documented.Source, rewritten, resources, wrongTitle),
            "Reject a changed title after readback.");
        const string scl = "// Report the observed change.\r\nIF (NOT #Initialized) OR (#Current <> #Previous) THEN\r\n" +
            "    \"FC_Send\"(Value := INT_TO_WSTRING(#Current), SensorGroup := WSTRING#'');\r\n" +
            "    #Previous := #Current;\r\nEND_IF;";
        Require(PlcFbdSource.ValidSclNetworkBody(scl), "Accept SCL statements with comments and an ordinary conditional.");
        Require(!PlcFbdSource.ValidSclNetworkBody("END_FUNCTION_BLOCK FUNCTION_BLOCK \"Other\" #Flag := TRUE;"),
            "Keep an SCL change inside one network.");
        var mixed = PlcFbdSource.Append(Original, new[] { scl }, new[] { ("Previous", "Int") }, NoLocals,
            titleIds: new[] { titleId }, language: "SCL");
        Require(mixed.ExistingNetworks == 1 && mixed.AddedNetworks.Contains("{ S7_Language := \"SCL\";") &&
            mixed.Source.Contains("{ S7_PreferredLanguage := \"FBD\" }"), "Retain the host FBD language and append one SCL network.");
        var sclCall = PlcFbdSource.ReadInputCalls(scl, 2).Single();
        Require(PlcFbdSource.OpenInputs(sclCall, new[] { "Value", "SensorGroup" }).Single() == ("SensorGroup", "emptyValue"),
            "Keep the empty input review when an SCL actual value contains a nested conversion.");

        try
        {
            PlcFbdSource.Append(Original, new[] { Body }, NoLocals, new[] { ("start", "Bool") });
        }
        catch (FormatException)
        {
            return;
        }
        throw new InvalidOperationException("Reject a duplicate local across sections regardless of case.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
