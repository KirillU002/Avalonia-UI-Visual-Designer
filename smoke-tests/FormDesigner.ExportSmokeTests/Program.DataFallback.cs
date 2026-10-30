using FormDesigner.Models;
using FormDesigner.ViewModels;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void ConfigureDllTablePreviewSampleFallback(MainWindowViewModel vm)
    {
        ConfigureLinqToSqlDllImport(vm);
        // The old scenario claimed fallback was enabled, but never opted in.
        vm.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer").AllowPreviewSampleFallback = true;
    }

    private static void AssertDllSampleFallbackRequiresOptIn(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer");
        source.PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
        source.AllowPreviewSampleFallback = false;
        if (context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult().Count != 0)
            throw new InvalidOperationException("Metadata-only DLL must not produce sample rows without an explicit opt-in.");
    }

    private static void ConfigureDllSampleFallbackWithRealRows(MainWindowViewModel vm)
    {
        ConfigureDllTablePreviewRealRows(vm);
        vm.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Preview.OrderRow").AllowPreviewSampleFallback = true;
    }

    private static void AssertDllSampleFallbackRespectsCancellation(SmokeContext context)
    {
        var source = context.ViewModel.BindingSources.Single(source => source.SourceTypeFullName == "Smoke.Data.Customers.Customer");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { context.ViewModel.LoadPreviewRowsForBindingSourceAsync(source, cancellation.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancelled DLL reads must not turn into successful sample fallback.");
    }
}
