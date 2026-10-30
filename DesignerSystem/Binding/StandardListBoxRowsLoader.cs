using FormDesigner.Models;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.DesignerSystem.Binding;

internal static class StandardListBoxRowsLoader
{
    public static Task<PreviewRowsLoadResult> LoadAsync(BindingSourceModel source, CancellationToken token = default)
    {
        var real = source.Clone();
        real.UseDemoData = false; real.AllowPreviewSampleFallback = false;
        if (real.PreviewRowMode == BindingSourceModel.PreviewRowModeSampleRows) real.PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
        return PreviewRowsLoader.LoadRowsAsync(real, token, updateSourceStatus: false);
    }
}
