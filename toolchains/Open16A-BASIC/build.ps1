param(
    [string] $Program,
    [Parameter(Mandatory = $true)] [string] $Output,
    [switch] $AutoRun
)

$root = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $root
$build = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Force -Path $build | Out-Null

$basic = Join-Path $build 'basic.bin'
& dotnet run --project (Join-Path $root 'Open16A-ASM') -- (Join-Path $PSScriptRoot 'basic.asm') -o $basic
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ([string]::IsNullOrWhiteSpace($Program)) {
    if ($AutoRun) {
        Write-Error '-AutoRun requires -Program.'
        exit 2
    }
    Copy-Item -Force $basic $Output
    Write-Output "Built standalone BASIC interpreter -> $Output"
    exit 0
}

$programImage = Join-Path $build 'program.bin'
$packArgs = @('run', '--project', (Join-Path $root 'Open16A-BASIC-PACK'), '--', $Program, '-o', $programImage)
if ($AutoRun) { $packArgs += '--autorun' }
& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet run --project (Join-Path $root 'Open16A-LD') -- "$basic@1300h" "$programImage@4000h" --base 1300h -o $Output
exit $LASTEXITCODE
