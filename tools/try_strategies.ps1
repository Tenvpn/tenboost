# Стенд стратегий: по очереди запускает winws2 с каждым кандидатом и проверяет, что открывается.
# Аналог blockcheck из zapret, но на наших кандидатах в формате catalog.json.
#
# Запуск — от администратора, ВПН выключен, TenBoost выключен (иначе два winws2 мешают друг другу):
#   powershell -ExecutionPolicy Bypass -File tools\try_strategies.ps1 -Candidates cand.json -Out result.txt
#
# Кандидаты: {"strategies":[{"id":..., "title":..., "args":[...]}]} — токены {lua:} {fake:} {filter:}
# берутся из установленного TenBoost, {list:} — из папки lists этого репозитория (или -Lists).
param(
    [Parameter(Mandatory)] [string] $Candidates,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Engine = "$env:ProgramFiles\TenBoost\engine",
    [string] $Lists = (Join-Path $PSScriptRoot '..\lists'),
    [int] $Warmup = 3
)

$ErrorActionPreference = 'Stop'
$targets = [ordered]@{
    'discord.com/api'      = 'https://discord.com/api/v10/gateway'
    'gateway.discord.gg'   = 'https://gateway.discord.gg/'
    'updates.discord.com'  = 'https://updates.discord.com/'
    'cdn.discordapp.com'   = 'https://cdn.discordapp.com/'
    'media.discordapp.net' = 'https://media.discordapp.net/'
    'dl.discordapp.net'    = 'https://dl.discordapp.net/'
    # Те же домены по заведомо верным адресам Cloudflare в обход DNS: открывается это, а не
    # обычный запрос — значит, провайдер подменяет DNS, и стратегия тут ни при чём.
    'discord.com@cf'       = 'https://discord.com/api/v10/gateway|162.159.135.232'
    'updates@cf'           = 'https://updates.discord.com/|162.159.137.232'
    'youtube.com'          = 'https://www.youtube.com/favicon.ico'
    'i.ytimg.com'          = 'https://i.ytimg.com/vi/dQw4w9WgXcQ/default.jpg'
}
$dirs = @{ lua = "$Engine\lua"; fake = "$Engine\files\fake"; filter = "$Engine\windivert.filter"; list = (Resolve-Path $Lists).Path }

function Log([string] $line) { $line | Tee-Object -FilePath $Out -Append }

function Probe {
    $row = @()
    foreach ($name in $targets.Keys) {
        $url, $pin = $targets[$name] -split '\|'
        $resolve = if ($pin) { @('--resolve', "$(([uri]$url).Host):443:$pin") } else { @() }
        $r = & curl.exe -s -o NUL -m 5 @resolve -w '%{http_code}|%{remote_ip}' $url 2>$null
        $code, $ip = ($r -split '\|') + @('', '')
        $ok = $code -and $code -ne '000'
        $row += [pscustomobject]@{ Target = $name; Ok = $ok; Code = $code; Ip = $ip }
    }
    $row
}

function Show([string] $label, $row) {
    $okCount = ($row | Where-Object Ok).Count
    Log ("{0,-34} {1}/{2}  {3}" -f $label, $okCount, $row.Count, (($row | ForEach-Object { "{0}={1}" -f $_.Target, ($(if ($_.Ok) { $_.Code } else { 'x' })) }) -join ' '))
}

if (Get-Process winws2 -ErrorAction SilentlyContinue) { throw 'winws2 уже запущен: выключите TenBoost в окне и повторите' }
Remove-Item $Out -ErrorAction SilentlyContinue
Log "стенд TenBoost $(Get-Date -Format s), движок $(Get-Content "$Engine\ENGINE_VERSION" -ErrorAction SilentlyContinue)"
Log ("маршрут по умолчанию: " + ((Get-NetRoute -DestinationPrefix 0.0.0.0/0 | Sort-Object RouteMetric | ForEach-Object InterfaceAlias) -join ', '))

# DNS: подменённый ответ провайдера не лечится никакой стратегией.
foreach ($d in 'discord.com', 'gateway.discord.gg', 'www.youtube.com') {
    $sys = (Resolve-DnsName $d -Type A -DnsOnly -ErrorAction SilentlyContinue | Where-Object Type -eq 'A' | Select-Object -First 3 -ExpandProperty IPAddress) -join ','
    $goo = (Resolve-DnsName $d -Type A -DnsOnly -Server 8.8.8.8 -ErrorAction SilentlyContinue | Where-Object Type -eq 'A' | Select-Object -First 3 -ExpandProperty IPAddress) -join ','
    Log ("DNS {0,-20} система: {1,-40} 8.8.8.8: {2}" -f $d, $sys, $goo)
}

Show 'без обхода' (Probe)

$list = (Get-Content $Candidates -Raw -Encoding UTF8 | ConvertFrom-Json).strategies
foreach ($s in $list) {
    $engineArgs = foreach ($a in $s.args) {
        [regex]::Replace($a, '\{([a-z]+):([^{}]*)\}', { param($m) Join-Path $dirs[$m.Groups[1].Value] $m.Groups[2].Value })
    }
    $p = Start-Process -FilePath "$Engine\winws2.exe" -ArgumentList ($engineArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) `
        -WorkingDirectory $Engine -WindowStyle Hidden -PassThru -RedirectStandardError "$Out.$($s.id).err"
    Start-Sleep -Seconds $Warmup
    if ($p.HasExited) {
        Log ("{0,-34} движок упал, код {1}: {2}" -f $s.id, $p.ExitCode, ((Get-Content "$Out.$($s.id).err" -ErrorAction SilentlyContinue | Select-Object -Last 2) -join ' / '))
        continue
    }
    Show $s.id (Probe)
    Stop-Process -Id $p.Id -Force
    $p.WaitForExit(5000) | Out-Null
    Remove-Item "$Out.$($s.id).err" -ErrorAction SilentlyContinue
}
Log 'готово'
