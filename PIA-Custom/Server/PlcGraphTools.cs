using System;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;

namespace Pia.Custom;

public static partial class PlcGraphTools
{
    [McpServerTool(Name = "GetPlcGraph"), Description("Read a GRAPH function block's configured steps, transitions, connections, branches, or permanent operations. Does not read live step states. Use GetExport for paged results.")]
    public static object GetPlcGraph(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath,
        [Description("Exact GRAPH function block path relative to the program-block root.")] string blockPath,
        [Description("One-based sequence index.")] int sequence = 1,
        [Description("flow, steps, transitions, branches, or operations.")] string section = "flow",
        [Description("Optional step or transition number, or one-based operation number, to include logic details.")] int? number = null,
        [Description("Zero-based offset in the selected section.")] int offset = 0,
        [Description("Number of items, from 1 to 50.")] int limit = 20)
    {
        var validation = PlcInspectionTools.ValidateBlockPaths(softwarePath, blockPath);
        if (validation != null) return validation;
        section = (section ?? "").Trim().ToLowerInvariant();
        if (section is not ("flow" or "steps" or "transitions" or "branches" or "operations") ||
            (number.HasValue && section is "flow" or "branches")) return new { status = "invalidSection" };
        if (sequence < 1 || number < 0 || offset < 0 || offset > int.MaxValue - 50 || limit < 1 || limit > 50)
            return new { status = "invalidPage" };

        return ProjectSession.WithOpenProject(project => PlcInspectionTools.WithOpenBlock(project, softwarePath, blockPath, (_, block, path) =>
        {
            var check = CheckGraphBlock(block, path);
            if (check != null) return check;
            var exported = PlcInspectionTools.ReadExport(file => block.Export(file, ExportOptions.None), "SW.Blocks.");
            var graph = exported == null ? null : PlcGraphReader.FindGraph(exported);
            if (graph == null) return new { status = "graphExportUnavailable", complete = false, blockPath = path };
            return new { blockPath = path, softwarePath = softwarePath.Trim(), graph = PlcGraphReader.Read(graph, sequence, section, number, offset, limit) };
        }));
    }

    private static object? CheckGraphBlock(PlcBlock block, string path)
    {
        if (!(block is FB) || block.ProgrammingLanguage.ToString() != "GRAPH") return new { status = "unsupportedBlock", blockPath = path };
        if (block.IsKnowHowProtected || !block.IsConsistent) return new { status = "graphUnavailable", complete = false, blockPath = path };
        return null;
    }
}
