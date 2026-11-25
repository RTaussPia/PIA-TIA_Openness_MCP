using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

public static partial class PlcSourceTools
{
    private static FbdPlan? pendingPlan;

    [McpServerTool(Name = "PlanPlcFbdAppend"), Description("Preview appending FBD networks and optional locals to existing function blocks on one PLC. Returns the exact additions and a plan ID. Does not change the project.")]
    public static object PlanPlcFbdAppend(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath,
        [Description("One to eight function block changes, with up to 12,000 network source characters across the plan.")] PlcFbdBlockChange[] changes)
        => PlanAppend(softwarePath, changes, "FBD");

    [McpServerTool(Name = "PlanPlcSclAppend"), Description("Preview one SCL network in an existing FBD function block. Includes title, declaration comments, open input review, and a plan ID. Does not change the project.")]
    public static object PlanPlcSclAppend(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath,
        [Description("One SCL network and optional local declarations.")] PlcSclBlockChange change)
        => change == null
            ? new { status = "invalidChange", reason = "Supply one SCL network change." }
            : PlanAppend(softwarePath, new[] { change.ToAppendChange() }, "SCL");

    private static object PlanAppend(string softwarePath, PlcFbdBlockChange[] changes, string language)
    {
        PlcFbdChange[] requested;
        try
        {
            requested = PlcFbdChange.Read(changes, language);
        }
        catch (FormatException ex)
        {
            return new { status = "invalidChange", reason = ex.Message };
        }
        foreach (var change in requested)
        {
            var validation = PlcInspectionTools.ValidateBlockPaths(softwarePath, change.BlockPath);
            if (validation != null) return validation;
        }
        return ProjectSession.WithOpenProject(project =>
        {
            var saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;

            var targets = new List<FbdTarget>();
            var reviewer = new PlcFbdPlanReview(softwarePath.Trim());
            var cultures = requested.Any(change => change.HasDocumentation)
                ? ActiveCultures(project)
                : Array.Empty<string>();
            foreach (var change in requested)
            {
                object outcome = WithFbdBlock(project, softwarePath, change.BlockPath, block =>
                {
                    using var documents = new PlcSourceWorkspace();
                    try
                    {
                        var exported = ReadSource(block, documents);
                        var preview = PrepareSource(change, exported.Path, exported.Text, cultures, language);
                        var review = reviewer.Read(change, block, preview.ExistingNetworks);
                        return new FbdTarget(change, block.Number, Fingerprint(documents),
                            preview.AddedNetworks, preview.ExistingNetworks, review);
                    }
                    catch (InvalidDataException)
                    {
                        return new { status = "sourceUnavailable", blockPath = change.BlockPath };
                    }
                    catch (FormatException ex)
                    {
                        return new { status = "unsupportedSource", blockPath = change.BlockPath, reason = ex.Message };
                    }
                });
                if (!(outcome is FbdTarget target)) return outcome;
                targets.Add(target);
            }

            saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;
            var plan = new FbdPlan(Guid.NewGuid(), project, softwarePath.Trim(), targets.ToArray(), cultures, language);
            pendingPlan = plan;
            return new
            {
                status = "preview",
                planId = plan.Id,
                project = project.Name,
                softwarePath = plan.SoftwarePath,
                changes = targets.Select(target => new
                {
                    blockPath = target.Change.BlockPath,
                    firstNetwork = target.ExistingNetworks + 1,
                    lastNetwork = target.ExistingNetworks + target.Change.Networks.Length,
                    statics = target.Change.Statics.Select(local => DescribeLocal(local, target.Change.Comments)).ToArray(),
                    temps = target.Change.Temps.Select(local => DescribeLocal(local, target.Change.Comments)).ToArray(),
                    networkTitles = target.Change.NetworkTitles?.Select((title, index) =>
                        new { network = target.ExistingNetworks + index + 1, title }).ToArray(),
                    inputReview = target.InputReview,
                    source = target.AddedNetworks
                }).ToArray()
            };
        });
    }

    [McpServerTool(Name = "ApplyPlcFbdAppend"), Description("Apply the latest FBD plan after explicit approval, if the saved project, target source, and offline state still match. Verifies readback in one TIA transaction.")]
    public static object ApplyPlcFbdAppend(
        [Description("ID returned by PlanPlcFbdAppend in this server session.")] string planId,
        [Description("True only after the user approves this exact preview.")] bool approved)
        => ApplyAppend(planId, approved, "FBD");

    [McpServerTool(Name = "ApplyPlcSclAppend"), Description("Apply a user-approved SCL network plan to the unchanged saved project while the PLC is offline. Verifies readback in a TIA transaction.")]
    public static object ApplyPlcSclAppend(
        [Description("ID returned by PlanPlcSclAppend in this server session.")] string planId,
        [Description("True only after the user approves this exact preview.")] bool approved)
        => ApplyAppend(planId, approved, "SCL");

    private static object ApplyAppend(string planId, bool approved, string language)
    {
        if (!approved) return new { status = "approvalRequired" };
        var plan = pendingPlan;
        if (plan == null || plan.Language != language || !Guid.TryParseExact(planId, "D", out var id) || id != plan.Id)
            return new { status = "planNotFound" };

        return ProjectSession.WithOpenProject(project =>
        {
            if (!project.Equals(plan.Project)) return new { status = "projectChanged" };
            if (!(project.Parent is TiaPortal portal) || !(project is ITransactionSupport transactionProject))
                return new { status = "transactionUnavailable" };
            using var exclusive = portal.ExclusiveAccess("Apply approved PLC function block changes");
            var saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;
            if (!string.Equals(Core.Portal.GetOnlineState(plan.SoftwarePath).State, "Offline", StringComparison.Ordinal))
                return new { status = "offlineRequired", softwarePath = plan.SoftwarePath };
            if (plan.Cultures.Length > 0 && !plan.Cultures.SequenceEqual(ActiveCultures(project)))
                return new { status = "stalePlan", reason = "The active project languages changed after the preview." };

            var imports = new List<FbdImport>();
            var reviewer = new PlcFbdPlanReview(plan.SoftwarePath);
            try
            {
                foreach (var target in plan.Targets)
                {
                    object outcome = WithFbdBlock(project, plan.SoftwarePath, target.Change.BlockPath,
                        block => StageImport(block, target, plan.Cultures, reviewer, language));
                    if (!(outcome is FbdImport import)) return outcome;
                    imports.Add(import);
                }

                string? blockPath = null;
                bool committing = false;
                try
                {
                    using var transaction = exclusive.Transaction(transactionProject, "Append approved " + language + " networks");
                    foreach (var import in imports)
                    {
                        blockPath = import.BlockPath;
                        ImportAndVerify(import);
                    }
                    pendingPlan = null;
                    committing = true;
                    transaction.CommitOnDispose();
                }
                catch (FbdImportException ex)
                {
                    return new { status = "applyFailed", blockPath, reason = ex.Message };
                }
                catch (Exception ex)
                {
                    return new { status = committing ? "commitUnverified" : "applyFailed", blockPath, reason = ex.GetType().Name };
                }
                return new { status = "applied", project = project.Name, softwarePath = plan.SoftwarePath,
                    blocks = imports.Select(import => import.BlockPath).ToArray(), verified = true, compiled = false, saved = false };
            }
            finally
            {
                foreach (var import in imports) import.Documents.Dispose();
            }
        });
    }

    private static object WithFbdBlock(ProjectBase project, string softwarePath, string blockPath, Func<PlcBlock, object> action) =>
        PlcInspectionTools.WithOpenBlock(project, softwarePath, blockPath, (_, block, path) =>
        {
            if (!(block is FB) || block.ProgrammingLanguage.ToString() != "FBD" || block.IsKnowHowProtected ||
                !block.IsConsistent || !(block.Parent is PlcBlockGroup))
                return new { status = "unsupportedBlock", blockPath = path };
            return action(block);
        });

    private static object StageImport(PlcBlock block, FbdTarget target, IReadOnlyList<string> cultures,
        PlcFbdPlanReview reviewer, string language)
    {
        var documents = new PlcSourceWorkspace();
        try
        {
            var exported = ReadSource(block, documents);
            if (block.Number != target.Number || Fingerprint(documents) != target.Fingerprint)
            {
                documents.Dispose();
                return new { status = "stalePlan", blockPath = target.Change.BlockPath };
            }
            var change = target.Change;
            object currentReview = reviewer.Read(change, block, target.ExistingNetworks);
            if (JsonSerializer.Serialize(currentReview) != JsonSerializer.Serialize(target.InputReview))
            {
                documents.Dispose();
                return new { status = "stalePlan", blockPath = change.BlockPath, reason = "The open input review changed after the preview." };
            }
            var existingEno = ReadConverterEno(block);
            string preservedSource = PlcFbdSource.PreserveConverterEno(exported.Text, existingEno);
            var updated = PrepareSource(change, exported.Path, preservedSource, cultures, language);
            var expectedEno = existingEno.Concat(change.Networks.Select(network =>
                language == "FBD" ? PlcFbdSource.ExpectedNewConverterEno(network) : Array.Empty<bool>())).ToArray();
            File.WriteAllText(exported.Path, updated.Source, new UTF8Encoding(true, true));
            if (updated.TextResources != null)
                File.WriteAllText(Path.ChangeExtension(exported.Path, ".s7res"), updated.TextResources.Write(), new UTF8Encoding(true, true));
            return new FbdImport(change.BlockPath, block.Name, block.Number, (PlcBlockGroup)block.Parent, documents,
                updated.Source, Fingerprint(documents, resourcesOnly: true, excludeTextResources: updated.TextResources != null),
                expectedEno, updated.TextResources);
        }
        catch
        {
            documents.Dispose();
            throw;
        }
    }

    private static (string Source, string AddedNetworks, int ExistingNetworks, PlcFbdResources? TextResources)
        PrepareSource(PlcFbdChange change, string sourcePath, string source, IReadOnlyList<string> cultures, string language)
    {
        PlcFbdResources? texts = null;
        string[]? titles = null;
        Dictionary<string, string>? comments = null;
        if (change.HasDocumentation)
        {
            if (cultures.Count == 0) throw new InvalidDataException("No active project languages were found for the proposed text.");
            texts = ReadTextResources(sourcePath);
            titles = change.NetworkTitles?.Select(title => texts.Add(title, cultures)).ToArray();
            comments = change.Comments.ToDictionary(item => item.Key, item => texts.Add(item.Value, cultures));
        }
        var result = PlcFbdSource.Append(source, change.Networks, change.Statics, change.Temps, titles, comments, language);
        return (result.Source, result.AddedNetworks, result.ExistingNetworks, texts);
    }

    private static PlcFbdResources ReadTextResources(string sourcePath)
    {
        var resources = new FileInfo(Path.ChangeExtension(sourcePath, ".s7res"));
        if (resources.Exists && resources.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Exported source resources exceed 4 MiB.");
        return new PlcFbdResources(resources.Exists ? File.ReadAllText(resources.FullName) : null);
    }

    private static object DescribeLocal((string Name, string Type) local, IReadOnlyDictionary<string, string> comments) =>
        comments.TryGetValue(local.Name, out string? comment)
            ? new { name = local.Name, dataType = local.Type, comment }
            : new { name = local.Name, dataType = local.Type };

    private static string[] ActiveCultures(ProjectBase project) =>
        project.LanguageSettings.ActiveLanguages.Select(language => language.Culture.Name)
            .OrderBy(culture => culture, StringComparer.Ordinal).ToArray();

    private static void ImportAndVerify(FbdImport import)
    {
        var result = import.Group.Blocks.ImportFromDocuments(import.Documents.Directory, import.Name, ImportDocumentOptions.Override);
        if (result.State != DocumentResultState.Success)
            throw new FbdImportException("TIA Portal rejected the SIMATIC SD import (" + result.State + ").");
        if (result.ImportedPlcBlocks.Count != 1)
            throw new FbdImportException("The successful import returned " + result.ImportedPlcBlocks.Count + " blocks; expected one.");
        var block = import.Group.Blocks.Find(import.Name);
        if (block == null || block.Number != import.Number || block.ProgrammingLanguage.ToString() != "FBD")
            throw new FbdImportException("The imported block's name, number, or programming language did not match the plan.");

        using var readback = new PlcSourceWorkspace();
        var source = ReadSource(block, readback);
        var actualTexts = import.TextResources == null ? null : ReadTextResources(source.Path);
        if (!PlcFbdSource.Equivalent(import.Source, source.Text, import.TextResources, actualTexts) ||
            Fingerprint(readback, resourcesOnly: true, excludeTextResources: actualTexts != null) != import.Resources ||
            (actualTexts != null && !import.TextResources!.HasSameTexts(actualTexts)))
            throw new FbdImportException("The re-exported source or resources did not match the plan.");
        var actualEno = PlcFbdSource.ReadExportedConverterEno(source.Text);
        if (actualEno.Length != import.ConverterEno.Length ||
            !actualEno.Zip(import.ConverterEno, (actual, expected) => actual.SequenceEqual(expected)).All(equal => equal))
            throw new FbdImportException("The S_CONV ENO settings did not match the plan.");
    }

    private static bool[][] ReadConverterEno(PlcBlock block)
    {
        XElement? exported = PlcInspectionTools.ReadExport(file => block.Export(file, ExportOptions.None), "SW.Blocks.");
        var units = exported?.Element("ObjectList")?.Elements("SW.Blocks.CompileUnit");
        if (units == null)
            throw new FbdImportException("SimaticML is unavailable for verifying conversion settings.");
        return units.Select(unit =>
        {
            var attributes = unit.Element("AttributeList");
            if ((string?)attributes?.Element("ProgrammingLanguage") != "FBD") return Array.Empty<bool>();
            var graph = attributes?.Element("NetworkSource")?.Elements()
                .SingleOrDefault(element => element.Name.LocalName == "FlgNet");
            return graph?.Descendants(graph.Name.Namespace + "Part")
                .Where(element => (string?)element.Attribute("Name") == "S_CONV")
                .Select(part => !string.Equals((string?)part.Attribute("DisabledENO"), "true", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<bool>();
        }).ToArray();
    }

    private static string Fingerprint(PlcSourceWorkspace documents, bool resourcesOnly = false, bool excludeTextResources = false)
    {
        using var hash = SHA256.Create();
        var files = documents.Directory.EnumerateFiles()
            .Where(file => !resourcesOnly || !file.Extension.Equals(".s7dcl", StringComparison.OrdinalIgnoreCase))
            .Where(file => !excludeTextResources || !file.Extension.Equals(".s7res", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal);
        return string.Join("|", files.Select(file =>
        {
            if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Exported source resources exceed 4 MiB.");
            using var stream = file.OpenRead();
            return file.Name + ":" + Convert.ToBase64String(hash.ComputeHash(stream));
        }));
    }

    private sealed class FbdPlan(Guid id, ProjectBase project, string softwarePath, FbdTarget[] targets, string[] cultures, string language)
    {
        internal readonly Guid Id = id;
        internal readonly ProjectBase Project = project;
        internal readonly string SoftwarePath = softwarePath;
        internal readonly FbdTarget[] Targets = targets;
        internal readonly string[] Cultures = cultures;
        internal readonly string Language = language;
    }

    private sealed class FbdTarget(PlcFbdChange change, int number, string fingerprint, string addedNetworks,
        int existingNetworks, object inputReview)
    {
        internal readonly PlcFbdChange Change = change;
        internal readonly int Number = number;
        internal readonly string Fingerprint = fingerprint;
        internal readonly string AddedNetworks = addedNetworks;
        internal readonly int ExistingNetworks = existingNetworks;
        internal readonly object InputReview = inputReview;
    }

    private sealed class FbdImport(string blockPath, string name, int number, PlcBlockGroup group,
        PlcSourceWorkspace documents, string source, string resources, bool[][] converterEno, PlcFbdResources? textResources)
    {
        internal readonly string BlockPath = blockPath;
        internal readonly string Name = name;
        internal readonly int Number = number;
        internal readonly PlcBlockGroup Group = group;
        internal readonly PlcSourceWorkspace Documents = documents;
        internal readonly string Source = source;
        internal readonly string Resources = resources;
        internal readonly bool[][] ConverterEno = converterEno;
        internal readonly PlcFbdResources? TextResources = textResources;
    }

    private sealed class FbdImportException(string message) : Exception(message) { }
}
