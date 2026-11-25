using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using TiaMcpServer.Siemens;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

internal static class Program
{
    public static void Main()
    {
        Engineering.TiaMajorVersion = 21;
        AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver;
        RunServer();
    }

    private static void RunServer()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        var excelExport = McpServerTool.Create(typeof(ExcelExportTool).GetMethod(nameof(ExcelExportTool.ExportExcel))!);
        var tools = new List<McpServerTool>
        {
            McpServerTool.Create(typeof(LibraryUsageTool).GetMethod(nameof(LibraryUsageTool.CheckLibraryUsage))!),
            McpServerTool.Create(typeof(ProjectStructureTool).GetMethod(nameof(ProjectStructureTool.GetProjectStructure))!),
            McpServerTool.Create(typeof(ProjectKnowledgeTool).GetMethod(nameof(ProjectKnowledgeTool.GetProjectKnowledge))!),
            McpServerTool.Create(typeof(PlcInspectionTools).GetMethod(nameof(PlcInspectionTools.GetPlcBlockInterface))!),
            McpServerTool.Create(typeof(PlcInspectionTools).GetMethod(nameof(PlcInspectionTools.GetPlcCallConnections))!),
            McpServerTool.Create(typeof(PlcSourceTools).GetMethod(nameof(PlcSourceTools.GetPlcBlockSource))!),
            McpServerTool.Create(typeof(PlcSourceTools).GetMethod(nameof(PlcSourceTools.PlanPlcFbdAppend))!),
            McpServerTool.Create(typeof(PlcSourceTools).GetMethod(nameof(PlcSourceTools.ApplyPlcFbdAppend))!),
            McpServerTool.Create(typeof(PlcSourceTools).GetMethod(nameof(PlcSourceTools.PlanPlcSclAppend))!),
            McpServerTool.Create(typeof(PlcSourceTools).GetMethod(nameof(PlcSourceTools.ApplyPlcSclAppend))!),
            McpServerTool.Create(typeof(PlcCompileTool).GetMethod(nameof(PlcCompileTool.CompilePlcSoftware))!),
            excelExport
        };
        foreach (var name in new[] { "ListPortalProcessProjects", "AttachToOpenProject", "SaveProject", "GetExport",
            "GetCrossReferences", "DescribeBlockLogic", "GetPlcTags" })
            tools.Add(McpServerTool.Create(typeof(Core).GetMethod(name)!));

        var wrappedTools = Core.WrapTools(tools);
        excelExport.ProtocolTool.Annotations!.IdempotentHint = false;
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(wrappedTools);
        builder.Build().Run();
    }
}
