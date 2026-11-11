param(
    [Parameter(Mandatory=$true)][string]$SptRoot,
    [string]$Dotnet = 'dotnet',
    [string]$OutputRoot = '',
    [switch]$BinariesOnly
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
$projectXml=[xml](Get-Content -Raw -LiteralPath (Join-Path $root 'client/UH60FastRopeInsertion.csproj'))
$version=[string]$projectXml.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a three-part release version.'}
$game=[IO.Path]::GetFullPath($SptRoot)
if (-not (Test-Path -LiteralPath (Join-Path $game 'BepInEx/core/BepInEx.dll'))) { throw 'A local SPT installation is needed for compile references only.' }
if (-not $OutputRoot) { $OutputRoot=Join-Path (Split-Path -Parent $root) ('output-'+(Get-Date -Format 'yyyyMMdd-HHmmss')) }
$output=[IO.Path]::GetFullPath($OutputRoot)
if ($output.StartsWith($game.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq $game) { throw 'Build output must not be inside the game.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory. Builds do not delete or overwrite release directories.' }
New-Item -ItemType Directory -Path $output | Out-Null
function Invoke-Dotnet([string[]]$arguments) {
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($arguments -join ' ')" }
}
Invoke-Dotnet -arguments @('build',(Join-Path $root 'client/UH60FastRopeInsertion.csproj'),'-c','Release',"-p:SptRoot=$game")
$core=Join-Path $root 'client/bin/Release/net472/UH60FastRopeInsertion.dll'
$coreHash=(Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash
Invoke-Dotnet -arguments @('build',(Join-Path $root 'performance-client/UH60Performance.csproj'),'-c','Release',"-p:SptRoot=$game","-p:UH60CoreSha256=$coreHash")
Invoke-Dotnet -arguments @('run','--project',(Join-Path $root 'tests/PublicationContracts.csproj'),'-c','Release','--',(Join-Path $root 'assets/cabin-flight.csv'))
if($BinariesOnly) {
    Copy-Item -LiteralPath $core -Destination $output
    Copy-Item -LiteralPath (Join-Path $root 'performance-client/bin/Release/net472/UH60Performance.dll') -Destination $output
    Write-Output "DLL-only output (not installed or published): $output"
    Get-ChildItem -LiteralPath $output -File | ForEach-Object { $_.Name+' SHA256 '+(Get-FileHash -LiteralPath $_.FullName).Hash }
    return
}
$package=Join-Path $output 'package'
$plugin=Join-Path $package 'BepInEx/plugins/UH60FastRopeInsertion'
New-Item -ItemType Directory -Path (Join-Path $plugin 'assets') | Out-Null
Copy-Item -LiteralPath $core -Destination $plugin
Copy-Item -LiteralPath (Join-Path $root 'performance-client/bin/Release/net472/UH60Performance.dll') -Destination $plugin
if(-not (Get-Content -Raw -LiteralPath (Join-Path $root 'client/UH60FastRopeInsertion.csproj')).Contains('UH60.CabinFlight')) {
    Copy-Item -LiteralPath (Join-Path $root 'assets/cabin-flight.csv') -Destination (Join-Path $plugin 'assets')
}
$docs=@('README.md','README_RU.md','LICENSE','THIRD_PARTY_NOTICES.md','DEPENDENCIES.md','dependencies.json','CHANGELOG.md','release.json')
foreach ($file in $docs) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $plugin }
function Manifest([string]$directory) {
    @(Get-ChildItem -LiteralPath $directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($directory.Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest=Manifest $package
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $output 'package-files.json'),(ConvertTo-Json -Depth 6 -InputObject $manifest),$utf8)
$zip=Join-Path $output "JozinZBazin-UH60FastRopeInsertion-$version-CANDIDATE.zip"
Compress-Archive -LiteralPath (Join-Path $package 'BepInEx') -DestinationPath $zip -CompressionLevel Optimal
# A second STRICT allowlist: no obj/bin/build caches, game references or local reference artifacts.
$source=Join-Path $output 'source'
New-Item -ItemType Directory -Path $source | Out-Null
foreach ($folder in @('client','performance-client','tests')) {
    $dest=Join-Path $source $folder
    New-Item -ItemType Directory -Path $dest | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $root $folder) -File | Where-Object { $_.Extension -in @('.cs','.csproj') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dest }
}
foreach ($folder in @('assets','client/assets')) {
    $dest=Join-Path $source $folder
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $root $folder) -File | Where-Object {$_.Extension -in @('.csv','.wav')} | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dest }
}
foreach ($file in ($docs+@('build.ps1','.gitignore','source-baseline.json','PUBLICATION_CHANGES.md','PUBLICATION_CHECKLIST_RU.md','FORGE_PAGE_RU.md'))) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $source
}
$sourceZip=Join-Path $output "JozinZBazin-UH60FastRopeInsertion-$version-SOURCE.zip"
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $source -Force | Select-Object -ExpandProperty FullName) -DestinationPath $sourceZip -CompressionLevel Optimal
$releaseMetadata=Get-Content -Raw -LiteralPath (Join-Path $root 'release.json') | ConvertFrom-Json
$summary=[ordered]@{ status='CANDIDATE_NOT_APPROVED_FOR_PUBLICATION'; version=$version; coreSha256=$coreHash;
    packageSha256=(Get-FileHash -LiteralPath $zip).Hash; sourceSha256=(Get-FileHash -LiteralPath $sourceZip).Hash;
    packageFiles=$manifest.Count; publicRepository=$releaseMetadata.publicSourceUrl;
    virusTotalReports=@($releaseMetadata.virusTotalReports); liveTest='NOT_RUN';
    gameInstallationModified=$false; output=$output }
[IO.File]::WriteAllText((Join-Path $output 'build-report.json'),(ConvertTo-Json -Depth 6 -InputObject $summary),$utf8)
Write-Output "Candidate and exact public sources prepared: $output"
Write-Output 'NOT installed, launched, uploaded or release-approved. Complete PUBLICATION_CHECKLIST_RU.md.'
