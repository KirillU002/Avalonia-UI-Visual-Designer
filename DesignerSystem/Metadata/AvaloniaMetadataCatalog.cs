using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FormDesigner.DesignerSystem.Metadata;

public enum MetadataEditor { Text, Boolean, Enumeration, Number, Color, Thickness, CornerRadius, GridLength, Font, Collection, ReadOnly, DateTime, TimeSpan }

public sealed record AvaloniaPropertyInfo(string Name, Type OwnerType, Type PropertyType,
    AvaloniaProperty? Property, PropertyInfo? ClrProperty, object? DefaultValue, bool HasDefaultValue,
    bool ReadOnly, bool Nullable, bool Inherited, bool InheritsValue, bool Attached, MetadataEditor Editor)
{
    // AddOwner can expose an attached-origin property as a normal CLR member
    // (e.g. TemplatedControl.FontFamily). Keep that unqualified member identity.
    public string Key => Attached && ClrProperty is null ? OwnerType.Name + "." + Name : Name;
    public bool Bindable => Property is not null && !ReadOnly;
    public string Kind => Property is null ? "CLR" : Property.IsAttached ? "AttachedProperty" : Property.IsDirect ? "DirectProperty" : "StyledProperty";
    public string DefaultText => HasDefaultValue ? MetadataValueCodec.Display(DefaultValue) : "<not available>";
}

public sealed record AvaloniaTypeInfo(Type Type, string ContainerKind, string? ChildProperty,
    bool IsVisual, bool ToolboxCandidate, string CandidateReason, IReadOnlyList<AvaloniaPropertyInfo> Properties)
{
    public IReadOnlyList<CollectionPropertyMetadata> Collections { get; init; } = Array.Empty<CollectionPropertyMetadata>();
}

/// <summary>
/// Discovery is not a support declaration. Only the shipped Avalonia assemblies are enumerated.
/// Ephemeron keys allow collectible plugin types to die even when metadata references their Type.
/// For other assemblies only CLR signatures and trusted base properties are inspected: no getters,
/// constructors or user static initializers are executed.
/// </summary>
public static class AvaloniaMetadataCatalog
{
    private static readonly ConditionalWeakTable<Type, Lazy<AvaloniaTypeInfo>> Cache = new();
    private static readonly Assembly[] StandardAssemblies = { typeof(Control).Assembly, typeof(AvaloniaObject).Assembly, typeof(DataGrid).Assembly,
        typeof(ColorPicker).Assembly, typeof(ItemsRepeater).Assembly };
    private static readonly Lazy<IReadOnlySet<AvaloniaProperty>> AttachedIndex = new(CreateAttachedIndex);
    private static int _buildCount;
    public static int BuildCount => Volatile.Read(ref _buildCount);
    public static bool IsStandard(Type type) => StandardAssemblies.Contains(type.Assembly);
    internal static IEnumerable<Assembly> FrameworkAssemblies => StandardAssemblies.Concat(new[]
    {
        typeof(Avalonia.Markup.Xaml.AvaloniaXamlLoader).Assembly, typeof(Avalonia.Data.Binding).Assembly
    }).Distinct();
    public static IReadOnlyList<Type> DiscoverControls() => StandardAssemblies.Distinct()
        .SelectMany(a => a.GetExportedTypes()).Where(t => typeof(Control).IsAssignableFrom(t) && !t.ContainsGenericParameters)
        .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
    public static AvaloniaTypeInfo Get(Type type) => Cache.GetValue(type,
        key => new Lazy<AvaloniaTypeInfo>(() => Build(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static IReadOnlySet<AvaloniaProperty> CreateAttachedIndex()
    {
        var result = new HashSet<AvaloniaProperty>();
        // Initialize only public attached-property declarations in shipped assemblies.
        // Registry.GetRegisteredAttached takes a host type, not an owner type. Its
        // contents otherwise depend on which framework owners were initialized first.
        var fields = StandardAssemblies.Distinct().SelectMany(a => a.GetExportedTypes()).Where(t => !t.ContainsGenericParameters)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(AttachedProperty<>));
        foreach (var field in fields)
        {
            RuntimeHelpers.RunClassConstructor(field.DeclaringType!.TypeHandle);
            if (field.GetValue(null) is AvaloniaProperty { IsAttached: true } property) result.Add(property);
        }
        return result;
    }

    private static AvaloniaTypeInfo Build(Type type)
    {
        Interlocked.Increment(ref _buildCount);
        var trusted = IsStandard(type);
        var properties = new Dictionary<string, AvaloniaPropertyInfo>(StringComparer.Ordinal);
        var ancestors = new Stack<Type>();
        for (var current = type; current is not null && typeof(AvaloniaObject).IsAssignableFrom(current); current = current.BaseType)
            if (IsStandard(current)) ancestors.Push(current);
        // Avalonia registration is performed by standard control static initializers.
        foreach (var ancestor in ancestors) RuntimeHelpers.RunClassConstructor(ancestor.TypeHandle);
        var attachedIndex = AttachedIndex.Value;
        var publicProperties = ancestors.SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => typeof(AvaloniaProperty).IsAssignableFrom(f.FieldType)).Select(f => f.GetValue(null))
            .OfType<AvaloniaProperty>().ToHashSet();
        var registry = AvaloniaPropertyRegistry.Instance;
        foreach (var ancestor in ancestors)
        foreach (var property in registry.GetRegistered(ancestor).Concat(registry.GetRegisteredDirect(ancestor)))
            Add(property, false);
        foreach (var ancestor in ancestors)
        foreach (var property in registry.GetRegisteredAttached(ancestor))
            if (attachedIndex.Contains(property)) Add(property, true);
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || properties.ContainsKey(property.Name)) continue;
            properties[property.Name] = new(property.Name, property.DeclaringType!, property.PropertyType, null, property, null, false,
                property.SetMethod?.IsPublic != true, IsNullable(property.PropertyType), property.DeclaringType != type, false, false,
                MetadataValueCodec.EditorFor(property.PropertyType));
        }
        var container = typeof(Panel).IsAssignableFrom(type) ? ("Children", "Children")
            : type == typeof(Viewbox) ? ("Decorator", "Child")
            : typeof(Decorator).IsAssignableFrom(type) ? ("Decorator", "Child")
            : typeof(HeaderedContentControl).IsAssignableFrom(type) ? ("HeaderedContent", "Content")
            : typeof(ItemsControl).IsAssignableFrom(type) ? ("Items", "Items")
            : typeof(ContentControl).IsAssignableFrom(type) ? ("Content", "Content")
            : ("None", (string?)null);
        var infrastructure = type.Namespace?.Contains(".Primitives", StringComparison.Ordinal) == true && type != typeof(ScrollBar) && type != typeof(UniformGrid) && type != typeof(ToggleButton) && type != typeof(AccessText)
            || type.Namespace?.Contains(".Presenters", StringComparison.Ordinal) == true
            || type.Name.EndsWith("Presenter", StringComparison.Ordinal) || type.Name.EndsWith("Panel", StringComparison.Ordinal) && type.Name.Contains("Virtualiz", StringComparison.Ordinal)
            || type.Name is "CaptionButtons" or "TitleBar" or "EmbeddableControlRoot" or "NativeControlHost" or "NativeMenuBar"
                or "DataGridCell" or "DataGridColumnHeader" or "DataGridRow" or "DataGridRowGroupHeader"
                or "DataValidationErrors" or "NotificationCard" or "WindowNotificationManager" or "TickBar" or "RefreshVisualizer";
        var popup = type == typeof(ContextMenu) || type == typeof(ToolTip);
        var candidate = trusted && typeof(Control).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null
            && !infrastructure && !popup && type != typeof(Window) && type != typeof(UserControl)
            && type != typeof(Control) && type != typeof(ContentControl) && type != typeof(ItemsControl) && type != typeof(Decorator)
            && type != typeof(Panel) && type != typeof(HeaderedContentControl) && type != typeof(TemplatedControl);
        return new(type, container.Item1, container.Item2, typeof(Control).IsAssignableFrom(type), candidate,
            !trusted ? "Plugin boundary" : type.IsAbstract ? "Abstract" : infrastructure ? "Infrastructure" : popup ? "Popup; specialized attachment workflow"
                : candidate ? "Candidate; pipeline verification required" : "Root/base/constructor policy",
            properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()) { Collections = CollectionMetadataCatalog.Discover(type) };

        void Add(AvaloniaProperty property, bool attached)
        {
            if (attached && !property.IsAttached) return;
            var clr = attached ? null : type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p => p.Name == property.Name);
            // Class selectors and framework helpers also register private runtime
            // properties. They are not public component metadata or authorable AXAML.
            if (!attached && clr is null && !publicProperties.Contains(property)) return;
            // Metadata APIs do not invoke an instance property getter.
            var metadata = property.GetMetadata(trusted ? type : property.OwnerType);
            var hasDefault = metadata is IStyledPropertyMetadata or IDirectPropertyMetadata;
            var value = metadata is IStyledPropertyMetadata styled ? styled.DefaultValue
                : metadata is IDirectPropertyMetadata direct ? direct.UnsetValue : null;
            var item = new AvaloniaPropertyInfo(property.Name, property.OwnerType, property.PropertyType, property, clr, value, hasDefault,
                property.IsReadOnly, IsNullable(property.PropertyType), !attached && (clr?.DeclaringType ?? property.OwnerType) != type, property.Inherits, property.IsAttached,
                MetadataValueCodec.EditorFor(property.PropertyType));
            properties[item.Key] = item;
        }
    }
    private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}

/// <summary>Invariant, deliberately bounded literal codec. Complex objects never fall back to ToString().</summary>
public static class MetadataValueCodec
{
    public const string NullText = "(null)";
    public static MetadataEditor EditorFor(Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type == typeof(BoxShadows)) return MetadataEditor.Text;
        if(type == typeof(TextTrimming)) return MetadataEditor.Enumeration;
        if (type == typeof(string) || type == typeof(char) || type == typeof(Uri) || type == typeof(Point)) return MetadataEditor.Text;
        if (type == typeof(bool)) return Nullable.GetUnderlyingType(declared) is null ? MetadataEditor.Boolean : MetadataEditor.Enumeration;
        if (type.IsEnum) return MetadataEditor.Enumeration;
        if (type == typeof(int) || type == typeof(double) || type == typeof(float) || type == typeof(long) || type == typeof(decimal)) return MetadataEditor.Number;
        if (type == typeof(Color) || typeof(IBrush).IsAssignableFrom(type)) return MetadataEditor.Color;
        if (type == typeof(Thickness)) return MetadataEditor.Thickness;
        if (type == typeof(CornerRadius)) return MetadataEditor.CornerRadius;
        if (type == typeof(GridLength)) return MetadataEditor.GridLength;
        if (type == typeof(FontFamily)) return MetadataEditor.Font;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return MetadataEditor.DateTime;
        if (type == typeof(TimeSpan)) return MetadataEditor.TimeSpan;
        return typeof(IEnumerable).IsAssignableFrom(type) ? MetadataEditor.Collection : MetadataEditor.ReadOnly;
    }
    public static string Display(object? value) => value switch
    {
        TextTrimming trimming when ReferenceEquals(trimming,TextTrimming.None) => "None",
        TextTrimming trimming when ReferenceEquals(trimming,TextTrimming.CharacterEllipsis) => "CharacterEllipsis",
        TextTrimming trimming when ReferenceEquals(trimming,TextTrimming.WordEllipsis) => "WordEllipsis",
        TextTrimming => "<TextTrimming>",
        BoxShadows shadows => shadows.ToString(),
        null => NullText, string text => text, char character => character.ToString(), Uri uri => uri.OriginalString, bool flag => flag ? "True" : "False",
        Enum item => item.ToString(), double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture), int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture), decimal number => number.ToString(CultureInfo.InvariantCulture),
        Color color => color.ToString(), ISolidColorBrush brush => brush.Color.ToString(), Thickness thickness => thickness.ToString(),
        CornerRadius radius => radius.ToString(), GridLength length => length.ToString(), FontFamily font => font.Name,
        RelativePoint origin => origin.ToString(),
        Rect rect => rect.ToString(), Size size => size.ToString(), Point point => point.ToString(), Vector vector => vector.ToString(),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture), DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => "<" + value.GetType().Name + ">"
    };
    public static bool TryParse(Type declared, string text, out object? value)
    {
        value = null;
        if (text == NullText || text == "{x:Null}") return !declared.IsValueType || Nullable.GetUnderlyingType(declared) is not null;
        if (text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return false;
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        try
        {
            if (type == typeof(string)) { value = text; return !text.TrimStart().StartsWith("{", StringComparison.Ordinal); }
            if (type == typeof(Uri))
            {
                if (text.Length > 8192 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
                value=uri; return true;
            }
            if(type==typeof(TextTrimming)) { value=text switch {"None"=>TextTrimming.None,"CharacterEllipsis"=>TextTrimming.CharacterEllipsis,"WordEllipsis"=>TextTrimming.WordEllipsis,_=>null};return value is not null; }
            if(type==typeof(BoxShadows))
            {
                if(text.Length>4096)return false;
                var shadows=BoxShadows.Parse(text);if(shadows.Count>16)return false;
                for(var i=0;i<shadows.Count;i++){var shadow=shadows[i];if(!double.IsFinite(shadow.OffsetX)||!double.IsFinite(shadow.OffsetY)||!double.IsFinite(shadow.Blur)||!double.IsFinite(shadow.Spread)||shadow.Blur<0||Math.Abs(shadow.OffsetX)>1e6||Math.Abs(shadow.OffsetY)>1e6||shadow.Blur>1e6||Math.Abs(shadow.Spread)>1e6)return false;}
                value=shadows;
            }
            else if (type == typeof(char)) { if(text.Length != 1 || char.IsSurrogate(text[0])) return false; value=text[0]; }
            else if (type == typeof(bool)) value = bool.Parse(text);
            else if (type.IsEnum) { value = Enum.Parse(type, text, true); if (!Enum.IsDefined(type, value) && !type.IsDefined(typeof(FlagsAttribute), false)) return false; }
            else if (type == typeof(double)) { var n = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); if (!double.IsFinite(n)) return false; value = n; }
            else if (type == typeof(float)) { var n = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); if (!float.IsFinite(n)) return false; value = n; }
            else if (type == typeof(int)) value = int.Parse(text, CultureInfo.InvariantCulture);
            else if (type == typeof(long)) value = long.Parse(text, CultureInfo.InvariantCulture);
            else if (type == typeof(decimal)) value = decimal.Parse(text, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            else if (type == typeof(Color)) value = Color.Parse(text);
            else if (typeof(IBrush).IsAssignableFrom(type)) { var brush = Brush.Parse(text); if (brush is not ISolidColorBrush) return false; value = brush; }
            else if (type == typeof(Thickness))
            {
                var thickness = Thickness.Parse(text);
                if (!double.IsFinite(thickness.Left) || !double.IsFinite(thickness.Top) || !double.IsFinite(thickness.Right) || !double.IsFinite(thickness.Bottom)) return false;
                value = thickness;
            }
            else if (type == typeof(CornerRadius))
            {
                var radius = CornerRadius.Parse(text);
                if (!double.IsFinite(radius.TopLeft) || !double.IsFinite(radius.TopRight) || !double.IsFinite(radius.BottomLeft) || !double.IsFinite(radius.BottomRight)) return false;
                value = radius;
            }
            else if (type == typeof(GridLength))
            {
                var length = GridLength.Parse(text); if (!double.IsFinite(length.Value)) return false; value = length;
            }
            else if (type == typeof(FontFamily)) value = new FontFamily(text);
            else if (type == typeof(Point)) { var point=Point.Parse(text);if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||Math.Abs(point.X)>1e6||Math.Abs(point.Y)>1e6)return false;value=point; }
            else if (type == typeof(DateTime)) value = DateTime.ParseExact(text, new[] { "yyyy-MM-dd", "O", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            else if (type == typeof(DateTimeOffset)) value = DateTimeOffset.ParseExact(text, new[] { "yyyy-MM-dd", "O" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
            else if (type == typeof(TimeSpan)) value = TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture);
            else return false;
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException) { return false; }
    }
}
