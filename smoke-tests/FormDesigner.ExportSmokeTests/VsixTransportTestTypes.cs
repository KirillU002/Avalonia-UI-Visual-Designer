// The transport client never calls the VSSDK. These SDK/DTO stand-ins allow its
// actual source to run in a net8 test process; they do not emulate devenv/DTE.
namespace Microsoft.VisualStudio.Shell
{
    internal sealed class AsyncPackage { }
}
namespace AvaloniaDesigner.VSIX
{
    internal sealed class VsDocumentSnapshot
    {
        public string DocumentId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public long Version { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }
}
