using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;

namespace Pia.Custom;

public static partial class PlcSourceTools
{
    [McpServerTool(Name = "GetPlcBlockSource"), Description("Read a bounded page of one PLC block's SIMATIC SD source.")]
    public static object GetPlcBlockSource(
        [Description("Exact PLC software name.")] string softwarePath,
        [Description("Exact program-block path.")] string blockPath,
        [Description("Zero-based character offset.")] int offset = 0,
        [Description("Maximum characters, from 1 to 8000.")] int limit = 4000)
    {
        if (offset < 0 || limit < 1 || limit > 8000) return new { status = "invalidPage" };
        var validation = PlcInspectionTools.ValidateBlockPaths(softwarePath, blockPath);
        if (validation != null) return validation;
        return ProjectSession.WithOpenProject(project =>
            PlcInspectionTools.WithOpenBlock(project, softwarePath, blockPath, (plc, block, path) =>
            {
                using var documents = new PlcSourceWorkspace();
                string source;
                try
                {
                    source = ReadSource(block, documents).Text;
                }
                catch (InvalidDataException)
                {
                    return new { status = "sourceUnavailable", softwarePath = plc.Name, blockPath = path };
                }
                if (offset > source.Length) return new { status = "invalidPage", sourceLength = source.Length };
                int count = Math.Min(limit, source.Length - offset);
                if (offset > 0 && offset < source.Length && char.IsLowSurrogate(source[offset]))
                    return new { status = "invalidPage", sourceLength = source.Length };
                if (count > 0 && offset + count < source.Length && char.IsHighSurrogate(source[offset + count - 1]))
                    count++;
                return new { status = "report", softwarePath = plc.Name, blockPath = path,
                    language = block.ProgrammingLanguage.ToString(), sourceLength = source.Length,
                    source = source.Substring(offset, count),
                    nextOffset = offset + count < source.Length ? offset + count : (int?)null };
            }));
    }

    private static (string Path, string Text) ReadSource(PlcBlock block, PlcSourceWorkspace documents)
    {
        var result = block.ExportAsDocuments(documents.Directory, block.Name);
        var file = result.ExportedDocuments.SingleOrDefault(item =>
            item.Extension.Equals(".s7dcl", StringComparison.OrdinalIgnoreCase));
        if (result.State != Siemens.Engineering.SW.DocumentResultState.Success || file == null || file.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("SIMATIC SD block source is unavailable or exceeds 4 MiB.");
        return (file.FullName, File.ReadAllText(file.FullName));
    }
}
