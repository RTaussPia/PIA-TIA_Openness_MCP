using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;

namespace Pia.Custom;

internal static class LibrarySelection
{
    private sealed class Folder
    {
        internal string Path { get; }
        private readonly LibraryTypeFolder? types;
        private readonly MasterCopyFolder? copies;

        internal Folder(string path, LibraryTypeFolder? types, MasterCopyFolder? copies)
        {
            Path = path;
            this.types = types;
            this.copies = copies;
        }

        internal IEnumerable<Folder> Children()
        {
            if (types != null)
                foreach (var child in types.Folders) yield return new Folder(Path + "/" + child.Name, child, null);
            if (copies != null)
                foreach (var child in copies.Folders) yield return new Folder(Path + "/" + child.Name, null, child);
        }

        internal IEnumerable<Folder> Walk()
        {
            yield return this;
            foreach (var child in Children())
                foreach (var nested in child.Walk()) yield return nested;
        }

        internal IEnumerable<(string path, LibraryType type)> NamedTypes(HashSet<string> names)
        {
            if (types == null) yield break;
            if (names.Count <= 3)
            {
                foreach (var name in names)
                {
                    var match = types.Types.Find(name);
                    if (match != null) yield return (Path + "/" + match.Name, match);
                }
                yield break;
            }
            foreach (var type in types.Types)
            {
                var name = type.Name;
                if (names.Contains(name)) yield return (Path + "/" + name, type);
            }
        }

        internal IEnumerable<Entry> Entries()
        {
            if (types != null)
                foreach (var type in types.Types) yield return new Entry(Path + "/" + type.Name, type, null);
            if (copies != null)
                foreach (var copy in copies.MasterCopies) yield return new Entry(Path + "/" + copy.Name, null, copy);
        }
    }

    private sealed class Entry
    {
        internal string Path { get; }
        internal LibraryType? Type { get; }
        internal MasterCopy? Copy { get; }

        internal Entry(string path, LibraryType? type, MasterCopy? copy)
        {
            Path = path;
            Type = type;
            Copy = copy;
        }
    }

    internal static object Read(ProjectBase project, string scope, string libraryName, string query,
        bool selectFolder, bool includeReferences, Guid? selectedVersion)
    {
        ILibrary library = project.ProjectLibrary;
        if (scope == "global")
        {
            IEngineeringObject? root = project;
            while (root != null && !(root is TiaPortal)) root = root.Parent;
            if (!(root is TiaPortal portal)) return new { status = "portalUnavailable", complete = false };
            var open = portal.GlobalLibraries.ToArray();
            var names = UsageRules.ResolveLibraries(open.Select(x => x.Name), libraryName);
            if (names.Length != 1)
                return new { status = names.Length == 0 ? "libraryNotOpen" : "selectLibrary",
                    candidates = names.Length == 0 ? open.Select(x => x.Name).ToArray() : names };
            var selected = open.Where(x => x.Name == names[0]).ToArray();
            if (selected.Length != 1) return new { status = "selectLibrary", candidates = names };
            library = selected[0];
            libraryName = names[0];
        }
        else libraryName = project.Name;

        var roots = new[] { new Folder("Types", library.TypeFolder, null), new Folder("MasterCopies", null, library.MasterCopyFolder) };
        var segments = query.Split('/');
        bool qualified = roots.Any(f => f.Path.Equals(segments[0], StringComparison.OrdinalIgnoreCase));
        var folderQuery = selectFolder ? query : string.Join("/", segments.Take(segments.Length - 1));
        List<Folder> folders;
        if (qualified)
        {
            folders = roots.Where(f => f.Path.Equals(segments[0], StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var segment in folderQuery.Split('/').Skip(1))
                folders = folders.SelectMany(f => f.Children()).Where(f => UsageRules.Matches(f.Path, segment)).ToList();
        }
        else
        {
            folders = roots.SelectMany(f => f.Walk()).ToList();
            if (folderQuery.Length > 0) folders = folders.Where(f => UsageRules.Matches(f.Path, folderQuery)).ToList();
        }
        if (folders.Count == 0)
        {
            var leaf = folderQuery.Split('/').Last();
            var choices = roots.SelectMany(f => f.Walk()).OrderBy(f => UsageRules.EditDistance(f.Path.Split('/').Last(), leaf))
                .Take(5).Select(f => f.Path).ToArray();
            return new { status = "folderNotFound", candidates = choices };
        }

        List<Entry> entries;
        if (selectFolder)
        {
            if (folders.Count != 1) return new { status = "selectFolder", candidates = folders.Select(f => f.Path).ToArray() };
            entries = folders[0].Walk().SelectMany(f => f.Entries()).ToList();
        }
        else
        {
            var leaf = segments.Last();
            var candidates = folders.SelectMany(f => f.Entries()).Where(e =>
                e.Path.Split('/').Last().IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var exact = candidates.Where(e => UsageRules.Matches(e.Path, leaf)).ToList();
            entries = exact.Count > 0 || segments.Length > 1 ? exact : candidates;
            if (entries.Count != 1)
                return new { status = entries.Count == 0 ? "entryNotFound" : "selectEntry", candidates = entries.Select(e => e.Path).ToArray() };
        }

        if (selectedVersion.HasValue && (entries[0].Type == null ||
            !entries[0].Type!.Versions.Any(v => v.Guid == selectedVersion.Value)))
            return new { status = "versionNotFound", entry = entries[0].Path };

        var typeNames = new HashSet<string>(entries.Where(e => e.Type != null).Select(e => e.Type!.Name), StringComparer.OrdinalIgnoreCase);
        var peers = new List<(string path, LibraryType type)>();
        if (typeNames.Count > 0)
        {
            var projectTypes = new Folder("Types", project.ProjectLibrary.TypeFolder, null);
            foreach (var folder in projectTypes.Walk())
                peers.AddRange(folder.NamedTypes(typeNames));
        }
        var byName = peers.ToLookup(p => p.type.Name, StringComparer.OrdinalIgnoreCase);
        var controllers = new List<PlcSoftware>();
        var issues = new List<string>();
        LibraryUsage.FindControllers(project, controllers, issues);
        var results = new List<EntryUsage>();
        foreach (var entry in entries)
        {
            var result = new EntryUsage(entry.Path, entry.Type != null ? "typeAndVersionId" : "nameOnly");
            result.Issues.AddRange(issues);
            try
            {
                if (entry.Type != null) TypeUsage.ReadType(project.ProjectLibrary, entry.Type, byName[entry.Type.Name], controllers, result, includeReferences, selectedVersion);
                else MasterCopyUsage.Read(entry.Copy!, controllers, result, includeReferences);
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Cannot finish checking this entry ({ex.GetType().Name}).");
            }
            results.Add(result);
        }
        return new { status = "report", project = project.Name, scope, library = libraryName,
            folder = selectFolder ? folders[0].Path : null, entries = results.Count,
            complete = issues.Count == 0 && results.All(r => r.Complete), requiresDecision = results.Any(r => r.RequiresDecision), issues, results };
    }
}
