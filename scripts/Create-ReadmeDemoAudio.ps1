param(
    [string]$OutputPath = 'docs/assets/demo-meeting-en.mp3',
    [string]$FfmpegPath = 'publish/final-visual-review/models/ffmpeg/bin/ffmpeg.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
    throw "FFmpeg not found: $FfmpegPath"
}

Add-Type -AssemblyName System.Speech

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$temporaryDirectory = Join-Path $env:TEMP 'echo-minutes-readme-demo'
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
$temporaryWav = Join-Path $temporaryDirectory 'demo-meeting.wav'

$prompt = New-Object System.Speech.Synthesis.PromptBuilder
$prompt.AppendText('Good morning. Let us review the EchoMinutes release.')
$prompt.AppendBreak([TimeSpan]::FromMilliseconds(650))
$prompt.AppendText('The new onboarding helps people choose and download a speech model before their first meeting.')
$prompt.AppendBreak([TimeSpan]::FromMilliseconds(750))
$prompt.AppendText('Next, we should prepare screenshots and a short demonstration for the project page.')
$prompt.AppendBreak([TimeSpan]::FromMilliseconds(650))
$prompt.AppendText('Agreed. The recording stays on this computer, and the transcript can be exported after the meeting.')

$synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synthesizer.SelectVoice('Microsoft Zira Desktop')
    $synthesizer.Rate = -1
    $synthesizer.SetOutputToWaveFile($temporaryWav)
    $synthesizer.Speak($prompt)
} finally {
    $synthesizer.Dispose()
}

& $FfmpegPath -hide_banner -loglevel error -y `
    -i $temporaryWav -ar 16000 -ac 1 -b:a 64k $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg failed with exit code $LASTEXITCODE."
}

Write-Output "Created synthetic demo audio at $OutputPath"
