using System;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace Pia.Custom;

public static class LibraryUsageTool
{
    [McpServerTool(Name = "CheckLibraryUsage"), Description("Check a PLC library entry or folder in the open V21 project. Choose project or global scope. Types match by type and version IDs; master copies by object names. Use GetExport for paged results.")]
    public static object CheckLibraryUsage(
        [Description("project or global. Select from the user's question.")] string scope,
        [Description("Entry name or path, optionally qualified with Types/ or MasterCopies/. Supply entry or folder.")] string entry = "",
        [Description("Folder name or path. Include all descendants. Supply folder or entry.")] string folder = "",
        [Description("Global library name or alias. Omit to select the only open global library.")] string library = "",
        [Description("Optional version GUID when explicitly selecting one type version by entry.")] string versionId = "",
        [Description("Read direct cross-references for each project instance. Default true.")] bool includeReferences = true,
        [Description("Only after user confirmation when the project saved state is unavailable.")] bool savedStateConfirmed = false)
    {
        scope = (scope ?? "").Trim().ToLowerInvariant();
        if (scope != "project" && scope != "global") return new { status = "invalidScope" };
        library = (library ?? "").Trim();
        if (library.Length > 512 || (scope == "project" && library.Length > 0)) return new { status = "invalidLibrary" };
        entry = (entry ?? "").Trim();
        folder = (folder ?? "").Trim();
        if ((entry.Length == 0) == (folder.Length == 0)) return new { status = "specifyEntryOrFolder" };
        bool selectFolder = folder.Length > 0;
        string query = selectFolder ? folder : entry;
        if (query.Length > 2048 || query.Split('/').Any(string.IsNullOrWhiteSpace))
            return new { status = selectFolder ? "invalidFolder" : "invalidEntry" };
        Guid? selectedVersion = null;
        if (!string.IsNullOrEmpty(versionId))
        {
            if (selectFolder || !Guid.TryParse(versionId, out var id) || id == Guid.Empty) return new { status = "invalidVersionId" };
            selectedVersion = id;
        }
        return ProjectSession.WithSavedProject(savedStateConfirmed, project =>
            LibrarySelection.Read(project, scope, library, query, selectFolder, includeReferences, selectedVersion));
    }
}
