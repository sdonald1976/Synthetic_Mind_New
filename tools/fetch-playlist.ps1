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

New-Item -ItemType Directory -Force $Out | Out-Null

# yt-dlp was installed via pip, so invoke it through python. ffmpeg (on PATH) merges audio+video.
python -m yt_dlp `
    -f "bestvideo[height<=$MaxHeight]+bestaudio/best[height<=$MaxHeight]/best" `
    --merge-output-format mp4 `
    --no-overwrites `
    --ignore-errors `
    -o "$Out/%(playlist_index)03d-%(title).80s.%(ext)s" `
    $Url

Write-Host ""
Write-Host "  Done. Now let it watch them:"
Write-Host "    dotnet run --project src/SyntheticMind.Watch -- $Out"
