param([ValidateSet('start','stop','firewall','remove-firewall')][string]$Action = 'start')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$cfg = Get-Content -LiteralPath (Join-Path $root 'settings.json') -Raw | ConvertFrom-Json
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
if ($Action -eq 'stop') {
    function Get-ListenerPid([int]$port) {
        foreach ($line in (& netstat.exe -ano -p tcp)) {
            $parts = $line -split '\s+' | Where-Object { $_ }
            if ($parts.Count -ge 5 -and $parts[1].EndsWith(":$port") -and $parts[3] -eq 'LISTENING') { return [int]$parts[4] }
        }
        return 0
    }
    function Stop-Tracked([string]$recordName, [string]$expectedPath, [int]$port, [bool]$allowLegacy) {
        $owner = Get-ListenerPid $port
        $recordPath = Join-Path $logs $recordName
        $record = if (Test-Path -LiteralPath $recordPath) { Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json } else { $null }
        if ($record -and $record.Executable) { $expectedPath = $record.Executable }
        $targetId = if ($record) { [int]$record.Id } elseif ($allowLegacy) { $owner } else { 0 }
        if (!$targetId) {
            if ($owner) { throw "Port $port belongs to a process not started by KVMem Desktop." }
            return
        }
        $process = Get-Process -Id $targetId -ErrorAction SilentlyContinue
        if (!$process) {
            if ($owner) { throw "Port $port is now owned by another process; refusing to stop it." }
            if ($record) { Remove-Item -LiteralPath $recordPath -Force -ErrorAction SilentlyContinue }
            return
        }
        if ($owner -and $owner -ne $targetId) { throw "Port $port is now owned by another process; refusing to stop it." }
        if ($record -and $record.StartTime -and [Math]::Abs(($process.StartTime.ToUniversalTime() - ([datetime]$record.StartTime).ToUniversalTime()).TotalSeconds) -gt 5) {
            if ($owner) { throw "Saved process ID for port $port has been reused." }
            Remove-Item -LiteralPath $recordPath -Force -ErrorAction SilentlyContinue
            return
        }
        if (!$expectedPath -or ![string]::Equals([IO.Path]::GetFullPath($process.Path), [IO.Path]::GetFullPath($expectedPath), [StringComparison]::OrdinalIgnoreCase)) { throw "Port $port belongs to an unexpected executable; refusing to stop it." }
        $process.Kill()
        if (!$process.WaitForExit(30000)) { throw "Process on port $port did not stop within 30 seconds." }
        if ($record) { Remove-Item -LiteralPath $recordPath -Force -ErrorAction SilentlyContinue }
    }
    Stop-Tracked 'server-process.json' $cfg.Executable 18200 $false
    Stop-Tracked 'bridge-process.json' $cfg.Node 18201 $true
    Write-Output 'KVMem Desktop services stopped.'
    exit 0
}
if ($Action -eq 'firewall' -or $Action -eq 'remove-firewall') {
    $ruleName = 'KVMem Desktop LAN'
    $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($existing) { $existing | Remove-NetFirewallRule }
    if ($Action -eq 'firewall') {
        $ip = [Net.IPAddress]::Parse($cfg.LanAddress)
        $prefix = [int]$cfg.LanPrefix
        if ($ip.AddressFamily -ne 'InterNetwork' -or $prefix -lt 8 -or $prefix -gt 30) { throw 'Invalid LAN network' }
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 18200,18201 -LocalAddress $cfg.LanAddress -RemoteAddress ($cfg.LanAddress + '/' + $prefix) -Profile Any | Out-Null
    }
    ('OK ' + $Action + ' ' + (Get-Date).ToString('s')) | Set-Content -LiteralPath (Join-Path $logs 'firewall-result.txt')
    exit 0
}
$serverAlive = $false
try { $m = Invoke-RestMethod 'http://127.0.0.1:18200/v1/models' -TimeoutSec 2; $serverAlive = $m.data[0].id -eq [IO.Path]::GetFileName($cfg.Model) } catch {}
if (!$serverAlive) {
    $probe = [Net.Sockets.TcpClient]::new()
    try { $probe.Connect('127.0.0.1',18200); throw 'Port 18200 is already in use or the model is still loading. Wait, or stop the existing service first.' }
    catch [Net.Sockets.SocketException] {} finally { $probe.Dispose() }
    if (!(Test-Path -LiteralPath $cfg.Executable)) { throw 'KVMem executable not found' }
    if (!(Test-Path -LiteralPath $cfg.Model)) { throw 'GGUF model not found' }
    $bind = if ($cfg.LanEnabled) { '0.0.0.0' } else { '127.0.0.1' }
    $arguments = @('-m', ('"' + $cfg.Model + '"'), '--host', $bind, '--port', '18200',
        '-c', $cfg.Context, '-n', $cfg.Reserve, '-b', '512', '-ngl', '99',
        '--kvmem-budget', $cfg.Budget, '--kvmem-gen-reserve', $cfg.Reserve,
        '--kvmem-gpu-ratio', '0.9', '--kvmem-block-tokens', '128', '--kv-dtype', 'q8_0',
        '--spec-type', 'draft-mtp', '--spec-draft-n-max', '3', '--spec-draft-p-min', '0', '--spec-kv-dtype', 'f16',
        '--reasoning-effort', 'medium', '--reasoning-budget', $cfg.ThinkingBudget,
        '--temp', '1.0', '--top-p', '0.95', '--top-k', '20', '--min-p', '0.0',
        '--presence-penalty', '0.0', '--frequency-penalty', '0.0', '--repeat-penalty', '1.0')
    if ($cfg.VisionEnabled) {
        if (!(Test-Path -LiteralPath $cfg.Mmproj -PathType Leaf)) { throw 'Vision projector GGUF not found' }
        if ([int]$cfg.ImageMaxTokens -lt 64 -or [int]$cfg.ImageMaxTokens -gt 4096) { throw 'Image token limit must be 64-4096' }
        $arguments += @('--mmproj', ('"' + $cfg.Mmproj + '"'), '--image-max-tokens', [int]$cfg.ImageMaxTokens)
        $arguments += if ($cfg.VisionGpu) { '--mmproj-offload' } else { '--no-mmproj-offload' }
    }
    $arguments += if ($cfg.Thinking) { '--enable-thinking' } else { '--no-think' }
    $env:CUDA_VISIBLE_DEVICES = '0'
    $p = Start-Process -FilePath $cfg.Executable -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'server-out.log') -RedirectStandardError (Join-Path $logs 'server.log') -PassThru
    @{Id=$p.Id;StartTime=$p.StartTime.ToUniversalTime().ToString('o');Executable=$cfg.Executable} | ConvertTo-Json | Set-Content (Join-Path $logs 'server-process.json')
}
$bridgeAlive = $false
try { $v = Invoke-RestMethod 'http://127.0.0.1:18201/api/version' -TimeoutSec 2; $bridgeAlive = $v.version -eq '0.0.0-kvmem-bridge' } catch {}
if (!$bridgeAlive) {
    $probe = [Net.Sockets.TcpClient]::new()
    try { $probe.Connect('127.0.0.1',18201); throw 'Port 18201 is already in use by a different service.' }
    catch [Net.Sockets.SocketException] {} finally { $probe.Dispose() }
    $env:KVMEM_BRIDGE_HOST = if ($cfg.LanEnabled) { '0.0.0.0' } else { '127.0.0.1' }
    $env:KVMEM_BRIDGE_MODEL = [IO.Path]::GetFileName($cfg.Model)
    $env:KVMEM_BRIDGE_CONTEXT = $cfg.Context
    $env:KVMEM_BRIDGE_MAX_OUTPUT = $cfg.Reserve
    $bridge = Start-Process -FilePath $cfg.Node -ArgumentList ('"' + (Join-Path $root 'ollama-kvmem-bridge.cjs') + '"') -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'bridge.log') -RedirectStandardError (Join-Path $logs 'bridge-error.log') -PassThru
    @{Id=$bridge.Id;StartTime=$bridge.StartTime.ToUniversalTime().ToString('o');Executable=$cfg.Node} | ConvertTo-Json | Set-Content (Join-Path $logs 'bridge-process.json')
}
