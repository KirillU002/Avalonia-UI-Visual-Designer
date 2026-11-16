using Avalonia.Controls;
using FormDesigner.DesignerSystem.Metadata;
using System.Text.Json;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    // Signature-only audit. A canonical registration collapses qualified aliases;
    // discovery and reviewed adapter flags remain separate measurements.
    private static void Write5DPropertyInventory(FormDesigner.PluginContracts.IDesignerRegistry registry,string directory)
    {
        var contextual=new HashSet<string>{"Grid.Row","Grid.Column","Grid.RowSpan","Grid.ColumnSpan","Canvas.Left","Canvas.Top","DockPanel.Dock","ToolTip.Tip"};
        var descriptors=registry.GetControls().ToDictionary(d=>d.TypeKey);
        var authorableCollections=new HashSet<string>{"Items","Children","Columns","Styles","Resources","DataTemplates","RowDefinitions","ColumnDefinitions","SelectedDates","BlackoutDates","Classes","Transitions","KeyBindings","GestureRecognizers","Inlines","FontFeatures"};
        var rows=AvaloniaMetadataCatalog.DiscoverControls().SelectMany(type=>AvaloniaMetadataCatalog.Get(type).Properties.Select(p=>new{
            Control=type.FullName,Assembly=type.Assembly.GetName().Name,Key=p.Key,Owner=p.OwnerType.FullName,p.Name,p.Kind,p.ReadOnly,p.Attached,
            Identity=p.Property is {} ap?"AP:"+ap.OwnerType.FullName+"."+ap.Name:"CLR:"+p.ClrProperty?.DeclaringType?.FullName+"."+p.Name,
            Applicable=(!p.ReadOnly||authorableCollections.Contains(p.Name))&&(!p.Attached||p.ClrProperty is not null||contextual.Contains(p.Key)),
            CollectionAdapter=AvaloniaMetadataCatalog.Get(type).Collections.Any(c=>c.PropertyName==p.Name&&c.CanEditItems),
            ContentAdapter=descriptors.TryGetValue(type.Name,out var descriptor)&&descriptor.IsContainer&&AvaloniaMetadataCatalog.Get(type).ChildProperty==p.Name&&p.Name is "Content" or "Child",
            SourceCollection=FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlControlMetadata.Find(type.Name)?.ChildProperty==p.Name,
            Reason=p.ReadOnly&&!authorableCollections.Contains(p.Name)?"Read-only diagnostic state":p.Attached&&p.ClrProperty is null&&!contextual.Contains(p.Key)?"Unrelated attached registration; excluded":"Writable native or reviewed contextual signature; complex objects stay in denominator"
        })).ToArray();
        File.WriteAllText(Path.Combine(directory,"PropertyCoverageInventory.json"),JsonSerializer.Serialize(rows));
    }
}
