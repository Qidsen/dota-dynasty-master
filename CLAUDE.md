# CLAUDE.md — Dota Dynasty

Главное продуктовое задание: `DOTA_DYNASTY_MASTER_SPEC.md` (v2.0). Его объём не сокращать.

## В начале каждой сессии
1. Прочитать этот файл и `docs/CURRENT_STATUS.md`.
2. `git status`, текущая ветка, последние коммиты.
3. `dotnet test` — убедиться, что база зелёная, прежде чем менять код.

## В конце сессии
Обновить `docs/CURRENT_STATUS.md` (что реально сделано и проверено, известные проблемы, следующие задачи) и добавить запись в `docs/DEVELOPMENT_LOG.md`. Архитектурные решения — в `docs/DECISION_LOG.md`.

## Команды
```
dotnet build              # сборка всего решения (DotaDynasty.slnx)
dotnet test               # все тесты (около 5 мин: приёмка гоняет матчи 5×5)
dotnet test --filter "Category!=Acceptance"   # быстрый набор (около 20 с)
dotnet run --project tools/Dynasty.MatchLab -c Release -- [heroes|swapped|mirror|base|lab] [firstSeed] [count] [maxMinutes]
dotnet run --project tools/Dynasty.MatchLab -c Release -- profiles   # профили героев для драфта
dotnet run --project tools/Dynasty.MatchLab -c Release -- skill 17 7 16   # игроки уровня 17 против 7 на зеркальных составах
# MATCHLAB_DEATHS=1 — печатать причины смертей героев по командам; MATCHLAB_PROFILE=1 — время по системам
```
Unity (6000.3.27f1, проект `unity/DotaDynasty`), всё в batchmode (если проект открыт в редакторе, batchmode его не откроет — закройте редактор или проверяйте копию без `Library`):
```
powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1      # после изменений в src/: DLL ядра + эталон детерминизма
Unity.exe -batchmode -projectPath unity/DotaDynasty -runTests -testPlatform EditMode -testResults r.xml   # детерминизм Mono
Unity.exe -batchmode -projectPath unity/DotaDynasty -runTests -testPlatform PlayMode -testResults r.xml   # просмотрщик
Unity.exe -batchmode -nographics -projectPath unity/DotaDynasty -executeMethod Dynasty.Unity.Editor.BuildTools.ProbeIl2Cpp   # детерминизм IL2CPP
Unity.exe -batchmode -nographics -projectPath unity/DotaDynasty -executeMethod Dynasty.Unity.Editor.BuildTools.BuildViewer   # Build/Viewer/DotaDynasty.exe
DotaDynasty.exe -dynasty-seed N -dynasty-screenshots DIR -dynasty-minute M   # скриншоты 3D/тактики по точкам зрения
DotaDynasty.exe -dynasty-seed N -dynasty-draft-screenshots DIR                # скриншоты экрана драфта
```
Требуется .NET SDK 10.

## Жёсткие правила
- Код симуляции (`src/`) не зависит от Unity: `netstandard2.1`, **C# 9** (ограничение Unity). Без file-scoped namespaces, records, `init`, global usings. Настройки — в `Directory.Build.props`.
- В симуляции запрещены `System.Random`, `DateTime.Now`, `float`/`double` в авторитетном состоянии (см. DECISION_LOG D-003), итерация по `Dictionary`/`HashSet` там, где порядок влияет на результат.
- Случайность — только через `SimulationContext.Random("<поток>")`.
- Состояние меняют только системы и обработчики команд через `SimulationContext`. Внешний код (AI, UI, менеджер) отправляет `ISimCommand`.
- Каждое доменное событие: стабильный `Type`, `SchemaVersion`, все поля пишутся в `WritePayload` в фиксированном порядке.
- **Честность порядка обработки (D-014):** внутри тика ни одно решение не должно зависеть от того, какой юнит обработан раньше (Dawn всегда первым). Погоня — по `StartPosition`, ограничения действий — по `StartFlags`; удары и касты, чей момент наступил, срабатывают. Новую механику проверяй зеркальным тестом.
- ИИ (`Dynasty.HeroAI`) работает только через публичный API и приказы; видит только `TeamKnowledge` своей команды (обзор). Новые решения ИИ — без обращения к невидимым юнитам; аудит `AiControlledMatch_NeverHoldsOrdersOnUnseenUnits`.
- Новые тесты поведения ИИ проверяй саботажем: отключи механизм — тест должен упасть.
- TDD: сначала падающий тест, потом код. Не выдавать непроверенное за работающее; не создавать пустые классы/экраны как «прогресс».
- Предупреждения компилятора = ошибки.

## Структура
- `src/Dynasty.Core` — ID, тики, RNG, хеши, fixed-point, события, команды, ruleset-идентичность, ядро симуляции.
- `src/Dynasty.MatchSimulation` — ruleset-схема, базовые правила и контент, навигация, бой, герои, способности, предметы, системы матча (`docs/MATCH_SIMULATION.md`).
- `src/Dynasty.Players` — профессиональные игроки, пулы героев, отчёты скаутов, генератор (`docs/PLAYERS.md`).
- `src/Dynasty.Training` — недельные тренировки, рост, усталость (`docs/PLAYERS.md`).
- `src/Dynasty.Drafting` — правила и сессия драфта, профили героев, составы, драфт-ИИ, план на игру (`docs/DRAFTING.md`).
- `src/Dynasty.HeroAI` — командный и тактический ИИ, стратегия команды из драфта (`docs/HERO_AI.md`).
- `src/Dynasty.Presentation` — слой представления без Unity: часы, кадры, сессия, статистика (`docs/PRESENTATION.md`).
- `unity/DotaDynasty` — Unity-просмотрщик; ядро в нём — DLL из `tools/sync-unity.ps1` (D-029).
- `tools/Dynasty.MatchLab` — консольный прогон матчей (net10.0, не для Unity).
- `tests/*` — xUnit, net10.0. Тестовые ruleset'ы собираются через `RulesetDraft`.
- `docs/` — архитектура, решения, статус, журнал.
