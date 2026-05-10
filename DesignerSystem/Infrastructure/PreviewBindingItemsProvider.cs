using FormDesigner.PluginContracts;
using System;
using System.Collections;

namespace FormDesigner.DesignerSystem.Infrastructure;

internal sealed class DelegatePreviewBindingItemsProvider : IPreviewBindingItemsProvider
{
    private readonly Func<string, IEnumerable?> _resolver;

    public DelegatePreviewBindingItemsProvider(Func<string, IEnumerable?> resolver)
    {
        _resolver = resolver;
    }

    public IEnumerable? GetItems(string bindingSourceId)
    {
        return string.IsNullOrWhiteSpace(bindingSourceId)
            ? null
            : _resolver(bindingSourceId);
    }
}
