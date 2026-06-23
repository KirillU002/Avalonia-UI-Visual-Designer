using System;
using System.Collections.Generic;

namespace FormDesigner.Localization;

public static class PropertyDescriptionProvider
{
    private static readonly Dictionary<string, string> RussianDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "Уникальное имя элемента в проекте. Используется в export, логике и привязках.",
        ["Text"] = "Текст, отображаемый пользователю.",
        ["Content"] = "Содержимое элемента, отображаемое пользователю.",
        ["Text / Content"] = "Текст или содержимое, отображаемое пользователю.",
        ["Title"] = "Заголовок формы или элемента.",
        ["FormTitle"] = "Заголовок окна формы.",
        ["Width"] = "Ширина элемента в пикселях.",
        ["Height"] = "Высота элемента в пикселях.",
        ["DesignWidth"] = "Ширина формы в пикселях.",
        ["DesignHeight"] = "Высота формы в пикселях.",
        ["X"] = "Позиция элемента по горизонтали на Canvas.",
        ["Y"] = "Позиция элемента по вертикали на Canvas.",
        ["Canvas.Left"] = "Позиция элемента по горизонтали на Canvas.",
        ["Canvas.Top"] = "Позиция элемента по вертикали на Canvas.",
        ["ZIndex"] = "Порядок наложения элемента: большее значение располагает элемент выше.",
        ["IsVisible"] = "Определяет, отображается ли элемент на форме.",
        ["IsLocked"] = "Блокирует перемещение и изменение размера элемента в Designer.",
        ["Background"] = "Цвет фона элемента.",
        ["Foreground"] = "Цвет текста или содержимого элемента.",
        ["BorderBrush"] = "Цвет рамки элемента.",
        ["BorderThickness"] = "Толщина рамки элемента.",
        ["CornerRadius"] = "Радиус скругления углов.",
        ["Opacity"] = "Прозрачность элемента от 0 до 1.",
        ["FontFamily"] = "Шрифт текста элемента.",
        ["FontSize"] = "Размер шрифта.",
        ["FontWeight"] = "Насыщенность шрифта.",
        ["Margin"] = "Внешний отступ элемента в Layout-контейнерах.",
        ["Padding"] = "Внутренний отступ содержимого элемента.",
        ["HorizontalAlignment"] = "Горизонтальное выравнивание внутри Layout-контейнера.",
        ["VerticalAlignment"] = "Вертикальное выравнивание внутри Layout-контейнера.",
        ["HorizontalContentAlignment"] = "Горизонтальное выравнивание содержимого внутри элемента.",
        ["VerticalContentAlignment"] = "Вертикальное выравнивание содержимого внутри элемента.",
        ["Watermark"] = "Подсказка, отображаемая в пустом поле ввода.",
        ["PlaceholderText"] = "Подсказка, отображаемая в пустом поле ввода.",
        ["ImageSource"] = "Путь или URI изображения.",
        ["Stretch"] = "Режим масштабирования изображения.",
        ["WindowState"] = "Начальное состояние окна при запуске.",
        ["StartupLocation"] = "Начальное положение окна при запуске.",
        ["Layout Type"] = "Layout-режим корневой формы.",
        ["SurfaceLayoutMode"] = "Layout-режим корневой формы.",
        ["Orientation"] = "Направление размещения дочерних элементов.",
        ["Spacing"] = "Расстояние между дочерними элементами.",
        ["Columns"] = "Колонки DataGrid или количество колонок Grid, в зависимости от выбранного элемента.",
        ["Rows"] = "Количество строк в Grid.",
        ["ColumnDefinitions"] = "Определения колонок Grid: Auto, *, 2*, 160.",
        ["RowDefinitions"] = "Определения строк Grid: Auto, *, 120.",
        ["Grid.Row"] = "Индекс строки родительского Grid.",
        ["Grid.Column"] = "Индекс колонки родительского Grid.",
        ["RowSpan"] = "Количество строк Grid, занимаемых элементом.",
        ["ColumnSpan"] = "Количество колонок Grid, занимаемых элементом.",
        ["StackPanel.Order"] = "Порядок элемента внутри родительского StackPanel.",
        ["Children Layout"] = "Layout-режим дочерних элементов внутри контейнера.",
        ["ShowGridLines"] = "Показывает линии Grid в Designer.",
        ["AnchorLeft"] = "Привязка элемента к левому краю формы.",
        ["AnchorTop"] = "Привязка элемента к верхнему краю формы.",
        ["AnchorRight"] = "Привязка элемента к правому краю формы.",
        ["AnchorBottom"] = "Привязка элемента к нижнему краю формы.",
        ["ItemsSource"] = "Коллекция данных, отображаемая в DataGrid.",
        ["BindingSource"] = "Источник данных, к которому привязан DataGrid.",
        ["AutoGenerateColumns"] = "Автоматически создавать колонки DataGrid из схемы источника.",
        ["HeaderBackground"] = "Цвет фона заголовков колонок DataGrid.",
        ["RowBackground"] = "Цвет фона строк DataGrid.",
        ["AlternateRowBackground"] = "Цвет фона чередующихся строк DataGrid.",
        ["GridLineBrush"] = "Цвет линий сетки DataGrid.",
        ["RowHeight"] = "Высота строки DataGrid.",
        ["HeaderHeight"] = "Высота заголовка DataGrid.",
        ["ShowHeader"] = "Показывает заголовки колонок DataGrid.",
        ["AllowFilter"] = "Показывает строку быстрого фильтра DataGrid.",
        ["FilterMode"] = "Режим поиска совпадений при фильтрации.",
        ["GroupPanel"] = "Показывает панель группировки DataGrid.",
        ["AllowGrouping"] = "Разрешает группировку строк DataGrid.",
        ["AllowSort"] = "Разрешает сортировку данных по колонкам.",
        ["FooterSummaryRow"] = "Показывает строку итогов DataGrid.",
        ["DataGridExportMode"] = "Режим export для DataGrid.",
        ["RuntimeNuGetRequired"] = "NuGet package, необходимый для текущего режима DataGrid.",
        ["TextAlignment"] = "Выравнивание текста в ячейках DataGrid.",
        ["CellPadding"] = "Внутренний отступ ячеек DataGrid.",
        ["CanUserSortColumns"] = "Разрешает сортировку данных по колонкам.",
        ["CanUserResizeColumns"] = "Разрешает изменять ширину колонок мышью.",
        ["SortMemberPath"] = "Имя свойства строки, по которому сортируется колонка DataGrid.",
        ["TextWrapping"] = "Переносить длинный текст на новую строку.",
        ["TextTrimming"] = "Обрезать текст, который не помещается в ячейке.",
        ["WidthMode"] = "Способ задания ширины колонки: Auto, Pixel или Star.",
        ["WidthValue"] = "Числовое значение ширины колонки.",
        ["MinWidth"] = "Минимальная ширина элемента или колонки.",
        ["MaxWidth"] = "Максимальная ширина элемента или колонки.",
        ["MinHeight"] = "Минимальная высота элемента.",
        ["MaxHeight"] = "Максимальная высота элемента.",
        ["IsReadOnly"] = "Запрещает редактирование значения пользователем.",
        ["IsVisibleColumn"] = "Определяет, отображается ли колонка DataGrid.",
        ["DisplayOrder"] = "Порядок отображения колонки.",
        ["FormatString"] = "Формат отображения значения.",
        ["Selection"] = "Сводка по текущему выделению.",
        ["Interactions"] = "Правила Logic, связанные с этим элементом."
    };

    public static string GetDescription(string propertyName, string fallback = "")
    {
        if (TryGetDescription(propertyName, out var description))
            return description;

        return fallback ?? string.Empty;
    }

    public static bool TryGetDescription(string propertyName, out string description)
    {
        description = string.Empty;
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        return RussianDescriptions.TryGetValue(propertyName.Trim(), out description!);
    }
}
