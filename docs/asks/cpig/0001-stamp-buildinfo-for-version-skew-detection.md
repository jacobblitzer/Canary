---
date: 2026-06-23
status: open
project: cpig
priority: medium
filed-by: penumbra
---

# Ask: stamp build-time git SHA into CPig.Rhino so cross-component version skew is detectable

## Why

Penumbra-perf shipped end-to-end version-skew detection on 2026-06-23 (commit at
`C:\Repos\Penumbra-perf` head, see `StartupDiagnostics.cs`). The pattern: every
Penumbra component (`.rhp`, embedded JS bundle, native DLL) carries its git SHA +
build timestamp; at first push, Penumbra.Rhino dumps the full provenance to BOTH
Rhino's command line AND Canary's NDJSON telemetry; if components disagree on git
SHA the conduit HARD-FAILS the compile rather than silently rendering with stale
code.

CPig.Rhino is loaded into the same Rhino process. Today its assembly version
(1.0.0.0) shows in the diagnostic dump, but there's no git SHA — so when a CPig
behavior is unexpected, we can't tell from the dump whether CPig is at HEAD or
several commits behind. That's the same class of confusion that drove 6 wasted
debug rounds in Penumbra over the last 2 days.

## What

Add an MSBuild target to `CPig.Rhino.csproj` (and probably the Interop +
Grasshopper projects too) modeled on Penumbra-perf's `StampBuildInfo`:

```xml
<Target Name="StampBuildInfo" BeforeTargets="CoreCompile">
  <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true" IgnoreExitCode="true">
    <Output TaskParameter="ConsoleOutput" PropertyName="_GitShaRaw" />
  </Exec>
  <Exec Command="git status --porcelain --untracked-files=no" ConsoleToMSBuild="true" IgnoreExitCode="true">
    <Output TaskParameter="ConsoleOutput" PropertyName="_GitStatusRaw" />
  </Exec>
  <PropertyGroup>
    <_GitSha Condition="'$(_GitShaRaw)' != ''">$(_GitShaRaw)</_GitSha>
    <_GitSha Condition="'$(_GitShaRaw)' == ''">unknown</_GitSha>
    <_GitDirty Condition="'$(_GitStatusRaw)' != ''">true</_GitDirty>
    <_GitDirty Condition="'$(_GitStatusRaw)' == ''">false</_GitDirty>
    <_BuildTimestamp>$([System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))</_BuildTimestamp>
    <_BuildInfoPath>$(IntermediateOutputPath)BuildInfo.generated.cs</_BuildInfoPath>
  </PropertyGroup>
  <ItemGroup>
    <_BuildInfoLines Include="namespace CPig.Rhino {" />
    <_BuildInfoLines Include="    internal static class BuildInfo {" />
    <_BuildInfoLines Include="        public const string GitSha = &quot;$(_GitSha)&quot;%3B" />
    <_BuildInfoLines Include="        public const bool GitDirty = $(_GitDirty)%3B" />
    <_BuildInfoLines Include="        public const string BuildTimestampUtc = &quot;$(_BuildTimestamp)&quot;%3B" />
    <_BuildInfoLines Include="    }" />
    <_BuildInfoLines Include="}" />
  </ItemGroup>
  <MakeDir Directories="$(IntermediateOutputPath)" />
  <WriteLinesToFile File="$(_BuildInfoPath)" Lines="@(_BuildInfoLines)" Overwrite="true" Encoding="UTF-8" />
  <ItemGroup>
    <Compile Include="$(_BuildInfoPath)" />
  </ItemGroup>
</Target>
```

Then somewhere in CPig.Rhino's plugin OnLoad (or a dedicated startup-diagnostics class):

```csharp
global::Rhino.RhinoApp.WriteLine(
  $"[cpig-startup] CPig.Rhino @ {Assembly.GetExecutingAssembly().Location}");
global::Rhino.RhinoApp.WriteLine(
  $"[cpig-startup]   git {BuildInfo.GitSha.Substring(0, Math.Min(8, BuildInfo.GitSha.Length))}" +
  $"{(BuildInfo.GitDirty ? " (dirty)" : "")}  built {BuildInfo.BuildTimestampUtc}");
```

Penumbra.Rhino's `StartupDiagnostics.cs` will start surfacing CPig's git SHA in
its dump automatically once `CPig.Rhino` exposes a public `BuildInfo.GitSha`
const at a discoverable type name (probably `CPig.Rhino.BuildInfo`) — we'll
reflect against it on first push.

## What "done" looks like

- `CPig.Rhino.csproj` (and Interop / Grasshopper as appropriate) have the
  `StampBuildInfo` target
- A `[cpig-startup]` banner appears in Rhino's command line at plugin load
  (3 lines, modeled on `[penumbra-startup]`)
- The shipped `.gha` / `.rhp` carries the git SHA as a const that Penumbra can
  read via reflection from the same AppDomain

## Cross-references

- Penumbra-perf reference impl:
  - `C:\Repos\Penumbra-perf\hosts\rhino\Penumbra.Rhino\Penumbra.Rhino.csproj`
    (`StampBuildInfo` target)
  - `C:\Repos\Penumbra-perf\hosts\rhino\Penumbra.Rhino\StartupDiagnostics.cs`
    (provenance dump + skew check)
- Pain history: 6 stale-component whack-a-mole incidents over 2026-06-21..23
  in Penumbra's CPig integration. Each one wasted 30-90 min isolating which
  component was stale because no end-to-end provenance trail existed.
