using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;

namespace Pia.Custom;

public static class ProjectStructureTool
{
    [McpServerTool(Name = "GetProjectStructure"), Description("Read PLC program folders and software units in the open V21 project. Returns paths for further lookups; block names are optional.")]
    public static object GetProjectStructure(
        [Description("Exact PLC software name. Omit to inspect all controllers.")] string softwarePath = "",
        [Description("Exact program folder path using slashes, relative to the program-block root.")] string folder = "",
        [Description("Folder depth below the selected folder or program-block root. Range 0–32.")] int maxDepth = 4,
        [Description("Include block names within returned folders. Default false.")] bool includeBlocks = false,
        [Description("Set only after confirmation when the saved state cannot be read.")] bool savedStateConfirmed = false)
    {
        softwarePath = (softwarePath ?? "").Trim();
        folder = (folder ?? "").Trim();
        if (maxDepth < 0 || maxDepth > 32) return new { status = "invalidDepth", maximum = 32 };
        if (softwarePath.Length > 512) return new { status = "invalidSoftwarePath" };
        var segments = folder.Length == 0 ? Array.Empty<string>() : folder.Split('/');
        if (folder.Length > 2048 || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."))
            return new { status = "invalidFolder" };
        if (segments.Length > 0 && softwarePath.Length == 0) return new { status = "softwarePathRequired" };

        return ProjectSession.WithSavedProject(savedStateConfirmed,
            project => Read(project, softwarePath, segments, maxDepth, includeBlocks));
    }

    private static object Read(ProjectBase project, string softwarePath, string[] segments, int maxDepth, bool includeBlocks)
    {
        var controllers = new List<PlcSoftware>();
        var issues = new List<string>();
        LibraryUsage.FindControllers(project, controllers, issues);
        var selected = controllers.Where(plc => softwarePath.Length == 0 ||
            plc.Name.Equals(softwarePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (softwarePath.Length > 0 && selected.Length != 1)
            return new { status = selected.Length == 0 ? "plcNotFound" : "ambiguousPlc", candidates = controllers.Select(p => p.Name).ToArray(), issues };

        var programs = new List<object>();
        foreach (var plc in selected)
        {
            var rows = new List<object>();
            bool depthLimited = false;
            try
            {
                var current = ProgramRoot(plc);
                foreach (var segment in segments)
                {
                    var children = current.Children().ToArray();
                    var match = children.FirstOrDefault(group => group.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return new { status = "folderNotFound", softwarePath = plc.Name, parent = current.Path, candidates = children.Select(group => group.Path).ToArray() };
                    current = match;
                }
                depthLimited = Walk(current, maxDepth, includeBlocks, rows, issues);
            }
            catch (EngineeringException ex)
            {
                issues.Add($"Cannot read all program folders on {plc.Name} ({ex.GetType().Name}).");
            }
            programs.Add(new { softwarePath = plc.Name, depthLimited, folders = rows });
        }
        return new { status = "report", project = project.Name, complete = issues.Count == 0, issues, programs };
    }

    private static bool Walk(ProgramFolder folder, int depth, bool includeBlocks, List<object> rows, List<string> issues)
    {
        ProgramFolder[] children;
        string[]? blocks = null;
        try
        {
            children = folder.Children().ToArray();
        }
        catch (EngineeringException ex)
        {
            issues.Add($"Cannot read subfolders of '{folder.Path}' ({ex.GetType().Name}).");
            rows.Add(new { name = folder.Name, path = folder.Path });
            return false;
        }

        if (includeBlocks)
        {
            try { blocks = folder.BlockNames().ToArray(); }
            catch (EngineeringException ex) { issues.Add($"Cannot read blocks in '{folder.Path}' ({ex.GetType().Name})."); }
        }

        rows.Add(new { name = folder.Name, path = folder.Path, hasChildren = children.Length > 0, blocks });
        if (depth == 0) return children.Length > 0;
        bool limited = false;
        foreach (var child in children) limited |= Walk(child, depth - 1, includeBlocks, rows, issues);
        return limited;
    }

    private static ProgramFolder ProgramRoot(PlcSoftware plc)
    {
        var root = plc.BlockGroup;
        return new ProgramFolder(root.Name, "", Children, () => root.Blocks.Select(block => block.Name));

        IEnumerable<ProgramFolder> Children()
        {
            foreach (var group in root.Groups) yield return FromGroup(group, "");
            var units = plc.GetService<PlcUnitProvider>()?.UnitGroup;
            if (units != null && (units.Units.Count > 0 || units.SafetyUnits.Count > 0))
                yield return new ProgramFolder("Software units", "Software units", UnitFolders, () => Array.Empty<string>());

            IEnumerable<ProgramFolder> UnitFolders()
            {
                foreach (var unit in units!.Units) yield return FromGroup(unit.BlockGroup, "Software units/", unit.Name);
                foreach (var unit in units.SafetyUnits) yield return FromGroup(unit.BlockGroup, "Software units/", unit.Name);
            }
        }
    }

    private static ProgramFolder FromGroup(PlcBlockGroup group, string parent, string? name = null)
    {
        name = name ?? group.Name;
        string path = parent + name;
        return new ProgramFolder(name, path,
            () => group.Groups.Select(child => FromGroup(child, path + "/")),
            () => group.Blocks.Select(block => block.Name));
    }

    private sealed class ProgramFolder
    {
        internal string Name { get; }
        internal string Path { get; }
        internal Func<IEnumerable<ProgramFolder>> Children { get; }
        internal Func<IEnumerable<string>> BlockNames { get; }

        internal ProgramFolder(string name, string path, Func<IEnumerable<ProgramFolder>> children, Func<IEnumerable<string>> blockNames)
        {
            Name = name;
            Path = path;
            Children = children;
            BlockNames = blockNames;
        }
    }
}
