SERVER_API_FILE="api/math-bees/openapi.yml"
SERVER_OUT_FILE="src/app/autogen/api/schemas.py"

CLIMATE_API_FILE="api/climate/openapi.yml"
CLIMATE_OUT_DIR="src/app/autogen/clients/climate"

TOPOLOGY_API_FILE="api/field-topology/openapi.yml"
TOPOLOGY_OUT_DIR="src/app/autogen/clients/field_topology"

FINANCE_API_FILE="api/finance/openapi.yml"
FINANCE_OUT_DIR="src/app/autogen/clients/finance"

echo -e "\033[36mGenerating Pydantic models for Server API...\033[0m"
mkdir -p src/app/autogen/api src/app/autogen/clients

datamodel-codegen \
  --input "$SERVER_API_FILE" \
  --input-file-type openapi \
  --output "$SERVER_OUT_FILE" \
  --target-python-version 3.11 \
  --use-standard-collections \
  --use-schema-description \
  --use-annotated \
  --disable-timestamp \
  --preset standard-py311-20261106

if [ $? -eq 0 ]; then
    echo -e "\033[32mSuccessfully generated: $SERVER_OUT_FILE\033[0m"
else
    echo -e "\033[31mServer models generation failed!\033[0m"
    exit 1
fi

echo -e "\033[36mGenerating Climate API Client...\033[0m"

mkdir -p "$CLIMATE_OUT_DIR"

openapi-python-client generate \
  --path "$CLIMATE_API_FILE" \
  --output-path "$CLIMATE_OUT_DIR" \
  --meta=none \
  --overwrite

if [ $? -eq 0 ]; then
    echo -e "\033[32mSuccessfully generated Climate Client to: $CLIMATE_OUT_DIR\033[0m"
else
    echo -e "\033[31mClimate client generation failed!\033[0m"
    exit 1
fi

echo -e "\033[36mGenerating Farm Topology API Client...\033[0m"

mkdir -p "$TOPOLOGY_OUT_DIR"

openapi-python-client generate \
  --path "$TOPOLOGY_API_FILE" \
  --output-path "$TOPOLOGY_OUT_DIR" \
  --meta=none \
  --overwrite

if [ $? -eq 0 ]; then
    echo -e "\033[32mSuccessfully generated Farm Topology Client to: $TOPOLOGY_OUT_DIR\033[0m"
else
    echo -e "\033[31mFarm Topology client generation failed!\033[0m"
    exit 1
fi

echo -e "\033[36mGenerating Finance API Client...\033[0m"

mkdir -p "$FINANCE_OUT_DIR"

openapi-python-client generate \
  --path "$FINANCE_API_FILE" \
  --output-path "$FINANCE_OUT_DIR" \
  --meta=none \
  --overwrite

if [ $? -eq 0 ]; then
    echo -e "\033[32mSuccessfully generated Finance Client to: $FINANCE_OUT_DIR\033[0m"
else
    echo -e "\033[31mFinance client generation failed!\033[0m"
    exit 1
fi

echo -e "\033[32mAll generations completed successfully!\033[0m"