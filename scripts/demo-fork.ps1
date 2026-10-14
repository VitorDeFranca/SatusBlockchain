<#
.SYNOPSIS
    Etapa 10 — demonstração de FORK forçado e convergência (longest chain rule).

.DESCRIPTION
    Provoca um fork de verdade em 3 nós de verdade e resolve pela regra da cadeia
    mais longa. Sequência completa (N = comprimento atual da cadeia):

      1. baseline: os 3 nós em N, mesmo hash (sem fork);
      2. partição: o node2 sai da rede Docker (docker network disconnect);
      3. node1 minera 2 blocos sem node2 (o push ao peer mudo estoura o timeout
         de 2s e vira log de aviso — tolerância a falhas);
      4. reconexão: node2 volta com N blocos, defasado e com outro topo;
      5. T2 nasce na mempool do node2;
      6. node2 minera SEM sincronizar → bloco no MESMO índice N com hash
         diferente do node1 = fork forçado (o push dele leva 409 nos peers);
      7. node2 POST /sync → adota a cadeia de N+2; o bloco perdedor vira órfão e
         a T2 volta para a mempool;
      8. node2 minera de novo → T2 re-confirmada no topo → 3/3 convergidos.

    Por que a partição é por rede Docker e não por `docker pause`: um nó pausado
    mantém o socket de escuta vivo no kernel — a requisição de push fica na fila
    de aceitação e é ENTREGUE quando o processo volta, ou seja, o nó pausado se
    atualiza sozinho e o fork nunca acontece. O disconnect derruba o endpoint de
    verdade: não há fila nenhuma para sobreviver à reconexão.

.PARAMETER Fresh
    Reinicia os 3 containers antes de começar (docker compose restart). O estado
    é em memória: os nós voltam ao genesis — base limpa e determinística.

.PARAMETER Hints
    Imprime, nos passos críticos, o que olhar na stack de observabilidade
    (Jaeger/Prometheus/Grafana/Loki), quando ela estiver ativa.

.EXAMPLE
    .\scripts\demo-fork.ps1
    .\scripts\demo-fork.ps1 -Fresh -Hints

.NOTES
    Compatível com Windows PowerShell 5.1. Só usa Docker CLI + Invoke-RestMethod.
    Se a execução for interrompida no meio, o finally (e o autocurar do passo 0)
    religa o node2 à rede — a demo nunca deixa a partição pendurada.
#>
[CmdletBinding()]
param(
    [switch]$Fresh,
    [switch]$Hints
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib\Node.ps1"

$repoRoot = Split-Path -Parent $PSScriptRoot
$net = $null
Push-Location $repoRoot

try {
    # ------------------------------------------------------------- PASSO 0
    Write-Step 0 "Pré-condições (Docker, stack e rede dos nós)"
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

    # Autocura: uma execução interrompida pode ter deixado o node2 fora da rede.
    if (-not (Test-ContainerInNetwork -Network $net -Container 'node2')) {
        Write-Host "   node2 fora da rede — reconectando (autocura de execução anterior)..."
        Connect-ContainerNetwork -Network $net -Container 'node2' | Out-Null
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
    Assert-True ($s1.Last -eq $s2.Last -and $s1.Last -eq $s3.Last) "os 3 nós terminam no MESMO hash (sem fork)"
    Assert-True ($s1.Valid -and $s2.Valid -and $s3.Valid) "as 3 cadeias são válidas"
    if ($script:DemoFailures -gt 0) {
        Write-Problem "Baseline inconsistente — rode com -Fresh para começar do genesis."
        exit 1
    }
    $n = $s1.Length
    Write-Host ("   N = {0} blocos: a demo é relativa e funciona em qualquer altura da cadeia." -f $n)

    # ------------------------------------------------------------- PASSO 2
    Write-Step 2 "Partição de rede — o node2 sai da rede Docker"
    if (-not (Disconnect-ContainerNetwork -Network $net -Container 'node2')) {
        Write-Problem "docker network disconnect falhou — rode manualmente para ver o erro."
        exit 1
    }
    Assert-True (-not (Test-ContainerInNetwork -Network $net -Container 'node2')) "node2 fora da rede do compose"
    Write-Hint "A partir daqui o node2 está vivo, mas sem rota: o push do node1 estoura o timeout de 2s do HttpClient e vira log 'inacessível' — falha isolada, nunca exceção."
    if ($Hints) {
        Write-Hint "Observabilidade: docker logs node1 -f mostra 'peer ... inacessível'; no Grafana, sum by(outcome)(satus_peer_push_total) registra o refused."
    }

    # ------------------------------------------------------------- PASSO 3
    Write-Step 3 "O lado isolado segue em frente — node1 minera 2 blocos"
    $t0 = Get-Date
    $mineA = Invoke-MineNode -Port $NodePorts['node1']
    $mineB = Invoke-MineNode -Port $NodePorts['node1']
    $dt = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
    Write-Host ("   node1 minou os índices {0} e {1} em {2}s (boa parte disso é o push esperando o node2 mudo)." -f $mineA.index, $mineB.index, $dt)
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Show-Cluster "estado após a mineração isolada:"
    Assert-Equal ($n + 2) $s1.Length "node1 avançou para N+2"
    Assert-Equal ($n + 2) $s3.Length "node3 recebeu os 2 blocos pelo push (não estava particionado)"
    Assert-True (-not (Test-ContainerInNetwork -Network $net -Container 'node2')) "node2 segue fora da rede (nada foi entregue a ele)"

    # ------------------------------------------------------------- PASSO 4
    Write-Step 4 "Reconexão — o node2 volta a um mundo que andou sem ele"
    Connect-ContainerNetwork -Network $net -Container 'node2' | Out-Null
    if (-not (Wait-Node -Port $NodePorts['node2'] -TimeoutSec 60)) {
        Write-Problem "node2 não voltou a responder após reconectar à rede."
        exit 1
    }
    $s2 = Get-NodeState -Port $NodePorts['node2']
    $s1 = Get-NodeState -Port $NodePorts['node1']
    Show-Cluster "estado após reconectar:"
    Assert-Equal $n $s2.Length "node2 voltou defasado: continua com N blocos"
    Assert-True ($s2.Last -ne $s1.Last) "topos divergentes: a cadeia do node2 já é outra história"

    # ------------------------------------------------------------- PASSO 5
    Write-Step 5 "Uma transação nasce no nó para trás (T2: carol → dave)"
    $sufixo = Get-Date -Format 'HHmmss'
    $txFrom = 'carol'
    $txTo = "dave-$sufixo"
    Add-DemoTransaction -Port $NodePorts['node2'] -From $txFrom -To $txTo -Amount 7 | Out-Null
    Assert-True (Test-TransactionPending -Port $NodePorts['node2'] -From $txFrom -To $txTo) "T2 pendente na mempool local do node2"
    Write-Hint "O gossip de 1 hop repassa T2 aos outros nós (mempool é local a cada nó) — mas ninguém minera até depois do sync, então T2 não escapa para o lado vencedor."

    # ------------------------------------------------------------- PASSO 6
    Write-Step 6 "FORK — o node2 minera por conta própria, sem sincronizar"
    $forkBlock = Invoke-MineNode -Port $NodePorts['node2']
    Assert-Equal $n $forkBlock.index "o bloco do node2 nasceu no índice N — o MESMO índice do bloco minerado pelo node1"
    $blkNode1 = Get-BlockByIndex -Port $NodePorts['node1'] -Index $n
    $blkNode2 = Get-BlockByIndex -Port $NodePorts['node2'] -Index $n
    Assert-True ($null -ne $blkNode1 -and $null -ne $blkNode2) "o índice N existe nas duas cadeias"
    Assert-True ($blkNode1.hash -ne $blkNode2.hash) "hashes diferentes no mesmo índice = FORK FORÇADO"
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s2 = Get-NodeState -Port $NodePorts['node2']
    Show-Cluster "estado com o fork existindo:"
    Assert-Equal ($n + 2) $s1.Length "node1 segue com N+2 (o lado mais longo)"
    Assert-Equal ($n + 1) $s2.Length "node2 criou N+1 (está mais curto — e essa é a regra do jogo)"
    Assert-True ($s1.Last -ne $s2.Last) "as duas versões da história divergem no topo"
    Write-Hint "O push do bloco do node2 levou 409 em node1 e node3: índice fora de sequência. Sem broadcast automático, quem decide corrigir é o próprio nó."
    if ($Hints) {
        Write-Hint "Jaeger (localhost:16686, serviço node2): trace do mine com spans de /blocks/receive nos dois peers. Loki: {compose_project=`"satusblockchain`"} |= `"409`"."
    }

    # ------------------------------------------------------------- PASSO 7
    Write-Step 7 "Resolução — node2 pergunta aos peers (POST /sync) e adota a mais longa"
    $sync = Invoke-SyncNode -Port $NodePorts['node2']
    Write-Host ("   relatório: previous={0} length={1} orphans={2} adopted={3}" -f $sync.previousLength, $sync.length, $sync.orphansBackToMempool, ($sync.adopted | ConvertTo-Json -Compress))
    Assert-True ($null -ne $sync.adopted) "adopted não nulo: node2 adotou a cadeia do peer $($sync.adopted.from)"
    Assert-Equal ($n + 2) $sync.length "node2 foi para N+2 — a altura da cadeia vencedora"
    Assert-Equal 1 $sync.orphansBackToMempool "exatamente a transação órfã (T2) voltou à mempool"
    Assert-True (Test-TransactionPending -Port $NodePorts['node2'] -From $txFrom -To $txTo) "T2 pendente de novo: o bloco que a continha foi descartado"
    Show-Cluster "estado após o sync:"
    if ($Hints) {
        Write-Hint "Jaeger: span 'sync' do node2 com tags satus.sync.outcome=adopted e satus.sync.orphans=1. Grafana: painel satus_chain_length reencontra os 3 nós no mesmo valor."
    }

    # ------------------------------------------------------------- PASSO 8
    Write-Step 8 "Fidelidade à origem — o node2 re-minera; a órfã T2 sobrevive no topo"
    $lastBlock = Invoke-MineNode -Port $NodePorts['node2']
    Assert-Equal ($n + 2) $lastBlock.index "o novo bloco fecha a cadeia em N+2 (vira N+3 blocos)"
    Assert-True (Test-BlockHasTransaction -Block $lastBlock -From $txFrom -To $txTo) "T2 está dentro do novo bloco (a órfã virou confirmada)"
    $s1 = Get-NodeState -Port $NodePorts['node1']
    $s2 = Get-NodeState -Port $NodePorts['node2']
    $s3 = Get-NodeState -Port $NodePorts['node3']
    Show-Cluster "estado final:"
    Assert-Equal ($n + 3) $s1.Length "node1 com N+3"
    Assert-Equal ($n + 3) $s2.Length "node2 com N+3"
    Assert-Equal ($n + 3) $s3.Length "node3 com N+3"
    Assert-True ($s1.Last -eq $s2.Last -and $s1.Last -eq $s3.Last) "MESMO último hash nos 3 nós — CONVERGÊNCIA"
    Assert-True ($s1.Valid -and $s2.Valid -and $s3.Valid) "as 3 cadeias passam na validação"
    Assert-True (-not (Test-TransactionPending -Port $NodePorts['node1'] -From $txFrom -To $txTo)) "mempool do node1 sem T2 (poda ao receber o bloco)"
    Assert-True (-not (Test-TransactionPending -Port $NodePorts['node2'] -From $txFrom -To $txTo)) "mempool do node2 sem T2 (virou bloco dela)"
    Assert-True (-not (Test-TransactionPending -Port $NodePorts['node3'] -From $txFrom -To $txTo)) "mempool do node3 sem T2 (poda ao receber o bloco)"

    # ------------------------------------------------------------- PASSO 9
    Write-Step 9 "A história, em resumo"
    Write-Host @"
   • Partição: o node2 saiu da rede e ficou com a versão antiga da cadeia.
   • Fork: ao voltar, ele minerou ANTES de sincronizar e criou um bloco no
     mesmo índice N com hash diferente — duas versões da história, ainda sem
     juiz (empate não adota: precisou de diferença estrita de altura).
   • Convergência: o POST /sync fez o node2 adotar a cadeia de N+2; o bloco
     perdedor virou órfão, a T2 voltou à mempool e foi re-minerada no topo.
   • Lição: nada disso precisou de coordenador — só regra local + regra da
     cadeia mais longa.
"@ -ForegroundColor Yellow
    if ($Hints) {
        Write-Host @"
   Onde olhar (stack de observabilidade, se ativa):
   • Grafana  http://localhost:3000/d/satus-nos — satus_chain_length (a
     divergência e o reencontro), sum by(outcome)(satus_peer_push_total).
   • Jaeger   http://localhost:16686 — traces de 'mine' e 'sync' cruzando nós.
   • Loki     Grafana → Explore → {compose_project="satusblockchain"} —
     'Cadeia adotada do peer', 'inacessível', '409'.
"@ -ForegroundColor DarkGray
    }

    if (Get-DemoSummary -DemoName "DEMO DE FORK (etapa 10)") { exit 0 } else { exit 1 }
}
finally {
    # Reparo garantido: a demo nunca deixa o node2 fora da rede, nem em erro.
    if ($net) {
        if (-not (Test-ContainerInNetwork -Network $net -Container 'node2')) {
            Write-Host ""
            Write-Host "   [finally] religando o node2 à rede do compose..." -ForegroundColor DarkYellow
            Connect-ContainerNetwork -Network $net -Container 'node2' | Out-Null
        }
    }
    Pop-Location
}
