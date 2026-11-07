# 配布用の zip を作る。BepInEx とその周りは版を固定して取り、sha256 で照合する。
#   BepInEx 6.0.0-be.735 (win-x64)
#   Unity Doorstop (winhttp.dll・版の表記は 4.5.1) と、pietro による Il2CppInterop 1.5.3 の修正版
#     … Gurge44/EndlessHostRoles@c2e0b297e の packaging から
#   Il2CppInterop.Runtime.dll は tools/InteropWbPatch で write barrier を通すように書き換える
#   doorstop_config.ini は tools/package/doorstop_config.ini (GC_DISABLE_INCREMENTAL を置かない)
#   BepInEx/config/BepInEx.cfg は tools/package/BepInEx.cfg (interop の先読みを切る)
#
# 使い方: pwsh -NoProfile -File tools/package.ps1 -Dll build/TownOfHost_L.dll -Out build/TownOfHost_L.zip
param(
    [Parameter(Mandatory)] [string] $Dll,
    [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$bepinexUrl = 'https://builds.bepinex.dev/projects/bepinex_be/735/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.735%2B5fef357.zip'
$bepinexSha = 'badef8112853a00939a0df6ca143bc0a4e3dc02bd4d21b873302731bfa0e4df4'
$ehr = 'https://raw.githubusercontent.com/Gurge44/EndlessHostRoles/c2e0b297e5fccec8e0a47de2157beb701cc2ff73/packaging'
$parts = [ordered]@{
    'winhttp.dll'                             = @("$ehr/epic/winhttp.dll", 'd1ebba9d7412fbf89a34bea19f1c346fb536c1657ee8efa2309eb9a3a17ab878')
    'BepInEx/core/Il2CppInterop.Common.dll'   = @("$ehr/il2cppinterop/Il2CppInterop.Common.dll", '3feddc562cec465b2a8c3f7beafbf31fbbf363349ef36cb602b20492d021773b')
    'BepInEx/core/Il2CppInterop.Common.xml'   = @("$ehr/il2cppinterop/Il2CppInterop.Common.xml", 'fad6cafb620cfe54e992e43681472411c5f8e2d5f8de4cb6829d93bc0ecee0f0')
    'BepInEx/core/Il2CppInterop.Runtime.dll'  = @("$ehr/il2cppinterop/Il2CppInterop.Runtime.dll", '1e0c158cf7ceaa4599841da4f85882330bdc0bff6bc70fa2b941dedde5c311c7')
    'BepInEx/core/Il2CppInterop.Runtime.xml'  = @("$ehr/il2cppinterop/Il2CppInterop.Runtime.xml", 'a9cb2e376904abd279ed6db375d8f46b8c46ca59e37d365ec8bb0d25ed0dc799')
    'BepInEx/core/Il2CppInterop.Runtime.deps.json' = @("$ehr/il2cppinterop/Il2CppInterop.Runtime.deps.json", '07e3ce436a14ff31c2de493fa402a52e1dadb6b0341dc8d9c8d93e332c6e1f3c')
    'licenses/LGPL-2.1.txt'                   = @('https://raw.githubusercontent.com/BepInEx/BepInEx/5fef3570f212b2fb5fbe9c1d20487c13c2fa90cb/LICENSE', 'f2ceca48af033d8adf337a8f9453e633cfad10a1f8c9fec2e5ae55a2d8ffe952')
    'licenses/LGPL-3.0.txt'                   = @('https://raw.githubusercontent.com/BepInEx/Il2CppInterop/dbda1cb353b0f4253345dc45136d170b9e50a5a0/LICENSE', '5eecce16e59e24ddd9d3712012517a033f2cd0459ace22b43d5659d4624abff0')
}
# InteropWbPatch の出力。Cecil の版と元の DLL が同じなら毎回同じバイト列になる
$patchedRuntimeSha = '535b2c8f0e715b65a8b2bce83f86a970c6df867e44e5cebc9cf52d0b01da3641'

function Get-Verified([string] $url, [string] $sha, [string] $dest) {
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Invoke-WebRequest -Uri $url -OutFile $dest
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash.ToLowerInvariant()
    if ($actual -ne $sha) { throw "sha256 mismatch: $url ($actual)" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "tohl-package-$([Guid]::NewGuid().ToString('N'))"
$stage = Join-Path $work 'stage'
try {
    New-Item -ItemType Directory -Force $stage | Out-Null

    $bepinexZip = Join-Path $work 'bepinex.zip'
    Get-Verified $bepinexUrl $bepinexSha $bepinexZip
    Expand-Archive -LiteralPath $bepinexZip -DestinationPath $stage

    foreach ($p in $parts.GetEnumerator()) {
        Get-Verified $p.Value[0] $p.Value[1] (Join-Path $stage $p.Key)
    }
    Set-Content -LiteralPath (Join-Path $stage '.doorstop_version') -Value '4.5.1' -NoNewline
    Copy-Item (Join-Path $PSScriptRoot 'package/doorstop_config.ini') (Join-Path $stage 'doorstop_config.ini') -Force
    # BepInEx.cfg は変えたい項目だけを置く。残りの項目は BepInEx が初回の起動で既定値を書き足す
    New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx/config') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'package/BepInEx.cfg') (Join-Path $stage 'BepInEx/config/BepInEx.cfg') -Force
    Copy-Item (Join-Path $PSScriptRoot 'package/THIRD-PARTY-NOTICES.txt') (Join-Path $stage 'THIRD-PARTY-NOTICES.txt') -Force
    Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'licenses/GPL-3.0.txt') -Force

    $runtime = Join-Path $stage 'BepInEx/core/Il2CppInterop.Runtime.dll'
    $patched = Join-Path $work 'Il2CppInterop.Runtime.dll'
    dotnet run --project (Join-Path $PSScriptRoot 'InteropWbPatch') -c Release -v:q -- $runtime $patched
    if ($LASTEXITCODE -ne 0) { throw "InteropWbPatch failed ($LASTEXITCODE)" }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $patched).Hash.ToLowerInvariant()
    if ($actual -ne $patchedRuntimeSha) { throw "patched Il2CppInterop.Runtime.dll differs from the expected build ($actual)" }
    Copy-Item $patched $runtime -Force

    if (Select-String -LiteralPath (Join-Path $stage 'doorstop_config.ini') -Pattern '^\s*GC_DISABLE_INCREMENTAL\s*=' -Quiet) {
        throw 'doorstop_config.ini must not set GC_DISABLE_INCREMENTAL'
    }

    Copy-Item $Dll (Join-Path $stage 'BepInEx/plugins/TownOfHost_L.dll') -Force

    if (Test-Path $Out) { Remove-Item $Out -Force }
    New-Item -ItemType Directory -Force (Split-Path ([IO.Path]::GetFullPath($Out)) -Parent) | Out-Null
    # Compress-Archive は Linux で . で始まる .doorstop_version を拾えないので、.NET の ZipFile で固める
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, [IO.Path]::GetFullPath($Out))
    Write-Host "packaged: $Out"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
