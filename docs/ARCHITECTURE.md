# Архитектура

Полный список модулей — спека §30. Здесь описано то, что **реально существует**.

## Слои
```
unity/DotaDynasty (Unity 6.3, URP)    — просмотрщик: 3D-трансляция, Tactical 2D, HUD (этап 04, PRESENTATION.md)
        │  ядро подключено DLL-файлами (tools/sync-unity.ps1, D-029)
Dynasty.Presentation                  — без Unity: часы, кадры по точке зрения, сессия, лента, статистика, раскладка карты
        │
tools/Dynasty.MatchLab                — консольная Match Laboratory
        │
Dynasty.HeroAI                        — командный и тактический ИИ, стратегия команды из драфта (HERO_AI.md)
        │
Dynasty.Drafting                      — правила и сессия драфта, профили героев, составы, драфт-ИИ, план на игру (DRAFTING.md)
        │
Dynasty.MatchSimulation               — правила, карта, юниты, бой, победа (MATCH_SIMULATION.md)
        │
Dynasty.Core                          — ядро + fixed-point математика
```
Зависимости направлены только вниз. Ядро не знает ни о Unity, ни о MOBA. Представление только читает состояние и события; матч, который смотрят, идёт так же, как без показа (тест `WatchingDoesNotChangeTheMatch_*`).

## Dynasty.Core

| Компонент | Тип | Назначение |
|---|---|---|
| Время | `SimTick`, `TickRate` | Фиксированный тик; перевод миллисекунд ruleset'а в тики целочисленно, округление half-up |
| ID | `EntityId`, `EntityIdAllocator` | Стабильные ID, монотонные, не переиспользуются; `0` = None; состояние аллокатора — одно число |
| RNG | `DeterministicRandom`, `RandomStreams`, `SplitMix64` | xoshiro256**; именованные потоки независимы от порядка запроса; состояние сериализуемо |
| Хеши | `Fnv1a64` | Дешёвая контрольная сумма событий/команд/состояния (не криптография) |
| События | `IDomainEvent`, `EventLog`, `EventRecord` | Append-only, упорядочено по тикам, последовательный номер, бегущий checksum |
| Команды | `ISimCommand`, `ICommandHandler`, `CommandLog`, `CommandRejected` | Валидация в тик исполнения, затем применение; отказ = событие |
| Правила | `RulesetDescriptor`, `RulesetFingerprint` | ID + версия + SHA-256 fingerprint неизменяемого снимка правил |
| Ядро | `SimulationKernel<TState>`, `SimulationContext<TState>`, `ISimulationSystem<TState>`, `SimulationConfig` | Цикл симуляции |

## Цикл тика
```
Step():
  1. команды, назначенные на CurrentTick — в порядке подачи:
       Validate(state) → Apply(context)  |  иначе событие core.command_rejected
  2. системы — в порядке регистрации: system.Tick(context)
  3. CurrentTick++
```
`EndSimulation()` завершает текущий тик полностью, после чего `Step()` запрещён.

## Гарантия воспроизводимости (текущая)
Одинаковые `(seed, ruleset fingerprint, журнал команд, набор систем, сборка)` дают идентичный поток событий (равный `EventLog.Checksum`). Покрыто тестами `SameSeed_*` и `ReplayingRecordedCommands_ReproducesTheExactRun`. Кросс-рантаймовая гарантия проверена: демо-матчи дают одинаковые контрольные суммы в .NET, Unity Mono (EditMode-тест) и Unity IL2CPP (собранный Windows-плеер, `BuildTools.ProbeIl2Cpp`); см. D-003, D-029.

| Математика | `Fixed`, `FixedVector2` | Q47.16 fixed-point для всего авторитетного состояния (D-003) |
| Каноника | `CanonicalWriter` | Однозначная байтовая запись контента → SHA-256 fingerprint |

## Чего пока нет
Сериализация снимков и сохранений, реплеи с перемоткой, JSON-импорт ruleset'ов, меню и экраны менеджера.
