# BizTalktoLogicApps.Tests

## Overview

**Comprehensive Test Suite** for the BizTalk to Logic Apps Migration Tool. Provides unit and integration tests for all migration components including orchestration conversion, map translation, pipeline migration, and refactoring capabilities.

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
| **Binding Snapshot** | `BindingSnapshotTests` | Bindings XML parsing |
| **Logic Apps Mapper** | `LogicAppsMapperTests` | Shape-to-action mapping |
| **Expression Mapper** | `ExpressionMapperTests` | XLANG to WDL translation |
| **Workflow Validator** | `WorkflowValidatorTests` | JSON schema validation |
| **BTM Parser** | `BtmParserTests` | BizTalk map parsing |
| **LML Generator** | `LmlGeneratorTests` | Liquid template generation |
| **Pipeline Parser** | `PipelineParserTests` | BizTalk pipeline parsing |
| **Refactoring Options** | `RefactoringOptionsTests` | Configuration validation |

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

#### Run All Tests
``cmd
dotnet test BizTalktoLogicApps.Tests.csproj
``

### Visual Studio Test Explorer

1. Open **Test Explorer** (Test → Test Explorer)
2. Click **Run All** to execute all tests
3. Use filters to run specific test categories

## Project Structure

``
BizTalktoLogicApps.Tests/
├── Unit/                          # Unit tests
├── Integration/                   # Integration tests
├── Data/                          # Test data files
│   └── BizTalk/
│       ├── ODX/                  # Sample orchestrations
│       ├── Bindings/             # Sample bindings
│       ├── BTM/                  # Sample maps
│       └── BTP/                  # Sample pipelines
└── Properties/
    └── AssemblyInfo.cs
``

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
- **[BTMtoLMLMigrator](../BTMtoLMLMigrator/README.md)** - Map to Liquid template conversion
- **[BTPtoLA](../BTPtoLA/README.md)** - Pipeline to Logic Apps conversion
- **[BizTalkToLogicApps.MCP](../BizTalkToLogicApps.MCP/README.md)** - MCP server wrapper

## License

MIT License - See LICENSE file in repository root.

## Author

**Harold Campos**

---

**Version**: 1.0.0  
**Last Updated**: January 2026
