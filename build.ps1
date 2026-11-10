# 构建 TempBar：生成单文件 exe（dist\TempBar.exe）
[CmdletBinding()]
param([switch]$SkipLibs)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# 1. 依赖库（首次构建时下载）
if (-not $SkipLibs) {
    & (Join-Path $root 'tools\fetch-libs.ps1')
}

# 2. 图标（多尺寸 ico）
Write-Host 'Generating icon...'
python (Join-Path $root 'tools\genicon.py') | Out-Null

# 3. 定位 MSBuild
$msbuild = $null
$candidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
foreach ($candidate in $candidates) {
    if (Test-Path $candidate) { $msbuild = $candidate; break }
}
if (-not $msbuild) { throw 'MSBuild not found - install Visual Studio 2022 (or Build Tools) with .NET desktop workload.' }

# 4. 编译
Write-Host 'Building...'
& $msbuild (Join-Path $root 'src\TempBar.csproj') -nologo -v:m -p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

# 5. 单文件产物：去掉 exe.config，保证只有一个文件
$out = Join-Path $root 'dist'
$config = Join-Path $out 'TempBar.exe.config'
if (Test-Path $config) { Remove-Item $config -Force }

$exe = Join-Path $out 'TempBar.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ''
Write-Host "Done: $exe  ($size MB)" -ForegroundColor Green
