infra/README.md
# Infra deployment - CleverSyncSOS

Quick reference to preview and deploy the `infra/main.bicep` template into the `CleverSyncSOS-rg` resource group in East US.

Prerequisites
- Azure CLI installed and logged in:
  - `az login`
  - `az account set --subscription "<your-subscription-id>"`
- Run from repo root so path `infra/main.bicep` resolves.
- Service principal or user must have Contributor rights on the resource group.

Preview (what-if)
- Bash / WSL / Git Bash (single-line)
 az deployment group what-if --resource-group CleverSyncSOS-rg --template-file infra/main.bicep --parameters prefix=cleversync location=eastus --output table
 az deployment group --resource-group CleverSyncSOS-rg --template-file infra/main.bicep --parameters prefix=cleversync location=eastus --output table

- 