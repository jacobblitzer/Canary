using Xunit;

// Test classes run SEQUENTIALLY, not in parallel.
//
// Deployment campaign, 2026-08-18. Two tests in ApproveReportExitCodeTests isolate
// themselves by mutating PROCESS-GLOBAL state - Directory.SetCurrentDirectory and the
// CANARY_WORKLOADS_DIR environment variable - and restore it in a finally. That is correct
// in isolation and unsound under xUnit's default, which runs distinct test CLASSES in
// parallel: while one test has the workloads root pointed at an empty temp directory, any
// concurrently-running test that resolves the workloads root sees that temp directory
// instead of the repo's own tree.
//
// The result was a suite that passed roughly two runs in three. Observed failures were
// ApproveReportExitCodeTests.Approve_SuiteBulk_SharedLayoutFallback_ApprovesAndReturnsZero
// and SettingsViewModelTests.StatusText_ReflectsCurrentSettings - different areas, same
// cause, and both green when run alone, which is exactly the signature that makes a flake
// easy to wave away as noise.
//
// This matters more than the seconds it costs. A test suite is the campaign's evidence: it
// is what every "463/463 green" claim in BUILD_LOG rests on, and a gate that is right most
// of the time cannot distinguish a real regression from its own noise. That is the
// silent-green defect this whole campaign exists to kill, wearing different clothes.
//
// The alternative - rewriting those two tests to avoid global state - is better in
// principle and was not chosen here: ReportCommand.ReportInner resolves the workloads root
// internally with no seam to inject one, so removing the global mutation means changing
// production code to suit a test. Serialising the assembly costs about a second on a suite
// that runs in three, and removes the entire class of race rather than the two instances
// that happened to surface.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
