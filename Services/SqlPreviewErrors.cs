using Microsoft.Data.SqlClient;
using System;

namespace FormDesigner.Services;

internal static class SqlPreviewErrors
{
    public static string Message(Exception error) => error switch
    {
        OperationCanceledException => "Загрузка SQL Server отменена.",
        SqlException { Number: -2 } or TimeoutException => "Превышено время ожидания SQL Server.",
        SqlException { Number: 18456 } => "Неверные учётные данные SQL Server.",
        SqlException { Number: 4060 } => "База данных SQL Server не найдена или недоступна.",
        SqlException { Number: 208 } => "Таблица SQL Server не найдена или недоступна.",
        SqlException { Number: 207 or 102 or 156 } => "Неверный SQL-запрос. Проверьте имена колонок и синтаксис.",
        _ => "Не удалось загрузить данные SQL Server. Проверьте подключение и SQL-запрос."
    };

    // Server messages can echo query literals or credentials. Keep identity and stack only.
    public static string Details(Exception error) =>
        $"exception={error.GetType().FullName}; number={(error as SqlException)?.Number}; inner={error.InnerException?.GetType().FullName}; stack={error.StackTrace}";
}
