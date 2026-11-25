using System;
using System.IO;

namespace Pia.Custom;

internal sealed class PlcSourceWorkspace : IDisposable
{
    internal DirectoryInfo Directory { get; }

    internal PlcSourceWorkspace()
    {
        // Openness validates the path supplied to the DirectoryInfo constructor.
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pia-plc-source-" + Guid.NewGuid().ToString("N")));
        Directory = new DirectoryInfo(path);
        Directory.Create();
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles()) file.Delete();
            Directory.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
