param(
    [Parameter(Mandatory=$true)]
    [string]$Environment
)

$ConfigFile = "$Environment.local.json"
$Config = Get-Content $ConfigFile | ConvertFrom-Json
$ResourceGroup = $Config.resourceGroupName

$KeyVaultName = "kv-$ResourceGroup"
$UserPrincipalName = az ad signed-in-user show --query userPrincipalName -o tsv

Write-Host "Granting Key Vault access to user: $UserPrincipalName"

az keyvault set-policy `
    --name $KeyVaultName `
    --resource-group $ResourceGroup `
    --upn $UserPrincipalName `
    --secret-permissions get list set delete