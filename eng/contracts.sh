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
    ;;
  contracts:test:security)
    run "contracts:test:security" dotnet test DigitalDealsCRM.slnx --configuration Release --filter "Category=Security"
    ;;
  contracts:test:cors)
    run "contracts:test:cors" dotnet test tests/DDCRM.Core.Api.Tests/DDCRM.Core.Api.Tests.csproj --configuration Release --filter "Category=Cors"
    ;;
  *)
    echo "Unknown entrypoint: ${command}" >&2
    exit 1
    ;;
esac
