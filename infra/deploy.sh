#!/bin/bash
# Deploy CleverSyncSOS Infrastructure to Azure
# This script deploys the Bicep template to create all required Azure resources

set -e

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() { echo -e "${CYAN}$1${NC}"; }
print_success() { echo -e "${GREEN}$1${NC}"; }
print_warning() { echo -e "${YELLOW}$1${NC}"; }
print_error() { echo -e "${RED}$1${NC}"; }

# Parse arguments
RESOURCE_GROUP=""
LOCATION="eastus"
SQL_ADMIN_LOGIN=""
SQL_ADMIN_PASSWORD=""
PREFIX="cleversync"
WHAT_IF=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -g|--resource-group)
            RESOURCE_GROUP="$2"
            shift 2
            ;;
        -l|--location)
            LOCATION="$2"
            shift 2
            ;;
        --sql-admin-login)
            SQL_ADMIN_LOGIN="$2"
            shift 2
            ;;
        --sql-admin-password)
            SQL_ADMIN_PASSWORD="$2"
            shift 2
            ;;
        -p|--prefix)
            PREFIX="$2"
            shift 2
            ;;
        --what-if)
            WHAT_IF=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 -g <resource-group> [options]"
            echo ""
            echo "Required:"
            echo "  -g, --resource-group       Azure resource group name"
            echo "  --sql-admin-login          SQL Server administrator login"
            echo "  --sql-admin-password       SQL Server administrator password"
            echo ""
            echo "Optional:"
            echo "  -l, --location             Azure region (default: eastus)"
            echo "  -p, --prefix               Resource name prefix (default: cleversync)"
            echo "  --what-if                  Preview changes without deploying"
            echo "  -h, --help                 Show this help message"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Validate required parameters
if [[ -z "$RESOURCE_GROUP" ]]; then
    print_error "Error: Resource group name is required (-g or --resource-group)"
    exit 1
fi

if [[ -z "$SQL_ADMIN_LOGIN" ]]; then
    print_error "Error: SQL admin login is required (--sql-admin-login)"
    exit 1
fi

if [[ -z "$SQL_ADMIN_PASSWORD" ]]; then
    print_error "Error: SQL admin password is required (--sql-admin-password)"
    exit 1
fi

# Header
print_info "========================================"
print_info "CleverSyncSOS Infrastructure Deployment"
print_info "========================================"
echo ""

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    print_error "Azure CLI is not installed. Please install from https://aka.ms/installazurecliwindows"
    exit 1
fi

AZ_VERSION=$(az version --query '\"azure-cli\"' -o tsv)
print_success "✓ Azure CLI version: $AZ_VERSION"

# Check if logged in
print_warning "Checking Azure login status..."
if ! az account show &> /dev/null; then
    print_warning "Not logged in. Initiating login..."
    az login
fi

ACCOUNT_NAME=$(az account show --query "name" -o tsv)
ACCOUNT_ID=$(az account show --query "id" -o tsv)
USER_NAME=$(az account show --query "user.name" -o tsv)

print_success "✓ Logged in as: $USER_NAME"
print_success "✓ Subscription: $ACCOUNT_NAME ($ACCOUNT_ID)"
echo ""

# Check if resource group exists, create if not
print_warning "Checking resource group '$RESOURCE_GROUP'..."
if ! az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    print_warning "Resource group does not exist. Creating..."
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none
    print_success "✓ Resource group created"
else
    print_success "✓ Resource group exists"
fi
echo ""

# Deploy Bicep template
print_warning "Deploying infrastructure..."
echo -e "${GRAY}  Resource Group: $RESOURCE_GROUP${NC}"
echo -e "${GRAY}  Location: $LOCATION${NC}"
echo -e "${GRAY}  Prefix: $PREFIX${NC}"
echo -e "${GRAY}  SQL Admin: $SQL_ADMIN_LOGIN${NC}"
echo ""

DEPLOYMENT_NAME="cleversync-$(date +%Y%m%d-%H%M%S)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$WHAT_IF" = true ]; then
    print_warning "Running in WhatIf mode (no changes will be made)..."
    az deployment group what-if \
        --resource-group "$RESOURCE_GROUP" \
        --template-file "$SCRIPT_DIR/main.bicep" \
        --parameters prefix="$PREFIX" location="$LOCATION" sqlAdminLogin="$SQL_ADMIN_LOGIN" sqlAdminPassword="$SQL_ADMIN_PASSWORD"
else
    DEPLOYMENT_OUTPUT=$(az deployment group create \
        --resource-group "$RESOURCE_GROUP" \
        --template-file "$SCRIPT_DIR/main.bicep" \
        --parameters prefix="$PREFIX" location="$LOCATION" sqlAdminLogin="$SQL_ADMIN_LOGIN" sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
        --name "$DEPLOYMENT_NAME" \
        --output json)

    echo ""
    print_success "========================================"
    print_success "Deployment Complete!"
    print_success "========================================"
    echo ""

    print_info "Outputs:"
    echo "$DEPLOYMENT_OUTPUT" | jq -r '.properties.outputs | to_entries[] | "  \(.key): \(.value.value)"'
    echo ""

    print_warning "Next Steps:"
    echo "1. Run database migrations to create SessionDb schema"
    echo -e "${GRAY}   dotnet ef database update --context SessionDbContext${NC}"
    echo ""
    echo "2. Add Clever API credentials to Key Vault"
    echo -e "${GRAY}   az keyvault secret set --vault-name <key-vault-name> --name 'CleverSyncSOS--District-<Name>--ClientId' --value '<client-id>'${NC}"
    echo -e "${GRAY}   az keyvault secret set --vault-name <key-vault-name> --name 'CleverSyncSOS--District-<Name>--ClientSecret' --value '<client-secret>'${NC}"
    echo ""
    echo "3. Deploy Function App code"
    echo -e "${GRAY}   func azure functionapp publish <function-app-name>${NC}"
fi
