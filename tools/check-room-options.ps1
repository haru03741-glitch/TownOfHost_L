<#
.SYNOPSIS
    本編の設定の器 (GameOptionsManager.CurrentGameOptions / GameHostOptions・LogicOptions の持つ設定) を差し替える代入が、
    決まった所だけにあるかを、ビルドの前に静的に探す。

.DESCRIPTION
    試合の間の差し替えは RoomOptions (試合に入る・出る) と、ホスト用に組んだ設定を当てる PlayerGameOptionsSender だけが行う。
    ほかの所で差し替えると、試合を出る時に部屋の設定の器へ戻されず、試合の値が部屋の設定として保存される。

    数えない物: RoomOptions.cs・PlayerGameOptionsSender.cs。
    ロビーで部屋の設定そのものを置き換える所 (プリセットの読み込みなど) は、同じ行に「部屋の設定を置き換える」と理由を書けば数えない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allow = '部屋の設定を置き換える'
$assign = [regex]'\b(CurrentGameOptions|GameHostOptions)\s*=(?!=)|\.SetGameOptions\s*\('
$owners = @('Modules/GameOptionsSender/RoomOptions.cs', 'Modules/GameOptionsSender/PlayerGameOptionsSender.cs')

$found = 0
$files = foreach ($dir in 'Roles', 'Modules', 'Core', 'Patches', 'ModGameMode') {
    $path = Join-Path $root $dir
    if (Test-Path $path) { Get-ChildItem -Path $path -Recurse -Filter *.cs }
}
foreach ($file in $files) {
    $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
    if ($owners -contains $rel) { continue }
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line.Contains($allow)) { continue }
        $code = ($line -split '//', 2)[0]
        if ($assign.IsMatch($code)) {
            Write-Host "$rel 行 $($i + 1): 本編の設定の器を差し替えている。試合の間の差し替えは RoomOptions を通す" -ForegroundColor Red
            $found++
        }
    }
}

if ($found -gt 0) {
    Write-Host "RoomOptions を通らない設定の差し替えが $found 個あります。ロビーで部屋の設定そのものを置き換える所は、同じ行に「$allow」と理由を書いてください。" -ForegroundColor Red
    exit 1
}
Write-Host 'RoomOptions を通らない設定の差し替えはありません。'
