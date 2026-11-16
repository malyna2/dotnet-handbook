// Azure lab: App Service + Azure SQL + Key Vault + managed identity + Application Insights, and a budget alert.
// Deploy into an EMPTY resource group you create for the lab; cleanup.sh deletes that group.
targetScope = 'resourceGroup'

@description('Short prefix for resource names, lowercase letters and digits.')
@minLength(3)
@maxLength(8)
param prefix string = 'dnlab'

param location string = resourceGroup().location

@description('Your Entra user: becomes the SQL server\'s Entra admin (no SQL logins exist at all).')
param sqlAdminLogin string
param sqlAdminObjectId string

@description('Where the budget alert emails go.')
param budgetEmail string

@description('Monthly budget for this resource group, in the billing currency.')
param budgetAmount int = 20

@description('First day of the current month, yyyy-MM-01. Budgets must start on the first of a month.')
param budgetStartDate string

var suffix = uniqueString(resourceGroup().id)
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'   // built-in role: read secret values

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-ai-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-plan-${suffix}'
  location: location
  sku: { name: 'B1' }
  kind: 'linux'
  properties: { reserved: true }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${prefix}-sql-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true          // no SQL logins, no password anywhere
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'User'
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: 'shop'
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
}

// Lab shortcut: lets Azure services reach the server. Production puts the server behind a private endpoint.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv-${suffix}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true              // access by Azure RBAC role, not access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource demoSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'PartnerApiKey'
  properties: { value: 'lab-only-not-a-real-key' }
}

resource web 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-web-${suffix}'
  location: location
  identity: { type: 'SystemAssigned' }        // the app's own Entra identity; Azure rotates its credentials
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'KeyVault__Uri', value: vault.properties.vaultUri }
        // A Key Vault reference: App Service resolves it with the app's managed identity.
        { name: 'Partner__ApiKey', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=${demoSecret.name})' }
      ]
      connectionStrings: [
        {
          name: 'Shop'
          type: 'SQLAzure'
          // No password: the managed identity signs in to Azure SQL.
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDb.name};Authentication=Active Directory Default;Encrypt=True;'
        }
      ]
    }
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, web.id, keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: '${prefix}-budget'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ budgetEmail ]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ budgetEmail ]
      }
    }
  }
}

output webAppName string = web.name
output webAppPrincipalId string = web.identity.principalId
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output keyVaultUri string = vault.properties.vaultUri
