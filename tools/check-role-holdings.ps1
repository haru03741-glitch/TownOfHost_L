<#
.SYNOPSIS
    役職 (Roles/) から、役職が外れた時に残ってしまう掛け方をしていないかを、ビルドの前に静的に探す。

.DESCRIPTION
    役職は試合の途中でも替わる。役職が自分や他の人に掛けた物は RoleBase.Holdings (Roles/Core/RoleHoldings.cs) から付けると、
    役職が外れる時にその役職の分だけ外れる。ここでは、Holdings を通さずに直に付けている所を探す。

    - 速さの修飾 (PlayerSpeed.Freeze / Override / Multiply / Add) は Holdings.Freeze などで付ける。

    役職が外れても残すのが正しい物 (相手に渡した加速・自分で外す時刻を持つ罠など) は、
    同じ行に「役職が外れても残す」と理由を書けば数えない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allow = '役職が外れても残す'

$rules = @(
    @{ Rx = [regex]'(?<![\w])(?:TownOfHostL\.Modules\.)?PlayerSpeed\.(Freeze|Override|Multiply|Add)\s*\('
       Msg = '速さの修飾を直に付けている。Holdings.Freeze / OverrideSpeed / MultiplySpeed / AddSpeed で付ける' }
)

$found = 0
$files = Get-ChildItem -Path (Join-Path $root 'Roles') -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne 'RoleHoldings.cs' }
foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $code = ($line -split '//', 2)[0]
        if ($line.Contains($allow)) { continue }
        foreach ($rule in $rules) {
            if (-not $rule.Rx.IsMatch($code)) { continue }
            $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName)
            Write-Host "$rel 行 $($i + 1): $($rule.Msg)" -ForegroundColor Red
            $found++
        }
    }
}

if ($found -gt 0) {
    Write-Host "役職が外れた時に残る掛け方が $found 個あります。残すのが正しい物は、同じ行に「$allow」と理由を書いてください。" -ForegroundColor Red
    exit 1
}
Write-Host '役職が外れた時に残る掛け方はありません。'
