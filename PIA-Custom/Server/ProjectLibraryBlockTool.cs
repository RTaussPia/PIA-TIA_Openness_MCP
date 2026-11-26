using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

public static class ProjectLibraryBlockTool
{
    private sealed class Selection(PlcSoftware plc, PlcBlockGroup group, CodeBlockLibraryTypeVersion version)
    {
        internal PlcSoftware Plc { get; } = plc;
        internal PlcBlockGroup Group { get; } = group;
        internal CodeBlockLibraryTypeVersion Version { get; } = version;
    }

    private sealed class Dependency(Guid typeId, Guid versionId, string name, string version, string[] instances)
    {
        public Guid TypeId { get; } = typeId;
        public Guid VersionId { get; } = versionId;
        public string Name { get; } = name;
        public string Version { get; } = version;
        public string[] Instances { get; } = instances;
    }

    private sealed class Plan(ProjectBase project, string softwarePath, string entry, Guid typeId, Guid versionId,
        DateTime modified, string targetFolder, Dependency[] dependencies)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal ProjectBase Project { get; } = project;
        internal string SoftwarePath { get; } = softwarePath;
        internal string Entry { get; } = entry;
        internal Guid TypeId { get; } = typeId;
        internal Guid VersionId { get; } = versionId;
        internal DateTime Modified { get; } = modified;
        internal string TargetFolder { get; } = targetFolder;
        internal Dependency[] Dependencies { get; } = dependencies;
    }

    private static Plan? pendingPlan;

    [McpServerTool(Name = "PlanProjectLibraryBlock"), Description("Preview adding one released FC or FB from the project library to an existing PLC program folder. Checks exact version and installed dependencies without changing the project.")]
    public static object PlanProjectLibraryBlock(
        [Description("Exact PLC software name from GetProjectStructure, up to 512 characters.")] string softwarePath,
        [Description("Exact project library type path from CheckLibraryUsage, starting with Types/.")] string entry,
        [Description("Exact version ID from CheckLibraryUsage, as a GUID.")] string versionId,
        [Description("Exact existing path relative to the PLC program-block root. Use slashes or an empty string for the root.")] string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(softwarePath) || softwarePath.Length > 512 || softwarePath.Any(char.IsControl))
            return new { status = "invalidSoftwarePath" };
        if (!IsRelativePath(entry) || !entry.StartsWith("Types/", StringComparison.Ordinal))
            return new { status = "invalidEntry" };
        if (!Guid.TryParseExact(versionId, "D", out var id) || id == Guid.Empty)
            return new { status = "invalidVersionId" };
        if (targetFolder != "" && !IsRelativePath(targetFolder))
            return new { status = "invalidTargetFolder" };

        return ProjectSession.WithSavedProject(false, project =>
        {
            var resolved = Resolve(project, softwarePath, entry, id, targetFolder);
            if (!(resolved is Selection selected)) return resolved;
            var conflict = CheckExisting(selected);
            if (conflict != null) return conflict;
            var dependencies = ReadDependencies(selected);
            if (dependencies.Any(d => d.Instances.Length == 0))
                return new { status = "dependencyRequired", dependencies };

            var version = selected.Version;
            var plan = new Plan(project, softwarePath, entry, version.TypeObject.Guid, id, version.ModifiedDate,
                targetFolder, dependencies);
            pendingPlan = plan;
            return new { status = "preview", planId = plan.Id, project = project.Name, softwarePath, entry,
                typeId = plan.TypeId, versionId = id, version = version.VersionNumber.ToString(),
                blockPath = BlockPath(targetFolder, version.TypeObject.Name), dependencies };
        });
    }

    [McpServerTool(Name = "ApplyProjectLibraryBlock"), Description("Add the approved released block in the saved offline project and verify its library link. Requires the current preview from this server session. Does not compile or save.")]
    public static object ApplyProjectLibraryBlock(
        [Description("Plan ID returned by PlanProjectLibraryBlock in this server session.")] string planId,
        [Description("Set true only after explicit approval of this preview.")] bool approved)
    {
        if (!approved) return new { status = "approvalRequired" };
        var plan = pendingPlan;
        if (plan == null || !Guid.TryParseExact(planId, "D", out var id) || id != plan.Id)
            return new { status = "planNotFound" };

        return ProjectSession.WithOpenProject(project =>
        {
            if (!project.Equals(plan.Project)) return new { status = "projectChanged" };
            if (!(project.Parent is TiaPortal portal) || !(project is ITransactionSupport transactionProject))
                return new { status = "transactionUnavailable" };

            using var access = portal.ExclusiveAccess("Add approved project library block");
            var saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;
            if (Core.Portal.GetOnlineState(plan.SoftwarePath).State != "Offline")
                return new { status = "offlineRequired", softwarePath = plan.SoftwarePath };

            var resolved = Resolve(project, plan.SoftwarePath, plan.Entry, plan.VersionId, plan.TargetFolder);
            if (!(resolved is Selection selected)) return resolved;
            var version = selected.Version;
            if (version.TypeObject.Guid != plan.TypeId || version.ModifiedDate != plan.Modified)
                return new { status = "stalePlan" };
            var conflict = CheckExisting(selected);
            if (conflict != null) return conflict;
            if (!SameDependencies(plan.Dependencies, ReadDependencies(selected)))
                return new { status = "stalePlan", reason = "The installed library dependencies changed after the preview." };

            string name = version.TypeObject.Name;
            string blockPath = BlockPath(plan.TargetFolder, name);
            bool committing = false;
            try
            {
                using var transaction = access.Transaction(transactionProject, "Add approved project library block");
                var created = selected.Group.Blocks.CreateFrom(version, UpdatePathsMode.ThrowIfPathsConflict);
                var readBack = selected.Group.Blocks.Find(name);
                var actualVersion = (readBack as IEngineeringServiceProvider)?.GetService<LibraryTypeInstanceInfo>()?.LibraryTypeVersion;
                if (!(readBack is FC || readBack is FB) || !readBack.Equals(created) ||
                    actualVersion?.Guid != plan.VersionId || actualVersion.TypeObject.Guid != plan.TypeId)
                    return new { status = "verificationFailed", blockPath, reason = "The created function or function block did not have the expected library link and path." };
                if (!SameDependencies(plan.Dependencies, ReadDependencies(selected)))
                    return new { status = "verificationFailed", blockPath, reason = "An installed library dependency changed during the import." };

                pendingPlan = null;
                committing = true;
                transaction.CommitOnDispose();
            }
            catch (Exception ex)
            {
                return new { status = committing ? "commitUnverified" : "applyFailed", blockPath, reason = ex.GetType().Name };
            }
            return new { status = "applied", project = project.Name, softwarePath = plan.SoftwarePath,
                blockPath, typeId = plan.TypeId, versionId = plan.VersionId, version = version.VersionNumber.ToString(),
                verified = true, compiled = false, saved = false };
        });
    }

    private static bool IsRelativePath(string? path)
    {
        if (path == null || path.Length > 2048 || path.Any(char.IsControl) || path.Contains("\\"))
            return false;
        var segments = path.Split('/');
        return segments.Length <= 32 &&
            segments.All(segment => !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
    }

    private static string BlockPath(string folder, string name) => folder == "" ? name : folder + "/" + name;

    private static object Resolve(ProjectBase project, string softwarePath, string entry, Guid id, string targetFolder)
    {
        var controllers = new List<PlcSoftware>();
        var issues = new List<string>();
        LibraryUsage.FindControllers(project, controllers, issues);
        if (issues.Count != 0) return new { status = "incomplete", issues };
        var matches = controllers.Where(plc => plc.Name == softwarePath).ToArray();
        if (matches.Length != 1)
            return new { status = matches.Length == 0 ? "plcNotFound" : "ambiguousPlc", softwarePath };

        var segments = entry.Split('/');
        LibraryTypeFolder folder = project.ProjectLibrary.TypeFolder;
        foreach (var segment in segments.Skip(1).Take(segments.Length - 2))
        {
            var child = folder.Folders.Find(segment);
            if (child == null || child.Name != segment) return new { status = "entryNotFound", entry };
            folder = child;
        }
        var type = folder.Types.Find(segments.Last());
        if (type == null || type.Name != segments.Last()) return new { status = "entryNotFound", entry };
        var version = type.Versions.FirstOrDefault(candidate => candidate.Guid == id);
        if (version == null) return new { status = "versionNotFound", entry, versionId = id };
        if (!(version is CodeBlockLibraryTypeVersion code) || type.Guid == Guid.Empty)
            return new { status = "unsupportedType", entry };
        if (version.State != LibraryTypeVersionState.Committed)
            return new { status = "releasedVersionRequired", entry, versionId = id };

        PlcBlockGroup group = matches[0].BlockGroup;
        foreach (var segment in targetFolder.Split('/').Where(segment => segment.Length != 0))
        {
            var child = group.Groups.Find(segment);
            if (child == null || child.Name != segment) return new { status = "targetFolderNotFound", targetFolder };
            group = child;
        }
        return new Selection(matches[0], group, code);
    }

    private static object? CheckExisting(Selection selected)
    {
        var source = selected.Version;
        foreach (var version in source.TypeObject.Versions.OrderBy(version => version.Guid == source.Guid))
        {
            var instances = version.FindInstances(selected.Plc)
                .Select(info => LibraryUsage.ObjectPath(info.LibraryTypeInstance, selected.Plc)).ToArray();
            if (instances.Length != 0)
                return new { status = source.Guid == version.Guid ? "alreadyPresent" : "versionConflict",
                    typeId = source.TypeObject.Guid, versionId = version.Guid, instances };
        }

        var name = source.TypeObject.Name;
        var matches = FindNamedBlocks(selected.Plc.BlockGroup, name)
            .Select(block => LibraryUsage.ObjectPath(block, selected.Plc)).ToArray();
        return matches.Length == 0 ? null : new { status = "nameConflict", name, instances = matches };
    }

    private static IEnumerable<PlcBlock> FindNamedBlocks(PlcBlockGroup folder, string name)
    {
        var match = folder.Blocks.Find(name);
        if (match != null) yield return match;
        foreach (var child in folder.Groups)
            foreach (var block in FindNamedBlocks(child, name))
                yield return block;
    }

    private static Dependency[] ReadDependencies(Selection selected)
    {
        var result = new List<Dependency>();
        var visited = new HashSet<Guid> { selected.Version.Guid };

        void Visit(LibraryTypeVersion version)
        {
            foreach (var dependency in version.Dependencies)
            {
                if (!visited.Add(dependency.Guid)) continue;
                var paths = dependency.FindInstances(selected.Plc)
                    .Select(info => LibraryUsage.ObjectPath(info.LibraryTypeInstance, selected.Plc))
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                result.Add(new Dependency(dependency.TypeObject.Guid, dependency.Guid, dependency.TypeObject.Name,
                    dependency.VersionNumber.ToString(), paths));
                Visit(dependency);
            }
        }

        Visit(selected.Version);
        return result.OrderBy(dependency => dependency.TypeId).ThenBy(dependency => dependency.VersionId).ToArray();
    }

    private static bool SameDependencies(Dependency[] expected, Dependency[] actual) =>
        expected.Length == actual.Length && expected.Zip(actual, (left, right) =>
            left.TypeId == right.TypeId && left.VersionId == right.VersionId && left.Instances.SequenceEqual(right.Instances))
            .All(same => same);
}
