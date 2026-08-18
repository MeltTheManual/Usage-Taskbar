# Draws the application icon and writes a multi-resolution .ico.
#
# The icon is a miniature of the hover card: a dark rounded tile with two meter bars, clay on top for
# Claude and green underneath for Codex, using the exact accent colours from ChipWindow. Each size is
# drawn at its own scale rather than shrunk from one large image, because two thin bars turn to mush
# when a 256px drawing is scaled down to 16px.
#
# Run this only when the icon design changes. The generated .ico is committed.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\Usage.App\assets'
$outFile = Join-Path $outDir 'Usage.ico'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Straight from ChipWindow.xaml.cs so the icon can never drift from the card.
$cardColor  = [System.Drawing.Color]::FromArgb(255, 42, 42, 42)
$trackColor = [System.Drawing.Color]::FromArgb(255, 60, 60, 60)
$claude     = [System.Drawing.Color]::FromArgb(255, 217, 119, 87)
$codex      = [System.Drawing.Color]::FromArgb(255, 16, 163, 127)

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $r = [Math]::Min($R, [Math]::Min($W, $H) / 2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0.5) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $W, $H)))
        return $path
    }
    $d = $r * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # The tile. Inset by half a pixel so the antialiased edge does not get clipped.
    $tile = New-RoundedPath -X 0.5 -Y 0.5 -W ($Size - 1) -H ($Size - 1) -R ($Size * 0.22)
    $brush = New-Object System.Drawing.SolidBrush($cardColor)
    $g.FillPath($brush, $tile)
    $brush.Dispose()
    $tile.Dispose()

    $pad   = [Math]::Max(2, [int][Math]::Round($Size * 0.18))
    $barH  = [Math]::Max(2, [int][Math]::Round($Size * 0.12))
    $gap   = [Math]::Max(2, [int][Math]::Round($Size * 0.14))
    $barW  = $Size - (2 * $pad)
    $top   = [int][Math]::Round(($Size - ((2 * $barH) + $gap)) / 2)
    $radius = $barH / 2.0

    # Top bar is Claude and reads fuller than the bottom one, so the icon looks like a reading
    # rather than a logo. Values are decorative only.
    $bars = @(
        @{ Y = $top;                    Fill = 0.72; Color = $claude },
        @{ Y = $top + $barH + $gap;     Fill = 0.42; Color = $codex  }
    )

    foreach ($bar in $bars) {
        $track = New-RoundedPath -X $pad -Y $bar.Y -W $barW -H $barH -R $radius
        $tb = New-Object System.Drawing.SolidBrush($trackColor)
        $g.FillPath($tb, $track)
        $tb.Dispose()
        $track.Dispose()

        $fillW = [Math]::Max($barH, [int][Math]::Round($barW * $bar.Fill))
        $fill = New-RoundedPath -X $pad -Y $bar.Y -W $fillW -H $barH -R $radius
        $fb = New-Object System.Drawing.SolidBrush($bar.Color)
        $g.FillPath($fb, $fill)
        $fb.Dispose()
        $fill.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# Vista and later allow PNG compressed images inside an .ico, which keeps the file small at 256px.
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($outFile)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR: reserved, type 1 for icon, image count.
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$images.Count)

# ICONDIRENTRY is 16 bytes each, so the first image starts after the header and all entries.
$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    # 256 is stored as 0, because the field is a single byte.
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim)          # width
    $bw.Write([byte]$dim)          # height
    $bw.Write([byte]0)             # palette size, 0 for truecolour
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # colour planes
    $bw.Write([uint16]32)          # bits per pixel
    $bw.Write([uint32]$img.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}

foreach ($img in $images) {
    $bw.Write($img.Bytes)
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

$written = Get-Item $outFile
Write-Host ("Wrote {0} ({1:N1} KB, {2} sizes)" -f $written.FullName, ($written.Length / 1KB), $images.Count)
