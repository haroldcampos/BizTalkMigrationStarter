# BizTalk Migration Starter - Architecture Overview

This document provides a comprehensive overview of all architecture diagrams for the BizTalk Migration Starter.

## System Overview

The BizTalk Migration Starter consists of four main components:

1. **ODXtoWFMigrator** - Orchestration to Workflow converter
2. **BTMtoLMLMigrator** - Map to Liquid template converter
3. **BTPtoLA** - Pipeline to Logic Apps converter
4. **BizTalkToLogicApps.MCP** - MCP Server for AI-assisted migration

---

## 1. ODXtoWFMigrator Architecture

### Six-Layer Pipeline (Standard Conversion)

```
+------------------------+
|  BizTalkOrchestration  |  Layer 1: Parse ODX XML
|       Parser           |  Extract shapes, ports, messages
+------------------------+
           |
           v
+------------------------+
|   BindingSnapshot      |  Layer 2: Parse bindings XML
|                        |  Extract adapters, addresses
+------------------------+
           |
           v
+------------------------+
|   LogicAppsMapper      |  Layer 3: Map to Logic Apps model
|                        |  Convert shapes -> actions
+------------------------+
           |
           v
+------------------------+
|  ExpressionMapper      |  Layer 4: Translate expressions
|                        |  XLANG -> WDL
+------------------------+
           |
           v
+------------------------+
| LogicAppJSONGenerator  |  Layer 5: Generate JSON
|                        |  Produce workflow.json
+------------------------+
           |
           v
+------------------------+
|  WorkflowValidator     |  Layer 6: Validate output
|                        |  Schema compliance check
+------------------------+
```

### Refactored Workflow Pipeline (Pattern-Optimized)

When using `--refactor`, additional optimization layers are applied:

```
+----------------------------+
| RefactoredWorkflowGenerator|  Orchestrator: Coordinates all phases
+----------------------------+
           |
           v
+----------------------------+
| Pattern Detection          |  Detect integration patterns
| (OrchestrationReportGen)   |  (Convoy, Scatter-Gather, Router)
+----------------------------+
           |
           v
+----------------------------+
| WorkflowReconstructor      |  Apply pattern optimizations
|                            |  - Convoy * Sessions
|                            |  - Scatter-Gather * Parallel
|                            |  - Consolidate nested scopes
+----------------------------+
           |
           v
+----------------------------+
| ConnectorOptimizer         |  Optimize connector selections
|                            |  - MSMQ * ServiceBus/RabbitMQ
|                            |  - FILE * AzureBlob (cloud)
|                            |  - Validate deployment target
+----------------------------+
           |
           v
+----------------------------+
| JsonPostProcessor          |  Final JSON enhancements
|                            |  - Add pattern metadata
|                            |  - Extract parameters
|                            |  - Format for readability
+----------------------------+
```

### Component Architecture

```
ODXtoWFMigrator/
*** Program.cs                       # CLI entry point
*** ProgramHelpers.cs                # CLI helper methods
*** BizTalkOrchestrationParser.cs    # ODX XML parsing
*** BindingSnapshot.cs               # Binding XML parsing
*** LogicAppsMapper.cs               # Shape -> Action conversion
*** ExpressionMapper.cs              # XLANG -> WDL translation
*** LogicAppJSONGenerator.cs         # Workflow JSON generation
*** ConnectorSchemaRegistry.cs       # Connector management
*** WorkflowValidator.cs             # Validation logic
*** OrchestrationReportGenerator.cs  # Report generation
*** OdxAnalyzer.cs                   # Gap analysis
*** ExceptionExtensions.cs           # Exception handling
*** Refactoring/                     # Pattern-based optimization
    *** RefactoredWorkflowGenerator.cs  # Refactoring orchestrator
    *** RefactoringOptions.cs           # Configuration options
    *** WorkflowReconstructor.cs        # Pattern transformations
    *** ConnectorOptimizer.cs           # Connector upgrades
    *** JsonPostProcessor.cs            # JSON post-processing
```

### Deployment Targets

| Target | Description | Available Connectors |
|--------|-------------|---------------------|
| **Cloud** | Azure Logic Apps Standard | Service Bus, Event Hub, Cosmos DB, Azure Blob |
| **OnPremises** | Logic Apps on Kubernetes/Docker | RabbitMQ, Kafka, IBM MQ, FileSystem, SQL |

### Connector Optimization Rules

| Original Adapter | Cloud Target | On-Premises Target |
|------------------|--------------|-------------------|
| MSMQ | ServiceBus | RabbitMQ/Kafka/IbmMq |
| FILE | AzureBlob (optional) | FileSystem |
| ServiceBus | ServiceBus | RabbitMQ/Kafka |
| EventHub | EventHub | Kafka/RabbitMQ |
| CosmosDB | CosmosDB | SQL |

### Purpose
Converts BizTalk Server orchestrations (.odx files) to Azure Logic Apps Standard workflows with full fidelity.

### Key Features
- * Complete orchestration parsing (38+ shape types)
- * Binding integration
- * Expression translation (XLANG * WDL)
- * Connector mapping
- * Control flow conversion
- * Pattern-based refactoring (NEW)
- * Cloud/On-Premises deployment targets (NEW)
- * Connector optimization (NEW)

---

## 2. BTMtoLMLMigrator Architecture

### Three-Phase Pipeline

```
+---------------+       +-------------------+       +--------------+
|   BtmParser   |  -->  | FunctoidTranslator|  -->  | LmlGenerator |
+---------------+       +-------------------+       +--------------+
    Phase 1                   Phase 2                   Phase 3
  Parse BTM             Translate Logic            Generate Liquid
```

### Purpose
Migrates BizTalk Server XSLT-based maps to Azure Logic Apps Data Mapper's Liquid format.

### Key Features
- * BTM parsing (functoids, links, schemas)
- * Functoid translation (50+ types)
- * XPath generation
- * Namespace handling
- * Schema-aware conversion

---

## 3. BTPtoLA Architecture

### Pipeline Conversion Flow

```
+-------------------+
|   Pipeline File   |  Input: BizTalk .btp XML
|    (.btp XML)     |
+-------------------+
          |
          v
+-------------------+
|  PipelineParser   |  Phase 1: Parse BTP XML
|                   |  Extract stages, components, properties
+-------------------+
          |
          v
+-------------------+
| PipelineWorkflow  |  Phase 2: Map to Logic Apps Model
|      Mapper       |  Convert components -> actions
|                   |  Detect default pipelines
+-------------------+
          |
          v
+-------------------+
|  PipelineJSON     |  Phase 3: Generate Workflow JSON
|    Generator      |  Create triggers, actions, parameters
+-------------------+
          |
          v
+-------------------+
|  Workflow JSON    |  Output: Logic Apps Standard workflow
| (workflow.json)   |
+-------------------+
```

### Component Architecture

```
BTPtoLA/
--- Models/
------ PipelineDocument       # Root pipeline model
------ PipelineStage          # Stage model (Decode, Disassemble, etc.)
------ PipelineComponent      # Component model with metadata
------ ComponentProperty      # Property model with type info
--
--- Services/
------ PipelineParser         # XML -> Model parser
------ PipelineWorkflowMapper # Model -> Workflow mapper
------ PipelineJSONGenerator  # Workflow -> JSON generator
--
--- Program.cs                 # CLI entry point
```

### Purpose
Parses BizTalk Pipeline (.btp) files and converts them to Azure Logic Apps workflow definitions.

### Key Features
- * Pipeline XML parsing
- * Stage extraction (Decode, Disassemble, Validate, etc.)
- * Component extraction with properties
- * Default pipeline detection
- * Workflow JSON generation

---

## 4. BizTalkToLogicApps.MCP Server Architecture

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
|  |  +-------------+  +-------------+  +-------------+       | |
|  |  |  Analysis   |  | Conversion  |  |   Mapping   |       | |
|  |  |    Tools    |  |    Tools    |  |    Tools    |       | |
|  |  +-------------+  +-------------+  +-------------+       | |
|  |  +-------------+  +-------------+  +-------------+       | |
|  |  |Configuration|  |  Pipeline   |  |     Map     |       | |
|  |  |    Tools    |  |    Tools    |  |    Tools    |       | |
|  |  +-------------+  +-------------+  +-------------+       | |
|  +----------------------------------------------------------+ |
+---------------------------------------------------------------+
                    | Direct Library Calls
                    v
+---------------------------------------------------------------+
|        BizTalk to Logic Apps Migration Libraries              |
|  +----------------------------------------------------------+ |
|  |  ODXtoWFMigrator                                         | |
|  |  ** BizTalkOrchestrationParser  ** LogicAppsMapper       | |
|  |  ** ConnectorSchemaRegistry     ** ExpressionMapper      | |
|  |  ** BindingSnapshot             ** WorkflowValidator     | |
|  |  ** OdxAnalyzer                 ** ReportGenerator       | |
|  |  ** Refactoring/                                         | |
|  |     ** RefactoredWorkflowGenerator                       | |
|  |     ** WorkflowReconstructor                             | |
|  |     ** ConnectorOptimizer                                | |
|  |     ** JsonPostProcessor                                 | |
|  +----------------------------------------------------------+ |
|  |  BTMtoLMLMigrator                                        | |
|  |  ** BtmParser         ** FunctoidTranslator              | |
|  |  ** LmlGenerator      ** BtmMigrator                     | |
|  +----------------------------------------------------------+ |
|  |  BTPtoLA                                                 | |
|  |  ** PipelineParser           ** PipelineWorkflowMapper   | |
|  |  ** PipelineJSONGenerator    ** PipelineConnectorRegistry| |
|  +----------------------------------------------------------+ |
+---------------------------------------------------------------+
```

### Tool Categories

| Category | Tools | Description |
|----------|-------|-------------|
| **Analysis** | analyze_biztalk_orchestration, analyze_odx_directory, generate_migration_report | Analyze artifacts for migration |
| **Conversion** | convert_biztalk_to_logicapp, convert_biztalk_to_logicapp_refactored, batch_convert | Convert orchestrations |
| **Refactored** | convert_biztalk_to_logicapp_refactored | Pattern-optimized conversion with deployment targets |
| **Pipeline** | analyze_biztalk_pipeline, convert_pipeline_to_workflow, batch_convert_pipelines | Pipeline migration |
| **Map** | analyze_btm_file, convert_btm_to_lml, batch_convert_btm_to_lml | Map migration |
| **Mapping** | map_biztalk_expression, resolve_connector_schema, parse_binding_file | Expression/connector mapping |
| **Configuration** | load_connector_registry, list_available_connectors, validate_workflow | Configuration tools |

### Refactored Conversion Tool

The `convert_biztalk_to_logicapp_refactored` tool supports:

| Parameter | Options | Description |
|-----------|---------|-------------|
| `deploymentTarget` | Cloud, OnPremises | Target deployment environment |
| `refactoringStrategy` | Conservative, Balanced, Aggressive | Optimization level |
| `messagingPlatform` | ServiceBus, RabbitMQ, Kafka, IbmMq | Preferred messaging |
| `databaseConnector` | Sql, CosmosDb, Postgres, OracleDb | Preferred database |
| `simplifyConvoyPatterns` | true/false | Use native sessions |
| `useNativeParallelBranches` | true/false | Use parallel branches |
| `consolidateNestedScopes` | true/false | Flatten nested scopes |
| `generateParametersJson` | true/false | Create parameters file |

### Purpose
MCP server that exposes BizTalk to Logic Apps migration toolkit as AI-accessible tools.

### Key Features
- * Tool discovery for AI assistants
- * Structured interaction (JSON-RPC)
- * Resource access (BizTalk & Logic Apps artifacts)
- * Pre-defined migration prompts
- * Refactored conversion with deployment targets (NEW)
- * 25+ migration tools

---

## Complete Migration Flow

```
BizTalk Artifacts
       |
       *** .odx (Orchestration)
       *         *
       *         *** Standard ******* ODXtoWFMigrator ******* workflow.json
       *         *
       *         *** Refactored ***** RefactoredWorkflowGenerator
       *                                      *
       *                              *****************
       *                              *               *
       *                           Cloud        On-Premises
       *                              *               *
       *                         ServiceBus      RabbitMQ
       *                         EventHub        Kafka
       *                         CosmosDB        IBM MQ
       *                              *               *
       *                              *****************
       *                                      *
       *                                      *
       *                              workflow.json
       *                              parameters.json
       *
       *** .btm (Map) *************** BTMtoLMLMigrator ******* .lml template
       *
       *** .btp (Pipeline) ********** BTPtoLA **************** workflow.json
                                                                    *
                                                                    *
                                                        *************************
                                                        *   Logic Apps Standard *
                                                        *   (Azure or K8s)      *
                                                        *************************

                      All orchestrated by
                             *
                   BizTalkToLogicApps.MCP
                    (AI-Assisted Migration)
                             *
                   *********************
                   *                   *
             Claude Desktop      VS Code MCP
```

##  Technology Stack

| Component | Framework | Language | Lines of Code |
|-----------|-----------|----------|---------------|
| **ODXtoWFMigrator** | .NET Framework 4.7.2 | C# | ~10,500 |
| **BTMtoLMLMigrator** | .NET Framework 4.7.2 | C# | ~1,800 |
| **BTPtoLA** | .NET Framework 4.7.2 | C# | ~2,500 |
| **BizTalkToLogicApps.MCP** | .NET Framework 4.7.2 | C# | ~3,500 |
| **Total** | | | **~18,300** |

## Migration Workflow

1. **Analysis Phase**
   - Use `analyze_biztalk_orchestration` (MCP)
   - Use `analyze_odx_directory` (MCP)
   - Review complexity reports

2. **Conversion Phase**
   - Convert orchestrations * ODXtoWFMigrator
     - Standard: `convert_biztalk_to_logicapp`
     - Optimized: `convert_biztalk_to_logicapp_refactored` (with deployment target)
   - Convert maps * BTMtoLMLMigrator
   - Convert pipelines * BTPtoLA

3. **Validation Phase**
   - Validate workflows with WorkflowValidator
   - Test expressions with ExpressionMapper
   - Verify connectors with ConnectorSchemaRegistry

4. **Deployment Phase**
   - Generate deployment packages (MCP)
   - Deploy to Azure Logic Apps Standard (Cloud) or Kubernetes (On-Premises)
   - Test and validate

## Refactoring Options Summary

### Deployment Targets
   - Logic Apps Standard (Azure)
   - Logic Apps Standard (Hybrid on AKS or On-prem Kubernetes)

### Pattern Optimizations

| Pattern | BizTalk Approach | Logic Apps Optimization |
|---------|------------------|------------------------|
| Sequential Convoy | Manual correlation | Service Bus/RabbitMQ sessions |
| Scatter-Gather | Parallel + correlation | Native parallel branches + join |
| Content-Based Router | Nested If/Else | Simplified Switch action |
| Nested Scopes | Deep nesting | Flattened structure |

## Related Documentation

- [ODXtoWFMigrator README](ODXtoWFMigrator/README.md)
- [BTMtoLMLMigrator README](BTMtoLMLMigrator/README.md)
- [BTPtoLA README](BTPtoLA/README.md)
- [BizTalkToLogicApps.MCP README](BizTalkToLogicApps.MCP/README.md)
- [Main README](README.md)

## Refactoring Components Detail

### RefactoredWorkflowGenerator
Orchestrates the pattern-based workflow generation process:
- Coordinates detection, optimization, and generation phases
- Handles deployment target validation
- Generates parameters.json when requested

### RefactoringOptions
Configuration options for refactored generation:
- `Target`: Cloud or OnPremises
- `Strategy`: Conservative, Balanced, or Aggressive
- `PreferredMessagingPlatform`: ServiceBus, RabbitMQ, Kafka, IbmMq
- `PreferredDatabaseConnector`: Sql, CosmosDb, Postgres, OracleDb

### WorkflowReconstructor
Applies pattern-based transformations:
- Sequential Convoy * Session-based messaging
- Scatter-Gather * Native parallel branches
- Content-Based Router * Switch simplification
- Nested Scopes * Flattened structure

### ConnectorOptimizer
Upgrades connectors based on deployment target:
- Validates connector availability
- Replaces cloud-only connectors for on-premises
- Applies preferred connector settings

### JsonPostProcessor
Final JSON-level optimizations:
- Adds pattern metadata
- Generates parameters.json
- Formats output for readability

---

**Version**: 1.1.0  
**Last Updated**: January 2026  
**Author**: Harold Campos
