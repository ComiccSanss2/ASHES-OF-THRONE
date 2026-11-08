# Sends bridge commands one by one. "wait:N" sleeps N seconds; "render:...|name" saves Logs\r_<name>.png
param([string]$file)
Set-Location D:\DarkFantasy
$log = "Logs\DarkFantasy_Console.log"
foreach ($line in Get-Content $file) {
    $line = $line.Trim(); if ($line -eq "") { continue }
    if ($line.StartsWith("wait:")) { Start-Sleep -Seconds ([double]$line.Substring(5)); continue }
    $name = $null
    if ($line.Contains("|")) { $parts = $line.Split("|"); $line = $parts[0]; $name = $parts[1] }
    $n = (Get-Content $log).Count
    Set-Content Logs\df_command.txt $line
    $t = 0
    do { Start-Sleep -Milliseconds 400; $t += 0.4; $new = Get-Content $log | Select-Object -Skip $n } until (($new -match "^\[cmd\] $([regex]::Escape($line))") -or $t -gt 20)
    if ($line -eq "shot") {
        Start-Sleep -Milliseconds 1500
        $script:shotN = $script:shotN + 1
        Copy-Item Logs\df_shot.png "Logs\s_$($script:shotN).png" -Force -ErrorAction SilentlyContinue
    }
    if ($line.StartsWith("render:")) {
        $t = 0
        do { Start-Sleep -Milliseconds 400; $t += 0.4; $new = Get-Content $log | Select-Object -Skip $n } until (($new -match "\[render\]") -or $t -gt 20)
        if ($name) { Copy-Item Logs\df_render.png "Logs\r_$name.png" -Force }
    }
    Start-Sleep -Milliseconds 300
}
