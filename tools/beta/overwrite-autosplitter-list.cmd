<# :
@echo off
set "CELESTE_BETA_SELF=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Expression ([IO.File]::ReadAllText($env:CELESTE_BETA_SELF))"
echo.
pause
exit /b
#>

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $env:CELESTE_BETA_SELF
$list = Join-Path $dir 'LiveSplit.AutoSplitters.xml'
$components = Join-Path $dir 'Components'
$upstream = 'https://raw.githubusercontent.com/LiveSplit/LiveSplit.AutoSplitters/master/LiveSplit.AutoSplitters.xml'
$release = 'https://github.com/caeyo/celeste-autosplit/releases/latest/download/'
$marker = 'Autosplitting is available. (celeste-autosplit beta)'

if (-not (Test-Path -LiteralPath (Join-Path $dir 'LiveSplit.exe'))) {
    Write-Host 'Put this file in your LiveSplit folder, next to LiveSplit.exe, and run it again.'
    return
}
if (Get-Process -Name LiveSplit -ErrorAction SilentlyContinue) {
    Write-Host 'Close LiveSplit, then run this again.'
    return
}

$installed = (Test-Path -LiteralPath $list) -and (Select-String -LiteralPath $list -SimpleMatch $marker -Quiet)

if ($installed) {
    Write-Host 'The celeste-autosplit beta is installed.'
    $choice = Read-Host 'Type U and press Enter to uninstall it, or just press Enter to refresh the auto splitter list'
} else {
    Write-Host 'This points LiveSplit''s Celeste auto splitter at the celeste-autosplit beta.'
    Write-Host 'Splits it migrates are backed up next to them as [BACKUP]<name>.lss. The old auto splitter cannot read migrated splits.'
    $choice = Read-Host 'Press Enter to install it, or close this window to cancel'
}

if ($installed -and $choice.Trim() -eq 'U') {
    (Get-Item -LiteralPath $list).IsReadOnly = $false
    Remove-Item -LiteralPath $list
    foreach ($file in 'celeste-autosplit.dll', 'celeste_autosplit.wasm') {
        $path = Join-Path $components $file
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Write-Host 'Uninstalled. LiveSplit downloads the official list again when it next starts.'
    Write-Host 'To go back to splits from before the beta, restore the [BACKUP] copies.'
    return
}

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$xml = New-Object System.Xml.XmlDocument
$xml.Load($upstream)

$entry = $xml.SelectSingleNode("/AutoSplitters/AutoSplitter[Games/Game='Celeste']")
if ($entry -eq $null) { throw 'The official list has no Celeste entry.' }

foreach ($node in @($entry.ChildNodes)) {
    if ($node.Name -ne 'Games') { [void]$entry.RemoveChild($node) }
}

function Add-Element($parent, $name, $text) {
    $element = $xml.CreateElement($name)
    if ($text) { $element.InnerText = $text }
    [void]$parent.AppendChild($element)
    $element
}

$urls = Add-Element $entry 'URLs'
[void](Add-Element $urls 'URL' ($release + 'celeste-autosplit.dll'))
[void](Add-Element $urls 'URL' ($release + 'celeste_autosplit.wasm'))
[void](Add-Element $entry 'Type' 'Component')
[void](Add-Element $entry 'Description' $marker)
[void](Add-Element $entry 'Website' 'https://github.com/caeyo/celeste-autosplit')
$runtime = Add-Element $entry 'AutoSplittingRuntime'
[void](Add-Element $runtime 'URL' ($release + 'celeste_autosplit.wasm'))
[void](Add-Element $runtime 'Description' 'Autosplitting is available. Configure at the linked website.')
[void](Add-Element $runtime 'Website' 'https://caeyo.github.io/celeste-autosplit/')

if (Test-Path -LiteralPath $list) { (Get-Item -LiteralPath $list).IsReadOnly = $false }
$xml.Save($list)
(Get-Item -LiteralPath $list).IsReadOnly = $true

Write-Host 'Installed. Start LiveSplit, load Celeste splits, and activate the auto splitter in Edit Splits.'
Write-Host 'Run this again to refresh the list for other games, or to uninstall.'
