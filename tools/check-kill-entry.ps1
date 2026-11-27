<#
.SYNOPSIS
    本編へキルを送る所が、キルの入口 (Roles/Core/Kill.cs) を通っているかを、ビルドの前に静的に探す。

.DESCRIPTION
    キルは Kill.Force / Kill.Suicide (判定を通さない) か、キルの判定 (CustomRoleManager.OnCheckMurder) から送る。
    入口は、相手が既に死んでいる・抜けている時に送らないことと、送り口の手当て (死体を残さない・溶かす) を 1 か所で受け持つ。
    本編の RpcMurderPlayer を直に呼ぶと、これを素通りする。

    数えない物: 入口そのもの (Kill.cs)・失敗したキルを見せる送り (RpcMurderPlayer(…, false))。
    直に呼ぶのが正しい所は、同じ行に「キルの入口を通さない」と理由を書けば数えない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allow = 'キルの入口を通さない'
$call = [regex]'\.RpcMurderPlayer(V2)?\s*\((?<args>[^;]*)\)\s*;'

$found = 0
$files = foreach ($dir in 'Roles', 'Modules', 'Core', 'Patches') {
    $path = Join-Path $root $dir
    if (Test-Path $path) { Get-ChildItem -Path $path -Recurse -Filter *.cs }
}
foreach ($file in $files) {
    $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
    if ($rel -eq 'Roles/Core/Kill.cs') { continue }
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line.Contains($allow)) { continue }
        $code = ($line -split '//', 2)[0]
        foreach ($m in $call.Matches($code)) {
            if ($m.Groups['args'].Value -match ',\s*false\s*$') { continue }
            Write-Host "$rel 行 $($i + 1): 本編の RpcMurderPlayer を直に呼んでいる。Kill.Force / Kill.Suicide から送る" -ForegroundColor Red
            $found++
        }
    }
}

if ($found -gt 0) {
    Write-Host "キルの入口を通らない送りが $found 個あります。直に呼ぶのが正しい物は、同じ行に「$allow」と理由を書いてください。" -ForegroundColor Red
    exit 1
}
Write-Host 'キルの入口を通らない送りはありません。'
