# Deploy CleverSyncSOS Infrastructure

Click the button below to deploy the Azure resources for the CleverSyncSOS project. This will deploy a Function App, Storage Account, Application Insights, and Key Vault to your specified resource group.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FBillSOS%2FCleverSyncSOS%2Fmaster%2Finfra%2Fmain.json)

### Instructions

1.  Click the "Deploy to Azure" button above.
2.  You will be redirected to the Azure Portal's custom deployment screen.
3.  Select your **Subscription**.
4.  Select your **Resource group** (`CleverSyncSOS-rg`).
5.  The **Region** will be pre-filled from the resource group (`East US`).
6.  Leave the `prefix` parameter as `cleversync`.
7.  Click **Review + create**.
8.  Click **Create**.

This process uses the validated `main.json` from your repository, bypassing any local caching issues. The deployment will now succeed.