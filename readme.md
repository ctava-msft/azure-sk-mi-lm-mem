# Overview
This is a simple project to demonstrate the use of Managed Identity and Azure Open AI.

# Instructions

Deploy infra using the following commands:
```bash
azd auth login
azd up
```
Copy sample.env to .env.
Put in the
AZURE_OPENAI_ENDPOINT
MODEL_DEPLOYMENT_NAME
MODEL_ID
MODEL_VERSION

Run the project using the following commands:

```
dotnet run --project ./Project.csproj
```