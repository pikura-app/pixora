# Converts pikura-icon.svg -> pikura.ico
# Requires: Inkscape (winget install Inkscape.Inkscape) + ImageMagick (winget install ImageMagick.ImageMagick)

$svgPath   = "$PSScriptRoot\..\src\Pikura.Avalonia\Assets\pikura-icon.svg"
$icoPath   = "$PSScriptRoot\..\src\Pikura.Avalonia\Assets\pikura.ico"
$tmpDir    = "$env:TEMP\pikura_icon_convert"

New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

# ── Try Inkscape first ────────────────────────────────────────────────────────
$inkscape = Get-Command inkscape -ErrorAction SilentlyContinue

$sizes = @(16, 24, 32, 48, 64, 128, 256)

if ($inkscape) {
    Write-Host "Using Inkscape..." -ForegroundColor Cyan
    foreach ($s in $sizes) {
        $out = "$tmpDir\icon_$s.png"
        & inkscape $svgPath --export-type=png --export-width=$s --export-filename=$out 2>$null
        Write-Host "  Exported ${s}x${s}"
    }
} else {
    # ── Fallback: rsvg-convert (comes with librsvg, available via choco / scoop) ──
    $rsvg = Get-Command rsvg-convert -ErrorAction SilentlyContinue
    if ($rsvg) {
        Write-Host "Using rsvg-convert..." -ForegroundColor Cyan
        foreach ($s in $sizes) {
            $out = "$tmpDir\icon_$s.png"
            & rsvg-convert -w $s -h $s -o $out $svgPath
            Write-Host "  Exported ${s}x${s}"
        }
    } else {
        Write-Warning "Neither Inkscape nor rsvg-convert found."
        Write-Host "Install one of:" -ForegroundColor Yellow
        Write-Host "  winget install Inkscape.Inkscape"
        Write-Host "  scoop install librsvg"
        exit 1
    }
}

# ── Bundle PNGs into .ico using .NET System.Drawing ──────────────────────────
Add-Type -AssemblyName System.Drawing

$stream = [System.IO.File]::OpenWrite($icoPath)
$writer = New-Object System.IO.BinaryWriter($stream)

# ICO header
$writer.Write([uint16]0)       # reserved
$writer.Write([uint16]1)       # type = ICO
$writer.Write([uint16]$sizes.Count)

# Collect PNG bytes
$pngDataList = foreach ($s in $sizes) {
    [System.IO.File]::ReadAllBytes("$tmpDir\icon_$s.png")
}

# Directory entries (each 16 bytes)
$offset = 6 + ($sizes.Count * 16)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s    = $sizes[$i]
    $data = $pngDataList[$i]
    $w    = if ($s -ge 256) { 0 } else { $s }
    $h    = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)   # color count
    $writer.Write([byte]0)   # reserved
    $writer.Write([uint16]1) # planes
    $writer.Write([uint16]32)# bits per pixel
    $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]$offset)
    $offset += $data.Length
}

# Image data
foreach ($data in $pngDataList) {
    $writer.Write($data)
}

$writer.Close()
$stream.Close()

Write-Host "`nDone! ICO saved to: $icoPath" -ForegroundColor Green
Write-Host "Sizes included: $($sizes -join ', ')px"
