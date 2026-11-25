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
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Core = TiaMcpServer.ModelContextProtocol.McpServer;

namespace Pia.Custom;

public static class PlcInspectionTools
{
    [McpServerTool(Name = "GetPlcBlockInterface"), Description("Read a PLC block's declared members or resolve one nested member through its referenced UDTs. Read-only.")]
    public static object GetPlcBlockInterface(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath,
        [Description("Exact program-block path, relative to the program-block root.")] string blockPath,
        [Description("Optional dotted member path such as SequenceInfo.Process.StepNo. Indexed paths are unsupported.")] string memberPath = "",
        [Description("Optional interface section such as Input, Output, InOut, Static, or Constant.")] string section = "",
        [Description("Zero-based offset for a declaration list.")] int offset = 0,
        [Description("Maximum declarations per page, from 1 to 100.")] int limit = 50)
    {
        memberPath = (memberPath ?? "").Trim();
        section = (section ?? "").Trim();
        var segments = memberPath.Length == 0 ? Array.Empty<string>() : memberPath.Split('.');
        if (memberPath.Length > 512 || segments.Length > 16 || segments.Any(s => string.IsNullOrWhiteSpace(s)) ||
            memberPath.IndexOfAny(new[] { '[', ']', '/' }) >= 0)
            return new { status = "invalidMemberPath" };
        if (section.Length > 64) return new { status = "invalidSection" };
        if (!ValidPage(offset, limit)) return new { status = "invalidPage" };

        return WithBlock(softwarePath, blockPath, (plc, block, path, item) =>
        {
            var sections = PlcInterfaceReader.InterfaceSections(item);
            if (sections == null) return new { status = "interfaceUnavailable", complete = false, blockPath = path };
            if (segments.Length > 0)
            {
                object member = PlcInterfaceReader.ResolveMember(sections, segments, section, type => ResolveType(plc, type));
                return new { status = "report", softwarePath = plc.Name, blockPath = path, member };
            }

            var declarations = PlcInterfaceReader.ReadDeclarations(sections, section).Skip(offset).Take(limit + 1).ToArray();
            return new { status = "report", softwarePath = plc.Name, blockPath = path,
                language = block.ProgrammingLanguage.ToString(), declarations = declarations.Take(limit).ToArray(),
                nextOffset = declarations.Length > limit ? offset + limit : (int?)null };
        });
    }

    [McpServerTool(Name = "GetPlcCallConnections"), Description("Read named block calls and pin wiring in LAD or FBD networks. For SCL use DescribeBlockLogic. Read-only.")]
    public static object GetPlcCallConnections(
        [Description("Exact PLC software name from GetProjectStructure.")] string softwarePath,
        [Description("Exact program-block path, relative to the program-block root.")] string blockPath,
        [Description("One-based network number, or 0 to inspect all networks.")] int network = 0,
        [Description("Optional exact name of the called FB or FC.")] string callName = "",
        [Description("Zero-based call offset.")] int offset = 0,
        [Description("Maximum calls per page, from 1 to 100.")] int limit = 20)
    {
        if (network < 0) return new { status = "invalidNetwork" };
        callName = (callName ?? "").Trim();
        if (callName.Length > 256) return new { status = "invalidCallName" };
        if (!ValidPage(offset, limit)) return new { status = "invalidPage" };

        return WithBlock(softwarePath, blockPath, (plc, block, path, item) =>
        {
            var unreadNetworks = new List<int>();
            var calls = PlcCallReader.Read(item, network, callName, unreadNetworks, out var networkCount);
            if (network > networkCount) return new { status = "networkNotFound", networkCount };
            return new { status = "report", softwarePath = plc.Name, blockPath = path,
                language = block.ProgrammingLanguage.ToString(), complete = unreadNetworks.Count == 0,
                unreadNetworks, networkCount, calls = calls.Skip(offset).Take(limit).ToArray(),
                nextOffset = offset + limit < calls.Count ? offset + limit : (int?)null };
        });
    }

    private static bool ValidPage(int offset, int limit) => offset >= 0 && offset <= int.MaxValue - 100 && limit >= 1 && limit <= 100;

    private static object WithBlock(string softwarePath, string blockPath, Func<PlcSoftware, PlcBlock, string, XElement, object> read)
    {
        var validation = ValidateBlockPaths(softwarePath, blockPath);
        if (validation != null) return validation;
        return ProjectSession.WithOpenProject(project => WithOpenBlock(project, softwarePath, blockPath, (plc, block, path) =>
        {
            if (!block.IsConsistent) return new { status = "blockInconsistent", softwarePath = plc.Name, blockPath = path };
            var exported = ReadExport(file => block.Export(file, ExportOptions.None), "SW.Blocks.");
            return exported == null ? new { status = "blockExportUnavailable", complete = false, blockPath = path } :
                read(plc, block, path, exported);
        }));
    }

    internal static object WithOpenBlock(ProjectBase project, string softwarePath, string blockPath,
        Func<PlcSoftware, PlcBlock, string, object> action)
    {
        softwarePath = softwarePath.Trim();
        blockPath = blockPath.Trim();

        var controllers = new List<PlcSoftware>();
        var issues = new List<string>();
        LibraryUsage.FindControllers(project, controllers, issues);
        var matches = controllers.Where(plc => plc.Name.Equals(softwarePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            return new { status = matches.Length == 0 ? "plcNotFound" : "ambiguousPlc",
                candidates = controllers.Select(plc => plc.Name).ToArray(), issues };
        var plc = matches[0];
        var block = Core.Portal.GetBlock(plc.Name, blockPath);
        if (block == null || !ExactBlockPath(plc, block, blockPath))
            return new { status = "blockNotFound", softwarePath = plc.Name, blockPath };
        return action(plc, block, blockPath);
    }

    internal static object? ValidateBlockPaths(string softwarePath, string blockPath)
    {
        softwarePath = (softwarePath ?? "").Trim();
        blockPath = (blockPath ?? "").Trim();
        if (softwarePath.Length == 0 || softwarePath.Length > 512) return new { status = "invalidSoftwarePath" };
        if (blockPath.Length == 0 || blockPath.Length > 2048 || blockPath.Split('/').Any(string.IsNullOrWhiteSpace))
            return new { status = "invalidBlockPath" };
        return null;
    }

    private static bool ExactBlockPath(PlcSoftware plc, PlcBlock block, string requested)
    {
        var fullName = LibraryUsage.ObjectPath(block, plc);
        if (fullName.Equals(plc.BlockGroup.Name + "/" + requested, StringComparison.OrdinalIgnoreCase))
            return true;

        // Software unit groups have another program-block root underneath the unit.
        var parts = requested.Split('/');
        if (parts.Length < 3 || !parts[0].Equals("Software units", StringComparison.OrdinalIgnoreCase))
            return false;
        var relative = string.Join("/", parts.Skip(2));
        return fullName.EndsWith("/" + parts[1] + "/" + plc.BlockGroup.Name + "/" + relative, StringComparison.OrdinalIgnoreCase);
    }

    private static (XElement? sections, string? issue) ResolveType(PlcSoftware plc, string declaration)
    {
        string typeName = declaration.Replace("\"", "");
        string unqualified = typeName.Split('.').Last();
        var types = Core.Portal.GetTypesWithPaths(plc.Name, "^" + Regex.Escape(unqualified) + "$", out var failure);
        if (types == null || failure != null) return (null, "The PLC type list could not be read completely.");
        var matches = types.Where(type => typeName.IndexOf('.') < 0
            ? type.Type.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
            : (type.Type.Namespace + "." + type.Type.Name).Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return (null, "The referenced type could not be resolved.");
        if (matches.Length > 1) return (null, "The referenced type is ambiguous.");
        var target = matches[0].Type;
        if (!target.IsConsistent) return (null, "The referenced type is inconsistent.");
        var exported = ReadExport(file => target.Export(file, ExportOptions.None), "SW.Types.");
        var sections = exported == null ? null : PlcInterfaceReader.InterfaceSections(exported);
        return (sections, sections == null ? "The referenced type's interface could not be read." : null);
    }

    internal static XElement? ReadExport(Action<FileInfo> export, string kindPrefix)
    {
        string directory = Path.Combine(Path.GetTempPath(), "pia-plc-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "item.xml");
        try
        {
            export(new FileInfo(path));
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024
            });
            return XDocument.Load(reader).Root?.Elements().SingleOrDefault(item =>
                item.Name.LocalName.StartsWith(kindPrefix, StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
