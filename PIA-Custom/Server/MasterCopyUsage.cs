using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace Pia.Custom;

internal static class MasterCopyUsage
{
    internal static void Read(MasterCopy copy, IEnumerable<PlcSoftware> controllers, EntryUsage report, bool includeReferences)
    {
        var names = UsageRules.MasterCopyObjectNames(copy.Name, copy.ContentDescriptions.Select(description => description.ContentName));
        foreach (var plc in controllers)
        {
            try
            {
                foreach (var obj in Objects(plc, names))
                    report.AddInstance(plc, obj, null, null, includeReferences);
            }
            catch (Exception ex)
            {
                report.Issues.Add($"Name lookup on {plc.Name} failed ({ex.GetType().Name}).");
            }
        }
    }

    private static IEnumerable<IEngineeringObject> Objects(PlcSoftware plc, string[] names)
    {
        foreach (var obj in InScope(plc.BlockGroup, plc.TypeGroup, names)) yield return obj;
        var provider = plc.GetService<PlcUnitProvider>();
        if (provider == null) yield break;
        foreach (var unit in provider.UnitGroup.Units)
            foreach (var obj in InScope(unit.BlockGroup, unit.TypeGroup, names)) yield return obj;
        foreach (var unit in provider.UnitGroup.SafetyUnits)
            foreach (var obj in InScope(unit.BlockGroup, unit.TypeGroup, names)) yield return obj;
    }

    private static IEnumerable<IEngineeringObject> InScope(PlcBlockSystemGroup blocks, PlcTypeSystemGroup types, string[] names)
    {
        foreach (var group in Tree<PlcBlockGroup>(blocks, g => g.Groups))
            foreach (var name in names)
                if (group.Blocks.Find(name) is PlcBlock block) yield return block;
        foreach (var root in blocks.SystemBlockGroups)
            foreach (var group in Tree<PlcSystemBlockGroup>(root, g => g.Groups))
                foreach (var name in names)
                    if (group.Blocks.Find(name) is PlcBlock block) yield return block;
        foreach (var group in Tree<PlcTypeGroup>(types, g => g.Groups))
            foreach (var name in names)
                if (group.Types.Find(name) is PlcType type) yield return type;
        foreach (var group in types.SystemTypeGroups)
            foreach (var name in names)
                if (group.Types.Find(name) is PlcType type) yield return type;
    }

    private static IEnumerable<T> Tree<T>(T root, Func<T, IEnumerable<T>> children)
    {
        yield return root;
        foreach (var child in children(root))
            foreach (var nested in Tree(child, children)) yield return nested;
    }
}
