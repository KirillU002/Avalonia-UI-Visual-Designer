using Avalonia.Controls;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunBetaWorkflow()
    {
        try
        {
            Directory.CreateDirectory(BetaFolder);
            var vm = GridFirstMultiForm();
            var home = vm.CurrentProject.Forms[0].Id;
            var nested = vm.CurrentProject.Forms[1].Id;
            vm.SetActiveForm(home, "beta-home");
            var input = vm.CreateControl("TextBox", 0, 0, vm.Controls.Single(c => c.Name == "NavigationBar").Id);
            input.Name = "HomeInput"; input.Text = "Сохранённый текст"; input.WidthMode = "Fixed"; input.Width = 180;
            input.Margin = "4,2"; input.Padding = 6; input.Opacity = .85; input.Foreground = "#FF123456";
            vm.SetActiveForm(nested, "beta-nested");
            var dock = vm.CreateControl("Group", 0, 0); dock.Name = "NestedDock"; dock.ChildLayoutMode = DesignerLayoutModes.Dock;
            var stack = vm.CreateControl("StackPanel", 0, 0, dock.Id); stack.Name = "EditorStack"; stack.LayoutOrientation = "Horizontal"; stack.LayoutSpacing = 8;
            foreach (var c in vm.Controls.Where(c => c.Name is "GridInput" or "GridApply")) { c.ParentId = stack.Id; c.Margin = "4"; }
            vm.SetActiveForm(home, "beta-ready");
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(180); GridFirstVisible(window, vm);
            // Exercise the real Inspector callback, not just a serialized DTO.
            vm.SelectSingleControl(vm.GetControl(input.Id)!);
            var row = GetPropertyGridRow(vm, nameof(DesignControlModel.Text)); vm.CommitPropertyGridTextEdit(row, "Текст из Inspector");
            NativeItemsUiWait(180);
            RequireCapability(vm.GetControl(input.Id)!.Text == "Текст из Inspector" && vm.SelectedControl?.Id == input.Id, "Inspector did not update the selected model.");
            // Rapid form switches must preserve session selection ownership and complete trees.
            for (var i = 0; i < 12; i++)
            {
                foreach (var form in vm.CurrentProject.Forms.ToArray())
                {
                    vm.SetActiveForm(form.Id, "beta-switch"); GridFirstVisible(window, vm);
                    var c = vm.Controls.First(); vm.SelectSingleControl(c);
                    RequireCapability(ReferenceEquals(vm.SelectedControl, vm.ActiveSession.SelectedControl), "Selection belongs to a stale form session.");
                }
            }
            vm.SetActiveForm(nested, "beta-container-delete");
            var deletedIds = vm.Controls.Where(c => c.Id == dock.Id || c.ParentId == dock.Id || c.ParentId == stack.Id).Select(c => c.Id).ToHashSet();
            File.WriteAllText(Path.Combine(BetaFolder, "delete-before.json"), JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()));
            Console.WriteLine($"OBSERVED beforeDelete undo={vm.ActiveSession.UndoSnapshots.Count}; hasUndo={vm.HasUndo}; ids={string.Join(',', deletedIds)}");
            vm.SelectSingleControl(vm.GetControl(dock.Id)!); vm.DeleteSelectedCommand.Execute(null); NativeItemsUiWait(120);
            RequireCapability(vm.Controls.All(c => !deletedIds.Contains(c.Id)), "Deleting a nested container left orphan children.");
            if (Environment.GetEnvironmentVariable("FORMDESIGNER_BETA_HISTORY_DIAGNOSTICS") == "1")
            {
                var previousRedo = vm.ActiveSession.RedoSnapshots.Count;
                var historyIndex = 0;
                vm.ActiveSession.HistoryChanged += (_, _) =>
                {
                    var session = vm.ActiveSession;
                    if (previousRedo > 0 && session.RedoSnapshots.Count == 0)
                        File.WriteAllText(Path.Combine(BetaFolder, "redo-reset-stack.txt"), Environment.StackTrace);
                    previousRedo = session.RedoSnapshots.Count;
                    File.WriteAllText(Path.Combine(BetaFolder, "history-observed-" + historyIndex++ + ".json"), session.CurrentSnapshot);
                };
            }
            GridEditorUndo(vm); NativeItemsUiWait(120);
            Console.WriteLine($"OBSERVED undoBeforeSnapshot undo={vm.ActiveSession.UndoSnapshots.Count}; redo={vm.ActiveSession.RedoSnapshots.Count}");
            File.WriteAllText(Path.Combine(BetaFolder, "delete-after-undo.json"), JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()));
            Console.WriteLine($"OBSERVED afterUndo undo={vm.ActiveSession.UndoSnapshots.Count}; redo={vm.ActiveSession.RedoSnapshots.Count}; missing={string.Join(',', deletedIds.Where(id=>vm.GetControl(id) is null))}; stackParent={vm.GetControl(stack.Id)?.ParentId}; expected={dock.Id}; status={vm.StatusText}");
            RequireCapability(deletedIds.All(id => vm.GetControl(id) is not null) && vm.GetControl(stack.Id)!.ParentId == dock.Id,
                "Undo lost the deleted subtree or ParentId.");
            RequireCapability(vm.HasRedo, "Native layout refresh invalidated Redo after restoring a deleted subtree.");
            GridEditorUndo(vm, true); RequireCapability(vm.Controls.All(c => !deletedIds.Contains(c.Id)), "Native Redo failed to delete the same subtree.");
            GridEditorUndo(vm); RequireCapability(deletedIds.All(id => vm.GetControl(id) is not null), "Repeated native Undo lost the subtree.");
            vm.SetActiveForm(home, "beta-save"); GridFirstVisible(window, vm);
            var path = Path.Combine(BetaFolder, "BetaWorkspace.formdesigner.json");
            var before = vm.ExportWorkspaceJson();
            var documents = BetaDocuments(vm);
            var save = (Task<bool>)typeof(MainWindow).GetMethod("SaveDocumentToHostFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { new BetaFile(path) })!;
            Pump(save); RequireCapability(save.Result && !vm.HasUnsavedWorkspaceChanges, "Physical workspace save failed or stayed dirty.");
            var oldSession = vm.ActiveSession;
            vm.NewProjectEditorCommand!.Execute(null); GridFirstVisible(window, vm);
            RequireCapability(oldSession.IsDisposed && vm.Controls.Count == 0, "New project did not release the old session.");
            vm.LoadDocumentJson(File.ReadAllText(path), path); GridFirstVisible(window, vm);
            RequireCapability(BetaDocuments(vm) == documents, "Save/Open changed IDs, hierarchy, properties, Logic, sources or DataGrid settings.");
            foreach (var form in vm.CurrentProject.Forms.ToArray())
            {
                vm.SetActiveForm(form.Id, "beta-preview"); GridFirstVisible(window, vm);
                foreach (var axaml in new[] { true, false })
                {
                    using var preview = ShowDataGridRegressionPreview(vm, axaml);
                    RequireCapability(preview.Window.Content is Control, "Preview did not create a native content tree.");
                }
            }
            vm.SetActiveForm(home, "beta-export");
            Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(BetaFolder, "export")));
            Pump(vm.ExportCurrentResultAsZipAsync(Path.Combine(BetaFolder, "BetaWorkspace.zip")));
            using var zip = ZipFile.OpenRead(Path.Combine(BetaFolder, "BetaWorkspace.zip"));
            RequireCapability(zip.Entries.Count(e => e.FullName.EndsWith(".axaml")) == 5, "ZIP did not contain all four forms.");
            File.WriteAllText(Path.Combine(BetaFolder, "workflow-result.json"), JsonSerializer.Serialize(new { Forms = 4, RapidSwitches = 48, NativePreviews = 8,
                Verified = "ControlId, ParentId, placement, all serialized properties, Logic, bindings, DataGrid; Inspector; subtree delete/Undo; physical Save/Open; ZIP" }));
            window.CloseForExternalHost(); SmokeWindows.Remove(window);
            Console.WriteLine("PASS BETA_EndToEnd: four forms; 48 native switches; Inspector; subtree delete/Undo; physical Save/New/Open; exact per-form DTOs; 8 Preview windows; folder + ZIP export.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL BETA_EndToEnd: " + error); return 1; }
        finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
    }
    private static string BetaDocuments(MainWindowViewModel vm)
    {
        vm.ExportWorkspaceJson();
        return JsonSerializer.Serialize(vm.CurrentProject.Forms.Select(f => new { f.Id, f.Name, f.Document }));
    }
    private sealed class BetaFile(string path) : IDesignerHostFile
    {
        public string Name => Path.GetFileName(path);
        public string? LocalPath => path;
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream>(File.OpenRead(path));
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream>(File.Create(path));
    }
}
