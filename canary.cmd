@echo off
rem Canary CLI wrapper (flight-recorder Phase A enablement, 2026-07-02).
rem Forwards to the DEBUG harness build — the output that a plain `dotnet build Canary.sln`
rem refreshes, so the wrapper never points at a stale configuration by default.
rem IMPORTANT: run from C:\Repos\Canary — the harness resolves workloads\ from the CURRENT
rem directory (SessionCommand/RunCommand use Directory.GetCurrentDirectory()).
"%~dp0src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe" %*
