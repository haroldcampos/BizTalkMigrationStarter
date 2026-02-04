# Quick Start Guide: Testing the ODX + Binding ? Report Pipeline

## Your Specific Scenario

You want to ensure that when you **bring ODX files (one or many) and binding files (one or many)**, the tool correctly generates migration reports.

## Test Files Created for This Scenario

### 1. **CompleteUserScenarioTests.cs** ? (Main Test File)
Located: `BizTalkToLogicApps.Tests/Integration/CompleteUserScenarioTests.cs`

This file contains **6 comprehensive scenarios** that match your exact workflow:

#### ? Scenario 1: Single ODX + Single Binding ? Single Report
- **Test**: `Scenario_SingleOdxSingleBinding_GeneratesCompleteReport`
- **What it does**: Feeds 1 ODX file and 1 binding file, generates HTML and Markdown reports
- **Validates**: Binding parsing, report generation, content accuracy

#### ? Scenario 2: Multiple ODX + Single Binding ? Batch Report
- **Test**: `Scenario_MultipleOdxSingleBinding_GeneratesBatchReport`
- **What it does**: Feeds 4 ODX files (simulating SAT payment system) with 1 shared binding file
- **Validates**: Batch processing, individual reports, batch summary generation

#### ? Scenario 3: Multiple ODX + Multiple Bindings ? Comprehensive Report
- **Test**: `Scenario_MultipleOdxMultipleBindings_GeneratesComprehensiveReports`
- **What it does**: Processes 2 ODX files with 3 binding files (Dev, Test, Prod environments)
- **Validates**: Environment-specific reports, binding differences

#### ? Scenario 4: Directory-Based Processing (Real User Workflow)
- **Test**: `Scenario_ProcessEntireDirectory_GeneratesAllReports`
- **What it does**: Processes all ODX files in a directory with all binding files
- **Validates**: Batch directory processing, multiple binding contexts

#### ? Scenario 5: Gap Analysis + Reporting
- **Test**: `Scenario_GapAnalysisPlusReporting_ProvidesCompleteInsights`
- **What it does**: Runs gap analysis on ODX files, then generates migration reports
- **Validates**: Complete analysis pipeline (gap detection + migration reporting)

#### ? Scenario 6: Error Handling in Batch Processing
- **Test**: `Scenario_BatchProcessingWithErrors_ContinuesAndReports`
- **What it does**: Processes mix of valid and invalid files
- **Validates**: Graceful error handling, continues processing valid files

### 2. **EndToEndReportGenerationTests.cs**
Located: `BizTalkToLogicApps.Tests/Integration/EndToEndReportGenerationTests.cs`

Contains **25+ granular tests** covering:
- Single/multiple ODX processing
- Binding data extraction (FILE, HTTP, WCF adapters)
- Report content validation
- Error handling

### 3. **OdxAnalyzerTests.cs**
Located: `BizTalkToLogicApps.Tests/Integration/OdxAnalyzerTests.cs`

Contains **15+ tests** for gap analysis:
- Pattern detection (correlation, transactions, convoy)
- Shape type analysis
- Complexity categorization
- Feature recommendations

## How to Run These Tests

### Option 1: Run All Tests (Recommended)

```powershell
# From solution directory
msbuild BizTalkToLogicApps.sln /t:Rebuild /p:Configuration=Debug
vstest.console.exe BizTalkToLogicApps.Tests\bin\Debug\BizTalkToLogicApps.Tests.dll
```

### Option 2: Run Only Your Specific Scenarios

```powershell
# Run just the CompleteUserScenarioTests class
vstest.console.exe BizTalkToLogicApps.Tests\bin\Debug\BizTalkToLogicApps.Tests.dll /Tests:CompleteUserScenarioTests
```

### Option 3: Run Single Scenario

```powershell
# Run just Scenario 4 (directory-based processing)
vstest.console.exe BizTalkToLogicApps.Tests\bin\Debug\BizTalkToLogicApps.Tests.dll /Tests:Scenario_ProcessEntireDirectory_GeneratesAllReports
```

### Option 4: Visual Studio Test Explorer

1. Open Visual Studio
2. Go to **Test ? Test Explorer**
3. Click "Run All" or right-click `CompleteUserScenarioTests` and select "Run"

## What Each Test Validates

### Input Processing
- ? ODX file parsing (single and multiple)
- ? Binding file parsing (single and multiple)
- ? Directory enumeration (all .odx and .xml files)
- ? Error handling for invalid files

### Report Generation
- ? Individual orchestration reports (HTML and Markdown)
- ? Batch summary reports
- ? Gap analysis JSON reports
- ? Environment-specific reports

### Report Content
- ? Orchestration statistics (shape counts, ports, messages)
- ? Complexity scores
- ? Migration readiness percentages
- ? Pattern detection (convoy, correlation, transactions)
- ? Recommendations
- ? Integration pattern suggestions

### Binding Integration
- ? Receive location details extracted
- ? Send port details extracted
- ? Filter conditions parsed
- ? WCF metadata preserved
- ? Adapter configurations captured

## Test Data

All tests create their own test data dynamically:

```
%TEMP%\BizTalkMigrator_Tests\
??? UserScenarios\
?   ??? Orchestrations\        # *.odx files created here
?   ??? Bindings\              # *.xml binding files created here
?   ??? Reports\               # Generated reports output here
```

Test data is **automatically cleaned up** after each test run.

## Expected Output

When tests run successfully, you'll see:

```
Passed: Scenario_SingleOdxSingleBinding_GeneratesCompleteReport
Passed: Scenario_MultipleOdxSingleBinding_GeneratesBatchReport
Passed: Scenario_MultipleOdxMultipleBindings_GeneratesComprehensiveReports
Passed: Scenario_ProcessEntireDirectory_GeneratesAllReports
Passed: Scenario_GapAnalysisPlusReporting_ProvidesCompleteInsights
Passed: Scenario_BatchProcessingWithErrors_ContinuesAndReports

Total tests: 6. Passed: 6. Failed: 0. Skipped: 0.
```

## Debugging Failed Tests

If a test fails:

1. **Check test output** for detailed error messages
2. **Examine temporary files** in `%TEMP%\BizTalkMigrator_Tests\UserScenarios\Reports\`
3. **Review generated reports** to see what was actually produced
4. **Use Visual Studio debugger** to step through test execution

## Real-World Usage After Tests Pass

Once tests pass, you can confidently use the tool:

```powershell
# Single orchestration with binding
BizTalkToLogicApps.exe convert "MyOrch.odx" --bindings "MyBindings.xml" --output "Reports\"

# Batch processing (if supported in Program.cs)
BizTalkToLogicApps.exe batch-convert "C:\Orchestrations\" --bindings "C:\Bindings\Prod.xml" --output "Reports\"

# Gap analysis
BizTalkToLogicApps.exe analyze "C:\Orchestrations\" --output "GapAnalysis.json"
```

## Summary

? **6 user scenario tests** in `CompleteUserScenarioTests.cs`  
? **25+ granular tests** in `EndToEndReportGenerationTests.cs`  
? **15+ gap analysis tests** in `OdxAnalyzerTests.cs`  
? **45+ total tests** validating your complete workflow  

**Your scenario is fully covered**: Feed ODX files (one or many) and binding files (one or many) ? Get comprehensive migration reports.
