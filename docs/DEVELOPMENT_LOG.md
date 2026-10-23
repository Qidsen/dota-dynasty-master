# Журнал разработки

## 2026-10-24 — Этап 00 Foundation
- Установлен .NET SDK 10.0.402; Unity 6000.3.9f1 уже стоит (не использовался).
- Создано решение и проекты, `Directory.Build.props` (C# 9/netstandard2.1 для `src/`, warnings as errors).
- По TDD (каждый компонент: падающие тесты → реализация) реализованы: RNG и потоки, FNV-1a, `SimTick`/`TickRate`, `EntityId`/аллокатор, `RulesetDescriptor`/fingerprint, `EventLog`, команды, `SimulationKernel`.
- Итог: сборка без предупреждений, 62/62 тестов.
- Документация: `CLAUDE.md`, `docs/ARCHITECTURE.md`, `ROADMAP.md`, `CURRENT_STATUS.md`, `DECISION_LOG.md`.
