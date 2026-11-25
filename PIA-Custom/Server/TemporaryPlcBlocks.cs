using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

internal sealed class TemporaryPlcBlocks
{
    internal const string FolderName = "--- PIA Temp ---";
    internal const string GuardName = TemporaryPlcGuard.BlockName;

    private readonly PlcBlockUserGroup group;
    private readonly PlcBlock guard;
    private readonly DateTime guardModifiedDate;
    private readonly ExclusiveAccess access;
    private readonly ITransactionSupport project;
    private readonly List<PlcBlock> ownedCopies;

    private TemporaryPlcBlocks(PlcBlockUserGroup group, PlcBlock guard, ExclusiveAccess access,
        ITransactionSupport project, List<PlcBlock> ownedCopies)
    {
        this.group = group;
        this.guard = guard;
        this.access = access;
        this.project = project;
        this.ownedCopies = ownedCopies;
        guardModifiedDate = guard.ModifiedDate;
    }

    internal static object? Verify(PlcSoftware plc, ExclusiveAccess access, ITransactionSupport project,
        Func<TemporaryPlcBlocks, object?> verifyCopies)
    {
        var groups = plc.BlockGroup.Groups;
        var group = groups.Find(FolderName);
        bool createdGroup = group == null;
        if (group != null && (group.Blocks.Count != 0 || group.Groups.Count != 0))
            return new { status = "temporaryFolderOccupied", folder = FolderName };

        using var files = new PlcSourceWorkspace();
        var guardFile = new FileInfo(Path.Combine(files.Directory.FullName, "guard.xml"));
        TemporaryPlcGuard.Document().Save(guardFile.FullName);

        PlcBlock? guard = null;
        var ownedCopies = new List<PlcBlock>();
        object? failure = null;
        object? cleanupFailure = null;
        string stage = "createFolder";
        try
        {
            using (var setup = access.Transaction(project, "Create temporary PLC compile guard"))
            {
                group ??= groups.Create(FolderName);
                stage = "importGuard";
                var imported = group.Blocks.Import(guardFile, ImportOptions.None);
                guard = imported.Count == 1 ? imported[0] : null;
                if (guard is FB && Equals(group.Blocks.Find(GuardName), guard) && setup.CanCommit)
                {
                    stage = "commitGuard";
                    setup.CommitOnDispose();
                }
                else
                    failure = new { status = "temporaryGuardUnavailable", folder = FolderName };
            }

            if (failure == null)
            {
                stage = "verifyGuard";
                var compilation = Core.Portal.CompileSoftware(plc.Name, "");
                if (compilation.ErrorCount > 0 && HasGuardError(compilation.Messages))
                {
                    stage = "verifyCopies";
                    failure = verifyCopies(new TemporaryPlcBlocks(group!, guard!, access, project, ownedCopies));
                }
                else
                {
                    failure = new { status = "temporaryGuardUnverified", folder = FolderName, block = GuardName,
                        compilation.ErrorCount, state = compilation.State.ToString() };
                }
            }
        }
        catch (Exception ex) when (ex is EngineeringException || ex is InvalidOperationException || ex is IOException ||
            ex is FormatException || ex is ModelContextProtocol.McpException || ex is TiaMcpServer.Siemens.PortalException)
        {
            failure = new { status = "temporaryVerificationFailed", folder = FolderName, stage, reason = ex.GetType().Name };
        }
        finally
        {
            cleanupFailure = Cleanup(access, project, plc, guard, createdGroup, ownedCopies);
        }

        return cleanupFailure ?? failure;
    }

    internal PlcBlock Import(FileInfo file, string blockName)
    {
        var currentGuard = group.Blocks.Find(GuardName);
        if (!Equals(currentGuard, guard) || guard.ModifiedDate != guardModifiedDate)
            throw new InvalidOperationException("The verified temporary PLC compile guard changed.");
        if (blockName == GuardName || group.Blocks.Find(blockName) != null)
            throw new InvalidOperationException("The temporary block must have a new name.");

        using var transaction = access.Transaction(project, "Import temporary PLC block copy");
        var imported = group.Blocks.Import(file, ImportOptions.None);
        if (imported.Count != 1 || !Equals(group.Blocks.Find(blockName), imported[0]) || !transaction.CanCommit)
            throw new InvalidOperationException("The temporary block import did not preserve the requested identity.");
        ownedCopies.Add(imported[0]);
        transaction.CommitOnDispose();
        return imported[0];
    }

    private static bool HasGuardError(IEnumerable<CompilerResultMessage> messages, bool insideGuard = false)
    {
        foreach (var message in messages)
        {
            bool matches = insideGuard || IdentifiesGuard(message.Path) || IdentifiesGuard(message.Description);
            if (matches && (message.State == CompilerResultState.Error || message.ErrorCount > 0)) return true;
            if (HasGuardError(message.Messages, matches)) return true;
        }
        return false;
    }

    private static bool IdentifiesGuard(string? text) => text != null && Regex.IsMatch(text,
        @"\b(?:" + GuardName + "|" + TemporaryPlcGuard.MissingVariable + @")\b", RegexOptions.CultureInvariant);

    private static object? Cleanup(ExclusiveAccess access, ITransactionSupport project, PlcSoftware plc, PlcBlock? guard,
        bool createdGroup, List<PlcBlock> ownedCopies)
    {
        var groups = plc.BlockGroup.Groups;
        try
        {
            var group = groups.Find(FolderName);
            if (group == null) return null;
            using (var cleanup = access.Transaction(project, "Remove temporary PLC blocks and compile guard"))
            {
                var current = group.Blocks.ToArray();
                for (int index = ownedCopies.Count - 1; index >= 0; index--)
                    current.FirstOrDefault(block => Equals(block, ownedCopies[index]))?.Delete();

                if (group.Groups.Count == 0 && group.Blocks.All(block => Equals(block, guard)))
                {
                    var currentGuard = group.Blocks.Find(GuardName);
                    if (Equals(currentGuard, guard)) currentGuard?.Delete();
                    if (createdGroup && group.Blocks.Count == 0) group.Delete();
                }
                if (cleanup.CanCommit) cleanup.CommitOnDispose();
            }

            group = groups.Find(FolderName);
            return group == null || !createdGroup && group.Blocks.Count == 0 && group.Groups.Count == 0 ? null : CleanupFailure(group);
        }
        catch (Exception ex) when (ex is EngineeringException || ex is InvalidOperationException)
        {
            return new { status = "temporaryCleanupFailed", folder = FolderName, reason = ex.GetType().Name };
        }
    }

    private static object CleanupFailure(PlcBlockUserGroup group) => new
    {
        status = "temporaryCleanupFailed", folder = FolderName, blockCount = group.Blocks.Count, groupCount = group.Groups.Count,
        remainingBlocks = group.Blocks.Select(block => block.Name).Take(20).ToArray(),
        remainingGroups = group.Groups.Select(child => child.Name).Take(20).ToArray()
    };
}
