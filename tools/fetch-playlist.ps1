# Download a YouTube playlist (or single video) into a LOCAL, gitignored folder for the batch
# watcher (finding 028) to learn from. Personal, on-device research use only — the videos are
# third-party copyrighted content and are never committed (youtube/ is in .gitignore).
#
# Usage:
#   ./tools/fetch-playlist.ps1 "https://www.youtube.com/playlist?list=..."
#   ./tools/fetch-playlist.ps1 "<url>" -Out youtube -MaxHeight 360
#
# Then let it watch them:
#   dotnet run --project src/SyntheticMind.Watch -- youtube
#
# Low resolution is deliberate: the pipeline downsamples to 80x60 grayscale + 16 kHz mono anyway,
# so 360p wastes nothing and saves enormous disk/bandwidth. For a curated, consistent set (one
# subject, one person) the cross-situational binding actually has a chance to lock on (finding 022).

param(
    [Parameter(Mandatory = $true)][string]$Url,
    [string]$Out = "youtube",
    [int]$MaxHeight = 360
)

# Find a working yt-dlp WITHOUT trusting bare `python` — different terminals resolve `python` to
# different installs (the Microsoft Store stub often lacks pip packages). Prefer the standalone
# exe pip dropped in a Scripts folder; then py launcher; then python -m as a last resort.
function Resolve-YtDlp {
    # NOTE: the leading `,` keeps PowerShell from unwrapping a single-element array into a bare
    # string (which would make $yt[0] index into the string and return just 'C').
    $onPath = Get-Command yt-dlp -ErrorAction SilentlyContinue
    if ($onPath) { return ,@($onPath.Source) }

    $exe = Get-ChildItem -ErrorAction SilentlyContinue -Path @(
        "$env:APPDATA\Python\Python*\Scripts\yt-dlp.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python*\Scripts\yt-dlp.exe",
        "C:\Program Files\Python*\Scripts\yt-dlp.exe"
    ) | Select-Object -First 1
    if ($exe) { return ,@($exe.FullName) }

    if (Get-Command py -ErrorAction SilentlyContinue) { return ,@("py", "-m", "yt_dlp") }
    return ,@("python", "-m", "yt_dlp")
}

$yt = Resolve-YtDlp
$invoker = $yt[0]
$prefix = if ($yt.Length -gt 1) { $yt[1..($yt.Length - 1)] } else { @() }
Write-Host "  using yt-dlp: $($yt -join ' ')"

# YouTube now requires solving a JavaScript challenge to extract formats; yt-dlp needs both a JS
# runtime AND its challenge-solver script (a "remote component" it fetches once from its own GitHub,
# github.com/yt-dlp/ejs). Without both, videos come back "not available" or missing formats.
$jsArgs = @()
foreach ($rt in @("node", "deno", "bun")) {
    if (Get-Command $rt -ErrorAction SilentlyContinue) {
        $jsArgs = @("--js-runtimes", $rt, "--remote-components", "ejs:github")
        Write-Host "  JS runtime: $rt (+ yt-dlp's EJS challenge solver from GitHub)"
        break
    }
}
if ($jsArgs.Count -eq 0) { Write-Warning "No JS runtime (node/deno/bun) on PATH - YouTube may refuse extraction. Install Node.js or Deno." }

New-Item -ItemType Directory -Force $Out | Out-Null

$ytArgs = @(
    "-f", "bestvideo[height<=$MaxHeight]+bestaudio/best[height<=$MaxHeight]/best",
    "--merge-output-format", "mp4",
    "--no-overwrites",
    "--ignore-errors"
) + $jsArgs + @(
    "-o", "$Out/%(playlist_index)03d-%(title).80s.%(ext)s",
    $Url
)

& $invoker @($prefix + $ytArgs)

Write-Host ""
Write-Host "  Done. Now let it watch them:"
Write-Host "    dotnet run --project src/SyntheticMind.Watch -- $Out"
