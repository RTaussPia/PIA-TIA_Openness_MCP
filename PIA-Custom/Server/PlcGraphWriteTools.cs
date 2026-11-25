using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

public static partial class PlcGraphTools
{
    private static readonly Regex FlagName = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);
    private static GraphPlan? pendingGraphPlan;

    [McpServerTool(Name = "PlanPlcGraphStepFlag"), Description("Preview adding one static BOOL and a GRAPH post-operation for an inclusive step range. Requires a user-confirmed selection for branched ranges. Read-only.")]
    public static object PlanPlcGraphStepFlag(
        [Description("Exact PLC software name.")] string softwarePath,
        [Description("Exact GRAPH function block path.")] string blockPath,
        [Description("One-based sequence index.")] int sequence,
        [Description("Number of the first step.")] int firstStep,
        [Description("Number of the last step, included in the range.")] int lastStep,
        [Description("New static BOOL name, up to 64 ASCII characters.")] string flag,
        [Description("FBD post-operation title, up to 120 characters.")] string title,
        [Description("New BOOL declaration comment, up to 500 characters.")] string comment,
        [Description("For a branched range, exact step numbers approved by the user, including the endpoints and shared steps. Omit until confirmed.")] int[]? branchSteps = null)
        => PlanGraphFlag(softwarePath, blockPath, sequence, firstStep, lastStep, flag, comment, branchSteps, false, title);

    [McpServerTool(Name = "PlanPlcGraphStepFlagActions"), Description("Preview a new BOOL and one N action in each selected GRAPH step. Set dataBlockPath to declare the BOOL in a global data block. Read-only.")]
    public static object PlanPlcGraphStepFlagActions(
        [Description("Exact PLC software name.")] string softwarePath,
        [Description("Exact GRAPH function block path.")] string blockPath,
        [Description("One-based sequence index.")] int sequence,
        [Description("Number of the first step.")] int firstStep,
        [Description("Number of the last step, included in the range.")] int lastStep,
        [Description("New BOOL name, up to 64 ASCII characters.")] string flag,
        [Description("New BOOL declaration comment, up to 500 characters.")] string comment,
        [Description("For a branched range, exact step numbers approved by the user, including the endpoints and shared steps. Omit until confirmed.")] int[]? branchSteps = null,
        [Description("Optional exact path of an existing global DB on the same PLC. Omit for a static local BOOL.")] string? dataBlockPath = null,
        [Description("Optional dotted path of a locally declared structure inside the target global DB.")] string? dataMemberParent = null)
        => PlanGraphFlag(softwarePath, blockPath, sequence, firstStep, lastStep, flag, comment, branchSteps, true, null,
            dataBlockPath, dataMemberParent);

    private static object PlanGraphFlag(string softwarePath, string blockPath, int sequence, int firstStep, int lastStep,
        string flag, string comment, int[]? branchSteps, bool stepActions, string? title, string? dataBlockPath = null,
        string? dataMemberParent = null)
    {
        var validation = PlcInspectionTools.ValidateBlockPaths(softwarePath, blockPath);
        if (validation != null) return validation;
        dataBlockPath = dataBlockPath?.Trim();
        dataMemberParent = dataMemberParent?.Trim();
        string[] parentPath = string.IsNullOrEmpty(dataMemberParent) ? Array.Empty<string>() : dataMemberParent!.Split('.');
        if ((dataBlockPath != null && PlcInspectionTools.ValidateBlockPaths(softwarePath, dataBlockPath) != null) ||
            (dataBlockPath == null && parentPath.Length > 0) || dataMemberParent?.Length > 512 || parentPath.Length > 16 ||
            parentPath.Any(part => string.IsNullOrWhiteSpace(part) || part.Any(char.IsControl) || part.IndexOfAny(new[] { '/', '\\', '[', ']', '"' }) >= 0))
            return new { status = "invalidDataBlockTarget" };
        if (sequence < 1 || firstStep < 0 || lastStep < 0 || flag == null || !FlagName.IsMatch(flag) ||
            (!stepActions && !ValidGraphText(title, 120)) || !ValidGraphText(comment, 500) ||
            (branchSteps != null && (branchSteps.Length == 0 || branchSteps.Length > 64 || branchSteps.Any(step => step < 0))))
            return new { status = "invalidChange", reason = stepActions ? "Provide a sequence and step range, a new BOOL name, and a comment." :
                "Provide a sequence and step range, a new BOOL name, a title, and a comment." };

        return ProjectSession.WithOpenProject(project =>
        {
            var saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;
            return PlcInspectionTools.WithOpenBlock(project, softwarePath, blockPath, (plc, block, path) =>
            {
                var check = CheckWritableGraphBlock(block, path);
                if (check != null) return check;
                try
                {
                    var original = ReadGraphDocument(block);
                    var changed = new XDocument(original);
                    var item = BlockItem(changed);
                    var graph = PlcGraphReader.FindGraph(item) ?? throw new FormatException("No unique GRAPH body was exported.");
                    var selected = PlcGraphFlagChange.ReadRange(graph, sequence, firstStep, lastStep, branchSteps);
                    var languages = GraphLanguages(project);
                    DataFlagPlan? data = null;
                    if (dataBlockPath != null)
                    {
                        object outcome = PlcInspectionTools.WithOpenBlock(project, plc.Name, dataBlockPath, (_, db, dbPath) =>
                        {
                            if (!WritableDataBlock(db)) return new { status = "unsupportedDataBlockWriteTarget", blockPath = dbPath };
                            var originalDb = ReadGraphDocument(db);
                            var changedDb = new XDocument(originalDb);
                            var memberPath = PlcGraphFlagChange.AppendDataFlagDeclaration(BlockItem(changedDb, true), parentPath,
                                flag, comment, languages);
                            var target = new BlockChange(dbPath, db.Number, BlockItem(originalDb, true), changedDb, true);
                            return new DataFlagPlan(target, db.Name, memberPath);
                        });
                        if (!(outcome is DataFlagPlan scopedData)) return outcome;
                        data = scopedData;
                        PlcGraphFlagChange.AppendGlobalStepActions(graph, sequence, selected, data.Name, data.MemberPath);
                    }
                    else if (stepActions)
                        PlcGraphFlagChange.AppendStepActions(item, graph, sequence, selected, flag, comment, languages);
                    else
                        PlcGraphFlagChange.Append(item, graph, selected, flag, title!, comment, languages);
                    saved = ProjectSession.CheckSaved(project, false);
                    if (saved != null) return saved;
                    var plan = new GraphPlan(Guid.NewGuid(), project, plc.Name, languages,
                        new BlockChange(path, block.Number, BlockItem(original), changed, false), stepActions, data, selected.Length);
                    pendingGraphPlan = plan;
                    var declaration = new { name = flag, dataType = "Bool", section = "Static", comment };
                    if (data != null)
                    {
                        string operand = "\"" + data.Name + "\"." + string.Join(".", data.MemberPath);
                        return new
                        {
                            status = "preview", planId = plan.Id, project = project.Name, softwarePath = plc.Name, blockPath = path, sequence,
                            declaration = new { name = flag, dataType = "Bool", startValue = false, comment,
                                blockPath = data.Change.Path, memberPath = string.Join(".", data.MemberPath), operand },
                            instanceInterfaceChanges = false, languages,
                            steps = selected.Select(step => new { step.Number, step.Name, action = new { qualifier = "N", operand } }).ToArray()
                        };
                    }
                    if (stepActions)
                        return new
                        {
                            status = "preview", planId = plan.Id, project = project.Name, softwarePath = plc.Name, blockPath = path,
                            sequence, declaration, instanceInterfaceChanges = true, languages,
                            steps = selected.Select(step => new { step.Number, step.Name, action = new { qualifier = "N", operand = "#" + flag } }).ToArray()
                        };
                    return new
                    {
                        status = "preview", planId = plan.Id, project = project.Name, softwarePath = plc.Name, blockPath = path,
                        sequence, steps = selected.Select(step => new { step.Number, step.Name }).ToArray(),
                        declaration,
                        operation = new { phase = "after", title, expression = flag + " := " + string.Join(" OR ", selected.Select(step => step.Name + ".X")) },
                        instanceInterfaceChanges = true, languages
                    };
                }
                catch (PlcGraphFlagChange.BranchSelectionRequired ex)
                {
                    return new { status = "branchSelectionRequired", blockPath = path, sequence,
                        branches = ex.Branches, candidateSteps = ex.CandidateSteps, complete = ex.Complete, reason = ex.Message };
                }
                catch (FormatException ex)
                {
                    return new { status = "unsupportedGraphChange", blockPath = path, reason = ex.Message };
                }
            });
        });
    }

    [McpServerTool(Name = "ApplyPlcGraphStepFlag"), Description("Apply the exact approved GRAPH flag preview to a saved offline project. Rolls back if the GRAPH readback differs from the plan.")]
    public static object ApplyPlcGraphStepFlag(
        [Description("ID returned by PlanPlcGraphStepFlag in this MCP session.")] string planId,
        [Description("True only after the user has approved this exact preview.")] bool approved)
        => ApplyGraphFlag(planId, approved, false);

    [McpServerTool(Name = "ApplyPlcGraphStepFlagActions"), Description("Apply the approved step-action plan to a saved offline project and roll back if the GRAPH readback differs.")]
    public static object ApplyPlcGraphStepFlagActions(
        [Description("ID returned by PlanPlcGraphStepFlagActions in this MCP session.")] string planId,
        [Description("True only after the user has approved this exact preview.")] bool approved)
        => ApplyGraphFlag(planId, approved, true);

    private static object ApplyGraphFlag(string planId, bool approved, bool stepActions)
    {
        if (!approved) return new { status = "approvalRequired" };
        var plan = pendingGraphPlan;
        if (plan == null || plan.StepActions != stepActions || !Guid.TryParseExact(planId, "D", out var requestedId) || plan.Id != requestedId)
            return new { status = "planNotFound" };

        return ProjectSession.WithOpenProject(project =>
        {
            if (!project.Equals(plan.Project)) return new { status = "projectChanged" };
            if (!(project.Parent is TiaPortal portal) || !(project is ITransactionSupport transactionProject))
                return new { status = "transactionUnavailable" };
            using var access = portal.ExclusiveAccess("Apply approved GRAPH step flag");
            var saved = ProjectSession.CheckSaved(project, false);
            if (saved != null) return saved;
            if (Core.Portal.GetOnlineState(plan.SoftwarePath).State != "Offline") return new { status = "offlineRequired" };
            if (!GraphLanguages(project).SequenceEqual(plan.Languages)) return new { status = "stalePlan" };
            var targets = new List<PreparedBlock>();
            using (var workspace = new PlcSourceWorkspace())
            {
                var changes = plan.Data == null ? new[] { plan.Graph } : new[] { plan.Data.Change, plan.Graph };
                foreach (var change in changes)
                {
                    object prepared = PlcInspectionTools.WithOpenBlock(project, plan.SoftwarePath, change.Path, (plc, block, path) =>
                    {
                        var check = CheckWritableTarget(block, change, path);
                        if (check != null) return check;
                        var originalDocument = ReadGraphDocument(block);
                        if (block.Number != change.Number || !PlcGraphFlagChange.SameBlock(change.Original, BlockItem(originalDocument, change.IsDataBlock)))
                            return new { status = "stalePlan", blockPath = path };
                        var file = new FileInfo(Path.Combine(workspace.Directory.FullName, change.IsDataBlock ? "flag.xml" : "graph.xml"));
                        change.Changed.Save(file.FullName);
                        return new PreparedBlock(change, plc, (PlcBlockGroup)block.Parent, block.Name, file, originalDocument);
                    });
                    if (!(prepared is PreparedBlock target)) return prepared;
                    targets.Add(target);
                }
                pendingGraphPlan = null;
                var controlAdditions = new List<PlcGraphImportComparison.CommentAddition>();
                if (plan.StepActions)
                {
                    var verification = TemporaryPlcBlocks.Verify(targets[0].Software, access, transactionProject,
                        temporary => VerifyTemporaryCopies(temporary, plan, targets, workspace, controlAdditions));
                    if (verification != null) return verification;
                    foreach (var target in targets)
                    {
                        string? difference = ReadbackDifference(target, target.Change.Original);
                        if (difference != null)
                            return new { status = "stalePlan", blockPath = target.Change.Path, reason = difference };
                    }
                }
                bool committing = false;
                string stage = "import";
                string affectedPath = plan.Graph.Path;
                string? failure = null;
                string? reason = null;
                try
                {
                    using var transaction = access.Transaction(transactionProject, "Append approved GRAPH step flag");
                    foreach (var target in targets)
                    {
                        affectedPath = target.Change.Path;
                        var imported = target.Group.Blocks.Import(target.File, ImportOptions.Override);
                        var updated = target.Group.Blocks.Find(target.Name);
                        if (imported.Count == 1 && updated != null && updated.Equals(imported[0])) continue;
                        failure = "readbackMismatch";
                        break;
                    }
                    stage = "readback";
                    if (failure == null)
                    {
                        foreach (var target in targets)
                        {
                            affectedPath = target.Change.Path;
                            reason = ReadbackDifference(target, BlockItem(target.Change.Changed, target.Change.IsDataBlock),
                                target.Change.IsDataBlock ? null : controlAdditions);
                            if (reason == null) continue;
                            failure = "readbackMismatch";
                            break;
                        }
                        if (failure == null && !transaction.CanCommit) failure = "transactionRejected";
                        if (failure == null)
                        {
                            committing = true;
                            transaction.CommitOnDispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = committing ? "commitUnverified" : "applyFailed";
                    reason = ex.GetType().Name;
                }
                if (failure != null)
                {
                    bool restored = false;
                    try
                    {
                        restored = targets.All(target => ReadbackDifference(target, target.Change.Original) == null);
                    }
                    catch (Exception ex) when (ex is EngineeringException || ex is IOException || ex is XmlException || ex is FormatException)
                    {
                        reason ??= ex.GetType().Name;
                    }
                    return new { status = restored ? failure : "rollbackUnverified", failure, blockPath = affectedPath, stage, reason, rollbackVerified = restored };
                }
                return new { status = "applied", project = project.Name, blockPath = plan.Graph.Path, verified = true, compiled = false, saved = false };
            }
        });
    }

    private static object? VerifyTemporaryCopies(TemporaryPlcBlocks temporary, GraphPlan plan, List<PreparedBlock> targets,
        PlcSourceWorkspace workspace, List<PlcGraphImportComparison.CommentAddition> controlAdditions)
    {
        string suffix = Guid.NewGuid().ToString("N");
        var graphTarget = targets.Single(target => !target.Change.IsDataBlock);
        string controlName = "PiaTempControl_" + suffix;
        var control = CopyForTemporaryImport(graphTarget.OriginalDocument, controlName, false);
        var controlBlock = ImportTemporaryCopy(temporary, workspace, control, controlName);
        if (controlBlock == null)
            return new { status = "temporaryCopyCompileFailed", blockPath = graphTarget.Change.Path };
        var actualControl = BlockItem(ReadGraphDocument(controlBlock));
        var originalGraph = BlockItem(graphTarget.OriginalDocument);
        controlAdditions.AddRange(PlcGraphImportComparison.FindCommentAdditions(originalGraph, actualControl));
        string? controlDifference = PlcGraphImportComparison.Difference(originalGraph, actualControl, true, controlAdditions);
        if (controlDifference != null)
            return new { status = "temporaryReadbackMismatch", blockPath = graphTarget.Change.Path, reason = controlDifference };

        string temporaryDbName = "PiaTempDb_" + suffix;
        foreach (var target in targets)
        {
            bool isDataBlock = target.Change.IsDataBlock;
            string name = isDataBlock ? temporaryDbName : "PiaTempGraph_" + suffix;
            var document = CopyForTemporaryImport(target.Change.Changed, name, isDataBlock);
            var expected = BlockItem(document, isDataBlock);
            if (!isDataBlock && plan.Data != null)
            {
                var graph = PlcGraphReader.FindGraph(expected) ?? throw new FormatException("The temporary copy has no unique GRAPH body.");
                if (PlcGraphFlagChange.RetargetGlobalStepActions(graph, plan.Data.Name, plan.Data.MemberPath, temporaryDbName) != plan.StepCount)
                    return new { status = "temporaryReadbackMismatch", blockPath = target.Change.Path, reason = "The approved actions could not be redirected to the temporary data block." };
            }
            var imported = ImportTemporaryCopy(temporary, workspace, document, name);
            if (imported == null) return new { status = "temporaryCopyCompileFailed", blockPath = target.Change.Path };
            var actual = BlockItem(ReadGraphDocument(imported), isDataBlock);
            string? difference = PlcGraphImportComparison.Difference(expected, actual, true, isDataBlock ? null : controlAdditions);
            if (difference != null)
                return new { status = "temporaryReadbackMismatch", blockPath = target.Change.Path, reason = difference };
        }
        return null;
    }

    private static XDocument CopyForTemporaryImport(XDocument source, string name, bool isDataBlock)
    {
        var document = new XDocument(source);
        var attributes = BlockItem(document, isDataBlock).Element("AttributeList") ?? throw new FormatException("The exported block has no attribute list.");
        attributes.SetElementValue("Name", name);
        attributes.Element("Number")?.Remove();
        attributes.Element("AutoNumber")?.Remove();
        return document;
    }

    private static PlcBlock? ImportTemporaryCopy(TemporaryPlcBlocks temporary, PlcSourceWorkspace workspace, XDocument document, string name)
    {
        var file = new FileInfo(Path.Combine(workspace.Directory.FullName, name + ".xml"));
        document.Save(file.FullName);
        var imported = temporary.Import(file, name);
        if (imported.IsConsistent) return imported;
        var compilation = imported.GetService<ICompilable>()?.Compile();
        return compilation?.ErrorCount == 0 && imported.IsConsistent ? imported : null;
    }

    private static string? ReadbackDifference(PreparedBlock target, XElement expected,
        IReadOnlyList<PlcGraphImportComparison.CommentAddition>? controlAdditions = null)
    {
        var current = target.Group.Blocks.Find(target.Name);
        if (current == null || current.Number != target.Change.Number) return "The imported block's identity differs from the plan.";
        if (!current.IsConsistent) return "TIA Portal marked the imported block inconsistent.";
        if (CheckWritableTarget(current, target.Change, target.Change.Path) != null) return "The imported block is unavailable for verification.";
        var actual = BlockItem(ReadGraphDocument(current), target.Change.IsDataBlock);
        return PlcGraphImportComparison.Difference(expected, actual, controlAdditions: controlAdditions);
    }

    private static object? CheckWritableTarget(PlcBlock block, BlockChange change, string path) => change.IsDataBlock
        ? WritableDataBlock(block) ? null : new { status = "unsupportedDataBlockWriteTarget", blockPath = path }
        : CheckWritableGraphBlock(block, path);

    private static bool WritableDataBlock(PlcBlock block) => block is GlobalDB db && db.IsConsistent && !db.IsKnowHowProtected &&
        db.Parent is PlcBlockGroup && db.GetService<LibraryTypeInstanceInfo>() == null;

    private static object? CheckWritableGraphBlock(PlcBlock block, string path)
    {
        var check = CheckGraphBlock(block, path);
        if (check != null) return check;
        return !(block.Parent is PlcBlockGroup) || ((FB)block).GetService<LibraryTypeInstanceInfo>() != null
            ? new { status = "unsupportedGraphWriteTarget", blockPath = path } : null;
    }

    private static bool ValidGraphText(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text!.Length <= limit && !text.Any(char.IsControl);

    private static string[] GraphLanguages(ProjectBase project) => project.LanguageSettings.ActiveLanguages
        .Select(language => language.Culture.Name).OrderBy(language => language, StringComparer.Ordinal).ToArray();

    private static XDocument ReadGraphDocument(PlcBlock block)
    {
        using var workspace = new PlcSourceWorkspace();
        var file = new FileInfo(Path.Combine(workspace.Directory.FullName, "graph.xml"));
        block.Export(file, ExportOptions.None);
        using var reader = XmlReader.Create(file.FullName, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024
        });
        return XDocument.Load(reader);
    }

    private static XElement BlockItem(XDocument document, bool isDataBlock = false) => document.Root?.Elements(
        isDataBlock ? "SW.Blocks.GlobalDB" : "SW.Blocks.FB").SingleOrDefault() ??
        throw new FormatException("The export does not contain exactly one block of the expected type.");

    private sealed class GraphPlan(Guid id, ProjectBase project, string softwarePath, string[] languages,
        BlockChange graph, bool stepActions, DataFlagPlan? data, int stepCount)
    {
        internal Guid Id { get; } = id;
        internal ProjectBase Project { get; } = project;
        internal string SoftwarePath { get; } = softwarePath;
        internal string[] Languages { get; } = languages;
        internal BlockChange Graph { get; } = graph;
        internal bool StepActions { get; } = stepActions;
        internal DataFlagPlan? Data { get; } = data;
        internal int StepCount { get; } = stepCount;
    }

    private sealed class BlockChange(string path, int number, XElement original, XDocument changed, bool isDataBlock)
    {
        internal string Path { get; } = path;
        internal int Number { get; } = number;
        internal XElement Original { get; } = original;
        internal XDocument Changed { get; } = changed;
        internal bool IsDataBlock { get; } = isDataBlock;
    }

    private sealed class DataFlagPlan(BlockChange change, string name, string[] memberPath)
    {
        internal BlockChange Change { get; } = change;
        internal string Name { get; } = name;
        internal string[] MemberPath { get; } = memberPath;
    }

    private sealed class PreparedBlock(BlockChange change, PlcSoftware software, PlcBlockGroup group, string name, FileInfo file, XDocument originalDocument)
    {
        internal BlockChange Change { get; } = change;
        internal PlcSoftware Software { get; } = software;
        internal PlcBlockGroup Group { get; } = group;
        internal string Name { get; } = name;
        internal FileInfo File { get; } = file;
        internal XDocument OriginalDocument { get; } = originalDocument;
    }
}
