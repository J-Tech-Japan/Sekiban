param(
    [Parameter(Mandatory=$true)]
    [string]$Environment
)

$ConfigFile = "$Environment.local.json"
$Config = Get-Content $ConfigFile | ConvertFrom-Json
$ResourceGroup = $Config.resourceGroupName
$Location = $Config.location

$AcrName = "acr$($ResourceGroup -replace '-', '')".ToLower()

Write-Host "Creating Azure Container Registry: $AcrName"

az acr create `
    --resource-group $ResourceGroup `
    --name $AcrName `
    --sku Basic `
    --location $Location