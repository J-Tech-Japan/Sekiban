param(
    [Parameter(Mandatory=$true)]
    [string]$Environment
)

$ConfigFile = "$Environment.local.json"
$Config = Get-Content $ConfigFile | ConvertFrom-Json
$ResourceGroup = $Config.resourceGroupName
$Location = $Config.location

Write-Host "Creating resource group: $ResourceGroup in location: $Location"

az group create --name $ResourceGroup --location $Location