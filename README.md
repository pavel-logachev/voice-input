# Voice Input

Нативное local-first приложение для Windows: удерживайте глобальную горячую клавишу, говорите по-русски и отпустите её — локальная расшифровка вставится в активное поле ввода.

## Статус

Рабочий MVP из исходников. Полный путь уже реализован:

```text
Ctrl+Shift+Space down
  → захват foreground HWND
  → WASAPI-запись в память
  → Ctrl+Shift+Space up
  → локальный GigaAM worker
  → Unicode SendInput в исходное окно
```

Готового установщика и подписанного релиза пока нет.

## Что работает

- WPF tray-приложение без Electron и локального web-сервера;
- глобальная комбинация `Ctrl + Shift + Space`;
- overlay с `WS_EX_NOACTIVATE`, не забирающий фокус;
- захват default microphone через WASAPI;
- преобразование в mono float32 PCM 16 kHz полностью в памяти;
- разбиение длинной записи на сегменты около 20 секунд по самым тихим окнам;
- GigaAM-v3 E2E RNNT Q4 через `transcribe.cpp`;
- отдельный ASR worker: падение native runtime не должно ронять tray-процесс;
- CPU backend по умолчанию — на тестовой машине он оказался быстрее встроенной AMD Vulkan-графики;
- автоматическая загрузка pinned runtime и модели с SHA-256-проверкой;
- защита от параллельных hotkey-сессий;
- отмена вставки, если foreground window изменилось во время диктовки;
- unit-тесты и отдельные Windows E2E harnesses для аудио, ASR и вставки.

## Запуск

Требования:

- Windows 10/11 x64;
- .NET 10 SDK;
- интернет при первом запуске.

```bash
dotnet run --project src/VoiceInput.App -c Release
```

Первый запуск загрузит примерно **200 MiB**:

- `transcribe.cpp` CPU/Vulkan runtime 0.1.3;
- `gigaam-v3-e2e-rnnt-Q4_K_M.gguf`.

Файлы сохраняются в `%LOCALAPPDATA%\VoiceInput`. После подготовки модели распознавание работает офлайн.

### Диктовка

1. Поставьте курсор в обычное поле ввода.
2. Удерживайте `Ctrl + Shift + Space`.
3. Говорите.
4. Отпустите клавиши.
5. Дождитесь локального распознавания и вставки.

Завершение приложения — через пункт **«Выход»** в tray menu.

## Проверка

```bash
dotnet format VoiceInput.sln --verify-no-changes --no-restore
dotnet build VoiceInput.sln -c Release
dotnet test VoiceInput.sln -c Release --no-build
```

Проверка реального default microphone без сохранения аудио:

```bash
dotnet run --project tests/VoiceInput.Audio.E2E -c Release
```

ASR E2E harness принимает пути к worker, runtime, модели и float32 PCM fixture. Он прогоняет production workflow `record → segment → worker → transcript → insertion sink`.

## Приватность

- микрофонный звук хранится только в памяти текущей сессии;
- аудио не сохраняется на диск и не отправляется в сеть;
- модель и native runtime загружаются только при подготовке;
- история расшифровок не ведётся;
- отдельная диагностическая запись включается только через явную переменную `VOICE_INPUT_DIAGNOSTIC_LOG` и не содержит аудио или текста расшифровки.

## Ограничения текущего MVP

- нет установщика, автообновления и code signing;
- используется default recording device — выбора микрофона в UI пока нет;
- вставка пока выполняется только через Unicode `SendInput`;
- нет clipboard/UI Automation fallback для несовместимых редакторов;
- нет `Esc`-отмены активной записи;
- elevated-приложения могут блокировать вставку из обычного процесса;
- UI первого запуска показывает стадии подготовки, но ещё не процент загрузки.

## Документы

- [Архитектурное видение](docs/ARCHITECTURE.md)
- [Исследование локальных ASR-моделей](docs/MODEL_RESEARCH.md)
- [Визуальная схема](docs/architecture.html)

## Лицензия

[MIT](LICENSE). Список используемых open-source компонентов: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
