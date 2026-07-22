$SourcePng = "C:\Users\Wheezer\dev\coq_automap\JoppaWorld.11.22.1.1.10.png"
$TargetTileDir = "C:\Users\Wheezer\AppData\LocalLow\Freehold Games\CavesOfQud\Synced\Saves\c35c04eb-4479-438d-ac7c-69af35cbf273\Automap\tiles"

$World = "JoppaWorld"
$Z = 12

$CleanTargetFirst = $true

if (!(Test-Path -LiteralPath $SourcePng)) {
    throw "Source PNG not found: $SourcePng"
}

if ($SourcePng -match "\\thumb\.") {
    throw "SourcePng looks like a thumbnail. Use a full tile PNG, not a thumb.*.png file."
}

New-Item -ItemType Directory -Force -Path $TargetTileDir | Out-Null

# Copy seed outside the target first so CleanTargetFirst cannot delete it.
$SeedPng = Join-Path $env:TEMP "AtlasStressSeed.png"
Copy-Item -LiteralPath $SourcePng -Destination $SeedPng -Force

if ($CleanTargetFirst) {
    Write-Host "Cleaning target PNGs..."
    Get-ChildItem -LiteralPath $TargetTileDir -Filter "*.png" -File | Remove-Item -Force
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$count = 0
$total = 80 * 25 * 3 * 3

for ($px = 0; $px -lt 80; $px++) {
    for ($py = 0; $py -lt 25; $py++) {
        for ($zx = 0; $zx -lt 3; $zx++) {
            for ($zy = 0; $zy -lt 3; $zy++) {
                $fileName = "{0}.{1}.{2}.{3}.{4}.{5}.png" -f $World, $px, $py, $zx, $zy, $Z
                $dest = Join-Path $TargetTileDir $fileName

                [System.IO.File]::Copy($SeedPng, $dest, $true)

                $count++

                if (($count % 500) -eq 0) {
                    Write-Host "Created $count / $total..."
                }
            }
        }
    }
}

$sw.Stop()

$fullFiles = Get-ChildItem -LiteralPath $TargetTileDir -Filter "$World.*.*.*.*.$Z.png" -File |
    Where-Object { $_.Name -notlike "thumb.*" }

$totalBytes = ($fullFiles | Measure-Object Length -Sum).Sum

Write-Host ""
Write-Host "Done."
Write-Host "Created full tile files: $($fullFiles.Count)"
Write-Host ("Approx full tile disk size: {0:N2} MB" -f ($totalBytes / 1MB))
Write-Host ("Elapsed: {0:N1} seconds" -f $sw.Elapsed.TotalSeconds)
Write-Host ""
Write-Host "Next: load this test save, press Ctrl+M, and let Atlas generate thumbnails."