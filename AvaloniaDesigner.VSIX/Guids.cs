using System;

namespace AvaloniaDesigner.VSIX;

internal static class Guids
{
    public const string PackageString = "97151B7E-03CD-468B-80F6-32601757621A";
    public const string CommandSetString = "343D141A-A3FC-4BA7-A0B0-A61F23AF1BCB";
    public static readonly Guid CommandSet = new(CommandSetString);
}

internal static class CommandIds
{
    public const int OpenInDesigner = 0x0100;
}
