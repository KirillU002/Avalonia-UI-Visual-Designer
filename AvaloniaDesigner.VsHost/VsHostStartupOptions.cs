using System;

namespace AvaloniaDesigner.VsHost;

internal sealed class VsHostStartupOptions
{
    public static VsHostStartupOptions Current { get; set; } = new();

    public string PipeName { get; private set; } = string.Empty;

    public static VsHostStartupOptions Parse(string[] args)
    {
        var options = new VsHostStartupOptions();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--pipe", StringComparison.OrdinalIgnoreCase))
                options.PipeName = args[index + 1];
        }

        if (string.IsNullOrWhiteSpace(options.PipeName))
            throw new ArgumentException("AvaloniaDesigner.VsHost requires --pipe <name>.");

        return options;
    }
}
