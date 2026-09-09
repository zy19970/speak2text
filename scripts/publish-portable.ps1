param(
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludeRuntimeFiles
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Speak2Text\Speak2Text.csproj"
$distRoot = Join-Path $repoRoot "dist"
$outDir = Join-Path $distRoot "Speak2Text-$RuntimeIdentifier"

if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}
New-Item $outDir -ItemType Directory -Force | Out-Null

Write-Host "Publishing Speak2Text..."
dotnet publish $project `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outDir

New-Item (Join-Path $outDir "engine") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $outDir "models") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $outDir "temp") -ItemType Directory -Force | Out-Null

if ($IncludeRuntimeFiles) {
    $engineSource = Join-Path $repoRoot "engine"
    $modelsSource = Join-Path $repoRoot "models"

    Get-ChildItem $engineSource -File | Where-Object { $_.Name -ne "README.md" } | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $outDir "engine") -Force
    }
    Get-ChildItem $modelsSource -File | Where-Object { $_.Name -ne "README.md" } | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $outDir "models") -Force
    }
}

Copy-Item (Join-Path $repoRoot "engine\README.md") (Join-Path $outDir "engine\README.md") -Force
Copy-Item (Join-Path $repoRoot "models\README.md") (Join-Path $outDir "models\README.md") -Force

$zipPath = Join-Path $distRoot "Speak2Text-$RuntimeIdentifier.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath

Write-Host "Done: $zipPath"
