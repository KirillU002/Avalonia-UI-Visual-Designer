using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.Metadata;

// Reporting policy over the existing catalog, not another discovery/registration engine.
public static class StandardLibraryCoverageReport
{
    private static readonly HashSet<string> Children = new(StringComparer.Ordinal) { "ComboBoxItem","ListBoxItem","MenuItem","TabItem","TreeViewItem","TabStripItem" };
    private static readonly HashSet<string> Associated = new(StringComparer.Ordinal) { "ContextMenu","ToolTip","Popup" };
    private static readonly HashSet<string> UsablePrimitives = new(StringComparer.Ordinal) { "AccessText","ToggleButton","ToggleSplitButton","ScrollBar","GridSplitter","UniformGrid","TabStrip","TabStripItem","HeaderedItemsControl" };
    private static readonly HashSet<string> Bases = new(StringComparer.Ordinal) { "Control","Panel","Decorator","HeaderedContentControl","TemplatedControl","SelectingItemsControl","Spinner","RangeBase","WindowBase" };
    private static readonly HashSet<string> CollectionItems = new(StringComparer.Ordinal) { "ComboBoxItem","ListBoxItem","MenuItem","TabItem","TreeViewItem" };
    public static void Write(IDesignerRegistry registry,string directory,IReadOnlySet<string>? verifiedTypes=null)
    {
        Directory.CreateDirectory(directory);
        var descriptors=registry.GetControls().Where(d=>d.GetType().Assembly==typeof(StandardLibraryCoverageReport).Assembly).ToDictionary(d=>d.TypeKey,StringComparer.Ordinal);
        var discovered=AvaloniaMetadataCatalog.DiscoverControls();
        var rows=new List<LibraryControlCoverage>();
        foreach(var type in discovered){
            var schema=AvaloniaMetadataCatalog.Get(type);var role=Role(type,schema);var applicable=role is "Standalone" or "ChildOnly";
            var alias=type.Name is "Canvas" or "DockPanel"?"Group":type.Name;descriptors.TryGetValue(alias,out var descriptor);
            var collection=CollectionItems.Contains(type.Name);var source=AxamlControlMetadata.Find(type.Name)is not null;
            var verified=verifiedTypes?.Contains(type.Name)==true;
            var nativeKeys=StandardControlProperties.PropertiesFor(type.Name).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal);
            var unsupported=schema.Properties.Where(p=>!p.ReadOnly&&p.Property is not null&&!nativeKeys.Contains(p.Key)
                && descriptor?.Properties.Any(d=>d.Key==p.Key)!=true).Select(p=>p.Key).ToArray();
            // A verified scalar workflow cannot imply arbitrary Styles/Resources/Templates authoring.
            var complete=verified&&descriptor is not null&&unsupported.Length==0&&schema.Collections.All(c=>c.CanEditItems);
            var status=!applicable?"Not Applicable for Toolbox":complete?"Fully Supported":descriptor is not null||collection?"Partially Supported":source?"AXAML Import Only":schema.Properties.Count>0?"Metadata Discovered Only":"Unsupported";
            rows.Add(new(type.Name,type.Namespace??"",type.Assembly.GetName().Name??"",type.BaseType?.FullName??"",role,applicable,status,
                descriptor?.TypeKey??(collection?"Parent collection editor":"None"),descriptor is not null?"Descriptor adapter":collection?"Collection owner selection":"Not enabled",
                descriptor is not null||collection?"Existing PropertyGrid":"Not enabled",descriptor is not null||collection?"Existing JSON contract":"Not enabled",
                source?"Literal/source subset":"Opaque",source?"Minimal scalar patches; structure guarded":"Preserved opaque",
                descriptor is not null||collection?"Adapter; see runtime evidence":"Not enabled",descriptor is not null||collection?"Adapter; see runtime evidence":"Not enabled",
                descriptor is not null||collection?"Native export adapter":"Not enabled",verified?"PASS native vertical slice; bounded subset":"No new runtime acceptance claim",
                string.Join("; ",schema.Collections.Select(c=>c.PropertyName+":"+c.SerializationStrategy).Concat(StandardControlProperties.IsContainer(type.Name)?new[]{"Native ParentId hierarchy; "+StandardControlProperties.ContainerMode(type.Name)}:Array.Empty<string>())),
                role=="ChildOnly"?"Parent Items/Content editor":role=="AttachedObject"?"Attachment workflow":type.Name=="MaskedTextBox"?"Validated mask; Culture/Provider read-only":
                type.Name is "ContentControl" or "ItemsControl"?"Content/items/template adapter required":type.Name is "Path" or "Polygon" or "Polyline"?"Bounded literals; object geometry read-only":"Complex values retain capability guards",
                verified,unsupported.Length,applicable?"":role+"; excluded from placement denominator"));
        }
        // Associated non-Control objects are audited separately, never inflated into visual-control coverage.
        foreach(var name in new[]{"NativeMenu","NativeMenuItem","Flyout","MenuFlyout"}){
            var type=typeof(Control).Assembly.GetExportedTypes().FirstOrDefault(t=>t.Name==name);
            if(type is null)continue;
            rows.Add(new(type.Name,type.Namespace??"",type.Assembly.GetName().Name??"",type.BaseType?.FullName??"","AssociatedNonvisual",false,"Not Applicable for Toolbox",
                "Attachment workflow","Related object","Existing editor where implemented","Property bag where implemented","Preserve/capability subset","Guarded",
                "Related object","Related object","Specialized adapter where implemented","No new runtime acceptance claim","","Specialized attachment editor",false,0,"Not a standalone visual Control"));
        }
        var header="Type,Namespace,Assembly,BaseType,Role,Applicable,Status,Toolbox,DesignerSurface,PropertyGrid,Json,AxamlImport,AxamlRoundTrip,AxamlPreview,LegacyPreview,Export,GeneratedRuntime,Collections,SpecialEditorRequirements,VerifiedScalarPipeline,UnsupportedWritableAvaloniaProperties,ExclusionReason";
        var csv=new List<string>{header};csv.AddRange(rows.Select(r=>Csv(r.Type,r.Namespace,r.Assembly,r.BaseType,r.Role,r.Applicable,r.Status,r.Toolbox,r.DesignerSurface,r.PropertyGrid,r.Json,r.AxamlImport,r.AxamlRoundTrip,r.AxamlPreview,r.LegacyPreview,r.Export,r.GeneratedRuntime,r.Collections,r.SpecialEditorRequirements,r.VerifiedScalarPipeline,r.UnsupportedWritableAvaloniaProperties,r.ExclusionReason)));
        File.WriteAllLines(Path.Combine(directory,"StandardLibraryCoverage.csv"),csv,new UTF8Encoding(false));
        var families=rows.GroupBy(r=>r.Assembly).Select(g=>{
            var eligible=g.Where(r=>r.Applicable).ToArray();return new{Assembly=g.Key,Discovered=g.Count(),Applicable=eligible.Length,
                Standalone=eligible.Count(r=>r.Role=="Standalone"),ChildOnly=eligible.Count(r=>r.Role=="ChildOnly"),
                FullySupported=eligible.Count(r=>r.Status=="Fully Supported"),PartiallySupported=eligible.Count(r=>r.Status=="Partially Supported"),
                AxamlImportOnly=eligible.Count(r=>r.Status=="AXAML Import Only"),MetadataOnly=eligible.Count(r=>r.Status=="Metadata Discovered Only"),Unsupported=eligible.Count(r=>r.Status=="Unsupported"),
                FullPercent=eligible.Length==0?0:100d*eligible.Count(r=>r.Status=="Fully Supported")/eligible.Length,
                PartialPercent=eligible.Length==0?0:100d*eligible.Count(r=>r.Status=="Partially Supported")/eligible.Length,
                VerifiedScalarPipelines=eligible.Count(r=>r.VerifiedScalarPipeline)};}).ToArray();
        var requested=new[]{"Image","ItemsControl","ContentControl","HeaderedContentControl","ContentPresenter","TextPresenter","Label","AccessText","ToggleButton","CheckBox","RadioButton","ButtonSpinner","NumericUpDown","Calendar","CalendarDatePicker","DatePicker","TimePicker","AutoCompleteBox","Carousel","TransitioningContentControl","Menu","MenuItem","NativeMenu","ContextMenu","TreeView","TreeViewItem","TabControl","TabItem","TabStrip","TabStripItem","SelectingItemsControl","Grid","Canvas","StackPanel","DockPanel","WrapPanel","UniformGrid","ScrollViewer","Viewbox","GridSplitter","RelativePanel","Popup","Flyout","ToolTip","AdornerLayer","Decorator","VisualLayerManager","GroupBox"};
        var actual=discovered.Select(t=>t.Assembly).Append(typeof(Control).Assembly).Append(typeof(Avalonia.AvaloniaObject).Assembly).Distinct().SelectMany(a=>a.GetTypes()).ToArray();
        var checks=requested.Select(name=>{var type=actual.FirstOrDefault(t=>t.Name==name);return new{Name=name,Exists=type is not null,Public=type?.IsPublic??false,Abstract=type?.IsAbstract??false,FullName=type?.FullName,Assembly=type?.Assembly.GetName().Name,IsControl=type is not null&&typeof(Control).IsAssignableFrom(type),Role=rows.FirstOrDefault(r=>r.Type==name)?.Role??"Unavailable or nonpublic"};}).ToArray();
        File.WriteAllText(Path.Combine(directory,"StandardLibraryCoverageSummary.json"),JsonSerializer.Serialize(new{Avalonia=typeof(Control).Assembly.GetName().Version?.ToString(),Families=families,RequestedTypes=checks,
            Definition="Full requires verified pipeline and no unsupported writable native properties/collections. Bounded scalar slices remain Partial. Root/attached/infrastructure are separate; assemblies are not combined.",
            MissingRequestedTypes=checks.Where(c=>!c.Exists).Select(c=>c.Name).ToArray(),Themes="Styles/themes are dependencies, not controls; no combined theme/third-party percentage"},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static string Role(Type type,AvaloniaTypeInfo schema)
    {
        if(type.IsAbstract)return "Abstract";
        if(type==typeof(Window)||type==typeof(UserControl))return "RootWorkflow";
        if(Children.Contains(type.Name))return "ChildOnly";
        if(Associated.Contains(type.Name))return "AttachedObject";
        if(Bases.Contains(type.Name))return "ArchitecturalBase";
        if(type.Name is "ContentControl" or "ItemsControl" || UsablePrimitives.Contains(type.Name))return "Standalone";
        if(type.Namespace?.Contains("Presenters",StringComparison.Ordinal)==true)return "TemplatePart";
        return schema.ToolboxCandidate?"Standalone":"Infrastructure";
    }
    private static string Csv(params object?[] values)=>string.Join(",",values.Select(v=>"\""+(v?.ToString()??"").Replace("\"","\"\"")+"\""));
    private sealed record LibraryControlCoverage(string Type,string Namespace,string Assembly,string BaseType,string Role,bool Applicable,string Status,string Toolbox,string DesignerSurface,string PropertyGrid,string Json,string AxamlImport,string AxamlRoundTrip,string AxamlPreview,string LegacyPreview,string Export,string GeneratedRuntime,string Collections,string SpecialEditorRequirements,bool VerifiedScalarPipeline,int UnsupportedWritableAvaloniaProperties,string ExclusionReason);
}
