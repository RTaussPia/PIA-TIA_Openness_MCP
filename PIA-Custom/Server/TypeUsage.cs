using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;

namespace Pia.Custom;

internal static class TypeUsage
{
    internal static void ReadType(ProjectLibrary projectLibrary, LibraryType source, IEnumerable<(string path, LibraryType type)> namePeers, List<PlcSoftware> controllers,
        EntryUsage report, bool includeReferences, Guid? selectedVersion)
    {
        var typeId = source.Guid;
        report.TypeId = typeId;
        if (typeId == Guid.Empty) { report.Issues.Add("Source type ID unavailable."); return; }
        var sourceVersions = new HashSet<Guid>();
        foreach (var version in source.Versions)
        {
            var id = version.Guid;
            if (id == Guid.Empty) { report.Issues.Add("A source version ID is unavailable."); continue; }
            sourceVersions.Add(id);
            report.SourceVersions.Add(new { version = version.VersionNumber.ToString(), versionId = id });
        }

        // Names identify possible conflicts only. Never traverse their PLC instances here.
        string? localPath = null;
        foreach (var peer in namePeers)
        {
            var peerId = peer.type.Guid;
            if (peerId == typeId) { localPath = peer.path; continue; }
            report.IdentityConflicts.Add(new { reason = "differentTypeId", projectEntry = peer.path, projectTypeId = peerId,
                projectVersions = peer.type.Versions.Select(v => new { version = v.VersionNumber.ToString(), versionId = v.Guid }).ToArray() });
        }

        var local = projectLibrary.FindType(typeId);
        if (local == null) return;
        foreach (var version in local.Versions)
        {
            var versionId = version.Guid;
            if (versionId == Guid.Empty) { report.Issues.Add("A project version ID is unavailable."); continue; }
            if (!UsageRules.MatchesTypeVersion(typeId, local.Guid, sourceVersions, versionId))
            {
                report.IdentityConflicts.Add(new { reason = "differentVersionId", projectEntry = localPath ?? ("Types/" + LibraryUsage.ObjectPath(local, projectLibrary.TypeFolder)),
                    projectTypeId = local.Guid, projectVersion = version.VersionNumber.ToString(), projectVersionId = versionId });
                continue;
            }
            if (selectedVersion.HasValue && selectedVersion.Value != versionId) continue;
            report.MatchedVersions.Add(new { version = version.VersionNumber.ToString(), versionId });
            foreach (var plc in controllers)
            {
                try
                {
                    foreach (var info in version.FindInstances(plc))
                        report.AddInstance(plc, info.LibraryTypeInstance, version.VersionNumber.ToString(), versionId, includeReferences);
                }
                catch (Exception ex)
                {
                    report.Issues.Add($"Instance lookup for version {versionId} on {plc.Name} failed ({ex.GetType().Name}).");
                }
            }
        }
    }
}

internal sealed class EntryUsage
{
    public string Entry { get; }
    public string MatchBasis { get; }
    public Guid? TypeId { get; set; }
    public List<object> MatchedVersions { get; } = new List<object>();
    public List<object> SourceVersions { get; } = new List<object>();
    public List<object> IdentityConflicts { get; } = new List<object>();
    public bool RequiresDecision => IdentityConflicts.Count > 0;
    public string? Question => RequiresDecision ? "Check a different project identity separately or skip it?" : null;
    public List<object> Instances { get; } = new List<object>();
    public List<string> Issues { get; } = new List<string>();
    public bool Complete => Issues.Count == 0;
    public bool? ProjectInstanceFound => Instances.Count > 0 ? true : Complete ? false : (bool?)null;

    public EntryUsage(string entry, string matchBasis)
    {
        Entry = entry;
        MatchBasis = matchBasis;
    }

    public void AddInstance(PlcSoftware plc, IEngineeringObject instance, string? version, Guid? versionId, bool includeReferences)
    {
        string projectObject = LibraryUsage.ObjectPath(instance, plc);
        var references = includeReferences ? new List<object>() : null;
        if (references != null && !LibraryUsage.ReadReferences(instance, projectObject, references))
            Issues.Add($"Incomplete cross-references for {plc.Name}/{projectObject}.");
        Instances.Add(new { plc = plc.Name, projectObject, version, versionId, references });
    }
}
