using System.Collections.Generic;

namespace FormDesigner.Localization;

public enum UiLanguage
{
    Russian,
    English
}

public sealed class UiTextCatalog
{
    private readonly Dictionary<string, string> _values;

    public UiTextCatalog(UiLanguage language, Dictionary<string, string> values)
    {
        Language = language;
        _values = values;
    }

    public UiLanguage Language { get; }

    public int KeyCount => _values.Count;

    public string NewProject => Get(nameof(NewProject));
    public string OpenProject => Get(nameof(OpenProject));
    public string Save => Get(nameof(Save));
    public string SaveAs => Get(nameof(SaveAs));
    public string Undo => Get(nameof(Undo));
    public string Redo => Get(nameof(Redo));
    public string Preview => Get(nameof(Preview));
    public string Export => Get(nameof(Export));
    public string Help => Get(nameof(Help));
    public string More => Get(nameof(More));
    public string UnsavedChanges => Get(nameof(UnsavedChanges));
    public string Mode => Get(nameof(Mode));
    public string RestoreBackup => Get(nameof(RestoreBackup));
    public string ResetInteractionState => Get(nameof(ResetInteractionState));
    public string OpenInteractionTrace => Get(nameof(OpenInteractionTrace));
    public string RecentFiles => Get(nameof(RecentFiles));
    public string ProjectStructure => Get(nameof(ProjectStructure));
    public string Components => Get(nameof(Components));
    public string Explorer => Get(nameof(Explorer));
    public string AddForm => Get(nameof(AddForm));
    public string AddAsset => Get(nameof(AddAsset));
    public string Done => Get(nameof(Done));
    public string Open => Get(nameof(Open));
    public string Rename => Get(nameof(Rename));
    public string DuplicateForm => Get(nameof(DuplicateForm));
    public string Delete => Get(nameof(Delete));
    public string ValidateBuild => Get(nameof(ValidateBuild));
    public string ExportToProject => Get(nameof(ExportToProject));
    public string LoadedDlls => Get(nameof(LoadedDlls));
    public string DllSearchWatermark => Get(nameof(DllSearchWatermark));
    public string Details => Get(nameof(Details));
    public string CopyPath => Get(nameof(CopyPath));
    public string Reload => Get(nameof(Reload));
    public string Remove => Get(nameof(Remove));
    public string Sources => Get(nameof(Sources));
    public string Types => Get(nameof(Types));
    public string Tables => Get(nameof(Tables));
    public string ErrorsWarnings => Get(nameof(ErrorsWarnings));
    public string Properties => Get(nameof(Properties));
    public string Data => Get(nameof(Data));
    public string Layout => Get(nameof(Layout));
    public string Plugins => Get(nameof(Plugins));
    public string Logic => Get(nameof(Logic));
    public string ResetView => Get(nameof(ResetView));
    public string Basic => Get(nameof(Basic));
    public string Collapse => Get(nameof(Collapse));
    public string SearchProperties => Get(nameof(SearchProperties));
    public string ExportPipeline => Get(nameof(ExportPipeline));
    public string Refresh => Get(nameof(Refresh));
    public string ExportZip => Get(nameof(ExportZip));
    public string GeneratedFiles => Get(nameof(GeneratedFiles));
    public string CopyFile => Get(nameof(CopyFile));
    public string BuildValidation => Get(nameof(BuildValidation));
    public string OpenValidationFolder => Get(nameof(OpenValidationFolder));
    public string OpenFullOutput => Get(nameof(OpenFullOutput));
    public string RequiredPackages => Get(nameof(RequiredPackages));
    public string NoAdditionalPackagesRequired => Get(nameof(NoAdditionalPackagesRequired));
    public string ExportDiagnostics => Get(nameof(ExportDiagnostics));
    public string NoExportWarningsOrErrors => Get(nameof(NoExportWarningsOrErrors));
    public string DataSources => Get(nameof(DataSources));
    public string Add => Get(nameof(Add));
    public string Create => Get(nameof(Create));
    public string Fields => Get(nameof(Fields));
    public string EditColumns => Get(nameof(EditColumns));
    public string OpenLogic => Get(nameof(OpenLogic));
    public string OpenFullDataTools => Get(nameof(OpenFullDataTools));
    public string LogicRules => Get(nameof(LogicRules));
    public string RuleEditor => Get(nameof(RuleEditor));
    public string Source => Get(nameof(Source));
    public string Event => Get(nameof(Event));
    public string Action => Get(nameof(Action));
    public string Target => Get(nameof(Target));
    public string Template => Get(nameof(Template));
    public string Apply => Get(nameof(Apply));
    public string Cancel => Get(nameof(Cancel));
    public string Problems => Get(nameof(Problems));
    public string Diagnostics => Get(nameof(Diagnostics));
    public string Logs => Get(nameof(Logs));
    public string Settings => Get(nameof(Settings));
    public string Close => Get(nameof(Close));
    public string Templates => Get(nameof(Templates));
    public string RecentProjects => Get(nameof(RecentProjects));
    public string StartScreenDescription => Get(nameof(StartScreenDescription));

    public string Get(string key)
    {
        if (_values.TryGetValue(key, out var value))
            return value;

        return key;
    }
}

public static class UiText
{
    public static UiTextCatalog Russian { get; } = new(
        UiLanguage.Russian,
        new Dictionary<string, string>
        {
            ["NewProject"] = "Новый проект",
            ["OpenProject"] = "Открыть проект",
            ["Save"] = "Сохранить",
            ["SaveAs"] = "Сохранить как...",
            ["Undo"] = "Отменить",
            ["Redo"] = "Повторить",
            ["Preview"] = "Открыть Preview",
            ["Export"] = "Export",
            ["Help"] = "Справка",
            ["More"] = "Ещё",
            ["UnsavedChanges"] = "Есть изменения",
            ["Mode"] = "Режим",
            ["RestoreBackup"] = "Восстановить backup...",
            ["ResetInteractionState"] = "Сбросить interaction state",
            ["OpenInteractionTrace"] = "Открыть Interaction Trace",
            ["RecentFiles"] = "Последние файлы",
            ["ProjectStructure"] = "Структура проекта",
            ["Components"] = "Компоненты",
            ["Explorer"] = "Explorer",
            ["AddForm"] = "+ Form",
            ["AddAsset"] = "+ Asset",
            ["Done"] = "Готово",
            ["Open"] = "Открыть",
            ["Rename"] = "Переименовать",
            ["DuplicateForm"] = "Дублировать Form",
            ["Delete"] = "Удалить",
            ["ValidateBuild"] = "Проверить Build",
            ["ExportToProject"] = "Export в проект",
            ["LoadedDlls"] = "Загруженные DLL",
            ["DllSearchWatermark"] = "Поиск по DLL, path, namespace, type, table, source или column...",
            ["Details"] = "Подробности",
            ["CopyPath"] = "Копировать path",
            ["Reload"] = "Перезагрузить",
            ["Remove"] = "Удалить",
            ["Sources"] = "Источники",
            ["Types"] = "Типы",
            ["Tables"] = "Tables",
            ["ErrorsWarnings"] = "Errors / Warnings",
            ["Properties"] = "Свойства",
            ["Data"] = "Данные",
            ["Layout"] = "Layout",
            ["Plugins"] = "Plugins",
            ["Logic"] = "Логика",
            ["ResetView"] = "Сбросить вид",
            ["Basic"] = "Basic",
            ["Collapse"] = "Свернуть",
            ["SearchProperties"] = "Поиск свойств...",
            ["ExportPipeline"] = "Export Pipeline",
            ["Refresh"] = "Обновить",
            ["ExportZip"] = "Export ZIP",
            ["GeneratedFiles"] = "Generated files",
            ["CopyFile"] = "Копировать файл",
            ["BuildValidation"] = "Проверка Build",
            ["OpenValidationFolder"] = "Открыть папку проверки",
            ["OpenFullOutput"] = "Открыть полный Output",
            ["RequiredPackages"] = "Required packages",
            ["NoAdditionalPackagesRequired"] = "Дополнительные packages не нужны.",
            ["ExportDiagnostics"] = "Диагностика Export",
            ["NoExportWarningsOrErrors"] = "Ошибок и warnings экспорта нет.",
            ["DataSources"] = "Источники данных",
            ["Add"] = "Добавить",
            ["Create"] = "Создать",
            ["Fields"] = "Поля",
            ["EditColumns"] = "Редактировать колонки...",
            ["OpenLogic"] = "Открыть Logic",
            ["OpenFullDataTools"] = "Открыть все Data tools",
            ["LogicRules"] = "Правила логики",
            ["RuleEditor"] = "Редактор правила",
            ["Source"] = "Источник",
            ["Event"] = "Событие",
            ["Action"] = "Действие",
            ["Target"] = "Цель",
            ["Template"] = "Template",
            ["Apply"] = "Применить",
            ["Cancel"] = "Отмена",
            ["Problems"] = "Problems",
            ["Diagnostics"] = "Диагностика",
            ["Logs"] = "Логи",
            ["Settings"] = "Настройки",
            ["Close"] = "Закрыть",
            ["Templates"] = "Шаблоны",
            ["RecentProjects"] = "Последние проекты",
            ["StartScreenDescription"] = "Создайте Form, откройте недавний проект или перейдите к docs."
        });

    public static UiTextCatalog Current { get; } = Russian;

    public static IReadOnlySet<string> TechnicalPropertyNames { get; } = new HashSet<string>
    {
        "Name",
        "Text",
        "Content",
        "Width",
        "Height",
        "Background",
        "Foreground",
        "BorderBrush",
        "BorderThickness",
        "CornerRadius",
        "Opacity",
        "FontSize",
        "FontWeight",
        "Margin",
        "Padding",
        "ItemsSource",
        "CanUserSortColumns",
        "CanUserResizeColumns",
        "TextWrapping",
        "TextTrimming",
        "SortMemberPath",
        "HorizontalAlignment",
        "VerticalAlignment"
    };

    public static IReadOnlySet<string> TechnicalControlNames { get; } = new HashSet<string>
    {
        "Button",
        "TextBox",
        "TextBlock",
        "Border",
        "DataGrid",
        "CheckBox",
        "ComboBox",
        "Window",
        "Grid",
        "StackPanel",
        "DockPanel",
        "WrapPanel"
    };
}
