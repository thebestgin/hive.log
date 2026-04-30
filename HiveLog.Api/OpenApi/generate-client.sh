#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
CONFIG_PATH="${SCRIPT_DIR}/nswag.json"
OUTPUT_DIR="${SCRIPT_DIR}/_output"

if [[ ! -f "${CONFIG_PATH}" ]]; then
  echo "NSwag configuration not found at ${CONFIG_PATH}" >&2
  exit 1
fi

if [[ ! -f "${SCRIPT_DIR}/swagger.backend-services.json" ]]; then
  echo "swagger.backend-services.json not found — start the server first to generate it" >&2
  exit 1
fi

mkdir -p "${OUTPUT_DIR}"

pushd "${REPO_ROOT}" >/dev/null

dotnet tool restore >/dev/null

dotnet nswag run "${CONFIG_PATH}"

popd >/dev/null

echo "NSwag client generated successfully in ${OUTPUT_DIR}"

# Copy generated client into HiveLog.Client project
# Used by all .NET backend services to send structured logs to HiveLog
DEST_DIR="${REPO_ROOT}/HiveLog.Client/Generated"
DEST_FILE="${DEST_DIR}/HiveLogBackendClient.cs"
SRC_FILE="${OUTPUT_DIR}/HiveLogBackendClient.cs"

mkdir -p "${DEST_DIR}"
cp -f "${SRC_FILE}" "${DEST_FILE}"
echo "Copied generated client to ${DEST_FILE}"
