<#
.SYNOPSIS
    Adds preview-off + Custom Preview trio to a CPig slop test JSON.

.DESCRIPTION
    For one slop test JSON:
    - Sets `"preview": false` on every node whose type is "component", except
      framework components (Log Hub, Crash Guard, Log Tap).
    - Inserts a `tint` color node and a `show` Custom Preview node.
    - Wires `<OutputNodeId>` output 0 -> show input 0, and tint output 0 -> show input 1.
    - Adds a "Preview" group containing tint and show.

    Pass -OutputNodeId to specify which existing node's output 0 should feed the
    Custom Preview. If unspecified, the script just toggles preview-off without
    adding the trio (useful for tests with no visible geometry, e.g. cpig-16).

.PARAMETER Path
    Absolute path to the slop test JSON.

.PARAMETER OutputNodeId
    Node id whose output 0 should feed Custom Preview. Omit for no trio.

.PARAMETER Argb
    "A,R,G,B" color for the swatch. Default "150,80,180,220" (semi-transparent blue).

.EXAMPLE
    .\add-preview-trio.ps1 -Path C:\Repos\CPig\research\slop_tests\02_implicit_tpms.json -OutputNodeId mesh
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [string]$OutputNodeId,
    [string]$Argb = "150,80,180,220"
)

$ErrorActionPreference = 'Stop'

$frameworkNames = @('Log Hub', 'Crash Guard', 'Log Tap', 'Custom Preview')

$raw = Get-Content -Raw $Path
$obj = $raw | ConvertFrom-Json

# 1. Set preview:false on all component-type nodes that aren't framework.
foreach ($node in $obj.nodes) {
    if ($node.type -eq 'component' -and ($node.name -notin $frameworkNames)) {
        # If preview already exists, leave it; otherwise add false.
        if (-not ($node.PSObject.Properties.Name -contains 'preview')) {
            $node | Add-Member -NotePropertyName 'preview' -NotePropertyValue $false -Force
        }
    }
}

# 2. If OutputNodeId given, append the preview trio.
if ($OutputNodeId) {
    $hasTint = $obj.nodes | Where-Object { $_.id -eq 'tint' }
    $hasShow = $obj.nodes | Where-Object { $_.id -eq 'show' }

    if (-not $hasTint) {
        $tint = [PSCustomObject]@{ id = 'tint'; type = 'color'; name = 'Result Tint'; argb = $Argb }
        $obj.nodes = @($obj.nodes) + $tint
    }
    if (-not $hasShow) {
        $show = [PSCustomObject]@{ id = 'show'; type = 'component'; name = 'Custom Preview'; guid = '537b0419-bbc2-4ff4-bf08-afe526367b2c' }
        $obj.nodes = @($obj.nodes) + $show
    }

    # Wires
    $newWires = @($obj.wires)
    if (-not ($newWires | Where-Object { $_.to -eq 'show' -and $_.to_input -eq 0 })) {
        $newWires += [PSCustomObject]@{ from = $OutputNodeId; from_output = 0; to = 'show'; to_input = 0 }
    }
    if (-not ($newWires | Where-Object { $_.to -eq 'show' -and $_.to_input -eq 1 })) {
        $newWires += [PSCustomObject]@{ from = 'tint'; from_output = 0; to = 'show'; to_input = 1 }
    }
    $obj.wires = $newWires

    # Group
    $hasPreviewGroup = $obj.groups | Where-Object { $_.name -eq 'Preview' }
    if (-not $hasPreviewGroup) {
        $previewGroup = [PSCustomObject]@{ name = 'Preview'; color = '#AA88FF'; nodes = @('tint', 'show') }
        $obj.groups = @($obj.groups) + $previewGroup
    }
}

$json = $obj | ConvertTo-Json -Depth 12
Set-Content -Path $Path -Value $json -Encoding UTF8

Write-Host "Updated $Path"
if ($OutputNodeId) { Write-Host "  Preview trio wired from '$OutputNodeId' output 0" }
else { Write-Host "  preview:false applied; no preview trio (no OutputNodeId given)" }
