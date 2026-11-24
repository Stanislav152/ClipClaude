# ClipClaude

Приложение для Windows, которое живёт в системном трее. Оно проверяет орфографию текста из буфера обмена или переводит его на английский через локально установленный **Claude Code CLI** (`claude`).

## Требования

- Windows 10/11 x64.
- Установленный и авторизованный Claude Code CLI: в терминале выполните `claude` и войдите в аккаунт.
- Для сборки нужен .NET 8 SDK. Для запуска готового exe .NET не нужен.

## Использование

1. Запустите `ClipClaude.exe` — в трее появится иконка.
2. Скопируйте текст (Ctrl+C) в любом приложении.
3. Нажмите горячую клавишу или выберите действие в меню иконки:
   - **Ctrl+Alt+S** — проверить орфографию;
   - **Ctrl+Alt+T** — перевести на английский.
4. В окне результата при необходимости поправьте текст и нажмите «Копировать» (или Ctrl+Enter — скопировать и закрыть; Esc — закрыть).

Левый клик по иконке открывает последний результат. Настройки (путь к `claude`, модель, горячие клавиши, таймаут, автокопирование, автозапуск) хранятся в `%APPDATA%\ClipClaude\settings.json`.

## Как вызывается Claude

```
claude -p --output-format text --tools "" --safe-mode --strict-mcp-config --no-session-persistence [--model <модель>]
```

- Промпт и текст передаются через stdin в UTF-8. Текст обрамлён тегами `<text>…</text>`, чтобы модель не воспринимала его как инструкции.
- `--tools ""` отключает все инструменты. `--safe-mode` отключает CLAUDE.md, MCP, хуки и плагины; обычная авторизация при этом работает.
- Рабочая папка процесса — `%LOCALAPPDATA%\ClipClaude\work`.
- По таймауту или по команде «Отменить запрос» завершается всё дерево процессов.
- Если `claude` установлен через npm (`claude.cmd`), он запускается через `cmd.exe /c` с фиксированными аргументами.

Флаги сверены с Claude Code 2.1.328.

## Приватность

Текст из буфера отправляется только в локальный CLI и не сохраняется на диск. В лог `%LOCALAPPDATA%\ClipClaude\logs\` (хранится 7 дней) пишутся только метаданные: длина текста, время, код выхода.

## Сборка

```powershell
# тесты
dotnet test

# один самодостаточный exe
dotnet publish src/ClipClaude -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# → publish\ClipClaude.exe
```

Иконки (`src/ClipClaude/Resources/*.ico`) генерируются скриптом `tools/make-icons.ps1`.

## Структура

```
src/ClipClaude/
  Program.cs              точка входа, single instance (mutex)
  TrayApp.cs              иконка в трее, меню, основной сценарий
  HotkeyManager.cs        глобальные горячие клавиши (RegisterHotKey)
  ClipboardService.cs     чтение и запись буфера (3 попытки)
  ClaudeCliRunner.cs      запуск CLI, stdin/stdout, таймаут
  Prompts.cs, ResponseParser.cs, DiffHighlighter.cs
  Settings/               SettingsStore (JSON, автозапуск), SettingsForm
  UI/                     ResultForm, ErrorForm, Theme (светлая/тёмная)
tests/ClipClaude.Tests/   xUnit
tests/FakeClaude/         поддельный claude: успех, ошибка, зависание
```

## Ограничения первой версии

Интерфейс только на русском; перевод только на английский; нет установщика и цифровой подписи (при первом запуске SmartScreen может показать предупреждение).
