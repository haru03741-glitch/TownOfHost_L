param([string]$StatusFile, [int]$GamePid)
# 起動時の読み込み画面。LoadingWindow.dll (BepInEx のパッチャー) が書く状態ファイルを読んで表示する。
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.VisualBasic
[System.Windows.Forms.Application]::EnableVisualStyles()

$stages = @('環境とBepInExの初期化', 'パッチャーの読み込みと適用', 'SuperNewRolesなどの読み込みと適用', 'ゲームの開始')
$bg = [System.Drawing.Color]::FromArgb(32, 32, 36)
$fg = [System.Drawing.Color]::FromArgb(235, 235, 235)
$dim = [System.Drawing.Color]::FromArgb(140, 140, 150)
$ok = [System.Drawing.Color]::FromArgb(110, 210, 130)
$run = [System.Drawing.Color]::FromArgb(250, 200, 80)
$font = New-Object System.Drawing.Font('Yu Gothic UI', 11)
$bold = New-Object System.Drawing.Font('Yu Gothic UI', 14, [System.Drawing.FontStyle]::Bold)

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Among Us を起動しています'
$form.ClientSize = New-Object System.Drawing.Size(460, 300)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.BackColor = $bg
$form.ForeColor = $fg
$form.TopMost = $true

$title = New-Object System.Windows.Forms.Label
$title.Text = '読み込み中…'
$title.Font = $bold
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(20, 16)
$form.Controls.Add($title)

$rows = @()
for ($i = 0; $i -lt $stages.Count; $i++) {
    $mark = New-Object System.Windows.Forms.Label
    $mark.Font = $font; $mark.AutoSize = $false
    $mark.Size = New-Object System.Drawing.Size(28, 26)
    $mark.Location = New-Object System.Drawing.Point(20, (60 + $i * 34))
    $mark.Text = '○'; $mark.ForeColor = $dim
    $name = New-Object System.Windows.Forms.Label
    $name.Font = $font; $name.AutoSize = $false
    $name.Size = New-Object System.Drawing.Size(300, 26)
    $name.Location = New-Object System.Drawing.Point(48, (60 + $i * 34))
    $name.Text = $stages[$i]; $name.ForeColor = $dim
    $time = New-Object System.Windows.Forms.Label
    $time.Font = $font; $time.AutoSize = $false; $time.TextAlign = 'TopRight'
    $time.Size = New-Object System.Drawing.Size(90, 26)
    $time.Location = New-Object System.Drawing.Point(350, (60 + $i * 34))
    $time.ForeColor = $dim
    $form.Controls.AddRange(@($mark, $name, $time))
    $rows += , @($mark, $name, $time)
}

$status = New-Object System.Windows.Forms.Label
$status.Font = New-Object System.Drawing.Font('Yu Gothic UI', 9.5)
$status.AutoSize = $false; $status.AutoEllipsis = $true
$status.Size = New-Object System.Drawing.Size(420, 22)
$status.Location = New-Object System.Drawing.Point(20, 202)
$status.ForeColor = $dim
$status.Text = '準備しています'
$form.Controls.Add($status)

$button = New-Object System.Windows.Forms.Button
$button.Text = 'Among Usを開く'
$button.Font = $font
$button.Size = New-Object System.Drawing.Size(180, 36)
$button.Location = New-Object System.Drawing.Point(140, 244)
$button.FlatStyle = 'Flat'
$button.BackColor = [System.Drawing.Color]::FromArgb(60, 60, 70)
$button.ForeColor = $fg
$button.Add_Click({
    try { [Microsoft.VisualBasic.Interaction]::AppActivate($GamePid) } catch {}
    $form.Close()
})
$form.Controls.Add($button)

$start = @{}; $done = @{}
$script:finishedAt = $null

function Format-Sec($ms) { '{0:N1} 秒' -f ($ms / 1000.0) }

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    # ゲームが終わったら閉じる
    if (-not (Get-Process -Id $GamePid -ErrorAction SilentlyContinue)) { $form.Close(); return }
    $lines = @()
    try { $lines = [System.IO.File]::ReadAllLines($StatusFile, [System.Text.Encoding]::UTF8) } catch {}
    foreach ($l in $lines) {
        if ($l.StartsWith('S ')) {
            $p = $l.Split(' ')
            $idx = [int]$p[1]; $ms = [long]$p[3]
            if ($p[2] -eq 'start') { $start[$idx] = $ms } else { $done[$idx] = $ms }
        } elseif ($l.StartsWith('M ')) {
            $status.Text = $l.Substring(2)
        }
    }
    for ($i = 0; $i -lt $stages.Count; $i++) {
        $r = $rows[$i]
        if ($done.ContainsKey($i)) {
            $r[0].Text = '✓'; $r[0].ForeColor = $ok; $r[1].ForeColor = $fg
            $from = if ($start.ContainsKey($i)) { $start[$i] } else { 0 }
            $r[2].Text = Format-Sec ($done[$i] - $from); $r[2].ForeColor = $fg
        } elseif ($start.ContainsKey($i)) {
            $r[0].Text = '●'; $r[0].ForeColor = $run; $r[1].ForeColor = $run
            $now = ((Get-Date) - (Get-Process -Id $GamePid).StartTime).TotalMilliseconds
            $r[2].Text = Format-Sec ($now - $start[$i]); $r[2].ForeColor = $run
        }
    }
    if ($done.ContainsKey(3)) {
        if ($null -eq $script:finishedAt) {
            $script:finishedAt = Get-Date
            $title.Text = ('準備ができました (合計 {0})' -f (Format-Sec $done[3]))
            $status.Text = 'タイトル画面が開きました'
            $form.TopMost = $false
        } elseif (((Get-Date) - $script:finishedAt).TotalSeconds -ge 10) {
            $form.Close()
        }
    }
})
$timer.Start()
[System.Windows.Forms.Application]::Run($form)
