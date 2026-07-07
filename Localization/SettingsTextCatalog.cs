using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;

namespace FormDesigner.Localization;

public sealed class SettingsTextCatalog
{
    public const string LanguageRussian = "Russian";
    public const string LanguageEnglish = "English";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";
    public const string ThemeSystem = "System";

    private readonly Dictionary<string, string> _values;

    private SettingsTextCatalog(string language, Dictionary<string, string> values)
    {
        Language = language;
        _values = values;
    }

    public string Language { get; }

    public bool IsEnglish => string.Equals(Language, LanguageEnglish, StringComparison.OrdinalIgnoreCase);

    public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

    public string WindowTitle => this[nameof(WindowTitle)];
    public string HeaderTitle => this[nameof(HeaderTitle)];
    public string HeaderSubtitle => this[nameof(HeaderSubtitle)];
    public string Sections => this[nameof(Sections)];
    public string SettingsOverview => this[nameof(SettingsOverview)];
    public string StatusUnsaved => this[nameof(StatusUnsaved)];
    public string StatusSaved => this[nameof(StatusSaved)];
    public string ResetAll => this[nameof(ResetAll)];
    public string Apply => this[nameof(Apply)];
    public string Save => this[nameof(Save)];
    public string Cancel => this[nameof(Cancel)];
    public string AppliedStatus => this[nameof(AppliedStatus)];
    public string SavedStatus => this[nameof(SavedStatus)];
    public string ReadyStatus => this[nameof(ReadyStatus)];
    public string DefaultsLoadedStatus => this[nameof(DefaultsLoadedStatus)];
    public string CancelStatus => this[nameof(CancelStatus)];
    public string RequiresRestartHint => this[nameof(RequiresRestartHint)];

    public string GeneralTitle => this[nameof(GeneralTitle)];
    public string GeneralSubtitle => this[nameof(GeneralSubtitle)];
    public string GeneralDescription => this[nameof(GeneralDescription)];
    public string LanguageLabel => this[nameof(LanguageLabel)];
    public string ThemeLabel => this[nameof(ThemeLabel)];
    public string UiSizeLabel => this[nameof(UiSizeLabel)];
    public string ConfirmNewProject => this[nameof(ConfirmNewProject)];
    public string RecoveryAutosave => this[nameof(RecoveryAutosave)];

    public string InterfaceTitle => this[nameof(InterfaceTitle)];
    public string InterfaceSubtitle => this[nameof(InterfaceSubtitle)];
    public string InterfaceDescription => this[nameof(InterfaceDescription)];
    public string ExperimentalLayoutTab => this[nameof(ExperimentalLayoutTab)];
    public string PropertyTooltips => this[nameof(PropertyTooltips)];
    public string CompactInspector => this[nameof(CompactInspector)];
    public string AdvancedProperties => this[nameof(AdvancedProperties)];
    public string CanvasGrid => this[nameof(CanvasGrid)];
    public string ShowCanvasGrid => this[nameof(ShowCanvasGrid)];
    public string SnapToGrid => this[nameof(SnapToGrid)];
    public string GridStep => this[nameof(GridStep)];

    public string PreviewTitle => this[nameof(PreviewTitle)];
    public string PreviewSubtitle => this[nameof(PreviewSubtitle)];
    public string PreviewDescription => this[nameof(PreviewDescription)];
    public string RuntimeBadge => this[nameof(RuntimeBadge)];
    public string CompactRuntimeBadge => this[nameof(CompactRuntimeBadge)];
    public string AutoHideRuntimeBadge => this[nameof(AutoHideRuntimeBadge)];
    public string PreviewTopmost => this[nameof(PreviewTopmost)];
    public string PreviewZoom => this[nameof(PreviewZoom)];

    public string ExportTitle => this[nameof(ExportTitle)];
    public string ExportSubtitle => this[nameof(ExportSubtitle)];
    public string ExportDescription => this[nameof(ExportDescription)];
    public string ValidateAfterExport => this[nameof(ValidateAfterExport)];
    public string VerboseBuildLogs => this[nameof(VerboseBuildLogs)];
    public string KeepBuildArtifacts => this[nameof(KeepBuildArtifacts)];
    public string CleanOldArtifacts => this[nameof(CleanOldArtifacts)];
    public string ExportSqlConnectionString => this[nameof(ExportSqlConnectionString)];
    public string SqlSecretWarning => this[nameof(SqlSecretWarning)];
    public string BuildTimeout => this[nameof(BuildTimeout)];

    public string NuGetTitle => this[nameof(NuGetTitle)];
    public string NuGetSubtitle => this[nameof(NuGetSubtitle)];
    public string NuGetDescription => this[nameof(NuGetDescription)];
    public string UseCustomNuGetSource => this[nameof(UseCustomNuGetSource)];
    public string NuGetSourcePath => this[nameof(NuGetSourcePath)];
    public string AllowHttpSource => this[nameof(AllowHttpSource)];
    public string NuGetFallback => this[nameof(NuGetFallback)];
    public string GenerateNuGetConfig => this[nameof(GenerateNuGetConfig)];
    public string EffectiveSource => this[nameof(EffectiveSource)];
    public string NuGetConfigLocationHint => this[nameof(NuGetConfigLocationHint)];
    public string TestSource => this[nameof(TestSource)];
    public string Clear => this[nameof(Clear)];

    public string SqlTitle => this[nameof(SqlTitle)];
    public string SqlSubtitle => this[nameof(SqlSubtitle)];
    public string SqlDescription => this[nameof(SqlDescription)];
    public string UseGlobalSqlSettings => this[nameof(UseGlobalSqlSettings)];
    public string ServerName => this[nameof(ServerName)];
    public string DatabaseName => this[nameof(DatabaseName)];
    public string AuthenticationMode => this[nameof(AuthenticationMode)];
    public string UserName => this[nameof(UserName)];
    public string Password => this[nameof(Password)];
    public string SavePassword => this[nameof(SavePassword)];
    public string PasswordWarning => this[nameof(PasswordWarning)];
    public string DefaultSchema => this[nameof(DefaultSchema)];
    public string PreviewTopN => this[nameof(PreviewTopN)];
    public string ConnectionTimeout => this[nameof(ConnectionTimeout)];
    public string TrustCertificate => this[nameof(TrustCertificate)];
    public string EncryptConnection => this[nameof(EncryptConnection)];
    public string EffectiveConnectionString => this[nameof(EffectiveConnectionString)];
    public string TestConnection => this[nameof(TestConnection)];
    public string SqlConfigured => this[nameof(SqlConfigured)];
    public string SqlNotConfigured => this[nameof(SqlNotConfigured)];
    public string CheckingSqlConnection => this[nameof(CheckingSqlConnection)];

    public string LogsTitle => this[nameof(LogsTitle)];
    public string LogsSubtitle => this[nameof(LogsSubtitle)];
    public string LogsDescription => this[nameof(LogsDescription)];
    public string SaveLogsToFile => this[nameof(SaveLogsToFile)];
    public string LogLevel => this[nameof(LogLevel)];
    public string MaxLogFiles => this[nameof(MaxLogFiles)];
    public string MaxLogFileSize => this[nameof(MaxLogFileSize)];
    public string LogsFolder => this[nameof(LogsFolder)];

    public string AdvancedTitle => this[nameof(AdvancedTitle)];
    public string AdvancedSubtitle => this[nameof(AdvancedSubtitle)];
    public string AdvancedDescription => this[nameof(AdvancedDescription)];
    public string TraceDiagnostics => this[nameof(TraceDiagnostics)];
    public string DeveloperWarnings => this[nameof(DeveloperWarnings)];
    public string ResetLayoutNextStart => this[nameof(ResetLayoutNextStart)];
    public string ExperimentalDisabled => this[nameof(ExperimentalDisabled)];
    public string SettingsFile => this[nameof(SettingsFile)];

    public string OptionRussian => this[nameof(OptionRussian)];
    public string OptionEnglish => this[nameof(OptionEnglish)];
    public string OptionLight => this[nameof(OptionLight)];
    public string OptionDark => this[nameof(OptionDark)];
    public string OptionSystem => this[nameof(OptionSystem)];
    public string OptionCompact => this[nameof(OptionCompact)];
    public string OptionComfortable => this[nameof(OptionComfortable)];
    public string OptionDense => this[nameof(OptionDense)];

    public static SettingsTextCatalog ForLanguage(string? language)
    {
        return IsEnglishLanguage(language) ? English : Russian;
    }

    public static bool IsEnglishLanguage(string? language)
    {
        return string.Equals(language, LanguageEnglish, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "English", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "EN", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLanguage(string? language)
    {
        return IsEnglishLanguage(language) ? LanguageEnglish : LanguageRussian;
    }

    public IReadOnlyList<SettingsOptionModel> CreateLanguageOptions() =>
        new[]
        {
            new SettingsOptionModel(LanguageRussian, OptionRussian),
            new SettingsOptionModel(LanguageEnglish, OptionEnglish)
        };

    public IReadOnlyList<SettingsOptionModel> CreateThemeOptions() =>
        new[]
        {
            new SettingsOptionModel(ThemeLight, OptionLight),
            new SettingsOptionModel(ThemeDark, OptionDark),
            new SettingsOptionModel(ThemeSystem, OptionSystem)
        };

    public IReadOnlyList<SettingsOptionModel> CreateDensityOptions() =>
        new[]
        {
            new SettingsOptionModel(MainWindowViewModel.UiDensityCompact, OptionCompact),
            new SettingsOptionModel(MainWindowViewModel.UiDensityComfortable, OptionComfortable),
            new SettingsOptionModel(MainWindowViewModel.UiDensityDense, OptionDense)
        };

    public IReadOnlyList<SettingsOptionModel> CreateSqlAuthenticationOptions() =>
        new[]
        {
            new SettingsOptionModel(SqlServerSettingsModel.AuthWindows, "Windows Authentication"),
            new SettingsOptionModel(SqlServerSettingsModel.AuthSqlLogin, "SQL Login")
        };

    public IReadOnlyList<SettingsOptionModel> CreateLogLevelOptions() =>
        new[]
        {
            new SettingsOptionModel("Error", "Error"),
            new SettingsOptionModel("Warning", "Warning"),
            new SettingsOptionModel("Info", "Info"),
            new SettingsOptionModel("Debug", "Debug"),
            new SettingsOptionModel("Trace", "Trace")
        };

    public static SettingsTextCatalog Russian { get; } = new(
        LanguageRussian,
        new Dictionary<string, string>
        {
            [nameof(WindowTitle)] = "Настройки Avalonia Designer",
            [nameof(HeaderTitle)] = "Настройки Avalonia Designer",
            [nameof(HeaderSubtitle)] = "Единое место для интерфейса, Preview, Export, NuGet, SQL Server и diagnostics.",
            [nameof(Sections)] = "Разделы",
            [nameof(SettingsOverview)] = "Settings Center",
            [nameof(StatusUnsaved)] = "Есть несохранённые изменения",
            [nameof(StatusSaved)] = "Все изменения сохранены",
            [nameof(ResetAll)] = "Сбросить всё",
            [nameof(Apply)] = "Применить",
            [nameof(Save)] = "Сохранить",
            [nameof(Cancel)] = "Отмена",
            [nameof(AppliedStatus)] = "Настройки применены и сохранены.",
            [nameof(SavedStatus)] = "Настройки сохранены.",
            [nameof(ReadyStatus)] = "Настройки готовы к редактированию.",
            [nameof(DefaultsLoadedStatus)] = "Значения по умолчанию загружены. Нажмите «Применить» или «Сохранить».",
            [nameof(CancelStatus)] = "Изменения закрыты без применения.",
            [nameof(RequiresRestartHint)] = "Settings Window обновляется сразу. Для части старых экранов может понадобиться перезапуск.",

            [nameof(GeneralTitle)] = "Общие",
            [nameof(GeneralSubtitle)] = "Язык, тема и recovery.",
            [nameof(GeneralDescription)] = "Базовое поведение приложения и внешний вид интерфейса.",
            [nameof(LanguageLabel)] = "Язык интерфейса",
            [nameof(ThemeLabel)] = "Тема",
            [nameof(UiSizeLabel)] = "Размер UI",
            [nameof(ConfirmNewProject)] = "Подтверждать New Project при несохранённых изменениях",
            [nameof(RecoveryAutosave)] = "Автосохранение recovery-файла",

            [nameof(InterfaceTitle)] = "Интерфейс",
            [nameof(InterfaceSubtitle)] = "Inspector, Canvas и Layout.",
            [nameof(InterfaceDescription)] = "Настройки плотности работы с Canvas и Property Inspector.",
            [nameof(ExperimentalLayoutTab)] = "Включить экспериментальную вкладку Layout",
            [nameof(PropertyTooltips)] = "Показывать подсказки свойств",
            [nameof(CompactInspector)] = "Компактный Property Inspector",
            [nameof(AdvancedProperties)] = "Показывать advanced свойства",
            [nameof(CanvasGrid)] = "Canvas grid",
            [nameof(ShowCanvasGrid)] = "Показывать сетку Canvas",
            [nameof(SnapToGrid)] = "Snap to grid",
            [nameof(GridStep)] = "Шаг сетки",

            [nameof(PreviewTitle)] = "Preview",
            [nameof(PreviewSubtitle)] = "Runtime badge и поведение окна.",
            [nameof(PreviewDescription)] = "Параметры окна Preview и служебной плашки runtime.",
            [nameof(RuntimeBadge)] = "Показывать runtime-плашку в Preview",
            [nameof(CompactRuntimeBadge)] = "Компактная runtime-плашка",
            [nameof(AutoHideRuntimeBadge)] = "Автоскрытие runtime-плашки",
            [nameof(PreviewTopmost)] = "Preview открывать поверх всех окон",
            [nameof(PreviewZoom)] = "Preview масштаб по умолчанию, %",

            [nameof(ExportTitle)] = "Export / Build",
            [nameof(ExportSubtitle)] = "Build logs, artifacts и безопасность.",
            [nameof(ExportDescription)] = "Поведение генерации проекта, Validate Build и artifacts.",
            [nameof(ValidateAfterExport)] = "Validate Build after export",
            [nameof(VerboseBuildLogs)] = "Подробные Build logs",
            [nameof(KeepBuildArtifacts)] = "Keep successful build artifacts",
            [nameof(CleanOldArtifacts)] = "Автоматически очищать старые artifacts",
            [nameof(ExportSqlConnectionString)] = "Export SQL connection string in generated code",
            [nameof(SqlSecretWarning)] = "Включайте экспорт SQL connection string только для локальных или безопасных проектов. Пароль не пишется в logs открытым текстом.",
            [nameof(BuildTimeout)] = "Build timeout, seconds",

            [nameof(NuGetTitle)] = "NuGet",
            [nameof(NuGetSubtitle)] = "Sources, HTTP и generated NuGet.config.",
            [nameof(NuGetDescription)] = "Настройки restore/build для exported project.",
            [nameof(UseCustomNuGetSource)] = "Использовать пользовательский NuGet source",
            [nameof(NuGetSourcePath)] = "Source URL/path",
            [nameof(AllowHttpSource)] = "Разрешить HTTP source / allowInsecureConnections",
            [nameof(NuGetFallback)] = "Добавить nuget.org fallback",
            [nameof(GenerateNuGetConfig)] = "Генерировать NuGet.config в exported project",
            [nameof(EffectiveSource)] = "Effective source",
            [nameof(NuGetConfigLocationHint)] = "NuGet.config будет создан рядом с .sln/.csproj экспортированного проекта.",
            [nameof(TestSource)] = "Проверить source",
            [nameof(Clear)] = "Очистить",

            [nameof(SqlTitle)] = "SQL Server",
            [nameof(SqlSubtitle)] = "Server, database, auth и preview rows.",
            [nameof(SqlDescription)] = "Глобальное подключение, которое Data tab использует для SQL sources и DLL table preview.",
            [nameof(UseGlobalSqlSettings)] = "Использовать глобальные SQL Server настройки для SQL sources",
            [nameof(ServerName)] = "Имя сервера",
            [nameof(DatabaseName)] = "База данных",
            [nameof(AuthenticationMode)] = "Тип авторизации",
            [nameof(UserName)] = "Имя пользователя",
            [nameof(Password)] = "Пароль",
            [nameof(SavePassword)] = "Сохранять пароль в user settings",
            [nameof(PasswordWarning)] = "Пароль сохраняется только при включённом флаге. В logs он маскируется.",
            [nameof(DefaultSchema)] = "Схема по умолчанию",
            [nameof(PreviewTopN)] = "Количество строк Preview",
            [nameof(ConnectionTimeout)] = "Connection timeout, seconds",
            [nameof(TrustCertificate)] = "Доверять сертификату",
            [nameof(EncryptConnection)] = "Шифрование",
            [nameof(EffectiveConnectionString)] = "Итоговая connection string",
            [nameof(TestConnection)] = "Проверить подключение",
            [nameof(SqlConfigured)] = "SQL Server настроен",
            [nameof(SqlNotConfigured)] = "SQL Server не настроен: укажите имя сервера и базу данных.",
            [nameof(CheckingSqlConnection)] = "Проверяем подключение к SQL Server...",

            [nameof(LogsTitle)] = "Logs / Diagnostics",
            [nameof(LogsSubtitle)] = "Уровень логов и файлы.",
            [nameof(LogsDescription)] = "Файловые logs, уровень событий и лимиты хранения.",
            [nameof(SaveLogsToFile)] = "Сохранять logs в файл",
            [nameof(LogLevel)] = "Log level",
            [nameof(MaxLogFiles)] = "Max log files count",
            [nameof(MaxLogFileSize)] = "Max log file size, MB",
            [nameof(LogsFolder)] = "Папка logs",

            [nameof(AdvancedTitle)] = "Advanced",
            [nameof(AdvancedSubtitle)] = "Диагностика и экспериментальные флаги.",
            [nameof(AdvancedDescription)] = "Опции для разработки. Недоступные флаги явно помечены как experimental.",
            [nameof(TraceDiagnostics)] = "Trace diagnostics",
            [nameof(DeveloperWarnings)] = "Developer warnings",
            [nameof(ResetLayoutNextStart)] = "Сбросить layout при следующем запуске",
            [nameof(ExperimentalDisabled)] = "Experimental: будет подключено в следующем цикле стабилизации.",
            [nameof(SettingsFile)] = "Файл настроек",

            [nameof(OptionRussian)] = "Русский",
            [nameof(OptionEnglish)] = "English",
            [nameof(OptionLight)] = "Светлая",
            [nameof(OptionDark)] = "Тёмная",
            [nameof(OptionSystem)] = "Системная",
            [nameof(OptionCompact)] = "Компактный",
            [nameof(OptionComfortable)] = "Обычный",
            [nameof(OptionDense)] = "Плотный"
        });

    public static SettingsTextCatalog English { get; } = new(
        LanguageEnglish,
        new Dictionary<string, string>
        {
            [nameof(WindowTitle)] = "Avalonia Designer Settings",
            [nameof(HeaderTitle)] = "Avalonia Designer Settings",
            [nameof(HeaderSubtitle)] = "One place for interface, Preview, Export, NuGet, SQL Server, and diagnostics.",
            [nameof(Sections)] = "Sections",
            [nameof(SettingsOverview)] = "Settings Center",
            [nameof(StatusUnsaved)] = "Unsaved changes",
            [nameof(StatusSaved)] = "All changes saved",
            [nameof(ResetAll)] = "Reset all",
            [nameof(Apply)] = "Apply",
            [nameof(Save)] = "Save",
            [nameof(Cancel)] = "Cancel",
            [nameof(AppliedStatus)] = "Settings applied and saved.",
            [nameof(SavedStatus)] = "Settings saved.",
            [nameof(ReadyStatus)] = "Settings are ready to edit.",
            [nameof(DefaultsLoadedStatus)] = "Default values loaded. Click Apply or Save.",
            [nameof(CancelStatus)] = "Draft changes were closed without applying.",
            [nameof(RequiresRestartHint)] = "Settings Window updates immediately. Some legacy screens may require restart.",

            [nameof(GeneralTitle)] = "General",
            [nameof(GeneralSubtitle)] = "Language, theme, and recovery.",
            [nameof(GeneralDescription)] = "Base application behavior and interface appearance.",
            [nameof(LanguageLabel)] = "Interface language",
            [nameof(ThemeLabel)] = "Theme",
            [nameof(UiSizeLabel)] = "UI size",
            [nameof(ConfirmNewProject)] = "Confirm New Project when there are unsaved changes",
            [nameof(RecoveryAutosave)] = "Autosave recovery file",

            [nameof(InterfaceTitle)] = "Interface",
            [nameof(InterfaceSubtitle)] = "Inspector, Canvas, and Layout.",
            [nameof(InterfaceDescription)] = "Density and editing settings for Canvas and Property Inspector.",
            [nameof(ExperimentalLayoutTab)] = "Enable experimental Layout tab",
            [nameof(PropertyTooltips)] = "Show property tooltips",
            [nameof(CompactInspector)] = "Compact Property Inspector",
            [nameof(AdvancedProperties)] = "Show advanced properties",
            [nameof(CanvasGrid)] = "Canvas grid",
            [nameof(ShowCanvasGrid)] = "Show Canvas grid",
            [nameof(SnapToGrid)] = "Snap to grid",
            [nameof(GridStep)] = "Grid step",

            [nameof(PreviewTitle)] = "Preview",
            [nameof(PreviewSubtitle)] = "Runtime badge and window behavior.",
            [nameof(PreviewDescription)] = "Preview window and runtime badge options.",
            [nameof(RuntimeBadge)] = "Show runtime badge in Preview",
            [nameof(CompactRuntimeBadge)] = "Compact runtime badge",
            [nameof(AutoHideRuntimeBadge)] = "Auto-hide runtime badge",
            [nameof(PreviewTopmost)] = "Open Preview above other windows",
            [nameof(PreviewZoom)] = "Default Preview zoom, %",

            [nameof(ExportTitle)] = "Export / Build",
            [nameof(ExportSubtitle)] = "Build logs, artifacts, and security.",
            [nameof(ExportDescription)] = "Project generation, Validate Build, and artifacts behavior.",
            [nameof(ValidateAfterExport)] = "Validate Build after export",
            [nameof(VerboseBuildLogs)] = "Verbose Build logs",
            [nameof(KeepBuildArtifacts)] = "Keep successful build artifacts",
            [nameof(CleanOldArtifacts)] = "Clean old artifacts automatically",
            [nameof(ExportSqlConnectionString)] = "Export SQL connection string in generated code",
            [nameof(SqlSecretWarning)] = "Enable SQL connection string export only for local or safe projects. Passwords are masked in logs.",
            [nameof(BuildTimeout)] = "Build timeout, seconds",

            [nameof(NuGetTitle)] = "NuGet",
            [nameof(NuGetSubtitle)] = "Sources, HTTP, and generated NuGet.config.",
            [nameof(NuGetDescription)] = "Restore/build settings for the exported project.",
            [nameof(UseCustomNuGetSource)] = "Use custom NuGet source",
            [nameof(NuGetSourcePath)] = "Source URL/path",
            [nameof(AllowHttpSource)] = "Allow HTTP source / allowInsecureConnections",
            [nameof(NuGetFallback)] = "Include nuget.org fallback",
            [nameof(GenerateNuGetConfig)] = "Generate NuGet.config in exported project",
            [nameof(EffectiveSource)] = "Effective source",
            [nameof(NuGetConfigLocationHint)] = "NuGet.config will be created next to the exported .sln/.csproj.",
            [nameof(TestSource)] = "Test source",
            [nameof(Clear)] = "Clear",

            [nameof(SqlTitle)] = "SQL Server",
            [nameof(SqlSubtitle)] = "Server, database, auth, and preview rows.",
            [nameof(SqlDescription)] = "Global connection used by Data tab for SQL sources and DLL table preview.",
            [nameof(UseGlobalSqlSettings)] = "Use global SQL Server settings for SQL sources",
            [nameof(ServerName)] = "Server name",
            [nameof(DatabaseName)] = "Database name",
            [nameof(AuthenticationMode)] = "Authentication mode",
            [nameof(UserName)] = "User name",
            [nameof(Password)] = "Password",
            [nameof(SavePassword)] = "Save password in user settings",
            [nameof(PasswordWarning)] = "Password is stored only when this flag is enabled. It is masked in logs.",
            [nameof(DefaultSchema)] = "Default schema",
            [nameof(PreviewTopN)] = "Preview rows count",
            [nameof(ConnectionTimeout)] = "Connection timeout, seconds",
            [nameof(TrustCertificate)] = "Trust certificate",
            [nameof(EncryptConnection)] = "Encrypt connection",
            [nameof(EffectiveConnectionString)] = "Effective connection string",
            [nameof(TestConnection)] = "Test connection",
            [nameof(SqlConfigured)] = "SQL Server configured",
            [nameof(SqlNotConfigured)] = "SQL Server is not configured: enter server and database.",
            [nameof(CheckingSqlConnection)] = "Checking SQL Server connection...",

            [nameof(LogsTitle)] = "Logs / Diagnostics",
            [nameof(LogsSubtitle)] = "Log level and files.",
            [nameof(LogsDescription)] = "File logs, event level, and retention limits.",
            [nameof(SaveLogsToFile)] = "Save logs to file",
            [nameof(LogLevel)] = "Log level",
            [nameof(MaxLogFiles)] = "Max log files count",
            [nameof(MaxLogFileSize)] = "Max log file size, MB",
            [nameof(LogsFolder)] = "Logs folder",

            [nameof(AdvancedTitle)] = "Advanced",
            [nameof(AdvancedSubtitle)] = "Diagnostics and experimental flags.",
            [nameof(AdvancedDescription)] = "Developer-oriented options. Unavailable flags are marked experimental.",
            [nameof(TraceDiagnostics)] = "Trace diagnostics",
            [nameof(DeveloperWarnings)] = "Developer warnings",
            [nameof(ResetLayoutNextStart)] = "Reset layout on next startup",
            [nameof(ExperimentalDisabled)] = "Experimental: this will be wired in a later stabilization cycle.",
            [nameof(SettingsFile)] = "Settings file",

            [nameof(OptionRussian)] = "Russian",
            [nameof(OptionEnglish)] = "English",
            [nameof(OptionLight)] = "Light",
            [nameof(OptionDark)] = "Dark",
            [nameof(OptionSystem)] = "System",
            [nameof(OptionCompact)] = "Compact",
            [nameof(OptionComfortable)] = "Comfortable",
            [nameof(OptionDense)] = "Dense"
        });
}
