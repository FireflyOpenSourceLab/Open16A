[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory,
    [switch]$NoRestore
)

$root = [IO.Path]::GetFullPath($PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\\$RuntimeIdentifier"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository: $root"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

if (-not $NoRestore) {
    & dotnet restore (Join-Path $root "OldSimulator.csproj") "--runtime" $RuntimeIdentifier "--ignore-failed-sources"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    & dotnet restore (Join-Path $root "OldSimulator.Expansion.EmbeddedAsm\OldSimulator.Expansion.EmbeddedAsm.csproj") "--runtime" $RuntimeIdentifier "--ignore-failed-sources"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$arguments = @(
    "publish",
    (Join-Path $root "OldSimulator.csproj"),
    "--configuration", "Release",
    "--runtime", $RuntimeIdentifier,
    "--output", $output,
    "-p:PublishSingleFile=true",
    "-p:SelfContained=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishTrimmed=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:GenerateDocumentationFile=false"
)
$arguments += "--no-restore"

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$forbidden = Get-ChildItem -LiteralPath $output -Recurse -File |
    Where-Object { $_.Name -match '\.(runtimeconfig\.json|pdb|xml)$' }
if ($forbidden) {
    $names = $forbidden.FullName -join [Environment]::NewLine
    throw "Publish output contains forbidden files:$([Environment]::NewLine)$names"
}

Write-Host "Published self-contained simulator to: $output"
