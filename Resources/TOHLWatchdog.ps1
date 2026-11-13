<#
  Town Of Host_L の番犬 (Among Us の外で動く見張り役)

  Among Us が落ちた・固まった・自分から再起動を頼んできた時に、起動し直す。
  TOHL がゲームの中の「番犬」を ON にすると、DLL に埋め込まれたこのファイルを TOHL_DATA\Watchdog に書き出し、
  タスク スケジューラから起動する (Among Us のプロセスの外に置き、Among Us を強制終了しても道連れにならないようにする)。
  Among Us の通信には触らない。
  タスク スケジューラで切り離す形と、心拍・再起動の印での見張り方は End K not (waffle-ful/Aeterna-End-K-not tools/EndKnotWatchdog.ps1) を元にした。

  見張り方:
    TOHL は番犬が ON の間、heartbeat.txt を 5 秒ごとに書き直す (ゲームのメインループから書くので、固まると止まる)。
    - Among Us が消えた → 起動し直す
    - Among Us は居るのに heartbeat.txt が止まった → 固まったとみなし、終わらせて起動し直す
    - restart.flag がある → TOHL が自分で終わって再起動を頼んだので、待ち時間を飛ばして起動し直す
    - stop.flag がある → 番犬を終わる (ゲームの終了・番犬を OFF にした時に TOHL が置く)
    起動し直す時は relaunch.flag を置き、TOHL は起動後のメニューでそれを見て部屋を作り直す。

  暴走を止める仕組み:
    - 起動の直後 150 秒は固まった判定をしない・起動の間は 120 秒あける・1 時間に 12 回まで
    - 心拍を 1 度も出さずに消える起動が 3 回続いたら、30 分おきの再試行だけにする (ログイン切れなど、起動し直しても直らない時)
#>
param(
    [Parameter(Mandatory = $true)][string]$DataDir,
    [string]$ExePath = '',
    [string]$Platform = 'Other',   # Steam / Other (Steam でなければ Epic のマニフェストを探す)
    [int]$RunSeconds = 0           # 0 = ずっと。>0 はその秒数で終わる (試し用)
)

$ErrorActionPreference = 'Continue'

$CheckIntervalSec   = 10
$StaleSeconds       = 90
$BootGraceSec       = 150
$RelaunchCooldownSec = 120
$MaxRelaunchPerHour = 12
$BootDeathHold      = 3
$BootDeathRetrySec  = 1800
$RestartFlagMaxAge  = 300
$RestartQuitWaitSec = 40

$TaskName    = 'TOHLWatchdog'
$Heartbeat   = Join-Path $DataDir 'heartbeat.txt'
$StopFlag    = Join-Path $DataDir 'stop.flag'
$RestartFlag = Join-Path $DataDir 'restart.flag'
$RelaunchFlag = Join-Path $DataDir 'relaunch.flag'
$LogFile     = Join-Path $DataDir 'watchdog.log'

function Write-Log([string]$msg) {
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $msg
    Write-Host $line
    try {
        if ((Test-Path $LogFile) -and (Get-Item $LogFile).Length -gt 1MB) {
            Move-Item $LogFile ($LogFile + '.old') -Force -ErrorAction SilentlyContinue
        }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    } catch { }
}

# 見張る Among Us。同じ PC に別の版 (Steam と Epic など) が動いていても取り違えないよう、exe のパスで絞る。
# パスが読めない個体 (権限の違いなど) は、心拍の pid が合えばそれ、合わなくても居る扱いにする (2 つ目を起動しない側へ倒す)
function Get-Au([int]$HeartbeatPid = 0) {
    $procs = @(Get-Process -Name 'Among Us' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return $null }
    if (-not $ExePath) { return $procs[0] }
    $unknown = @()
    foreach ($p in $procs) {
        $path = $null
        try { $path = $p.Path } catch { }
        if ($path) {
            if ($path -ieq $ExePath) { return $p }
        } else {
            $unknown += $p
        }
    }
    foreach ($p in $unknown) { if ($p.Id -eq $HeartbeatPid) { return $p } }
    if ($unknown.Count -gt 0) { return $unknown[0] }
    return $null
}

# heartbeat.txt の 1 行 (pid=.. state=..) と、書かれてからの秒数
function Get-Heartbeat {
    $r = @{ Pid = 0; Age = [double]::PositiveInfinity; Write = [datetime]::MinValue }
    if (-not (Test-Path $Heartbeat)) { return $r }
    try {
        $item = Get-Item $Heartbeat
        $r.Write = $item.LastWriteTime
        $r.Age = ((Get-Date) - $item.LastWriteTime).TotalSeconds
        $text = Get-Content -Path $Heartbeat -Raw -ErrorAction SilentlyContinue
        if ($text -match 'pid=(\d+)') { $r.Pid = [int]$Matches[1] }
    } catch { }
    return $r
}

function Get-FlagAge([string]$path) {
    try { return ((Get-Date) - (Get-Item $path).LastWriteTime).TotalSeconds } catch { return [double]::PositiveInfinity }
}

# Epic 版なら起動の URL。Epic のマニフェストのうち、入れ先が見張る exe のフォルダと同じ物を探す (無ければ Epic 版ではない)
function Find-EpicUrl {
    try {
        if (-not $ExePath) { return $null }
        $auDir = (Split-Path -Parent $ExePath).TrimEnd('\')
        $dir = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
        foreach ($f in @(Get-ChildItem -Path $dir -Filter '*.item' -ErrorAction SilentlyContinue)) {
            try { $m = Get-Content -Path $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
            $loc = "$($m.InstallLocation)".TrimEnd('\', '/')
            if ($m.AppName -and $loc -and ($loc -ieq $auDir)) {
                return "com.epicgames.launcher://apps/$($m.AppName)?action=launch&silent=true"
            }
        }
    } catch { }
    return $null
}

# Steam 版は Steam 本体が立っていないと起動の途中で止まるので、先に起こして待つ
function Start-SteamIfNeeded {
    if (Get-Process -Name 'steam' -ErrorAction SilentlyContinue) { return }
    try {
        $exe = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamExe
        if ($exe -and (Test-Path $exe)) {
            Write-Log 'Steam が起動していないので起動し、30 秒待つ'
            Start-Process -FilePath $exe -ArgumentList '-silent'
            Start-Sleep -Seconds 30
        }
    } catch { Write-Log "Steam を起動できなかった: $($_.Exception.Message)" }
}

$script:LaunchTimes = New-Object System.Collections.Generic.List[datetime]
$script:LastLaunch = [datetime]::MinValue
$script:Judged = $true        # 直前の起動が心拍を出したかを判定し終えたか
$script:SawProc = $false      # 直前の起動の後に Among Us が現れたか
$script:BootDeaths = 0
$script:SeenPid = 0           # 心拍を出したのを見た Among Us (固まった判定はこの個体だけにする)

function Start-Au([string]$why) {
    Write-Log "Among Us を起動する ($why)"
    try { Set-Content -Path $RelaunchFlag -Value (Get-Date).ToString('o') -Encoding UTF8 } catch { }
    try {
        $url = $null
        if ($Platform -ne 'Steam') { $url = Find-EpicUrl }
        if ($url) {
            Start-Process $url
        } elseif ($ExePath -and (Test-Path $ExePath)) {
            if ($Platform -eq 'Steam') { Start-SteamIfNeeded }
            # TOHL は TOHL_DATA を作業フォルダからの相対で読むので、exe のフォルダで起動する
            Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path -Parent $ExePath)
        } else {
            Write-Log "起動の手段が無い (ExePath=$ExePath Platform=$Platform)"
            return
        }
    } catch {
        Write-Log "起動に失敗: $($_.Exception.Message)"
    }
    $now = Get-Date
    $script:LastLaunch = $now
    $script:LaunchTimes.Add($now)
    $script:Judged = $false
    $script:SawProc = $false
}

function Test-HourlyRoom {
    $cut = (Get-Date).AddHours(-1)
    for ($i = $script:LaunchTimes.Count - 1; $i -ge 0; $i--) {
        if ($script:LaunchTimes[$i] -lt $cut) { $script:LaunchTimes.RemoveAt($i) }
    }
    return $script:LaunchTimes.Count -lt $MaxRelaunchPerHour
}

function Stop-AuProcess($p, [string]$why) {
    Write-Log "Among Us (pid=$($p.Id)) を終わらせる ($why)"
    try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { Write-Log "終わらせられなかった: $($_.Exception.Message)" }
    try { $p.WaitForExit(15000) | Out-Null } catch { }
}

# ===== 本体 =====

try { New-Item -ItemType Directory -Force -Path $DataDir | Out-Null } catch { }
$mutex = New-Object System.Threading.Mutex($false, 'Local\TOHLWatchdog')
if (-not $mutex.WaitOne(0)) {
    Write-Log '番犬はもう動いているので、こちらは終わる'
    return
}
try { Remove-Item $StopFlag -Force -ErrorAction SilentlyContinue } catch { }
Write-Log "番犬を始めた (Platform=$Platform ExePath=$ExePath)"
$started = Get-Date
$holdLogAt = [datetime]::MinValue
$hourlyLogAt = [datetime]::MinValue

while ($true) {
    if (Test-Path $StopFlag) {
        Write-Log '止めの印があるので番犬を終わる'
        try { Remove-Item $StopFlag -Force -ErrorAction SilentlyContinue } catch { }
        break
    }
    if ($RunSeconds -gt 0 -and ((Get-Date) - $started).TotalSeconds -ge $RunSeconds) {
        Write-Log "試しの時間 ($RunSeconds 秒) が過ぎたので終わる"
        break
    }

    $now = Get-Date
    $hb = Get-Heartbeat
    $au = Get-Au $hb.Pid
    $sinceLaunch = ($now - $script:LastLaunch).TotalSeconds

    if ($au) {
        $script:SawProc = $true
        if ($hb.Pid -eq $au.Id -and $hb.Age -lt $StaleSeconds) {
            if ($script:SeenPid -ne $au.Id) { Write-Log "心拍を確かめた (pid=$($au.Id))" }
            $script:SeenPid = $au.Id
            if (-not $script:Judged -and $hb.Write -gt $script:LastLaunch) {
                $script:Judged = $true
                $script:BootDeaths = 0
            }
        }
    }

    # TOHL が再起動を頼んだ (自分で終わる途中)。終わるのを待ち、終わらなければ終わらせて起動し直す
    if (Test-Path $RestartFlag) {
        $age = Get-FlagAge $RestartFlag
        if ($age -gt $RestartFlagMaxAge) {
            Write-Log ('古い再起動の印 ({0:N0} 秒前) を捨てた' -f $age)
            Remove-Item $RestartFlag -Force -ErrorAction SilentlyContinue
        } elseif ($au -and $age -lt $RestartQuitWaitSec) {
            # まだ終わっていない。次の見回りで見る
        } else {
            Remove-Item $RestartFlag -Force -ErrorAction SilentlyContinue
            if ($au) { Stop-AuProcess $au '再起動を頼まれたのに終わらない' }
            if (Test-HourlyRoom) {
                $script:Judged = $true
                Start-Au '再起動を頼まれた'
            } else {
                Write-Log "1 時間の再起動の上限 ($MaxRelaunchPerHour 回) に達したので、再起動の頼みを見送った"
            }
        }
        Start-Sleep -Seconds 3
        continue
    }

    if ($au) {
        # 固まった: 心拍を出していた個体の心拍が止まった
        $inGrace = $sinceLaunch -lt $BootGraceSec
        if ($script:SeenPid -eq $au.Id -and $hb.Age -ge $StaleSeconds -and -not $inGrace) {
            Write-Log ('心拍が {0:N0} 秒止まっている (固まった)' -f $hb.Age)
            Stop-AuProcess $au '固まった'
            $script:SeenPid = 0
            if ($sinceLaunch -ge $RelaunchCooldownSec -and (Test-HourlyRoom)) {
                $script:Judged = $true
                Start-Au '固まった'
            }
        }
        Start-Sleep -Seconds $CheckIntervalSec
        continue
    }

    # ===== Among Us が居ない =====

    if (-not $script:Judged -and $hb.Write -gt $script:LastLaunch) {
        # 見回りの合間に立ち上がって消えた (心拍は出ていた) ので、起動の失敗には数えない
        $script:Judged = $true
        $script:SawProc = $true
        $script:BootDeaths = 0
    }
    if (-not $script:Judged) {
        if (-not $script:SawProc -and $sinceLaunch -lt $BootGraceSec) {
            # まだ立ち上がっていない (Steam や Epic の起動待ち)
            Start-Sleep -Seconds $CheckIntervalSec
            continue
        }
        $script:Judged = $true
        $script:BootDeaths++
        Write-Log "起動したのに心拍を出さずに消えた ($($script:BootDeaths) 回続けて)"
    }

    if ($script:BootDeaths -ge $BootDeathHold -and $sinceLaunch -lt $BootDeathRetrySec) {
        if (($now - $holdLogAt).TotalSeconds -ge 300) {
            $holdLogAt = $now
            Write-Log ('起動しても心拍が出ないのが続くので、起動し直しを休む (ログインや回線を確かめる。{0:N0} 秒後にもう 1 度試す)' -f ($BootDeathRetrySec - $sinceLaunch))
        }
        Start-Sleep -Seconds $CheckIntervalSec
        continue
    }
    if ($sinceLaunch -lt $RelaunchCooldownSec) {
        Start-Sleep -Seconds $CheckIntervalSec
        continue
    }
    if (-not (Test-HourlyRoom)) {
        if (($now - $hourlyLogAt).TotalSeconds -ge 300) {
            $hourlyLogAt = $now
            Write-Log "1 時間の再起動の上限 ($MaxRelaunchPerHour 回) に達したので待つ"
        }
        Start-Sleep -Seconds $CheckIntervalSec
        continue
    }
    $script:SeenPid = 0
    Start-Au 'Among Us が見つからない (落ちた)'
    Start-Sleep -Seconds $CheckIntervalSec
}

# タスクの定義を片付ける (動いているこの番犬は消えない)
try { & schtasks.exe /Delete /F /TN $TaskName 2>&1 | Out-Null } catch { }
try { $mutex.ReleaseMutex() } catch { }
Write-Log '番犬を終わった'
