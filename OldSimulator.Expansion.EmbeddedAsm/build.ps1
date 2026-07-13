[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$project = Join-Path $PSScriptRoot "OldSimulator.Expansion.EmbeddedAsm.csproj"
$arguments = @("build", $project, "--configuration", $Configuration)
if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$dll = Join-Path $PSScriptRoot "bin\$Configuration\net10.0\Open16A.EmbeddedAsm.dll"
Write-Host "Built embedded firmware card: $dll"
