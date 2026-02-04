# BizTalk to Logic Apps Test Data

This directory contains test data for the BizTalk to Logic Apps migration tool.

## Directory Structure

```
Data/
*** BizTalk/                    # INPUT: BizTalk Server artifacts
*   *** Bindings/              # BizTalk binding XML files
*   *** Maps/                  # BizTalk map (.btm) files
*   *** ODX/                   # BizTalk orchestration (.odx) files
*   *** Pipelines/             # BizTalk pipeline (.btp) files
*   *** Schemas/               # BizTalk schema (.xsd) files
*
*** LogicApps/                  # OUTPUT: Generated Logic Apps artifacts
    *** LMLs/                  # Liquid map (.lml) files (from BTM conversion)
    *** Pipelines/             # Logic Apps pipeline workflows
    *** Workflows/             # Logic Apps orchestration workflows
```

## How to Use

### For Developers Testing the Migration Tool

1. **Add Your BizTalk Artifacts**:
   - Drop your `.odx` orchestration files into `BizTalk/ODX/`
   - Drop your binding `.xml` files into `BizTalk/Bindings/`
   - **IMPORTANT**: Use matching names! For example:
     - `CustomerOrder.odx` * `CustomerOrder.xml`
     - `PaymentProcessing.odx` * `PaymentProcessing.xml`

2. **Run the Integration Tests**:
   - Open Visual Studio Test Explorer
   - Run the test: `GenerateRefactoredWorkflows_ProcessAllOdxFiles_GeneratesAllWorkflows`
   - OR run: `GenerateRefactoredWorkflowToFile_ProcessAllFiles_CreatesAllWorkflows`

3. **Find Your Generated Workflows**:
   - Check `LogicApps/Workflows/` for the output files
   - Each orchestration generates:
     - `{OrchestrationName}_workflow.json` - The Logic Apps workflow definition
     - `{OrchestrationName}_workflow.parameters.json` - The parameters file

### File Naming Convention

**Critical**: The binding file MUST match the orchestration file name:

| ODX File | Bindings File | Generated Workflow |
|----------|---------------|-------------------|
| `HelloOrchestration.odx` | `HelloOrchestration.xml` | `HelloOrchestration_workflow.json` |
| `LoanProcessor.odx` | `LoanProcessor.xml` | `LoanProcessor_workflow.json` |
| `UpdateContact.odx` | `UpdateContact.xml` | `UpdateContact_workflow.json` |

### Example Test Workflow

```
1. Developer adds files:
   Data/BizTalk/ODX/OrderProcessing.odx
   Data/BizTalk/Bindings/OrderProcessing.xml

2. Developer runs test in Visual Studio

3. Tool generates:
   Data/LogicApps/Workflows/OrderProcessing_workflow.json
   Data/LogicApps/Workflows/OrderProcessing_workflow.parameters.json

4. Developer reviews the generated Logic Apps workflow
```

## Test Output

When you run the integration tests, you'll see console output like:

```
Processing 6 ODX files from C:\...\Data\BizTalk\ODX
--------------------------------------------------------------------------------

Processing: HelloOrchestration.odx
  * SUCCESS: Generated HelloOrchestration_workflow.json
  * Generated parameters file

Processing: LoanProcessor.odx
  * SUCCESS: Generated LoanProcessor_workflow.json
  * Generated parameters file

Processing: OrphanOrchestration.odx
  * WARNING: Bindings file not found: OrphanOrchestration.xml
  Skipping OrphanOrchestration.odx

================================================================================
SUMMARY:
  Total ODX files: 6
  Successful: 5
  Failed: 0
================================================================================
```

## Current Test Data

The following BizTalk orchestrations are included for testing:

- * **Aggregate.odx** - Aggregation pattern orchestration
- * **HelloOrchestration.odx** - Simple hello world orchestration
- * **LoanProcessor.odx** - Loan processing workflow
- * **MethodCallService.odx** - Service method call orchestration
- * **ReceivePOandSubmitToWS.odx** - Purchase order to web service
- * **UpdateContact.odx** - Contact update orchestration

Each has a corresponding binding file with the same base name.

## Adding New Test Cases

To add a new test case:

1. Export your BizTalk orchestration to an `.odx` file
2. Export your BizTalk bindings to an `.xml` file
3. Ensure both files have the same base name (e.g., `MyOrch.odx` and `MyOrch.xml`)
4. Copy both files to the appropriate directories:
   - `MyOrch.odx` * `Data/BizTalk/ODX/`
   - `MyOrch.xml` * `Data/BizTalk/Bindings/`
5. Run the tests - your orchestration will be automatically processed!

## Troubleshooting

### "No ODX files found in test data directory"
- Make sure you have `.odx` files in `Data/BizTalk/ODX/`
- Check that the files have the `.odx` extension

### "Bindings file not found"
- Verify the binding file exists in `Data/BizTalk/Bindings/`
- Check that the binding file has the **exact same base name** as the ODX file
- Example: If you have `CustomerOrder.odx`, you need `CustomerOrder.xml` (not `CustomerOrderBindings.xml`)

### "At least one ODX file should be processed successfully"
- This error means all orchestrations failed to process
- Check the test output console for specific error messages
- Verify your ODX and binding files are valid BizTalk artifacts

## Notes

- The output directory (`LogicApps/Workflows`) is **NOT** automatically cleaned between test runs
- Generated files are **preserved** for review and comparison
- If you want fresh output, manually delete files from `LogicApps/Workflows` before running tests
- The tests are **data-driven** - add as many orchestrations as you want without modifying the test code!

## Related Tests

- **RefactoredWorkflowGeneratorTests.cs** - Main integration tests for ODX to workflow conversion
- **CompleteUserScenarioTests.cs** - End-to-end user scenario tests
- **LogicAppJSONGeneratorTests.cs** - JSON generation tests

## License

Copyright (c) Microsoft Corporation.
Licensed under the MIT License.
