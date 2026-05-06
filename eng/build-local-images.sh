#!/usr/bin/env bash
set -euo pipefail

target="${1:-all}"

if [[ "${target}" != "workers" && "${target}" != "integrations" && "${target}" != "all" ]]; then
  echo "ERROR: unsupported target '${target}'. Use: workers | integrations | all" >&2
  exit 1
fi

assert_dir() {
  local path="$1"
  local label="$2"
  if [[ ! -d "${path}" ]]; then
    echo "ERROR: repository '${label}' was not found: ${path}" >&2
    exit 1
  fi
}

assert_file() {
  local path="$1"
  local label="$2"
  if [[ ! -f "${path}" ]]; then
    echo "ERROR: file '${label}' was not found: ${path}" >&2
    exit 1
  fi
}

build_image() {
  local name="$1"
  local context="$2"
  local dockerfile="$3"
  local tag="$4"
  shift 4
  local build_args=("$@")

  echo "==> build: ${name} -> ${tag}"
  local cmd=(docker build -f "${dockerfile}" -t "${tag}")
  for build_arg in "${build_args[@]}"; do
    cmd+=(--build-arg "${build_arg}")
  done
  cmd+=("${context}")
  "${cmd[@]}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
WORKSPACE_ROOT="$(cd "${REPO_ROOT}/.." && pwd)"

build_workers() {
  echo "==> group: workers"
  local ddcrm_repo="${REPO_ROOT}"
  local funpay_repo="${WORKSPACE_ROOT}/DDCRM-FunPay"

  assert_dir "${ddcrm_repo}" "DigitalDealsCRM"
  assert_file "${ddcrm_repo}/docker/api.Dockerfile" "DigitalDealsCRM docker/api.Dockerfile"
  build_image \
    "DDCRM worker-api" \
    "${ddcrm_repo}" \
    "${ddcrm_repo}/docker/api.Dockerfile" \
    "ddcrm/worker-api:local" \
    "SERVICE_PROJECT=src/DDCRM.Worker.Api/DDCRM.Worker.Api.csproj"

  assert_dir "${funpay_repo}" "DDCRM-FunPay"
  assert_file "${funpay_repo}/Dockerfile" "DDCRM-FunPay Dockerfile"
  build_image \
    "DDCRM FunPay worker" \
    "${funpay_repo}" \
    "${funpay_repo}/Dockerfile" \
    "ddcrm/funpay-worker:local"
}

build_integrations() {
  echo "==> group: integrations"
  local stats_repo="${WORKSPACE_ROOT}/DigitalDealsStats"
  local steam_repo="${WORKSPACE_ROOT}/DDCRM-Steam"

  assert_dir "${stats_repo}" "DigitalDealsStats"
  assert_file "${stats_repo}/Dockerfile" "DigitalDealsStats Dockerfile"
  build_image \
    "DigitalDealsStats service" \
    "${stats_repo}" \
    "${stats_repo}/Dockerfile" \
    "ddcrm/marketstat:local"

  assert_dir "${steam_repo}" "DDCRM-Steam"
  assert_file "${steam_repo}/Dockerfile" "DDCRM-Steam Dockerfile"
  build_image \
    "DDCRM-Steam worker runtime" \
    "${steam_repo}" \
    "${steam_repo}/Dockerfile" \
    "ddcrm/steam-worker:local"
}

case "${target}" in
  workers)
    build_workers
    ;;
  integrations)
    build_integrations
    ;;
  all)
    build_workers
    build_integrations
    ;;
esac

echo "[build-local-images] completed (target=${target})."
