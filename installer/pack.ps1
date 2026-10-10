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
$PublishDir    = Join-Path $ProjectRoot "yopet\bin\$Configuration\net9.0\$Runtime\publish"
$PluginsDir    = Join-Path $PublishDir "plugins"
$WxsFile       = Join-Path $env:TEMP "pet-yue-build.wxs"
$OutputMsi     = Join-Path $ScriptDir "yopet-$Version.msi"

# ── 1. Ensure WiX installed ──
if (-not (Get-Command wix.exe -ErrorAction SilentlyContinue)) {
    Write-Host "[1/5] Installing WiX Toolset..."
    dotnet tool install --global wix --version 5.*
}

# ── 2. Publish main project ──
Write-Host "[2/5] Publishing main project..."
dotnet publish (Join-Path $ProjectRoot "yopet\yopet.csproj") `
    --configuration $Configuration --runtime $Runtime --self-contained false
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# ── 3. Build plugins and copy to publish/plugins/ ──
Write-Host "[3/5] Building plugins..."
if (Test-Path $PluginsDir) { Remove-Item $PluginsDir -Recurse -Force }
New-Item -ItemType Directory -Path $PluginsDir | Out-Null

$skipDlls = @("yopet.Sdk.dll", "yopet.Core.dll",
              "yopet.Services.dll", "yopet.ViewModels.dll")

$projFiles = Get-ChildItem -Path (Join-Path $ProjectRoot "Plugins") -Recurse -Filter "*.csproj"
foreach ($proj in $projFiles) {
    Write-Host "  Building $($proj.BaseName)..."
    dotnet build $proj.FullName --configuration $Configuration --no-restore 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Plugin $($proj.BaseName) build failed" }
    $binDir = Join-Path $proj.Directory.FullName "bin\$Configuration\net9.0"
    if (Test-Path $binDir) {
        foreach ($f in Get-ChildItem -File $binDir -Filter "*.dll") {
            if ($f.Name -notin $skipDlls) { Copy-Item $f.FullName (Join-Path $PluginsDir $f.Name) -Force }
        }
    }
}
$pluginCount = (Get-ChildItem $PluginsDir -Filter "*.dll").Count
Write-Host "  $pluginCount plugin dll(s) copied"

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

# plugins/ subdirectory
$sb.AppendLine('          <Directory Id="pluginsDir" Name="plugins">') | Out-Null
$pluginFiles = Get-ChildItem -File $PluginsDir
foreach ($file in $pluginFiles) {
    $baseId = "p_" + [System.IO.Path]::GetFileNameWithoutExtension($file.Name) -replace '[^a-zA-Z0-9_]', '_'
    $id = $baseId; $suffix = 0
    while ($compRefs -contains $id) { $suffix++; $id = "${baseId}_$suffix" }
    $compRefs.Add($id)
    $sb.AppendLine("            <Component Id=`"$id`" Guid=`"*`">") | Out-Null
    $sb.AppendLine("              <File Source=`"!(bindpath.plugins)\$($file.Name)`" />") | Out-Null
    $sb.AppendLine("            </Component>") | Out-Null
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
Remove-Item $PluginsDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path "$env:TEMP\_wix_build") {
    Remove-Item "$env:TEMP\_wix_build" -Recurse -Force -ErrorAction SilentlyContinue
}

$size = [math]::Round((Get-Item $OutputMsi).Length / 1MB, 1)
Write-Host "`n[Done] $OutputMsi (${size} MB)"
