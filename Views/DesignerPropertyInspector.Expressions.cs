using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.ViewModels;
using System.Linq;

namespace FormDesigner.Views;

public partial class DesignerPropertyInspector
{
    private PropertyGridRowViewModel? _expressionRow;
    private void TrackExpressionTarget(object? sender,PointerPressedEventArgs e)
    {
        if(e.Source is not Control control)return;
        var row=control.GetVisualAncestors().OfType<Control>().Prepend(control).Select(c=>c.DataContext).OfType<PropertyGridRowViewModel>().FirstOrDefault();
        if(row is null)return;
        _expressionRow=row;
        this.FindControl<Button>("ExpressionEditorButton")!.IsEnabled=row.CanEditExpression;
        this.FindControl<TextBlock>("ExpressionTargetText")!.Text=row.Label;
    }
    private void ClearExpressionTarget()
    {
        _expressionRow=null;
        if(this.FindControl<Button>("ExpressionEditorButton")is {} button)button.IsEnabled=false;
        if(this.FindControl<TextBlock>("ExpressionTargetText")is {} label)label.Text="Выберите строку свойства";
    }
    private async void ExpressionEditor_Click(object? sender,RoutedEventArgs e)
    {
        if(Context is not MainWindowViewModel vm||_expressionRow is not {} row||vm.OpenPropertyExpressionEditor(row)is not {} context)return;
        if(TopLevel.GetTopLevel(this)is not Window owner)return;
        await new PropertyExpressionEditorWindow(vm,context).ShowDialog(owner);
    }
}
