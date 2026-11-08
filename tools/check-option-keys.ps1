<#
.SYNOPSIS
    設定 (OptionItem) の保存のキーが重なりそうな所を、ビルド前に静的に探す。

.DESCRIPTION
    設定は保存のキー (親をたどった名前の道筋。Modules/OptionItem/OptionItem.cs の Key) で引く。
    同じ親の下に同じ名前の設定が 2 つあると、起動時に 2 つ目以降へ #2・#3… が付く。
    番号は作る順で決まるので、間に設定を足すと番号がずれて、保存した値が別の設定へ移る。
    重なる設定には SetKeyName で別の名前を付ける。

    ここで探すのは、同じ関数の中で、同じ名前の式と同じ親で作っている Create の組。
      XxxOptionItem.Create(RoleInfo, <名前>, ...)  親は役職の出現率 (または .SetParent / 最後の引数)
      XxxOptionItem.Create(<名前>, ...)            親は .SetParent の引数 (無ければ無し)
    同じ文か、作った変数への次の文で SetKeyName を呼んでいれば数えない。

    ループの中で 1 回だけ書いた Create (役職ごとに作る設定など) や、別の関数で作って同じ親へ付ける物は
    ここでは見つからない。起動時の OptionItem.FinalizeKeys が重なりをエラーで出す (DEBUG ビルドでは画面にも出す)。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$optTypes = 'BooleanOptionItem|IntegerOptionItem|FloatOptionItem|StringOptionItem|TextOptionItem|ObjectOptionitem|FilterOptionItem'
$createRx = [regex]"\b(?:$optTypes)\s*\.\s*Create\s*\("
$roleInfoRx = [regex]'^(?:\w+\.)*(?:RoleInfo|roleInfo|info|Info)$'
$methodRx = [regex]'(?m)^[ \t]*(?:(?:public|private|internal|protected|static|override|virtual|sealed|async)\s+)+[\w<>\[\],.? ]+\s+\w+\s*\([^;{]*\)\s*(?:=>|\{|$)'

# コメントを空白に置き換える。文字列の中の // は残し、行番号がずれないよう改行は保つ。
function Remove-Comments([string]$src) {
    $sb = [System.Text.StringBuilder]::new($src.Length)
    $i = 0; $n = $src.Length
    while ($i -lt $n) {
        $c = $src[$i]
        if ($c -eq '"' -or $c -eq "'") {
            $q = $c; [void]$sb.Append($c); $i++
            while ($i -lt $n -and $src[$i] -ne $q) {
                if ($src[$i] -eq '\') { [void]$sb.Append($src[$i]); $i++ }
                if ($i -lt $n) { [void]$sb.Append($src[$i]); $i++ }
            }
            if ($i -lt $n) { [void]$sb.Append($src[$i]); $i++ }
            continue
        }
        if ($c -eq '/' -and $i + 1 -lt $n -and $src[$i + 1] -eq '/') {
            while ($i -lt $n -and $src[$i] -ne "`n") { [void]$sb.Append(' '); $i++ }
            continue
        }
        if ($c -eq '/' -and $i + 1 -lt $n -and $src[$i + 1] -eq '*') {
            while ($i -lt $n -and -not ($src[$i] -eq '*' -and $i + 1 -lt $n -and $src[$i + 1] -eq '/')) {
                [void]$sb.Append($(if ($src[$i] -eq "`n") { "`n" } else { ' ' })); $i++
            }
            [void]$sb.Append('  '); $i += 2
            continue
        }
        [void]$sb.Append($c); $i++
    }
    $sb.ToString()
}

# s[start] は '(' の次。引数の文字列の並びと、閉じ括弧の位置を返す
function Split-Args([string]$s, [int]$start) {
    $depth = 0; $a = $start; $args_ = [System.Collections.Generic.List[string]]::new(); $i = $start
    while ($i -lt $s.Length) {
        $c = $s[$i]
        if ($c -eq '"' -or $c -eq "'") {
            $q = $c; $i++
            while ($i -lt $s.Length -and $s[$i] -ne $q) { if ($s[$i] -eq '\') { $i++ }; $i++ }
        }
        elseif ('([{'.Contains($c)) { $depth++ }
        elseif (')]}'.Contains($c)) {
            if ($depth -eq 0) { $args_.Add($s.Substring($a, $i - $a).Trim()); return , @($args_, $i) }
            $depth--
        }
        elseif ($c -eq ',' -and $depth -eq 0) { $args_.Add($s.Substring($a, $i - $a).Trim()); $a = $i + 1 }
        $i++
    }
    return , @($args_, $i)
}

$found = 0; $calls = 0
$files = Get-ChildItem -Path $root -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(build|bin|obj|\.git|\.vs)\\' }
foreach ($file in $files) {
    $src = Remove-Comments ([System.IO.File]::ReadAllText($file.FullName))
    $methodStarts = @($methodRx.Matches($src) | ForEach-Object { $_.Index })
    $groups = @{}
    foreach ($m in $createRx.Matches($src)) {
        $calls++
        $r = Split-Args $src ($m.Index + $m.Length)
        $a = $r[0]; $end = $r[1]
        if ($a.Count -eq 0) { continue }
        $isRole = $roleInfoRx.IsMatch($a[0])
        $name = if ($isRole -and $a.Count -gt 1) { $a[1] } else { $a[0] }
        $semi = $src.IndexOf(';', $end); if ($semi -lt 0) { $semi = $src.Length - 1 }
        $stmt = $src.Substring($end, $semi - $end)
        if ($stmt.Contains('SetKeyName')) { continue }
        # 作った変数への次の文の SetKeyName (型を保つため分けて書いた物)
        $lineStart = $src.LastIndexOf("`n", $m.Index) + 1
        $lhs = [regex]::Match($src.Substring($lineStart, $m.Index - $lineStart), '(\w+)\s*=\s*$')
        if ($lhs.Success) {
            $next = $src.Substring($semi + 1, [Math]::Min(200, $src.Length - $semi - 1))
            if ($next -match "^\s*$([regex]::Escape($lhs.Groups[1].Value))\s*\.\s*SetKeyName\s*\(") { continue }
        }
        $pm = [regex]::Match($stmt, '\.SetParent\(([^;]*?)\)\s*(?:\.|$)')
        $parent = if ($pm.Success) { $pm.Groups[1].Value.Trim() } elseif ($isRole) { 'role' } else { 'none' }
        if ($isRole -and -not $pm.Success -and $a.Count -ge 6 -and $a[$a.Count - 1] -match '^(Option\w*|\w*Opt\w*|parent)$') { $parent = $a[$a.Count - 1] }
        $method = ($methodStarts | Where-Object { $_ -lt $m.Index } | Select-Object -Last 1)
        $line = ([regex]::Matches($src.Substring(0, $m.Index), "`n")).Count + 1
        $key = "$method|$name|$parent"
        if (-not $groups.ContainsKey($key)) { $groups[$key] = [System.Collections.Generic.List[int]]::new() }
        $groups[$key].Add($line)
    }
    foreach ($k in $groups.Keys) {
        if ($groups[$k].Count -lt 2) { continue }
        $parts = $k.Split('|')
        $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        Write-Host "$rel 行 $($groups[$k] -join ', '): 名前 $($parts[1]) / 親 $($parts[2]) の設定が重なる。SetKeyName で別の名前を付ける" -ForegroundColor Red
        $found++
    }
}

Write-Host "設定の Create: $calls 件"
if ($found -gt 0) {
    Write-Host "保存のキーが重なりそうな組が $found 個あります。" -ForegroundColor Red
    exit 1
}
Write-Host "保存のキーが重なりそうな組はありません。"
