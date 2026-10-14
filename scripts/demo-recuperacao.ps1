<#
.SYNOPSIS
    Etapa 8 (roteiro da etapa 10) — recuperação de um nó que ficou para trás.

.DESCRIPTION
    O caso feliz da sincronização manual (pull). Sequência (N = altura atual):

      1. baseline: os 3 nós em N, mesmo hash;
      2. o node3 cai (docker compose stop);
      3. a rede segue: node1 minera 2 blocos — o push ao node3 morto é recusado
         na hora (conexão recusada, falha rápida do PeerClient);
      4. node3 volta (docker compose start) e descobre que PERDEU TUDO: o estado
         é em memória, ele volta ao genesis — o passado não se reenvia por push;
      5. POST /sync no node3 → ele pergunta a cadeia dos peers e adota a válida
         mais longa (N+2), com a mesma impressão digital do node1;
      6. 3/3 convergidos e válidos.

    É o companion natural do demo-fork.ps1: este script cobre a falha e a
    recuperação, aquele cobre a divergência e a reconciliação.

.PARAMETER Fresh
    Reinicia os 3 containers antes de começar (estado zera no genesis).

.PARAMETER Hints
    Imprime, nos passos críticos, o que olhar na stack de observabilidade.

.EXAMPLE
    .\scripts\demo-recuperacao.ps1
    .\scripts\demo-recuperacao.ps1 -Fresh -Hints

.NOTES
    Compatível com Windows PowerShell 5.1. Só usa Docker CLI + Invoke-RestMethod.
#>
[CmdletBinding()]
param(
    [switch]$Fresh,
    [switch]$Hints
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib\Node.ps1"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    # ------------------------------------------------------------- PASSO 0
    Write-Step 0 "Pré-condições (Docker e stack)"
    if (-not (Test-DockerDaemon)) {
        Write-Problem "Docker daemon indisponível — abra o Docker Desktop e rode de novo."
        exit 1
    }
    $net = Get-ClusterNetworkName
    if (-not $net) {
        Write-Problem "Não achei a rede dos nós: a stack parece estar fora do ar. Rode: docker compose up -d"
        exit 1
    }
    Write-Ok "Docker no ar; rede do projeto: $net"

    if ($Fresh) {
        Write-Host "   -Fresh: reiniciando os 3 containers (o estado em memória zera no genesis)..."
        docker compose restart node1 node2 node3 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Problem "docker compose restart falhou — rode manualmente para ver o erro."
            exit 1
        }
    }

    foreach ($name in $NodePorts.Keys) {
        $ok = Wait-Node -Port $NodePorts[$name] -TimeoutSec 90
        if (-not $ok) {
            Write-Problem ("Nó {0} não respondeu na porta {1}. Stack no ar? docker compose up -d" -f $name, $NodePorts[$name])
            exit 1
        }
    }
    Write-Ok "Os 3 nós respondem nas portas 8080/8081/8082."

    # ------------------------------------------------------------- PASSO 1
    Write-Step 1 "Estado inicial — os três nós em harmonia"
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s2 = Get-NodeState -Port $NodePorts['node2']
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Show-Cluster "estado por nó:"
    Assert-True ($s1.Length -eq $s2.Length -and $s1.Length -eq $s3.Length) "os 3 nós têm o MESMO comprimento"
    Assert-True ($s1.Last -eq $s2.Last -and $s1.Last -eq $s3.Last) "os 3 nós terminam no MESMO hash"
    Assert-True ($s1.Valid -and $s2.Valid -and $s3.Valid) "as 3 cadeias são válidas"
    if ($script:DemoFailures -gt 0) {
        Write-Problem "Baseline inconsistente — rode com -Fresh para começar do genesis."
        exit 1
    }
    $n = $s1.Length
    Write-Host ("   N = {0} blocos: a demo é relativa e funciona em qualquer altura da cadeia." -f $n)

    # ------------------------------------------------------------- PASSO 2
    Write-Step 2 "O node3 cai (docker compose stop)"
    docker compose stop node3 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Problem "docker compose stop node3 falhou."; exit 1 }
    if (-not (Wait-NodeDown -Port $NodePorts['node3'] -TimeoutSec 30)) {
        Write-Problem "node3 continuou respondendo depois do stop."
        exit 1
    }
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Assert-True (-not $s3.Reachable) "node3 inacessível — a falha está acontecendo"

    # ------------------------------------------------------------- PASSO 3
    Write-Step 3 "A rede segue sem ele — node1 minera 2 blocos"
    $mineA = Invoke-MineNode -Port $NodePorts['node1']
    $mineB = Invoke-MineNode -Port $NodePorts['node1']
    Write-Host ("   node1 minou os índices {0} e {1} (o push ao node3 morto é recusado na hora)." -f $mineA.index, $mineB.index)
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s2 = Get-NodeState -Port $NodePorts['node2']
    Show-Cluster "estado com o node3 fora:"
    Assert-Equal ($n + 2) $s1.Length "node1 avançou para N+2"
    Assert-Equal ($n + 2) $s2.Length "node2 acompanhou pelo push (a rede de pé funciona)"
    Write-Hint "Ninguém reenvia o passado: o push carrega só o bloco novo. A história perdida só volta por PULL (POST /sync)."
    if ($Hints) {
        Write-Hint "Observabilidade: no Prometheus, o alvo do node3 fica health=down; no Grafana, satus_chain_length mostra 2 curvas em N+2 e o node3 ausente."
    }

    # ------------------------------------------------------------- PASSO 4
    Write-Step 4 "O node3 volta (docker compose start) — e perdeu tudo"
    docker compose start node3 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Problem "docker compose start node3 falhou."; exit 1 }
    if (-not (Wait-Node -Port $NodePorts['node3'] -TimeoutSec 90)) {
        Write-Problem "node3 não voltou a responder após o start."
        exit 1
    }
    $s3 = Get-NodeState -Port $NodePorts['node3']
    $s1 = Get-NodeState -Port $NodePorts['node1']
    Show-Cluster "estado após religar o node3:"
    Assert-True ($s3.Valid) "a cadeia do node3 é válida (é o genesis)"
    Assert-Equal 1 $s3.Length "node3 voltou ao GENESIS: o estado é em memória e se foi"
    Assert-True ($s3.Length -lt $s1.Length) "node3 está atrás de node1 (N < N+2) — precisa sincronizar"
    Write-Hint "Volta no tempo sem corrupção: o node3 tem uma cadeia perfeita, só que de 1 bloco. Quem decide quando se atualizar é ele."

    # ------------------------------------------------------------- PASSO 5
    Write-Step 5 "Recuperação — node3 faz POST /sync e adota a cadeia dos peers"
    $sync = Invoke-SyncNode -Port $NodePorts['node3']
    Write-Host ("   relatório: previous={0} length={1} adopted={2}" -f $sync.previousLength, $sync.length, ($sync.adopted | ConvertTo-Json -Compress))
    Assert-True ($null -ne $sync.adopted) "adopted não nulo: node3 adotou a cadeia do peer $($sync.adopted.from)"
    Assert-Equal ($n + 2) $sync.length "node3 foi de 1 bloco direto para N+2"
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Assert-True ($s1.Last -eq $s3.Last) "node3 terminou no MESMO hash do node1 — recuperação completa"
    if ($Hints) {
        Write-Hint "Jaeger (serviço node3): span 'sync' com satus.sync.outcome=adopted; Loki: {compose_project=`"satusblockchain`"} |= `"Cadeia adotada`"."
    }

    # ------------------------------------------------------------- PASSO 6
    Write-Step 6 "Estado final — 3/3 novamente em consenso"
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s2 = Get-NodeState -Port $NodePorts['node2']
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Show-Cluster "estado final:"
    Assert-Equal ($n + 2) $s1.Length "node1 com N+2"
    Assert-Equal ($n + 2) $s2.Length "node2 com N+2"
    Assert-Equal ($n + 2) $s3.Length "node3 com N+2"
    Assert-True ($s1.Last -eq $s2.Last -and $s1.Last -eq $s3.Last) "MESMO último hash nos 3 nós — CONVERGÊNCIA"
    Assert-True ($s1.Valid -and $s2.Valid -and $s3.Valid) "as 3 cadeias passam na validação"
    Write-Host @"
   • Queda: o node3 morreu e a rede continuou minerando sem ele.
   • Retorno: ele voltou ao genesis (memória volátil) — nada se corrige sozinho.
   • Recuperação: um único POST /sync reconstruiu a história inteira, porque o
     sync é PULL e a cadeia validável se explica por inteiro.
"@ -ForegroundColor Yellow

    if (Get-DemoSummary -DemoName "DEMO DE RECUPERAÇÃO (etapa 8)") { exit 0 } else { exit 1 }
}
finally {
    Pop-Location
}
