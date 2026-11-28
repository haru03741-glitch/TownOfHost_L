<#
.SYNOPSIS
    客へ本編の役職 (SetRole) を送る所が、見る人ごとの画面の表 (Modules/RoleView.cs) に記録しているかを、ビルドの前に静的に探す。
    ホストの手元だけで付ける所 (CoSetRole・RoleManager.SetRole) は見ない (ホストの画面は表を使わず手元の役職を読む)。

.DESCRIPTION
    RoleView は、見る人の画面に今ある役職と、生きている時の見え方を覚える。送る所が 1 か所でも記録しないと表が実際の画面とずれ、
    表を使う判断 (本人へ同じ役職を送り直さない・復活で戻す見え方・能力のクールの数え) が誤る。

    SetRole は関所 (RpcSetRoleDesync・RpcSetRoleNormal・SetRoleEx・RoleView.Send・CustomRpcSender.WriteSetRole) から送る。
    RpcCalls.SetRole を直に書く行は、その行から 12 行以内に RoleView の記録 (Note・Record・Unknown など) を置くか、
    同じ行に「画面の表は」で始まる理由 (どこで覚えるか) を書く。

    数えない物: 関所そのもの (RoleView.cs・ExtendedPlayerControl.cs・CustomRpcSender.cs)・受信の処理 (RPC.cs・RpcGuard.cs)。
    本編の PlayerControl.RpcSetRole は、パッチ (PlayerControlSetRolePatch) が関所へ回すので数えない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allow = '画面の表は'
$write = [regex]'RpcCalls\.SetRole\b'
$note = [regex]'RoleView\.(Note|NoteByClientId|Record|RecordByClientId|Unknown)\s*\('
$skip = @('Modules/RoleView.cs', 'Modules/ExtendedPlayerControl.cs', 'Modules/CustomRpcSender.cs', 'Modules/RPC.cs', 'Modules/RpcGuard.cs')
$window = 12

$found = 0
$files = foreach ($dir in 'Roles', 'Modules', 'Core', 'Patches') {
    $path = Join-Path $root $dir
    if (Test-Path $path) { Get-ChildItem -Path $path -Recurse -Filter *.cs }
}
foreach ($file in $files) {
    $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
    if ($skip -contains $rel) { continue }
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $code = ($line -split '//', 2)[0]
        if (-not $write.IsMatch($code)) { continue }
        if ($line.Contains($allow)) { continue }
        $ok = $false
        for ($j = $i; $j -lt [Math]::Min($lines.Length, $i + $window + 1); $j++) {
            if ($note.IsMatch(($lines[$j] -split '//', 2)[0])) { $ok = $true; break }
        }
        if ($ok) { continue }
        Write-Host "$rel 行 $($i + 1): SetRole を直に書いているが、RoleView に記録していない" -ForegroundColor Red
        $found++
    }
}

if ($found -gt 0) {
    Write-Host "画面の表に記録しない SetRole の送りが $found 個あります。関所から送るか、送った後で RoleView.Note を呼んでください。記録が別の所にある物は、同じ行に「$allow…」と理由を書いてください。" -ForegroundColor Red
    exit 1
}
Write-Host '客への SetRole の送りは、どれも画面の表に記録しています。'
