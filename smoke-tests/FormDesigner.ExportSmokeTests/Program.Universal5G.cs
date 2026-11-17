using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using FormDesigner.DesignerSystem.Metadata;
using System.Text.Json;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static int Audit5G()
    {
        var code=RunUniversal5EAudit();
        var directory=Environment.GetEnvironmentVariable("UNIVERSAL5E_OUTPUT_DIR")!;
        var rows=AvaloniaMetadataCatalog.DiscoverControls().SelectMany(t=>AvaloniaMetadataCatalog.Get(t).Properties.Select(p=>new{Control=t.FullName,p.Key,OwnerType=p.OwnerType.FullName,PropertyType=p.PropertyType.FullName,p.ReadOnly,p.Attached,p.Inherited,p.Kind,p.Nullable}));
        File.WriteAllText(Path.Combine(directory,"NativePropertySignatures.json"),JsonSerializer.Serialize(rows));return code;
    }
    private static int Inspect5GApi()
    {
        var names=new[]{"AutoCompleteBox","ExperimentalAcrylicBorder","LayoutTransformControl","HeaderedItemsControl","RefreshContainer","RelativePanel","ReversibleStackPanel","SplitView","ThemeVariantScope","ToggleSplitButton","TransitioningContentControl","TextBlock","TextBox","MaskedTextBox","SelectableTextBlock","Border"};
        var all=AvaloniaMetadataCatalog.DiscoverControls();
        var data=names.Select(name=>{var t=all.Single(t=>t.Name==name);var m=AvaloniaMetadataCatalog.Get(t);return new{Type=t.FullName,Assembly=t.Assembly.GetName().Name,BaseType=t.BaseType!.FullName,t.IsPublic,t.IsAbstract,m.ContainerKind,m.ChildProperty,Properties=m.Properties.Select(p=>new{p.Key,Owner=p.OwnerType.FullName,Type=p.PropertyType.FullName,p.ReadOnly,p.DefaultText,p.Kind})};});
        Directory.CreateDirectory("artifacts/universal-phase5g");File.WriteAllText("artifacts/universal-phase5g/api.json",JsonSerializer.Serialize(data));
        foreach(var text in new[]{"translate(4px,6px)","translate(4,6)","rotate(30deg)","scale(2,0.5)","skew(5deg,10deg)","translate(4px,6px) rotate(30deg) scale(2,0.5)","none"})
        {try{var v=(ITransform)TransformOperations.Parse(text);Console.WriteLine("TRANSFORM_OK "+text+" = "+v.Value);}catch(Exception e){Console.WriteLine("TRANSFORM_REJECT "+text+" "+e.GetType().Name);}}
        foreach(var text in new[]{"Underline","Underline,Strikethrough","Underline Strikethrough","None","Baseline"})
        {try{var v=TextDecorationCollection.Parse(text);Console.WriteLine("DECORATIONS_OK "+text+" = "+string.Join(",",v.Select(d=>d.Location)));}catch(Exception e){Console.WriteLine("DECORATIONS_REJECT "+text+" "+e.GetType().Name);}}
        var native=new LayoutTransformControl();
        for(var iteration=0;iteration<2;iteration++)
        {
            native.LayoutTransform=TransformOperations.Parse("rotate(90deg) scale(1.5,1)");
            native.UseRenderTransform=false;
            native.RenderTransform=TransformOperations.Parse("translate(4px,6px) rotate(15deg) scale(1.1,0.9) skew(3deg,0deg)");
            Console.WriteLine("LTC_NATIVE_PASS "+iteration+" useRender="+native.UseRenderTransform+" layout="+native.LayoutTransform?.Value);
        }
        var vm=Universal4AVm("<a:LayoutTransformControl Name='Target' LayoutTransform='rotate(90deg) scale(1.5,1)' UseRenderTransform='False' RenderTransform='translate(4px,6px) rotate(15deg) scale(1.1,0.9) skew(3deg,0deg)'/>");
        var model=vm.Controls.Single();var view=(LayoutTransformControl)StandardControlProperties.Create(CreateSmokeControlNode(model),"LayoutTransformControl");
        Console.WriteLine("LTC_FACTORY "+view.LayoutTransform?.Value);
        vm.ActiveAxamlSourceDocument!.SourceMap.TryGet(model.Id,out var reference);
        foreach(var literal in FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlControlMetadata.Find("LayoutTransformControl")!.LiteralProperties.Where(p=>p.Metadata is not null&&p.Key.Contains("Transform")))Console.WriteLine("LTC_ACCESSOR "+literal.Key+" AP="+literal.Metadata!.Property!.Name+" value="+literal.Read(model));
        FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.ApplySourceLayout(reference,model,view);
        Console.WriteLine("LTC_SOURCE_SCALARS "+view.LayoutTransform?.Value);
        return 0;
    }
}
