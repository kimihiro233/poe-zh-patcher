Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [Math]::Max(2, [int]($s * 0.18))
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 18, 24))
    $g.FillPath($bg, $path)

    $penW = [float][Math]::Max(1.0, $s / 26.0)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 196, 158, 86), $penW)
    $g.DrawPath($pen, $path)

    $fontPx = [float]($s * 0.58)
    if ($fontPx -lt 9) { $fontPx = 9 }
    $font = New-Object System.Drawing.Font("Microsoft JhengHei", $fontPx, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, [float]$s, [float]$s)
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 203, 138))
    $g.DrawString("繁", $font, $gold, $rect, $fmt)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $bmp.Dispose()
    $font.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $dim = 0
    if ($s -lt 256) { $dim = $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()

$target = Join-Path $PSScriptRoot "app.ico"
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Output ("icon: {0} bytes -> {1}" -f (Get-Item $target).Length, $target)
