# TESTS_UI.md — Canary Test Specifications (Phases 8–12)

## Phase 8 Tests

### ConsoleTestLogger Tests (Unit)

```
ConsoleTestLogger_Log_FormatsWithTimestamp
  Setup: create ConsoleTestLogger, redirect Console.Out
  Action: call Log("test message")
  Assert: output matches "[HH:mm:ss] [Canary] test message"

ConsoleTestLogger_Quiet_SuppressesOutput
  Setup: create ConsoleTestLogger with Quiet=true, redirect Console.Out
  Action: call Log("test message")
  Assert: output is empty

TestRunner_UsesInjectedLogger
  Setup: create TestRunner with mock ITestLogger
  Action: call RunSuiteAsync with a test that has no agent (will fail/crash)
  Assert: mock logger received Log and/or LogStatus calls
```

---

## Phase 9 Tests

### WorkloadExplorer Tests (Unit)

```
WorkloadExplorer_DiscoverWorkloads_FindsConfigFiles
  Setup: create temp dir with workloads/test1/workload.json and workloads/test1/tests/foo.json
  Action: LoadWorkloadsAsync(tempDir)
  Assert: returns 1 workload with 1 test definition

WorkloadExplorer_EmptyDirectory_ReturnsEmptyList
  Setup: create empty temp dir
  Action: LoadWorkloadsAsync(tempDir)
  Assert: returns empty list, no exception

WorkloadExplorer_MissingTestsDir_ReturnsWorkloadWithNoTests
  Setup: create temp dir with workloads/test1/workload.json but no tests/ folder
  Action: LoadWorkloadsAsync(tempDir)
  Assert: returns 1 workload with 0 test definitions
```

---

## Phase 10 Tests

### BaselineManager Tests (Unit)

```
BaselineManager_ApproveCheckpoint_CopiesSingleFile
  Setup: create temp dir with candidates/after_stroke.png
  Action: ApproveCheckpoint(workloadsDir, "test1", "mytest", "after_stroke")
  Assert: baselines/after_stroke.png exists, is identical to candidate

BaselineManager_RejectCheckpoint_DeletesCandidate
  Setup: create temp dir with candidates/after_stroke.png
  Action: RejectCheckpoint(workloadsDir, "test1", "mytest", "after_stroke")
  Assert: candidates/after_stroke.png no longer exists
```

### TestResultSerializer Tests (Unit)

```
TestResultSerializer_RoundTrip_PreservesAllFields
  Setup: create TestResult with all fields populated (name, workload, status, duration, checkpoint results)
  Action: SaveAsync then LoadAsync
  Assert: all fields match original
```

### ResultsHistory Tests (Unit)

```
ResultsHistory_ScansDirectory_FindsResults
  Setup: create temp results dir with saved TestResult JSON files
  Action: scan directory
  Assert: returns correct count with correct metadata

ResultsHistory_EmptyDirectory_ReturnsEmptyList
  Setup: create empty temp dir
  Action: scan directory
  Assert: returns empty list, no exception
```

---

## Phase 11 Tests

### Test Definition Editor Tests (Unit)

```
TestDefinitionValidator_MissingName_ReturnsError
  Setup: TestDefinition with Name = null
  Action: validate
  Assert: error message contains "name"

TestDefinitionValidator_InvalidTolerance_ReturnsError
  Setup: TestCheckpoint with Tolerance = 1.5
  Action: validate
  Assert: error message contains "tolerance"

TestDefinitionEditor_Save_WritesValidJson
  Setup: populate TestDefinition with all fields
  Action: save to temp path, reload
  Assert: all fields round-trip correctly
```

### Workload Editor Tests (Unit)

```
WorkloadEditor_Save_WritesValidJson
  Setup: populate WorkloadConfig with all fields
  Action: save to temp path, reload
  Assert: all fields round-trip correctly
```

### GuiTestLogger Tests (Unit)

```
GuiTestLogger_OnLog_FiresEvent
  Setup: create GuiTestLogger, subscribe to OnLog
  Action: call Log("message")
  Assert: event fired with "message"

GuiTestLogger_OnStatus_IncludesLevel
  Setup: create GuiTestLogger, subscribe to OnStatus
  Action: call LogStatus("PASS", "test", TestStatusLevel.Pass)
  Assert: event fired with correct symbol, message, and level
```

---

## Phase 12 Tests

### Integration Tests (Unit)

```
Integration_CLI_StillWorks_AfterRefactor
  Setup: build Canary.Harness
  Action: run with --help
  Assert: output contains "canary" and "run"

Integration_RunCommand_UsesCore
  Setup: invoke RunCommand handler with bad workload path
  Assert: error message generated (proves Core is wired correctly)

MainForm_LaunchesWithoutError
  Setup: create MainForm instance
  Action: show and immediately close
  Assert: no exceptions thrown
```

---

## Updated Test Count Progression

| Phase | New Tests | Total |
|-------|-----------|-------|
| 0–7   | 52        | 52    |
| 8     | 3         | 55    |
| 9     | 3         | 58    |
| 10    | 5         | 63    |
| 11    | 6         | 69    |
| 12    | 3         | 72    |
