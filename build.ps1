# Brightness Tray build script
# Compiles bin\BrightnessTray.exe using the built-in .NET Framework C# compiler (csc.exe)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$assetsDir = Join-Path $scriptDir "assets"
$binDir = Join-Path $scriptDir "bin"
$iconPath = Join-Path $assetsDir "BrightnessTray.ico"
$exePath = Join-Path $binDir "BrightnessTray.exe"
$manifestPath = Join-Path $scriptDir "app.manifest"

if (!(Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
}
if (!(Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

# 1. Generate icon if missing
if (!(Test-Path $iconPath)) {
    Write-Host "Generating multi-resolution app icon: assets\BrightnessTray.ico..."
    Add-Type -AssemblyName System.Drawing

    function New-SunBitmap([int]$size) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        
        $amber = [System.Drawing.Color]::FromArgb(255, 184, 28) # #FFB81C
        $brush = New-Object System.Drawing.SolidBrush($amber)
        $pen = New-Object System.Drawing.Pen($amber, [Math]::Max(1.0, $size * 0.08))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        $cx = $size / 2.0
        $cy = $size / 2.0
        $coreRadius = $size * 0.22
        $g.FillEllipse($brush, [float]($cx - $coreRadius), [float]($cy - $coreRadius), [float]($coreRadius * 2), [float]($coreRadius * 2))

        $r1 = $size * 0.32
        $r2 = $size * 0.44
        for ($i = 0; $i -lt 8; $i++) {
            $angle = $i * [Math]::PI / 4.0
            $x1 = $cx + $r1 * [Math]::Cos($angle)
            $y1 = $cy + $r1 * [Math]::Sin($angle)
            $x2 = $cx + $r2 * [Math]::Cos($angle)
            $y2 = $cy + $r2 * [Math]::Sin($angle)
            $g.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
        }

        $pen.Dispose()
        $brush.Dispose()
        $g.Dispose()
        return $bmp
    }

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
    $images = @()
    foreach ($sz in $sizes) {
        $bmp = New-SunBitmap $sz
        if ($sz -eq 256) {
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $ms.ToArray()
            $ms.Dispose()
            $images += [PSCustomObject]@{ Size = $sz; IsPng = $true; Bytes = $bytes }
        } else {
            $ms = New-Object System.IO.MemoryStream
            $bw = New-Object System.IO.BinaryWriter($ms)
            $bw.Write([uint32]40)                      # biSize
            $bw.Write([int32]$sz)                      # biWidth
            $bw.Write([int32]($sz * 2))                # biHeight (XOR + AND mask)
            $bw.Write([uint16]1)                       # biPlanes
            $bw.Write([uint16]32)                      # biBitCount
            $bw.Write([uint32]0)                       # biCompression (BI_RGB)
            $bw.Write([uint32]($sz * $sz * 4))         # biSizeImage
            $bw.Write([int32]0)                        # biXPelsPerMeter
            $bw.Write([int32]0)                        # biYPelsPerMeter
            $bw.Write([uint32]0)                       # biClrUsed
            $bw.Write([uint32]0)                       # biClrImportant

            for ($y = $sz - 1; $y -ge 0; $y--) {
                for ($x = 0; $x -lt $sz; $x++) {
                    $px = $bmp.GetPixel($x, $y)
                    $bw.Write([byte]$px.B)
                    $bw.Write([byte]$px.G)
                    $bw.Write([byte]$px.R)
                    $bw.Write([byte]$px.A)
                }
            }

            $andRowBytes = [int][Math]::Ceiling($sz / 32.0) * 4
            $andTotal = $andRowBytes * $sz
            $bw.Write((New-Object byte[] $andTotal))

            $bytes = $ms.ToArray()
            $bw.Dispose()
            $ms.Dispose()
            $images += [PSCustomObject]@{ Size = $sz; IsPng = $false; Bytes = $bytes }
        }
        $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($iconPath)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([uint16]0)
    $w.Write([uint16]1)
    $w.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($img in $images) {
        $bW = if ($img.Size -eq 256) { [byte]0 } else { [byte]$img.Size }
        $bH = if ($img.Size -eq 256) { [byte]0 } else { [byte]$img.Size }
        $w.Write($bW)
        $w.Write($bH)
        $w.Write([byte]0)
        $w.Write([byte]0)
        $w.Write([uint16]1)
        $w.Write([uint16]32)
        $w.Write([uint32]$img.Bytes.Length)
        $w.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }

    foreach ($img in $images) {
        $w.Write($img.Bytes)
    }

    $w.Dispose()
    $fs.Dispose()
    Write-Host "Icon created successfully."
}

# 2. Stop running process
$running = Get-Process -Name "BrightnessTray" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running BrightnessTray process..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 3. Locate csc.exe
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (!(Test-Path $csc)) {
    throw "Could not locate csc.exe in .NET Framework directory."
}
Write-Host "Using compiler: $csc"

# 4. Compile
$srcFiles = Get-ChildItem (Join-Path $scriptDir "src\*.cs") | Select-Object -ExpandProperty FullName
if (!$srcFiles) {
    throw "No C# source files found in src directory."
}

$compileArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/win32manifest:$manifestPath",
    "/win32icon:$iconPath",
    "/out:$exePath",
    "/r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll"
) + $srcFiles

Write-Host "Compiling BrightnessTray.exe..."
& $csc $compileArgs

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$exeItem = Get-Item $exePath
Write-Host "Compilation succeeded: $($exeItem.FullName) ($([Math]::Round($exeItem.Length / 1KB, 1)) KB)" -ForegroundColor Green
