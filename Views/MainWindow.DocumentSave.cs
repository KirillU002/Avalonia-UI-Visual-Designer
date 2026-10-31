using FormDesigner.DesignerSystem;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private bool _documentSaveInProgress;
    private sealed record DocumentSaveScope(MainWindowViewModel Model, WorkspaceModel Workspace,
        DesignerDocumentSession Session, AxamlRoundTripDocument? Axaml);

    private DocumentSaveScope CaptureDocumentSaveScope() => new(VM, VM.Workspace, VM.ActiveSession, VM.ActiveAxamlSourceDocument);
    private bool IsSaveWorkspaceCurrent(DocumentSaveScope scope) => ReferenceEquals(DataContext, scope.Model) && ReferenceEquals(VM.Workspace, scope.Workspace);
    private bool IsSaveDocumentCurrent(DocumentSaveScope scope) => IsSaveWorkspaceCurrent(scope) && !scope.Session.IsDisposed
        && ReferenceEquals(VM.ActiveSession, scope.Session) && ReferenceEquals(VM.ActiveAxamlSourceDocument, scope.Axaml);

    private bool BeginDocumentSave()
    {
        if (_documentSaveInProgress)
        { VM.StatusText = "Сохранение уже выполняется. Дождитесь его завершения и повторите Save для новых изменений."; return false; }
        _documentSaveInProgress = true;
        return true;
    }

    private bool CanClearSavedDraft() => !VM.HasUnsavedWorkspaceChanges;
}
