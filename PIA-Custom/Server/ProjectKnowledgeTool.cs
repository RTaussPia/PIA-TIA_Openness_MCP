using System;
using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;

namespace Pia.Custom;

public static class ProjectKnowledgeTool
{
    [McpServerTool(Name = "GetProjectKnowledge"), Description("Read one bundled PIA Markdown document for interpreting TIA project results.")]
    public static object GetProjectKnowledge(
        [Description("Document ID: libraryUsage, projectConventions, iasLibrary, or excelExport.")] string document)
    {
        string relativePath;
        switch (document)
        {
            case "libraryUsage":
                relativePath = Path.Combine("capabilities", "CheckLibraryUsage.md");
                break;
            case "projectConventions":
                relativePath = Path.Combine("knowledge", "TiaPortalProjectConventions.md");
                break;
            case "iasLibrary":
                relativePath = Path.Combine("knowledge", "PiaIndustrialAppSuiteLibrary.md");
                break;
            case "excelExport":
                relativePath = Path.Combine("capabilities", "ExportExcel.md");
                break;
            default:
                return new { status = "unknownDocument", candidates = new[] { "libraryUsage", "projectConventions", "iasLibrary", "excelExport" } };
        }

        try
        {
            string markdown = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));
            return new { status = "report", document, markdown };
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return new { status = "documentUnavailable", document };
        }
    }
}
