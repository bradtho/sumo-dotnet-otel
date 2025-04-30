# README.md

## OpenTelemetry MVC App (.NET 8)

This is a simple .NET 8 MVC application that uses Entity Framework Core with SQL Server, and includes OpenTelemetry tracing with automatic instrumentation.

### ✅ Features

- .NET 8
- MSSQL backend
- Docker Compose for orchestration
- OpenTelemetry (with Sumo Logic and Jaeger exporter)
- Entity Framework Core + automatic migrations
- SQL query tracing
- Seed data
- Client App with automatic polling of API endpoints

### 🔧 Prerequisites

- Docker & Docker Compose
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (only needed for running `dotnet ef` locally).
- Review the .env_example file and edit the OTEL_EXPORTER_OTLP_ENDPOINT value to be your Sumo Logic OTLP endpoint for direct connection or use `http://otelcol:4318` for routing via the Sumo Logic OTEL Collector (recommended).
- Import the Sumo Logic Dashboards in ./Dashboards into your Sumo Logic account.
- Jaeger is installed to locally validate Traces but is not required.

### 🚀 How to Run

```bash

# 1. Generate migration (only needed once)
dotnet ef migrations add InitialCreate

# 2. Move the .env_example file to .env
mv .env_example .env

# 3. Run the app with Docker Compose
docker compose up -d --build

## For logging to the console omit the -d flag.
```

App will be available at: `http://localhost:5001/items`

### 🧪 Test the API

The HttpClientPollingApp will randomly poll each of the API endpoints from within the Pod. However, to manually poll and endpoint from your terminal you may run any of the following.

```bash
curl http://localhost:5001/items # returns all items stored in the database
# generate errors for testing
curl http://localhost:5001/items/deadlock-error
curl http://localhost:5001/items/connection-pool-error
curl http://localhost:5001/items/transaction-error
curl http://localhost:5001/items/constraint-error
curl http://localhost:5001/items/timeout-error
# adds a new item to the database
curl -X POST http://localhost:5001/items -H "Content-Type: application/json" -d '{"name":"New Item"}' 
```
