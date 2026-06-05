---
date: 2026-06-04
tags: [bug, canary, ui]
status: resolved
project: canary
severity: high
component: ui-avalonia
fix-commit: pending
---

# BUG-0010 — Canary.UI crashes on cold first launch ("opens then closes")

## Symptom

Launching `Canary.UI.exe` opens the window, which then immediately closes.
Launching it a **second** time works fine. Intermittent — correlated with a cold
(first-boot / cold-file-cache) launch.

## Evidence

Windows Application Event Log (the window vanished before any in-app surface could
show the error):

```
.NET Runtime (Id 1026): Canary.UI.exe — process terminated due to an unhandled exception.
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
   at System.Collections.Generic.List`1.Enumerator.MoveNext()
   at Avalonia.Controls.Presenters.PanelContainerGenerator.OnItemsChanged(...)
   at Avalonia.Controls.ItemsSourceView...ICollectionChangedListener.PostChanged(...)
Application Error (Id 1000): Canary.UI.exe, exception 0xe0434352 (managed)
```

## Root cause

`MainWindowViewModel`'s constructor calls `ApplyWorkloadsDir(detected)`, which
fire-and-forgets two async disk scans **before the window renders**:

```csharp
_ = Sessions.LoadWorkloadsAsync(dir);
_ = Tests.LoadWorkloadsAsync(dir);
```

`WorkloadTreeViewModel.LoadAsync` awaited the scan with **`ConfigureAwait(false)`**,
so the continuation — which does `Roots.Clear()` + `Roots.Add(...)` /
`Children.Add(...)` on UI-bound `ObservableCollection`s — ran on a **threadpool
thread**. Avalonia (unlike WPF) does **not** marshal collection-change
notifications, so an off-UI-thread mutation races the container generator while it
enumerates items → "Collection was modified."

It's a data race, which is why it's intermittent: on a **cold** launch the disk
scan is slow enough that its continuation lands exactly while the window is first
realizing the bound list; on a **warm** launch the timing differs and they don't
collide.

Two siblings shared the anti-pattern (`ConfigureAwait(false)` before UI mutation):
`SessionsViewModel.LoadWorkloadsAsync` (the other startup loader — latent crash on
the Sessions tab) and `ResultsViewerViewModel.LoadGifStatsAsync` (sets the
`GifStats` bound property off-thread).

## Fix

Resume on the UI thread before touching bound state — `ConfigureAwait(false)` →
`ConfigureAwait(true)` at the three continuation points:

- `ViewModels/WorkloadTreeViewModel.cs` `LoadAsync` (the crash)
- `ViewModels/SessionsViewModel.cs` `LoadWorkloadsAsync`
- `ViewModels/ResultsViewerViewModel.cs` `LoadGifStatsAsync`

The heavy file IO inside the scan/identify calls remains async (off-thread); only
the final continuation that mutates UI-bound state is marshalled back. Each fix
carries an inline comment so the `ConfigureAwait(true)` isn't "tidied" back later.

## Verification

- `dotnet build src/Canary.UI.Avalonia/Canary.UI.Avalonia.csproj -c Release` → 0/0.
- Operator-confirmable: kill any `Canary.UI.exe`, launch the freshly built exe
  cold, repeat several times — it should no longer open-then-close. (A race can't
  be proven absent by one run, but the off-thread mutation that caused it is gone.)

## Lesson / guard

`ConfigureAwait(false)` is correct for library/IO code, but **any continuation
that mutates a UI-bound `ObservableCollection` or raises `PropertyChanged` must be
on the UI thread.** In Avalonia this is unforgiving (no auto-marshaling). Grep
`ConfigureAwait(false)` in `Canary.UI.Avalonia` before adding new async loaders.
