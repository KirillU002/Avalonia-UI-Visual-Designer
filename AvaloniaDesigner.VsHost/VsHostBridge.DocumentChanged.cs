using AvaloniaDesigner.Host.Protocol;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VsHost;

public sealed partial class VsHostBridge
{
    private async Task HandleDocumentChangedAsync(DesignerHostEnvelope message)
    {
        var changed = _connection?.GetPayload<DocumentChangedPayload>(message);
        if (changed is null || _document is null)
        {
            await SendErrorAsync(message, "DOCUMENT_CHANGE_UNAVAILABLE", "There is no open document to reload.");
            return;
        }

        var document = new OpenDocumentPayload
        {
            FilePath = _document.FilePath,
            Text = changed.Text,
            Version = changed.Version,
            Checksum = changed.Checksum,
            ProjectPath = _document.ProjectPath,
            ProjectName = _document.ProjectName,
            TargetFramework = _document.TargetFramework,
            AvaloniaVersion = _document.AvaloniaVersion
        };
        await OpenDocumentAsync(message.DocumentId, document, message);
    }
}
