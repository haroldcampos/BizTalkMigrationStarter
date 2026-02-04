# BizTalk to Logic Apps Migration Toolkit

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-blue)](https://dotnet.microsoft.com/download/dotnet-framework)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

A comprehensive, migration toolkit for transforming Microsoft BizTalk Server artifacts (orchestrations, maps, and pipelines) to Azure Logic Apps Standard. Includes specialized CLI tools for each artifact type plus an AI-powered Model Context Protocol (MCP) server for intelligent, assisted migration.

## Quick Start

```powershell
# ORCHESTRATIONS: Convert a BizTalk orchestration to Logic Apps
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml

# ORCHESTRATIONS: Refactored conversion with pattern optimizations (Cloud)
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor

# ORCHESTRATIONS: Refactored conversion for on-premises with RabbitMQ
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor --messaging rabbitmq

# ORCHESTRATIONS: Generate a migration readiness report
ODXtoWFMigrator.exe report MyOrchestration.odx --format html

# ORCHESTRATIONS: Create a complete deployment package
ODXtoWFMigrator.exe package MyOrchestration.odx bindings.xml

# ORCHESTRATIONS: Batch convert all orchestrations in a directory
ODXtoWFMigrator.exe batch convert --directory C:\BizTalk --bindings bindings.xml

# MAPS: Convert BizTalk map to Liquid template
BTMtoLMLMigrator.exe OrderToInvoice.btm Order.xsd Invoice.xsd

# PIPELINES: Convert BizTalk pipeline to Logic Apps workflow
BTPtoLA.exe XMLReceive.btp C:\Output

# AI-ASSISTED: Start MCP server for AI tools
BizTalkToLogicApps.MCP.exe
```

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Installation](#installation)
- [Usage](#usage)
- [Project Structure](#project-structure)
- [Migration Approach](#migration-approach)
- [Examples](#examples)
- [Testing](#testing)
- [License](#license)

## Overview

This toolkit automates the complex process of migrating BizTalk Server artifacts to Azure Logic Apps Standard. It consists of three specialized migration tools plus an AI-powered MCP server:

### **ODXtoWFMigrator** - Orchestration Migration
- **Orchestration Parsing**: Extracts shapes, ports, messages, and logic from ODX files
- **Binding Analysis**: Processes BizTalk bindings to configure Logic Apps connections
- **Expression Conversion**: Translates BizTalk XLANG expressions to Logic Apps expressions
- **Workflow Generation**: Creates valid Logic Apps Standard workflow JSON
- **Refactored Workflow Generation**: Pattern-based optimization with deployment target support (Cloud/OnPremises)
- **Connector Optimization**: Automatically upgrades connectors based on deployment target
- **Validation**: Verifies generated workflows for deployment readiness
- **Gap Analysis**: Identifies unsupported patterns and recommends alternatives
- **Package Generation**: Produces complete deployable artifacts
- **Callable Workflow Detection**: Automatically configures child workflows with Request triggers
- **Self-Recursion Handling**: Converts recursive calls to Until loops

### **BTMtoLMLMigrator** - Map Migration
- **BTM Parsing**: Extracts functoids, links, and transformations from BizTalk maps
- **Schema Analysis**: Processes source and target XSD schemas for namespace extraction
- **Functoid Translation**: Converts BizTalk functoids to Liquid template equivalents
- **Liquid Generation**: Creates Azure Logic Apps Data Mapper compatible templates
- **Batch Conversion**: Process multiple maps simultaneously

### **BTPtoLA** - Pipeline Migration
- **Pipeline Parsing**: Extracts stages, components, and configurations from BTP files
- **Component Mapping**: Maps BizTalk pipeline components to Logic Apps actions
- **Default Pipeline Detection**: Identifies PassThru, XMLReceive, XMLTransmit patterns
- **Workflow Generation**: Creates Logic Apps workflows with component equivalents
- **Stage Analysis**: Preserves pipeline stage semantics in workflows

### **BizTalkToLogicApps.MCP** - AI-Assisted Migration
- **Model Context Protocol Server**: Exposes migration tools to AI assistants
- **25+ AI Tools**: Analysis, conversion, validation, and configuration tools
- **Multi-Artifact Support**: Handles orchestrations, maps, and pipelines
- **Claude/VS Code Integration**: Works with Claude Desktop and VS Code MCP extensions

## Features

### Core Migration Capabilities

- **Orchestration Conversion**: Convert BizTalk ODX files to Logic Apps workflows
- **Refactored Conversion**: Pattern-based optimization with Cloud/OnPremises deployment targets
- **Binding Integration**: Extract transport configurations and adapter settings
- **Bindings-Only Mode**: Generate workflows from bindings without orchestration files
- **Batch Processing**: Process multiple orchestrations simultaneously
- **Self-Recursion Detection**: Automatically convert recursive calls to loops
- **Callable Workflow Support**: Detect and configure child workflows with Request triggers
- **Map Conversion**: Transform BizTalk maps (.btm) to Liquid templates (.lml)
- **Pipeline Conversion**: Convert BizTalk pipelines (.btp) to Logic Apps workflows

### Analysis & Reporting

- **Migration Readiness Reports**: HTML/Markdown reports with complexity scoring
- **Gap Analysis**: Identify unsupported patterns and migration blockers
- **ODX Analysis**: Scan entire directories for migration feasibility
- **Validation**: Built-in workflow validator catches issues before deployment
- **Batch Reports**: Consolidated reports for multiple orchestrations
- **Pipeline Analysis**: Detect default pipeline patterns and component compatibility

### Production-Ready Output

- **Deployment Packages**: Complete Logic Apps Standard packages
- **Connection Configuration**: Auto-generated connections.json
- **CI/CD Integration**: Azure DevOps YAML pipelines
- **PowerShell Scripts**: Ready-to-use deployment automation
- **Documentation**: Auto-generated README with deployment instructions

### Connector Support

#### File & Storage Connectors

| BizTalk Adapter | Logic Apps Connector | Status |
|----------------|---------------------|--------|
| FILE | FileSystem | Fully Supported |
| FTP/FTPS | Ftp | Fully Supported |
| SFTP | Sftp | Fully Supported |
| Azure Blob Storage | AzureBlob | Fully Supported |
| Azure Table Storage | AzureTable | Fully Supported |

#### Messaging & Integration Connectors

| BizTalk Adapter | Logic Apps Connector | Status |
|----------------|---------------------|--------|
| Azure Service Bus | ServiceBus | Fully Supported |
| Azure Event Hub | EventHub | Fully Supported |
| IBM MQ | IbmMq | Fully Supported |
| MSMQ | N/A | Manual mapping required |
| MLLP (HL7) | MLLP | Fully Supported |

#### Database Connectors

| BizTalk Adapter | Logic Apps Connector | Status |
|----------------|---------------------|--------|
| SQL Server | Sql | Fully Supported |
| Azure Cosmos DB | CosmosDb | Fully Supported |
| Oracle Database | OracleDb | Fully Supported |
| IBM Db2 | Db2 | Fully Supported |
| IBM Informix | Informix | Fully Supported |

#### Protocol & HTTP Connectors

| BizTalk Adapter | Logic Apps Connector | Status |
|----------------|---------------------|--------|
| HTTP/HTTPS | Http | Fully Supported |
| SMTP | SMTP | Fully Supported |
| WCF-* | Custom ServiceProvider | Custom implementation |

#### B2B & EDI Connectors

| BizTalk Component | Logic Apps Connector | Status |
|------------------|---------------------|--------|
| AS2 Encode/Decode | AS2 | Fully Supported |
| X12 Encode/Decode | X12 | Fully Supported |
| EDIFACT Encode/Decode | EDIFACT | Fully Supported |
| HL7 Encode/Decode | HL7 | Fully Supported |
| SWIFT MT Encode/Decode | SWIFT | Fully Supported |

#### Enterprise Connectors (Mainframe/Legacy)

| BizTalk Adapter | Logic Apps Connector | Status |
|----------------|---------------------|--------|
| SAP | SAP | Fully Supported |
| IBM CICS | CICS | Fully Supported |
| IBM IMS | IMS | Fully Supported |
| IBM Host File (VSAM) | HostFile | Fully Supported as offline parser (NO SNA connectivity) |

#### Transform & Validation Connectors

| BizTalk Operation | Logic Apps Action | Status |
|------------------|-------------------|--------|
| XML Validation | XmlValidation | Fully Supported |
| XML Transform (XSLT) | Xslt | Fully Supported |
| XML Parse | XmlParse | Fully Supported |
| XML Compose | XmlCompose | Fully Supported |
| Flat File Decode | FlatFileDecoding | Fully Supported |
| Flat File Encode | FlatFileEncoding | Fully Supported |
| JSON Decode | ParseJson | Fully Supported |
| JSON Encode | Compose | Fully Supported |
| Business Rules | Rules | Fully Supported |

#### Notes on Connector Support

**Fully Supported**: Direct connector or built-in action available with complete feature parity

**Custom Implementation**: Requires Azure Functions or custom connector development

**Manual Mapping Required**: No direct equivalent, requires alternative approach (e.g., Azure Storage Queue instead of MSMQ)

**Integration Account Required**: B2B connectors (AS2, X12, EDIFACT, SWIFT) require an Azure Integration Account for schemas, maps, agreements, and certificates

**Schema Upload**: XML/Flat File operations require schemas to be uploaded to Logic App artifacts (source: "LogicApp" in connector configuration)

## Architecture

### Solution Structure

```
BizTalkMigrator/
|
+-- ODXtoWFMigrator/                 # Orchestration to Workflow migration tool
|   +-- Program.cs                   # CLI entry point
|   +-- ProgramHelpers.cs            # CLI helper methods
|   +-- BizTalkOrchestrationParser.cs # ODX parsing
|   +-- BindingSnapshot.cs           # Binding XML parsing
|   +-- LogicAppsMapper.cs           # BizTalk -> Logic Apps mapping
|   +-- LogicAppJSONGenerator.cs     # Workflow JSON generation
|   +-- ExpressionMapper.cs          # Expression conversion
|   +-- ConnectorSchemaRegistry.cs   # Connector management
|   +-- WorkflowValidator.cs         # Validation logic
|   +-- OrchestrationReportGenerator.cs # Report generation
|   +-- OdxAnalyzer.cs               # Gap analysis
|   +-- ExceptionExtensions.cs       # Exception handling helpers
|   +-- Refactoring/                 # Pattern-based optimization
|       +-- RefactoredWorkflowGenerator.cs # Refactoring orchestrator
|       +-- RefactoringOptions.cs    # Configuration options
|       +-- WorkflowReconstructor.cs # Pattern transformations
|       +-- ConnectorOptimizer.cs    # Connector upgrades
|       +-- JsonPostProcessor.cs     # JSON post-processing
|
+-- BTMtoLMLMigrator/                # BizTalk Maps to Liquid Mapper
|   +-- Program.cs                   # CLI entry point
|   +-- BtmParser.cs                 # BTM file parser
|   +-- BtmMigrator.cs               # Migration orchestrator
|   +-- FunctoidTranslator.cs        # Functoid to Liquid conversion
|   +-- LmlGenerator.cs              # Liquid template generator
|   +-- Models.cs                    # Data models
|
+-- BTPtoLA/                         # Pipeline to Logic Apps conversion
|   +-- Program.cs                   # CLI entry point
|   +-- Services/
|   |   +-- PipelineParser.cs        # BTP file parser
|   |   +-- PipelineWorkflowMapper.cs # Pipeline to workflow mapper
|   |   +-- PipelineJSONGenerator.cs # Workflow JSON generator
|   |   +-- PipelineConnectorRegistry.cs # Connector mappings
|   +-- Models/
|       +-- PipelineDocument.cs      # Pipeline structure
|       +-- PipelineStage.cs         # Stage model
|       +-- PipelineComponent.cs     # Component model
|       +-- ComponentMetadata.cs     # Component metadata
|       +-- ComponentCategory.cs     # Category enumeration
|       +-- ComponentProperty.cs     # Component properties
|       +-- DefaultPipelineInfo.cs   # Default pipeline detection
|       +-- StageExecutionMode.cs    # Stage execution modes
|       +-- PipelineWorkflowModel.cs # Workflow model
|
+-- BizTalkToLogicApps.MCP/          # MCP Server for AI-assisted migration
|   +-- Program.cs                   # Server entry point
|   +-- Server/
|   |   +-- McpServer.cs             # MCP protocol server
|   |   +-- ToolRegistry.cs          # Tool registration
|   |   +-- ToolHandlers/            # Tool implementations
|   |       +-- AnalysisToolHandler.cs
|   |       +-- ConversionToolHandler.cs  # Includes refactored conversion
|   |       +-- MapConversionToolHandler.cs
|   |       +-- PipelineToolHandler.cs
|   |       +-- MappingToolHandler.cs
|   |       +-- ConfigurationToolHandler.cs
|   +-- Models/
|       +-- McpProtocol.cs           # Protocol models
|       +-- ToolSchemas.cs           # Tool schema definitions
|
+-- BizTalktoLogicApps.Tests/        # Comprehensive test suite
    +-- Unit/                        # Unit tests (isolated components)
    +-- Integration/                 # Integration tests (end-to-end)
    +-- Data/                        # Test data (ODX, BTM, BTP files)
    +-- Properties/
        +-- AssemblyInfo.cs
```

### Migration Architecture Overview

```
+-------------------------------------------------------------------------+
|                        BIZTALK SERVER ARTIFACTS                         |
+-------------------------------------------------------------------------+
|                                                                         |
|  +-----------------------+  +-------------------+  +-------------------+|
|  | Orchestrations (.odx) |  | Maps (.btm)       |  | Pipelines (.btp)  ||
|  | + Bindings (.xml)     |  | + Schemas (.xsd)  |  |                   ||
|  +-----------------------+  +-------------------+  +-------------------+|
|            |                         |                      |           |
+------------|-------------------------|----------------------|-----------+
             |                         |                      |
             v                         v                      v
    +----------------+        +----------------+      +----------------+
    | ODXtoWFMigrator|        |BTMtoLMLMigrator|      |    BTPtoLA     |
    +----------------+        +----------------+      +----------------+
             |                         |                      |
             v                         v                      v
    +----------------+        +----------------+      +----------------+
    |  Workflow.json |        | Liquid (.lml)  |      |  Workflow.json |
    +----------------+        +----------------+      +----------------+
             |                         |                      |
             +-------------------------|----------------------+
                                       |
                                       v
             +-------------------------------------------------------+
             |          AZURE LOGIC APPS STANDARD DEPLOYMENT         |
             +-------------------------------------------------------+
             |                                                       |
             |  +----------------+  +------------------+             |
             |  | Workflows      |  | Data Mapper      |             |
             |  | (Orchestrations|  | (Liquid Maps)    |             |
             |  |  + Pipelines)  |  |                  |             |
             |  +----------------+  +------------------+             |
             |                                                       |
             +-------------------------------------------------------+

                                    ^
                                    |
                    +----------------------------+
                    | BizTalkToLogicApps.MCP     |
                    | (AI-Assisted Migration)    |
                    +----------------------------+
                    | - Analysis Tools           |
                    | - Conversion Tools         |
                    | - Validation Tools         |
                    | - Configuration Tools      |
                    +----------------------------+
                              ^
                              |
                    +---------+---------+
                    |                   |
              +----------+        +-----------+
              | Claude   |        | VS Code   |
              | Desktop  |        | with MCP  |
              +----------+        +-----------+
```

### Individual Migration Pipelines

#### ODXtoWFMigrator - Orchestration Migration

```
+-------------------------+
|   BizTalk Orchestration |
|   (.odx) + Bindings     |
+-------------------------+
            |
            v
+---------------------------------------+
| BizTalkOrchestrationParser            |
| - Parse ODX XML structure             |
| - Extract shapes, ports, messages     |
+---------------------------------------+
            |
            v
+---------------------------------------+
| BindingSnapshot                       |
| - Extract adapter configurations      |
| - Parse transport settings            |
+---------------------------------------+
            |
            v
+---------------------------------------+
| LogicAppsMapper                       |
| - Map shapes to actions               |
| - Detect self-recursion               |
| - Detect callable workflows           |
+---------------------------------------+
            |
            v
+---------------------------------------+
| LogicAppJSONGenerator                 |
| - Generate workflow JSON              |
| - Apply connector schemas             |
+---------------------------------------+
            |
            v
+---------------------------------------+
| WorkflowValidator                     |
| - Validate schema compliance          |
| - Check semantic correctness          |
+---------------------------------------+
            |
            v
+---------------------------------------+
| Logic Apps Standard Workflow          |
| + Deployment Package                  |
| (connections.json, host.json, etc.)   |
+---------------------------------------+
```

#### BTMtoLMLMigrator - Map Migration

```
+-------------------------+
|   BizTalk Map (.btm)    |
| + Source/Target Schemas |
+-------------------------+
            |
            v
+---------------------------------------+
| BtmParser                             |
| - Parse map XML structure             |
| - Extract functoids and links         |
+---------------------------------------+
            |
            v
+---------------------------------------+
| Schema Analysis                       |
| - Parse source XSD schema             |
| - Parse target XSD schema             |
| - Extract namespaces                  |
+---------------------------------------+
            |
            v
+---------------------------------------+
| FunctoidTranslator                    |
| - Convert functoids to Liquid syntax  |
| - Map string/math/logical operations  |
| - Handle scripting functoids          |
+---------------------------------------+
            |
            v
+---------------------------------------+
| LmlGenerator                          |
| - Generate Liquid template            |
| - Format output for readability       |
+---------------------------------------+
            |
            v
+---------------------------------------+
| Liquid Template (.lml)                |
| For Logic Apps Data Mapper            |
+---------------------------------------+
```

#### BTPtoLA - Pipeline Migration

```
+-------------------------+
| BizTalk Pipeline (.btp) |
+-------------------------+
            |
            v
+---------------------------------------+
| PipelineParser                        |
| - Parse pipeline XML structure        |
| - Extract stages and components       |
| - Extract component properties        |
+---------------------------------------+
            |
            v
+---------------------------------------+
| DefaultPipelineDetection              |
| - Identify PassThruReceive            |
| - Identify XMLReceive/XMLTransmit     |
| - Detect standard patterns            |
+---------------------------------------+
            |
            v
+---------------------------------------+
| PipelineWorkflowMapper                |
| - Map components to Logic Apps actions|
| - Apply connector registry            |
| - Preserve stage semantics            |
+---------------------------------------+
            |
            v
+---------------------------------------+
| PipelineJSONGenerator                 |
| - Generate workflow JSON              |
| - Add Integration Account references |
+---------------------------------------+
            |
            v
+---------------------------------------+
| Logic Apps Standard Workflow          |
| With pipeline component equivalents   |
+---------------------------------------+
```

## Installation

### Prerequisites

- Windows operating system
- .NET Framework 4.7.2 or higher
- (Optional) Azure subscription for deployment
- (Optional) Claude Desktop or VS Code with MCP extension for AI-assisted migration

### Build from Source

```powershell
# Clone the repository
git clone https://github.com/hcampos_microsoft/BizTalkMigrator.git
cd BizTalkMigrator

# Build all projects
msbuild BizTalkMigrator.sln /t:Rebuild /p:Configuration=Release

# Executables will be in:
# - ODXtoWFMigrator\bin\Release\ODXtoWFMigrator.exe
# - BTMtoLMLMigrator\bin\Release\BTMtoLMLMigrator.exe
# - BTPtoLA\bin\Release\BTPtoLA.exe
# - BizTalkToLogicApps.MCP\bin\Release\BizTalkToLogicApps.MCP.exe
```

### Download Pre-Built Binary

[Link to releases page]

## Usage

### Command Reference

#### ODXtoWFMigrator - Orchestration Migration

```powershell
ODXtoWFMigrator <command> [options]
```

| Command | Description |
|---------|-------------|
| `migrate`, `convert` | Convert orchestration to Logic Apps workflow |
| `bindings-only` | Generate workflows from bindings only (no ODX) |
| `report`, `analyze` | Generate migration readiness report |
| `batch` | Process multiple orchestrations |
| `diagnose` | Run diagnostics on orchestration |
| `generate-package`, `package` | Create deployment package |
| `analyze-odx`, `gap-analysis` | Analyze ODX files for gaps |
| `help` | Show help information |

**Command-Line Options:**

| Option | Description |
|--------|-------------|
| `--refactor` | Use refactored workflow generator with pattern-based optimizations |
| `--messaging <system>` | Messaging platform: `servicebus`, `rabbitmq`, `kafka`, `ibmmq` |
| `--target <pattern>` | Target pattern hint: `aggregator`, `sequential-convoy`, etc. |
| `--format`, `-f` | Report format: `html` or `markdown` |
| `--output`, `-o` | Output file path |
| `--directory`, `-d` | Directory for batch processing |
| `--bindings`, `-b` | Bindings file path |
| `--schema-version` | Logic Apps schema version (default: 2016-06-01) |

#### BTMtoLMLMigrator - Map Migration

```powershell
BTMtoLMLMigrator <btm-file> <source-schema> <target-schema> [output-file]
```

| Parameter | Description |
|-----------|-------------|
| `btm-file` | Path to BizTalk map (.btm) file |
| `source-schema` | Path to source XSD schema |
| `target-schema` | Path to target XSD schema |
| `output-file` | Optional output path for .lml file |

#### BTPtoLA - Pipeline Migration

```powershell
BTPtoLA <pipeline-file.btp> [output-directory]
BTPtoLA /workflow <pipeline-file.btp> [output-directory]
BTPtoLA /batch <pipeline-file.btp> <output-directory>
```

| Command | Description |
|---------|-------------|
| `<file.btp>` | Parse and analyze pipeline |
| `<file.btp> <dir>` | Generate workflow to directory |
| `/workflow` | Generate workflow (explicit) |
| `/batch` | Non-interactive batch mode |

#### BizTalkToLogicApps.MCP - AI Server

```powershell
BizTalkToLogicApps.MCP.exe
```

Starts the MCP server for AI-assisted migration. See [BizTalkToLogicApps.MCP/README.md](BizTalkToLogicApps.MCP/README.md) for tool documentation.

### Common Workflows

#### 1. Single Orchestration Migration

```powershell
# Step 1: Generate readiness report
ODXtoWFMigrator.exe report MyOrchestration.odx --format html

# Step 2: Review report and address issues

# Step 3: Convert to Logic Apps (standard)
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml

# Step 3 (alternative): Convert with pattern-based optimizations
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor

# Step 4: Generate deployment package
ODXtoWFMigrator.exe package MyOrchestration.odx bindings.xml C:\Output
```

#### 2. Refactored Conversion (Pattern-Optimized)

```powershell
# Cloud deployment with Service Bus (default)
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor

# Cloud deployment with explicit Service Bus
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor --messaging servicebus

# On-premises deployment with RabbitMQ
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor --messaging rabbitmq

# On-premises deployment with Kafka
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor --messaging kafka

# On-premises deployment with IBM MQ
ODXtoWFMigrator.exe migrate MyOrchestration.odx bindings.xml --refactor --messaging ibmmq

# Batch refactored conversion
ODXtoWFMigrator.exe batch convert -d C:\Orchestrations -b bindings.xml --refactor
```

**Refactoring Benefits:**
- Uses native Logic Apps patterns (sessions for convoy, parallel branches for scatter-gather)
- Optimizes connector selection based on deployment target
- Simplifies complex BizTalk constructs (nested scopes ? flat)
- Generates parameters.json for externalized configuration

#### 3. Batch Orchestration Migration

```powershell
# Step 1: Analyze all orchestrations
ODXtoWFMigrator.exe batch report --directory C:\BizTalk\Orchestrations

# Step 2: Convert all orchestrations
ODXtoWFMigrator.exe batch convert `
    --directory C:\BizTalk\Orchestrations `
    --bindings C:\BizTalk\bindings.xml `
    --output C:\LogicApps

# Step 2 (alternative): Convert with refactoring optimizations
ODXtoWFMigrator.exe batch convert `
    --directory C:\BizTalk\Orchestrations `
    --bindings C:\BizTalk\bindings.xml `
    --output C:\LogicApps `
    --refactor
```

#### 4. Bindings-Only Migration

When you don't have ODX files, generate workflows from bindings:

```powershell
ODXtoWFMigrator.exe bindings-only bindings.xml C:\Output
```

This creates one workflow per receive location, with filtered send ports as actions.

#### 5. Gap Analysis

```powershell
# Analyze directory for unsupported patterns
ODXtoWFMigrator.exe analyze-odx C:\BizTalk\Orchestrations --output gap-report.json
```

#### 6. Map Migration

```powershell
# Convert single BizTalk map to Liquid template
BTMtoLMLMigrator.exe OrderToInvoice.btm Order.xsd Invoice.xsd

# Specify custom output location
BTMtoLMLMigrator.exe OrderToInvoice.btm Order.xsd Invoice.xsd C:\Output\OrderToInvoice.lml
```

#### 7. Pipeline Migration

```powershell
# Analyze a pipeline
BTPtoLA.exe XMLReceive.btp

# Generate workflow
BTPtoLA.exe XMLReceive.btp C:\Output

# Batch mode (non-interactive)
BTPtoLA.exe /batch XMLReceive.btp C:\Output
```

#### 8. AI-Assisted Migration

```powershell
# Start MCP server for Claude Desktop or VS Code
BizTalkToLogicApps.MCP.exe

# Then use AI assistant to analyze, convert, and validate BizTalk artifacts
# See BizTalkToLogicApps.MCP/README.md for available AI tools
```

### Output Files

#### Standard Migration Output

```
MyOrchestration.workflow.json       # Logic Apps workflow definition
```

#### Deployment Package Output

```
LogicAppsPackage/
|
+-- MyOrchestration/
|   +-- workflow.json               # Workflow definition
|
+-- connections.json                # API connection configuration
+-- host.json                       # Logic Apps Standard host config
+-- local.settings.json             # App settings template
+-- deploy.ps1                      # PowerShell deployment script
+-- azure-pipelines.yml             # Azure DevOps pipeline
+-- README.md                       # Deployment instructions
```

## Migration Approach

### Shape Mapping

| BizTalk Shape | Logic Apps Action | Notes |
|--------------|-------------------|-------|
| Receive (Activate) | ServiceProvider trigger | Starts workflow |
| Receive (non-Activate) | ServiceProvider action | Inline receive |
| Send | ServiceProvider action | Send operation |
| Construct | Compose | Message construction |
| Transform | Transform | XSLT mapping |
| Decision | Condition | If/else branching |
| Loop | Until | Iterative processing |
| Parallel | Parallel | Concurrent branches |
| Call | Workflow | Nested workflow |
| Delay | Delay | Timeout operations |
| Terminate | Terminate | Stop workflow |
| Scope | Scope | Transaction boundary |
| Expression | Compose | Variable assignment |

### Special Cases

#### Self-Recursion

BizTalk supports recursive orchestration calls, but Logic Apps does not. The tool automatically:

1. Detects self-recursive patterns
2. Converts to Until loop with condition
3. Preserves recursion logic

Example:

```
BizTalk: Call("SelfOrchestration")
    |
    v
Logic Apps: Until { Condition: not(condition), Actions: [...] }
```

#### Callable Workflows

Child orchestrations (called by other orchestrations) are detected and configured with:
- **Request trigger** instead of adapter trigger
- **Response action** for return values
- Proper parameter mapping

Detection uses:
- Naming patterns (`*Child*`, `*Sub*`, `*Helper*`)
- Absence of activating receive shape
- Cross-reference analysis (in batch mode)

## Examples

### Example 1: Orchestration - Simple Order Processing

**Input**: Order processing orchestration with FILE receive and SQL send

```powershell
ODXtoWFMigrator.exe migrate OrderProcessing.odx bindings.xml
```

**Output**: Logic Apps workflow with:
- FileSystem trigger (ReadFile)
- SQL action (ExecuteProcedure)
- Error handling scope

### Example 2: Orchestration - Complex Multi-Branch

**Input**: Multi-branch orchestration with parallel processing

```powershell
ODXtoWFMigrator.exe package ComplexOrch.odx bindings.xml
```

**Output**: Complete deployment package with:
- Parallel actions for concurrent processing
- Condition actions for decision branches
- Scope actions for error handling
- Deployment scripts and CI/CD pipeline

### Example 3: Orchestration - Batch Migration

**Input**: Directory with 50+ orchestrations

```powershell
ODXtoWFMigrator.exe batch convert `
    --directory C:\BizTalk `
    --bindings bindings.xml `
    --output C:\LogicApps
```

**Output**: 
- Individual workflow JSON for each orchestration
- Consolidated validation report
- Callable workflow detection across all files

### Example 4: Map - Order to Invoice Transformation

**Input**: BizTalk map with functoids transforming Order to Invoice

```powershell
BTMtoLMLMigrator.exe OrderToInvoice.btm Order.xsd Invoice.xsd
```

**Output**: Liquid template (.lml) with:
- Source to target field mappings
- Functoid equivalents (string, math, logical operations)
- Schema namespace references
- Ready for Logic Apps Data Mapper

### Example 5: Pipeline - XML Receive Pipeline

**Input**: Standard XMLReceive pipeline with MIME decoder and XML disassembler

```powershell
BTPtoLA.exe XMLReceive.btp C:\Output
```

**Output**: Logic Apps workflow with:
- Request trigger (for pipeline entry)
- Parse JSON action (XML disassembler equivalent)
- Integration Account schema validation
- Response action

## Testing

The solution includes a comprehensive test suite with 95%+ code coverage.

### Running Tests

```powershell
# Using Visual Studio Test Explorer
# 1. Open solution in Visual Studio
# 2. Test > Test Explorer
# 3. Click "Run All"

# Using command line
vstest.console.exe BizTalkToLogicApps.Tests\bin\Debug\BizTalkToLogicApps.Tests.dll
```

### Test Coverage

- **Unit Tests**: Expression mapping, binding parsing, shape conversion
- **Integration Tests**: End-to-end migration, workflow generation, validation
- **Regression Tests**: Self-recursion detection, workflow action format
- **Error Handling**: Invalid inputs, missing files, parse failures

See [BizTalkToLogicApps.Tests/README.md](BizTalkToLogicApps.Tests/README.md) for detailed test documentation.

## Project Status

### Supported Features

| Feature | Status | Notes |
|---------|--------|-------|
| ODX Parsing | Complete | All standard shapes supported |
| Binding Parsing | Complete | All standard adapters |
| Expression Mapping | Complete | 90%+ coverage |
| Workflow Generation | Complete | Valid Logic Apps JSON |
| Refactored Generation | Complete | Pattern-based optimization |
| Cloud Deployment | Complete | Service Bus, Event Hub, Cosmos DB |
| On-Premises Deployment | Complete | RabbitMQ, Kafka, IBM MQ |
| Validation | Complete | Schema and semantic validation |
| Self-Recursion Detection | Complete | Automatic conversion to loops |
| Callable Workflows | Complete | Request trigger support |
| Batch Processing | Complete | Multi-file support |
| Gap Analysis | Complete | Pattern detection |
| Report Generation | Complete | HTML/Markdown |
| Deployment Packages | Complete | CI/CD integration |
| Map Migration | Complete | BTM to Liquid conversion |
| Pipeline Migration | Complete | BTP to workflow conversion |
| MCP AI Tools | Complete | 25+ tools for AI assistants |

### Known Limitations

#### ODXtoWFMigrator (Orchestrations)
- XSLT content extraction not automated
- Correlation sets detected but not translated. Recommend the use of Logic Apps templates.
- Compensation logic requires manual testing
- Complex XPath expressions may need manual review

#### BTMtoLMLMigrator (Maps)
- Scripting functoids require manual conversion
- Custom XSLT not automatically converted
- Database lookup functoids need Logic Apps actions
- Flat file schemas not supported by Logic Apps Data Mapper
- Complex looping functoids may need manual review

#### BTPtoLA (Pipelines)
- Custom pipeline components require custom code or Azure Functions
- Some third-party components may not have Logic Apps equivalents
- Complex pipeline configurations may need manual review
- Party resolution requires Azure B2B account configuration
- BAM tracking must be reimplemented (Business Process tracking)

### Development Setup

```powershell
# Fork and clone the repository
git clone https://github.com/your-username/BizTalkToLogicApps.git

# Create a feature branch
git checkout -b feature/your-feature-name

# Make changes and commit
git commit -am "Add your feature"

# Run tests
dotnet test

# Push and create pull request
git push origin feature/your-feature-name
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Resources

- **Azure Logic Apps Documentation**: https://learn.microsoft.com/azure/logic-apps/
- **BizTalk Migration Documentation**: https://learn.microsoft.com/azure/logic-apps/biztalk-server-migration-overview
- **BizTalk Server Documentation**: https://learn.microsoft.com/biztalk/
- **Logic Apps Connectors**: https://learn.microsoft.com/connectors/connector-reference/
- **Model Context Protocol**: https://modelcontextprotocol.io/
- **YouTube resources**: https://www.youtube.com/@hcamposu

## Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/hcampos_microsoft/BizTalkMigrator/issues
- **Email**: harold_campos@hotmail.com

## Author

**Harold Campos** - harold_campos@hotmail.com

---

**Version**: 1.0.0  
**Last Updated**: January 2026  
**Maintained By**: Harold Campos (harold_campos@hotmail.com)

This is an independent project and is not affiliated with or endorsed by Microsoft Corporation.
