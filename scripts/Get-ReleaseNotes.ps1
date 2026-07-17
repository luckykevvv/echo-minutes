param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$ChangelogPath = 'CHANGELOG.md',

    [string]$OutputPath = 'artifacts/release-notes.md'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ChangelogPath -PathType Leaf)) {
    throw "Changelog not found: $ChangelogPath"
}

$lines = @(Get-Content -LiteralPath $ChangelogPath -Encoding UTF8)
$escapedVersion = [Regex]::Escape($Version)
$start = -1

for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match "^## \[$escapedVersion\](?:\s+-\s+.+)?$") {
        $start = $index
        break
    }
}

if ($start -lt 0) {
    throw "CHANGELOG.md is missing a section for version $Version."
}

$end = $lines.Count
for ($index = $start + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^## \[') {
        $end = $index
        break
    }
}

$notes = if ($end -le $start + 1) {
    @()
} else {
    @($lines[($start + 1)..($end - 1)])
}
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[0])) {
    $notes = @($notes | Select-Object -Skip 1)
}
while ($notes.Count -gt 0 -and [string]::IsNullOrWhiteSpace($notes[-1])) {
    $notes = @($notes | Select-Object -First ($notes.Count - 1))
}

if ($notes.Count -eq 0 -or
    -not ($notes | Where-Object { $_ -match '^###\s+\S' }) -or
    -not ($notes | Where-Object { $_ -match '^-\s+\S' })) {
    throw "The changelog section for version $Version must contain categorized release notes with concrete bullet points."
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$content = ($notes -join "`n") + "`n"
[IO.File]::WriteAllText($OutputPath, $content, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared release notes for $Version at $OutputPath"
