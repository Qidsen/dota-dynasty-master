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
dotnet test               # все тесты
dotnet run --project tools/Dynasty.MatchLab -c Release -- [base|lab] [firstSeed] [count] [maxMinutes]
```
Требуется .NET SDK 10. Unity 6.3 LTS (6000.3.x) понадобится с этапа 04.

## Жёсткие правила
- Код симуляции (`src/`) не зависит от Unity: `netstandard2.1`, **C# 9** (ограничение Unity). Без file-scoped namespaces, records, `init`, global usings. Настройки — в `Directory.Build.props`.
- В симуляции запрещены `System.Random`, `DateTime.Now`, `float`/`double` в авторитетном состоянии (см. DECISION_LOG D-003), итерация по `Dictionary`/`HashSet` там, где порядок влияет на результат.
- Случайность — только через `SimulationContext.Random("<поток>")`.
- Состояние меняют только системы и обработчики команд через `SimulationContext`. Внешний код (AI, UI, менеджер) отправляет `ISimCommand`.
- Каждое доменное событие: стабильный `Type`, `SchemaVersion`, все поля пишутся в `WritePayload` в фиксированном порядке.
- TDD: сначала падающий тест, потом код. Не выдавать непроверенное за работающее; не создавать пустые классы/экраны как «прогресс».
- Предупреждения компилятора = ошибки.

## Структура
- `src/Dynasty.Core` — ID, тики, RNG, хеши, fixed-point, события, команды, ruleset-идентичность, ядро симуляции.
- `src/Dynasty.MatchSimulation` — ruleset-схема, базовые правила, состояние и системы матча (`docs/MATCH_SIMULATION.md`).
- `tools/Dynasty.MatchLab` — консольный прогон матчей (net10.0, не для Unity).
- `tests/*` — xUnit, net10.0. Тестовые ruleset'ы собираются через `RulesetDraft`.
- `docs/` — архитектура, решения, статус, журнал.
