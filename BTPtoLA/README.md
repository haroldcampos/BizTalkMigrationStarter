# BizTalk Pipeline to Logic Apps Converter (BTPtoLA)

A .NET Framework 4.7.2 console application that parses BizTalk Pipeline (.btp) files and converts them to Azure Logic Apps workflow definitions. It supports both Receive and Send pipelines (mid-low complexity), extracting stages, components, and properties, and generates Logic Apps Standard workflow JSON file.

## *** Architecture

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
*** Models/
*   *** PipelineDocument       # Root pipeline model
*   *** PipelineStage          # Stage model (Decode, Disassemble, etc.)
*   *** PipelineComponent      # Component model with metadata
*   *** ComponentProperty      # Property model with type info
*   *** ComponentMetadata      # Component metadata and behavior
*   *** ComponentCategory      # Stage category GUID mapping
*   *** StageExecutionMode     # Execution mode enum (All, FirstMatch)
*   *** PipelineWorkflowModel  # Intermediate workflow model
*   *** DefaultPipelineInfo    # Default pipeline detection
*
*** Services/
*   *** PipelineParser            # XML -> Model parser
*   *** PipelineWorkflowMapper    # Model -> Workflow mapper
*   *** PipelineJSONGenerator     # Workflow -> JSON generator
*   *** PipelineConnectorRegistry # BizTalk component -> Logic Apps action mappings
*
*** Program.cs                 # CLI entry point
```

## Overview

This tool helps organizations migrate from **BizTalk Server** to **Azure Logic Apps** by automatically converting BizTalk Pipeline configurations into Logic Apps workflow JSON definitions.

## Features

- * Parse BizTalk Pipeline XML (.btp) files
- * Extract pipeline stages (Decode, Disassemble, Validate, ResolveParty, Assemble, Encode)
- * Extract pipeline components and their properties
- * **Detect Default Pipelines** (PassThruReceive, PassThruTransmit, XMLReceive, XMLTransmit)
- * Convert to Azure Logic Apps workflow definition (JSON)
- * Support for both Receive and Send pipelines
- * Console application with file or demo mode
- * Comprehensive metadata about stages, components, and execution modes

## Default Pipelines

BizTalk Server includes four default pipelines in the `Microsoft.BizTalk.DefaultPipelines` assembly. The tool automatically detects these patterns:

### 1. PassThruReceive Pipeline
**Pattern**: Receive pipeline with **no components** in any stage  
**Assembly**: Microsoft.BizTalk.DefaultPipelines  

**Use Cases**:
- Simple pass-through scenarios when no message processing is necessary
- Source and destination of message are known
- No validation, encoding, or disassembling required
- Commonly used with PassThruTransmit pipeline

**** Limitations**:
- * **Cannot route messages to orchestrations** (no disassembler)
- * **Does not support property promotion**

### 2. PassThruTransmit Pipeline
**Pattern**: Send pipeline with **no components** in any stage  
**Assembly**: Microsoft.BizTalk.DefaultPipelines

**Use Cases**:
- No document processing necessary before sending
- Simple message pass-through

**** Limitations**:
- * No message transformation or encoding

### 3. XMLReceive Pipeline
**Pattern**: Receive pipeline with XML Disassembler and Party Resolution  
**Assembly**: Microsoft.BizTalk.DefaultPipelines

**Stages Configuration**:
- **Decode**: Empty
- **Disassemble**: Contains XML Disassembler component
- **Validate**: Empty
- **ResolveParty**: Contains Party Resolution component

**Use Cases**:
- Processing XML messages
- Disassembling XML envelopes into individual documents
- Resolving parties from certificates or security IDs
- Property promotion from XML documents

**** Limitations**:
- ** **Does not support XML documents larger than 4 gigabytes**

### 4. XMLTransmit Pipeline
**Pattern**: Send pipeline with XML Assembler  
**Assembly**: Microsoft.BizTalk.DefaultPipelines

**Stages Configuration**:
- **Pre-Assemble**: Empty
- **Assemble**: Contains XML Assembler component
- **Encode**: Empty

**Use Cases**:
- Assembling XML messages with envelopes
- Serializing messages to XML format
- Moving context properties to message body

**** Limitations**:
- ** **Does not support XML documents larger than 4 gigabytes**

## BizTalk Pipeline Stages Supported

### Important: Execution Mode Rules

**Critical Information**:
- ** **Execution Mode is READ-ONLY** and cannot be changed
- ** **ALL Send Pipeline stages** use `All` execution mode
- ** **ALL Receive Pipeline stages EXCEPT Disassemble** use `All` execution mode
- ** **ONLY the Disassemble stage** uses `FirstMatch` execution mode

### Receive Pipeline Stages:

1. **Decode** - Decrypts/decodes messages (e.g., MIME/SMIME decoder)
   - **Execution Mode**: All (Read-Only)
   - **Behavior**: Takes one message and produces one message
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_Decoder (9d0e4103-4cce-4536-83fa-4a5040674ad6)

2. **Disassemble** - Splits and parses messages (e.g., XML disassembler, Flat file disassembler)
   - **Execution Mode**: FirstMatch (Read-Only) ** **ONLY STAGE WITH FirstMatch**
   - **Behavior**: Only the first component that recognizes the message format runs. Can produce 0-N messages
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_DisassemblingParser (9d0e4105-4cce-4536-83fa-4a5040674ad6)
   - **Important**: If no component recognizes the format, message processing fails

3. **Validate** - Validates messages against schemas (e.g., XML validator)
   - **Execution Mode**: All (Read-Only)
   - **Behavior**: Runs once per message created by Disassemble stage
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_Validate (9d0e410d-4cce-4536-83fa-4a5040674ad6)

4. **ResolveParty** - Resolves party information (e.g., Party resolution)
   - **Execution Mode**: All (Read-Only)
   - **Behavior**: Runs once per message created by Disassemble stage
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_PartyResolver (9d0e410e-4cce-4536-83fa-4a5040674ad6)

### Send Pipeline Stages:

1. **Pre-Assemble** - Pre-processing before assembly
   - **Execution Mode**: All (Read-Only)
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_Any (9d0e4101-4cce-4536-83fa-4a5040674ad6)

2. **Assemble** - Assembles messages (e.g., XML assembler, Flat file assembler)
   - **Execution Mode**: All (Read-Only)
   - **Behavior**: Serializes messages and adds envelopes
   - **Component Range**: 0-1 component ** **MAXIMUM 1 ASSEMBLER**
   - **Component Category**: CATID_AssemblingSerializer (9d0e4107-4cce-4536-83fa-4a5040674ad6)

3. **Encode** - Encodes/encrypts messages (e.g., MIME/SMIME encoder)
   - **Execution Mode**: All (Read-Only)
   - **Component Range**: 0-255 components
   - **Component Category**: CATID_Encoder (9d0e4108-4cce-4536-83fa-4a5040674ad6)

## Pipeline Type Detection

The tool automatically detects pipeline type using:
- **Category ID** from the pipeline definition
  - Receive: `f66b9f5e-43ff-4f5f-ba46-885348ae1b4e`
  - Send: `8c6b051c-0ff5-4fc2-9ae5-5016cb726282`
- **Policy File Path** (e.g., BTSReceivePolicy.xml vs BTSTransmitPolicy.xml)

## Supported Components

BizTalk Server includes three types of pipeline components, which are all supported by this tool:

### 1. General Components
**Message Flow**: 1 message in -> 0-1 message out  
**Purpose**: Process one message and produce zero or one message

| Component | Supports Probing | Description |
|-----------|-----------------|-------------|
| **MIME/SMIME Decoder** | No | Decodes and decrypts MIME/SMIME messages, verifies signatures |
| **MIME/SMIME Encoder** | No | Encodes and encrypts messages in MIME/SMIME format, adds signatures |
| **Party Resolution** | No | Resolves party information from certificates or security IDs |
| **XML Validator** | No | Validates XML messages against specified schemas |
| **JSON Decoder** | No | Decodes JSON messages to XML |
| **JSON Encoder** | No | Encodes XML messages to JSON |

**Typical Stages**: Decode, Encode, Validate, ResolveParty

### 2. Assembling Components
**Message Flow**: 1 message in -> 1 message out  
**Purpose**: Convert XML to native format, add envelopes, move properties from context to document

| Component | Supports Probing | Description |
|-----------|-----------------|-------------|
| **XML Assembler** | No | Serializes XML, wraps in envelope, moves context properties to body |
| **Flat File Assembler** | No | Converts XML to flat file format according to schema |
| **BizTalk Framework Assembler** | No | Wraps messages with BizTalk Framework envelope for reliable messaging |
| **EDI Assembler** | No | Assembles EDI messages (X12, EDIFACT) |

**Typical Stage**: Assemble (Send pipelines only)

### 3. Disassembling Components
**Message Flow**: 1 message in -> 0-N messages out  
**Purpose**: Convert native format to XML, remove envelopes, split into individual documents, promote properties

| Component | Supports Probing | Description |
|-----------|-----------------|-------------|
| **XML Disassembler** | Yes | Disassembles XML envelopes into individual documents, promotes properties |
| **Flat File Disassembler** | Yes | Converts flat files to XML according to schema, splits into messages |
| **BizTalk Framework Disassembler** | Yes | Processes BizTalk Framework messages with reliable messaging |
| **EDI Disassembler** | Yes | Disassembles EDI messages (X12, EDIFACT) |

**Typical Stage**: Disassemble (Receive pipelines only)

**Key Responsibilities**:
- Convert non-XML messages to XML representation
- Remove envelopes and split into individual documents
- Promote properties from message body to context
- Set message type property (Namespace#RootElement) for routing

### Probing Functionality

**Disassembling components** support probing - they examine the first portion of the message to determine if they can process it.

**How Probing Works**:
1. Component checks the beginning of the message
2. If format is recognized, the component processes the entire message
3. If format is unknown, the next component in the stage is tried
4. In the Disassemble stage (FirstMatch execution mode), only the first component that successfully probes the message will run

## Usage

### Command Line

```bash
# Parse and analyze a BizTalk Pipeline file (no output directory)
BTPtoLA.exe <path-to-pipeline.btp>

# Convert pipeline and save workflow to output directory
BTPtoLA.exe <path-to-pipeline.btp> <output-directory>

# Explicit workflow generation mode
BTPtoLA.exe /workflow <path-to-pipeline.btp> [output-directory]

# Non-interactive batch mode (no prompts, errors to stderr)
BTPtoLA.exe /batch <path-to-pipeline.btp> <output-directory>

# Examples
BTPtoLA.exe C:\BizTalkPipelines\ReceivePipeline.btp               # Analyze only
BTPtoLA.exe C:\BizTalkPipelines\ReceivePipeline.btp C:\Output     # Generate workflow
```

> **Note**: The output filename is always `<PipelineName>_workflow.json` inside the output directory.

### Demo Mode

Run without arguments to see demos of all four default pipelines:

```bash
BTPtoLA.exe
```

**Demo Pipelines Included**:
1. **PassThruReceive** - Pass-through receive with no components
2. **XMLReceive** - XML receive with disassembler and party resolution
3. **PassThruTransmit** - Pass-through send with no components
4. **XMLTransmit** - XML send with assembler

## BizTalk Pipeline XML Structure

BizTalk Pipeline files (.btp) are XML documents with the following structure:

```xml
<*xml version="1.0" encoding="utf-16"*>
<Document PolicyFilePath="BTSReceivePolicy.xml" MajorVersion="1" MinorVersion="0">
  <Description />
  <Stages>
    <Stage CategoryId="9d0e4103-4cce-4536-83fa-4a5040674ad6">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.MIME_SMIME_Decoder</Name>
          <ComponentName>MIME/SMIME decoder</ComponentName>
          <Description>MIME/SMIME decoder component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name="AllowNonMIMEMessage">
              <Value xsi:type="xsd:boolean">false</Value>
            </Property>
          </Properties>
        </Component>
      </Components>
    </Stage>
  </Stages>
</Document>
```

## Stage Category IDs

The tool recognizes the following BizTalk Pipeline stage GUIDs and Component Categories:

### Component Categories (CATIDs)

Component categories define which stages a component can be placed in. This is called **Stage Affinity**.

| Component Category | Category ID | Allowed Stages | Description |
|--------------------|-------------|----------------|-------------|
| **CATID_Decoder** | 9d0e4103-4cce-4536-83fa-4a5040674ad6 | Decode | All decoding components should implement this category |
| **CATID_DisassemblingParser** | 9d0e4105-4cce-4536-83fa-4a5040674ad6 | Disassemble | All disassembling and parsing components should implement this category |
| **CATID_Validate** | 9d0e410d-4cce-4536-83fa-4a5040674ad6 | Validate | Validation components should implement this category |
| **CATID_PartyResolver** | 9d0e410e-4cce-4536-83fa-4a5040674ad6 | ResolveParty | Stage used for Party Resolution component |
| **CATID_Encoder** | 9d0e4108-4cce-4536-83fa-4a5040674ad6 | Encode | All encoding components should implement this category |
| **CATID_AssemblingSerializer** | 9d0e4107-4cce-4536-83fa-4a5040674ad6 | Assemble | All serializing and assembling components should implement this category |
| **CATID_Any** | 9d0e4101-4cce-4536-83fa-4a5040674ad6 | Any stage | Components can be placed into any stage of a pipeline |

### Stage Affinity

- **COM-based components** express stage affinity by registering the stage ID as their implementation category
- **.NET-based components** specify stage affinity using the `ComponentCategory` class attribute
- **Multiple categories**: A component can implement more than one category to be available in multiple stages

### Receive Pipeline Stages:
| Stage | Category ID |
|-------|-------------|
| Decode | 9d0e4103-4cce-4536-83fa-4a5040674ad6 |
| Disassemble | 9d0e4105-4cce-4536-83fa-4a5040674ad6 |
| Validate | 9d0e410d-4cce-4536-83fa-4a5040674ad6 |
| ResolveParty | 9d0e410e-4cce-4536-83fa-4a5040674ad6 |

### Send Pipeline Stages:
| Stage | Category ID |
|-------|-------------|
| Pre-Assemble | 9d0e4101-4cce-4536-83fa-4a5040674ad6 |
| Assemble | 9d0e4107-4cce-4536-83fa-4a5040674ad6 |
| Encode | 9d0e4108-4cce-4536-83fa-4a5040674ad6 |

## Output Format

The tool generates an Azure Logic Apps workflow definition in JSON format. The output file is always wrapped in a `kind`/`definition` envelope:

```json
{
  "kind": "Stateful",
  "definition": {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "triggers": {
      "When_a_message_is_received": {
        "type": "Request",
        "kind": "Http"
      }
    },
    "actions": {
      "XML_disassembler": {
        "type": "Foreach",
        "foreach": "@triggerBody()?['items']",
        "actions": {
          "Parse_XML_with_Schema": {
            "type": "XmlParse",
            "inputs": {
              "content": "@items('XML_disassembler')?['$content']",
              "schema": {
                "source": "LogicApp",
                "name": "SCHEMA_NAME_HERE"
              }
            },
            "runAfter": {}
          }
        },
        "runAfter": {}
      },
      "MIME_SMIME_decoder": {
        "type": "InvokeFunction",
        "inputs": {
          "functionName": "DecodeMimeSmimeMessage",
          "parameters": {
            "messageContent": "@triggerBody()?['$content']",
            "validateSignature": "true",
            "decryptMessage": "true"
          }
        },
        "runAfter": {}
      }
    },
    "outputs": {}
  }
}
```

### Action Types in Generated Workflows

- **`Foreach`** — wraps Disassembling components (XML/Flat File Disassembler) because they produce 0-N messages
- **`XmlParse`** — child of Foreach for XML Disassembler; requires schema uploaded to Logic App artifacts
- **`FlatFileDecoding`** — child of Foreach for Flat File Disassembler; requires schema in artifacts
- **`XmlCompose`** — for XML Assembler; requires schema in artifacts
- **`FlatFileEncoding`** — for Flat File Assembler; requires schema in artifacts
- **`XmlValidation`** — for XML Validator; requires schema in artifacts
- **`InvokeFunction`** — for MIME Decoder/Encoder and Party Resolution; requires Local Function deployment
- **`Xslt`** — for XSL Transform components; requires map uploaded to Logic App artifacts
- **`Compose`** — fallback for other/custom components; contains migration notes as comments in `inputs`

## Project Structure

```
BTPtoLA/
*** Models/
*   *** PipelineDocument.cs      # Root pipeline document model
*   *** PipelineStage.cs         # Pipeline stage model
*   *** PipelineComponent.cs     # Pipeline component model
*   *** ComponentProperty.cs     # Component property model
*   *** ComponentMetadata.cs     # Component metadata and behavior definitions
*   *** ComponentCategory.cs     # Stage category GUID mapping
*   *** StageExecutionMode.cs    # Execution mode enum (All, FirstMatch)
*   *** PipelineWorkflowModel.cs # Intermediate workflow model for JSON generation
*   *** DefaultPipelineInfo.cs   # Default pipeline detection logic
*** Services/
*   *** PipelineParser.cs           # XML parser for .btp files
*   *** PipelineWorkflowMapper.cs   # Maps pipeline model to workflow model
*   *** PipelineJSONGenerator.cs    # Generates Logic Apps workflow JSON
*   *** PipelineConnectorRegistry.cs # BizTalk component to Logic Apps action registry
*** Program.cs                   # Main console application
*** README.md                    # This file
```

## Technical Details

- **Target Framework**: .NET Framework 4.7.2
- **C# Version**: 7.3
- **Dependencies**: System.Xml (built-in)

## Component Mapping

The tool maps BizTalk pipeline components to Logic Apps action types via `PipelineWorkflowMapper`:

| BizTalk Component | Logic Apps Action Type | Notes |
|-------------------|----------------------|-------|
| XML Disassembler | `Foreach` → `XmlParse` | Foreach wraps XmlParse; schema upload required |
| Flat File Disassembler | `Foreach` → `FlatFileDecoding` | Foreach wraps FlatFileDecoding; schema upload required |
| XML Assembler | `XmlCompose` | Schema upload required |
| Flat File Assembler | `FlatFileEncoding` | Schema upload required |
| XML Validator | `XmlValidation` | Schema upload required |
| MIME/SMIME Decoder | `InvokeFunction` | Requires Local Function (e.g., `DecodeMimeSmimeMessage`) |
| MIME/SMIME Encoder | `InvokeFunction` | Requires Local Function (e.g., `EncodeMimeSmimeMessage`) |
| Party Resolution | `InvokeFunction` | Requires Local Function (e.g., `ResolvePartner`) |
| XSL Transform | `Xslt` | Map upload required |
| Other EDI / Custom | `Compose` | Placeholder — manual implementation required |

> **Note**: Disassembling components are wrapped in a `Foreach` action because they can produce 0-N output messages. Child action types (`XmlParse`, `FlatFileDecoding`) contain schema placeholder references that must be updated before deployment.

## Limitations

### ** Important: InvokeFunction Requirements for Custom Components

**For MIME Decoder, MIME Encoder, Party Resolution, and other custom components that use `InvokeFunction` actions:**

Your local Local Function **must have a `function.json` file** located in the following folder structure:

```
/home/site/wwwroot/lib/custom/InvokeFunction
```

**Critical Requirements:**
- The `function.json` file **must not be empty**
- The folder name usually matches your function name (e.g., `DecodeMimeSmimeMessage`, `EncodeMimeSmimeMessage`, `ResolvePartner`)
- This file is **required** for Logic Apps Standard to successfully invoke local functions

**Example folder structure:**
```
/home/site/wwwroot/
*** lib/
*   *** custom/
*       *** InvokeFunction/
*           *** DecodeMimeSmimeMessage/
*           *   *** function.json
*           *** EncodeMimeSmimeMessage/
*           *   *** function.json
*           *** ResolvePartner/
*               *** function.json
```

**Consequences if missing:**
- * Logic Apps will **fail to invoke the function**
- * You will get **validation errors** in the Azure Portal
- * The workflow **will not execute** correctly

### Other Limitations

- The tool provides a **structural conversion** from BizTalk to Logic Apps
- Custom pipeline components may require manual mapping
- Logic Apps actions use generic types (Compose) for most components
- Advanced BizTalk features may need custom Logic Apps connectors or Local Functions

## Future Enhancements

- [ ] Add more specific Logic Apps action mappings
- [ ] Support for custom pipeline components
- [ ] Schema mapping and transformation
- [ ] Batch file processing
- [ ] Integration with Azure deployment scripts

## References

- [BizTalk Pipeline Components Documentation](https://learn.microsoft.com/en-us/biztalk/core/about-pipelines-stages-and-components)
- [Azure Logic Apps Workflow Definition](https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-workflow-definition-language)

## Changelog

### v1.2.0 (February 2026)

#### New MCP Tools

- **`list_pipeline_connectors`** (BizTalkToLogicApps.MCP) — exposes all BizTalk pipeline component → Logic Apps action mappings from `PipelineConnectorRegistry`, with optional filtering by category, complexity, and custom-code requirement
- **`parse_pipeline_xml_content`** (BizTalkToLogicApps.MCP) — parses BizTalk pipeline XML from a raw string (no file required), returning pipeline type, stage structure, components, and optional default pattern detection

#### Documentation Fixes

- Fixed CLI syntax: second argument is an **output directory**, not an output file; output filename is always `<PipelineName>_workflow.json`
- Fixed demo mode description: removed incorrect "plus custom pipelines" claim (exactly 4 demos run)
- Fixed Component Architecture diagram: added missing `ComponentMetadata`, `ComponentCategory`, `StageExecutionMode`, `PipelineWorkflowModel`, `DefaultPipelineInfo` (Models) and `PipelineConnectorRegistry` (Services)
- Fixed Project Structure: removed non-existent `LogicAppsConverter.cs`; added all missing model and service files
- Fixed Component Mapping table: replaced stale 2-row table (XML Disassembler → ParseJson, All Others → Compose) with accurate full mapping covering `Foreach`/`XmlParse`, `FlatFileDecoding`, `XmlCompose`, `FlatFileEncoding`, `XmlValidation`, `InvokeFunction`, `Xslt`, and `Compose`
- Fixed Output Format JSON example: replaced incorrect flat structure (with `parameters` block and structured `inputs` object) with actual `kind`/`definition` envelope output; added action type reference section

### v1.0.0 (January 2026)

- Initial release

## ** License

MIT License - See LICENSE file in repository root.

## ** Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/haroldcampos/BizTalkMigrationStarter/issues


## ** Author

**Harold Campos**

---

**Version**: 1.2.0  
**Last Updated**: March 2026

**Related Projects**:
- **[ODXtoWFMigrator](../ODXtoWFMigrator/README.md)** - BizTalk orchestration to Logic Apps workflow conversion
- **[BTMtoLMLMigrator](../BTMtoLMLMigrator/README.md)** - BizTalk map to LML (Logic Apps Mapping Language) conversion
- **[BizTalkToLogicApps.MCP](https://github.com/haroldcampos/BizTalkMigrationStarter/blob/main/BizTalktoLogicApps.MCP/README.md)** - MCP server for AI-assisted migration
- [BizTalk Pipeline Components Documentation](https://learn.microsoft.com/biztalk/core/about-pipelines-stages-and-components)
- [Azure Logic Apps Workflow Definition](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-definition-language)
- [BizTalk Server Documentation](https://learn.microsoft.com/biztalk/)
