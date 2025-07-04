param(
    [Parameter(Mandatory=$true)]
    [string]$Environment
)

$ConfigFile = "$Environment.local.json"
$Config = Get-Content $ConfigFile | ConvertFrom-Json
$ResourceGroup = $Config.resourceGroupName
$Location = $Config.location

$WorkspaceName = "law-$ResourceGroup"

Write-Host "Creating Log Analytics Workspace: $WorkspaceName"

az monitor log-analytics workspace create `
    --resource-group $ResourceGroup `
    --workspace-name $WorkspaceName `
    --location $Location