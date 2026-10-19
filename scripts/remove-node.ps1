<#
.SYNOPSIS
    Etapa 10: tira um nó da rede com o sistema rodando.

.DESCRIPTION
    Dois jeitos de sair, para demonstrar os dois caminhos da membership:

      (padrão)  docker stop  -> SIGTERM -> desligamento gracioso: o nó manda
                DELETE /peers aos peers e sai da lista de todos na hora.

      -Crash    docker kill  -> o processo morre sem despedida: os outros nós só
                percebem pelas falhas — o nó sai da lista de cada um após 3 chamadas
                seguidas sem resposta (cada push de bloco ou transação conta uma).

    Nós criados pelo add-node também têm o container apagado. Os nós base
    (node1..node3) só param — eles são sementes do compose: religue com
    `docker compose start <nome>`.

.EXAMPLE
    .\scripts\remove-node.ps1 node4

.EXAMPLE
    .\scripts\remove-node.ps1 node4 -Crash
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Name,

    # Simula uma queda (docker kill) em vez de um desligamento gracioso (docker stop).
    [switch] $Crash
)

$baseNodes = @('node1', 'node2', 'node3')

if ($Crash) {
    docker kill $Name | Out-Null
} else {
    docker stop $Name | Out-Null
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Não foi possível parar '$Name' (docker saiu com código $LASTEXITCODE)."
    exit $LASTEXITCODE
}

if ($baseNodes -contains $Name) {
    Write-Host "'$Name' parado. É um nó base (semente): religue com 'docker compose start $Name'."
    exit 0
}

docker rm $Name | Out-Null

if ($Crash) {
    Write-Host "'$Name' caiu sem se despedir: os outros o tiram da lista após 3 chamadas sem resposta."
} else {
    Write-Host "'$Name' se despediu e saiu da lista dos outros nós."
}
Write-Host "  Confira: curl http://localhost:8080/peers"
