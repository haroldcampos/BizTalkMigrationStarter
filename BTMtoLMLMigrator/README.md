# BTMtoLMLMigrator

## Overview

**BizTalk Map (BTM) to Liquid Mapping Language (LML) Converter** - Migrates BizTalk Server XSLT-based maps to Azure Logic Apps Data Mapper's Liquid format. You will need to have available the source and destination schemas, along with the BizTalk map.

## ** Purpose

Converts BizTalk transformation maps to Logic Apps Liquid templates, enabling:
- **Automated map migration** from BizTalk to Azure
- **Functoid translation** to Liquid filters and functions
- **XPath preservation** with namespace handling
- **Schema-aware conversion** for accurate data mapping

## * Features

### Core Capabilities

- * **BTM Parsing** - Extracts functoids, links, and schemas from .btm files
- * **Functoid Translation** - Converts 50+ functoid types to Liquid equivalents
- * **XPath Generation** - Creates Liquid accessor paths from BizTalk XPath
- * **Namespace Handling** - Preserves XML namespaces in Liquid templates
- * **Schema Integration** - Uses XSD schemas for accurate type mapping
- * **Liquid Generation** - Produces standards-compliant Liquid map files

### Supported Functoid Types

| Category | Functoids |
|----------|-----------|
| **String** | String Concatenate, Uppercase, Lowercase, Substring, String Find, Size |
| **Mathematical** | Add, Subtract, Multiply, Divide, Modulo, Absolute Value |
| **Logical** | Logical AND, Logical OR, Logical NOT, Equal, Not Equal, Greater Than, Less Than |
| **Conversion** | Value Mapping, Value Mapping (Flattening), Index, Iteration, Looping |
| **Advanced** | Scripting (C#/VB.NET), Table Looping, Custom XSLT |
| **Date/Time** | Date/Time formatting functions |
| **Cumulative** | Sum, Average, Min, Max, Count |

## ** Quick Start

### Prerequisites

- .NET Framework 4.7.2 or higher
- BizTalk Server map files (.btm)
- Source and target XSD schema files (recommended)

### Command-Line Usage

```cmd
BTMtoLMLMigrator.exe <btm-file> <source-schema> <target-schema> [output-file]
```

### Examples

#### Example 1: Basic Conversion
```cmd
BTMtoLMLMigrator.exe OrderToInvoice.btm Order.xsd Invoice.xsd OrderToInvoice.lml
```

#### Example 2: Auto-Named Output
```cmd
BTMtoLMLMigrator.exe CustomerUpdate.btm Customer.xsd UpdateRequest.xsd
# Creates CustomerUpdate.lml in same directory
```

#### Example 3: Complex Map with Functoids
```cmd
BTMtoLMLMigrator.exe ComplexTransform.btm SourceSchema.xsd TargetSchema.xsd Output.lml
```

## *** Architecture

### Three-Phase Pipeline

```
+---------------+       +-------------------+       +--------------+
|   BtmParser   |  -->  | FunctoidTranslator|  -->  | LmlGenerator |
+---------------+       +-------------------+       +--------------+
    Phase 1                   Phase 2                   Phase 3
  Parse BTM             Translate Logic            Generate Liquid
```

### Component Details

#### 1. BtmParser
**Responsibility**: Extract map metadata from .btm XML

**Extracts**:
- Source and target schemas
- Functoid definitions (type, parameters, inputs)
- Links between source -> functoid -> target
- XML namespaces for XPath generation

**Output**: `BtmMapData` model

#### 2. FunctoidTranslator
**Responsibility**: Convert BizTalk functoids to Liquid equivalents

**Translates**:
- String operations -> Liquid filters (`upcase`, `downcase`, `append`)
- Math operations -> Liquid arithmetic
- Logical operations -> Liquid conditionals (`if`, `unless`)
- Scripting functoids -> Liquid custom filters (manual review required)

**Output**: `BtmMapData` with translated functoids

#### 3. LmlGenerator
**Responsibility**: Generate Liquid template from translated map

**Generates**:
- Liquid template structure
- Assign blocks for field mappings
- Filter chains for transformations
- Conditional logic for branching
- Loops for repeating elements

**Output**: `.lml` Liquid template file

## ** Project Structure

```
BTMtoLMLMigrator/
*** BtmMigrator.cs           # Main orchestration class
*** BtmParser.cs             # BTM XML parsing
*** FunctoidTranslator.cs    # Functoid -> Liquid translation
*** LmlGenerator.cs          # Liquid template generation
*** Models.cs                # Data models (BtmMapData, Functoid, Link)
*** Program.cs               # CLI entry point
*** Properties/
    *** AssemblyInfo.cs      # Assembly metadata
```

## ** Programmatic Usage

### C# Integration

```csharp
using BizTalktoLogicApps.BTMtoLMLMigrator;

// Initialize migrator
var migrator = new BtmMigrator();

// Convert BTM to LML
string lmlContent = migrator.ConvertBtmToLml(
    btmFilePath: @"C:\BizTalk\Maps\OrderToInvoice.btm",
    sourceSchemaPath: @"C:\BizTalk\Schemas\Order.xsd",
    targetSchemaPath: @"C:\BizTalk\Schemas\Invoice.xsd"
);

// Write to file
File.WriteAllText(@"C:\Output\OrderToInvoice.lml", lmlContent);
```

### Parser Only (for Analysis)

```csharp
var parser = new BtmParser();
var mapData = parser.Parse(
    btmFilePath: @"C:\Maps\ComplexMap.btm",
    sourceSchemaPath: @"C:\BizTalk\Schemas\Order.xsd",
    targetSchemaPath: @"C:\BizTalk\Schemas\Invoice.xsd" 
);

Console.WriteLine($"Map has {mapData.Functoids.Count} functoids");
Console.WriteLine($"Map has {mapData.Links.Count} links");
```

### Translator Only (for Testing)

```csharp
var translator = new FunctoidTranslator();
var translatedMap = translator.TranslateFunctoids(
    mapData,
    sourceSchemaPath: @"C:\Schemas\Source.xsd",
    targetSchemaPath: @"C:\Schemas\Target.xsd"
);

// Inspect translated functoids
foreach (var functoid in translatedMap.Functoids)
{
    Console.WriteLine($"{functoid.FunctoidType}: {functoid.LiquidEquivalent}");
}
```

## ** Data Models

### BtmMapData
```csharp
public class BtmMapData
{
    public string BtmFilePath { get; set; }
    public string SourceSchema { get; set; }
    public string TargetSchema { get; set; }
    public List<Functoid> Functoids { get; set; }
    public List<Link> Links { get; set; }
    public Dictionary<string, string> SourceNamespaces { get; set; }
    public Dictionary<string, string> TargetNamespaces { get; set; }
}
```

### Functoid
```csharp
public class Functoid
{
    public string FunctoidId { get; set; }
    public string FunctoidFid { get; set; }
    public string FunctoidType { get; set; }
    public List<string> InputParameters { get; set; }
    public string ScripterCode { get; set; }
    public string LiquidEquivalent { get; set; }  // Set by translator
}
```

### Link
```csharp
public class Link
{
    public string LinkFrom { get; set; }  // Source field or functoid ID
    public string LinkTo { get; set; }    // Target field or functoid ID
}
```

## ** Functoid Translation Examples

### String Concatenate
**BizTalk**:
```xml
<Functoid Type="StringConcatenate">
  <Input>FirstName</Input>
  <Input>LastName</Input>
</Functoid>
```

**Liquid Output**:
```liquid
{% assign FullName = FirstName | append: ' ' | append: LastName %}
```

### Value Mapping with Condition
**BizTalk**:
```xml
<Functoid Type="ValueMapping">
  <Input>Status == 'Active'</Input>
  <Input>CustomerID</Input>
</Functoid>
```

**Liquid Output**:
```liquid
{% if Status == 'Active' %}
  {% assign MappedCustomerID = CustomerID %}
{% endif %}
```

### Mathematical Add
**BizTalk**:
```xml
<Functoid Type="Add">
  <Input>Subtotal</Input>
  <Input>Tax</Input>
</Functoid>
```

**Liquid Output**:
```liquid
{% assign Total = Subtotal | plus: Tax %}
```

## ** Advanced Features

### Schema-Aware XPath Generation

When schemas are provided, the converter:
1. Extracts XML namespace prefixes
2. Generates fully-qualified XPath expressions
3. Maps to Liquid accessor paths

**Example**:
- **BizTalk XPath**: `/ns0:Order/ns0:Items/ns0:Item`
- **Liquid Path**: `Order.Items.Item`

### Scripting Functoid Handling

For complex scripting functoids:
```liquid
{%- comment -%}
WARNING: Scripting functoid detected
Original C# code:
  public string FormatSSN(string ssn) {
    return ssn.Substring(0,3) + "-" + ssn.Substring(3,2) + "-" + ssn.Substring(5);
  }
Requires manual conversion to Liquid filter
{%- endcomment -%}
```

### Namespace Preservation

```liquid
{%- comment -%}
Source Namespaces:
  ns0 = http://Company.Schemas.Order
Target Namespaces:
  ns1 = http://Company.Schemas.Invoice
{%- endcomment -%}
```

## ** Known Limitations & Unsupported Scenarios

### * **NOT SUPPORTED - Cannot Convert**

#### 1. **Flat File Maps**
**Status**: * **NOT SUPPORTED**

Azure Logic Apps Data Mapper **does not support flat file processing**. Flat file maps require:
- BizTalk Flat File schemas (.xsd with flat file annotations)
- Flat File Assembler/Disassembler pipelines
- Positional or delimited parsing logic

**Workaround**:
- Use BizTalk Maps from Logic Apps. Runtime supports Flat file maps. Currently Flat file maps cannot be edited using the Data Mapper.

**Detection**: The converter does NOT check for flat file schemas. Review your BizTalk project for:
- Schemas with `<appinfo>` sections containing `<schemaInfo standard="Flat File">`
- Maps referencing schemas ending with patterns like `_FF.xsd`, `_FlatFile.xsd`

#### 2. **Scripting Functoids**
**Status**: ** **Requires Manual Conversion**

C#/VB.NET code in scripting functoids cannot be automatically converted to Liquid.

**Example**:
```csharp
// BizTalk Scripting Functoid
public string FormatSSN(string ssn) {
    return ssn.Substring(0,3) + "-" + ssn.Substring(3,2) + "-" + ssn.Substring(5);
}
```

**Liquid Equivalent** (requires manual implementation):
```liquid
{% assign part1 = ssn | slice: 0, 3 %}
{% assign part2 = ssn | slice: 3, 2 %}
{% assign part3 = ssn | slice: 5, 4 %}
{% assign formatted_ssn = part1 | append: '-' | append: part2 | append: '-' | append: part3 %}
```

**Workaround**:
- Implement as **Azure Functions** called from Logic Apps
- Rewrite as **Liquid custom filters** (requires Liquid template expertise)

#### 3. **Custom XSLT**
**Status**: * **NOT SUPPORTED**

Inline XSLT code or `<xsl:template>` elements cannot be converted.

**Workaround**:
- Rewrite transformation logic in Liquid
- Use **XSLT action** in Logic Apps (limited to simple transformations)
- Consider **Azure Functions** for complex transformations

#### 4. **Database Lookup Functoids**
**Status**: ** **Requires Logic Apps Actions**

BizTalk database lookup functoids (ValueExtractor, IDExtractor) are external and cannot be embedded in Liquid.

**Workaround**:
- Add **SQL Server connector** actions in Logic Apps workflow
- Perform lookup BEFORE transformation
- Pass lookup results as input to Liquid map

#### 5. **COM+ and .NET Assembly Calls**
**Status**: * **NOT SUPPORTED**

Functoids calling external assemblies cannot be converted.

**Workaround**:
- Reimplement as **Azure Functions**
- Use **Logic Apps connectors** if equivalent exists (e.g., SAP, SharePoint)

### ** **Partial Support - Manual Review Required**

#### 1. **Complex Looping (Table Looping)**
**Status**: ** **May Require Restructuring**

BizTalk Table Looping functoids with complex iteration patterns may not translate cleanly to Liquid `for` loops.

**Review Required**: Validate loop logic in generated Liquid templates.

#### 2. **Conditional Functoids with Complex Expressions**
**Status**: ** **Check Generated Output**

Nested logical functoids (AND, OR, NOT) may generate verbose Liquid conditionals.

**Example**:
```liquid
{% if condition1 == true and condition2 == false or condition3 == true %}
```

**Review Required**: Simplify generated conditions for readability.

#### 3. **Cumulative Functoids (Sum, Average, Count)**
**Status**: ** **Limited to Simple Cases**

Cumulative functoids work only on direct repeating elements. Complex grouping requires manual Liquid code.

### * **Pre-Migration Checklist**

Before converting BTM files, verify:

- [ ] **No flat file schemas referenced** (check schema properties)
- [ ] **No scripting functoids** (or prepare to rewrite in Liquid/Azure Functions)
- [ ] **No database lookup functoids** (plan SQL actions in workflow)
- [ ] **No custom XSLT** (plan rewrite in Liquid)
- [ ] **No external assembly calls** (plan Azure Functions migration)
- [ ] **Simple looping patterns only** (or prepare for manual review)

### ** **How to Detect Unsupported Scenarios**

Use the **`analyze_btm_file`** tool before conversion:

```json
{
  "name": "analyze_btm_file",
  "arguments": {
    "btmFilePath": "C:\\Maps\\MyMap.btm",
    "includeDetails": true
  }
}
```

**Check for**:
- `"Scripting"` functoid type -> Requires manual conversion
- `"Database Lookup"` or `"ValueExtractor"` -> Not supported
- `"Table Looping"` -> May need manual review
- High functoid count (50+) -> Complex map, thorough testing needed

## ** Troubleshooting

### Common Issues

#### Issue: "BTM file not found"
```
Error: BTM file not found: C:\Maps\MyMap.btm
```
**Solution**: Verify file path and ensure .btm extension

#### Issue: "Source schema not found"
```
Error: Source schema file not found: C:\Schemas\Source.xsd
```
**Solution**: Schemas are recommended but optional. Omit if unavailable:
```cmd
BTMtoLMLMigrator.exe MyMap.btm "" "" MyMap.lml
```

#### Issue: "Failed to parse XML"
```
Error: Failed to parse XML in map file
```
**Solution**: Validate BTM file in BizTalk Mapper, re-save to fix corruption

### Validation

#### Check Map Complexity
```csharp
var parser = new BtmParser();
var mapData = parser.Parse(btmPath, sourceSchemaPath, targetSchemaPath);

Console.WriteLine($"Functoids: {mapData.Functoids.Count}");
Console.WriteLine($"Links: {mapData.Links.Count}");
Console.WriteLine($"Complexity: {(mapData.Functoids.Count > 50 * "High" : "Medium")}");
```

#### Identify Problematic Functoids
```csharp
var scriptingFunctoids = mapData.Functoids
    .Where(f => f.FunctoidType == "Scripting")
    .ToList();

if (scriptingFunctoids.Any())
{
    Console.WriteLine($"WARNING: {scriptingFunctoids.Count} scripting functoids require manual review");
}
```

## * Best Practices

### 1. Always Provide Schemas
```cmd
# GOOD - With schemas
BTMtoLMLMigrator.exe Map.btm Source.xsd Target.xsd

# OK - Without schemas (less accurate)
BTMtoLMLMigrator.exe Map.btm "" ""
```

### 2. Organize Output
```cmd
# Create dedicated output folder
mkdir C:\LiquidMaps
BTMtoLMLMigrator.exe Map.btm Source.xsd Target.xsd C:\LiquidMaps\Map.lml
```

### 3. Batch Processing
```powershell
# Convert all BTM files in directory
Get-ChildItem C:\BizTalk\Maps\*.btm | ForEach-Object {
    $outputPath = "C:\LiquidMaps\$($_.BaseName).lml"
    & BTMtoLMLMigrator.exe $_.FullName "" "" $outputPath
}
```

### 4. Validate Before Migration
- Test maps in BizTalk Mapper before converting
- Review scripting functoids for Liquid compatibility
- Check for external dependencies (database lookups, assemblies)

## ** Integration with MCP Server

This library is wrapped by **BizTalktoLogicApps.MCP** for AI assistant integration:

```json
{
  "name": "convert_btm_to_lml",
  "arguments": {
    "btmFilePath": "C:\\Maps\\OrderToInvoice.btm",
    "sourceSchemaPath": "C:\\Schemas\\Order.xsd",
    "targetSchemaPath": "C:\\Schemas\\Invoice.xsd"
  }
}
```

See **BizTalkToLogicApps.MCP** project for MCP server usage.

## ** Version History

- **v1.0** - Initial release
  - BTM parsing with namespace extraction
  - 50+ functoid type translations
  - Liquid template generation

## ** Dependencies

### .NET Framework
- **Target**: .NET Framework 4.7.2
- **System.Xml** - XML parsing
- **System.Xml.Linq** - LINQ to XML

### No External Packages
All functionality uses built-in .NET Framework libraries.

## ** License

MIT License - See LICENSE file in repository root.

## ** Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/haroldcampos/BizTalkMigratorStarter/issues

## ** Author

**Harold Campos**

---

**Version**: 1.0.0  
**Last Updated**: January 28, 2026

**Related Projects**:
- **[ODXtoWFMigrator](../ODXtoWFMigrator/README.md)** - BizTalk orchestration to Logic Apps workflow conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - BizTalk pipeline to Logic Apps conversion
- **[BizTalkToLogicApps.MCP](../BizTalkToLogicApps.MCP/README.md)** - MCP server for AI-assisted migration
- [Azure Logic Apps Data Mapper](https://learn.microsoft.com/azure/logic-apps/logic-apps-enterprise-integration-liquid-transform)
- [BizTalk Server Documentation](https://learn.microsoft.com/biztalk/)

