[CmdletBinding()]
param(
    [string]$Config,
    [string]$WowPath,
    [switch]$DryRun,
    [switch]$NoBackup,
    [string]$CancelFile,
    [string]$SessionCacheDirectory,
    [string]$CacheDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WoWAddonUpdater\Downloads')
)

<#!
.SYNOPSIS
    Aktualisiert WoW-3.3.5a-Addons von GitHub, GitLab und direkten ZIP-Adressen.

.DESCRIPTION
    Neben Wow.exe ablegen und Start-Update.cmd starten. Die Konfiguration wird beim
    ersten Start aus addons.example.json erstellt. Downloads werden geprueft und
    ersetzte Addon-Ordner gesichert. -DryRun prueft ohne Installation.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([Console]::IsOutputRedirected) { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) }

function Write-Info([string]$Message) { Write-Host "INFO    | $Message" }
function Write-Ok([string]$Message) { Write-Host "OK      | $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "WARNUNG | $Message" -ForegroundColor Yellow }
function Write-Fail([string]$Message) { Write-Host "FEHLER  | $Message" -ForegroundColor Red }

function Test-Cancellation {
    $signal = Get-Variable -Name CancelFile -ValueOnly -ErrorAction SilentlyContinue
    if ($signal -and (Test-Path -LiteralPath $signal)) {
        throw [System.OperationCanceledException]::new('Vorgang abgebrochen.')
    }
}

function Get-WarperiaError($Reply) {
    $data = Get-PropertyValue $Reply 'data'
    $message = if ($data -is [string]) { $data } else { [string](Get-PropertyValue $data 'message' '') }
    if ($message -match '(?i)maximum download limit|download limit|too many|rate.limit') {
        return 'Warperia-Downloadlimit erreicht. Bitte spaeter erneut versuchen. Der Anbieter bestimmt die Wartezeit; es erfolgt kein automatischer Wiederholungsversuch.'
    }
    if ($message) { return ('Warperia: ' + [System.Net.WebUtility]::HtmlDecode(($message -replace '<[^>]+>', ''))) }
    return 'Warperia hat keine Downloadadresse geliefert. Bitte spaeter erneut versuchen.'
}

function Get-PropertyValue {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()]$Default = $null
    )

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Assert-AddonFolderName {
    param([Parameter(Mandatory)][string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -match "[\\/]" -or
        $Name -eq "." -or $Name -eq ".." -or $Name.Contains("..") -or
        $Name.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $Name -match '[. ]$|^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)') {
        throw "Ungueltiger Addon-Ordnername: '$Name'"
    }
}

function Test-TrustedUri {
    param(
        [Parameter(Mandatory)][uri]$Uri,
        [Parameter(Mandatory)][string[]]$TrustedHosts
    )

    if ($Uri.Scheme -ne "https") {
        throw "Nur HTTPS-Downloads sind erlaubt: $Uri"
    }

    if ($TrustedHosts.Count -gt 0 -and $TrustedHosts -notcontains $Uri.Host) {
        throw "Der Server '$($Uri.Host)' ist nicht in trustedHosts eingetragen. Bitte die Downloadquelle pruefen."
    }
}

function Get-GitHubArchiveUri {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Branch)

    $repositoryUri = [uri]$Repository
    if ($repositoryUri.Host -ne "github.com") {
        throw "Eine GitHub-Quelle muss https://github.com/owner/repository verwenden: $Repository"
    }

    $path = $repositoryUri.AbsolutePath.Trim("/") -replace "\.git$", ""
    $parts = @($path.Split("/", [System.StringSplitOptions]::RemoveEmptyEntries))
    if ($parts.Count -ne 2) {
        throw "GitHub-Benutzer und Repository konnten nicht erkannt werden: $Repository"
    }

    $encodedBranch = [uri]::EscapeDataString($Branch)
    return "https://codeload.github.com/$($parts[0])/$($parts[1])/zip/refs/heads/$encodedBranch"
}

function Get-GitLabArchiveUri {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Branch)

    $repositoryUri = [uri]$Repository
    if ($repositoryUri.Host -ne "gitlab.com") {
        throw "Eine GitLab-Quelle muss https://gitlab.com/group/project verwenden: $Repository"
    }

    $projectPath = $repositoryUri.AbsolutePath.Trim("/") -replace "\.git$", ""
    if ([string]::IsNullOrWhiteSpace($projectPath)) {
        throw "GitLab-Projekt konnte nicht erkannt werden: $Repository"
    }

    $encodedProject = [uri]::EscapeDataString($projectPath)
    $encodedBranch = [uri]::EscapeDataString($Branch)
    return "https://gitlab.com/api/v4/projects/$encodedProject/repository/archive.zip?sha=$encodedBranch"
}

function Get-SourceArchiveUri {
    param([Parameter(Mandatory)]$Source)

    $type = [string](Get-PropertyValue $Source "type")
    switch ($type.ToLowerInvariant()) {
        "github" {
            return Get-GitHubArchiveUri -Repository ([string](Get-PropertyValue $Source "repository")) -Branch ([string](Get-PropertyValue $Source "branch"))
        }
        "gitlab" {
            return Get-GitLabArchiveUri -Repository ([string](Get-PropertyValue $Source "repository")) -Branch ([string](Get-PropertyValue $Source "branch"))
        }
        "warperia" {
            $page = [uri]([string](Get-PropertyValue $Source "url"))
            if ($page.Scheme -ne 'https' -or $page.Host -ne 'warperia.com' -or
                $page.AbsolutePath -notmatch '^/addon-wotlk/[a-z0-9-]+/download/$' -or $page.Query) {
                throw 'Warperia benoetigt eine HTTPS-Downloadseite unter /addon-wotlk/.'
            }
            return $page.AbsoluteUri
        }
        "zip" {
            $url = [string](Get-PropertyValue $Source "url")
            if ([string]::IsNullOrWhiteSpace($url)) { throw "Eine ZIP-Quelle benoetigt das Feld url." }
            return $url
        }
        default { throw "Unbekannter Quellentyp '$type'. Erlaubt sind github, gitlab, zip und warperia." }
    }
}

function Download-File {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string[]]$TrustedHosts
    )

    $currentUri = [uri]$Uri
    for ($redirectCount = 0; $redirectCount -le 5; $redirectCount++) {
        Test-Cancellation
        Test-TrustedUri -Uri $currentUri -TrustedHosts $TrustedHosts
        $request = [System.Net.HttpWebRequest]::Create($currentUri)
        $request.Method = "GET"
        $request.AllowAutoRedirect = $false
        $request.UserAgent = "WoW-Addon-Updater/1.0"
        $request.Timeout = 60000
        $request.ReadWriteTimeout = 60000

        try {
            $response = [System.Net.HttpWebResponse]$request.GetResponse()
        }
        catch {
            throw "Download fehlgeschlagen fuer ${currentUri}: $($_.Exception.Message)"
        }

        try {
            $statusCode = [int]$response.StatusCode
            if ($statusCode -ge 300 -and $statusCode -lt 400) {
                $location = $response.Headers["Location"]
                if ([string]::IsNullOrWhiteSpace($location)) {
                    throw "Weiterleitung ohne Zieladresse von $currentUri"
                }
                $currentUri = New-Object System.Uri($currentUri, $location)
                continue
            }

            if ($statusCode -lt 200 -or $statusCode -ge 300) {
                throw "Server meldet HTTP $statusCode fuer $currentUri"
            }

            $stream = $response.GetResponseStream()
            $fileStream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
            try {
                $buffer = New-Object byte[] 65536
                while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    Test-Cancellation
                    $fileStream.Write($buffer, 0, $count)
                }
            }
            finally {
                $fileStream.Dispose()
                $stream.Dispose()
            }
            return
        }
        finally {
            $response.Dispose()
        }
    }

    throw "Zu viele Weiterleitungen beim Download von $Uri"
}

function Download-Warperia {
    param([string]$PageUri, [string]$Destination, [string[]]$TrustedHosts)

    $pageFile = $Destination + '.html'
    Download-File -Uri $PageUri -Destination $pageFile -TrustedHosts $TrustedHosts
    $page = Get-Content -LiteralPath $pageFile -Raw -Encoding UTF8
    $addonId = [regex]::Match($page, 'data-addon-id="(\d+)"').Groups[1].Value
    $nonce = [regex]::Match($page, '"download_nonce":"([^"]+)"').Groups[1].Value
    if (-not $addonId -or -not $nonce) {
        throw 'Warperia-Downloadseite wurde geaendert oder ist nicht frei abrufbar.'
    }
    $countdown = [regex]::Match($page, 'data-countdown-seconds="(\d+)"')
    $seconds = if ($countdown.Success) { [int]$countdown.Groups[1].Value } else { 5 }
    if ($seconds -lt 0 -or $seconds -gt 60) { throw 'Unbekannte Warperia-Wartezeit.' }
    Write-Info "Warperia: Warte $seconds Sekunden auf den Download."
    for ($tick = 0; $tick -lt ($seconds * 4); $tick++) { Test-Cancellation; Start-Sleep -Milliseconds 250 }
    Test-Cancellation
    $endpoint = [uri]'https://warperia.com/wp-admin/admin-ajax.php'
    Test-TrustedUri -Uri $endpoint -TrustedHosts $TrustedHosts
    $request = [System.Net.HttpWebRequest]::Create($endpoint)
    $request.Method = 'POST'
    $request.AllowAutoRedirect = $false
    $request.UserAgent = 'WoW-Addon-Updater/1.0'
    $request.Timeout = 60000
    $request.ReadWriteTimeout = 60000
    $request.ContentType = 'application/x-www-form-urlencoded'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(('action=increment_addon_download&addon_id=' + $addonId + '&nonce=' + [uri]::EscapeDataString($nonce)))
    $request.ContentLength = $bytes.Length
    $stream = $request.GetRequestStream()
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    $response = $request.GetResponse()
    try {
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        try { $reply = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } finally { $response.Dispose() }
    $data = Get-PropertyValue $reply 'data'
    $downloadUrl = [string](Get-PropertyValue $data 'download_url')
    if ((Get-PropertyValue $reply 'success' $false) -ne $true -or -not $downloadUrl) {
        throw (Get-WarperiaError $reply)
    }
    # Die kurzzeitig gueltige Adresse wird mit derselben Programmkennung genutzt.
    Download-File -Uri $downloadUrl -Destination $Destination -TrustedHosts $TrustedHosts
}

function Test-ArchiveHash {
    param([Parameter(Mandatory)][string]$ArchivePath, [AllowNull()][string]$ExpectedHash)

    if ([string]::IsNullOrWhiteSpace($ExpectedHash)) { return }
    $actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ine $ExpectedHash.Replace(" ", "")) {
        throw "SHA-256 stimmt nicht ueberein. Erwartet: $ExpectedHash, erhalten: $actualHash."
    }
}

function Expand-ZipSafely {
    param([Parameter(Mandatory)][string]$ArchivePath, [Parameter(Mandatory)][string]$Destination)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace('\', '/')
            $extractionRoot = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
            $resolvedEntry = [System.IO.Path]::GetFullPath((Join-Path $Destination $entryName))
            if ($entryName.StartsWith("/") -or $entryName.Contains(':') -or
                $entryName -match "(^|/)\.\.(/|$)" -or
                -not $resolvedEntry.StartsWith($extractionRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Das Archiv enthaelt einen unzulaessigen Pfad: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    [System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $Destination)
}

function Get-PackageRoot {
    param([Parameter(Mandatory)][string]$ExtractPath)

    $allItems = @(Get-ChildItem -LiteralPath $ExtractPath -Force)
    $directories = @($allItems | Where-Object { $_.PSIsContainer })
    $files = @($allItems | Where-Object { -not $_.PSIsContainer })
    if ($directories.Count -eq 1 -and $files.Count -eq 0) {
        return $directories[0].FullName
    }
    return $ExtractPath
}

function Get-TocFolders {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $tocFiles = @(Get-ChildItem -LiteralPath $PackageRoot -Filter "*.toc" -File -Recurse -Force)
    if ($tocFiles.Count -eq 0) {
        throw "Im Archiv wurde keine .toc-Datei gefunden. Es wurde kein WoW-Addon erkannt."
    }

    $result = @()
    foreach ($group in ($tocFiles | Group-Object DirectoryName)) {
        $result += [pscustomobject]@{
            Path = $group.Name
            Tocs = @($group.Group)
        }
    }
    # Eingebettete Bibliotheken bleiben Bestandteil ihres Addons.
    # Nur die aeussersten Ordner mit TOC werden separat installiert.
    return @($result | Where-Object {
        $candidate = $_.Path
        $ancestors = @($result | Where-Object {
            $candidate.StartsWith($_.Path.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)
        })
        $ancestors.Count -eq 0
    })
}

function Find-TocFolder {
    param(
        [Parameter(Mandatory)]$TocFolders,
        [Parameter(Mandatory)][string]$TocName
    )

    $matches = @()
    foreach ($folder in $TocFolders) {
        foreach ($toc in $folder.Tocs) {
            if ($toc.BaseName -ieq $TocName) { $matches += $folder }
        }
    }
    $matches = @($matches | Sort-Object Path -Unique)
    if ($matches.Count -eq 0) {
        throw "'$TocName.toc' fehlt im Archiv. Bitte den toc-Eintrag pruefen."
    }
    if ($matches.Count -gt 1) {
        throw "'$TocName.toc' wurde mehrfach gefunden. Das Paket ist nicht eindeutig."
    }
    return $matches[0]
}

function Resolve-Components {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)]$AddonSpecification
    )

    $tocFolders = @(Get-TocFolders -PackageRoot $PackageRoot)
    $components = @()
    if ($AddonSpecification -is [string] -and $AddonSpecification -ieq "auto") {
        foreach ($folder in $tocFolders) {
            $target = Split-Path -Leaf $folder.Path
            if (-not (Test-Path -LiteralPath (Join-Path $folder.Path "$target.toc") -PathType Leaf)) {
                if ($folder.Tocs.Count -ne 1) {
                    throw "Addon-Ordner '$target' ist nicht eindeutig. Bitte target und toc explizit festlegen."
                }
                $target = $folder.Tocs[0].BaseName
            }
            Assert-AddonFolderName $target
            $components += [pscustomobject]@{ Source = $folder.Path; Target = $target }
        }
    }
    else { foreach ($item in @($AddonSpecification)) {
        if ($item -is [string]) {
            $target = $item
            $tocName = $item
        }
        else {
            $target = [string](Get-PropertyValue $item "target")
            $tocName = [string](Get-PropertyValue $item "toc" $target)
        }
        Assert-AddonFolderName $target
        $folder = Find-TocFolder -TocFolders $tocFolders -TocName $tocName
        if (-not (Test-Path -LiteralPath (Join-Path $folder.Path "$target.toc") -PathType Leaf)) {
            throw "Zielordner '$target' benoetigt '$target.toc' direkt im Ordner. Bitte die Zuordnung pruefen."
        }
        $components += [pscustomobject]@{ Source = $folder.Path; Target = $target }
    } }

    $duplicateTargets = @($components | Group-Object Target | Where-Object { $_.Count -gt 1 })
    if ($duplicateTargets.Count -gt 0) {
        throw "Ein Zielordner wurde mehrfach ausgewaehlt: $($duplicateTargets.Name -join ', ')"
    }
    return $components
}

function Copy-DirectoryContents {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $child.FullName -Destination $Destination -Recurse -Force
    }
}

function Get-DirectoryDigest {
    param([Parameter(Mandatory)][string]$Path)

    $baseLength = $Path.TrimEnd("\\").Length + 1
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force | Sort-Object FullName)) {
        $relativePath = $file.FullName.Substring($baseLength).Replace("\\", "/")
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $lines.Add("$relativePath`t$fileHash")
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

function Install-Components {
    param(
        [Parameter(Mandatory)]$Components,
        [Parameter(Mandatory)][string]$AddonsRoot,
        [Parameter(Mandatory)][string]$BackupRoot,
        [switch]$DryRun,
        [switch]$NoBackup
    )

    $stageParent = if ($DryRun) { [System.IO.Path]::GetTempPath() } else { Split-Path -Parent $AddonsRoot }
    $stageRoot = Join-Path $stageParent (".wow-addon-updater-stage-" + [guid]::NewGuid().ToString("N"))
    $toInstall = @()
    try {
        foreach ($component in $Components) {
            Test-Cancellation
            $stageTarget = Join-Path $stageRoot $component.Target
            Copy-DirectoryContents -Source $component.Source -Destination $stageTarget
            $sourceDigest = Get-DirectoryDigest -Path $stageTarget
            $destination = Join-Path $AddonsRoot $component.Target
            $destinationDigest = if (Test-Path -LiteralPath $destination) { Get-DirectoryDigest -Path $destination } else { $null }
            if ($sourceDigest -eq $destinationDigest) {
                Write-Info "$($component.Target) ist bereits aktuell"
            }
            else {
                $toInstall += [pscustomobject]@{ Target = $component.Target; Stage = $stageTarget; Destination = $destination }
            }
        }

        if ($toInstall.Count -eq 0) { return }
        if ($DryRun) {
            foreach ($component in $toInstall) { Write-Info "[Prueflauf] Wuerde installieren: $($component.Target)" }
            return
        }

        Test-Cancellation
        # Ab hier wird das aktuelle Paket vollstaendig installiert oder zurueckgerollt.
        $backupRun = $null
        if (-not $NoBackup) {
            $backupRun = Join-Path $BackupRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")
            New-Item -ItemType Directory -Path $backupRun -Force | Out-Null
        }

        $movedBackups = @()
        $installedTargets = @()
        try {
            foreach ($component in $toInstall) {
                if (Test-Path -LiteralPath $component.Destination) {
                    if ($NoBackup) {
                        Remove-Item -LiteralPath $component.Destination -Recurse -Force
                    }
                    else {
                        Move-Item -LiteralPath $component.Destination -Destination $backupRun
                        $movedBackups += $component.Target
                    }
                }
                Move-Item -LiteralPath $component.Stage -Destination $AddonsRoot
                $installedTargets += $component.Target
                Write-Ok "Installiert: $($component.Target)"
            }
        }
        catch {
            foreach ($target in $installedTargets) {
                $installedPath = Join-Path $AddonsRoot $target
                if (Test-Path -LiteralPath $installedPath) {
                    Remove-Item -LiteralPath $installedPath -Recurse -Force
                }
            }
            if (-not $NoBackup) {
                foreach ($target in $movedBackups) {
                    $backupPath = Join-Path $backupRun $target
                    $destinationPath = Join-Path $AddonsRoot $target
                    if ((Test-Path -LiteralPath $backupPath) -and -not (Test-Path -LiteralPath $destinationPath)) {
                        Move-Item -LiteralPath $backupPath -Destination $AddonsRoot
                    }
                }
            }
            throw
        }
    }
    finally {
        if (Test-Path -LiteralPath $stageRoot) {
            Remove-Item -LiteralPath $stageRoot -Recurse -Force
        }
    }
}

function Update-Source {
    param(
        [Parameter(Mandatory)]$Source,
        [Parameter(Mandatory)][string]$AddonsRoot,
        [Parameter(Mandatory)][string]$BackupRoot,
        [Parameter(Mandatory)][string[]]$TrustedHosts,
        [switch]$DryRun,
        [switch]$NoBackup
    )

    $name = [string](Get-PropertyValue $Source "name" "Unbenannte Quelle")
    Test-Cancellation
    $archiveUri = Get-SourceArchiveUri -Source $Source
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wow-addon-updater-" + [guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        $archivePath = Join-Path $tempRoot "download.zip"
        Write-Info "Lade $name herunter"
        $isWarperia = (Get-PropertyValue $Source 'type') -eq 'warperia'
        $cacheRoot = Get-Variable -Name SessionCacheDirectory -ValueOnly -ErrorAction SilentlyContinue
        if ($isWarperia) {
            $cacheRoot = Get-Variable -Name CacheDirectory -ValueOnly -ErrorAction SilentlyContinue
            if (-not $cacheRoot) { $cacheRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WoWAddonUpdater\Downloads' }
        }
        if ($cacheRoot) {
            New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
            $hasher = [System.Security.Cryptography.SHA256]::Create()
            try { $key = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($archiveUri)))).Replace('-', '') }
            finally { $hasher.Dispose() }
            $cachedZip = Join-Path $cacheRoot ($key + '.zip')
            $cachedHash = $cachedZip + '.sha256'
            $useCache = $false
            if ((Test-Path -LiteralPath $cachedZip) -and (Test-Path -LiteralPath $cachedHash)) {
                $age = [DateTime]::UtcNow - (Get-Item -LiteralPath $cachedZip).LastWriteTimeUtc
                $useCache = $age.TotalHours -ge 0 -and $age.TotalHours -lt 24 -and
                    ((Get-FileHash -LiteralPath $cachedZip -Algorithm SHA256).Hash -eq (Get-Content -LiteralPath $cachedHash -Raw).Trim())
            }
            if ($useCache) {
                Copy-Item -LiteralPath $cachedZip -Destination $archivePath
                Write-Info "CACHE | Paket vom $((Get-Item -LiteralPath $cachedZip).LastWriteTime.ToString('dd.MM.yyyy HH:mm')); kein erneuter Download."
            }
            elseif ($isWarperia) { Download-Warperia -PageUri $archiveUri -Destination $archivePath -TrustedHosts $TrustedHosts }
            else { Download-File -Uri $archiveUri -Destination $archivePath -TrustedHosts $TrustedHosts }
        }
        else {
            Download-File -Uri $archiveUri -Destination $archivePath -TrustedHosts $TrustedHosts
        }
        Test-ArchiveHash -ArchivePath $archivePath -ExpectedHash ([string](Get-PropertyValue $Source "sha256"))
        $extractPath = Join-Path $tempRoot "extract"
        New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
        Expand-ZipSafely -ArchivePath $archivePath -Destination $extractPath
        $packageRoot = Get-PackageRoot -ExtractPath $extractPath
        $addons = Get-PropertyValue $Source "addons" "auto"
        $components = @(Resolve-Components -PackageRoot $packageRoot -AddonSpecification $addons)
        $versions = @($components | ForEach-Object {
            $version = 'Nicht angegeben'
            $tocPath = Join-Path $_.Source ($_.Target + '.toc')
            if (Test-Path -LiteralPath $tocPath) {
                $match = [regex]::Match((Get-Content -LiteralPath $tocPath -Raw), '(?im)^##\s*Version\s*:\s*([^\r\n]+)')
                if ($match.Success -and $match.Groups[1].Value -notmatch '@|\$') { $version = $match.Groups[1].Value.Trim() }
            }
            @{folder=$_.Target; version=$version}
        })
        $versionJson = ConvertTo-Json -InputObject $versions -Compress
        Write-Host ('VERSION | ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($versionJson)))
        Test-Cancellation
        if ($cacheRoot -and -not $useCache) {
            Copy-Item -LiteralPath $archivePath -Destination ($cachedZip + '.partial') -Force
            Move-Item -LiteralPath ($cachedZip + '.partial') -Destination $cachedZip -Force
            [System.IO.File]::WriteAllText($cachedHash, (Get-FileHash -LiteralPath $cachedZip -Algorithm SHA256).Hash)
        }
        Install-Components -Components $components -AddonsRoot $AddonsRoot -BackupRoot $BackupRoot -DryRun:$DryRun -NoBackup:$NoBackup
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($Config)) {
        $Config = Join-Path $PSScriptRoot "addons.json"
    }
    if (-not (Test-Path -LiteralPath $Config -PathType Leaf)) {
        $defaultConfig = Join-Path $PSScriptRoot "addons.json"
        $exampleConfig = Join-Path $PSScriptRoot "addons.example.json"
        if ([System.IO.Path]::GetFullPath($Config) -eq $defaultConfig -and
            (Test-Path -LiteralPath $exampleConfig -PathType Leaf)) {
            Copy-Item -LiteralPath $exampleConfig -Destination $Config
            Write-Info "addons.json wurde aus der Vorlage erstellt."
        }
        else { throw "Konfigurationsdatei nicht gefunden: $Config" }
    }
    $configuration = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
    $trustedHosts = @([string[]](Get-PropertyValue $configuration "trustedHosts" @()))
    if ($trustedHosts.Count -eq 0) {
        throw "trustedHosts muss mindestens einen Server enthalten."
    }

    # Standard: Wow.exe liegt neben dem Skript. Alte Konfigurationspfade
    # werden nicht mehr ausgewertet. -WowPath bleibt ein optionaler Expertenparameter.
    if ([string]::IsNullOrWhiteSpace($WowPath)) {
        $WowPath = Join-Path $PSScriptRoot "Wow.exe"
    }
    $WowPath = [System.IO.Path]::GetFullPath($WowPath)
    if (-not (Test-Path -LiteralPath $WowPath -PathType Leaf)) {
        throw "Wow.exe wurde nicht gefunden: $WowPath. Bitte die Updater-Dateien direkt neben Wow.exe ablegen."
    }

    $addonsRoot = Join-Path (Split-Path -Parent $WowPath) "Interface\AddOns"
    if (-not (Test-Path -LiteralPath $addonsRoot)) {
        if ($DryRun) { Write-Info "[Prueflauf] Wuerde Ordner erstellen: $addonsRoot" }
        else { New-Item -ItemType Directory -Path $addonsRoot -Force | Out-Null }
    }
    $backupRoot = Join-Path (Split-Path -Parent $addonsRoot) "AddOns_UpdaterBackups"
    $sources = @(Get-PropertyValue $configuration "sources" @())
    if ($sources.Count -eq 0) { throw "Es sind keine Downloadquellen konfiguriert." }

    Write-Info "Verwendeter Addon-Ordner: $addonsRoot"
    if ($DryRun) { Write-Warn "Prueflauf: Downloads und Kontrolle. Die installierten Addons werden nicht veraendert." }

    $failures = @()
    foreach ($source in $sources) {
        $enabled = Get-PropertyValue $source "enabled" $true
        if ($enabled -eq $false) { continue }
        try {
            Update-Source -Source $source -AddonsRoot $addonsRoot -BackupRoot $backupRoot -TrustedHosts $trustedHosts -DryRun:$DryRun -NoBackup:$NoBackup
        }
        catch {
            if ($_.Exception -is [System.OperationCanceledException]) {
                Write-Warn 'Vorgang abgebrochen. Noch nicht gestartete Pakete bleiben unveraendert.'
                exit 2
            }
            $sourceName = [string](Get-PropertyValue $source "name" "Unbenannte Quelle")
            Write-Fail "${sourceName}: $($_.Exception.Message)"
            $failures += $sourceName
        }
    }

    if ($failures.Count -gt 0) {
        Write-Fail "Mit Fehlern beendet. Betroffene Quellen: $($failures -join ', ')"
        exit 1
    }
    Write-Ok "Alle aktivierten Quellen wurden erfolgreich geprueft."
}
catch {
    Write-Fail $_.Exception.Message
    exit 1
}
