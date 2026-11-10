#!/usr/bin/env pwsh
# One-click MSI packager for yopet
$ErrorActionPreference = "Stop"

# ── Config ──
$Version       = "1.0.0"
$Configuration = "Release"
$Runtime       = "win-x64"
$UpgradeCode   = "649E83B3-1177-464F-9AAE-396C8EC3EE66"
$AppName       = "yopet"
$Manufacturer  = "Zhuoyue(9486983)"
$ExeName       = "yopet.exe"
$ExeDesc      = "desktop pet companion"

$ScriptDir     = Split-Path -Parent $PSCommandPath
$ProjectRoot   = Split-Path -Parent $ScriptDir

function Resolve-Dotnet10 {
    $candidates = @()
    if ($env:YOPET_DOTNET) { $candidates += $env:YOPET_DOTNET }
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT "dotnet.exe") }
    if ($env:USERPROFILE) { $candidates += (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe") }
    $candidates += "C:\Program Files\dotnet\dotnet.exe"
    $pathDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($pathDotnet) { $candidates += $pathDotnet.Source }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $sdks = & $candidate --list-sdks 2>$null
        if ($sdks -match '^\s*10\.') { return $candidate }
    }

    throw "No .NET 10 SDK found. Install .NET 10 SDK or set YOPET_DOTNET to its dotnet.exe path."
}

function Get-RelativePath([string]$BasePath, [string]$FullPath) {
    $trimChars = [char[]]@('\', '/')
    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd($trimChars)
    $full = [System.IO.Path]::GetFullPath($FullPath)
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length).TrimStart($trimChars)
    }
    throw "Path '$FullPath' is not under '$BasePath'."
}

$DotnetExe = Resolve-Dotnet10
$env:DOTNET_ROOT = Split-Path -Parent $DotnetExe
$env:PATH = "$(Split-Path -Parent $DotnetExe);$env:PATH"
Write-Host "Using .NET SDK: $(& $DotnetExe --version)"
$PublishDir    = Join-Path $ProjectRoot "yopet\bin\$Configuration\net10.0\$Runtime\publish"
$PluginsDir    = Join-Path $PublishDir "plugins"
$WxsFile       = Join-Path $env:TEMP "pet-yue-build.wxs"
$OutputMsi     = Join-Path $ScriptDir "yopet-$Version.msi"

# ── 1. Ensure WiX installed ──
if (-not (Get-Command wix.exe -ErrorAction SilentlyContinue)) {
    Write-Host "[1/5] Installing WiX Toolset..."
    & $DotnetExe tool install --global wix --version 5.*
}

# ── 2. Publish main project ──
Write-Host "[2/5] Publishing main project..."
& $DotnetExe publish (Join-Path $ProjectRoot "yopet\yopet.csproj") `
    --configuration $Configuration --runtime $Runtime --self-contained false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# ���� 3. Build plugins and copy to publish/plugins/ ����
Write-Host "[3/5] Building plugins..."
if (Test-Path $PluginsDir) { Remove-Item $PluginsDir -Recurse -Force }
New-Item -ItemType Directory -Path $PluginsDir | Out-Null

$rootFileHashes = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath $PublishDir -Recurse -File | ForEach-Object {
    if (-not $rootFileHashes.ContainsKey($_.Name)) {
        $rootFileHashes[$_.Name] = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
    }
    [void]$rootFileHashes[$_.Name].Add(
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)
}

$projFiles = Get-ChildItem -Path (Join-Path $ProjectRoot "plugins") -Recurse -Filter "*.csproj"
$skippedDuplicateNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($proj in $projFiles) {
    $pluginName = $proj.BaseName
    Write-Host "  Building $pluginName..."
    & $DotnetExe build $proj.FullName --configuration $Configuration 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Plugin $pluginName build failed" }

    $binDir = Join-Path $proj.Directory.FullName "bin\$Configuration\net10.0"
    $entryDll = Join-Path $binDir "$pluginName.dll"
    if (-not (Test-Path -LiteralPath $entryDll)) {
        throw "Plugin entry assembly not found: $entryDll"
    }

    $stagingDir = Join-Path $PluginsDir $pluginName
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    Copy-Item -LiteralPath $entryDll -Destination $stagingDir -Force

    $entryDeps = Join-Path $binDir "$pluginName.deps.json"
    if (-not (Test-Path -LiteralPath $entryDeps)) {
        throw "Plugin dependency manifest not found: $entryDeps"
    }
    Copy-Item -LiteralPath $entryDeps -Destination $stagingDir -Force

    foreach ($f in Get-ChildItem -LiteralPath $binDir -Recurse -File) {
        if ($f.Extension -eq ".pdb") { continue }

        $relativePath = Get-RelativePath $binDir $f.FullName
        if ($relativePath.Equals("$pluginName.dll", [System.StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Equals("$pluginName.deps.json", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($relativePath.StartsWith("runtimes\", [System.StringComparison]::OrdinalIgnoreCase)) {
            $runtimeParts = $relativePath.Split('\')
            if ($runtimeParts.Length -gt 1 -and
                $runtimeParts[1] -notin @("win", $Runtime)) {
                continue
            }
        }

        if ($rootFileHashes.ContainsKey($f.Name)) {
            $pluginHash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
            if (-not $rootFileHashes[$f.Name].Contains($pluginHash)) {
                throw "Plugin dependency '$relativePath' differs from the host copy. Use a shared host version or isolate the dependency."
            }
            [void]$skippedDuplicateNames.Add($f.Name)
            continue
        }

        $destination = Join-Path $stagingDir $relativePath
        $destinationDir = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $destination -Force
    }
}

$pluginCount = (Get-ChildItem -LiteralPath $PluginsDir -Directory).Count
$pluginDllCount = (Get-ChildItem -LiteralPath $PluginsDir -Recurse -File -Filter "*.dll").Count
Write-Host "  $pluginCount plugin folder(s), $pluginDllCount dll(s) staged; $($skippedDuplicateNames.Count) host duplicate name(s) skipped"

$missingEntries = Get-ChildItem -LiteralPath $PluginsDir -Directory | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $_.FullName "$($_.Name).dll"))
}
if ($missingEntries) {
    throw "Plugin entries missing: $($missingEntries.Name -join ', ')"
}

# ── 4. Generate .wxs ──
Write-Host "[4/5] Generating WiX source file..."

$sb = New-Object System.Text.StringBuilder
$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>') | Out-Null
$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"') | Out-Null
$sb.AppendLine('     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">') | Out-Null
$sb.AppendLine("  <Package Name=`"$( $AppName )`" Language=`"2052`" Version=`"$Version`" Manufacturer=`"$( $Manufacturer )`"") | Out-Null
$sb.AppendLine("           UpgradeCode=`"$UpgradeCode`" InstallerVersion=`"200`" Scope=`"perUser`" Codepage=`"65001`">") | Out-Null
$sb.AppendLine('    <MajorUpgrade DowngradeErrorMessage="!(loc.DowngradeMsg)" />') | Out-Null
$sb.AppendLine('    <MediaTemplate CompressionLevel="high" EmbedCab="yes" />') | Out-Null
$sb.AppendLine('    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />') | Out-Null
$sb.AppendLine('') | Out-Null
$sb.AppendLine("    <StandardDirectory Id=`"LocalAppDataFolder`">") | Out-Null
$sb.AppendLine("      <Directory Id=`"PublisherFolder`" Name=`"$( $Manufacturer )`">") | Out-Null
$sb.AppendLine("        <Directory Id=`"INSTALLFOLDER`" Name=`"$( $AppName )`">") | Out-Null

$compRefs = New-Object System.Collections.Generic.List[string]
$wixDirectoryIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

function ConvertTo-WixAttribute([string]$value) {
    return [System.Security.SecurityElement]::Escape($value)
}

function New-WixDirectoryId([string]$value) {
    $base = "d_" + ($value -replace '[^a-zA-Z0-9_]', '_')
    $id = $base
    $suffix = 0
    while (-not $wixDirectoryIds.Add($id)) {
        $suffix++
        $id = "${base}_$suffix"
    }
    return $id
}

function New-WixComponentId([string]$value, $used) {
    $base = "p_" + ($value -replace '[^a-zA-Z0-9_]', '_')
    $id = $base
    $suffix = 0
    while ($used -contains $id) {
        $suffix++
        $id = "${base}_$suffix"
    }
    $used.Add($id)
    return $id
}

function Add-WixPluginDirectory {
    param(
        [System.Text.StringBuilder]$Builder,
        [System.IO.DirectoryInfo]$Directory,
        [string]$PluginName,
        [string]$RelativePath
    )

    foreach ($subDirectory in Get-ChildItem -LiteralPath $Directory.FullName -Directory | Sort-Object Name) {
        $subRelativePath = if ($RelativePath) {
            Join-Path $RelativePath $subDirectory.Name
        } else {
            $subDirectory.Name
        }
        $directoryId = New-WixDirectoryId "${PluginName}_$subRelativePath"
        $Builder.AppendLine("            <Directory Id=`"$directoryId`" Name=`"$(ConvertTo-WixAttribute $subDirectory.Name)`">") | Out-Null
        Add-WixPluginDirectory -Builder $Builder -Directory $subDirectory -PluginName $PluginName -RelativePath $subRelativePath
        $Builder.AppendLine('            </Directory>') | Out-Null
    }

    foreach ($file in Get-ChildItem -LiteralPath $Directory.FullName -File | Sort-Object Name) {
        $fileRelativePath = if ($RelativePath) {
            Join-Path $RelativePath $file.Name
        } else {
            $file.Name
        }
        $componentId = New-WixComponentId "${PluginName}_$fileRelativePath" $compRefs
        $sourcePath = "!(bindpath.plugins)\$PluginName\$fileRelativePath"
        $Builder.AppendLine("            <Component Id=`"$componentId`" Guid=`"*`">") | Out-Null
        $Builder.AppendLine("              <File Source=`"$(ConvertTo-WixAttribute $sourcePath)`" />") | Out-Null
        $Builder.AppendLine('            </Component>') | Out-Null
    }
}

# Main files
$pubFiles = Get-ChildItem -File $PublishDir | Where-Object { $_.Extension -notin '.pdb' }
foreach ($file in $pubFiles) {
    $baseId = "f_" + [System.IO.Path]::GetFileNameWithoutExtension($file.Name) -replace '[^a-zA-Z0-9_]', '_'
    $id = $baseId; $suffix = 0
    while ($compRefs -contains $id) { $suffix++; $id = "${baseId}_$suffix" }
    $compRefs.Add($id)
    $sb.AppendLine("          <Component Id=`"$id`" Guid=`"*`">") | Out-Null
    $sb.AppendLine("            <File Source=`"!(bindpath.pub)\$($file.Name)`" />") | Out-Null
    $sb.AppendLine("          </Component>") | Out-Null
}

# plugins/ subdirectories
$sb.AppendLine('          <Directory Id="pluginsDir" Name="plugins">') | Out-Null
foreach ($pluginDirectory in Get-ChildItem -LiteralPath $PluginsDir -Directory | Sort-Object Name) {
    $pluginDirectoryId = New-WixDirectoryId $pluginDirectory.Name
    $sb.AppendLine("            <Directory Id=`"$pluginDirectoryId`" Name=`"$(ConvertTo-WixAttribute $pluginDirectory.Name)`">") | Out-Null
    Add-WixPluginDirectory -Builder $sb -Directory $pluginDirectory -PluginName $pluginDirectory.Name -RelativePath ""
    $sb.AppendLine('            </Directory>') | Out-Null
}
$sb.AppendLine('          </Directory>') | Out-Null

# I18n localization JSON files subdirectory
$i18nPublishDir = Join-Path $PublishDir "I18n"
if (Test-Path $i18nPublishDir) {
    $sb.AppendLine('          <Directory Id="i18nDir" Name="I18n">') | Out-Null
    foreach ($file in Get-ChildItem -File $i18nPublishDir -Filter "*.json") {
        $baseId = "i18n_" + [System.IO.Path]::GetFileNameWithoutExtension($file.Name) -replace '[^a-zA-Z0-9_]', '_'
        $id = $baseId; $suffix = 0
        while ($compRefs -contains $id) { $suffix++; $id = "${baseId}_$suffix" }
        $compRefs.Add($id)
        $sb.AppendLine("            <Component Id=`"$id`" Guid=`"*`">") | Out-Null
        $sb.AppendLine("              <File Source=`"!(bindpath.i18n)\$($file.Name)`" />") | Out-Null
        $sb.AppendLine("            </Component>") | Out-Null
    }
    $sb.AppendLine('          </Directory>') | Out-Null
}

$sb.AppendLine('        </Directory>') | Out-Null
$sb.AppendLine('      </Directory>') | Out-Null
$sb.AppendLine('    </StandardDirectory>') | Out-Null
$sb.AppendLine('') | Out-Null

# Start menu shortcut
$sb.AppendLine('    <StandardDirectory Id="ProgramMenuFolder">') | Out-Null
$sb.AppendLine("      <Directory Id=`"AppShortcutFolder`" Name=`"$( $AppName )`">") | Out-Null
$sb.AppendLine('        <Component Id="ShortcutComponent" Guid="*">') | Out-Null
$sb.AppendLine("          <Shortcut Id=`"PetShortcut`" Name=`"$( $AppName )`" Description=`"$( $ExeDesc )`"") | Out-Null
$sb.AppendLine("                    Target=`"[INSTALLFOLDER]$ExeName`" />") | Out-Null
$sb.AppendLine('          <RemoveFolder Id="RemoveShortcutFolder" Directory="AppShortcutFolder" On="uninstall" />') | Out-Null
$sb.AppendLine("          <RegistryValue Root=`"HKCU`" Key=`"Software\$( $Manufacturer )\$( $AppName )`" Name=`"installed`" Type=`"integer`" Value=`"1`" />") | Out-Null
$sb.AppendLine('        </Component>') | Out-Null
$sb.AppendLine('      </Directory>') | Out-Null
$sb.AppendLine('    </StandardDirectory>') | Out-Null
$sb.AppendLine('') | Out-Null
$sb.AppendLine('    <Feature Id="Main">') | Out-Null
$sb.AppendLine('      <ComponentGroupRef Id="PublishedFiles" />') | Out-Null
$sb.AppendLine('      <ComponentRef Id="ShortcutComponent" />') | Out-Null
$sb.AppendLine('    </Feature>') | Out-Null
$sb.AppendLine('  </Package>') | Out-Null
$sb.AppendLine('') | Out-Null
$sb.AppendLine('  <Fragment>') | Out-Null
$sb.AppendLine('    <ComponentGroup Id="PublishedFiles">') | Out-Null

foreach ($id in $compRefs) {
    $sb.AppendLine("      <ComponentRef Id=`"$id`" />") | Out-Null
}

$sb.AppendLine('    </ComponentGroup>') | Out-Null
$sb.AppendLine('  </Fragment>') | Out-Null
$sb.AppendLine('</Wix>') | Out-Null

[System.IO.File]::WriteAllText($WxsFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

# ── 4b. Localization file (keeps Chinese strings separate from WXS) ──
$locSb = New-Object System.Text.StringBuilder
$locSb.AppendLine('<?xml version="1.0" encoding="utf-8"?>') | Out-Null
$locSb.AppendLine('<WixLocalization xmlns="http://wixtoolset.org/schemas/v4/wxl" Culture="zh-CN">') | Out-Null
$locSb.AppendLine('  <String Id="DowngradeMsg" Value="A newer version is already installed. Uninstall the previous version first." />') | Out-Null
$locSb.AppendLine('</WixLocalization>') | Out-Null
$locWxl = Join-Path $env:TEMP "pet-yue-loc.wxl"
[System.IO.File]::WriteAllText($locWxl, $locSb.ToString(), [System.Text.UTF8Encoding]::new($false))

# ── 5. Build MSI ──
Write-Host "[5/5] Building MSI..."
wix build $WxsFile -out $OutputMsi -arch x64 -ext WixToolset.UI.wixext `
    -loc $locWxl `
    -bindpath pub="$PublishDir" -bindpath plugins="$PluginsDir" `
    -bindpath i18n="$(Join-Path $PublishDir 'I18n')" `
    -intermediatefolder "$env:TEMP\_wix_build"
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

# ── Cleanup ──
Remove-Item $WxsFile, $locWxl -Force -ErrorAction SilentlyContinue
if (Test-Path "$env:TEMP\_wix_build") {
    Remove-Item "$env:TEMP\_wix_build" -Recurse -Force -ErrorAction SilentlyContinue
}

$size = [math]::Round((Get-Item $OutputMsi).Length / 1MB, 1)
Write-Host "`n[Done] $OutputMsi (${size} MB)"
