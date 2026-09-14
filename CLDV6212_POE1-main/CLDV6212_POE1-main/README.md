# CoffeeNChill — Part 1: Azure Functions, Tables, Files & Docker Hub

Digital menu and staff document management for the CoffeeNChill campus café. Menu items are stored in **Azure Table Storage** (`MenuItems`), and operational documents are stored in an **Azure File Share** (`staff-docs`). All storage is emulated locally with **Azurite** inside Docker containers.

## Architecture

```
┌─────────────────────┐     HTTP (7071)      ┌──────────────────────────────┐
│  Postman / Client   │ ───────────────────► │  Azure Functions Container   │
└─────────────────────┘                      │  (coffeenchill-functions)    │
                                             └──────────────┬───────────────┘
                                                            │
                              AzureWebJobsStorage           │
                              (UseDevelopmentStorage)       ▼
                                             ┌──────────────────────────────┐
                                             │  Azurite Container           │
                                             │  Tables :10002  Files :10000 │
                                             │  Blobs  :10000  Queues :10001│
                                             └──────────────────────────────┘
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Postman](https://www.postman.com/downloads/)

## Local Development Setup

### 1. Clone the repository

```bash
git clone <your-github-repo-url>
cd CLDV6212_POE1
```

### 2. Configure local settings

Copy the example settings file and ensure Azurite connection is configured:

```bash
cd CoffeeNChill.Functions
copy local.settings.json.example local.settings.json
```

`local.settings.json` uses `AzureWebJobsStorage=UseDevelopmentStorage=true` to connect to Azurite on `127.0.0.1`.

### 3. Start Azurite (standalone container)

From the `Azurite` folder:

```bash
cd ../Azurite
docker build -t <ST10435159>/coffeenchill-Azurite:v1.0 .
docker run -d --name coffeenchill-Azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 <ST10435159>/coffeenchill-Azurite:v1.0
```

Or pull/run the official image directly:

```bash
docker run -d --name coffeenchill-Azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

### 4. Run Azure Functions locally

```bash
cd ../CoffeeNChill.Functions
func start
```

The API base URL is: `http://localhost:7071/api`

## API Endpoints

### Menu Management (Azure Table: `MenuItems`)

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/menu` | Create a menu item |
| GET | `/api/menu` | Get all menu items |
| GET | `/api/menu/category/{category}` | Filter by category (PartitionKey) |
| PUT | `/api/menu/{category}/{id}` | Update price, availability, name, or description |
| DELETE | `/api/menu/{category}/{id}` | Delete a menu item |

**Menu item schema**

| Field | Type | Notes |
|-------|------|-------|
| PartitionKey | string | Category: `Hot Drinks`, `Cold Drinks`, `Pastries`, `Sandwiches` |
| RowKey | string | SKU/ID, e.g. `COF-001` |
| Name | string | Item name |
| Description | string | Short description |
| Price | double | Item price |
| IsAvailable | bool | Availability flag |

**Sample create body**

```json
{
  "partitionKey": "Hot Drinks",
  "rowKey": "COF-001",
  "name": "Espresso",
  "description": "Strong single shot of espresso",
  "price": 25.50,
  "isAvailable": true
}
```

### Staff Documents (Azure File Share: `staff-docs`)

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/documents/upload` | Upload file via `multipart/form-data` |
| GET | `/api/documents` | List files with name, size, last modified |
| GET | `/api/documents/download/{fileName}` | Download a file |

Allowed upload extensions: `.pdf`, `.doc`, `.docx`, `.txt`, `.md`

## Docker: Standalone Container Execution

> Part 1 requires individual `docker run` commands — no Docker Compose.

### Build and push Azurite image

```bash
cd Azurite
docker build -t <dockerhub_username>/coffeenchill-Azurite:v1.0 .
docker push <dockerhub_username>/coffeenchill-Azurite:v1.0
```

### Build and push Functions image

```bash
cd ../CoffeeNChill.Functions
docker build -t <dockerhub_username>/coffeenchill-functions:v1.0 .
docker push <dockerhub_username>/coffeenchill-functions:v1.0
```

### Run containers independently

**Terminal 1 — Azurite**

```bash
docker run -d --name coffeenchill-Azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 <dockerhub_username>/coffeenchill-Azurite:v1.0
```

**Terminal 2 — Functions**

When the Functions container runs separately from Azurite, set `AZURITE_HOST=host.docker.internal` so storage endpoints resolve to the host machine:

```bash
docker run -p 7071:80 ^
  -e AzureWebJobsStorage="UseDevelopmentStorage=true" ^
  -e AZURITE_HOST="host.docker.internal" ^
  <dockerhub_username>/coffeenchill-functions:v1.0
```

On Linux/macOS, replace `^` with `\`.

## Postman Testing

1. Import `docs/CoffeeNChill.postman_collection.json`
2. Import `docs/CoffeeNChill.postman_environment.json` (optional — collection already defines `{{baseUrl}}`)
3. For the **Upload Staff Document** request, select `docs/sample-recipe.txt` as the file in the form-data body
4. Run the collection with **Collection Runner**

The collection includes automated tests for all menu CRUD operations, category filtering, document upload/list/download, and 404 error cases.

## Project Structure

```
CLDV6212_POE1/
├── Azurite/
│   └── Dockerfile                  # coffeenchill-Azurite:v1.0
├── CoffeeNChill.Functions/
│   ├── Dockerfile                  # coffeenchill-functions:v1.0
│   ├── MenuFunctions.cs            # Menu CRUD HTTP triggers
│   ├── StaffDocumentFunctions.cs   # Document upload/list/download
│   ├── Models/
│   │   └── MenuItem.cs
│   ├── Services/
│   │   ├── MenuTableService.cs
│   │   ├── StaffFileShareService.cs
│   │   └── StorageConnectionHelper.cs
│   └── local.settings.json.example
├── docs/
│   ├── CoffeeNChill.postman_collection.json
│   ├── CoffeeNChill.postman_environment.json
│   └── sample-recipe.txt
└── README.md
```

## Group Member Contributions

| Member | Contributions |
|--------|---------------|
| _Member 1_ | St10471939 |
| _Member 2_ | St10435159|
| _Member 3_ | ST10486778 |

## Video

YouTube walkthrough: _[Add your unlisted YouTube link here]_

The video demonstrates:

- Starting Azurite and Functions containers with `docker run`
- Running the Postman collection against the live API
- Brief code walkthrough of menu and document functions

