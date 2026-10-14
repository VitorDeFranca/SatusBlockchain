<#
.SYNOPSIS
    Helpers compartilhados pelos scripts de demo (etapas 8 e 10).

.DESCRIPTION
    Dot-source no início de cada script principal:

        . "$PSScriptRoot\lib\Node.ps1"

    Três blocos: (1) saída de tela com passos e asserções; (2) cliente HTTP dos
    nós via Invoke-RestMethod (compatível com PowerShell 5.1); (3) utilitários de
    Docker para a partição de rede (disconnect/connect na rede do compose).

    AVISO para quem for estender os scripts: toda a sequência com dependência de
    ordem precisa rodar em UM ÚNICO shell. Chamadas paralelas ao mesmo terminal
    embaralham a cronologia da demonstração (o fork deixa de ser determinístico).

.NOTES
    Compatível com Windows PowerShell 5.1 — sem operador ??, sem ternário, sem
    ForEach-Object -Parallel. Os .ps1 do projeto são gravados em UTF-8 com BOM
    de propósito: sem o BOM, o PowerShell 5.1 lê como ANSI e os acentos viram mojibake.
#>

# Contadores vivem no escopo do script principal (o dot-source funde os dois scopes).
$script:DemoChecks = 0
$script:DemoFailures = 0

# Portas publicadas dos 3 nós no host (docker-compose.yml).
$NodePorts = [ordered]@{ node1 = 8080; node2 = 8081; node3 = 8082 }

# ---------------------------------------------------------------------------
# Saída de tela
# ---------------------------------------------------------------------------

function Write-Step {
    param([string]$Number, [string]$Title)
    Write-Host ""
    Write-Host ("=" * 72)
    Write-Host ("  PASSO {0} — {1}" -f $Number, $Title) -ForegroundColor Cyan
    Write-Host ("=" * 72)
}

function Write-Ok {
    param([string]$Message)
    Write-Host ("   [OK]     {0}" -f $Message) -ForegroundColor Green
}

function Write-Problem {
    param([string]$Message)
    $script:DemoFailures++
    Write-Host ("   [FALHA]  {0}" -f $Message) -ForegroundColor Red
}

function Write-Hint {
    param([string]$Message)
    Write-Host ("   [dica]   {0}" -f $Message) -ForegroundColor DarkGray
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:DemoChecks++
    if ($Condition) { Write-Ok $Message } else { Write-Problem $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    Assert-True ($Expected -eq $Actual) ("{0} — esperado '{1}', obtido '{2}'" -f $Message, $Expected, $Actual)
}

function Get-DemoSummary {
    param([string]$DemoName)
    Write-Host ""
    Write-Host ("=" * 72)
    if ($script:DemoFailures -eq 0) {
        Write-Host ("  {0}: as {1} verificações passaram." -f $DemoName, $script:DemoChecks) -ForegroundColor Green
        Write-Host ("=" * 72)
        return $true
    }
    Write-Host ("  {0}: {1} de {2} verificações FALHARAM." -f $DemoName, $script:DemoFailures, $script:DemoChecks) -ForegroundColor Red
    Write-Host ("=" * 72)
    return $false
}

# ---------------------------------------------------------------------------
# Cliente HTTP dos nós
# ---------------------------------------------------------------------------

function Get-NodeState {
    # Um snapshot com significado de healthcheck: identidade + altura + validade +
    # impressão digital do topo (último hash). /chain/validate faz a validação real
    # da cadeia inteira — a mesma que o HEALTHCHECK do container usa.
    param([int]$Port)
    try {
        $id = Invoke-RestMethod -Uri "http://localhost:$Port/" -TimeoutSec 10
        $v = Invoke-RestMethod -Uri "http://localhost:$Port/chain/validate" -TimeoutSec 30
        return [pscustomobject]@{
            Node      = [string]$id.node
            Length    = [int]$v.length
            Last      = [string]$v.latestHash
            Valid     = [bool]$v.valid
            Reachable = $true
        }
    }
    catch {
        return [pscustomobject]@{
            Node      = "?"
            Length    = -1
            Last      = ""
            Valid     = $false
            Reachable = $false
        }
    }
}

function Show-Cluster {
    param([string]$Label)
    Write-Host ("   {0}" -f $Label) -ForegroundColor DarkGray
    foreach ($name in $NodePorts.Keys) {
        $s = Get-NodeState -Port $NodePorts[$name]
        if ($s.Reachable) {
            $short = $s.Last
            if ($short.Length -ge 13) { $short = $short.Substring(0, 13) }
            Write-Host ("     {0}: blocks={1,-4} valid={2,-6} last={3}..." -f $s.Node, $s.Length, $s.Valid, $short)
        }
        else {
            Write-Host ("     {0}: INACESSIVEL (porta {1})" -f $name, $NodePorts[$name]) -ForegroundColor DarkYellow
        }
    }
}

function Invoke-MineNode {
    # POST /blocks/mine aguarda o push aos peers antes de responder: quando a
    # resposta chega, os peers alcançáveis já têm o bloco (determinismo da demo).
    # O timeout maior acomoda o PoW e os ~2s de push para peer morto (tolerância
    # a falhas do PeerClient: vira log de aviso, nunca exceção).
    param([int]$Port)
    $p = @{ Method = 'Post'; Uri = "http://localhost:$Port/blocks/mine"; TimeoutSec = 120 }
    Invoke-RestMethod @p
}

function Invoke-SyncNode {
    param([int]$Port)
    $p = @{ Method = 'Post'; Uri = "http://localhost:$Port/sync"; TimeoutSec = 60 }
    Invoke-RestMethod @p
}

function Add-DemoTransaction {
    param([int]$Port, [string]$From, [string]$To, [double]$Amount)
    $body = @{ from = $From; to = $To; amount = $Amount } | ConvertTo-Json
    $p = @{
        Method      = 'Post'
        Uri         = "http://localhost:$Port/transactions/"
        ContentType = 'application/json'
        Body        = $body
        TimeoutSec  = 30
    }
    Invoke-RestMethod @p
}

function Get-PendingTransactions {
    param([int]$Port)
    $p = @{ Uri = "http://localhost:$Port/transactions/pending"; TimeoutSec = 10 }
    Invoke-RestMethod @p
}

function Get-ChainBlocks {
    param([int]$Port)
    $p = @{ Uri = "http://localhost:$Port/chain"; TimeoutSec = 60 }
    Invoke-RestMethod @p
}

function Get-BlockByIndex {
    param([int]$Port, [int]$Index)
    $chain = Get-ChainBlocks -Port $Port
    foreach ($b in $chain) { if ($b.index -eq $Index) { return $b } }
    return $null
}

function Test-TransactionPending {
    param([int]$Port, [string]$From, [string]$To)
    $pending = Get-PendingTransactions -Port $Port
    if ($null -eq $pending) { return $false }
    foreach ($t in $pending) {
        if ($t.from -eq $From -and $t.to -eq $To) { return $true }
    }
    return $false
}

function Test-BlockHasTransaction {
    param($Block, [string]$From, [string]$To)
    if ($null -eq $Block) { return $false }
    if ($null -eq $Block.transactions) { return $false }
    foreach ($t in $Block.transactions) {
        if ($t.from -eq $From -and $t.to -eq $To) { return $true }
    }
    return $false
}

function Wait-Node {
    param([int]$Port, [int]$TimeoutSec = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $s = Get-NodeState -Port $Port
        if ($s.Reachable) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Wait-NodeDown {
    param([int]$Port, [int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $s = Get-NodeState -Port $Port
        if (-not $s.Reachable) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# ---------------------------------------------------------------------------
# Docker (partição de rede)
# ---------------------------------------------------------------------------

function Test-DockerDaemon {
    docker info 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Get-ClusterNetworkName {
    # O nome da rede do projeto compose (name: satusblockchain + satus-net) é
    # derivado, não chumbado: o próprio node1 declara qual rede ele usa.
    $out = docker inspect node1 --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $name = ($out | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { return $null }
    return $name
}

function Get-NetworkContainers {
    param([string]$Network)
    $out = docker network inspect $Network --format '{{range .Containers}}{{.Name}} {{end}}' 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    $joined = ($out | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($joined)) { return @() }
    $names = @()
    foreach ($part in ($joined -split '\s+')) {
        if (-not [string]::IsNullOrWhiteSpace($part)) { $names += $part }
    }
    return $names
}

function Test-ContainerInNetwork {
    param([string]$Network, [string]$Container)
    $members = Get-NetworkContainers -Network $Network
    foreach ($m in $members) { if ($m -eq $Container) { return $true } }
    return $false
}

function Disconnect-ContainerNetwork {
    param([string]$Network, [string]$Container)
    docker network disconnect $Network $Container 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Connect-ContainerNetwork {
    param([string]$Network, [string]$Container)
    docker network connect $Network $Container 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}
