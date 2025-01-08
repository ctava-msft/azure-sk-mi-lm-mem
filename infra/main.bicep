targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention.')
param environmentName string

@minLength(1)
@allowed([
  'eastus'
  'eastus2'
  'westus3'
])
@description('Primary location for all resources.')
param location string

@description('Id of the principal to assign database and application roles.')
param principalId string = ''

// Optional parameters
param openAiAccountName string = ''
param userAssignedIdentityName string = ''

var abbreviations = loadJsonContent('abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  repo: 'https://github.com/AzureCosmosDB/cosmosdb-nosql-copilot'
}

var chatSettings = {
  maxContextWindow: '3'
  cacheSimilarityScore: '0.95'
  productMaxResults: '10'
}

var openAiSettings = {
  completionModelName: 'gpt-4o'
  completionDeploymentName: 'gpt-4o'
  embeddingModelName: 'text-embedding-3-large'
  embeddingDeploymentName: 'text-embedding-3-large'
  maxRagTokens: '1500'
  maxContextTokens: '500'
}

var principalType = 'User'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: environmentName
  location: location
  tags: tags
}

module identity 'app/identity.bicep' = {
  name: 'identity'
  scope: resourceGroup
  params: {
    identityName: !empty(userAssignedIdentityName) ? userAssignedIdentityName : '${abbreviations.userAssignedIdentity}-${resourceToken}'
    location: location
    tags: tags
  }
}

module ai 'app/ai.bicep' = {
  name: 'ai'
  scope: resourceGroup
  params: {
    accountName: !empty(openAiAccountName) ? openAiAccountName : '${abbreviations.openAiAccount}-${resourceToken}'
    location: location
    completionModelName: openAiSettings.completionModelName
    completionsDeploymentName: openAiSettings.completionDeploymentName
    embeddingsModelName: openAiSettings.embeddingModelName
    embeddingsDeploymentName: openAiSettings.embeddingDeploymentName
    tags: tags
  }
}


module security 'app/security.bicep' = {
  name: 'security'
  scope: resourceGroup
  params: {
    appPrincipalId: identity.outputs.principalId
    userPrincipalId: !empty(principalId) ? principalId : null
    principalType: principalType
  }
}

// AI outputs
output AZURE_OPENAI_ACCOUNT_ENDPOINT string = ai.outputs.endpoint
output AZURE_OPENAI_COMPLETION_DEPLOYMENT_NAME string = ai.outputs.deployments[0].name
output AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME string = ai.outputs.deployments[1].name
output AZURE_OPENAI_MAX_RAG_TOKENS string = openAiSettings.maxRagTokens
output AZURE_OPENAI_MAX_CONTEXT_TOKENS string = openAiSettings.maxContextTokens

// Chat outputs
output AZURE_CHAT_MAX_CONTEXT_WINDOW string = chatSettings.maxContextWindow
output AZURE_CHAT_CACHE_SIMILARITY_SCORE string = chatSettings.cacheSimilarityScore
output AZURE_CHAT_PRODUCT_MAX_RESULTS string = chatSettings.productMaxResults
