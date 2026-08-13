param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "artifacts",
    [string]$PublishDirectory,
    [string]$Version = "1.0.0",
    [switch]$SkipPublish,
    [string]$SourceCommit = ""
)

$ErrorActionPreference = "Stop"
if ($Version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Version must use the exact MAJOR.MINOR.PATCH format without a leading 'v'."
}
if ($SourceCommit -and $SourceCommit -cnotmatch '^[0-9a-fA-F]{40}$') {
    throw "SourceCommit must be a full 40-character Git commit SHA."
}

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$projectPrefix = $projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $artifactRoot.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve inside $projectRoot"
}
$publishRoot = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    Join-Path $artifactRoot "publish-$RuntimeIdentifier"
}
elseif ([System.IO.Path]::IsPathRooted($PublishDirectory)) {
    [System.IO.Path]::GetFullPath($PublishDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $PublishDirectory))
}
if (-not $publishRoot.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "PublishDirectory must resolve inside $projectRoot"
}

$archiveName = "5thMR.Autoseeder-$Version-$RuntimeIdentifier.zip"
$archivePath = Join-Path $artifactRoot $archiveName
$manifestPath = Join-Path $artifactRoot "release-manifest.json"
$checksumPath = Join-Path $artifactRoot "SHA256SUMS.txt"

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
if (-not $SkipPublish -and (Test-Path -LiteralPath $publishRoot)) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
if (Test-Path -LiteralPath $checksumPath) { Remove-Item -LiteralPath $checksumPath -Force }

if ($SkipPublish) {
    if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
        throw "PublishDirectory does not exist: $publishRoot"
    }
}
else {
    $publishArguments = @(
        "publish",
        (Join-Path $projectRoot "Autoseeder.Client.csproj"),
        "--configuration", "Release",
        "--runtime", $RuntimeIdentifier,
        "--self-contained", "true",
        "--output", $publishRoot,
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:ContinuousIntegrationBuild=true",
        "-p:Deterministic=true",
        "-p:DebugSymbols=false",
        "-p:DebugType=None",
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version"
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

$publishedExecutable = Join-Path $publishRoot "5thMR.Autoseeder.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found at $publishedExecutable"
}

Add-Type -AssemblyName System.IO.Compression
$archiveStream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::CreateNew)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $files = Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Sort-Object FullName
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($publishRoot.TrimEnd('\').Length + 1).Replace("\", "/")
            $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [System.IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $archiveStream.Dispose() }

$archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    schemaVersion = 3
    product = "5thMR Autoseeder"
    version = $Version
    sourceCommit = if ($SourceCommit) { $SourceCommit.ToLowerInvariant() } else { $null }
    runtimeIdentifier = $RuntimeIdentifier
    targetFramework = "net10.0-windows"
    selfContained = $true
    singleFile = $true
    artifact = $archiveName
    sha256 = $archiveSha256
}
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$manifestJson = ($manifest | ConvertTo-Json -Depth 3) + "`n"
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, $utf8WithoutBom)

$manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksums = @(
    "$archiveSha256  $archiveName",
    "$manifestSha256  release-manifest.json"
) -join "`n"
[System.IO.File]::WriteAllText($checksumPath, $checksums + "`n", [System.Text.Encoding]::ASCII)

Write-Host "Created $archivePath"
Write-Host "SHA-256 $archiveSha256"
