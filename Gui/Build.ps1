$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sourceFile = Join-Path $PSScriptRoot 'Updater.cs'
$outputFile = Join-Path $package 'WoW-Addon-Updater.exe'
$engineFile = Join-Path $package 'Update-WoWAddons.ps1'
$catalogFile = Join-Path $package 'addons.example.json'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$outputFile" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$engineFile,engine.ps1" "/resource:$catalogFile,catalog.json" $sourceFile (Join-Path $PSScriptRoot 'Extras.cs') (Join-Path $PSScriptRoot 'GameLauncher.cs') (Join-Path $PSScriptRoot 'VersionInfo.cs') (Join-Path $PSScriptRoot 'Localization.cs') (Join-Path $PSScriptRoot 'SelfUpdate.cs') "/resource:$(Join-Path $PSScriptRoot 'english.json'),english.json" (Join-Path $PSScriptRoot 'DarkMenuRenderer.cs') (Join-Path $PSScriptRoot 'DarkDialog.cs')
if ($LASTEXITCODE -ne 0) { throw 'EXE konnte nicht erstellt werden.' }
'EXE erstellt: ' + (Join-Path $package 'WoW-Addon-Updater.exe')
