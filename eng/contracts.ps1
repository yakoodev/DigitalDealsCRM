param(
    [Parameter(Mandatory = $true)]
    [string]$Command
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

switch ($Command) {
    'contracts:validate' {
        Invoke-Step -Title 'contracts:validate' -Action { dotnet run --project tools/DDCRM.Contracts.Cli -- validate }
    }
    'contracts:lint' {
        Invoke-Step -Title 'contracts:lint' -Action { dotnet run --project tools/DDCRM.Contracts.Cli -- lint }
    }
    'contracts:diff:external' {
        Invoke-Step -Title 'contracts:diff:external' -Action { dotnet run --project tools/DDCRM.Contracts.Cli -- diff external }
    }
    'contracts:diff:worker' {
        Invoke-Step -Title 'contracts:diff:worker' -Action { dotnet run --project tools/DDCRM.Contracts.Cli -- diff worker }
    }
    'contracts:test:services' {
        Invoke-Step -Title 'contracts:test:services (core)' -Action { dotnet test tests/DDCRM.Core.Api.Tests/DDCRM.Core.Api.Tests.csproj --configuration Release }
        Invoke-Step -Title 'contracts:test:services (iam)' -Action { dotnet test tests/DDCRM.Iam.Api.Tests/DDCRM.Iam.Api.Tests.csproj --configuration Release }
        Invoke-Step -Title 'contracts:test:services (route)' -Action { dotnet test tests/DDCRM.RouteRegistry.Api.Tests/DDCRM.RouteRegistry.Api.Tests.csproj --configuration Release }
        Invoke-Step -Title 'contracts:test:services (accounts-manager)' -Action { dotnet test tests/DDCRM.AccountsManager.Api.Tests/DDCRM.AccountsManager.Api.Tests.csproj --configuration Release }
        Invoke-Step -Title 'contracts:test:services (billing)' -Action { dotnet test tests/DDCRM.Billing.Api.Tests/DDCRM.Billing.Api.Tests.csproj --configuration Release }
        Invoke-Step -Title 'contracts:test:services (gateway)' -Action { dotnet test tests/DDCRM.Gateway.Api.Tests/DDCRM.Gateway.Api.Tests.csproj --configuration Release }
    }
    'contracts:test:worker' {
        Invoke-Step -Title 'contracts:test:worker' -Action { dotnet test tests/DDCRM.Worker.Api.Tests/DDCRM.Worker.Api.Tests.csproj --configuration Release }
    }
    'contracts:test:security' {
        Invoke-Step -Title 'contracts:test:security (all)' -Action { dotnet test DigitalDealsCRM.slnx --configuration Release --filter "Category=Security" }
    }
    'contracts:test:cors' {
        Invoke-Step -Title 'contracts:test:cors (core)' -Action { dotnet test tests/DDCRM.Core.Api.Tests/DDCRM.Core.Api.Tests.csproj --configuration Release --filter "Category=Cors" }
        Invoke-Step -Title 'contracts:test:cors (gateway)' -Action { dotnet test tests/DDCRM.Gateway.Api.Tests/DDCRM.Gateway.Api.Tests.csproj --configuration Release --filter "Category=Cors" }
    }
    default {
        Write-Error "Неизвестный entrypoint: $Command"
    }
}
