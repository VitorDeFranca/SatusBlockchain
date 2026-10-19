<#
.SYNOPSIS
    Etapa 10: acrescenta um nó à rede com o sistema rodando.

.DESCRIPTION
    Cria um container novo a partir do molde `node-extra` do docker-compose.yml (mesma
    imagem, mesma rede, mesma dificuldade), com NODE_ID e ADVERTISE_URL próprios.

    Ao subir, o nó:
      1. se anuncia à semente (POST /peers) — ela confere o genesis dele chamando-o de volta;
      2. descobre os demais nós pela resposta do anúncio e se anuncia a cada um;
      3. sincroniza a cadeia (POST /sync interno): nasce só com o genesis e puxa a história.

    Pré-requisito: os nós base no ar (`docker compose up -d`).

.EXAMPLE
    .\scripts\add-node.ps1 node4 -HostPort 8083

.EXAMPLE
    .\scripts\add-node.ps1 node5 -Seed http://node2:8080
#>
param(
    # Nome do container = NODE_ID = host na rede do Docker (http://<Name>:8080).
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[a-z][a-z0-9-]*$')]
    [string] $Name,

    # Porta publicada no host (opcional). Sem ela o nó só é acessível de dentro da rede.
    [int] $HostPort = 0,

    # Nó já existente pelo qual o novo entra na rede (endereço INTERNO do Docker).
    [string] $Seed = 'http://node1:8080'
)

$compose = Join-Path $PSScriptRoot '..\docker-compose.yml'

$dockerArgs = @(
    'compose', '-f', $compose, 'run', '--detach', '--name', $Name,
    '-e', "NODE_ID=$Name",
    '-e', "ADVERTISE_URL=http://${Name}:8080",
    '-e', "PEERS=$Seed"
)

if ($HostPort -gt 0) {
    $dockerArgs += @('--publish', "${HostPort}:8080")
}

# Com a stack de observabilidade no ar, o nó novo também exporta traces para o Jaeger
# (os nós base recebem essa variável do docker-compose.observability.yml).
$jaeger = docker ps --filter 'name=^jaeger$' --format '{{.Names}}'
if ($jaeger) {
    $dockerArgs += @('-e', 'OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317')
}

$dockerArgs += 'node-extra'

& docker @dockerArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao criar o nó '$Name' (docker saiu com código $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Nó '$Name' criado: ele se anuncia a $Seed e sincroniza a cadeia sozinho."
Write-Host "  Dentro da rede : http://${Name}:8080"
if ($HostPort -gt 0) {
    Write-Host "  Do host        : http://localhost:$HostPort"
}
Write-Host "  Log da entrada : docker logs -f $Name"
Write-Host "  Lista da rede  : curl http://localhost:8080/peers   (node1 já o conhece)"
Write-Host "  Remover        : .\scripts\remove-node.ps1 $Name   (-Crash simula uma queda)"
