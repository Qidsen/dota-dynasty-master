# Архитектура

Полный список модулей — спека §30. Здесь описано то, что **реально существует**.

## Слои
```
Presentation (Unity: 3D, 2D, UI)      — этап 04+, только читает снимки/события, отправляет команды
        │
tools/Dynasty.MatchLab                — консольная Match Laboratory (существует)
        │
Dynasty.MatchSimulation               — правила, карта, юниты, бой, победа (этап 01, см. MATCH_SIMULATION.md)
        │
Dynasty.Core                          — ядро (этап 00) + fixed-point математика (этап 01)
```
Зависимости направлены только вниз. Ядро не знает ни о Unity, ни о MOBA.

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
Одинаковые `(seed, ruleset fingerprint, журнал команд, набор систем, сборка)` дают идентичный поток событий (равный `EventLog.Checksum`). Покрыто тестами `SameSeed_*` и `ReplayingRecordedCommands_ReproducesTheExactRun`. Кросс-рантаймовая гарантия (.NET ↔ Unity Mono/IL2CPP) держится, пока ядро целочисленное; см. D-003.

| Математика | `Fixed`, `FixedVector2` | Q47.16 fixed-point для всего авторитетного состояния (D-003) |
| Каноника | `CanonicalWriter` | Однозначная байтовая запись контента → SHA-256 fingerprint |

## Чего пока нет
Герои, способности, предметы, ИИ, обзор, навграф, сериализация снимков/сохранений, JSON-импорт ruleset'ов, Unity-проект.
