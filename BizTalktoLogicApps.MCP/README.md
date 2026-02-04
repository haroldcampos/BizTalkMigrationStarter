# BizTalk to Logic Apps Migration MCP Server

A **Model Context Protocol (MCP)** server that exposes the BizTalk migration starter as AI-accessible tools. This enables AI assistants to help with migration planning, analysis, conversion, and deployment of BizTalk orchestrations to Azure Logic Apps.

## Overview

This MCP server wraps the existing BizTalkToLogicApps migration functionality and exposes it through a standardized protocol that can be used by AI assistants, IDEs, and development tools.

### What is MCP?

The Model Context Protocol (MCP) is an open standard for connecting AI assistants to external tools and data sources. It enables:

- **Tool Discovery**: AI assistants can discover available migration tools automatically
- **Structured Interaction**: Standardized request/response format for tool invocation
- **Resource Access**: Access to BizTalk and Logic Apps artifacts
- **Prompt Templates**: Pre-defined migration guidance prompts

## ??? Architecture

### MCP Server Architecture

```
+---------------------------------------------------------------+
|                  AI Assistant / Client                        |
|           (Claude Desktop, VS Code, Custom Apps)              |
+---------------------------------------------------------------+
                    | MCP Protocol (JSON-RPC over stdio)
                    v
+---------------------------------------------------------------+
|                MCP Server (This Project)                      |
|  +----------------------------------------------------------+ |
|  |             Tool Registry & Handlers                     | |
|  |  +-----------------+  +-----------------+               | |
|  |  |  Orchestration  |  |   Pipeline      |               | |
|  |  |   Tools         |  |   Tools         |               | |
|  |  | (ODX->Workflow) |  | (BTP->Workflow) |               | |
|  |  +-----------------+  +-----------------+               | |
|  |  +-----------------+  +-----------------+               | |
|  |  |  Map Tools      |  | Configuration   |               | |
|  |  | (BTM->Liquid)   |  |   Tools         |               | |
|  |  +-----------------+  +-----------------+               | |
|  +----------------------------------------------------------+ |
+---------------------------------------------------------------+
                    | Direct Library Calls
                    v
+---------------------------------------------------------------+
|           BizTalk to Logic Apps Migration Libraries           |
|  +----------------------------------------------------------+ |
|  |  ODXtoWFMigrator - Orchestration to Workflow            | |
|  |  • BizTalkOrchestrationParser • LogicAppsMapper         | |
|  |  • ConnectorSchemaRegistry    • ExpressionMapper        | |
|  |  • BindingSnapshot            • WorkflowValidator       | |
|  |  • OdxAnalyzer                • ReportGenerator         | |
|  +----------------------------------------------------------+ |
|  |  BTMtoLMLMigrator - BizTalk Maps to Liquid              | |
|  |  • BtmParser         • FunctoidTranslator               | |
|  |  • LmlGenerator      • BtmMigrator                      | |
|  +----------------------------------------------------------+ |
|  |  BTPtoLA - Pipelines to Workflows                       | |
|  |  • PipelineParser           • PipelineWorkflowMapper    | |
|  |  • PipelineJSONGenerator    • PipelineConnectorRegistry | |
|  +----------------------------------------------------------+ |
+---------------------------------------------------------------+
```

### Tool Categories

| Category | Description | Libraries Used |
|----------|-------------|----------------|
| **Orchestration Tools** | Analyze and convert BizTalk orchestrations (.odx) to Logic Apps workflows, including pattern-based refactoring | ODXtoWFMigrator |
| **Pipeline Tools** | Convert BizTalk pipelines (.btp) to Logic Apps workflows with component mapping | BTPtoLA |
| **Map Tools** | Transform BizTalk maps (.btm) to Liquid templates (.lml) for Logic Apps Data Mapper | BTMtoLMLMigrator |
| **Configuration Tools** | Registry management, workflow validation, connector resolution | All Libraries |

## Available Tools

### Orchestration Analysis Tools

#### `analyze_biztalk_orchestration`
Analyzes BizTalk orchestration files and generates migration feasibility reports.

**Parameters:**
- `odxFilePath` (required): Path to .odx orchestration file
- `bindingFilePath` (optional): Path to binding XML file
- `outputFormat` (optional): "json" | "html" | "markdown"
- `includeComplexity` (optional): Include complexity analysis (default: true)

**Example:**
```json
{
  "name": "analyze_biztalk_orchestration",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\MyOrchestration.odx",
    "includeComplexity": true
  }
}
```

#### `analyze_odx_directory`
Deep analysis of all ODX files in a directory.

**Parameters:**
- `directoryPath` (required): Directory containing .odx files
- `recursive` (optional): Search subdirectories (default: true)
- `outputPath` (optional): Output path for JSON report

#### `generate_migration_report`
Generates comprehensive migration assessment reports.

**Parameters:**
- `odxFilePath` (required): Path to .odx file
- `outputPath` (optional): Output file path
- `format` (optional): "html" | "markdown"

#### `validate_biztalk_artifacts`
Validates BizTalk artifacts before migration.

**Parameters:**
- `odxFilePath` (required): Path to .odx file
- `bindingFilePath` (optional): Path to binding file

### Pipeline Analysis Tools

#### `analyze_biztalk_pipeline`
Analyzes BizTalk pipeline files (.btp) and reports structure, stages, components, and default pipeline pattern detection.

**Parameters:**
- `btpFilePath` (required): Path to the BizTalk .btp pipeline file
- `includeMetadata` (optional): Include detailed stage and component metadata (default: true)
- `detectPattern` (optional): Detect if pipeline matches default patterns (PassThru, XMLReceive, etc.) (default: true)

**Example:**
```json
{
  "name": "analyze_biztalk_pipeline",
  "arguments": {
    "btpFilePath": "C:\\BizTalk\\Pipelines\\ReceivePipeline.btp",
    "includeMetadata": true,
    "detectPattern": true
  }
}
```

#### `validate_biztalk_pipeline`
Validates BizTalk pipeline structure and components before migration to identify potential issues.

**Parameters:**
- `btpFilePath` (required): Path to .btp pipeline file
- `checkComponents` (optional): Validate component compatibility for Logic Apps (default: true)
- `checkConfiguration` (optional): Validate component configurations (default: true)

### Map Analysis Tools

#### `analyze_btm_file`
Analyzes a BizTalk Map file and provides statistics about functoids, links, and complexity.

**Parameters:**
- `btmFilePath` (required): Path to the BizTalk .btm map file
- `includeDetails` (optional): Include detailed functoid and link information (default: false)

**Example:**
```json
{
  "name": "analyze_btm_file",
  "arguments": {
    "btmFilePath": "C:\\BizTalk\\Maps\\OrderToInvoice.btm",
    "includeDetails": true
  }
}
```

#### `validate_btm_file`
Validates a BizTalk Map file for common issues before conversion to LML.

**Parameters:**
- `btmFilePath` (required): Path to the BizTalk .btm map file
- `sourceSchemaPath` (optional): Path to source XSD schema file
- `targetSchemaPath` (optional): Path to target XSD schema file

### Orchestration Conversion Tools

#### `convert_biztalk_to_logicapp`
Converts BizTalk orchestration to Logic Apps workflow JSON.

**Parameters:**
- `odxFilePath` (required): Path to .odx file
- `bindingFilePath` (required): Path to binding XML
- `outputPath` (required): Output path for workflow.json
- `connectorRegistryPath` (optional): Custom connector registry
- `schemaVersion` (optional): Schema version (default: "2016-06-01")
- `workflowType` (optional): "Stateful" | "Stateless" (default: "Stateful")
- `validateOutput` (optional): Validate generated workflow (default: true)

**Example:**
```json
{
  "name": "convert_biztalk_to_logicapp",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\OrderProcessing.odx",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputPath": "C:\\Output\\OrderProcessing.workflow.json",
    "workflowType": "Stateful"
  }
}
```

#### `convert_biztalk_to_logicapp_refactored`
Converts BizTalk orchestration to Logic Apps with pattern-based optimizations for cleaner, more maintainable workflows. Supports cloud and on-premises deployment targets with automatic connector optimization.

**Parameters:**
- `odxFilePath` (required): Path to .odx orchestration file
- `bindingFilePath` (required): Path to BizTalk binding XML file
- `outputPath` (required): Output path for generated workflow.json
- `deploymentTarget` (optional): "Cloud" | "OnPremises" - Cloud uses Service Bus, OnPremises uses RabbitMQ/Kafka (default: "Cloud")
- `refactoringStrategy` (optional): "Conservative" | "Balanced" | "Aggressive" - Optimization aggressiveness level (default: "Balanced")
- `messagingPlatform` (optional): "ServiceBus" | "RabbitMQ" | "Kafka" | "IbmMq" - Preferred messaging platform
- `databaseConnector` (optional): "Sql" | "CosmosDb" | "Postgres" | "OracleDb" - Preferred database connector (default: "Sql")
- `simplifyConvoyPatterns` (optional): Use native session support for convoy patterns (default: true)
- `useNativeParallelBranches` (optional): Use Logic Apps native parallel branches for scatter-gather (default: true)
- `consolidateNestedScopes` (optional): Flatten unnecessary nested scopes (default: true)
- `generateParametersJson` (optional): Generate separate parameters.json file (default: true)
- `includePatternComments` (optional): Add pattern metadata to workflow definition (default: true)
- `workflowType` (optional): "Stateful" | "Stateless" (default: "Stateful")
- `schemaVersion` (optional): Logic Apps schema version (default: "2016-06-01")
- `validateOutput` (optional): Validate generated workflow (default: true)

**Example:**
```json
{
  "name": "convert_biztalk_to_logicapp_refactored",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\OrderProcessing.odx",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputPath": "C:\\Output\\OrderProcessing.workflow.json",
    "deploymentTarget": "Cloud",
    "refactoringStrategy": "Balanced",
    "messagingPlatform": "ServiceBus",
    "simplifyConvoyPatterns": true,
    "useNativeParallelBranches": true,
    "generateParametersJson": true
  }
}
```

**Response includes:**
- Success status and output paths
- Deployment target and strategy used
- Messaging platform and database connector selected
- Optimizations applied (convoy simplification, parallel branches, scope consolidation)
- Validation results

#### `generate_deployment_package`
Creates complete Logic Apps Standard deployment package.

**Parameters:**
- `odxFilePath` (required): Path to .odx file
- `bindingFilePath` (required): Path to binding XML
- `outputDirectory` (required): Output directory
- `schemaVersion` (optional): Schema version
- `includeDevOps` (optional): Include Azure DevOps YAML (default: true)
- `includePowerShell` (optional): Include PS deployment script (default: true)

#### `batch_convert_orchestrations`
Converts multiple orchestrations in one operation.

**Parameters:**
- `directoryPath` (required): Directory with .odx files
- `bindingFilePath` (required): Binding XML path
- `outputDirectory` (required): Output directory
- `schemaVersion` (optional): Schema version
- `recursive` (optional): Search subdirectories (default: true)

#### `convert_bindings_only`
Generates workflows from bindings without orchestration files.

**Parameters:**
- `bindingFilePath` (required): Binding XML path
- `outputDirectory` (required): Output directory
- `schemaVersion` (optional): Schema version

### Pipeline Conversion Tools

#### `convert_pipeline_to_workflow`
Converts BizTalk pipeline (.btp) to Azure Logic Apps Standard workflow JSON with component mapping.

**Parameters:**
- `btpFilePath` (required): Path to the BizTalk .btp pipeline file
- `outputPath` (required): Output path for generated workflow.json file
- `workflowType` (optional): "Stateful" | "Stateless" (default: "Stateful")
- `workflowName` (optional): Custom workflow name (defaults to pipeline filename)
- `validateOutput` (optional): Validate generated workflow structure (default: true)

**Example:**
```json
{
  "name": "convert_pipeline_to_workflow",
  "arguments": {
    "btpFilePath": "C:\\BizTalk\\Pipelines\\XMLReceive.btp",
    "outputPath": "C:\\Output\\XMLReceive.workflow.json",
    "workflowType": "Stateful"
  }
}
```

#### `batch_convert_pipelines`
Converts multiple BizTalk pipeline files (.btp) to Logic Apps workflows in a single operation.

**Parameters:**
- `directoryPath` (required): Directory containing .btp pipeline files
- `outputDirectory` (required): Output directory for generated workflow files
- `workflowType` (optional): "Stateful" | "Stateless" (default: "Stateful")
- `recursive` (optional): Search subdirectories recursively (default: true)
- `continueOnError` (optional): Continue processing if a pipeline fails (default: true)

### Map Conversion Tools

#### `convert_btm_to_lml`
Converts BizTalk Map (BTM) file to Azure Logic Apps Liquid Mapping Language (LML) format.

**Parameters:**
- `btmFilePath` (required): Path to the BizTalk .btm map file
- `outputLmlPath` (optional): Output path for generated .lml file (defaults to same directory as BTM)
- `sourceSchemaPath` (optional): Path to source XSD schema file for namespace extraction
- `targetSchemaPath` (optional): Path to target XSD schema file for namespace extraction
- `preserveFormatting` (optional): Preserve formatting and whitespace in output (default: true)

**Example:**
```json
{
  "name": "convert_btm_to_lml",
  "arguments": {
    "btmFilePath": "C:\\BizTalk\\Maps\\OrderToInvoice.btm",
    "sourceSchemaPath": "C:\\BizTalk\\Schemas\\Order.xsd",
    "targetSchemaPath": "C:\\BizTalk\\Schemas\\Invoice.xsd",
    "outputLmlPath": "C:\\Output\\OrderToInvoice.lml"
  }
}
```

#### `batch_convert_btm_to_lml`
Batch converts multiple BizTalk Map files to Logic Apps Liquid Mapping Language format.

**Parameters:**
- `directoryPath` (required): Directory containing .btm files
- `outputDirectory` (optional): Output directory for generated .lml files (defaults to same as input)
- `recursive` (optional): Search subdirectories recursively (default: true)
- `sourceSchemaDirectory` (optional): Directory containing source XSD schemas
- `targetSchemaDirectory` (optional): Directory containing target XSD schemas

### Mapping Tools

#### `map_biztalk_expression`
Translates BizTalk XLANG expressions to Logic Apps expressions.

**Parameters:**
- `expression` (required): BizTalk expression string
- `context` (optional): Expression context
- `targetVersion` (optional): "Standard" | "Consumption"

**Example:**
```json
{
  "name": "map_biztalk_expression",
  "arguments": {
    "expression": "count > 10 && status == \"Active\"",
    "context": "decision"
  }
}
```

#### `resolve_connector_schema`
Resolves BizTalk adapters to Logic Apps connectors.

**Parameters:**
- `adapterType` (required): BizTalk adapter type (FILE, FTP, SQL, etc.)
- `registryPath` (optional): Custom registry path
- `operationType` (optional): "trigger" | "action" | "both"

**Example:**
```json
{
  "name": "resolve_connector_schema",
  "arguments": {
    "adapterType": "FILE",
    "operationType": "both"
  }
}
```

#### `parse_binding_file`
Parses BizTalk binding XML to extract configurations.

**Parameters:**
- `bindingFilePath` (required): Path to binding XML
- `extractFilters` (optional): Extract filter expressions (default: true)
- `extractTransportConfig` (optional): Extract transport config (default: true)

### Configuration Tools

#### `load_connector_registry`
Loads custom connector schema registry.

**Parameters:**
- `registryPath` (required): Path to registry JSON
- `validate` (optional): Validate structure (default: true)

#### `list_available_connectors`
Lists all available Logic Apps connectors.

**Parameters:**
- `registryPath` (optional): Custom registry path
- `filterByType` (optional): Filter by type/category

#### `validate_workflow`
Validates Logic Apps workflow JSON.

**Parameters:**
- `workflowPath` (required): Path to workflow.json
- `strictMode` (optional): Fail on warnings (default: false)

## Resources

The server exposes the following resource URIs:

- `biztalk://orchestration/{name}` - BizTalk orchestration ODX files
- `biztalk://pipeline/{name}` - BizTalk pipeline BTP files
- `biztalk://map/{name}` - BizTalk map BTM files
- `biztalk://binding/{name}` - BizTalk binding XML files
- `biztalk://schema/{name}` - BizTalk/XSD schema files
- `logicapp://definition/{name}` - Generated Logic Apps workflow JSON
- `logicapp://liquid/{name}` - Generated Liquid template LML files

## Prompts

Pre-defined prompts for common migration scenarios:

### `analyze-migration-complexity`
Assess BizTalk to Logic Apps migration effort for orchestrations, pipelines, and maps.

**Arguments:**
- `odxPath`: Path to orchestration file (optional)
- `btpPath`: Path to pipeline file (optional)
- `btmPath`: Path to map file (optional)

### `suggest-connector-mappings`
Recommend Logic Apps connector alternatives for BizTalk adapters and pipeline components.

**Arguments:**
- `adapterType`: BizTalk adapter type
- `componentType`: Pipeline component type (optional)

### `generate-migration-checklist`
Create step-by-step migration plan for orchestrations, pipelines, or maps.

**Arguments:**
- `orchestrationName`: Orchestration name (optional)
- `pipelineName`: Pipeline name (optional)
- `mapName`: Map name (optional)

### `explain-conversion-differences`
Document BizTalk vs Logic Apps differences for various artifact types.

**Arguments:**
- `feature`: BizTalk feature to explain
- `artifactType`: "orchestration" | "pipeline" | "map"

### `analyze-functoid-conversion`
Explain how specific BizTalk functoids convert to Liquid template equivalents.

**Arguments:**
- `functoidType`: Type of functoid to analyze

### `pipeline-component-mapping`
Show Logic Apps equivalents for BizTalk pipeline components.

**Arguments:**
- `componentName`: Pipeline component name

## Installation

### Prerequisites

- .NET Framework 4.7.2 or higher
- Windows OS (for BizTalk SDK dependencies)
- BizTalkToLogicApps migration library

### Build

```powershell
# Restore NuGet packages
nuget restore BizTalkToLogicApps.MCP.csproj

# Build the project
msbuild BizTalkToLogicApps.MCP.csproj /t:Rebuild /p:Configuration=Release

# Output: BizTalkToLogicApps.MCP\bin\Release\BizTalkToLogicApps.MCP.exe
```

## Usage

### With Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "biztalk-migration": {
      "command": "C:\\Path\\To\\BizTalkToLogicApps.MCP.exe",
      "args": [],
      "env": {}
    }
  }
}
```

### With VS Code

Install the MCP extension and configure:

```json
{
  "mcp.servers": [
    {
      "name": "BizTalk Migration",
      "command": "C:\\Path\\To\\BizTalkToLogicApps.MCP.exe"
    }
  ]
}
```

### Command Line Testing

Test the server using stdio:

```powershell
# Initialize
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | BizTalkToLogicApps.MCP.exe

# List tools
echo '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' | BizTalkToLogicApps.MCP.exe

# Call a tool
echo '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"analyze_biztalk_orchestration","arguments":{"odxFilePath":"test.odx"}}}' | BizTalkToLogicApps.MCP.exe
```

### Programmatic Usage

```csharp
using BizTalkToLogicApps.MCP.Server;
using System;
using System.IO;

var input = Console.In;
var output = Console.Out;

var server = new McpServer(input, output);
server.Start();
```

## Protocol

### Message Format

All messages use JSON-RPC 2.0 format:

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "convert_biztalk_to_logicapp",
    "arguments": {
      "odxFilePath": "path/to/file.odx",
      "bindingFilePath": "path/to/bindings.xml",
      "outputPath": "path/to/output.json"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\n  \"success\": true,\n  \"outputPath\": \"...\"\n}"
      }
    ],
    "isError": false
  }
}
```

**Error:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32602,
    "message": "Invalid params",
    "data": "ODX file not found: path/to/file.odx"
  }
}
```

### Error Codes

- `-32700`: Parse error (invalid JSON)
- `-32600`: Invalid request
- `-32601`: Method not found
- `-32602`: Invalid params
- `-32603`: Internal error
- `-32002`: Server not initialized

## Debugging

Debug logs are written to:
```
%APPDATA%\BizTalkToLogicApps.MCP\debug.log
```

Enable verbose logging by checking the debug.log file for:
- Message reception timestamps
- Method invocations
- Tool execution details
- Error stack traces

## Example Workflows

### Complete Orchestration Migration Workflow

1. **Analyze** orchestration complexity:
```json
{
  "name": "analyze_biztalk_orchestration",
  "arguments": {
    "odxFilePath": "MyOrch.odx",
    "includeComplexity": true
  }
}
```

2. **Validate** artifacts:
```json
{
  "name": "validate_biztalk_artifacts",
  "arguments": {
    "odxFilePath": "MyOrch.odx",
    "bindingFilePath": "bindings.xml"
  }
}
```

3. **Convert** to Logic Apps:
```json
{
  "name": "convert_biztalk_to_logicapp",
  "arguments": {
    "odxFilePath": "MyOrch.odx",
    "bindingFilePath": "bindings.xml",
    "outputPath": "workflow.json"
  }
}
```

4. **Generate** deployment package:
```json
{
  "name": "generate_deployment_package",
  "arguments": {
    "odxFilePath": "MyOrch.odx",
    "bindingFilePath": "bindings.xml",
    "outputDirectory": "DeploymentPackage"
  }
}
```

5. **Validate** generated workflow:
```json
{
  "name": "validate_workflow",
  "arguments": {
    "workflowPath": "workflow.json"
  }
}
```

### Refactored Orchestration Migration Workflow

For optimized migrations with pattern-based refactoring:

1. **Analyze** orchestration complexity (same as above)

2. **Convert** with refactoring optimizations:
```json
{
  "name": "convert_biztalk_to_logicapp_refactored",
  "arguments": {
    "odxFilePath": "MyOrch.odx",
    "bindingFilePath": "bindings.xml",
    "outputPath": "workflow.json",
    "deploymentTarget": "Cloud",
    "refactoringStrategy": "Balanced",
    "messagingPlatform": "ServiceBus",
    "simplifyConvoyPatterns": true,
    "useNativeParallelBranches": true,
    "consolidateNestedScopes": true,
    "generateParametersJson": true
  }
}
```

3. **Validate** generated workflow (same as above)

**Benefits of refactored conversion:**
- Automatic convoy pattern simplification using Logic Apps native session support
- Scatter-gather patterns converted to native parallel branches
- Nested scopes consolidated for cleaner workflow structure
- Separate parameters.json file for environment-specific configuration
- Pattern metadata comments for maintainability

### Complete Pipeline Migration Workflow

1. **Analyze** pipeline structure:
```json
{
  "name": "analyze_biztalk_pipeline",
  "arguments": {
    "btpFilePath": "MyPipeline.btp",
    "includeMetadata": true,
    "detectPattern": true
  }
}
```

2. **Validate** pipeline:
```json
{
  "name": "validate_biztalk_pipeline",
  "arguments": {
    "btpFilePath": "MyPipeline.btp",
    "checkComponents": true,
    "checkConfiguration": true
  }
}
```

3. **Convert** to Logic Apps workflow:
```json
{
  "name": "convert_pipeline_to_workflow",
  "arguments": {
    "btpFilePath": "MyPipeline.btp",
    "outputPath": "MyPipeline_workflow.json",
    "workflowType": "Stateful"
  }
}
```

### Complete Map Migration Workflow

1. **Analyze** map complexity:
```json
{
  "name": "analyze_btm_file",
  "arguments": {
    "btmFilePath": "OrderToInvoice.btm",
    "includeDetails": true
  }
}
```

2. **Validate** map:
```json
{
  "name": "validate_btm_file",
  "arguments": {
    "btmFilePath": "OrderToInvoice.btm",
    "sourceSchemaPath": "Order.xsd",
    "targetSchemaPath": "Invoice.xsd"
  }
}
```

3. **Convert** to Liquid template:
```json
{
  "name": "convert_btm_to_lml",
  "arguments": {
    "btmFilePath": "OrderToInvoice.btm",
    "sourceSchemaPath": "Order.xsd",
    "targetSchemaPath": "Invoice.xsd",
    "outputLmlPath": "OrderToInvoice.lml"
  }
}
```

### Batch Orchestration Migration

```json
{
  "name": "batch_convert_orchestrations",
  "arguments": {
    "directoryPath": "C:\\BizTalk\\Orchestrations",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputDirectory": "C:\\Output",
    "recursive": true
  }
}
```

### Batch Pipeline Migration

```json
{
  "name": "batch_convert_pipelines",
  "arguments": {
    "directoryPath": "C:\\BizTalk\\Pipelines",
    "outputDirectory": "C:\\Output\\Pipelines",
    "workflowType": "Stateful",
    "recursive": true
  }
}
```

### Batch Map Migration

```json
{
  "name": "batch_convert_btm_to_lml",
  "arguments": {
    "directoryPath": "C:\\BizTalk\\Maps",
    "outputDirectory": "C:\\Output\\LiquidMaps",
    "sourceSchemaDirectory": "C:\\BizTalk\\Schemas\\Source",
    "targetSchemaDirectory": "C:\\BizTalk\\Schemas\\Target",
    "recursive": true
  }
}
```

## Integration Examples

### AI Assistant Prompt

**Example 1: Orchestration Migration**
```
I have a BizTalk orchestration that I need to migrate to Azure Logic Apps. 
The file is located at C:\BizTalk\OrderProcessing.odx and the bindings are 
at C:\BizTalk\bindings.xml. Can you:

1. Analyze the complexity of this migration
2. Check for any validation issues
3. Convert it to a Logic Apps workflow
4. Generate a complete deployment package

Please provide recommendations for any unsupported patterns you find.
```

The AI assistant will use the MCP tools to:
1. Call `analyze_biztalk_orchestration` with complexity analysis
2. Call `validate_biztalk_artifacts` to check for issues
3. Call `convert_biztalk_to_logicapp` to generate workflow
4. Call `generate_deployment_package` to create deployable artifacts
5. Provide human-readable recommendations based on tool outputs

**Example 2: Pipeline Migration**
```
I have BizTalk pipelines in C:\BizTalk\Pipelines that need to be migrated 
to Logic Apps. Can you:

1. Analyze all pipeline files in the directory
2. Validate each pipeline for Logic Apps compatibility
3. Convert them all to Logic Apps workflows
4. Report any components that require manual conversion

Focus on XMLReceive.btp and XMLTransmit.btp first.
```

The AI assistant will use:
1. `analyze_biztalk_pipeline` for each pipeline
2. `validate_biztalk_pipeline` to check compatibility
3. `batch_convert_pipelines` for bulk conversion
4. Provide warnings about custom components

**Example 3: Map Migration**
```
I need to migrate BizTalk maps to Logic Apps Liquid templates. The maps 
are in C:\BizTalk\Maps and schemas in C:\BizTalk\Schemas. Can you:

1. Analyze the complexity of all BTM files
2. Identify any maps with scripting functoids
3. Convert all maps to LML format
4. Create a migration report

Please flag any maps that will need manual review.
```

The AI assistant will use:
1. `analyze_btm_file` for complexity assessment
2. `validate_btm_file` to detect scripting functoids
3. `batch_convert_btm_to_lml` for conversion
4. Generate summary of issues requiring attention

### Custom Integration

```csharp
// Custom client example
using System.Diagnostics;
using System.IO;

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "BizTalkToLogicApps.MCP.exe",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    }
};

process.Start();

// Send initialize
var initRequest = @"{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{}}";
process.StandardInput.WriteLine(initRequest);
var initResponse = process.StandardOutput.ReadLine();

// Call tool
var toolRequest = @"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/call"",""params"":{""name"":""analyze_biztalk_orchestration"",""arguments"":{""odxFilePath"":""test.odx""}}}";
process.StandardInput.WriteLine(toolRequest);
var toolResponse = process.StandardOutput.ReadLine();

process.Close();
```

## Limitations

- Requires .NET Framework 4.7.2 (Windows only)
- Uses stdio transport (no HTTP support currently)
- Inherits all limitations from underlying BizTalkToLogicApps libraries:
  
  **Orchestration (ODXtoWFMigrator):**
  - Business Rules Engine (BRE) requires manual conversion
  - XSLT content extraction not automated
  - Correlation sets detected but not translated
  - Complex expressions may need manual review
  
  **Maps (BTMtoLMLMigrator):**
  - Scripting functoids require manual conversion to Liquid
  - Custom XSLT not automatically converted
  - Database lookup functoids need Logic Apps actions
  - Flat file schemas not supported by Logic Apps Data Mapper
  
  **Pipelines (BTPtoLA):**
  - Custom pipeline components require custom code or Azure Functions
  - Some third-party components may not have Logic Apps equivalents
  - Complex pipeline configurations may need manual review

## Future Enhancements

- [ ] HTTP/SSE transport support
- [ ] Streaming responses for large operations
- [ ] Progress notifications for batch operations
- [ ] Resource templates (pre-configured connectors)
- [ ] Enhanced prompts with multi-step guidance
- [ ] Support for custom connector development
- [ ] Integration with Azure DevOps APIs
- [ ] Real-time collaboration feature

## Contributing

When adding new tools:

1. Create handler in appropriate `ToolHandlers` class
2. Define tool schema using `ToolSchemas` helpers
3. Register tool in handler's `RegisterTools` method
4. Update this README with tool documentation
5. Add integration tests

## License

MIT License - See LICENSE file in repository root.

## Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/haroldcampos/BizTalkMigrationStarter/issues
- **MCP Specification**: https://modelcontextprotocol.io

## Author

**Harold Campos**

---

**Version**: 1.0.0  
**MCP Protocol Version**: 2024-11-05  
**Target Framework**: .NET Framework 4.7.2  
**Last Updated**: January 28, 2025

**Related Projects**:
- **[ODXtoWFMigrator](../ODXtoWFMigrator/README.md)** - BizTalk orchestration to Logic Apps workflow conversion
- **[BTMtoLMLMigrator](../BTMtoLMLMigrator/README.md)** - BizTalk map to Liquid template conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - BizTalk pipeline to Logic Apps conversion
- [Azure Logic Apps Documentation](https://learn.microsoft.com/azure/logic-apps/)
- [BizTalk Server Documentation](https://learn.microsoft.com/biztalk/)
