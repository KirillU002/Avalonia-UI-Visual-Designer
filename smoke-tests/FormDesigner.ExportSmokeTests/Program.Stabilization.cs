using Avalonia.Controls;
using FormDesigner.DesignerSystem;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private const string AuditAxaml = "<Window xmlns=\"https://github.com/avaloniaui\"><Canvas><!-- retained --><Button Name=\"AuditButton\" Content=\"Before\" Width=\"140\" Height=\"40\" /></Canvas></Window>";
    private static int RunStabilizationChecks()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action test)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { test(); passed++; Console.WriteLine("PASS AUDIT_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL AUDIT_" + name + ": " + ex); }
            finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        foreach (var axaml in new[] { false, true })
            Check("RejectedNativeOpenPreserves" + (axaml ? "Axaml" : "Json"), () =>
            {
                var vm = CreateViewModel("ProtectedOpen");
                if (axaml) vm.LoadAxamlImportedDocument(new AxamlImportService().Import(AuditAxaml, "original.axaml"), "original.axaml");
                else vm.CreateControl("Button", 10, 20);
                var control = vm.Controls.First(); control.Text = "Unsaved work"; vm.SelectSingleControl(control);
                var session = vm.ActiveSession; var workspace = vm.Workspace;
                var invalid = new DesignerDocumentFileModel { LayoutSemanticsVersion = 3 };
                var rejected = false; try { vm.LoadDocumentJson(JsonSerializer.Serialize(invalid)); } catch (InvalidOperationException) { rejected = true; }
                RequireCapability(rejected && ReferenceEquals(vm.Workspace, workspace) && ReferenceEquals(vm.ActiveSession, session)
                    && !session.IsDisposed && ReferenceEquals(vm.SelectedControl, control) && vm.Controls.Contains(control), "Rejected input replaced/disposed the current document or selection.");
                RequireCapability(vm.IsAxamlRoundTripDocument == axaml, "Rejected JSON erased the AXAML source context.");
                if (axaml) RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("Unsaved work"), "The original AXAML is no longer patchable.");
            });
        foreach (var malformed in new[] { "{\"Project\":{\"Forms\":null}}", "{\"Project\":{\"Forms\":\"invalid\"}}", "{\"Project\":null}" })
            Check("MalformedWorkspace" + malformed.Length, () =>
            {
                var vm = CreateViewModel("KeepWorkspace"); var c = vm.CreateControl("Button", 0, 0); var workspace = vm.Workspace;
                var rejected = false; try { vm.LoadDocumentJson(malformed); } catch (Exception ex) when (ex is InvalidOperationException or JsonException or InvalidDataException) { rejected = true; }
                RequireCapability(rejected && ReferenceEquals(vm.Workspace, workspace) && vm.Controls.Contains(c), "Malformed workspace was silently opened as an empty single document.");
            });
        Check("InvalidInactiveFormRejectedBeforeLoad", () =>
        {
            var incoming = NativeGridModel(); incoming.CreateNewForm("Grid"); incoming.SetActiveForm(incoming.CurrentProject.Forms[0].Id, "audit");
            var bad = incoming.CurrentProject.Forms[1]; bad.Document.LayoutSemanticsVersion = 3; bad.CurrentSnapshot = JsonSerializer.Serialize(bad.Document);
            var vm = CreateViewModel("KeepOriginal"); var c = vm.CreateControl("Button", 0, 0); var before = vm.Workspace;
            var rejected = false; try { vm.LoadDocumentJson(incoming.ExportWorkspaceJson()); } catch (InvalidOperationException) { rejected = true; }
            RequireCapability(rejected && ReferenceEquals(before, vm.Workspace) && vm.Controls.Contains(c), "Invalid inactive form was accepted and can fail later during form switching.");
        });
        foreach (var axaml in new[] { false, true })
        foreach (var replace in new[] { false, true })
            Check("AsyncSave" + (axaml ? "Axaml" : "Json") + (replace ? "ReplaceProject" : "EditInFlight"), () =>
            {
                var vm = CreateViewModel("SaveRace");
                if (axaml) vm.LoadAxamlImportedDocument(new AxamlImportService().Import(AuditAxaml, "save.axaml"), "save.axaml");
                else vm.CreateControl("Button", 0, 0);
                vm.Controls.First().Text = "Saved revision"; NativeItemsUiWait(180);
                var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(150);
                var file = new AuditDelayedFile(axaml ? "save.axaml" : "save.formdesigner.json");
                var method = axaml ? "SaveAxamlDocumentToHostFileAsync" : "SaveDocumentToHostFileAsync";
                var save = (Task<bool>)typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { file })!;
                RequireCapability(file.Opened && !save.IsCompleted, "The save did not reach controlled asynchronous storage.");
                if (replace) { vm.NewProjectEditorCommand!.Execute(null); vm.CreateControl("TextBox", 0, 0).Text = "New project unsaved"; }
                else vm.Controls.First().Text = "Edited while saving";
                NativeItemsUiWait(180); var currentPath = vm.CurrentDocumentPath; var session = vm.ActiveSession;
                file.Release(); Pump(save);
                RequireCapability(save.Result && vm.HasUnsavedChanges, "The completed save marked unsaved/newer work as saved or reported failure after writing.");
                RequireCapability(ReferenceEquals(vm.ActiveSession, session) && (!replace || vm.CurrentDocumentPath == currentPath), "Old save completion rebound the replacement project to the old path.");
                var written = Encoding.UTF8.GetString(file.Bytes.ToArray());
                RequireCapability(written.Contains("Saved revision") && !written.Contains("Edited while saving") && !written.Contains("New project unsaved"), "The output is not the revision captured by this save.");
                if (axaml && !replace) RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("Edited while saving"), "Post-save AXAML source rebase lost the pending edit.");
            });
        Check("AxamlPathReadDoesNotPatchReplacementProject", () =>
        {
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "stabilization-audit", "read-gate"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "original.axaml"); File.WriteAllText(path, AuditAxaml);
            var fs = new AuditDelayedFileSystem(path); var host = new TestDesignerHostServices(true, fs);
            var vm = CreateViewModel("ReadRace"); vm.LoadAxamlImportedDocument(new AxamlImportService().Import(AuditAxaml, path), path); vm.Controls.First().Text = "Old pending edit";
            var window = new MainWindow(host) { DataContext = vm }; SmokeWindows.Add(window); window.Show(); NativeItemsUiWait(120);
            var save = (Task<bool>)typeof(MainWindow).GetMethod("SaveAxamlDocumentToPathAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { path })!;
            RequireCapability(fs.ReadEntered && !save.IsCompleted, "Path save did not await source read.");
            vm.LoadAxamlImportedDocument(new AxamlImportService().Import(AuditAxaml, "replacement.axaml"), "replacement.axaml"); vm.Controls.First().Text = "Replacement pending edit";
            fs.ReleaseRead(); Pump(save);
            RequireCapability(!save.Result && File.ReadAllText(path) == AuditAxaml && vm.CurrentDocumentPath == "replacement.axaml" && vm.HasUnsavedChanges, "A save resumed after its source read and wrote the replacement project's patch into the original file.");
        });
        Check("BackupPreservesRapidRevisions", () =>
        {
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "stabilization-audit", "backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "document.formdesigner.json"); var service = new DocumentBackupService(folder);
            while (DateTime.Now.Millisecond > 700) Thread.Sleep(5);
            File.WriteAllText(path, "revision one"); var first = service.TryCreateBackupAsync(path).GetAwaiter().GetResult();
            File.WriteAllText(path, "revision two"); var second = service.TryCreateBackupAsync(path).GetAwaiter().GetResult();
            RequireCapability(first is not null && second is not null && first.FilePath != second.FilePath && File.ReadAllText(first.FilePath) == "revision one" && File.ReadAllText(second.FilePath) == "revision two", "Rapid saves overwrite the preceding backup revision.");
        });
        Check("BackupRetentionDoesNotDeleteAnotherDocument", () =>
        {
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "stabilization-audit", "backup-scope-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "MainWindow.axaml"); var copy = Path.Combine(folder, "MainWindow.copy.axaml");
            File.WriteAllText(path, "main"); File.WriteAllText(copy, "copy"); var service = new DocumentBackupService(folder);
            var other = service.TryCreateBackupAsync(copy).GetAwaiter().GetResult()!;
            for (var i = 0; i < 7; i++) { File.WriteAllText(path, "revision " + i); service.TryCreateBackupAsync(path).GetAwaiter().GetResult(); }
            RequireCapability(File.Exists(other.FilePath) && service.ListBackups(copy).Count == 1 && service.ListBackups(path).Count == 5, "Retention for MainWindow deleted or listed backups belonging to MainWindow.copy.");
        });
        foreach (var axaml in new[] { false, true })
            Check("NormalSaveClearsDirty" + (axaml ? "Axaml" : "Json"), () =>
            {
                var vm = CreateViewModel("NormalSave");
                if (axaml) vm.LoadAxamlImportedDocument(new AxamlImportService().Import(AuditAxaml, "normal.axaml"), "normal.axaml"); else vm.CreateControl("Button", 0, 0);
                vm.Controls.First().Text = "Normal changed value"; var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(120);
                var file = new AuditDelayedFile(axaml ? "normal.axaml" : "normal.formdesigner.json");
                var task = (Task<bool>)typeof(MainWindow).GetMethod(axaml ? "SaveAxamlDocumentToHostFileAsync" : "SaveDocumentToHostFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { file })!;
                file.Release(); Pump(task);
                RequireCapability(task.Result && !vm.HasUnsavedChanges && vm.CurrentProject.Forms.All(f => !f.IsDirty), "Normal Save does not acknowledge the exact current revision.");
                if (axaml) RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges && Encoding.UTF8.GetString(file.Bytes.ToArray()).Contains("<!-- retained -->"), "Normal AXAML Save lost source preservation/rebase.");
            });
        Check("WorkspaceSaveAcknowledgesEachWrittenFormOnly", () =>
        {
            var vm = CreateViewModel("MultiSave"); var first = vm.ActiveFormDocument!; vm.CreateControl("Button", 0, 0).Text = "First revision";
            var second = vm.CreateNewForm("Grid"); vm.CreateControl("TextBox", 0, 0).Text = "Second revision"; vm.SetActiveForm(first.Id, "audit-multi-save");
            var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(100); var file = new AuditDelayedFile("multi.formdesigner.json");
            var method = typeof(MainWindow).GetMethod("SaveDocumentToHostFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task<bool>)method.Invoke(window, new object[] { file })!;
            vm.SetActiveForm(second.Id, "audit-switch-during-save"); vm.Controls.First().Text = "Second later edit"; NativeItemsUiWait(100);
            file.Release(); Pump(task);
            RequireCapability(task.Result && !first.IsDirty && second.IsDirty && vm.HasUnsavedChanges, "Workspace Save marked the wrong form revision as saved.");
            var next = new AuditDelayedFile("multi.formdesigner.json"); task = (Task<bool>)method.Invoke(window, new object[] { next })!; next.Release(); Pump(task);
            RequireCapability(task.Result && vm.CurrentProject.Forms.All(f => !f.IsDirty) && !vm.HasUnsavedChanges, "A second Save did not acknowledge all written forms.");
        });
        Check("InactiveDirtyFormRequiresUnsavedConfirmation", () =>
        {
            var vm = CreateViewModel("InactiveUnsaved"); var first = vm.ActiveFormDocument!; vm.CreateControl("Button", 0, 0);
            var second = vm.CreateNewForm("Grid"); vm.CreateControl("TextBox", 0, 0);
            vm.MarkWorkspaceSaved("protected.formdesigner.json", vm.ExportWorkspaceJson());
            vm.SetActiveForm(first.Id, "audit-edit-inactive"); vm.Controls.First().Text = "Must not be discarded"; vm.SetActiveForm(second.Id, "audit-clean-active");
            RequireCapability(first.IsDirty && !second.IsDirty && !vm.HasUnsavedChanges, "Fixture does not isolate an inactive dirty form.");
            var host = new TestDesignerHostServices(true); host.Dialogs.NextResult = DesignerDialogResult.Cancelled;
            var window = new MainWindow(host) { DataContext = vm }; SmokeWindows.Add(window); window.Show(); NativeItemsUiWait(100);
            var confirm = (Task<bool>)typeof(MainWindow).GetMethod("EnsureUnsavedChangesHandledAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!; Pump(confirm);
            RequireCapability(!confirm.Result && host.Dialogs.Requests.Any(r => r.Kind == DesignerDialogKind.UnsavedChanges), "Close/New/Open can discard an inactive dirty form without a confirmation.");
        });
        Check("ConcurrentSaveIsRejectedAndRetryWorks", () =>
        {
            var vm = CreateViewModel("ConcurrentSave"); vm.CreateControl("Button", 0, 0); var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(100);
            var method = typeof(MainWindow).GetMethod("SaveDocumentToHostFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var first = new AuditDelayedFile("first.formdesigner.json"); var task = (Task<bool>)method.Invoke(window, new object[] { first })!;
            var next = new AuditDelayedFile("second.formdesigner.json"); var duplicate = (Task<bool>)method.Invoke(window, new object[] { next })!; Pump(duplicate);
            RequireCapability(!duplicate.Result && !next.Opened, "Overlapping Save opened a second writer.");
            first.Release(); Pump(task); var retry = (Task<bool>)method.Invoke(window, new object[] { next })!; next.Release(); Pump(retry);
            RequireCapability(task.Result && retry.Result && !vm.HasUnsavedChanges, "Save gate was not released for a new operation.");
        });
        Check("SaveConfirmationCannotDiscardLateEdit", () =>
        {
            var vm = CreateViewModel("SaveThenClose"); var c = vm.CreateControl("Button", 0, 0); c.Text = "Before close";
            var file = new AuditDelayedFile("close.formdesigner.json"); var host = new TestDesignerHostServices(true);
            host.Dialogs.NextResult = DesignerDialogResult.Save; host.FilePicker.NextSaveFile = file;
            var window = new MainWindow(host) { DataContext = vm }; SmokeWindows.Add(window); window.Show(); NativeItemsUiWait(100);
            var confirm = (Task<bool>)typeof(MainWindow).GetMethod("EnsureUnsavedChangesHandledAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            RequireCapability(file.Opened && !confirm.IsCompleted, "Close confirmation did not start the delayed save.");
            c.Text = "Late edit must survive"; NativeItemsUiWait(100); file.Release(); Pump(confirm);
            RequireCapability(!confirm.Result && vm.HasUnsavedChanges && c.Text == "Late edit must survive", "Close confirmation discarded a change made while saving.");
        });
        Check("InactiveDirtyFormAutosavesAndKeepsRecovery", () =>
        {
            var vm = CreateViewModel("InactiveRecovery"); var first = vm.ActiveFormDocument!; vm.CreateControl("Button", 0, 0);
            var second = vm.CreateNewForm("Grid"); vm.CreateControl("TextBox", 0, 0); vm.MarkWorkspaceSaved("recovery.formdesigner.json", vm.ExportWorkspaceJson());
            vm.SetActiveForm(first.Id, "audit-recovery-edit"); vm.Controls.First().Text = "Recovery must include this"; vm.SetActiveForm(second.Id, "audit-recovery-clean");
            var host = new TestDesignerHostServices(true); var window = new MainWindow(host) { DataContext = vm }; SmokeWindows.Add(window); window.Show(); NativeItemsUiWait(120);
            typeof(MainWindow).GetMethod("AutosaveTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            var recovery = new AutosaveRecoveryService(host.Paths.RecoveryDirectory); var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!File.Exists(recovery.RecoveryFilePath) && DateTime.UtcNow < deadline) NativeItemsUiWait(20);
            var draftTask = recovery.TryLoadDraftAsync(); Pump(draftTask);
            RequireCapability(draftTask.Result?.DocumentJson.Contains("Recovery must include this") == true, "Autosave skipped an inactive dirty form.");
            vm.SetActiveForm(first.Id, "audit-recovery-back"); vm.SetActiveForm(second.Id, "audit-recovery-return-clean");
            RequireCapability(File.Exists(recovery.RecoveryFilePath), "Switching to a clean form deleted the dirty workspace recovery.");
        });
        Check("LegacyBackupNamesRemainReadable", () =>
        {
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "stabilization-audit", "backup-legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var backupFolder = Path.Combine(folder, ".formdesigner-backups"); Directory.CreateDirectory(backupFolder);
            var path = Path.Combine(folder, "MainWindow.axaml"); var legacy = Path.Combine(backupFolder, "MainWindow.2026-10-30_10-20-30.backup.formdesigner.json"); File.WriteAllText(legacy, AuditAxaml);
            var copy = Path.Combine(backupFolder, "MainWindow.copy.2026-10-30_10-20-30.backup.formdesigner.json"); File.WriteAllText(copy, "other");
            var listed = new DocumentBackupService(folder).ListBackups(path);
            RequireCapability(listed.Count == 1 && listed[0].FilePath == legacy && File.ReadAllText(listed[0].FilePath) == AuditAxaml, "New backup naming hid legacy revisions or mixed another file's revisions.");
        });
        Check("BoundedNativeDesignerPerformance", () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew(); var vm = GridEditorModel("80,*", "200,*");
            for (var i = 0; i < 100; i++) { var c = vm.CreateControl("Button", 0, 0); c.Text = "Control " + i; c.GridRow = i % 2; c.GridColumn = (i / 2) % 2; }
            var modelMs = watch.ElapsedMilliseconds; watch.Restart(); var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(120);
            var renderMs = watch.ElapsedMilliseconds; watch.Restart();
            var point = GridCellPoint(vm, "", 1, 1); var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++) RequireCapability(vm.ResolveGridDropTarget(point).IsValid, "Repeated measured hit tests stopped resolving the current form.");
            Console.WriteLine($"OBSERVED controls=100; modelMs={modelMs}; designerShowAndIdleMs={renderMs}; hitTests=200; hitTestMs={watch.ElapsedMilliseconds}; hitTestAllocatedBytes={GC.GetAllocatedBytesForCurrentThread() - allocated}");
        });
        Check("ClosedPlainWindowNegativeControl", () =>
        {
            var references = Enumerable.Range(0, 6).Select(_ => AuditClosedPlainWindow()).ToArray();
            NativeItemsUiWait(1000); ForceFullGc();
            Console.WriteLine("OBSERVED plainWindowClosed=6; alive=" + references.Count(r => r.IsAlive));
            RequireCapability(references.All(r => !r.IsAlive), "Plain Avalonia windows are retained in the same test environment.");
        });
        foreach (var axaml in new[] { false, true })
            Check("ClosedPreviewCollectible" + (axaml ? "Axaml" : "Legacy"), () =>
            {
                var vm = NativeGridControls(); var references = new List<WeakReference>();
                AuditClosedPreview(vm, axaml); ForceFullGc(); var before = GC.GetTotalMemory(true);
                for (var i = 0; i < 6; i++) references.Add(AuditClosedPreview(vm, axaml));
                NativeItemsUiWait(1000); ForceFullGc(); var after = GC.GetTotalMemory(true); var alive = references.Count(r => r.IsAlive);
                Console.WriteLine($"OBSERVED previewMode={(axaml ? "Axaml" : "Legacy")}; closed=6; alive={alive}; managedBefore={before}; managedAfter={after}");
                RequireCapability(alive == 0, "Closed Preview windows remain strongly referenced after idle and full GC.");
            });
        Console.WriteLine($"Stabilization checks: {passed}/{passed + failed}"); return failed == 0 && passed > 0 ? 0 : 1;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AuditClosedPreview(MainWindowViewModel vm, bool axaml)
    {
        using var preview = ShowDataGridRegressionPreview(vm, axaml); return new WeakReference(preview.Window);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AuditClosedPlainWindow()
    {
        var window = new Window { Width = 200, Height = 150, ShowInTaskbar = false, ShowActivated = false, Position = new Avalonia.PixelPoint(-3000, -3000), Content = new Button { Content = "plain" } };
        window.Show(); NativeItemsUiWait(60); window.Close(); return new WeakReference(window);
    }
    private sealed class AuditDelayedFile(string name) : IDesignerHostFile
    {
        private readonly TaskCompletionSource<Stream> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MemoryStream Bytes { get; } = new();
        public bool Opened { get; private set; }
        public string Name => name;
        public string? LocalPath => null;
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) { Opened = true; return _gate.Task; }
        public void Release() => _gate.SetResult(Bytes);
    }
    private sealed class AuditDelayedFileSystem(string target) : IDesignerFileSystem
    {
        private readonly PhysicalDesignerFileSystem _physical = new();
        private readonly TaskCompletionSource<bool> _readGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadEntered { get; private set; }
        public void ReleaseRead() => _readGate.SetResult(true);
        public bool FileExists(string path) => _physical.FileExists(path);
        public bool DirectoryExists(string path) => _physical.DirectoryExists(path);
        public void CreateDirectory(string path) => _physical.CreateDirectory(path);
        public void DeleteFile(string path) => _physical.DeleteFile(path);
        public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        { if (path == target) { ReadEntered = true; await _readGate.Task; } return await _physical.ReadAllTextAsync(path, cancellationToken); }
        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => _physical.WriteAllTextAsync(path, contents, cancellationToken);
        public Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default) => _physical.WriteAllTextAtomicallyAsync(path, contents, cancellationToken);
        public Task WriteAllTextAtomicallyAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default) => _physical.WriteAllTextAtomicallyAsync(path, contents, encoding, cancellationToken);
    }
}
