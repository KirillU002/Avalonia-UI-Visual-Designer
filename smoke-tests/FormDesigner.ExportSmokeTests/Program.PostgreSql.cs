using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using Npgsql;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private const string PostgresSecretMarker = "postgres-SECRET-do-not-persist-7e42";

    private static int RunPostgreSqlChecks()
    {
        var failed = 0;
        var passed = 0;
        void Check(string name, Action assertion)
        {
            try { assertion(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception ex)
            {
                var detail = name == "PostgresRealServerIntegration" ? PostgreSqlErrors.Details(ex) : ex.GetType().Name + ": " + ex.Message;
                Console.WriteLine("FAIL " + name + ": " + detail); failed++;
            }
        }
        Check("PostgresSettingsSerializationNoPlaintext", () =>
        {
            var settings = PostgresSettings();
            var json = JsonSerializer.Serialize(settings);
            RequireNotContains(json, PostgresSecretMarker, "Password must never be serialized.");
            var restored = JsonSerializer.Deserialize<PostgreSqlSettingsModel>(json)!;
            RequireEqual("QuotedDb", restored.Database, "Database must survive serialization.");
            RequireEqual("public", restored.DefaultSchema, "Default schema must be public.");
            if (restored.Port != 5432 || restored.PreviewRowsLimit != 100) throw new Exception("Invalid PostgreSQL defaults.");
        });
        Check("PostgresLocalSettingsProtectedRoundTrip", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FormDesigner-PostgresTests", Guid.NewGuid().ToString("N"));
            var service = new AppSettingsService(directory);
            var settings = new AppSettingsModel { PostgreSql = PostgresSettings() };
            service.SaveAsync(settings).GetAwaiter().GetResult();
            if (service.LastError is not null) throw new Exception(service.LastError);
            RequireNotContains(File.ReadAllText(service.SettingsFilePath), PostgresSecretMarker, "Local settings must contain only ciphertext.");
            var restored = service.Load();
            if (!string.IsNullOrEmpty(settings.PostgreSql.ProtectedPassword))
                RequireEqual(PostgresSecretMarker, restored.PostgreSql.Password, "DPAPI must recover only for this user.");
            settings.PostgreSql.RememberPassword = false;
            service.SaveAsync(settings).GetAwaiter().GetResult();
            RequireEqual("", service.Load().PostgreSql.Password, "RememberPassword=false must remove the saved secret.");
        });
        Check("PostgresConnectionBuilderEscapesValues", () =>
        {
            var settings = PostgresSettings(); settings.Password = "p;ass=\"word";
            var result = new PostgreSqlConnectionService().Build(settings);
            if (!result.Success) throw new Exception(result.ErrorMessage);
            var actual = new NpgsqlConnectionStringBuilder(result.ConnectionString);
            RequireEqual(settings.Password, actual.Password!, "Connection builder must escape delimiters.");
            RequireEqual(settings.Database, actual.Database!, "Database must survive builder quoting.");
            RequireNotContains(result.MaskedConnectionString, settings.Password, "Masked connection must not contain password.");
            settings.Port = 0;
            if (new PostgreSqlConnectionService().Build(settings).Success) throw new Exception("Invalid port was accepted.");
        });
        Check("PostgresSourceDtoCannotPersistPassword", () =>
        {
            var source = PostgresSource();
            source.SourceConnectionString = "Host=localhost;Database=test;Username=test;Password=" + PostgresSecretMarker;
            RequireNotContains(JsonSerializer.Serialize(source), PostgresSecretMarker, "Runtime source must strip secret.");
            var reversed = JsonSerializer.Deserialize<BindingSourceFileModel>("{\"SourceConnectionString\":\"Host=localhost;Password=" + PostgresSecretMarker + "\",\"SourceKind\":\"PostgreSql\"}")!;
            RequireNotContains(JsonSerializer.Serialize(reversed), PostgresSecretMarker, "DTO property order must not expose password.");
            var clone = source.Clone();
            RequireEqual("PostgreSql", clone.SourceKind, "Clone must preserve provider.");
            RequireEqual("public", new BindingSourceModel { SourceKind = "PostgreSql" }.SourceSchemaName, "PostgreSQL runtime default must be public.");
            RequireEqual("public", JsonSerializer.Deserialize<BindingSourceFileModel>("{\"SourceKind\":\"PostgreSql\"}")!.SourceSchemaName, "Missing PostgreSQL schema must default to public.");
        });
        Check("PostgresQuotedIdentifiersKeepCaseAndDots", () =>
        {
            var sql = DatabaseProviderAdapters.PostgreSql.Select("Case.Schema", "Order\"Items", null, 7);
            RequireContains(sql, "\"Case.Schema\".\"Order\"\"Items\"", "Schema and table are indivisible case-sensitive identifiers.");
            RequireContains(sql, "LIMIT 7", "PostgreSQL must use LIMIT.");
            RequireNotContains(sql, "TOP", "PostgreSQL must not use SQL Server dialect.");
        });
        Check("PostgresQueryRejectsWritesAndMultipleStatements", () =>
        {
            foreach (var query in new[] { "DELETE FROM t", "SELECT 1; DROP TABLE t", "WITH x AS (DELETE FROM t RETURNING *) SELECT * FROM x", "SELECT * INTO t FROM s", "SELECT * FROM t FOR UPDATE", "COMMIT", "SELECT 'unterminated" })
            {
                var rejected = false;
                try { _ = PostgreSqlReadQuery.Validate(query); } catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new Exception("Write or ambiguous query was accepted.");
            }
        });
        Check("PostgresQueryHandlesCommentsStringsCtes", () =>
        {
            foreach (var query in new[] { "SELECT '; delete' AS \"update\";", "WITH x AS (SELECT 1 AS id) SELECT * FROM x", "/*outer /* inner */ ok*/ SELECT $$a;b$$", "SELECT 1 -- trailing comment" })
                _ = DatabaseProviderAdapters.PostgreSql.Select("public", "unused", query, 5);
        });
        Check("PostgresMetadataUsesParameters", () =>
        {
            foreach (var adapter in new[] { DatabaseProviderAdapters.SqlServer, DatabaseProviderAdapters.PostgreSql })
            {
                RequireContains(adapter.TablesQuery, "@schema", "Schema metadata values must be parameters.");
                RequireContains(adapter.ColumnsQuery, "@table", "Table metadata values must be parameters.");
            }
        });
        Check("PostgresBindingSourceCreationAndFormSwitch", () =>
        {
            var vm = CreateViewModel("PostgresSourceChecks"); vm.ApplyPostgreSqlSettings(PostgresSettings());
            var source = vm.AddPostgreSqlBindingSource(); source.SourceTableName = "Customers";
            source.Fields.Add(Field("Id", "Id", "", "int", "100"));
            var firstForm = vm.ActiveFormDocument!.Id;
            vm.CreateNewForm(); vm.AddPostgreSqlBindingSource().SourceTableName = "Orders";
            vm.SetActiveForm(firstForm, "PostgresTest");
            RequireEqual("PostgreSql", vm.BindingSources.Single().SourceKind, "Switching form must preserve provider.");
            RequireEqual("Customers", vm.BindingSources.Single().SourceTableName, "Switching form must preserve table.");
            RequireNotContains(vm.ExportWorkspaceJson(), PostgresSecretMarker, "Project must not contain secret.");
        });
        Check("PostgresSettingsCenterApplyResetCancel", () =>
        {
            var vm = CreateViewModel("PostgresSettingsCenter");
            var settings = new SettingsWindowViewModel(vm, "isolated-settings.json");
            settings.SelectSection("postgres");
            if (!settings.IsPostgresSelected) throw new Exception("PostgreSQL section missing.");
            settings.Postgres.Database = "Changed";
            if (!settings.IsDirty) throw new Exception("Nested settings must mark editor dirty.");
            RequireEqual("", vm.CapturePostgreSqlSettings().Database, "Unapplied edit must not change global settings.");
            settings.ApplyToSource();
            RequireEqual("Changed", vm.CapturePostgreSqlSettings().Database, "Apply must update global settings.");
            settings.ResetPostgreSqlSettingsCommand.Execute(null);
            if (settings.Postgres.Port != 5432) throw new Exception("Reset defaults failed.");
        });
        Check("PostgresLegacySqlServerJsonCompatibility", () =>
        {
            var source = JsonSerializer.Deserialize<BindingSourceFileModel>("{\"SourceKind\":\"SqlServer\",\"SourceTableName\":\"Products\",\"SourceConnectionString\":\"Server=.;Database=old;Integrated Security=True\"}")!;
            if (source.SourceKind != "SqlServer") throw new Exception("Legacy provider changed.");
            RequireEqual("dbo", source.SourceSchemaName, "Legacy default schema must remain dbo.");
            RequireContains(source.SourceConnectionString, "Server=.", "Legacy connection must remain unchanged.");
        });
        Check("PostgresSqlServerRuntimeModesUnchanged", () =>
        {
            var source = SqlCustomersSource();
            if (DataGridRuntimeDataModeResolver.Resolve(source).Mode != DataGridRuntimeDataMode.Sql)
                throw new Exception("Legacy SQL Server must still use its runtime loader.");
            if (DataGridRuntimeDataModeResolver.Resolve(source, includeDemoData: true).Mode != DataGridRuntimeDataMode.Demo)
                throw new Exception("SQL Server's explicit demo setting must retain its old behavior.");
        });
        Check("PostgresDoesNotAllowSyntheticFallback", () =>
        {
            var source = PostgresSource(); source.UseDemoData = true; source.AllowPreviewSampleFallback = true;
            source.PreviewRowMode = BindingSourceModel.PreviewRowModeSampleRows;
            var mode = DataGridRuntimeDataModeResolver.Resolve(source, includeDemoData: true);
            if (mode.Mode != DataGridRuntimeDataMode.Sql || mode.ExplicitDemoEnabled) throw new Exception("PostgreSQL must ignore synthetic modes.");
        });
        Check("PostgresRealConnectionFailureHasNoDemoRows", () =>
        {
            var vm = CreateViewModel("PostgresFailure"); var source = PostgresSource();
            source.SourceConnectionString = "Host=127.0.0.1;Port=1;Database=absent;Username=test;Timeout=1";
            source.AllowPreviewSampleFallback = source.UseDemoData = true;
            var rows = vm.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
            if (rows.Count != 0) throw new Exception("Connection failure must leave an empty grid.");
            RequireContains(source.PreviewRowsStatus, "PostgreSQL", "Connection failure must be visible.");
        });
        Check("PostgresReadCancellationBeforeConnect", () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try
            {
                new DatabaseReadService(DatabaseProviderAdapters.PostgreSql).LoadRowsAsync(PostgresSource(), cancellation.Token).GetAwaiter().GetResult();
                throw new Exception("Cancellation was ignored.");
            }
            catch (OperationCanceledException) { }
            var vm = CreateViewModel("PostgresClosedPreview"); ConfigurePostgreSqlDataGridExport(vm);
            var window = new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
            typeof(PreviewWindow).GetField("_databasePreviewClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            Pump((Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!);
            if (typeof(PreviewWindow).GetField("_databasePreviewCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is not null)
                throw new Exception("A closed Preview must not start a new request.");
        });
        Check("PostgresChangedTimeoutKeepsSessionCredential", () =>
        {
            var built = new PostgreSqlConnectionService().Build(PostgresSettings());
            var profile = new NpgsqlConnectionStringBuilder(built.MaskedConnectionString) { CommandTimeout = 1 };
            RequireEqual(PostgresSecretMarker, new NpgsqlConnectionStringBuilder(PostgreSqlConnectionService.Resolve(profile.ConnectionString)).Password!, "Timeout changes must not lose session credential.");
        });
        Check("PostgresPreviewFailureVisibleWithoutSyntheticRows", () => AssertPostgreSqlPreviewFailure(axaml: false));
        Check("PostgresAxamlPreviewFailureVisibleWithoutSyntheticRows", () => AssertPostgreSqlPreviewFailure(axaml: true));
        Check("PostgresDiagnosticsDoNotLeakSecrets", () =>
        {
            var entries = new List<string>();
            void Capture(string code, string detail) => entries.Add(code + " " + detail);
            PostgreSqlDiagnostics.Emitted += Capture;
            try
            {
                var settings = PostgresSettings(); settings.Host = "127.0.0.1"; settings.Port = 1; settings.ConnectionTimeoutSeconds = 1;
                _ = new PostgreSqlConnectionService().TestConnectionAsync(settings).GetAwaiter().GetResult();
            }
            finally { PostgreSqlDiagnostics.Emitted -= Capture; }
            var logs = string.Join("\n", entries);
            RequireContains(logs, "POSTGRES_CONNECTION_FAILED", "Failure event missing.");
            RequireNotContains(logs, PostgresSecretMarker, "Diagnostic must not contain password.");
            RequireNotContains(logs, "Host=", "Diagnostic must not contain full connection string.");
        });
        Check("PostgresErrorsAreRussianAndRedacted", () =>
        {
            foreach (var state in new[] { "28P01", "3D000", "42P01", "57014", "25006" })
            {
                var exception = new PostgresException(PostgresSecretMarker, "ERROR", "ERROR", state);
                RequireNotContains(PostgreSqlErrors.Message(exception), PostgresSecretMarker, "User error must not reveal server text.");
                RequireNotContains(PostgreSqlErrors.Details(exception), PostgresSecretMarker, "Diagnostic must not reveal server text.");
                RequireContains(PostgreSqlErrors.Details(exception), state, "Diagnostic must keep SQLSTATE.");
            }
        });
        Console.WriteLine($"PostgreSQL offline/contract checks: {passed} passed; {failed} failed.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_POSTGRES")))
            Console.WriteLine("SKIP PostgreSQL real-server integration: FORMDESIGNER_TEST_POSTGRES is not set. No live schemas, rows, credentials, timeout or cancellation checks were claimed.");
        else
            Check("PostgresRealServerIntegration", () => RunPostgreSqlServerIntegration());
        return failed == 0 ? 0 : 1;
    }

    private static PostgreSqlSettingsModel PostgresSettings() => new()
    {
        Host = "localhost", Database = "QuotedDb", Username = "designer", Password = PostgresSecretMarker
    };
    private static BindingSourceModel PostgresSource(string id = "pg-customers", string table = "Customers")
    {
        var source = new BindingSourceModel
        {
            Id = id, Name = id, Path = id.Replace('-', '_'), ItemTypeName = id.Replace('-', '_') + "Row",
            SourceKind = "PostgreSql", SourceSchemaName = "public", SourceTableName = table,
            SourceConnectionString = "Host=localhost;Database=test;Username=designer;Timeout=1;Command Timeout=1"
        };
        source.Fields.Add(Field("Id", "Id", "", "int", "100"));
        source.Fields.Add(Field("Display name", "Name", "", "string", "*"));
        return source;
    }

    private static void ConfigurePostgreSqlDataGridExport(MainWindowViewModel vm)
    {
        vm.Controls.Clear(); vm.BindingSources.Clear();
        vm.GenerationMode = MainWindowViewModel.GenerationModeCleanUi;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        vm.ApplyPostgreSqlSettings(PostgresSettings());
        var source = PostgresSource();
        source.SourceConnectionString = new PostgreSqlConnectionService().Build(PostgresSettings()).ConnectionString;
        vm.BindingSources.Add(source);
        var grid = Control(DesignerControlTypes.DataGrid, "CustomersGrid", 20, 20, 600, 280);
        grid.BindingSourceId = source.Id; vm.Controls.Add(grid);
    }
    private static void AssertPostgreSqlDataGridExport(SmokeContext context)
    {
        RequireContains(context.CSharp, "using Npgsql;", "Export must use Npgsql.");
        RequireContains(context.CSharp, "FromPostgreSqlAsync", "Export must include async loader.");
        RequireContains(context.CSharp, "SET TRANSACTION READ ONLY", "Runtime must remain read-only.");
        RequireContains(context.CSharp, "Opened += async", "Every window must load on Opened.");
        RequireContains(context.CSharp, "_postgreSqlLifetime.Cancel", "Window close must cancel reads.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding", "DataGrid must bind collection.");
        RequireNotContains(context.CSharp, PostgresSecretMarker, "Generated code must not contain designer password.");
        RequireNotContains(context.CSharp, "Seedpg_customers", "PostgreSQL must not generate fake rows.");
        RequireContains(context.CSharp, "ReadString(reader, @\"Name\")", "Caption must not replace database column name.");
        if (!context.ViewModel.RequiredPackages.Any(package => package.Id == "Npgsql" && package.Version == "8.0.9")) throw new Exception("Npgsql missing from manifest.");
        if (context.ViewModel.RequiredPackages.Any(package => package.Id == "Microsoft.Data.SqlClient")) throw new Exception("PostgreSQL-only export must not require SqlClient.");
        using var actual = ExportToTemporaryProject(context);
        var readme = File.ReadAllText(Path.Combine(actual.Path, "README.generated.md"));
        RequireContains(readme, "FORMDESIGNER_POSTGRES_CONNECTION_STRING", "README must describe runtime configuration.");
        foreach (var file in Directory.EnumerateFiles(actual.Path, "*", SearchOption.AllDirectories).Where(file => file.EndsWith(".cs") || file.EndsWith(".axaml") || file.EndsWith(".md")))
            RequireNotContains(File.ReadAllText(file), PostgresSecretMarker, "Exported files must not contain password.");
        RequireGeneratedPostgreSqlRuntimeFailure(context, actual.Path);
    }

    private static void ConfigurePostgreSqlQuotedColumns(MainWindowViewModel vm)
    {
        ConfigurePostgreSqlDataGridExport(vm);
        var source = vm.BindingSources.Single(); source.Fields.Clear();
        source.SourceSchemaName = "Case.Schema"; source.SourceTableName = "Order.Items";
        foreach (var name in new[] { "name", "Name", "m_field", "1st column", "display.name", "Clone", "OnPropertyChanged" })
            source.Fields.Add(Field("Caption " + name, name, "", "string", "*"));
        source.Fields.Add(Field("Nullable number", "nullable_count", "", "int?", "100"));
    }
    private static void AssertPostgreSqlQuotedColumns(SmokeContext context)
    {
        RequireContains(context.CSharp, "ReadString(reader, @\"name\")", "Lowercase database column must stay lowercase in reader mapping.");
        RequireContains(context.CSharp, "ReadString(reader, @\"Name\")", "Case-distinct database column must remain distinct.");
        RequireContains(context.CSharp, "ReadValue<int?>(reader, @\"nullable_count\")", "Nullable values must remain nullable in DTO.");
        RequireContains(context.CSharp, "ReadString(reader, @\"display.name\")", "Dot in column name must not become a path in SQL mapping.");
        RequireContains(context.Xaml, "{Binding Name2}", "Colliding C# names must map to distinct generated properties.");
        RequireContains(context.CSharp, "Clone2", "Database column must not collide with DTO Clone method.");
        RequireContains(context.CSharp, "OnPropertyChanged2", "Database column must not shadow CommunityToolkit notification methods.");
        var preview = context.ViewModel.GenerateExportedAxamlPreviewSnapshot();
        RequireEqual("Name2", preview.PostgreSqlFieldPaths[context.ViewModel.BindingSources.Single().Id]["Name"], "AXAML Preview must retain the export field-name mapping.");
        DotnetBuild(context.ProjectPath);
        var builtAssembly = LoadGeneratedApplicationAssembly(Path.Combine(context.ProjectPath, "bin", "Debug", "net6.0", context.Scenario.Name + ".dll"));
        var generatedType = builtAssembly.GetType(context.ViewModel.ExportProjectNamespace + ".MainWindowViewModel")!;
        using var values = new System.Data.DataTable(); values.Columns.Add("nullable_count", typeof(int)); values.Columns.Add("array_value", typeof(int[]));
        values.Rows.Add(7, new int[] { 1, 2 }); values.Rows.Add(DBNull.Value, new int[] { 3 });
        using var reader = values.CreateDataReader(); reader.Read();
        var readValue = generatedType.GetMethod("ReadValue", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(int?));
        if (!Equals(7, readValue.Invoke(null, new object[] { reader, "nullable_count" }))) throw new Exception("Generated nullable reader lost an integer value.");
        var readString = generatedType.GetMethod("ReadString", BindingFlags.Static | BindingFlags.NonPublic)!;
        RequireEqual("1, 2", (string)readString.Invoke(null, new object[] { reader, "array_value" })!, "Array values must render their contents.");
        reader.Read();
        if (readValue.Invoke(null, new object[] { reader, "nullable_count" }) is not null) throw new Exception("Generated nullable reader must keep DBNull as null.");
    }
    private static void ConfigurePostgreSqlEremexExport(MainWindowViewModel vm)
    {
        ConfigurePostgreSqlQuotedColumns(vm);
        LoadBuiltEremexPlugin(vm);
        var grid = vm.Controls.Single(); grid.Type = "Eremex.DataGridControl";
        grid.AutoGenerateColumns = false;
        vm.IncludePluginRuntimeReferences = true;
    }
    private static void AssertPostgreSqlEremexExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "FieldName=\"Name2\"", "Eremex must use the generated C# property for a case-distinct column.");
        RequireContains(context.CSharp, "FromPostgreSqlAsync", "Eremex-bound PostgreSQL must get the runtime loader.");
        if (!context.ViewModel.RequiredPackages.Any(p => p.Id == "Npgsql") || !context.ViewModel.RequiredPackages.Any(p => p.Id == "Eremex.Avalonia.Controls"))
            throw new Exception("Eremex/PostgreSQL package manifest incomplete.");
    }

    private static void AssertPostgreSqlPreviewFailure(bool axaml)
    {
        var vm = CreateViewModel("PostgresPreviewFailure"); ConfigurePostgreSqlDataGridExport(vm);
        vm.BindingSources.Single().SourceConnectionString = "Host=127.0.0.1;Port=1;Database=test;Username=test;Timeout=1";
        vm.GenerateXaml();
        PreviewWindow window;
        if (axaml) window = new PreviewWindow(vm.GenerateExportedAxamlPreviewSnapshot(), vm.Registry);
        else window = new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry, vm.CreatePreviewProjectFormsSnapshot(), vm.ActiveFormDocument!.Id);
        var preload = (Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        Pump(preload);
        if (!window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible) throw new Exception("Preview must show a connection error.");
        RequireContains(window.FindControl<TextBlock>("DatabaseErrorText")!.Text!, "PostgreSQL", "Preview error must identify provider.");
        if (axaml)
        {
            var snapshot = vm.GenerateExportedAxamlPreviewSnapshot();
            Pump((Task)typeof(PreviewWindow).GetMethod("LoadExportedAxamlPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { snapshot })!);
        }
        else typeof(PreviewWindow).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        var grids = window.GetLogicalDescendants().OfType<DataGrid>().ToArray();
        if (grids.Length == 0 || grids.Any(grid => grid.ItemsSource is IEnumerable items && items.Cast<object>().Any()))
            throw new Exception("Preview must contain an empty real DataGrid after failure.");
        window.Close();
    }

    private static void RequireGeneratedPostgreSqlRuntimeFailure(SmokeContext context, string exportFolder)
    {
        DotnetBuild(exportFolder);
        var name = Path.GetFileNameWithoutExtension(Directory.GetFiles(exportFolder, "*.csproj").Single());
        var assembly = LoadGeneratedApplicationAssembly(Path.Combine(exportFolder, "bin", "Debug", "net6.0", name + ".dll"));
        var window = (Window)Activator.CreateInstance(assembly.GetType(context.ViewModel.ExportProjectNamespace + ".MainWindow")!)!;
        var viewModel = window.DataContext!;
        var method = viewModel.GetType().GetMethods().Single(m => m.Name.EndsWith("FromPostgreSqlAsync", StringComparison.Ordinal));
        const string variable = "FORMDESIGNER_POSTGRES_PG_CUSTOMERS";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=" + PostgresSecretMarker + ";Timeout=1");
            Pump((Task)method.Invoke(viewModel, new object[] { CancellationToken.None })!);
            var status = (string)viewModel.GetType().GetProperty("PostgreSqlStatus")!.GetValue(viewModel)!;
            RequireContains(status, "PostgreSQL", "Generated runtime must expose useful error.");
            RequireNotContains(status, PostgresSecretMarker, "Generated runtime must not reveal password.");
            Dispatcher.UIThread.RunJobs();
            var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single();
            if (grid.ItemsSource is not IEnumerable rows || rows.Cast<object>().Any()) throw new Exception("Generated PostgreSQL DataGrid must remain bound to an empty collection on failure.");
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); window.Close(); }
    }

    private static void ConfigurePostgreSqlMixedMultiForm(MainWindowViewModel vm)
    {
        ConfigurePostgreSqlDataGridExport(vm);
        vm.CreateNewForm(); vm.Controls.Clear(); vm.BindingSources.Clear();
        var orders = PostgresSource("pg-orders", "Orders"); vm.BindingSources.Add(orders);
        var grid = Control(DesignerControlTypes.DataGrid, "OrdersGrid", 20, 20, 600, 280); grid.BindingSourceId = orders.Id; vm.Controls.Add(grid);
        vm.CreateNewForm(); vm.Controls.Clear(); vm.BindingSources.Clear();
        var sql = SqlCustomersSource(); sql.Name = "Products"; sql.SourceTableName = "Products"; sql.SourceQuery = "";
        vm.BindingSources.Add(sql);
        grid = Control(DesignerControlTypes.DataGrid, "ProductsGrid", 20, 20, 600, 280); grid.BindingSourceId = sql.Id; vm.Controls.Add(grid);
        vm.ExportSqlConnectionString = true;
    }
    private static void AssertPostgreSqlMixedMultiForm(SmokeContext context)
    {
        var allCode = string.Join("\n", context.GeneratedFiles.Where(file => file.Path.EndsWith(".cs")).Select(file => file.Content));
        RequireContains(allCode, "FORMDESIGNER_POSTGRES_PG_CUSTOMERS", "First form config missing.");
        RequireContains(allCode, "FORMDESIGNER_POSTGRES_PG_ORDERS", "Second form config missing.");
        RequireContains(allCode, "new SqlConnection", "SQL Server third form must keep its loader.");
        if (!context.ViewModel.RequiredPackages.Any(p => p.Id == "Npgsql") || !context.ViewModel.RequiredPackages.Any(p => p.Id == "Microsoft.Data.SqlClient"))
            throw new Exception("Mixed forms must include both drivers.");
        var json = context.ViewModel.ExportWorkspaceJson();
        RequireNotContains(json, PostgresSecretMarker, "Multi-form JSON must not contain PostgreSQL secret.");
        var restored = CreateViewModel("PostgresMixedReload"); restored.LoadDocumentJson(json);
        if (restored.CurrentProject.Forms.Count != 3) throw new Exception("Multi-form JSON lost forms.");
        foreach (var form in restored.CurrentProject.Forms)
        {
            restored.SetActiveForm(form.Id, "PostgresRegression"); restored.GenerateXaml();
            if (form.Document.BindingSources.Any(s => s.SourceKind == "PostgreSql"))
                RequireContains(restored.GeneratedCSharp, "FromPostgreSqlAsync", "Active-form switches must not lose loader.");
        }
        var copied = JsonSerializer.Deserialize<WorkspaceModel>(json)!;
        foreach (var form in copied.Project.Forms)
        {
            foreach (var source in form.Document.BindingSources.Where(source => source.SourceKind == "PostgreSql"))
            {
                var previousId = source.Id; source.Id = "shared-pg-source";
                foreach (var grid in form.Document.Controls.Where(grid => grid.BindingSourceId == previousId)) grid.BindingSourceId = source.Id;
            }
        }
        var collisionCheck = CreateViewModel("PostgresCopiedSourceIds"); collisionCheck.LoadDocumentJson(JsonSerializer.Serialize(copied)); collisionCheck.GenerateXaml();
        var generated = string.Join("\n", collisionCheck.GeneratedFiles.Select(file => file.Content));
        var scopedKeys = System.Text.RegularExpressions.Regex.Matches(generated, "FORMDESIGNER_POSTGRES_[A-F0-9]{16}").Select(match => match.Value).Distinct().ToArray();
        if (scopedKeys.Length != 2) throw new Exception("Copied forms sharing SourceId need independent PostgreSQL environment keys.");
    }

    private static int RunPostgreSqlEremexServerChecks()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_POSTGRES")))
        {
            Console.WriteLine("SKIP PostgreSQL Eremex real-server integration: no explicitly configured test database.");
            return 0;
        }
        try { RunPostgreSqlServerIntegration(eremexOnly: true); return 0; }
        catch (Exception ex) { Console.WriteLine("FAIL PostgresEremexRealServerIntegration: " + PostgreSqlErrors.Details(ex)); return 1; }
    }

    private static void RunPostgreSqlServerIntegration(bool eremexOnly = false)
    {
        var configured = Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_POSTGRES")!;
        var options = new NpgsqlConnectionStringBuilder(configured) { IncludeErrorDetail = false, Timeout = 3, CommandTimeout = 3 };
        var settings = new PostgreSqlSettingsModel { Host = options.Host!, Port = options.Port, Database = options.Database!, Username = options.Username!, Password = options.Password ?? "", SslMode = options.SslMode.ToString(), ConnectionTimeoutSeconds = 3, CommandTimeoutSeconds = 3 };
        var connectionService = new PostgreSqlConnectionService();
        if (!connectionService.TestConnectionAsync(settings).GetAwaiter().GetResult().Success) throw new Exception("Real test PostgreSQL is not reachable.");
        var profile = PostgreSqlConnectionService.WithoutSecrets(connectionService.Build(settings).ConnectionString);
        var schema = "Designer.Test_" + Guid.NewGuid().ToString("N");
        var quote = DatabaseProviderAdapters.PostgreSql.QuoteIdentifier(schema);
        using var connection = new NpgsqlConnection(options.ConnectionString); connection.Open();
        try
        {
            var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PostgreSql", "fixture.sql"))
                .Replace("\"Designer.Phase2\"", quote, StringComparison.Ordinal);
            using var setup = new NpgsqlCommand(fixture, connection);
            setup.ExecuteNonQuery();
            if (eremexOnly)
            {
                RequireLivePostgreSqlPreviewAndExport(profile, schema, options.ConnectionString, eremex: true);
                Console.WriteLine("LIVE PostgreSQL Eremex: both Preview modes and compiled exported ViewModel loader checked. Separate app-process/UI acceptance is still required.");
                return;
            }
            var service = new DatabaseReadService(DatabaseProviderAdapters.PostgreSql);
            if (!service.GetSchemasAsync(profile).GetAwaiter().GetResult().Contains(schema)) throw new Exception("Live schema missing.");
            var tables = service.GetTablesAsync(profile, schema).GetAwaiter().GetResult();
            foreach (var name in new[] { "Customers", "Orders", "EmptyTable", "CustomersView", "Order.Items" })
                if (!tables.Any(t => t.Name == name)) throw new Exception("Live test table/view missing.");
            var columns = service.GetColumnsAsync(profile, schema, "Customers").GetAwaiter().GetResult();
            if (!columns.Any(c => c.Name == "Id" && c.IsPrimaryKey && !c.IsNullable && c.DataType == "integer")
                || !columns.Any(c => c.Name == "Email" && c.IsNullable && c.DataType == "text")
                || !columns.Any(c => c.Name == "Name" && !c.IsNullable)) throw new Exception("Live metadata incorrect.");
            var orderColumns = service.GetColumnsAsync(profile, schema, "Orders").GetAwaiter().GetResult();
            if (!orderColumns.Any(c => c.Name == "Amount" && c.IsNullable && c.DataType == "numeric(12,2)")
                || !orderColumns.Any(c => c.Name == "OrderDate" && !c.IsNullable && c.DataType == "timestamp without time zone"))
                throw new Exception("Orders numeric/timestamp metadata incorrect.");
            var source = PostgresSource(); source.SourceConnectionString = profile; source.SourceSchemaName = schema;
            var rows = service.LoadRowsAsync(source).GetAwaiter().GetResult();
            RequireLiveCustomers(rows);
            var orders = source.Clone(); orders.SourceTableName = "Orders";
            var orderRows = service.LoadRowsAsync(orders).GetAwaiter().GetResult();
            if (orderRows.Count != 3 || !orderRows.Any(row => row["Id"] == "1002" && row["Amount"] == "42.75"))
                throw new Exception("Orders rows are not actual database values.");
            var quoted = source.Clone(); quoted.SourceTableName = "Order.Items";
            var quotedRows = service.LoadRowsAsync(quoted).GetAwaiter().GetResult();
            if (quotedRows.Count != 1 || quotedRows[0]["name"] != "lowercase" || quotedRows[0]["Name"] != "Uppercase"
                || quotedRows[0]["display.name"] != "dot value") throw new Exception("Quoted identifiers lost their exact case or dots.");
            RequireLivePostgreSqlPreviewAndExport(profile, schema, options.ConnectionString);
            source.SourceTableName = "EmptyTable";
            if (service.LoadRowsAsync(source).GetAwaiter().GetResult().Count != 0) throw new Exception("Empty table must stay empty.");
            source.SourceQuery = "SELECT pg_sleep(10)";
            using var cancellation = new CancellationTokenSource(150);
            try { service.LoadRowsAsync(source, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Live query did not cancel."); }
            catch (OperationCanceledException) { }
            var timed = new NpgsqlConnectionStringBuilder(profile) { CommandTimeout = 1 };
            source.SourceConnectionString = timed.ConnectionString;
            try { service.LoadRowsAsync(source).GetAwaiter().GetResult(); throw new Exception("Live timeout was ignored."); }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException) { }
            source.SourceQuery = ""; source.SourceTableName = "MissingTable";
            RequireLiveSqlState(() => service.LoadRowsAsync(source).GetAwaiter().GetResult(), "42P01");
            source.SourceQuery = "SELECT missing_column FROM " + quote + ".\"Customers\"";
            RequireLiveSqlState(() => service.LoadRowsAsync(source).GetAwaiter().GetResult(), "42703");
            source.SourceQuery = "SELECT FROM";
            RequireLiveSqlState(() => service.LoadRowsAsync(source).GetAwaiter().GetResult(), "42601");
            source.SourceQuery = ""; source.SourceTableName = "Customers";
            RequireLiveCustomers(service.LoadRowsAsync(source).GetAwaiter().GetResult());
            var missingDatabase = settings.Clone(); missingDatabase.Database = "designer_missing_" + Guid.NewGuid().ToString("N");
            var missingResult = connectionService.TestConnectionAsync(missingDatabase).GetAwaiter().GetResult();
            if (missingResult.Success || !missingResult.Message.Contains("База данных", StringComparison.Ordinal))
                throw new Exception("Nonexistent database must be diagnosed.");
            settings.Password = "intentionally-invalid-test-password";
            if (connectionService.TestConnectionAsync(settings).GetAwaiter().GetResult().Success) throw new Exception("Server did not reject invalid password; test requires password authentication.");
            settings.Password = options.Password ?? "";
            if (!connectionService.TestConnectionAsync(settings).GetAwaiter().GetResult().Success) throw new Exception("Reconnect after invalid credentials failed.");
            RequireLiveCustomers(service.LoadRowsAsync(source).GetAwaiter().GetResult());
            Console.WriteLine("LIVE PostgreSQL: connection/reconnect, Customers/Orders/EmptyTable, metadata/PK/nullability/types/views, quoted identifiers, real/empty rows, both standard Preview modes, generated ViewModel loader, cancellation/timeout and SQL/credentials/database/table errors checked. Separate app-process/UI acceptance is still required.");
        }
        finally
        {
            using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {quote} CASCADE", connection); cleanup.ExecuteNonQuery();
        }
    }

    private static void RequireLivePostgreSqlPreviewAndExport(string profile, string schema, string runtimeConnection, bool eremex = false)
    {
        var scenarioName = eremex ? "PostgresLiveEremex" : "PostgresLivePipeline";
        var vm = CreateViewModel(scenarioName); ConfigurePostgreSqlDataGridExport(vm);
        if (eremex)
        {
            LoadBuiltEremexPlugin(vm);
            vm.Controls.Single().Type = "Eremex.DataGridControl";
            vm.Controls.Single().AutoGenerateColumns = false;
            vm.IncludePluginRuntimeReferences = true;
        }
        var source = vm.BindingSources.Single(); source.SourceConnectionString = profile; source.SourceSchemaName = schema;
        source.Fields.Clear();
        foreach (var field in new DatabaseReadService(DatabaseProviderAdapters.PostgreSql).DescribeAsync(source).GetAwaiter().GetResult()) source.Fields.Add(field);
        vm.GenerateXaml();
        foreach (var axaml in new[] { false, true })
        {
            var snapshot = vm.GenerateExportedAxamlPreviewSnapshot();
            var window = axaml ? new PreviewWindow(snapshot, vm.Registry)
                : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry, vm.CreatePreviewProjectFormsSnapshot(), vm.ActiveFormDocument!.Id);
            try
            {
                Pump((Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!);
                if (axaml) Pump((Task)typeof(PreviewWindow).GetMethod("LoadExportedAxamlPreviewAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { snapshot })!);
                else typeof(PreviewWindow).GetMethod("RenderDocument", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var grid = eremex ? window.GetLogicalDescendants().OfType<Control>().Single(control => control.GetType().Name == "DataGridControl")
                    : window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.GetType().GetProperty("ItemsSource")!.GetValue(grid) is not IEnumerable items) throw new Exception("Preview ItemsSource is absent.");
                var names = items.Cast<object>().Select(item => ReadLiveCell(item, "Name")).OrderBy(name => name).ToArray();
                if (!names.SequenceEqual(new[] { "Alice North", "Boris River", "Clara Pine" })) throw new Exception("Preview did not bind all distinct actual row values.");
            }
            finally { window.Close(); }
        }
        // Export two independent PostgreSQL forms; no SQL Server is contacted.
        if (!eremex)
        {
            var customersForm = vm.ActiveFormDocument!.Id;
            vm.CreateNewForm(); vm.Controls.Clear(); vm.BindingSources.Clear();
            var orders = PostgresSource("pg-orders", "Orders"); orders.SourceConnectionString = profile; orders.SourceSchemaName = schema;
            orders.Fields.Clear();
            foreach (var field in new DatabaseReadService(DatabaseProviderAdapters.PostgreSql).DescribeAsync(orders).GetAwaiter().GetResult()) orders.Fields.Add(field);
            vm.BindingSources.Add(orders);
            var grid = Control(DesignerControlTypes.DataGrid, "OrdersGrid", 20, 20, 600, 280); grid.BindingSourceId = orders.Id; vm.Controls.Add(grid);
            vm.SetActiveForm(customersForm, "PostgresLiveMultiForm");
            vm.GenerateXaml();
        }
        var context = new SmokeContext(new(scenarioName, ConfigurePostgreSqlDataGridExport, _ => { }, true), vm,
            Path.Combine(FindRepositoryRoot(), "artifacts", "postgres-live"), vm.GeneratedXaml, vm.GeneratedCSharp, vm.GeneratedFiles.ToList(), "", "");
        using var export = ExportToTemporaryProject(context);
        var password = new NpgsqlConnectionStringBuilder(runtimeConnection).Password;
        if (!string.IsNullOrEmpty(password))
            foreach (var file in Directory.EnumerateFiles(export.Path, "*", SearchOption.AllDirectories)
                .Where(file => new[] { ".cs", ".axaml", ".md", ".json", ".config", ".csproj", ".sln" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
                RequireNotContains(File.ReadAllText(file), password, "Exported source/configuration must not contain the actual test credential.");
        DotnetBuild(export.Path);
        var assemblyName = Path.GetFileNameWithoutExtension(Directory.GetFiles(export.Path, "*.csproj").Single());
        var assembly = LoadGeneratedApplicationAssembly(Path.Combine(export.Path, "bin", "Debug", "net6.0", assemblyName + ".dll"));
        const string variable = "FORMDESIGNER_POSTGRES_PG_CUSTOMERS";
        var previous = Environment.GetEnvironmentVariable(variable);
        const string ordersVariable = "FORMDESIGNER_POSTGRES_PG_ORDERS";
        var previousOrders = Environment.GetEnvironmentVariable(ordersVariable);
        try
        {
            Environment.SetEnvironmentVariable(variable, runtimeConnection);
            Environment.SetEnvironmentVariable(ordersVariable, runtimeConnection);
            var runtime = Activator.CreateInstance(assembly.GetType(vm.ExportProjectNamespace + ".MainWindowViewModel")!)!;
            var loader = runtime.GetType().GetMethods().Single(method => method.Name.EndsWith("FromPostgreSqlAsync", StringComparison.Ordinal));
            Pump((Task)loader.Invoke(runtime, new object[] { CancellationToken.None })!);
            var rows = (IEnumerable)runtime.GetType().GetProperty("pg_customersView")!.GetValue(runtime)!;
            var actual = rows.Cast<object>().ToArray();
            var names = actual.Select(item => Convert.ToString(item.GetType().GetProperty("Name")!.GetValue(item))).OrderBy(name => name).ToArray();
            if (!names.SequenceEqual(new[] { "Alice North", "Boris River", "Clara Pine" }))
                throw new Exception("Compiled exported ViewModel did not load all distinct database values.");
            if (!eremex)
            {
                var ordersType = assembly.GetTypes().Single(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal) && type.GetProperty("pg_ordersView") is not null);
                var ordersRuntime = Activator.CreateInstance(ordersType)!;
                var ordersLoader = ordersType.GetMethods().Single(method => method.Name.EndsWith("FromPostgreSqlAsync", StringComparison.Ordinal));
                Pump((Task)ordersLoader.Invoke(ordersRuntime, new object[] { CancellationToken.None })!);
                var ordersRows = (IEnumerable)ordersType.GetProperty("pg_ordersView")!.GetValue(ordersRuntime)!;
                var ids = ordersRows.Cast<object>().Select(item => ReadLiveCell(item, "Id")).OrderBy(id => id).ToArray();
                if (!ids.SequenceEqual(new[] { "1001", "1002", "1003" })) throw new Exception("Secondary form must load its own actual Orders rows.");
            }
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); Environment.SetEnvironmentVariable(ordersVariable, previousOrders); }
    }

    private static string ReadLiveCell(object row, string field)
    {
        if (row is IDictionary<string, object?> properties) return Convert.ToString(properties[field]) ?? "";
        if (row is IDictionary dictionary) return Convert.ToString(dictionary[field]) ?? "";
        return Convert.ToString(System.ComponentModel.TypeDescriptor.GetProperties(row)[field]?.GetValue(row)) ?? "";
    }

    private static void RequireLiveCustomers(IReadOnlyList<Dictionary<string, string>> rows)
    {
        if (rows.Count != 3 || !rows.Any(row => row["Id"] == "101" && row["Name"] == "Alice North" && row["City"] == "Kazan")
            || !rows.Any(row => row["Id"] == "205" && row["Name"] == "Boris River" && row["Email"] == "")
            || !rows.Any(row => row["Id"] == "309" && row["Name"] == "Clara Pine" && row["City"] == ""))
            throw new Exception("Customers must contain exactly the three distinct fixture rows and actual NULL values.");
    }

    private static void RequireLiveSqlState(Action action, string expected)
    {
        try { action(); }
        catch (PostgresException ex) when (ex.SqlState == expected) { return; }
        throw new Exception("Live PostgreSQL must reject this invalid operation with SQLSTATE " + expected + ".");
    }
}
