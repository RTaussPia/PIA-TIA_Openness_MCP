using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

internal static class ProjectSession
{
    internal static object WithSavedProject(bool savedStateConfirmed, Func<ProjectBase, object> read)
    {
        var project = Core.Portal.CurrentProject;
        if (project == null)
        {
            var names = TiaPortal.GetProcesses().Select(p => p.ProjectPath)
                .Where(path => path != null).Select(path => Path.GetFileNameWithoutExtension(path!.Name)).ToArray();
            if (names.Length != 1)
                return new { status = names.Length == 0 ? "openProjectRequired" : "selectProject", projects = names };
            if (!Core.Portal.AttachToOpenProject(names[0]))
                return new { status = "attachRequired", project = names[0] };
            project = Core.Portal.CurrentProject;
        }
        if (project == null) return new { status = "openProjectRequired" };
        var state = CheckSaved(project, savedStateConfirmed);
        if (state != null) return state;
        object result;
        try
        {
            result = read(project);
        }
        catch (Exception ex)
        {
            return new { status = "incomplete", complete = false, project = project.Name,
                reason = "Cannot read the project library or device tree (" + ex.GetType().Name + ")." };
        }
        return CheckSaved(project, savedStateConfirmed) ?? result;
    }

    private static object? CheckSaved(ProjectBase project, bool confirmed)
    {
        try
        {
            return project.IsModified ? new { status = "saveApprovalRequired", project = project.Name } : null;
        }
        catch (EngineeringException)
        {
            return confirmed ? null : new { status = "confirmSavedState", project = project.Name };
        }
    }
}
