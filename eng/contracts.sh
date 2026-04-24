#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: ./eng/contracts.sh <entrypoint>" >&2
  exit 1
fi

command="$1"

run() {
  local title="$1"
  shift
  echo "==> ${title}"
  "$@"
}

case "${command}" in
  contracts:validate)
    run "contracts:validate" dotnet run --project tools/DDCRM.Contracts.Cli -- validate
    ;;
  contracts:lint)
    run "contracts:lint" dotnet run --project tools/DDCRM.Contracts.Cli -- lint
    ;;
  contracts:diff:external)
    run "contracts:diff:external" dotnet run --project tools/DDCRM.Contracts.Cli -- diff external
    ;;
  contracts:diff:worker)
    run "contracts:diff:worker" dotnet run --project tools/DDCRM.Contracts.Cli -- diff worker
    ;;
  contracts:test:services)
    run "contracts:test:services (core)" dotnet test tests/DDCRM.Core.Api.Tests/DDCRM.Core.Api.Tests.csproj --configuration Release
    run "contracts:test:services (iam)" dotnet test tests/DDCRM.Iam.Api.Tests/DDCRM.Iam.Api.Tests.csproj --configuration Release
    run "contracts:test:services (route)" dotnet test tests/DDCRM.RouteRegistry.Api.Tests/DDCRM.RouteRegistry.Api.Tests.csproj --configuration Release
    run "contracts:test:services (accounts-manager)" dotnet test tests/DDCRM.AccountsManager.Api.Tests/DDCRM.AccountsManager.Api.Tests.csproj --configuration Release
    run "contracts:test:services (billing)" dotnet test tests/DDCRM.Billing.Api.Tests/DDCRM.Billing.Api.Tests.csproj --configuration Release
    run "contracts:test:services (entitlement)" dotnet test tests/DDCRM.Entitlement.Api.Tests/DDCRM.Entitlement.Api.Tests.csproj --configuration Release
    run "contracts:test:services (gateway)" dotnet test tests/DDCRM.Gateway.Api.Tests/DDCRM.Gateway.Api.Tests.csproj --configuration Release
    ;;
  contracts:test:worker)
    run "contracts:test:worker" dotnet test tests/DDCRM.Worker.Api.Tests/DDCRM.Worker.Api.Tests.csproj --configuration Release
    ;;
  contracts:test:security)
    run "contracts:test:security" dotnet test DigitalDealsCRM.slnx --configuration Release --filter "Category=Security"
    ;;
  contracts:test:cors)
    run "contracts:test:cors (core)" dotnet test tests/DDCRM.Core.Api.Tests/DDCRM.Core.Api.Tests.csproj --configuration Release --filter "Category=Cors"
    run "contracts:test:cors (gateway)" dotnet test tests/DDCRM.Gateway.Api.Tests/DDCRM.Gateway.Api.Tests.csproj --configuration Release --filter "Category=Cors"
    ;;
  *)
    echo "Unknown entrypoint: ${command}" >&2
    exit 1
    ;;
esac
