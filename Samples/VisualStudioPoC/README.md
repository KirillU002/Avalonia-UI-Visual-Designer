# Visual Studio PoC fixture

Откройте `SimpleAvaloniaApp.csproj` в Visual Studio, откройте `MainWindow.axaml`, затем выберите `Tools -> Open in Avalonia UI Visual Designer`.

Проверьте: измените `Button1.Content`, `Button1.Width` и `Canvas.Left`, нажмите `Apply changes` в отдельном host. В буфере Visual Studio должны измениться только соответствующие значения. Комментарий `<!-- keep me -->` и `custom:Custom.Unknown="keep-me"` должны остаться без изменений. После этого сохраните документ обычным `Ctrl+S`.
