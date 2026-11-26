using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW.Blocks;

namespace Pia.Custom;

public static partial class PlcSourceTools
{
    private static object? VerifyTemporaryStaticAttributes(TemporaryPlcBlocks temporary, IEnumerable<FbdImport> imports)
    {
        foreach (var import in imports)
        {
            string name = "PiaTempFbd_" + Guid.NewGuid().ToString("N");
            using var files = new PlcSourceWorkspace();
            string copySource = PlcFbdSource.CopyBlockWithAutomaticNumber(import.Source, name);
            string document = Path.Combine(files.Directory.FullName, name + ".s7dcl");
            File.WriteAllText(document, copySource, new UTF8Encoding(true, true));
            var originalResources = new FileInfo(Path.Combine(import.Documents.Directory.FullName, import.Name + ".s7res"));
            if (originalResources.Exists) originalResources.CopyTo(Path.ChangeExtension(document, ".s7res"));

            var copy = temporary.ImportDocuments(files, name);
            using var uncompiledExport = new PlcSourceWorkspace();
            string uncompiled = ReadSource(copy, uncompiledExport).Text;
            var syntax = PlcFbdSource.ReadStaticAttributeSyntax(uncompiled, import.NewStatics);
            if (syntax.Count != import.NewStatics.Length)
                return new { status = "temporaryReadbackMismatch", blockPath = import.BlockPath,
                    reason = "Some planned static variables are missing from the temporary SIMATIC SD export." };

            if (!copy.IsConsistent)
            {
                var result = copy.GetService<ICompilable>()?.Compile();
                if (result == null || result.ErrorCount != 0 || !copy.IsConsistent)
                    return new { status = "temporaryCopyCompileFailed", blockPath = import.BlockPath,
                        errorCount = result?.ErrorCount, reason = result == null ? null : FirstCompileError(result.Messages) };
            }

            var sections = ReadStaticInterface(copy);
            foreach (string variable in import.NewStatics)
            {
                var readback = PlcStaticAttributes.ReadValues(sections, variable);
                if (readback != import.StaticAttributes!.Values)
                    return new { status = "temporaryReadbackMismatch", blockPath = import.BlockPath,
                        reason = $"The compiled temporary copy did not confirm the four approved attributes of '{variable}'." };
            }

            var copyTexts = import.TextResources == null ? null : ReadTextResources(Path.Combine(uncompiledExport.Directory.FullName, name + ".s7dcl"));
            var difference = PlcFbdSource.ReadbackDifference(
                PlcFbdSource.ExcludeVerifiedStaticAttributes(copySource, import.NewStatics),
                PlcFbdSource.ExcludeVerifiedStaticAttributes(PlcFbdSource.CopyBlockWithAutomaticNumber(uncompiled, name), import.NewStatics),
                import.TextResources, copyTexts);
            if (difference != null)
                return new { status = "temporaryReadbackMismatch", blockPath = import.BlockPath, reason = difference };

            import.VerifiedStaticSyntax = syntax;
        }

        return null;
    }

    private static string? FirstCompileError(IEnumerable<CompilerResultMessage> messages)
    {
        foreach (var message in messages)
        {
            var nested = FirstCompileError(message.Messages);
            if (nested != null) return nested;
            if (message.State != CompilerResultState.Error || string.IsNullOrWhiteSpace(message.Description)) continue;
            return string.Concat(message.Description.Take(300).Select(character => char.IsControl(character) ? ' ' : character));
        }
        return null;
    }
}
