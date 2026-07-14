[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$root = [IO.Path]::GetFullPath($PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\\toolchains"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository: $root"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output | Out-Null

$runtimes = @("win-x64", "linux-x64")
$projects = @(
    @{ Name = "open16a-asm"; Project = "toolchains\\Open16A-ASM\\Open16A-ASM.csproj" },
    @{ Name = "open16a-ld"; Project = "toolchains\\Open16A-LD\\Open16A-LD.csproj" },
    @{ Name = "open16a-lsp"; Project = "toolchains\\Open16A-LSP\\Open16A-LSP.csproj" }
)

foreach ($runtime in $runtimes) {
    foreach ($project in $projects) {
        & dotnet restore (Join-Path $root $project.Project) "--runtime" $runtime "--force-evaluate" "--ignore-failed-sources"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $bin = Join-Path $output "$runtime\\bin"
    foreach ($project in $projects) {
        $destination = Join-Path $bin $project.Name
        $arguments = @(
            "publish", (Join-Path $root $project.Project),
            "--configuration", "Release",
            "--runtime", $runtime,
            "--output", $destination,
            "--no-restore",
            "-p:PublishSingleFile=true",
            "-p:SelfContained=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:PublishTrimmed=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:GenerateDocumentationFile=false"
        )
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Get-ChildItem -LiteralPath $destination -File |
            Where-Object { $_.Name -match '\.(runtimeconfig\.json|deps\.json|pdb|xml)$' } |
            Remove-Item -Force
    }
}

$basicRom = Join-Path $output "open16a-basic.bin"
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "toolchains\\Open16A-BASIC\\build.ps1") -Output $basicRom
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$nvimSource = Join-Path $root "toolchains\\Open16A-Nvim"
$nvimStage = Join-Path $output "nvim-stage\\Open16A-Nvim"
Copy-Item -LiteralPath $nvimSource -Destination $nvimStage -Recurse -Force
Remove-Item -LiteralPath (Join-Path $nvimStage ".git") -Recurse -Force -ErrorAction SilentlyContinue
foreach ($runtime in $runtimes) {
    $serverName = if ($runtime -eq "win-x64") { "Open16A-LSP.exe" } else { "Open16A-LSP" }
    $serverSource = Join-Path $output "$runtime\\bin\\open16a-lsp\\$serverName"
    $serverDestination = Join-Path $nvimStage "lsp\\$runtime"
    New-Item -ItemType Directory -Path $serverDestination -Force | Out-Null
    Copy-Item -LiteralPath $serverSource -Destination $serverDestination
}
Compress-Archive -Path $nvimStage -DestinationPath (Join-Path $output "Open16A-Nvim.zip") -Force
Remove-Item -LiteralPath (Join-Path $output "nvim-stage") -Recurse -Force

$vscode = Join-Path $root "toolchains\\Open16A-VSCode"
Push-Location $vscode
try {
    & npm run compile
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$vsce = Join-Path $vscode "node_modules\\.bin\\vsce.cmd"
foreach ($runtime in $runtimes) {
    $target = if ($runtime -eq "win-x64") { "win32-x64" } else { "linux-x64" }
    $serverName = if ($runtime -eq "win-x64") { "Open16A-LSP.exe" } else { "Open16A-LSP" }
    $stage = Join-Path $output "vscode-stage\\$target"
    $extension = Join-Path $stage "extension"
    New-Item -ItemType Directory -Path $extension -Force | Out-Null
    foreach ($item in @("package.json", "README.zh-CN.md", "language-configuration.json", ".vscodeignore", "syntaxes", "out")) {
        Copy-Item -LiteralPath (Join-Path $vscode $item) -Destination $extension -Recurse -Force
    }
    $serverDestination = Join-Path $extension "server\\$runtime"
    New-Item -ItemType Directory -Path $serverDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $output "$runtime\\bin\\open16a-lsp\\$serverName") -Destination $serverDestination

    Push-Location $extension
    try {
        & $vsce package --target $target --out (Join-Path $output "Open16A-VSCode-$target.vsix")
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
}
Remove-Item -LiteralPath (Join-Path $output "vscode-stage") -Recurse -Force

Write-Host "Published toolchains to: $output"
