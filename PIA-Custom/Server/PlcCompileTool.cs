using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using TiaMcpServer.Siemens;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

public static class PlcCompileTool
{
    [McpServerTool(Name = "CompilePlcSoftware"), Description("Compile one offline PLC in the current open project and report errors and warnings. Use GetExport for paged results.")]
    public static object CompilePlcSoftware(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath)
    {
        softwarePath = (softwarePath ?? "").Trim();
        if (softwarePath.Length == 0 || softwarePath.Length > 512 || softwarePath.Any(char.IsControl))
            return new { status = "invalidSoftwarePath" };

        return ProjectSession.WithOpenProject(project =>
        {
            var controllers = new List<PlcSoftware>();
            var issues = new List<string>();
            LibraryUsage.FindControllers(project, controllers, issues);
            if (issues.Count > 0)
                return new { status = "plcSelectionIncomplete", project = project.Name, issues };

            var matches = controllers.Where(plc => plc.Name.Equals(softwarePath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1)
                return new { status = matches.Length == 0 ? "plcNotFound" : "ambiguousPlc", project = project.Name, softwarePath };
            string plcName = matches[0].Name;
            if (!(project.Parent is TiaPortal portal))
                return new { status = "exclusiveAccessUnavailable", project = project.Name, softwarePath = plcName };

            try
            {
                using var access = portal.ExclusiveAccess("Compile offline PLC software");
                string onlineState = Core.Portal.GetOnlineState(plcName).State ?? "Unknown";
                if (onlineState != "Offline")
                    return new { status = "offlineRequired", project = project.Name, softwarePath = plcName, onlineState };

                var result = Core.CompileAndDiagnosePlc(plcName, "");
                var errors = result.Errors?.ToArray() ?? Array.Empty<string>();
                var warnings = result.Warnings?.ToArray() ?? Array.Empty<string>();
                return new
                {
                    status = "compiled", project = project.Name, softwarePath = plcName, state = result.State,
                    result.ErrorCount, result.WarningCount, errors, warnings,
                    complete = result.Meta?["diagnosticsComplete"]?.GetValue<bool>() == true &&
                        result.ErrorCount == errors.Length && result.WarningCount == warnings.Length
                };
            }
            catch (Exception ex) when (ex is EngineeringException || ex is PortalException || ex is McpException)
            {
                return new { status = "compileFailed", project = project.Name, softwarePath = plcName,
                    reason = "TIA Portal did not return a compilation result (" + ex.GetType().Name + ")." };
            }
        });
    }
}
