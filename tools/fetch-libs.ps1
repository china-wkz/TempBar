# 下载本项目依赖的传感器库（编译期需要）
#   LibreHardwareMonitorLib 0.9.4 (MIT)  - CPU 温度
#   HidSharp 2.1.0            (Apache-2.0)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$libs = Join-Path $root 'libs'
New-Item -ItemType Directory -Force -Path $libs | Out-Null

function Get-Lib {
    param([string]$Id, [string]$Version, [string]$Entry)

    $dest = Join-Path $libs ([System.IO.Path]::GetFileName($Entry))
    if (Test-Path $dest) {
        Write-Host "  [skip] $([System.IO.Path]::GetFileName($Entry)) 已存在"
        return
    }

    $url = "https://api.nuget.org/v3-flatcontainer/$($Id.ToLower())/$Version/$($Id.ToLower()).$Version.nupkg"
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "$Id.$Version.nupkg"
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "$Id.$Version"

    Write-Host "  [get ] $Id $Version"
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    Expand-Archive -Path $tmp -DestinationPath $dir -Force
    Copy-Item (Join-Path $dir $Entry) $dest -Force
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  [ok  ] $dest"
}

Write-Host 'Fetching sensor libraries...'
Get-Lib -Id 'LibreHardwareMonitorLib' -Version '0.9.4' -Entry 'lib/net472/LibreHardwareMonitorLib.dll'
Get-Lib -Id 'HidSharp'                -Version '2.1.0' -Entry 'lib/netstandard2.0/HidSharp.dll'
Write-Host 'Done.'
