# ODXtoWFMigrator

## Overview

**BizTalk Orchestration (ODX) to Azure Logic Apps Workflow Converter** - Enterprise-grade migration tool that converts BizTalk Server orchestrations to Azure Logic Apps Standard workflows with full fidelity. The use of this tool is recommended as a migration starter, and not for a production solution. Azure Logic Apps and BizTalk Server follow different paradigms and have different architectures, so we don't recommend the outcome of this tool to be left as is but to be further optimized, and possibly redesigned, to leverage the full benefits of Azure Logic Apps.

##  Purpose

Automates the migration of BizTalk orchestrations to Azure Logic Apps by:
- **Parsing .odx files** and extracting all orchestration metadata
- **Mapping BizTalk shapes** to Logic Apps actions (Receive->Trigger, Send->Action, etc.)
- **Converting expressions** from XLANG to Logic Apps Workflow Definition Language
- **Resolving connectors** from BizTalk adapters to Azure Logic Apps connectors
- **Generating workflow JSON** ready for Azure deployment

##  Features

### Core Capabilities

-  **Complete Orchestration Parsing** - Extracts 38+ shape types, ports, messages, variables
-  **Binding Integration** - Merges orchestration logic with runtime bindings
-  **Expression Translation** - Converts XLANG C# to Logic Apps expressions
-  **Connector Mapping** - Maps BizTalk adapters (FILE, FTP, SQL, ServiceBus, WCF) to Logic Apps connectors
-  **Control Flow Conversion** - Handles Parallel, Loop, Decide, Scope, Exception Handling
-  **Self-Recursion Detection** - Converts recursive orchestration calls to retry loops
-  **Gap Analysis** - Identifies unsupported patterns and migration complexity
-  **Validation** - Ensures generated workflows comply with Azure schema
-  **Reporting** - Generates HTML/Markdown migration reports
-  **Refactored Workflow Generation** - Pattern-based optimization with deployment target support
-  **Bindings-Only Mode** - Generate workflows from BizTalk bindings without orchestration files
-  **Callable Workflow Detection** - Automatically detects child workflows for nested workflow support

### Supported BizTalk Shapes

| Category | Shapes |
|----------|--------|
| **Messaging** | Receive, Send, Construct, Transform, Message Assignment |
| **Control Flow** | Decide (If/Else), Loop, ForEach, While, Until |
| **Parallel** | Parallel, Listen, Task |
| **Orchestration** | Call, Start (with self-recursion detection) |
| **Exception** | Scope, Catch, Throw, Terminate, Suspend |
| **Transaction** | Atomic Transaction, Long-Running Transaction, Compensation |
| **Advanced** | Expression, Delay, Variable Declaration, Call Rules (BRE) |

### Supported BizTalk Adapters

| Adapter | Logic Apps Connector |
|---------|---------------------|
| FILE | File System |
| FTP/SFTP | FTP/SFTP |
| SQL | SQL Server |
| WCF-* | HTTP |
| MSMQ/ServiceBus | Azure Service Bus |
| SMTP | Office 365 Outlook / Gmail |
| HTTP/REST | HTTP |
| SAP | SAP ECC |
| DB2 | IBM DB2 |
| AS2/X12/EDIFACT | Integration Account (B2B) |
| HostApps (CICS/IMS/VSAM) | Host Apps Connectors |

##  Quick Start

### Prerequisites

- .NET Framework 4.7.2 or higher
- BizTalk Server orchestration (.odx) files
- BizTalk bindings export (.xml)

### Command-Line Usage

```cmd
ODXtoWFMigrator.exe <command> [options]
```

### Commands

| Command | Description |
|---------|-------------|
| `migrate`, `convert` | Convert BizTalk orchestration to Logic Apps workflow |
| `bindings-only`, `-b` | Generate workflows from bindings only (no orchestration) |
| `report`, `analyze` | Generate a migration readiness report |
| `batch` | Process multiple orchestrations |
| `diagnose` | Run diagnostics on an orchestration |
| `generate-package`, `package` | Create deployable Logic Apps Standard package |
| `analyze-odx`, `gap-analysis` | Analyze ODX files for gaps and unsupported patterns |
| `help` | Show help message |

### Examples

#### Example 1: Basic Conversion
```cmd
ODXtoWFMigrator.exe migrate OrderProcessing.odx bindings.xml output.json
```

#### Example 2: Refactored Conversion with Optimizations
```cmd
ODXtoWFMigrator.exe migrate OrderProcessing.odx bindings.xml --refactor
ODXtoWFMigrator.exe migrate OrderProcessing.odx bindings.xml --refactor --messaging servicebus
ODXtoWFMigrator.exe migrate OrderProcessing.odx bindings.xml --refactor --target aggregator --messaging rabbitmq
```

#### Example 3: Bindings-Only Mode (No Orchestration Required)
```cmd
ODXtoWFMigrator.exe bindings-only bindings.xml
ODXtoWFMigrator.exe bindings-only bindings.xml C:\Output --refactor
```

#### Example 4: Directory Analysis
```cmd
ODXtoWFMigrator.exe analyze-odx C:\BizTalk\Orchestrations --output analysis-report.json
```

#### Example 5: Generate Migration Report
```cmd
ODXtoWFMigrator.exe report OrderProcessing.odx --format html --output report.html
```

#### Example 6: Generate Deployment Package
```cmd
ODXtoWFMigrator.exe generate-package OrderProcessing.odx bindings.xml C:\Output
ODXtoWFMigrator.exe package OrderProcessing.odx bindings.xml --refactor --messaging ibmmq
```

#### Example 7: Batch Processing
```cmd
ODXtoWFMigrator.exe batch convert --directory C:\BizTalk\Orchestrations --bindings bindings.xml
ODXtoWFMigrator.exe batch convert -d C:\Orchestrations -b bindings.xml --refactor
```

### Command-Line Options

| Option | Description |
|--------|-------------|
| `--refactor` | Use refactored workflow generator with pattern-based optimizations |
| `--target <pattern>` | Target messaging pattern hint (aggregator, sequential-convoy, etc.) |
| `--messaging <system>` | Messaging system (servicebus, ibmmq, rabbitmq, kafka, sapodata, saperp) |
| `--format`, `-f` | Report format: html (default) or markdown/md |
| `--output`, `-o` | Output file path |
| `--directory`, `-d` | Process all .odx files in directory (batch mode) |
| `--bindings`, `-b` | Bindings file path |
| `--schema-version` | Logic Apps schema version (default: 2016-06-01) |

##  Architecture

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
|                            |  - Convoy ? Sessions
|                            |  - Scatter-Gather ? Parallel
|                            |  - Consolidate nested scopes
+----------------------------+
           |
           v
+----------------------------+
| ConnectorOptimizer         |  Optimize connector selections
|                            |  - MSMQ ? ServiceBus/RabbitMQ
|                            |  - FILE ? AzureBlob (cloud)
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

### Component Details

#### 1. BizTalkOrchestrationParser
**File**: `BizTalkOrchestrationParser.cs` (~2000 lines)

**Responsibilities**:
- Parse .odx XML files
- Extract 38 shape types into strongly-typed models
- Build shape hierarchy with parent-child relationships
- Extract port types, operations, messages

**Key Classes**:
- `OrchestrationModel` - Root orchestration metadata
- `ShapeModel` - Base class for all shapes (38 derived classes)
- `PortModel`, `MessageModel`, `PortTypeModel`

#### 2. BindingSnapshot
**File**: `BindingSnapshot.cs` (~600 lines)

**Responsibilities**:
- Parse BizTalk bindings XML exports
- Extract receive locations with adapter configurations
- Extract send ports with transport details
- Preserve WCF metadata (security, encoding, timeouts)

**Key Classes**:
- `BindingSnapshot` - Root bindings container
- `BindingReceiveLocation` - Receive location with adapter metadata
- `BindingSendPort` - Send port with transport configuration

#### 3. LogicAppsMapper
**File**: `LogicAppsMapper.cs` (~1500 lines)

**Responsibilities**:
- Convert BizTalk shapes to Logic Apps actions
- Map control flow (Parallel, Loop, Decide)
- Handle self-recursive orchestration calls
- Create trigger from receive locations
- Flatten variable declarations to workflow root

**Key Methods**:
- `MapToLogicApp()` - Main conversion entry point
- `ConvertShape()` - Recursive shape conversion
- `IsSelfRecursiveCall()` - Detect circular calls

#### 4. ExpressionMapper
**File**: `ExpressionMapper.cs` (~400 lines)

**Responsibilities**:
- Translate XLANG C# expressions to Logic Apps WDL
- Convert operators (&&->and, ==->equals, etc.)
- Map method calls (.ToUpper()->toUpper())
- Handle message/variable references

**Examples**:
- `orderCount > 10` ? `@greater(variables('orderCount'), 10)`
- `Message.OrderHeader.Status == "Active"` ? `@equals(body('Message')['OrderHeader']['Status'], 'Active')`
- `a == 1 && b == 2 && c == 3` ? `@and(equals(variables('a'), 1), and(equals(variables('b'), 2), equals(variables('c'), 3)))` *(N-ary support)*

#### 5. LogicAppJSONGenerator
**File**: `LogicAppJSONGenerator.cs` (~1400 lines)

**Responsibilities**:
- Generate Azure Logic Apps workflow JSON
- Create triggers with connector parameters
- Generate actions with runAfter dependencies
- Handle WCF metadata conversion
- Apply connector schemas from registry

**Output**: Standards-compliant `workflow.json` for Azure deployment

### Supporting Components

#### ConnectorSchemaRegistry
**File**: `ConnectorSchemaRegistry.cs` (~300 lines)

Maps BizTalk adapters to Logic Apps connectors with operation schemas:
```json
{
  "Connectors": {
    "FileSystem": {
      "ServiceProviderId": "/serviceProviders/fileSystem",
      "Triggers": {
        "whenFilesAreAdded": {
          "OperationId": "whenFilesAreAdded",
          "Parameters": ["folderPath", "fileMask"]
        }
      }
    }
  }
}
```

### Refactoring Components (Pattern-Optimized Generation)

#### RefactoredWorkflowGenerator
**File**: `Refactoring/RefactoredWorkflowGenerator.cs` (~200 lines)

**Responsibilities**:
- Orchestrate pattern-based workflow generation
- Coordinate detection, optimization, and generation phases
- Handle deployment target validation

**Key Methods**:
- `GenerateRefactoredWorkflow()` - Main entry point for refactored generation
- `GenerateRefactoredWorkflowToFile()` - Generate and save to file with parameters

#### RefactoringOptions
**File**: `Refactoring/RefactoringOptions.cs` (~150 lines)

**Configuration Options**:
- `Target` - DeploymentTarget (Cloud or OnPremises)
- `Strategy` - RefactoringStrategy (Conservative, Balanced, Aggressive)
- `PreferredMessagingPlatform` - ServiceBus, RabbitMQ, Kafka, IbmMq
- `PreferredDatabaseConnector` - Sql, CosmosDb, Postgres, OracleDb
- `SimplifyConvoyPatterns` - Use native session support
- `UseNativeParallelBranches` - Use Logic Apps parallel branches
- `ConsolidateNestedScopes` - Flatten unnecessary nesting
- `GenerateParametersJson` - Create parameters.json file

#### WorkflowReconstructor
**File**: `Refactoring/WorkflowReconstructor.cs` (~500 lines)

**Responsibilities**:
- Apply pattern-based transformations to workflows
- Convert Sequential Convoy to session-based messaging
- Optimize Scatter-Gather with native parallel branches
- Simplify Content-Based Router to Switch actions
- Consolidate nested scopes

**Key Methods**:
- `OptimizeWorkflow()` - Apply all pattern optimizations
- `ApplyConvoyOptimization()` - Session-based messaging
- `ApplyScatterGatherOptimization()` - Native parallel branches
- `ApplyContentBasedRouterOptimization()` - Switch simplification

#### ConnectorOptimizer
**File**: `Refactoring/ConnectorOptimizer.cs` (~350 lines)

**Responsibilities**:
- Upgrade legacy BizTalk adapters to modern Logic Apps connectors
- Validate connector availability for deployment target
- Replace cloud-only connectors for on-premises deployments

**Connector Upgrade Rules**:
| Original | Cloud Target | On-Premises Target |
|----------|--------------|-------------------|
| MSMQ | ServiceBus | RabbitMQ/Kafka/IbmMq |
| FILE | AzureBlob (optional) | FileSystem (unchanged) |
| ServiceBus | ServiceBus | RabbitMQ/Kafka (required) |
| EventHub | EventHub | Kafka/RabbitMQ (required) |
| CosmosDB | CosmosDB | SQL (required) |
| AzureBlob | AzureBlob | FileSystem (required) |

#### JsonPostProcessor
**File**: `Refactoring/JsonPostProcessor.cs` (~200 lines)

**Responsibilities**:
- Apply final JSON-level optimizations
- Add pattern metadata to workflow definition
- Generate parameters.json file
- Extract configurable values (connection strings, paths)

#### OdxAnalyzer
**File**: `OdxAnalyzer.cs` (~500 lines)

Performs gap analysis on directories of orchestrations:
- Counts shape types and frequencies
- Identifies patterns requiring attention (correlation, atomic transactions, compensation)
- Calculates complexity metrics
- Generates recommendations

#### OrchestrationReportGenerator
**File**: `OrchestrationReportGenerator.cs` (~1300 lines)

Generates migration reports:
- HTML/Markdown formatted output
- Pattern detection (10 Enterprise Integration Patterns)
- Conversion strategy recommendations
- Risk assessment

##  Project Structure

```
ODXtoWFMigrator/
??? BizTalkOrchestrationParser.cs    # ODX parsing (2000 lines, 40 classes)
??? BindingSnapshot.cs               # Bindings parsing (600 lines, 3 classes)
??? LogicAppsMapper.cs               # Shape->Action conversion (1500 lines)
??? ExpressionMapper.cs              # XLANG->WDL translation (400 lines)
??? LogicAppJSONGenerator.cs         # JSON generation (1400 lines, 50+ methods)
??? ConnectorSchemaRegistry.cs       # Connector mapping (300 lines)
??? OdxAnalyzer.cs                   # Gap analysis (500 lines)
??? OrchestrationReportGenerator.cs  # Report generation (1300 lines)
??? WorkflowValidator.cs             # Schema validation (200 lines)
??? WorkflowValidation.cs            # Validation utilities (30 lines)
??? ExceptionExtensions.cs           # Fatal exception handling
??? Program.cs                       # CLI entry point (1200+ lines)
??? ProgramHelpers.cs                # CLI helper methods
??? Properties/AssemblyInfo.cs       # Assembly metadata
??? Refactoring/                     # Pattern-based optimization (NEW)
?   ??? RefactoredWorkflowGenerator.cs  # Refactoring orchestrator (200 lines)
?   ??? RefactoringOptions.cs           # Configuration options (150 lines)
?   ??? WorkflowReconstructor.cs        # Pattern transformations (500 lines)
?   ??? ConnectorOptimizer.cs           # Connector upgrades (350 lines)
?   ??? JsonPostProcessor.cs            # JSON post-processing (200 lines)
??? Schemas/
    ??? Connectors/
        ??? connector-registry.json     # Connector definitions
```

**Total**: ~10,500+ lines of code

##  Programmatic Usage

### Basic Conversion

```csharp
using BizTalktoLogicApps.ODXtoWFMigrator;

// Parse orchestration
var orchestration = BizTalkOrchestrationParser.ParseOdx(@"C:\BizTalk\OrderProcessing.odx");

// Parse bindings
var binding = BindingSnapshot.Parse(@"C:\BizTalk\bindings.xml");

// Map to Logic Apps
var map = LogicAppsMapper.MapToLogicApp(orchestration, binding);

// Generate JSON
var registry = ConnectorSchemaRegistry.CreateDefault();
string workflowJson = LogicAppJSONGenerator.GenerateStandardWorkflow(map, "Stateful", "2020-05-01-preview", registry);

// Save
File.WriteAllText(@"C:\Output\workflow.json", workflowJson);
```

### Refactored Workflow Generation (Pattern-Optimized)

```csharp
using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;

// Configure refactoring options
var options = new RefactoringOptions
{
    // Deployment target
    Target = DeploymentTarget.Cloud,  // or DeploymentTarget.OnPremises
    
    // Refactoring strategy
    Strategy = RefactoringStrategy.Balanced,  // Conservative, Balanced, or Aggressive
    
    // Messaging platform (auto-selected based on target if not specified)
    PreferredMessagingPlatform = "ServiceBus",  // ServiceBus, RabbitMQ, Kafka, IbmMq
    
    // Pattern optimizations
    SimplifyConvoyPatterns = true,       // Use native sessions
    UseNativeParallelBranches = true,    // Use Logic Apps parallel
    ConsolidateNestedScopes = true,      // Flatten unnecessary scopes
    
    // Output options
    IncludePatternComments = true,       // Add metadata to workflow
    GenerateParametersJson = true,       // Create parameters file
    WorkflowType = "Stateful"
};

// Generate refactored workflow
string workflowJson = RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
    @"C:\BizTalk\OrderProcessing.odx",
    @"C:\BizTalk\bindings.xml",
    options);

// Or generate directly to file (also creates parameters.json)
RefactoredWorkflowGenerator.GenerateRefactoredWorkflowToFile(
    @"C:\BizTalk\OrderProcessing.odx",
    @"C:\BizTalk\bindings.xml",
    @"C:\Output\workflow.json",
    options);
```

### On-Premises Deployment with RabbitMQ

```csharp
using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;

// Configure for on-premises with RabbitMQ
var options = new RefactoringOptions
{
    Target = DeploymentTarget.OnPremises,
    PreferredMessagingPlatform = "RabbitMQ",  // or "Kafka", "IbmMq"
    PreferredDatabaseConnector = "Sql",       // CosmosDB not available on-prem
    SimplifyConvoyPatterns = true,
    GenerateParametersJson = true
};

// Validate options (throws if Service Bus specified for on-prem)
options.Validate();

string workflowJson = RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
    odxPath, bindingsPath, options);
```

### Bindings-Only Workflow Generation

```csharp
using BizTalktoLogicApps.ODXtoWFMigrator;

// Parse bindings file
var bindings = BindingSnapshot.Parse(@"C:\BizTalk\bindings.xml");

// Generate workflows from bindings (one per receive location)
var workflows = LogicAppsMapper.MapBindingsToWorkflows(bindings);

Console.WriteLine($"Generated {workflows.Count} workflow(s)");

// Generate JSON for each workflow
var registry = ConnectorSchemaRegistry.CreateDefault();
foreach (var workflow in workflows)
{
    string json = LogicAppJSONGenerator.GenerateStandardWorkflow(
        workflow, "Stateful", "2016-06-01", registry);
    
    File.WriteAllText($@"C:\Output\{workflow.Name}\workflow.json", json);
}
```

### Directory Analysis

```csharp
var report = OdxAnalyzer.AnalyzeDirectory(@"C:\BizTalk\Orchestrations");

Console.WriteLine($"Total files: {report.TotalFilesAnalyzed}");
Console.WriteLine($"Success rate: {report.SuccessfullyParsed}/{report.TotalFilesAnalyzed}");
Console.WriteLine($"Unsupported shapes: {report.UnsupportedShapeFrequency.Count}");

// Print report
OdxAnalyzer.PrintReport(report);

// Save to JSON
OdxAnalyzer.SaveReportToJson(report, @"C:\Output\analysis.json");
```

### Generate Migration Report

```csharp
var reportHtml = OrchestrationReportGenerator.GenerateReport(
    orchestration,
    binding,
    format: "html"
);

File.WriteAllText(@"C:\Output\migration-report.html", reportHtml);
```

### Expression Translation

```csharp
var expression = "orderCount > 10 && orderStatus == \"Active\"";
var variableNames = new List<string> { "orderCount", "orderStatus" };

string logicAppsExpr = ExpressionMapper.MapExpression(expression, variableNames);
// Output: @and(greater(variables('orderCount'), 10), equals(variables('orderStatus'), 'Active'))
```

##  Data Flow Example

### Input: BizTalk Orchestration

**OrderProcessing.odx**:
```
Receive (OrderReceive) [Activate]
  |
  v
Decide (CheckOrderAmount)
  IF: Order.TotalAmount > 1000
    TRUE: Send (ApprovalRequired)
    FALSE: Send (AutoProcess)
  |
  v
Construct (BuildResponse)
  Transform (OrderToInvoice)
  |
  v
Send (SendInvoice)
```

**bindings.xml**:
```xml
<ReceiveLocation Name="OrderReceive">
  <TransportType>FILE</TransportType>
  <Address>C:\Orders\In\*.xml</Address>
</ReceiveLocation>
<SendPort Name="SendInvoice">
  <TransportType>FILE</TransportType>
  <Address>C:\Invoices\Out\%MessageID%.xml</Address>
</SendPort>
```

### Output: Logic Apps Workflow

**workflow.json** (simplified):
```json
{
  "definition": {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2020-05-01-preview/workflowdefinition.json#",
    "triggers": {
      "When_files_are_added": {
        "type": "ServiceProvider",
        "inputs": {
          "parameters": {
            "folderPath": "C:\\Orders\\In",
            "fileMask": "*.xml"
          },
          "serviceProviderConfiguration": {
            "connectionName": "filesystem",
            "operationId": "whenFilesAreAddedOrModified",
            "serviceProviderId": "/serviceProviders/fileSystem"
          }
        }
      }
    },
    "actions": {
      "CheckOrderAmount": {
        "type": "If",
        "expression": "@greater(body('OrderReceive')['TotalAmount'], 1000)",
        "actions": {
          "ApprovalRequired": { "type": "Compose" }
        },
        "else": {
          "actions": {
            "AutoProcess": { "type": "Compose" }
          }
        }
      },
      "BuildResponse_Transform": {
        "type": "Xslt",
        "inputs": {
          "content": "@body('OrderReceive')",
          "map": {
            "source": "OrderToInvoice"
          }
        }
      },
      "SendInvoice": {
        "type": "ServiceProvider",
        "inputs": {
          "parameters": {
            "filePath": "C:\\Invoices\\Out\\@{guid()}.xml",
            "body": "@body('BuildResponse_Transform')"
          }
        }
      }
    }
  }
}
```

##  Advanced Features

### Refactored Workflow Generation

The `--refactor` flag enables pattern-based optimization that produces cleaner, more maintainable workflows:

**Key Benefits**:
- Uses native Logic Apps patterns instead of BizTalk-style workarounds
- Optimizes connector selection based on deployment target
- Simplifies complex BizTalk constructs (convoy ? sessions, nested scopes ? flat)
- Generates deployment-ready workflows with parameters file

**Deployment Targets**:

| Target | Description | Available Connectors |
|--------|-------------|---------------------|
| **Cloud** | Azure Logic Apps Standard | All connectors including Service Bus, Event Hub, Cosmos DB |
| **OnPremises** | Logic Apps on Kubernetes/Docker | FileSystem, SQL, RabbitMQ, Kafka, IBM MQ, FTP, HTTP |

**Pattern Optimizations Applied**:

| Pattern | Before (BizTalk) | After (Logic Apps) |
|---------|------------------|-------------------|
| Sequential Convoy | Manual correlation | Service Bus/RabbitMQ sessions |
| Scatter-Gather | Parallel + correlation | Native parallel branches + join |
| Content-Based Router | Nested If/Else | Simplified Switch action |
| Nested Scopes | Deep nesting | Flattened structure |

### Self-Recursion Detection

BizTalk allows orchestrations to call themselves. Logic Apps doesn't support this.

**Detection**:
```csharp
private static bool IsSelfRecursiveCall(string invokee)
{
    // Compares Call/Start target against current orchestration name
    return invokee.Equals(_currentOrchestrationName, StringComparison.OrdinalIgnoreCase);
}
```

**Conversion**:
```json
// BizTalk: Call(SameOrchestration)
// Logic Apps: Until loop with retry logic
{
  "type": "Until",
  "expression": "@equals(variables('retryComplete'), true)",
  "actions": { /* orchestration body */ }
}
```

### Variable Declaration Flattening

**BizTalk**: Variables can be declared inside Scopes  
**Logic Apps**: InitializeVariable must be at workflow root

**Solution**: Two-pass processing
1. **Pass 1**: Collect all `VariableDeclarationShapeModel` from entire tree
2. **Pass 2**: Flatten to root-level actions before other shapes

### WCF Metadata Preservation

Preserves all WCF binding metadata for manual review:
- Security modes (Transport, Message, TransportWithMessageCredential)
- Client credentials (Windows, Certificate, Username)
- Message encoding (Text, MTOM)
- Timeouts (Open, Close, Send, Receive)
- Algorithm suites for encryption

**JSON Output**:
```json
{
  "trigger": {
    "metadata": {
      "securityMode": "Transport",
      "transportClientCredentialType": "Windows",
      "messageEncoding": "Text",
      "maxReceivedMessageSize": 65536
    }
  }
}
```

### Connector Schema Registry

**Custom Connectors**: Load from JSON
```csharp
var registry = ConnectorSchemaRegistry.LoadFromFile(@"C:\Config\connector-registry.json");
```

**Default Connectors**: Built-in for common adapters
```csharp
var registry = ConnectorSchemaRegistry.CreateDefault();
// Includes: HTTP, FileSystem, FTP, SQL, ServiceBus
```

### Bindings-Only Workflow Generation

Generate Logic Apps workflows from BizTalk bindings **without orchestration files**:

**Use Cases**:
- Customers who only have bindings exports available
- Migrating simple pass-through messaging scenarios
- Creating baseline workflows for manual enhancement

**How It Works**:
1. Parses all receive locations from bindings
2. Creates one workflow per receive location
3. Uses send port filters (`BTS.ReceivePortName`) to match send ports
4. Preserves transport metadata (WCF, HostApps, etc.)

**Command Line**:
```cmd
ODXtoWFMigrator.exe bindings-only bindings.xml C:\Output
```

**Output Structure**:
```
C:\Output\
??? ReceiveLocation1\
?   ??? workflow.json
??? ReceiveLocation2\
?   ??? workflow.json
??? connections.json
??? host.json
??? local.settings.json
```

### Callable Workflow Detection

The tool automatically detects callable (child) workflows during batch processing:

**Detection Methods**:
1. **Naming patterns**: Workflows with names containing "Child", "Sub", "Helper", "Reproceso"
2. **No activating receive**: Workflows without an activating receive shape
3. **Referenced by others**: Workflows called via Call/Start shapes in other orchestrations

**Impact**:
- Callable workflows use `Request` trigger (HTTP-triggered)
- Non-callable workflows use appropriate service triggers (File, Service Bus, etc.)
- Enables nested workflow invocation in Logic Apps

##  Known Limitations & Unsupported Scenarios

###  **NOT SUPPORTED IN LOGIC APPS - Cannot Convert**

#### 1. **Shapes Not Available in Logic Apps**

The following BizTalk shapes **do not have direct equivalents** in Azure Logic Apps:

| BizTalk Shape | Status | Reason | Workaround |
|---------------|--------|--------|------------|
| **Atomic Transaction** |  **Not Supported** | Logic Apps has no distributed transaction (DTC) support | Logic Apps (sagas), or accept eventual consistency |
| **Compensation** |  **Limited** | No built-in compensation framework | Manual compensating actions in catch blocks |
| **Convoy (Sequential/Parallel)** |  **Not Supported** | No message correlation like BizTalk | Service Bus sessions + custom logic, RabbitMQ + custom logic, Kafka + custom logic, or NMS (JMS) + custom logic  |

#### 2. **Advanced Correlation**

**Status**:  **Requires Redesign**

BizTalk correlation sets with multiple properties and complex initialization/follow patterns cannot be directly migrated.

**What Won't Work**:
- Correlation on multiple message properties
- Correlation across multiple receive shapes
- Parallel convoy patterns (multiple activating receives)
- Sequential convoy patterns (correlated receives after activation)

**Workaround**:
- **Service Bus Sessions** - Use sessionId for simple correlation
- **Cosmos DB** - Store correlation state externally
- **Redesign** - Rethink message flow without correlation

#### 3. **Dynamic Ports**

**Status**:  **Requires Custom Code**

BizTalk dynamic send ports (late-bound at runtime) requires custom code.

**Example**:
```csharp
// BizTalk: Dynamic port address set at runtime
DynamicSendPort(Microsoft.XLANGs.BaseTypes.Address) = 
    "file://C:\\" + Order.CustomerID + "\Output\";
```

**Workaround**:
- **Call local functions or use Inline code in workflow** - Determine endpoint and invoke Logic Apps connector
- **Switch Actions** - If limited destinations, use Switch/Case
- **Hardcode destinations** - If environment-specific, use app settings

#### 4. **Self-Recursive Orchestrations**

**Status**:  **Converted to Loops** (May Require Review)

BizTalk allows orchestrations to call themselves. Logic Apps cannot call itself (creates infinite loop).

**Automatic Conversion**:
```json
// BizTalk: Call(SameOrchestration)
// Converted to:
{
  "type": "Until",
  "expression": "@equals(variables('retryComplete'), true)",
  "actions": { /* orchestration logic */ }
}
```

** WARNING**: Review all converted self-recursive calls. The Until loop may not match original intent.

###  **Partial Support - Manual Review Required**

#### 1. **Exception Handling Differences**

**BizTalk**: Catch blocks can specify exception type  
**Logic Apps**: Catch blocks are generic (no type filtering)

**Impact**: All exceptions trigger the catch block. Add conditional logic inside catch to filter by error type.

**Example**:
```json
{
  "runAfter": { "TryScope": ["Failed"] },
  "actions": {
    "Filter_By_Exception_Type": {
      "type": "If",
      "expression": "@contains(actions('TryScope').error.code, 'FileNotFound')"
    }
  }
}
```

#### 2. **Scope Timeout Behavior**

**BizTalk**: Atomic/Long-Running scopes have explicit timeouts  
**Logic Apps**: Scopes don't timeout (action-level timeouts only)

**Impact**: Converted scopes may behave differently. Add explicit timeout actions if needed.

###  **HTTP Fallback for Unbound Send Shapes**

**Status**:  **Manual Configuration Required**

When the migration tool encounters Send shapes in BizTalk orchestrations that are not bound to physical send ports in the bindings export, or when send port bindings lack endpoint address information, the generator creates HTTP actions with a placeholder URI (`http://localhost/service`) as a defensive fallback. This ensures the workflow generates valid JSON and allows the migration to complete successfully, rather than failing the conversion. These HTTP placeholders indicate missing or incomplete bindings and should be manually replaced with proper Logic Apps connectors (File System, Service Bus, SQL, etc.) during post-migration configuration. You can identify these placeholders by searching for `"uri": "http://localhost/service"` in the generated workflow JSON.

###  **Default Message Body References with @triggerBody()**

**Status**:  **Manual Review Required**

The migration tool uses `@triggerBody()` as the default message body reference throughout generated workflows, including in Send actions, Transform actions, and connector parameters. While this works correctly for simple linear workflows where the trigger's message flows through the entire process, it becomes incorrect in workflows with message transformations, construct shapes, or multiple message variables. In BizTalk, each message is an independent variable that can be transformed, constructed, or reassigned; in the generated Logic Apps workflow, you must manually update message references to use the correct action outputs (e.g., `@body('TransformAction')` instead of `@triggerBody()` after a transformation, or `@variables('MessageVariable')` for constructed messages). Review all `@triggerBody()` references in the generated JSON and replace them with the appropriate action output references to ensure the correct message data flows through your workflow, particularly after Transform (XSLT) actions, Construct shapes, and Message Assignment operations.

###  **Message Format Limitations**

#### Flat File Messages

**Status**:  **Additional Actions Required**

Logic Apps does not natively parse flat file messages like BizTalk pipelines.

**BizTalk Flow**:
```
Receive (Flat File)  [Flat File Disassembler]  Orchestration  [Flat File Assembler]  Send
```

**Logic Apps Equivalent**:
```
Trigger  [Flat File Decode Action]  Workflow  [Flat File Encode Action]  Send
```

**Requirements**:
- Flat file schemas must be in **Integration Account**
- Add **Flat File Decoding/Encoding actions** before/after transformation
- Converter does NOT automatically add these actions

**Manual Steps**:
1. Upload flat file schemas to Integration Account
2. Add "Decode Flat File" action after trigger
3. Add "Encode Flat File" action before send

###  **Pre-Migration Checklist**

Before converting orchestrations, verify:

**Unsupported Shapes**:
- [ ] **No Compensation logic** (or plan manual compensating actions)
- [ ] **No Convoy patterns** (or plan Service Bus sessions redesign)
- [ ] **No Dynamic Ports** (or plan custom code routing logic)

**Correlation**:
- [ ] **No complex correlation sets** (or plan Service Bus sessions)
- [ ] **No sequential/parallel convoy** (or redesign)

**Messages**:
- [ ] **No flat file messages** (or plan Flat File actions + Integration Account)
- [ ] **All messages are XML/JSON** (or plan parsing logic)

**Self-Recursion**:
- [ ] **No self-recursive calls** (or review generated Until loops)

###  **How to Detect Unsupported Scenarios**

#### Use Gap Analysis Tool

```cmd
ODXtoWFMigrator.exe --analyze C:\BizTalk\Orchestrations --output gap-report.json
```

**Check Report For**:
- `FilesWithCorrelation` -> Requires Service Bus sessions or redesign
- `FilesWithConvoy` -> Requires architectural changes
- `FilesWithCompensation` -> Requires manual compensating logic
- `UnsupportedShapes` -> Cannot be automatically converted

#### Review Converter Warnings

Converter outputs warnings for problematic patterns:
```
WARNING: Self-recursive call detected - converted to Until loop
WARNING: Correlation set 'OrderCorrelation' - requires manual design
WARNING: Convoy pattern detected - requires architectural redesign
```

###  **Migration Complexity Matrix**

| Pattern | Complexity | Migration Approach |
|---------|-----------|-------------------|
| **Simple Send/Receive** |  Low | Automatic conversion |
| **Parallel/Loop/Decide** |  Low | Automatic conversion |
| **Exception Handling** |  Medium | Automatic + manual review |
| **Self-Recursion** |  Medium | Converted to loops (review) |
| **Transformations (XSLT)** |  Low | Automatic (XSLT action) |
| **Correlation Sets** |  High | Service Bus sessions or redesign |
| **Convoy Patterns** |  Very High | Architectural redesign required |
| **Atomic Transactions** |  Very High | Browse Lock/Peek Lock operations or accept eventual consistency |
| **Compensation** |  High | Manual compensating actions |
| **Dynamic Ports** |  Medium | Logic Apps control actions + Azure Functions routing |

###  **Recommended Migration Strategy**

1. **Run Gap Analysis** - Identify all unsupported patterns upfront
2. **Prioritize Simple Orchestrations** - Migrate low-complexity workflows first
3. **Redesign Complex Patterns** - Plan architectural changes for correlation/convoy

##  Troubleshooting

### Common Issues

#### Issue: "Orchestration file not found"
```
Error: Orchestration file not found: C:\BizTalk\OrderProcessing.odx
```
**Solution**: Verify .odx file path is correct and file exists

#### Issue: "Invalid ODX file: Missing XML declaration"
```
Error: Invalid ODX file 'OrderProcessing.odx': Missing XML declaration
```
**Solution**: Ensure file is a valid BizTalk orchestration (not corrupted). Open in Visual Studio, re-save.

#### Issue: "Failed to parse bindings"
```
Error: Failed to parse bindings: XML element 'BindingInfo' not found
```
**Solution**: Export bindings from BizTalk Admin Console, ensure XML is well-formed

#### Issue: "No connector found for adapter WCF-Custom"
```
Warning: Using fallback HTTP connector for WCF-Custom
```
**Solution**: Create custom connector schema in `connector-registry.json` or use HTTP with manual configuration

### Validation

#### Validate Generated Workflow
```csharp
var validator = new WorkflowValidator();
var errors = validator.Validate(@"C:\Output\workflow.json");

if (errors.Count == 0)
{
    Console.WriteLine("Workflow is valid!");
}
else
{
    foreach (var error in errors)
    {
        Console.WriteLine($"ERROR: {error}");
    }
}
```

##  Best Practices

### 1. Always Provide Bindings
```csharp
// GOOD - With bindings (accurate connector mapping)
var map = LogicAppsMapper.MapToLogicApp(orchestration, binding);

// BAD - Without bindings (falls back to HTTP trigger)
var map = LogicAppsMapper.MapToLogicApp(orchestration, null);  // DON'T DO THIS
```

### 2. Use Gap Analysis First
```csharp
// Analyze before converting
var report = OdxAnalyzer.AnalyzeDirectory(@"C:\BizTalk\Orchestrations");

// Review unsupported shapes
foreach (var unsupported in report.UnsupportedShapeFrequency)
{
    Console.WriteLine($"Unsupported: {unsupported.Key} ({unsupported.Value} occurrences)");
}
```

### 3. Review Self-Recursive Calls
```csharp
// Check for self-recursion in orchestration
var calls = orchestration.Shapes.OfType<CallShapeModel>();
foreach (var call in calls)
{
    if (call.Invokee == orchestration.Name)
    {
        Console.WriteLine($"WARNING: Self-recursive call detected: {call.Name}");
    }
}
```

### 4. Validate Before Deployment
```csharp
// Generate workflow
string json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, "Stateful");

// Validate schema compliance
var validator = new WorkflowValidator();
var errors = validator.Validate(json);

if (errors.Count > 0)
{
    Console.WriteLine("Workflow has validation errors!");
    // Fix issues before deploying
}
```

##  Integration with MCP Server

This library is wrapped by **BizTalktoLogicApps.MCP** for AI assistant integration:

### Standard Conversion Tool

```json
{
  "name": "convert_biztalk_to_logicapp",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\OrderProcessing.odx",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputPath": "C:\\Output\\workflow.json",
    "workflowType": "Stateful",
    "validateOutput": true
  }
}
```

### Refactored Conversion Tool (Pattern-Optimized)

```json
{
  "name": "convert_biztalk_to_logicapp_refactored",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\OrderProcessing.odx",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputPath": "C:\\Output\\workflow.json",
    "deploymentTarget": "Cloud",
    "refactoringStrategy": "Balanced",
    "messagingPlatform": "ServiceBus",
    "databaseConnector": "Sql",
    "simplifyConvoyPatterns": true,
    "useNativeParallelBranches": true,
    "consolidateNestedScopes": true,
    "generateParametersJson": true,
    "includePatternComments": true,
    "workflowType": "Stateful",
    "validateOutput": true
  }
}
```

### On-Premises Deployment Example

```json
{
  "name": "convert_biztalk_to_logicapp_refactored",
  "arguments": {
    "odxFilePath": "C:\\BizTalk\\OrderProcessing.odx",
    "bindingFilePath": "C:\\BizTalk\\bindings.xml",
    "outputPath": "C:\\Output\\workflow.json",
    "deploymentTarget": "OnPremises",
    "messagingPlatform": "RabbitMQ",
    "databaseConnector": "Sql",
    "generateParametersJson": true
  }
}
```

### MCP Tool Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `deploymentTarget` | enum | `Cloud` or `OnPremises` |
| `refactoringStrategy` | enum | `Conservative`, `Balanced`, `Aggressive` |
| `messagingPlatform` | enum | `ServiceBus`, `RabbitMQ`, `Kafka`, `IbmMq` |
| `databaseConnector` | enum | `Sql`, `CosmosDb`, `Postgres`, `OracleDb` |
| `simplifyConvoyPatterns` | bool | Use native sessions for convoy patterns |
| `useNativeParallelBranches` | bool | Use Logic Apps parallel for scatter-gather |
| `consolidateNestedScopes` | bool | Flatten unnecessary scope nesting |
| `generateParametersJson` | bool | Create separate parameters.json |
| `includePatternComments` | bool | Add pattern metadata to workflow |

See **BizTalkToLogicApps.MCP** project for MCP server usage.

##  Documentation Status

 **100% XML Documented** (98% overall project completion)

**Documented Components**:
-  BizTalkOrchestrationParser (100% - all 38 shape classes)
-  BindingSnapshot (100%)
-  LogicAppJSONGenerator (100%)
-  ExpressionMapper (100%)
-  ConnectorSchemaRegistry (100%)
-  OdxAnalyzer (100%)
-  WorkflowValidator (100%)
-  LogicAppsMapper (70% - excellent inline docs, needs XML tags)
-  RefactoredWorkflowGenerator (100%)
-  RefactoringOptions (100%)
-  WorkflowReconstructor (100%)
-  ConnectorOptimizer (100%)
-  JsonPostProcessor (100%)
-  ExceptionExtensions (100%)

##  Statistics

- **Total Lines**: ~10,500+
- **Classes**: 75+ (40 shape models + 35 utility classes)
- **Methods**: 250+
- **Properties**: 350+
- **Supported Shapes**: 38
- **Supported Adapters**: 25+
- **Refactoring Components**: 5 (Generator, Options, Reconstructor, Optimizer, PostProcessor)

##  Dependencies

### .NET Framework
- **Target**: .NET Framework 4.7.2
- **System.Xml** - XML parsing
- **System.Xml.Linq** - LINQ to XML

### NuGet Packages
- **Newtonsoft.Json** 13.0.3 - JSON serialization

## Changelog

### v1.1.0 (January 2026)

#### Bug Fixes

- **Fixed `ThreadLocal<string>` memory leak in `LogicAppsMapper`** — Replaced `ThreadLocal<string>` fields (`_currentOrchestrationName`, `_currentOrchestrationFullName`) with `[ThreadStatic]` static fields. `ThreadLocal<T>` implements `IDisposable` but was never disposed in the static class, causing memory leaks in long-running processes such as the MCP server. `[ThreadStatic]` provides identical per-thread isolation without requiring disposal.

- **Fixed `InvertCondition` ignoring parenthesized grouping** — De Morgan's law splits now respect parenthesis depth via a new `SplitAtTopLevel()` method. Previously, `"(a < 5 && b > 3) || c == 1"` was incorrectly split on the inner `&&` instead of the top-level `||`, producing malformed Until conditions when converting BizTalk While loops.

- **Fixed N-ary `&&`/`||` expressions falling back to string literals in `ExpressionMapper`** — `ConvertLogicalAnd` and `ConvertLogicalOr` now handle 3+ operands by building nested `and()`/`or()` calls (e.g., `a && b && c` ? `and(a, and(b, c))`). Previously, only binary expressions were converted; expressions with 3+ predicates were silently dropped as unconverted string literals.

#### Tests Added

- `InvertCondition_ParenthesizedCompound_RespectsGrouping`
- `InvertCondition_TernaryAnd_InvertsAllThreeParts`
- `MapExpression_TernaryAnd_ProducesNestedAndCalls`
- `MapExpression_TernaryOr_ProducesNestedOrCalls`
- `MapExpression_QuaternaryAnd_ProducesNestedAndCalls`

### v1.0.0 (January 2026)

- Initial release

##  License

MIT License - See LICENSE file in repository root.

##  Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/haroldcampos/BizTalkMigrationStarter/issues

##  Author

**Harold Campos** 

---

**Version**: 1.1.0  
**Last Updated**: January 2026

**Related Projects**:
- **[BTMtoLMLMigrator](../BTMtoLMLMigrator/README.md)** - BizTalk map to LML (Logic Apps Mapping Language) conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - BizTalk pipeline to Logic Apps conversion
- **[BizTalkToLogicApps.MCP](https://github.com/haroldcampos/BizTalkMigrationStarter/blob/main/BizTalktoLogicApps.MCP/README.md)** - MCP server for AI-assisted migration
- [Azure Logic Apps Documentation](https://learn.microsoft.com/azure/logic-apps/)
- [BizTalk Server Documentation](https://learn.microsoft.com/biztalk/)

