using FormDesigner.Models;
using FormDesigner.Services;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string BetaFolder => Environment.GetEnvironmentVariable("FORMDESIGNER_BETA_ARTIFACTS")
        ?? Path.Combine(FindRepositoryRoot(), "artifacts", "beta-readiness");
    private static string DataGridRegressionFolder => Environment.GetEnvironmentVariable("FORMDESIGNER_DATAGRID_ARTIFACTS")
        ?? Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0");

    private static int RunBetaReadinessChecks()
    {
        Directory.CreateDirectory(BetaFolder);
        var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS BETA_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL BETA_" + name + ": " + ex); }
            finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }

        Check("RecoveryReadLockPreservesLastDraft", () =>
        {
            var service = new AutosaveRecoveryService(Path.Combine(BetaFolder, "recovery-read-lock"));
            var draft = new RecoveryDraftFileModel { SessionId = "beta", DocumentJson = GridFirstVm().ExportWorkspaceJson() };
            Pump(service.SaveDraftAsync(draft));
            var bytes = File.ReadAllBytes(service.RecoveryFilePath);
            // Deny a second reader, but permit deletion: this reproduces a transient I/O
            // error without granting TryLoad permission to discard the only recovery copy.
            using (var held = new FileStream(service.RecoveryFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
            {
                var read = service.TryLoadDraftAsync(); Pump(read);
                RequireCapability(read.Result is null, "Locked draft unexpectedly read.");
            }
            RequireCapability(File.Exists(service.RecoveryFilePath) && File.ReadAllBytes(service.RecoveryFilePath).SequenceEqual(bytes),
                "Transient read failure deleted or changed the last recovery draft.");
            var retry = service.TryLoadDraftAsync(); Pump(retry);
            RequireCapability(retry.Result?.DocumentJson == draft.DocumentJson, "Recovery could not be retried after releasing the lock.");
        });
        Check("MalformedRecoveryIsNotDeletedOnInspection", () =>
        {
            var service = new AutosaveRecoveryService(Path.Combine(BetaFolder, "recovery-malformed"));
            Directory.CreateDirectory(service.RecoveryDirectoryPath);
            const string original = "{\"DocumentJson\":\"recoverable partial text";
            File.WriteAllText(service.RecoveryFilePath, original);
            var read = service.TryLoadDraftAsync(); Pump(read);
            RequireCapability(read.Result is null && File.Exists(service.RecoveryFilePath) && File.ReadAllText(service.RecoveryFilePath) == original,
                "Merely inspecting a damaged recovery file destroyed the original bytes.");
        });
        Check("RecoveryPublicationKeepsExistingReadersConsistent", () =>
        {
            var service = new AutosaveRecoveryService(Path.Combine(BetaFolder, "recovery-publication"));
            Pump(service.SaveDraftAsync(new RecoveryDraftFileModel { DocumentJson = "old-json" }));
            using var reader = new FileStream(service.RecoveryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Pump(service.SaveDraftAsync(new RecoveryDraftFileModel { DocumentJson = "new-json" }));
            using var text = new StreamReader(reader);
            var old = JsonSerializer.Deserialize<RecoveryDraftFileModel>(text.ReadToEnd());
            RequireCapability(old?.DocumentJson == "old-json", "Publishing recovery mutated bytes belonging to an existing reader.");
            var current = service.TryLoadDraftAsync(); Pump(current);
            RequireCapability(current.Result?.DocumentJson == "new-json", "The committed recovery draft was not published.");
        });
        foreach (var native in new[] { false, true })
        foreach (var caseVariant in new[] { false, true }) Check("RejectedDuplicateIdsPreserveSession_" + native + "_" + caseVariant, () =>
        {
            var vm = GridFirstVm(); var selected = vm.CreateControl("Button", 0, 0); vm.SelectSingleControl(selected);
            var session = vm.ActiveSession; var workspace = vm.Workspace;
            var incoming = new DesignerDocumentFileModel { LayoutSemanticsVersion = native ? 2 : 1, SurfaceLayoutMode = native ? "Grid" : "Absolute" };
            incoming.Controls.Add(new DesignerControlFileModel { Id = "duplicate", Type = "Button", Name = "A" });
            incoming.Controls.Add(new DesignerControlFileModel { Id = caseVariant ? "DUPLICATE" : "duplicate", Type = "TextBox", Name = "B" });
            var rejected = false;
            try { vm.LoadDocumentJson(JsonSerializer.Serialize(incoming)); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { rejected = true; Console.WriteLine($"OBSERVED duplicateIds native={native}; exception={ex.GetType().Name}; sessionDisposed={session.IsDisposed}; sameWorkspace={ReferenceEquals(workspace,vm.Workspace)}; controls={vm.Controls.Count}"); }
            RequireCapability(rejected && ReferenceEquals(session, vm.ActiveSession) && !session.IsDisposed && ReferenceEquals(workspace, vm.Workspace)
                && ReferenceEquals(vm.SelectedControl, selected), "Invalid Canvas control identities replaced/disposed the working session or were silently accepted.");
        });
        Check("LegacyMissingIdsRemainSupported", () =>
        {
            var vm = GridFirstVm();
            vm.LoadDocumentJson("{\"Controls\":[{\"Id\":\"\",\"Type\":\"Button\"},{\"Id\":\"\",\"Type\":\"TextBox\"}]}");
            RequireCapability(!vm.UsesNativeLayout && vm.Controls.Count == 2 && vm.Controls.All(c => !string.IsNullOrEmpty(c.Id))
                && vm.Controls.Select(c => c.Id).Distinct().Count() == 2, "Historical missing-ID repair was broken.");
        });
        foreach (var snapshot in new[] { false, true }) Check("InvalidInactiveCanvasRejected_" + snapshot, () =>
        {
            var incoming = GridFirstVm(); incoming.CreateNewForm("Absolute");
            var invalid = incoming.CurrentProject.Forms.Last();
            var model = new DesignerDocumentFileModel();
            model.Controls.Add(new DesignerControlFileModel { Id = "dup", Type = "Button" });
            model.Controls.Add(new DesignerControlFileModel { Id = "dup", Type = "TextBox" });
            if (snapshot) invalid.CurrentSnapshot = JsonSerializer.Serialize(model);
            else { invalid.Document = model; invalid.CurrentSnapshot = ""; }
            var json = JsonSerializer.Serialize(incoming.Workspace);
            var vm = GridFirstVm(); var session = vm.ActiveSession; var original = vm.CreateControl("Button", 0, 0);
            var rejected = false; try { vm.LoadDocumentJson(json); } catch (InvalidOperationException) { rejected = true; }
            RequireCapability(rejected && ReferenceEquals(vm.ActiveSession, session) && !session.IsDisposed && vm.Controls.Contains(original),
                "Invalid inactive Canvas document/snapshot was accepted or changed the current session.");
        });
        Check("RecoveryExplicitDiscardStillWorks", () =>
        {
            var service = new AutosaveRecoveryService(Path.Combine(BetaFolder, "recovery-discard"));
            Pump(service.SaveDraftAsync(new RecoveryDraftFileModel { DocumentJson = "{}" }));
            RequireCapability(service.TryDeleteDraft() && !File.Exists(service.RecoveryFilePath), "Explicit recovery deletion stopped working.");
        });
        Check("DeferredRecoverySurvivesOpeningAnotherProject", () =>
        {
            var vm = GridFirstVm(); var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(120);
            var service = (AutosaveRecoveryService)typeof(FormDesigner.Views.MainWindow).GetField("_autosaveRecoveryService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            Pump(service.SaveDraftAsync(new RecoveryDraftFileModel { DocumentJson = vm.ExportWorkspaceJson() }));
            var original = File.ReadAllBytes(service.RecoveryFilePath);
            var task = (Task<bool>)typeof(FormDesigner.Views.MainWindow).GetMethod("CheckRecoveryOnStartupAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
            NativeItemsUiWait(120);
            window.OwnedWindows.OfType<FormDesigner.Views.RecoveryWindow>().Single().Close(FormDesigner.Views.RecoveryDialogResult.OpenNormally); Pump(task);
            vm.LoadDocumentJson(GridFirstLegacyJson()); GridFirstVisible(window, vm);
            RequireCapability(File.Exists(service.RecoveryFilePath) && File.ReadAllBytes(service.RecoveryFilePath).SequenceEqual(original),
                "Opening a different clean document silently discarded the deferred recovery draft.");
            RequireCapability(!vm.AutosaveStatusText.Contains("очищен", StringComparison.OrdinalIgnoreCase), "Status falsely reports deletion of a protected recovery draft.");
            vm.CreateControl("Button", 0, 0);
            typeof(FormDesigner.Views.MainWindow).GetMethod("AutosaveTimer_Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            NativeItemsUiWait(200);
            RequireCapability(File.ReadAllBytes(service.RecoveryFilePath).SequenceEqual(original), "Autosave of another document overwrote deferred recovery.");
            task = (Task<bool>)typeof(FormDesigner.Views.MainWindow).GetMethod("CheckRecoveryOnStartupAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
            NativeItemsUiWait(100); window.OwnedWindows.OfType<FormDesigner.Views.RecoveryWindow>().Single().Close(FormDesigner.Views.RecoveryDialogResult.DeleteDraft); Pump(task);
            RequireCapability(!File.Exists(service.RecoveryFilePath), "Explicit discard no longer removed deferred recovery.");
            typeof(FormDesigner.Views.MainWindow).GetMethod("AutosaveTimer_Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            NativeItemsUiWait(250);
            RequireCapability(File.Exists(service.RecoveryFilePath), "Autosave did not resume after the deferred recovery was discarded.");
        });
        foreach (var canvas in new[] { false, true }) Check("DeleteImmediatelyAfterSwitchIsAtomic_" + canvas, () =>
        {
            var vm = GridFirstVm(); if (canvas) vm.CreateNewForm("Absolute");
            var form = vm.ActiveDocumentId; var parent = vm.CreateControl("StackPanel", 0, 0);
            var child = vm.CreateControl("Button", 0, 0, parent.Id); var sibling = vm.CreateControl("TextBox", 0, 0);
            vm.CreateNewForm(); vm.SetActiveForm(form, "beta-atomic-delete");
            vm.SelectSingleControl(vm.GetControl(parent.Id)!); vm.DeleteSelectedCommand.Execute(null);
            RequireCapability(vm.GetControl(parent.Id) is null && vm.GetControl(child.Id) is null, "Subtree delete failed.");
            GridEditorUndo(vm);
            RequireCapability(vm.GetControl(parent.Id) is not null && vm.GetControl(child.Id)?.ParentId == parent.Id && vm.GetControl(sibling.Id) is not null,
                "Delete coalesced with earlier editing/restoration and Undo did not restore the exact subtree.");
            GridEditorUndo(vm, true);
            RequireCapability(vm.GetControl(parent.Id) is null && vm.GetControl(child.Id) is null && vm.GetControl(sibling.Id) is not null, "Redo changed unrelated controls.");
        });
        Check("TwentyNewProjectsReleaseOldSessions", () =>
        {
            var vm = GridFirstVm(); var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null);
            var refs = Enumerable.Range(0, 20).Select(_ => BetaReplaceProject(vm, window)).ToArray();
            NativeItemsUiWait(180); ForceFullGc();
            RequireCapability(refs.All(r => !r.IsAlive), "A replaced project session is retained by the live Designer.");
            Console.WriteLine("OBSERVED replacedSessions=20; alive=" + refs.Count(r => r.IsAlive));
        });
        Check("NativeSourceModesAndRedoAcrossProviders", () =>
        {
            var vm = GridFirstVm(); var sqlForm = vm.ActiveDocumentId;
            var sql = ProductsSource(); sql.Id = "beta-sql"; sql.SourceKind = "SqlServer"; sql.PreviewRowMode = BindingSourceModel.PreviewRowModeAllRows;
            vm.BindingSources.Add(sql); vm.CreateControl("TextBox", 0, 0);
            var pgForm = vm.CreateNewForm().Id;
            var pg = ProductsSource(); pg.Id = "beta-pg"; pg.SourceKind = "PostgreSql"; pg.PreviewRowMode = BindingSourceModel.PreviewRowModeSchemaOnly;
            vm.BindingSources.Add(pg); vm.CreateControl("TextBox", 0, 0);
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null);
            foreach (var id in new[] { sqlForm, pgForm, sqlForm, pgForm })
            {
                vm.SetActiveForm(id, "beta-source-switch"); GridFirstVisible(window, vm);
                var expected = id == sqlForm ? BindingSourceModel.PreviewRowModeAllRows : BindingSourceModel.PreviewRowModeSchemaOnly;
                RequireCapability(vm.BindingSources.Single().PreviewRowMode == expected, "Switching providers changed authored preview mode.");
                var point = new Avalonia.Point(30, 30); var target = vm.ResolveLayoutDropTarget(point);
                var added = vm.CommitGridToolboxDrop("Button", target, point, vm.ActiveDocumentId)!; GridFirstVisible(window, vm);
                RequireCapability(added is not null, "Grid drop failed in provider fixture.");
                var addedId = added!.Id; GridEditorUndo(vm);
                RequireCapability(vm.HasRedo && vm.GetControl(addedId) is null && vm.BindingSources.Single().PreviewRowMode == expected, "Native refresh changed mode or invalidated Redo.");
                GridEditorUndo(vm, true); RequireCapability(vm.GetControl(addedId) is not null, "Provider form Redo lost ControlId.");
            }
        });
        Console.WriteLine($"Beta readiness checks: {passed}/{passed + failed}");
        return failed == 0 ? 0 : 1;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference BetaReplaceProject(FormDesigner.ViewModels.MainWindowViewModel vm, FormDesigner.Views.MainWindow window)
    {
        vm.CreateControl("Button", 0, 0); var session = vm.ActiveSession; var weak = new WeakReference(session);
        vm.NewProjectEditorCommand!.Execute(null); GridFirstVisible(window, vm);
        RequireCapability(session.IsDisposed, "New Project did not dispose the previous session.");
        return weak;
    }

    // Test-only, opt-in forensic checkpoint. The original assertion still runs
    // after release; this never runs in the application or ordinary smoke suite.
    private static void BetaHoldNegativeControlForDump(IReadOnlyList<WeakReference> references)
    {
        if (Environment.GetEnvironmentVariable("FORMDESIGNER_BETA_HOLD_NEGATIVE_CONTROL") != "1") return;
        var folder = Path.Combine(BetaFolder, "memory-diagnostics"); Directory.CreateDirectory(folder);
        var release = Path.Combine(folder, "release.txt");
        if (File.Exists(release)) File.Delete(release);
        File.WriteAllText(Path.Combine(folder, "ready.json"), JsonSerializer.Serialize(new { ProcessId = Environment.ProcessId, Alive = references.Count(r => r.IsAlive) }));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(release) && watch.Elapsed < TimeSpan.FromMinutes(5)) NativeItemsUiWait(50);
        GC.KeepAlive(references);
    }
}
