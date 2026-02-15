# BTMtoLMLMigrator

## Overview

**BizTalk Map (BTM) to Logic Apps Mapping Language (LML) Converter** - Migrates BizTalk Server XSLT-based maps to Azure Logic Apps Data Mapper's Language format. You will need to have available the source and destination schemas, along with the BizTalk map.

The tool produces **YAML-based LML files** compatible with [Azure Logic Apps Data Mapper](https://learn.microsoft.com/azure/logic-apps/logic-apps-enterprise-integration-maps). The output uses XPath-style source expressions, `$for()` loops, `$if()` conditionals, and `$@` attribute syntax defined by the Data Mapper specification.

## Purpose

Converts BizTalk transformation maps to Azure Data Mapper LML files, enabling:
- **Automated map migration** from BizTalk to Azure
- **Functoid translation** to Data Mapper functions (`concat`, `add`, `is-equal`, etc.)
- **XPath preservation** with namespace handling
- **Schema-aware conversion** for accurate data mapping

## Features

### Core Capabilities

- **BTM Parsing** - Extracts functoids, links, and schemas from .btm files
- **Functoid Translation** - Converts 50+ functoid types to Data Mapper function equivalents
- **XPath Generation** - Creates LML source expressions from BizTalk XPath
- **Namespace Handling** - Preserves XML namespaces with `$sourceNamespaces` / `$targetNamespaces` declarations
- **Schema Integration** - Uses XSD schemas for accurate type mapping and loop detection
- **LML Generation** - Produces YAML-formatted `.lml` files for Azure Data Mapper

### Supported Functoid Types

| Category | Functoids | LML Function Examples |
|----------|-----------|----------------------|
| **String** | String Concatenate, Uppercase, Lowercase, Substring, String Find, Size | `concat()`, `upper-case()`, `lower-case()`, `substring()`, `contains()`, `string-length()` |
| **Mathematical** | Add, Subtract, Multiply, Divide, Modulo, Absolute Value | `add()`, `subtract()`, `multiply()`, `divide()`, `modulo()`, `abs()` |
| **Logical** | Logical AND, OR, NOT, Equal, Not Equal, Greater Than, Less Than | `and()`, `or()`, `not()`, `is-equal()`, `greater-than()`, `less-than()` |
| **Conversion** | Value Mapping, Value Mapping (Flattening), Index, Iteration, Looping | `if-then-else()`, `position()`, `$for()` |
| **Advanced** | Scripting (C#/VB.NET), Table Looping, Custom XSLT | `custom-function()` with original code in comment |
| **Date/Time** | Current Date, Current Time, Date Formatting | `current-date()`, `current-time()`, `current-dateTime()`, `format-dateTime()` |
| **Cumulative** | Sum, Average, Min, Max, Count | `sum()`, `avg()`, `min()`, `max()`, `count()` |

## Quick Start

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

## LML Output Format

The converter generates **YAML-based LML files** for Azure Data Mapper. Every generated `.lml` file follows this structure:

```yaml
$version: 1
$input: XML
$output: XML
$sourceSchema: Order.xsd
$targetSchema: Invoice.xsd
$sourceNamespaces:
  ns0: http://schemas.example.com/Order
$targetNamespaces:
  ns0: http://schemas.example.com/Invoice
ns0:Invoice:
  CustomerName: concat(/ns0:Order/FirstName, ' ', /ns0:Order/LastName)
  OrderDate: current-dateTime()
  Items:
    $for(/ns0:Order/Items/Item):
      LineItem:
        ProductId: ProductId
        Quantity: Qty
```

### Key LML Syntax Elements

| Syntax | Purpose | Example |
|--------|---------|---------|
| `$version` | LML format version | `$version: 1` |
| `$input` / `$output` | Data formats | `$input: XML` |
| `$sourceSchema` / `$targetSchema` | Schema file references | `$sourceSchema: Order.xsd` |
| `$sourceNamespaces` / `$targetNamespaces` | Namespace declarations | `ns0: http://...` |
| `FieldName: expression` | Simple field mapping | `Total: add(/ns0:Order/Subtotal, /ns0:Order/Tax)` |
| `$for(xpath):` | Loop over repeating elements | `$for(/ns0:Order/Items/Item):` |
| `$if(condition):` | Conditional mapping | `$if(is-equal(/ns0:Order/Status, "Active")):` |
| `$@AttributeName` | XML attribute mapping | `$@Currency: /ns0:Order/@Currency` |
| `$value` | Element text when attributes/children coexist | `$value: /ns0:Order/Amount` |

## Architecture

### Three-Phase Pipeline

```
+---------------+       +-------------------+       +--------------+
|   BtmParser   |  -->  | FunctoidTranslator|  -->  | LmlGenerator |
+---------------+       +-------------------+       +--------------+
    Phase 1                   Phase 2                   Phase 3
  Parse BTM             Translate Logic            Generate LML
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
**Responsibility**: Convert BizTalk functoids to Data Mapper expressions

**Translates**:
- String operations -> Data Mapper functions (`concat()`, `upper-case()`, `lower-case()`)
- Math operations -> Data Mapper arithmetic (`add()`, `subtract()`, `multiply()`)
- Logical operations -> Data Mapper conditionals (`if-then-else()`, `is-equal()`)
- Looping functoids -> `$for()` loop structures
- Scripting functoids -> `custom-function()` with original code in comment (manual review required)

**Output**: `TranslatedMapData` with LML-ready expressions

#### 3. LmlGenerator
**Responsibility**: Generate YAML-formatted LML from translated map data

**Generates**:
- LML header (`$version`, `$input`, `$output`, `$sourceSchema`, `$targetSchema`)
- Namespace declarations (`$sourceNamespaces`, `$targetNamespaces`)
- Hierarchical field mappings with XPath source expressions
- `$for()` loops for repeating elements
- `$if()` conditionals for conditional mappings
- `$@` attribute mappings

**Output**: `.lml` Azure Data Mapper file

## Project Structure

```
BTMtoLMLMigrator/
??? BtmMigrator.cs           # Main orchestration class
??? BtmParser.cs             # BTM XML parsing
??? FunctoidTranslator.cs    # Functoid -> Data Mapper expression translation
??? LmlGenerator.cs          # YAML LML template generation
??? Models.cs                # Data models (BtmMapData, TranslatedMapData, LmlMapping)
??? Program.cs               # CLI entry point
??? Properties/
    ??? AssemblyInfo.cs      # Assembly metadata
```

## Programmatic Usage

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

// Inspect translated mappings
foreach (var mapping in translatedMap.Mappings)
{
    Console.WriteLine($"{mapping.TargetPath}: {mapping.SourceExpression}");
}
```

## Data Models

### BtmMapData
```csharp
public class BtmMapData
{
    public string SourceSchema { get; set; }
    public string TargetSchema { get; set; }
    public Dictionary<string, string> SourceNamespaces { get; set; }
    public Dictionary<string, string> TargetNamespaces { get; set; }
    public List<BtmFunctoid> Functoids { get; set; }
    public List<BtmLink> Links { get; set; }
    public BtmSchemaTree SourceTree { get; set; }
    public BtmSchemaTree TargetTree { get; set; }
}
```

### TranslatedMapData
```csharp
public class TranslatedMapData
{
    public string Version { get; set; }           // "1"
    public string InputFormat { get; set; }       // "XML"
    public string OutputFormat { get; set; }      // "XML"
    public string SourceSchema { get; set; }
    public string TargetSchema { get; set; }
    public Dictionary<string, string> SourceNamespaces { get; set; }
    public Dictionary<string, string> TargetNamespaces { get; set; }
    public List<LmlMapping> Mappings { get; set; }
}
```

### LmlMapping
```csharp
public class LmlMapping
{
    public string TargetPath { get; set; }            // Target element XPath
    public string SourceExpression { get; set; }      // Data Mapper expression
    public List<LmlMapping> Children { get; set; }    // Child element mappings
    public string LoopExpression { get; set; }        // XPath for $for() loops
    public string ConditionalExpression { get; set; } // Expression for $if()
    public bool IsLoop { get; set; }
    public bool IsConditional { get; set; }
    public bool IsAttribute { get; set; }
    public Dictionary<string, string> Attributes { get; set; } // $@ mappings
}
```

## Functoid Translation Examples

### String Concatenate
**BizTalk**: StringConcatenate functoid with two inputs (FirstName, LastName)

**LML Output**:
```yaml
FullName: concat(/ns0:Order/FirstName, ' ', /ns0:Order/LastName)
```

### Value Mapping with Condition
**BizTalk**: ValueMapping functoid with condition and value inputs

**LML Output**:
```yaml
MappedCustomerID: if-then-else(is-equal(/ns0:Order/Status, "Active"), /ns0:Order/CustomerID, null)
```

### Mathematical Add
**BizTalk**: MathAdd functoid with two inputs (Subtotal, Tax)

**LML Output**:
```yaml
Total: add(/ns0:Order/Subtotal, /ns0:Order/Tax)
```

### Logical Comparison with Conditional Mapping
**BizTalk**: LogicalGt functoid connected directly to a target field

**LML Output**:
```yaml
HighValueOrder: if-then-else(greater-than(/ns0:Order/Amount, 1000), /ns0:Order/Amount, null)
```

### Looping (Repeating Elements)
**BizTalk**: Looping functoid connecting source and target repeating records

**LML Output**:
```yaml
LineItems:
  $for(/ns0:Order/Items/Item):
    LineItem:
      ProductId: ProductId
      Quantity: Qty
      Price: UnitPrice
```

### Attribute Mapping
**BizTalk**: Direct link to a target attribute

**LML Output**:
```yaml
Order:
  $@Currency: /ns0:Order/@CurrencyCode
  $@OrderDate: current-dateTime()
  Amount: /ns0:Order/TotalAmount
```

## Advanced Features

### Schema-Aware XPath Generation

When schemas are provided, the converter:
1. Extracts XML namespace prefixes from XSD `targetNamespace`
2. Generates namespace-qualified XPath source expressions
3. Detects repeating elements (`maxOccurs="unbounded"`) to create `$for()` loops
4. Only applies namespace prefix to the root element (nested elements inherit)

**Example**:
- **BizTalk XPath**: `/*[local-name()='Order']/*[local-name()='Items']/*[local-name()='Item']`
- **LML Source Expression**: `/ns0:Order/Items/Item`

### Scripting Functoid Handling

Scripting functoids with inline C# code are translated to a `custom-function()` placeholder with the original code preserved in a comment:

```yaml
FormattedSSN: custom-function(/ns0:Employee/SSN) /* Original code: public string FormatSSN(string ssn) { return ssn.Substring(0,3) + "-" + ssn.Substring(3,2) + "-" + ssn.Substring(5); } */
```

Common patterns are auto-detected and translated:
- `Regex.IsMatch()` ? `matches()`
- `String.Replace()` ? `replace()`
- `StartsWith()` ? `starts-with()`
- `EndsWith()` ? `ends-with()`

### Namespace Preservation

Namespaces are declared in dedicated LML header sections:

```yaml
$sourceNamespaces:
  ns0: http://Company.Schemas.Order
$targetNamespaces:
  ns0: http://Company.Schemas.Invoice
```

The target schema's primary business namespace is enforced as `ns0` for Azure Data Mapper compatibility.

## Known Limitations & Unsupported Scenarios

### NOT SUPPORTED - Cannot Convert

#### 1. **Flat File Maps**
**Status**: **NOT SUPPORTED**

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
**Status**: **Requires Manual Conversion**

C#/VB.NET code in scripting functoids cannot be fully converted to LML. The converter auto-detects common patterns (regex, string operations) and translates them, but complex logic requires manual implementation.

**Example**:
```csharp
// BizTalk Scripting Functoid
public string FormatSSN(string ssn) {
    return ssn.Substring(0,3) + "-" + ssn.Substring(3,2) + "-" + ssn.Substring(5);
}
```

**Generated LML** (requires manual review):
```yaml
FormattedSSN: custom-function(/ns0:Employee/SSN) /* Original code: ... */
```

**Workaround**:
- Implement as **Azure Functions** called from Logic Apps
- Use Data Mapper's built-in `concat()` and `substring()` functions where possible

#### 3. **Custom XSLT**
**Status**: **NOT SUPPORTED**

Inline XSLT code or `<xsl:template>` elements cannot be converted.

**Workaround**:
- Rewrite transformation logic using Data Mapper functions
- Use **XSLT action** in Logic Apps (limited to simple transformations)
- Consider **Azure Functions** for complex transformations

#### 4. **Database Lookup Functoids**
**Status**: **Requires Logic Apps Actions**

BizTalk database lookup functoids (ValueExtractor, IDExtractor) are external and cannot be embedded in LML.

**Workaround**:
- Add **SQL Server connector** actions in Logic Apps workflow
- Perform lookup BEFORE transformation
- Pass lookup results as input to LML map

#### 5. **COM+ and .NET Assembly Calls**
**Status**: **NOT SUPPORTED**

Functoids calling external assemblies cannot be converted.

**Workaround**:
- Reimplement as **Azure Functions**
- Use **Logic Apps connectors** if equivalent exists (e.g., SAP, SharePoint)

### Partial Support - Manual Review Required

#### 1. **Complex Looping (Table Looping)**
**Status**: **May Require Restructuring**

BizTalk Table Looping functoids with complex iteration patterns may not translate cleanly to `$for()` loops.

**Review Required**: Validate loop logic in generated LML templates.

#### 2. **Conditional Functoids with Complex Expressions**
**Status**: **Check Generated Output**

Nested logical functoids (AND, OR, NOT) may generate deeply nested LML expressions.

**Example**:
```yaml
Result: if-then-else(and(is-equal(Field1, "A"), not(is-equal(Field2, "B"))), ValueA, ValueB)
```

**Review Required**: Verify generated expressions produce correct results in Azure Data Mapper.

#### 3. **Cumulative Functoids (Sum, Average, Count)**
**Status**: **Limited to Simple Cases**

Cumulative functoids work only on direct repeating elements. Complex grouping requires manual LML code.

### Pre-Migration Checklist

Before converting BTM files, verify:

- [ ] **No flat file schemas referenced** (check schema properties)
- [ ] **No scripting functoids** (or prepare to rewrite using Data Mapper functions / Azure Functions)
- [ ] **No database lookup functoids** (plan SQL actions in workflow)
- [ ] **No custom XSLT** (plan rewrite using Data Mapper functions)
- [ ] **No external assembly calls** (plan Azure Functions migration)
- [ ] **Simple looping patterns only** (or prepare for manual review)

### How to Detect Unsupported Scenarios

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

## Troubleshooting

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
Console.WriteLine($"Complexity: {(mapData.Functoids.Count > 50 ? "High" : "Medium")}");
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

## Best Practices

### 1. Always Provide Schemas
```cmd
# GOOD - With schemas (enables loop detection, namespace resolution)
BTMtoLMLMigrator.exe Map.btm Source.xsd Target.xsd

# OK - Without schemas (less accurate, no loop detection)
BTMtoLMLMigrator.exe Map.btm "" ""
```

### 2. Organize Output
```cmd
# Create dedicated output folder
mkdir C:\lmlMaps
BTMtoLMLMigrator.exe Map.btm Source.xsd Target.xsd C:\lmlMaps\Map.lml
```

### 3. Batch Processing
```powershell
# Convert all BTM files in directory
Get-ChildItem C:\BizTalk\Maps\*.btm | ForEach-Object {
    $outputPath = "C:\lmlMaps\$($_.BaseName).lml"
    & BTMtoLMLMigrator.exe $_.FullName "" "" $outputPath
}
```

### 4. Validate Before Migration
- Test maps in BizTalk Mapper before converting
- Review scripting functoids for Data Mapper function equivalents
- Check for external dependencies (database lookups, assemblies)
- Load the generated `.lml` file in Azure Data Mapper to verify

## Integration with MCP Server

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

## Version History

- **v1.0** - Initial release
  - BTM parsing with namespace extraction
  - 50+ functoid type translations to Data Mapper functions
  - YAML-based LML generation for Azure Data Mapper

## Dependencies

### .NET Framework
- **Target**: .NET Framework 4.7.2
- **System.Xml** - XML parsing
- **System.Xml.Linq** - LINQ to XML

### No External Packages
All functionality uses built-in .NET Framework libraries.

## Changelog

### v1.0.0 (January 2026)

- Initial release

## License

MIT License - See LICENSE file in repository root.

## Support

For issues, questions, or feature requests:

- **GitHub Issues**: https://github.com/haroldcampos/BizTalkMigrationStarter/issues

## Author

**Harold Campos**

---

**Version**: 1.0.0  
**Last Updated**: January 28, 2026

**Related Projects**:
- **[ODXtoWFMigrator](../ODXtoWFMigrator/README.md)** - BizTalk orchestration to Logic Apps workflow conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - BizTalk pipeline to Logic Apps conversion
- **[BizTalkToLogicApps.MCP](../BizTalkToLogicApps.MCP/README.md)** - MCP server for AI-assisted migration
- [BizTalk Server Documentation](https://learn.microsoft.com/biztalk/)

