using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private FormAppearance? _formAppearance;
    private Dictionary<string,string>? _styleSourceValues;
    internal string AppearanceRevision => _currentSnapshot;
    public FormAppearance AppearanceDraft() => _formAppearance?.Copy() ?? new();
    internal void ApplyFormStyle(Avalonia.Controls.Control view,DesignControlModel control)
    {
        var source = _activeAxamlRoundTripDocument;
        if (source != null && source.SourceMap.TryGet(control.Id,out var reference))
            FormStyleProjection.Apply(view,source.StyleCatalog.Sheet,control.Id,control.Name,reference.Element.LocalName,reference);
        else if (_formAppearance != null)
            FormStyleProjection.Apply(view,_formAppearance,control.Id,control.Name,control.Type);
    }
    public bool HasImportedStyleScope => _activeAxamlRoundTripDocument is not null;
    public IReadOnlyList<StyleSourceValue> StyleFields => _activeAxamlRoundTripDocument?.StyleCatalog.Fields.AsReadOnly()
        ?? (IReadOnlyList<StyleSourceValue>)Array.Empty<StyleSourceValue>();

    public IEnumerable<string> StyleNotices()
    {
        if (_activeAxamlRoundTripDocument is not {} doc) return Array.Empty<string>();
        var notices = PropertyExpressionProjection.Descendants(doc.Syntax.Root)
            .Where(n => n.LocalName is "Application.Resources" or "Styles.Resources" or "Style.Resources" or "ControlTheme" or "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate" ||
                n.LocalName.EndsWith(".Resources",StringComparison.Ordinal) && n.Parent != doc.Syntax.Root)
            .Select(n => n.Name + " · " + (n.GetAttributeValue("TargetType") ?? n.GetAttributeValue("DataType") ?? n.Parent?.Name)
                + (n.GetAttributeValue("BasedOn") is {} basis ? " · BasedOn=" + basis : "") + " — только чтение");
        return doc.StyleCatalog.Notices.Concat(notices).Distinct().ToArray();
    }

    internal string OriginalTemplateFieldValue(StyleSourceValue field) => (_axamlBufferSource ?? _activeAxamlRoundTripDocument)?.StyleCatalog.Fields.SingleOrDefault(f => f.Id == field.Id)?.Value ?? field.Value;

    public void SaveSourceStyleValue(string documentId,StyleSourceValue field,string value)
    {
        if (ActiveDocumentId != documentId || IsBusy || !StyleFields.Any(f => ReferenceEquals(f,field)))
            throw new InvalidOperationException("Документ изменился. Откройте редактор повторно.");
        var document = CreateDocumentFileModel();
        document.StyleSourceValues = new(document.StyleSourceValues ?? new()) { [field.Id] = value };
        if (!CommitAxamlStructure(document,SelectedControl?.Id)) throw new InvalidOperationException(StatusText);
    }

    public void SaveAppearance(string documentId, FormAppearance candidate)
    {
        if (ActiveDocumentId != documentId || IsBusy || HasImportedStyleScope)
            throw new InvalidOperationException("Область оформления изменилась.");
        ValidateAppearanceForExport(candidate);
        _lastHistoryMutationUtc = DateTime.MinValue;
        BeginUndoBatch();
        try { _formAppearance = candidate.Copy(); }
        finally { CommitUndoBatch(); }
    }

    private static readonly Lazy<HashSet<string>> ReservedFormResources = new(() =>
        typeof(ThemeResourceKeys).GetFields(BindingFlags.Public|BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal));

    private static void ValidateAppearanceForExport(FormAppearance sheet)
    {
        FormStyleAuthoring.Validate(sheet);
        if (sheet.Resources.Any(r => ReservedFormResources.Value.Contains(r.Key)))
            throw new InvalidOperationException("Ключ занят ресурсами оформления Designer.");
    }

    private static void AppendAuthoredResources(StringBuilder sb,FormAppearance sheet)
    {
        foreach (var r in sheet.Resources.Where(r => r.Variant.Length == 0))
            sb.Append("    ").AppendLine(FormStyleAuthoring.ResourceXml(r));
        var groups = sheet.Resources.Where(r => r.Variant.Length > 0).GroupBy(r => r.Variant).ToArray();
        if (groups.Length == 0) return;
        sb.AppendLine("      <ResourceDictionary.ThemeDictionaries>");
        foreach (var group in groups)
        {
            sb.Append("        <ResourceDictionary x:Key=\"").Append(group.Key).AppendLine("\">");
            foreach (var r in group) sb.Append("          ").AppendLine(FormStyleAuthoring.ResourceXml(r));
            sb.AppendLine("        </ResourceDictionary>");
        }
        sb.AppendLine("      </ResourceDictionary.ThemeDictionaries>");
    }

    private string ApplyAuthoredClasses(string generated)
    {
        if (_formAppearance?.Classes.Count is not > 0) return generated;
        var root = AxamlSyntaxDocument.Parse(generated).Root;
        var names = PropertyExpressionProjection.Descendants(root).ToLookup(n => n.GetAttributeValue("x:Name"));
        var edits = new List<AxamlTextEdit>();
        foreach (var c in Controls)
        {
            if (!_formAppearance.Classes.TryGetValue(c.Id,out var value)) continue;
            var node = names[GetExportControlName(c)].Single();
            if (node.FindAttribute("Classes") is {} attr) edits.Add(new(attr.ValueSpan.End,0," "+value));
            else edits.Add(new(node.OpeningTagSpan.Start+1+node.Name.Length,0," Classes=\""+value+"\""));
        }
        return AxamlPatchWriter.ApplyEdits(generated,edits);
    }
}
