using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Linq;
using System.Text;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private static string PostgreSqlGeneratedIdentifier(string original)
    {
        return PostgreSqlExportNames.Identifier(original);
    }
    private SqlRuntimeExportPlan BuildPostgreSqlRuntimeExportPlan(CrudGenerationContext context)
    {
        var source = context.Source;
        var configured = !string.IsNullOrWhiteSpace(source.SourceTableName) || !string.IsNullOrWhiteSpace(source.SourceQuery);
        var schemaOnly = source.PreviewRowMode == BindingSourceModel.PreviewRowModeSchemaOnly || !source.UseRealPreviewRowsIfAvailable;
        var plan = new SqlRuntimeExportPlan
        {
            Context = context, RuntimeMode = schemaOnly ? "Empty" : "Sql",
            HasConnectionString = false, HasCommandText = configured,
            ShouldGenerateLoader = configured, ShouldCallLoader = configured && !schemaOnly,
            ShouldGenerateSeedRows = false, ShouldExportConnectionString = false,
            ConnectionStringValue = "", ConnectionStringReason = "runtime-environment",
            PostgreSqlEnvironmentKey = PostgreSqlScopedEnvironmentVariable(_exportActiveFormIdOverride ?? ActiveFormDocument?.Id ?? "", source.Id),
            CommandText = configured ? DatabaseProviderAdapters.PostgreSql.Select(
                string.IsNullOrWhiteSpace(source.SourceSchemaName) ? "public" : source.SourceSchemaName,
                source.SourceTableName, source.SourceQuery, null) : ""
        };
        PostgreSqlDiagnostics.Write("POSTGRES_EXPORT_GENERATED", $"loader={plan.ShouldGenerateLoader}; autoLoad={plan.ShouldCallLoader}");
        return plan;
    }

    private static string PostgreSqlEnvironmentVariable(BindingSourceModel source) =>
        "FORMDESIGNER_POSTGRES_" + SanitizeIdentifier(source.Id, "SOURCE").ToUpperInvariant();

    private static string PostgreSqlScopedEnvironmentVariable(string formId, string sourceId) =>
        "FORMDESIGNER_POSTGRES_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(formId + "::" + sourceId)))[..16];

    private string BuildPostgreSqlProjectConfigurationGuide()
    {
        var sources = CurrentProject.Forms.SelectMany(form => form.Document.BindingSources
                .Where(source => DataSourceIdentity.IsPostgreSql(source.SourceKind))
                .Select(source => (FormId: form.Id, FormName: form.DisplayName, source.Id, source.Name)))
            .Concat(BindingSources.Where(source => DataSourceIdentity.IsPostgreSql(source.SourceKind))
                .Select(source => (FormId: ActiveFormDocument?.Id ?? "", FormName: ActiveDocumentName, source.Id, source.Name)))
            .DistinctBy(source => (source.FormId, source.Id)).ToArray();
        if (sources.Length == 0) return "";
        var guide = new StringBuilder("\n\n## PostgreSQL runtime\n");
        guide.AppendLine("Установите Npgsql " + PostgreSqlConnectionService.PackageVersion + " (добавлен в package manifest при использовании DataGrid).");
        guide.AppendLine("До запуска задайте переменную FORMDESIGNER_POSTGRES_CONNECTION_STRING в окружении приложения. Пароль Designer не переносится в экспорт.");
        guide.AppendLine("Для разных подключений задайте переменные источников (они имеют приоритет над общей):");
        foreach (var source in sources)
            guide.AppendLine("- " + source.FormName + " / " + source.Name + ": " + PostgreSqlScopedEnvironmentVariable(source.FormId, source.Id));
        guide.AppendLine("После ключа формы/источника проверяется совместимый alias FORMDESIGNER_POSTGRES_<SOURCE_ID>, затем общая переменная. Scoped keys различаются даже у копий форм с одинаковым SourceId.");
        guide.AppendLine("Значение — обычная строка Npgsql с Host, Port, Database, Username, Password и Ssl Mode. Храните её вне репозитория.");
        guide.AppendLine("Каждая форма запускает свой async loader при открытии. При закрытии формы запрос отменяется. Таймаут ограничен; ошибки показываются на русском языке, коллекция остаётся пустой. Пустая таблица не подменяется demo rows.");
        guide.AppendLine("Loader выполняется в READ ONLY транзакции; CRUD-кнопки редактора меняют только коллекцию в памяти и не записывают PostgreSQL.");
        return guide.ToString();
    }

    private static void AppendPostgreSqlRuntimeLoader(StringBuilder sb, CrudGenerationContext context, SqlRuntimeExportPlan plan)
    {
        sb.AppendLine($"    public async Task {plan.LoaderMethodName}(CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var items = new List<{context.ItemTypeName}>();");
        sb.AppendLine("        PostgreSqlStatus = string.Empty;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            var configured = Environment.GetEnvironmentVariable(\"{plan.PostgreSqlEnvironmentKey}\")");
        sb.AppendLine($"                ?? Environment.GetEnvironmentVariable(\"{PostgreSqlEnvironmentVariable(context.Source)}\")");
        sb.AppendLine("                ?? Environment.GetEnvironmentVariable(\"FORMDESIGNER_POSTGRES_CONNECTION_STRING\");");
        sb.AppendLine("            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException(\"PostgreSQL connection is not configured.\");");
        sb.AppendLine("            var options = new NpgsqlConnectionStringBuilder(configured) { IncludeErrorDetail = false, PersistSecurityInfo = false };");
        sb.AppendLine("            options.Timeout = Math.Clamp(options.Timeout, 1, 300);");
        sb.AppendLine("            options.CommandTimeout = Math.Clamp(options.CommandTimeout, 1, 300);");
        sb.AppendLine("            await using var connection = new NpgsqlConnection(options.ConnectionString);");
        sb.AppendLine("            await connection.OpenAsync(cancellationToken);");
        sb.AppendLine("            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);");
        sb.AppendLine("            await using (var guard = connection.CreateCommand())");
        sb.AppendLine("            {");
        sb.AppendLine("                guard.Transaction = transaction; guard.CommandTimeout = options.CommandTimeout;");
        sb.AppendLine("                guard.CommandText = \"SET TRANSACTION READ ONLY\";");
        sb.AppendLine("                await guard.ExecuteNonQueryAsync(cancellationToken);");
        sb.AppendLine("            }");
        sb.AppendLine("            await using var command = connection.CreateCommand();");
        sb.AppendLine("            command.Transaction = transaction;");
        sb.AppendLine($"            command.CommandText = {plan.CommandTextConstantName};");
        sb.AppendLine("            command.CommandTimeout = options.CommandTimeout;");
        sb.AppendLine("            await using var reader = await command.ExecuteReaderAsync(cancellationToken);");
        sb.AppendLine("            while (await reader.ReadAsync(cancellationToken))");
        sb.AppendLine("            {");
        sb.AppendLine($"                items.Add(new {context.ItemTypeName}");
        sb.AppendLine("                {");
        for (var index = 0; index < context.Fields.Count; index++)
        {
            var field = context.Fields[index];
            var mapping = field.Clone();
            mapping.GeneratedPropertyName = field.GeneratedPropertyName;
            mapping.Header = field.Path; // captions may be edited; database column names may not
            sb.AppendLine($"                    {GetGeneratedRowPropertyName(field)} = {BuildGeneratedSqlReaderExpression(mapping)}{(index == context.Fields.Count - 1 ? "" : ",")}");
        }
        sb.AppendLine("                });");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            items.Clear();");
        sb.AppendLine("            PostgreSqlStatus = ex switch");
        sb.AppendLine("            {");
        sb.AppendLine("                PostgresException { SqlState: \"28P01\" or \"28000\" } => \"Неверные учётные данные PostgreSQL.\",");
        sb.AppendLine("                PostgresException { SqlState: \"3D000\" } => \"База данных PostgreSQL не найдена.\",");
        sb.AppendLine("                PostgresException { SqlState: \"42P01\" } => \"Таблица PostgreSQL не найдена.\",");
        sb.AppendLine("                PostgresException { SqlState: \"57014\" } or TimeoutException or NpgsqlException { InnerException: TimeoutException } => \"Превышено время ожидания PostgreSQL.\",");
        sb.AppendLine("                InvalidOperationException => \"Укажите подключение PostgreSQL в переменных окружения приложения.\",");
        sb.AppendLine("                _ => \"Не удалось подключиться к PostgreSQL. Проверьте параметры подключения.\"");
        sb.AppendLine("            };");
        sb.AppendLine("            System.Diagnostics.Debug.WriteLine($\"POSTGRES_CONNECTION_FAILED exception={ex.GetType().Name}; sqlState={(ex as PostgresException)?.SqlState}\");");
        sb.AppendLine("        }");
        sb.AppendLine("        if (cancellationToken.IsCancellationRequested) return;");
        sb.AppendLine($"        ReplaceItems({context.CollectionPropertyName}, items);");
        sb.AppendLine($"        Apply{context.ItemTypeName}Filter();");
        sb.AppendLine($"        System.Diagnostics.Debug.WriteLine($\"POSTGRES_RUNTIME_BINDING_CHECK rows={{{context.ViewCollectionPropertyName}.Count}}\");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendPostgreSqlWindowLifecycle(StringBuilder sb, System.Collections.Generic.IEnumerable<SqlRuntimeExportPlan> plans)
    {
        sb.AppendLine("        Opened += async (_, _) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var errors = new List<string>();");
        foreach (var plan in plans.Where(plan => plan.IsPostgreSql && plan.ShouldCallLoader))
        {
            sb.AppendLine($"            await ViewModel.{plan.LoaderMethodName}(_postgreSqlLifetime.Token);");
            sb.AppendLine("            if (!string.IsNullOrEmpty(ViewModel.PostgreSqlStatus)) errors.Add(ViewModel.PostgreSqlStatus);");
        }
        sb.AppendLine("            ViewModel.PostgreSqlStatus = string.Join(Environment.NewLine, errors);");
        sb.AppendLine("            if (!_postgreSqlLifetime.IsCancellationRequested && !string.IsNullOrEmpty(ViewModel.PostgreSqlStatus))");
        sb.AppendLine("            {");
        sb.AppendLine("                new Window { Title = \"PostgreSQL\", Width = 520, Height = 180,");
        sb.AppendLine("                    Content = new TextBlock { Text = ViewModel.PostgreSqlStatus, Margin = new Avalonia.Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap } }.Show(this);");
        sb.AppendLine("            }");
        sb.AppendLine("        };");
        sb.AppendLine("        Closed += (_, _) => _postgreSqlLifetime.Cancel();");
    }
}
