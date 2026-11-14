$SERVER_API_FILE = "../api/server/openapi.yml"
$SERVER_OUT_FILE = "src/app/autogen/api/schemas.py"

$CLIMATE_API_FILE = "../api/clients/climate/openapi.yml"
$CLIMATE_OUT_DIR = "src/app/autogen/clients/climate"

$TOPOLOGY_API_FILE = "../api/clients/field_topology/openapi.yml"
$TOPOLOGY_OUT_DIR = "src/app/autogen/clients/field_topology"

$FINANCE_API_FILE = "../api/clients/finance/openapi.yml"
$FINANCE_OUT_DIR = "src/app/autogen/clients/finance"

Write-Host "Generating Pydantic models for Server API..." -ForegroundColor Cyan
New-Item -Force -ItemType File "src/app/autogen/__init__.py", "src/app/autogen/api/__init__.py", "src/app/autogen/clients/__init__.py" | Out-Null

datamodel-codegen `
  --input $SERVER_API_FILE `
  --input-file-type openapi `
  --output $SERVER_OUT_FILE `
  --output-model-type pydantic_v2.BaseModel `
  --target-python-version 3.11 `
  --use-standard-collections `
  --use-schema-description `
  --use-annotated `
  --disable-timestamp `
  --preset standard-py311-20261106

if ($?) {
    Write-Host "Successfully generated: $SERVER_OUT_FILE" -ForegroundColor Green
} else {
    Write-Host "Server models generation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Generating Climate API Client..." -ForegroundColor Cyan

New-Item -Force -ItemType Directory $CLIMATE_OUT_DIR | Out-Null

openapi-python-client generate `
  --path $CLIMATE_API_FILE `
  --output-path $CLIMATE_OUT_DIR `
  --meta=none `
  --overwrite

if ($?) {
    Write-Host "Successfully generated Climate Client to: $CLIMATE_OUT_DIR" -ForegroundColor Green
} else {
    Write-Host "Climate client generation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Generating Farm Topology API Client..." -ForegroundColor Cyan

New-Item -Force -ItemType Directory $TOPOLOGY_OUT_DIR | Out-Null

openapi-python-client generate `
  --path $TOPOLOGY_API_FILE `
  --output-path $TOPOLOGY_OUT_DIR `
  --meta=none `
  --overwrite

if ($?) {
    Write-Host "Successfully generated Farm Topology Client to: $TOPOLOGY_OUT_DIR" -ForegroundColor Green
} else {
    Write-Host "Farm Topology client generation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Generating Finance API Client..." -ForegroundColor Cyan

New-Item -Force -ItemType Directory $FINANCE_OUT_DIR | Out-Null

openapi-python-client generate `
  --path $FINANCE_API_FILE `
  --output-path $FINANCE_OUT_DIR `
  --meta=none `
  --overwrite

if ($?) {
    Write-Host "Successfully generated Finance Client to: $FINANCE_OUT_DIR" -ForegroundColor Green
} else {
    Write-Host "Finance client generation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "All generations completed successfully!" -ForegroundColor Green