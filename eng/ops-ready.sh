#!/usr/bin/env bash
set -euo pipefail

skip_contracts="false"
if [[ "${1:-}" == "--skip-contracts" ]]; then
  skip_contracts="true"
fi

step() {
  local title="$1"
  echo "==> ${title}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

step "ops:docker:version"
docker compose version >/dev/null

declare -A states
declare -A healths

while IFS='|' read -r service state health; do
  states["$service"]="$state"
  healths["$service"]="$health"
done < <(docker compose ps --format '{{.Service}}|{{.State}}|{{.Health}}')

required_healthy=(
  "postgres"
  "core-api"
  "iam-api"
  "route-registry-api"
  "accounts-manager-api"
  "billing-api"
  "entitlement-api"
  "gateway-api"
  "worker-api"
)

required_running=(
  "ui"
)

step "ops:docker:status"
for service in "${required_healthy[@]}"; do
  if [[ -z "${states[$service]:-}" ]]; then
    echo "ERROR: service '${service}' is missing in docker compose ps output." >&2
    exit 1
  fi

  if [[ "${states[$service]}" != "running" ]]; then
    echo "ERROR: service '${service}' state is '${states[$service]}', expected 'running'." >&2
    exit 1
  fi

  if [[ "${healths[$service]:-}" != "healthy" ]]; then
    echo "ERROR: service '${service}' health is '${healths[$service]:-}', expected 'healthy'." >&2
    exit 1
  fi

  echo "ok - ${service} state=${states[$service]} health=${healths[$service]}"
done

for service in "${required_running[@]}"; do
  if [[ -z "${states[$service]:-}" ]]; then
    echo "ERROR: service '${service}' is missing in docker compose ps output." >&2
    exit 1
  fi

  if [[ "${states[$service]}" != "running" ]]; then
    echo "ERROR: service '${service}' state is '${states[$service]}', expected 'running'." >&2
    exit 1
  fi

  echo "ok - ${service} state=${states[$service]}"
done

probe() {
  local name="$1"
  local url="$2"
  local token="${3:-}"

  if [[ -n "${token}" ]]; then
    curl -fsS -H "X-Service-Token: ${token}" "${url}" >/dev/null
  else
    curl -fsS "${url}" >/dev/null
  fi

  echo "ok - ${name} => ${url}"
}

step "ops:smoke:health-probes"
probe "core-api" "http://localhost:5073/health"
probe "iam-api" "http://localhost:5120/health" "internal-token-a"
probe "route-registry-api" "http://localhost:5110/health" "internal-token-a"
probe "accounts-manager-api" "http://localhost:5137/health" "internal-token-a"
probe "billing-api" "http://localhost:5122/health" "internal-token-a"
probe "entitlement-api" "http://localhost:5221/health" "internal-token-a"
probe "gateway-api" "http://localhost:5068/health"
probe "worker-api" "http://localhost:5072/health" "worker-token-a"
probe "ui" "http://localhost:3000"

if [[ "${skip_contracts}" == "false" ]]; then
  step "ops:contracts:gates"
  contract_tasks=(
    "contracts:validate"
    "contracts:lint"
    "contracts:diff:external"
    "contracts:diff:worker"
    "contracts:test:services"
    "contracts:test:security"
    "contracts:test:cors"
    "contracts:test:worker"
  )

  for task in "${contract_tasks[@]}"; do
    echo "==> ${task}"
    ./eng/contracts.sh "${task}"
  done
else
  step "ops:contracts:gates (skipped)"
fi

echo "[ops-ready] dry-run completed successfully."
