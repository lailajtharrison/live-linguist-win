# Live Linguist (French) — one-time setup on the target Windows PC.
# Run this from the unzipped bundle folder.
$ErrorActionPreference = "Stop"
$bundle = $PSScriptRoot
$appDir = Join-Path $bundle "LiveLinguist"
$modelName = "ll-fr-0.6b-v2-Q4_K_M.gguf"

# 1) Model directory
$modelDir = Join-Path $env:LOCALAPPDATA "LiveLinguist"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null
$dst = Join-Path $modelDir $modelName

# If the model was dropped into the bundle folder, install it; else prompt.
$srcCandidates = @((Join-Path $bundle $modelName), (Join-Path $appDir $modelName))
$src = $srcCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($src) {
    Copy-Item $src $dst -Force
    Write-Host "[ok] Model installed -> $dst"
} elseif (Test-Path $dst) {
    Write-Host "[ok] Model already present -> $dst"
} else {
    Write-Warning "[todo] Put '$modelName' (400 MB) in this folder and re-run setup.ps1, or copy it to $modelDir"
}

# 2) French speech pack check
$hasFr = $false
try { $hasFr = @(Get-WinUserLanguageList | Where-Object { $_.LanguageTag -like 'fr*' }).Count -gt 0 } catch {}
if (-not $hasFr) {
    Write-Warning "[todo] Add 'Francais (France)' speech: Settings > Time and language > Speech, and allow Microphone for desktop apps."
} else {
    Write-Host "[ok] A French language is installed."
}

Write-Host ""
Write-Host "When both [ok]: run  $appDir\LiveLinguistWinUI.exe  and speak French."
