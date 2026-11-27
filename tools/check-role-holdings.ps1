<#
.SYNOPSIS
    役職 (Roles/) から、役職が外れた時に残ってしまう掛け方をしていないかを、ビルドの前に静的に探す。

.DESCRIPTION
    役職は試合の途中でも替わる。役職が自分や他の人に掛けた物は RoleBase.Holdings (Roles/Core/RoleHoldings.cs) から付けると、
    役職が外れる時にその役職の分だけ外れる。ここでは、Holdings を通さずに直に付けている所を探す。

    - 速さの修飾 (PlayerSpeed.Freeze / Override / Multiply / Add) は Holdings.Freeze などで付ける。
    - 他の人向けの登録 (CustomRoleManager の *Others) は Holdings.Subscribe / SubscribeShared で足し、手で外さない。
      属性 (Roles/AddOns) は役職のインスタンスを持たないので数えない。

    役職が外れても残すのが正しい物 (相手に渡した加速・自分で外す時刻を持つ罠など) は、
    同じ行に「役職が外れても残す」と理由を書けば数えない。

    - 遅れて走る処理のうち、この役職のインスタンス (Player・MyState・MyTaskState・Holdings) を使う物は RoleBase.After で作る
      (役職が外れたら捨てる)。役職が外れても必ず走らせる後始末 (変身の戻し・撃ち終わりの片付けなど) は、
      new LateTask の行かその 1 つ上の行に「役職が外れても走らせる」と理由を書けば数えない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allow = '役職が外れても残す'
$allowTask = '役職が外れても走らせる'
# 役職のインスタンスの持ち物 (ほかの物の .Player や Player = の代入は除く)
$instanceRx = [regex]'(?<![\w.])(Player|MyState|MyTaskState|Holdings)\b(?!\s*=[^=])'

# new LateTask( の開きかっこから、対になる閉じかっこの位置を返す (文字列とコメントの中のかっこは数えない)
function Find-CallEnd([string]$s, [int]$open) {
    $depth = 0
    for ($i = $open; $i -lt $s.Length; $i++) {
        $c = $s[$i]
        if ($c -eq '"') {
            $i++
            while ($i -lt $s.Length -and $s[$i] -ne '"') { if ($s[$i] -eq '\') { $i++ }; $i++ }
        }
        elseif ($c -eq '/' -and $i + 1 -lt $s.Length -and $s[$i + 1] -eq '/') {
            while ($i -lt $s.Length -and $s[$i] -ne "`n") { $i++ }
        }
        elseif ($c -eq '(') { $depth++ }
        elseif ($c -eq ')') { $depth--; if ($depth -eq 0) { return $i } }
    }
    return -1
}

$rules = @(
    @{ Rx = [regex]'(?<![\w])(?:TownOfHostL\.Modules\.)?PlayerSpeed\.(Freeze|Override|Multiply|Add)\s*\('
       Msg = '速さの修飾を直に付けている。Holdings.Freeze / OverrideSpeed / MultiplySpeed / AddSpeed で付ける' }
    @{ Rx = [regex]'CustomRoleManager\.\w+Others\.(Add|Remove)\s*\('
       Msg = '他の人向けの登録を直に足し外ししている。Holdings.Subscribe (共有の static な処理を最後の 1 人で外すなら SubscribeShared) で足す' }
)

$found = 0
$files = Get-ChildItem -Path (Join-Path $root 'Roles') -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne 'RoleHoldings.cs' -and $_.FullName -notmatch '[\\/]Roles[\\/](AddOns|Core)[\\/]' }
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lines = $text -split "`n"
    foreach ($m in [regex]::Matches($text, 'new LateTask\(')) {
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Length
        $line = $lines[$lineNo - 1]
        if ($line.TrimStart().StartsWith('//')) { continue }
        if ($line.Contains($allowTask) -or ($lineNo -ge 2 -and $lines[$lineNo - 2].Contains($allowTask))) { continue }
        $end = Find-CallEnd $text ($m.Index + $m.Length - 1)
        if ($end -lt 0) { continue }
        $body = $text.Substring($m.Index + $m.Length, $end - $m.Index - $m.Length)
        if (-not $instanceRx.IsMatch($body)) { continue }
        $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        Write-Host "$rel 行 ${lineNo}: 役職のインスタンスを使う遅れた処理を直に作っている。After で作る (役職が外れたら捨てる)" -ForegroundColor Red
        $found++
    }
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
    Write-Host "役職が外れた時に残る掛け方が $found 個あります。残すのが正しい物は、同じ行に「$allow」(遅れた処理は「$allowTask」) と理由を書いてください。" -ForegroundColor Red
    exit 1
}
Write-Host '役職が外れた時に残る掛け方はありません。'
