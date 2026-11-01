param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectPath).Path
$project=@(Get-ChildItem -LiteralPath $root -Filter '*.csproj')
if($project.Count -ne 1){throw 'Expected one generated project.'}
$entry=Join-Path $root 'Program.cs'
$probe=Join-Path $root 'Layout7NavigationProbe.cs'
if(Test-Path -LiteralPath $probe){throw 'Probe already exists.'}
$saved=@{}
$saved[$entry]=[IO.File]::ReadAllBytes($entry)
$code=@'
using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
public static class Layout7NavigationProbe
{
    static readonly List<Window> Windows=new();
    public static void Track(Window window)
    {
        window.Position=new PixelPoint(-4000,-4000);window.ShowInTaskbar=false;window.ShowActivated=false;
        window.Opened+=(_,_)=>Windows.Add(window);
    }
    static void Pump(int ms){using var stop=new CancellationTokenSource(ms);Dispatcher.UIThread.MainLoop(stop.Token);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Click(Window window,string name)
    {
        var button=window.FindControl<Button>(name)??throw new Exception("Missing Button "+name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(140);window.UpdateLayout();
    }
    static bool Rows(object? value)=>value is IEnumerable rows&&rows.Cast<object>().Any();
    public static void Run(AppBuilder builder,string[] args)
    {
        builder.SetupWithoutStarting();
        var mainType=Assembly.GetExecutingAssembly().GetTypes().Single(t=>t.Name=="MainWindow"&&typeof(Window).IsAssignableFrom(t));
        var home=(Window)Activator.CreateInstance(mainType)!;
        try
        {
            home.Show();Pump(200);Check(home.Content is Canvas,"Canvas home migrated.");
            Check(home.DataContext is not null&&Rows(home.FindControl<DataGrid>("HomeRows")?.ItemsSource),"Home BindingSource/VM missing.");
            var pages=new List<Window>();
            foreach(var nav in new[]{"OpenGrid","OpenNested","OpenData"})
            {
                var before=Windows.Count;Click(home,nav);Check(Windows.Count==before+1,"OpenForm Logic did not show "+nav);
                var page=Windows.Last();pages.Add(page);Check(page.Content is Grid,"Additional form lost Grid root.");
                Check(page.DataContext is not null&&!ReferenceEquals(page.DataContext,home.DataContext),"Forms share a DataContext.");
                if(nav=="OpenGrid")
                {
                    Check(Rows(page.FindControl<DataGrid>("GridRows")?.ItemsSource),"Grid DataGrid BindingSource lost.");
                    Click(page,"GridApply");Check(page.FindControl<TextBox>("GridInput")?.Text=="Grid accepted","Generated Button Logic failed.");
                }
                if(nav=="OpenNested")
                {
                    Check(page.FindControl<Control>("NestedStack")?.GetVisualDescendants().OfType<StackPanel>().Any()==true,"Nested Stack hierarchy lost.");
                    Check(page.FindControl<Control>("NestedWrap")?.GetVisualDescendants().OfType<WrapPanel>().Any()==true,"Nested Wrap hierarchy lost.");
                    Check(Rows(page.FindControl<DataGrid>("NestedRows")?.ItemsSource),"Nested DataGrid data lost.");
                    Check(page.FindControl<ListBox>("NestedStaticList")?.Items.Count==3,"Nested static ListBox items lost.");
                    var drop=page.FindControl<DropDownButton>("DropDownButton")!;drop.Flyout!.ShowAt(drop);Pump(30);Check(drop.Flyout.IsOpen,"DropDownButton flyout failed.");drop.Flyout.Hide();
                    var split=page.FindControl<SplitButton>("SplitButton")!;split.Flyout!.ShowAt(split);Pump(30);Check(split.Flyout.IsOpen,"SplitButton flyout failed.");split.Flyout.Hide();
                }
                if(nav=="OpenData")
                {
                    var grid=page.FindControl<DataGrid>("BoundGrid")!;Check(Rows(grid.ItemsSource),"DataGrid source has no explicit local fixture rows.");
                    var original=grid.Bounds.Size;page.Width+=240;page.Height+=140;Pump(180);page.UpdateLayout();
                    Check(grid.Bounds.Width>original.Width+100&&grid.Bounds.Height>original.Height+70,"DataGrid did not resize.");
                    Console.WriteLine($"MULTIFORM_DATAGRID before={original}; after={grid.Bounds.Size}; rows={grid.ItemsSource!.Cast<object>().Count()}");
                }
                var opened=Windows.Count;Click(page,"BackHome");Check(Windows.Count==opened+1,"Back navigation did not show Canvas home.");
                var back=Windows.Last();Check(back.Content is Canvas&&back.FindControl<Button>("OpenGrid") is not null,"Back navigation opened the wrong form.");
                Check(!ReferenceEquals(back.DataContext,home.DataContext)&&!ReferenceEquals(back.DataContext,page.DataContext),"New home reused another form VM.");back.Close();
                Console.WriteLine("MULTIFORM_NAVIGATION "+nav+" -> BackHome PASS");
            }
            Check(pages.Select(p=>p.DataContext).Distinct().Count()==3,"Additional pages share ViewModels.");
            foreach(var page in pages){page.Close();}
            Console.WriteLine("MULTIFORM_RUNTIME PASS forms=4; navigation=6; independentContexts=true; Canvas/Grid/Nested/DataGrid=true");
        }
        finally{foreach(var window in Windows.ToArray())window.Close();}
    }
}
'@
try{
    $original=[Text.Encoding]::UTF8.GetString($saved[$entry])
    $marker='BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)'
    if(-not $original.Contains($marker)){throw 'Entry marker missing.'}
    [IO.File]::WriteAllText($entry,$original.Replace($marker,'global::Layout7NavigationProbe.Run(BuildAvaloniaApp(), args)'),[Text.UTF8Encoding]::new($false))
    foreach($file in Get-ChildItem -LiteralPath $root -Filter '*.axaml.cs'){
        $body=[IO.File]::ReadAllText($file.FullName)
        if($body.Contains('InitializeComponent();')){
            $saved[$file.FullName]=[IO.File]::ReadAllBytes($file.FullName)
            [IO.File]::WriteAllText($file.FullName,$body.Replace('InitializeComponent();','InitializeComponent(); global::Layout7NavigationProbe.Track(this);'),[Text.UTF8Encoding]::new($false))
        }
    }
    [IO.File]::WriteAllText($probe,$code,[Text.UTF8Encoding]::new($false))
    & dotnet build $project[0].FullName -m:1 --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'Instrumented build failed.'}
    [xml]$xml=Get-Content -LiteralPath $project[0].FullName -Raw
    $assembly=Join-Path $root ('bin/Debug/'+[string]$xml.Project.PropertyGroup.TargetFramework+'/'+$project[0].BaseName+'.dll')
    $out=Join-Path $root 'navigation.stdout.log';$err=Join-Path $root 'navigation.stderr.log'
    $p=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"'+$assembly+'"') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if(-not $p.WaitForExit(45000)){$p.Kill();throw 'Runtime timed out.'}
    $p.WaitForExit();Get-Content -LiteralPath $out
    if($p.ExitCode -ne 0){Get-Content -LiteralPath $err;throw 'Runtime navigation failed.'}
}finally{
    foreach($path in $saved.Keys){[IO.File]::WriteAllBytes($path,$saved[$path])}
    if(Test-Path -LiteralPath $probe){Remove-Item -LiteralPath $probe}
    & dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
}
