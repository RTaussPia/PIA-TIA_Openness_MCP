using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using TiaMcpServer.Siemens;

namespace Pia.Custom;

internal static class LibraryUsage
{
    internal static void FindControllers(ProjectBase project, List<PlcSoftware> plcs, List<string> issues)
    {
        void VisitItem(DeviceItem item)
        {
            try
            {
                if (item.GetService<SoftwareContainer>()?.Software is PlcSoftware plc && !plcs.Contains(plc))
                    plcs.Add(plc);
            }
            catch (Exception ex) { issues.Add("Cannot inspect controller at " + item.Name + " (" + ex.GetType().Name + ")."); }
            foreach (var child in item.DeviceItems) VisitItem(child);
        }
        void VisitDevices(IEnumerable<Device> devices)
        {
            foreach (var device in devices)
                foreach (var item in device.DeviceItems) VisitItem(item);
        }
        void VisitGroup(DeviceUserGroup group)
        {
            VisitDevices(group.Devices);
            foreach (var child in group.Groups) VisitGroup(child);
        }
        VisitDevices(project.Devices);
        foreach (var group in project.DeviceGroups) VisitGroup(group);
        VisitDevices(project.UngroupedDevicesGroup.Devices);
    }

    internal static bool ReadReferences(IEngineeringObject instance, string projectObject, List<object> rows)
    {
        try
        {
            var service = (instance as IEngineeringServiceProvider)?.GetService<CrossReferenceService>();
            if (service == null)
            {
                rows.Add(new { status = "incomplete", reason = "Cross-reference service unavailable." });
                return false;
            }
            var result = service.GetCrossReferences(CrossReferenceFilter.AllObjects);
            var references = CrossReferenceFlattener.Flatten(result, projectObject, out var truncation, out _);
            return UsageRules.AddReferences(references, truncation != null, rows);
        }
        catch (Exception ex)
        {
            rows.Add(new { status = "incomplete",
                reason = "Cross-reference lookup failed (" + ex.GetType().Name + ")." });
            return false;
        }
    }

    internal static string ObjectPath(IEngineeringObject instance, IEngineeringObject root)
    {
        var names = new Stack<string>();
        for (var node = instance; node != null && !node.Equals(root); node = node.Parent)
        {
            var name = node.GetType().GetProperty("Name")?.GetValue(node) as string;
            if (!string.IsNullOrEmpty(name)) names.Push(name!);
        }
        return string.Join("/", names);
    }
}
