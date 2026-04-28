param(
    [switch]$SkipContracts
)

$ErrorActionPreference = 'Stop'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "==> $Title"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    Invoke-Step -Title 'ops:docker:version' -Action {
        docker compose version | Out-Null
    }

    $requiredServices = @(
        @{ Name = 'postgres'; RequireHealth = $true },
        @{ Name = 'core-api'; RequireHealth = $true },
        @{ Name = 'iam-api'; RequireHealth = $true },
        @{ Name = 'route-registry-api'; RequireHealth = $true },
        @{ Name = 'accounts-manager-api'; RequireHealth = $true },
        @{ Name = 'billing-api'; RequireHealth = $true },
        @{ Name = 'entitlement-api'; RequireHealth = $true },
        @{ Name = 'gateway-api'; RequireHealth = $true },
        @{ Name = 'worker-api'; RequireHealth = $true },
        @{ Name = 'ui'; RequireHealth = $false }
    )

    Invoke-Step -Title 'ops:docker:status' -Action {
        $rows = docker compose ps --format "{{.Service}}|{{.State}}|{{.Health}}"
        if (-not $rows) {
            throw "docker compose ps вернул пустой список сервисов."
        }

        $statusByService = @{}
        foreach ($row in $rows) {
            $parts = $row -split '\|', 3
            if ($parts.Length -lt 3) {
                continue
            }

            $service = $parts[0].Trim()
            $state = $parts[1].Trim()
            $health = $parts[2].Trim()
            $statusByService[$service] = @{
                State = $state
                Health = $health
            }
        }

        foreach ($serviceConfig in $requiredServices) {
            $name = $serviceConfig.Name
            $requireHealth = [bool]$serviceConfig.RequireHealth

            if (-not $statusByService.ContainsKey($name)) {
                throw "Сервис '$name' отсутствует в docker compose ps."
            }

            $state = $statusByService[$name].State
            $health = $statusByService[$name].Health

            if ($state -ne 'running') {
                throw "Сервис '$name' не в состоянии running (текущее: '$state')."
            }

            if ($requireHealth -and $health -ne 'healthy') {
                throw "Сервис '$name' не healthy (текущее: '$health')."
            }

            if (-not $requireHealth) {
                Write-Host "ok - $name state=$state"
                continue
            }

            Write-Host "ok - $name state=$state health=$health"
        }
    }

    $healthChecks = @(
        @{ Name = 'core-api'; Url = 'http://localhost:5073/health'; Token = $null },
        @{ Name = 'iam-api'; Url = 'http://localhost:5120/health'; Token = 'internal-token-a' },
        @{ Name = 'route-registry-api'; Url = 'http://localhost:5110/health'; Token = 'internal-token-a' },
        @{ Name = 'accounts-manager-api'; Url = 'http://localhost:5137/health'; Token = 'internal-token-a' },
        @{ Name = 'billing-api'; Url = 'http://localhost:5122/health'; Token = 'internal-token-a' },
        @{ Name = 'entitlement-api'; Url = 'http://localhost:5221/health'; Token = 'internal-token-a' },
        @{ Name = 'gateway-api'; Url = 'http://localhost:5068/health'; Token = $null },
        @{ Name = 'worker-api'; Url = 'http://localhost:5072/health'; Token = 'worker-token-a' },
        @{ Name = 'ui'; Url = 'http://localhost:3400'; Token = $null }
    )

    Invoke-Step -Title 'ops:smoke:health-probes' -Action {
        foreach ($check in $healthChecks) {
            $headers = @{}
            if ($check.Token) {
                $headers['X-Service-Token'] = $check.Token
            }

            try {
                $response = Invoke-WebRequest -Uri $check.Url -Headers $headers -UseBasicParsing -TimeoutSec 15
                if ($response.StatusCode -ne 200) {
                    throw "HTTP статус '$($response.StatusCode)'"
                }
                Write-Host "ok - $($check.Name) => $($check.Url)"
            }
            catch {
                throw "Health probe failed for '$($check.Name)' ($($check.Url)): $($_.Exception.Message)"
            }
        }
    }

    if (-not $SkipContracts) {
        $contractTasks = @(
            'contracts:validate',
            'contracts:lint',
            'contracts:diff:external',
            'contracts:diff:worker',
            'contracts:test:services',
            'contracts:test:security',
            'contracts:test:cors',
            'contracts:test:worker'
        )

        Invoke-Step -Title 'ops:contracts:gates' -Action {
            foreach ($task in $contractTasks) {
                Write-Host "==> $task"
                ./eng/contracts.ps1 $task
                if ($LASTEXITCODE -ne 0) {
                    exit $LASTEXITCODE
                }
            }
        }
    }
    else {
        Write-Host "==> ops:contracts:gates (skipped)"
    }

    Write-Host "[ops-ready] dry-run completed successfully."
}
finally {
    Pop-Location
}
