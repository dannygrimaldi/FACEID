#!/usr/bin/env bash
set -euo pipefail

# Creates (or recreates) the Qdrant collection used by Face API RGB+IR mode.
# Default collection name requested by project: FacesTwo
#
# Usage examples:
#   ./create-qdrant-facestwo.sh
#   ./create-qdrant-facestwo.sh --recreate
#   QDRANT_URL=http://127.0.0.1:6333 QDRANT_API_KEY=xxx ./create-qdrant-facestwo.sh
#   COLLECTION_NAME=FacesTwo VECTOR_SIZE=512 ./create-qdrant-facestwo.sh

QDRANT_URL="${QDRANT_URL:-http://127.0.0.1:6333}"
QDRANT_API_KEY="${QDRANT_API_KEY:-}"
COLLECTION_NAME="${COLLECTION_NAME:-FacesTwo}"
VECTOR_SIZE="${VECTOR_SIZE:-512}"
RECREATE_COLLECTION="${RECREATE_COLLECTION:-false}"
ON_DISK_PAYLOAD="${ON_DISK_PAYLOAD:-true}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --recreate)
      RECREATE_COLLECTION=true
      shift
      ;;
    --name)
      COLLECTION_NAME="${2:-}"
      shift 2
      ;;
    --url)
      QDRANT_URL="${2:-}"
      shift 2
      ;;
    --vector-size)
      VECTOR_SIZE="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if ! [[ "$VECTOR_SIZE" =~ ^[0-9]+$ ]] || [[ "$VECTOR_SIZE" -le 0 ]]; then
  echo "VECTOR_SIZE must be a positive integer. Current value: $VECTOR_SIZE" >&2
  exit 1
fi

api_headers=()
if [[ -n "$QDRANT_API_KEY" ]]; then
  api_headers=(-H "api-key: ${QDRANT_API_KEY}")
fi

request() {
  local method="$1"
  local path="$2"
  local data="${3:-}"

  if [[ -n "$data" ]]; then
    curl -fsS -X "$method" "${QDRANT_URL}${path}" \
      "${api_headers[@]}" \
      -H "Content-Type: application/json" \
      --data-raw "$data"
  else
    curl -fsS -X "$method" "${QDRANT_URL}${path}" \
      "${api_headers[@]}"
  fi
}

collection_exists() {
  curl -fsS "${QDRANT_URL}/collections/${COLLECTION_NAME}" "${api_headers[@]}" >/dev/null 2>&1
}

create_payload_index() {
  local field_name="$1"
  local field_schema="$2"

  request PUT "/collections/${COLLECTION_NAME}/index?wait=true" \
    "{
      \"field_name\": \"${field_name}\",
      \"field_schema\": \"${field_schema}\"
    }" >/dev/null

  echo "  - payload index ready: ${field_name} (${field_schema})"
}

echo "Checking Qdrant availability at: ${QDRANT_URL}"
request GET "/collections" >/dev/null

if collection_exists; then
  if [[ "${RECREATE_COLLECTION}" == "true" ]]; then
    echo "Collection '${COLLECTION_NAME}' exists. Recreating..."
    request DELETE "/collections/${COLLECTION_NAME}" >/dev/null
  else
    echo "Collection '${COLLECTION_NAME}' already exists. Skipping create."
  fi
fi

if ! collection_exists; then
  echo "Creating collection '${COLLECTION_NAME}' (size=${VECTOR_SIZE}, distance=Cosine)..."
  request PUT "/collections/${COLLECTION_NAME}" \
    "{
      \"vectors\": {
        \"size\": ${VECTOR_SIZE},
        \"distance\": \"Cosine\"
      },
      \"on_disk_payload\": ${ON_DISK_PAYLOAD}
    }" >/dev/null
fi

echo "Creating payload indexes..."
create_payload_index "person_id" "keyword"
create_payload_index "employeeId" "keyword"
create_payload_index "type" "keyword"
create_payload_index "modality" "keyword"
create_payload_index "updated_at" "keyword"
create_payload_index "template_rank" "integer"
create_payload_index "quality" "float"
create_payload_index "auto_updated" "bool"

echo "Collection summary:"
request GET "/collections/${COLLECTION_NAME}" | {
  if command -v jq >/dev/null 2>&1; then
    jq .
  else
    cat
  fi
}

echo
echo "Done. Collection '${COLLECTION_NAME}' is ready."
