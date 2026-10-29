# Generates src/AmbientSoundMixer.App/Assets/app.ico (original artwork: rounded square with sound-wave bars).
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\AmbientSoundMixer.App\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 30, 58, 96)), ([System.Drawing.Color]::FromArgb(255, 74, 168, 214)), 55
    $g.FillPath($grad, $path)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
    $heights = 0.30, 0.55, 0.78, 0.50, 0.28
    $bw = $size * 0.10; $gap = $size * 0.05
    $total = 5 * $bw + 4 * $gap; $x = ($size - $total) / 2
    foreach ($h in $heights) {
        $bh = $size * $h; $y = ($size - $bh) / 2
        $bar = New-Object System.Drawing.Drawing2D.GraphicsPath
        $rr = $bw / 2
        $bar.AddArc($x, $y, $bw, $bw, 180, 180); $bar.AddArc($x, $y + $bh - $bw, $bw, $bw, 0, 180); $bar.CloseFigure()
        $g.FillPath($white, $bar)
        $x += $bw + $gap
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }
$icoPath = Join-Path $out 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $frames[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }
$bw.Close(); $fs.Close()
Write-Host "Wrote $icoPath"
