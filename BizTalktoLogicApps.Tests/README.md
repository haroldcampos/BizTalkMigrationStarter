# BizTalktoLogicApps.Tests

## Overview

**Comprehensive Test Suite** for the BizTalk Migration starter. Provides unit and integration tests for all migration components including orchestration conversion, map translation, pipeline migration, and refactoring capabilities.

## Purpose

Ensures the reliability and correctness of BizTalk to Azure Logic Apps migration by:
- **Unit Testing** - Validating individual components in isolation
- **Integration Testing** - Verifying end-to-end migration workflows
- **Regression Prevention** - Catching breaking changes early
- **Documentation** - Serving as usage examples for migration APIs

## Test Coverage

### Unit Tests

| Component | Test Class | Coverage |
|-----------|-----------|----------|
| **Orchestration Parser** | `BizTalkOrchestrationParserTests` | ODX file parsing, shape extraction |
| **Binding Snapshot** | `BindingSnapshotTests` | Bindings XML parsing, send port filters, WCF metadata |
| **Logic Apps Mapper** | `LogicAppsMapperTests` | Shape-to-action mapping, self-recursion, message flow, De Morgan's law, duplicate prevention |
| **Logic Apps Mapper (Thread Safety)** | `LogicAppsMapperThreadSafetyTests` | Concurrent `MapToLogicApp` calls, `[ThreadStatic]` isolation |
| **Expression Mapper** | `ExpressionMapperTests` | XLANG to WDL translation, N-ary logical operators, exception variables, type casts |
| **Workflow Validator** | `WorkflowValidatorTests` | JSON schema validation |
| **Receive Pattern Analysis** | `ReceivePatternAnalysisTests` | Activation patterns, correlation detection |
| **Exception Extensions** | `ExceptionExtensionsTests` | Fatal exception detection (`OutOfMemoryException`, etc.) |
| **BTM Parser** | `BtmParserTests` | BizTalk map parsing, functoid/link extraction |
| **LML Generator** | `LmlGeneratorTests` | Logic Apps Mapping Language (LML) YAML generation, namespace handling |
| **Pipeline Parser** | `PipelineParserTests` | BizTalk pipeline parsing, stage/component extraction |
| **Pipeline Workflow Mapper** | `PipelineWorkflowMapperTests` | Component to action mapping, default pipeline detection |
| **Pipeline JSON Generator** | `PipelineJSONGeneratorTests` | Pipeline workflow JSON structure |
| **Refactoring Options** | `RefactoringOptionsTests` | Configuration validation, deployment target constraints |

### Integration Tests

| Test Class | Scenarios Covered |
|-----------|------------------|
| **CompleteUserScenarioTests** | End-to-end user workflows |
| **ODXtoWFMigratorTests** | Orchestration conversion scenarios |
| **LogicAppJSONGeneratorTests** | Workflow JSON generation |
| **EndToEndReportGenerationTests** | Diagnostic report generation, batch processing |
| **OdxAnalyzerTests** | Gap analysis and pattern detection |
| **BTMtoLMLMigratorTests** | Map conversion scenarios |
| **BTPtoLAMigratorTests** | Pipeline migration scenarios |
| **RefactoredWorkflowGeneratorTests** | Pattern-based optimization |

## Quick Start

### Prerequisites

- Visual Studio 2019 or higher
- .NET Framework 4.7.2
- MSTest Test Framework

### Running Tests

> **Note:** `dotnet test` may hang on .NET Framework 4.7.2 projects. Use Visual Studio Test Explorer or `vstest.console.exe` instead.

### Visual Studio Test Explorer (Recommended)

1. Open **Test Explorer** (Test → Test Explorer)
2. Click **Run All** to execute all tests
3. Use filters to run specific test categories

## Project Structure

```
BizTalktoLogicApps.Tests/
├── Unit/                                    # Fast, isolated unit tests
│   ├── BizTalkOrchestrationParserTests.cs
│   ├── BindingSnapshotTests.cs
│   ├── LogicAppsMapperTests.cs
│   ├── LogicAppsMapperThreadSafetyTests.cs
│   ├── ExpressionMapperTests.cs
│   ├── WorkflowValidatorTests.cs
│   ├── ReceivePatternAnalysisTests.cs
│   ├── ExceptionExtensionsTests.cs
│   ├── Refactoring/
│   │   └── RefactoringOptionsTests.cs
│   ├── BTPtoLA/
│   │   ├── PipelineParserTests.cs
│   │   ├── PipelineWorkflowMapperTests.cs
│   │   └── PipelineJSONGeneratorTests.cs
│   └── BTMtoLMLMigrator/
│       ├── BtmParserTests.cs
│       └── LmlGeneratorTests.cs
├── Integration/                             # Tests requiring file I/O
│   ├── CompleteUserScenarioTests.cs
│   ├── EndToEndReportGenerationTests.cs
│   ├── LogicAppJSONGeneratorTests.cs
│   ├── OdxAnalyzerTests.cs
│   ├── ODXtoWFMigrator/
│   │   └── ODXtoWFMigratorTests.cs
│   ├── BTPtoLA/
│   │   └── BTPtoLAMigratorTests.cs
│   ├── BTMtoLML/
│   │   └── BTMtoLMLMigratorTests.cs
│   └── Refactoring/
│       └── RefactoredWorkflowGeneratorTests.cs
├── Data/                                    # Test data (see Data/README.md)
│   ├── BizTalk/
│   │   ├── Bindings/
│   │   ├── Maps/
│   │   ├── ODX/
│   │   ├── Pipelines/
│   │   └── Schemas/
│   └── LogicApps/
│       ├── LMLs/
│       ├── Pipelines/
│       └── Workflows/
└── Properties/
    └── AssemblyInfo.cs
```

## Test Data

Test data is located in the `Data/` directory with sample BizTalk artifacts.

## Best Practices

### 1. Use Descriptive Test Names
``csharp
[TestMethod]
public void ParseOdx_ValidFile_ReturnsOrchestrationModel()
``

### 2. Follow Arrange-Act-Assert Pattern
``csharp
// Arrange
var component = new MyComponent();

// Act
var result = component.DoSomething();

// Assert
Assert.IsNotNull(result);
``

### 3. Add Owner Attributes
``csharp
[TestClass]
[Owner("github-username")]
public class MyTests { }
``

### 4. Handle Fatal Exceptions
``csharp
catch (Exception ex) when (!ex.IsFatal())
{
    // Handle recoverable errors
}
``

## Test Metrics

- **Total Test Classes**: 20+
- **Total Test Methods**: 150+
- **Code Coverage Target**: 80%+

## Related Projects

- **[ODXtoWFMigrator](../ODXtoWFMigrator/README.md)** - Orchestration to workflow migration
- **[BTMtoLMLMigrator](../BTMtoLMLMigrator/README.md)** - Map to LML (Logic Apps Mapping Language) conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - Pipeline to Logic Apps conversion
- **[BizTalkToLogicApps.MCP](https://github.com/haroldcampos/BizTalkMigrationStarter/blob/main/BizTalktoLogicApps.MCP/README.md)** - MCP server wrapper

## Changelog

### v1.1.0 (January 2026)

#### Tests Added

- `InvertCondition_ParenthesizedCompound_RespectsGrouping` — validates parenthesized compound conditions split correctly at top-level operators
- `InvertCondition_TernaryAnd_InvertsAllThreeParts` — validates 3-way `&&` inversion produces `||` with all operators inverted
- `MapExpression_TernaryAnd_ProducesNestedAndCalls` — validates `a == 1 && b == 2 && c == 3` produces nested `and()` calls
- `MapExpression_TernaryOr_ProducesNestedOrCalls` — validates `x == 1 || y == 2 || z == 3` produces nested `or()` calls
- `MapExpression_QuaternaryAnd_ProducesNestedAndCalls` — validates 4-way `&&` produces nested `and()` calls

### v1.0.0 (January 2026)

- Initial release

## License

MIT License - See LICENSE file in repository root.

## Author

**Harold Campos**

---

**Version**: 1.1.0  
**Last Updated**: January 2026
