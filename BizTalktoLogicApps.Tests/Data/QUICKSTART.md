# Quick Start: Testing BizTalk to Logic Apps Migration

## ?? Quick Start (30 seconds)

1. **Drop your files**:
   ```
   Data/BizTalk/ODX/         ? Your .odx orchestration files here
   Data/BizTalk/Bindings/    ? Your .xml binding files here (MUST match ODX names!)
   ```

2. **Run the test** in Visual Studio Test Explorer:
   - Test: `GenerateRefactoredWorkflows_ProcessAllOdxFiles_GeneratesAllWorkflows`

3. **Get your results**:
   ```
   Data/LogicApps/Workflows/ ? Generated Logic Apps workflows appear here!
   ```

## ? File Naming Rule

**The binding XML file MUST have the same name as the ODX file:**

| ? Correct | ? Wrong |
|-----------|---------|
| `Order.odx` + `Order.xml` | `Order.odx` + `OrderBindings.xml` |
| `Payment.odx` + `Payment.xml` | `Payment.odx` + `Bindings.xml` |

## ?? Example

```bash
# Step 1: Add your files
Data/BizTalk/ODX/CustomerOrder.odx
Data/BizTalk/Bindings/CustomerOrder.xml

# Step 2: Run test (in Visual Studio Test Explorer)

# Step 3: Find output
Data/LogicApps/Workflows/CustomerOrder_workflow.json
Data/LogicApps/Workflows/CustomerOrder_workflow.parameters.json
```

## ?? Available Tests

| Test Name | What It Does |
|-----------|-------------|
| `GenerateRefactoredWorkflows_ProcessAllOdxFiles_GeneratesAllWorkflows` | Processes ALL ODX files and shows detailed console output |
| `GenerateRefactoredWorkflowToFile_ProcessAllFiles_CreatesAllWorkflows` | Same but focuses on file output verification |

## ?? Console Output Example

```
Processing 3 ODX files from C:\...\Data\BizTalk\ODX
--------------------------------------------------------------------------------

Processing: CustomerOrder.odx
  ? SUCCESS: Generated CustomerOrder_workflow.json
  ? Generated parameters file

Processing: PaymentProcessor.odx
  ? SUCCESS: Generated PaymentProcessor_workflow.json
  ? Generated parameters file

Processing: InventoryUpdate.odx
  ? WARNING: Bindings file not found: InventoryUpdate.xml
  Skipping InventoryUpdate.odx

================================================================================
SUMMARY:
  Total ODX files: 3
  Successful: 2
  Failed: 0
================================================================================
```

## ?? What Gets Generated?

For each orchestration, you get:

1. **`{Name}_workflow.json`** - The Logic Apps workflow definition
   - Contains triggers, actions, and workflow logic
   - Ready to deploy to Azure Logic Apps Standard

2. **`{Name}_workflow.parameters.json`** - The parameters file
   - Contains configurable parameters
   - Separate from workflow for environment-specific values

## ??? Troubleshooting

| Problem | Solution |
|---------|----------|
| "No ODX files found" | Add `.odx` files to `Data/BizTalk/ODX/` |
| "Bindings file not found" | Add matching `.xml` file to `Data/BizTalk/Bindings/` with **same name** |
| "Failed to process" | Check console output for specific error message |

## ?? More Information

See [Data/README.md](README.md) for complete documentation.

## ?? Pro Tips

- ? Tests are **data-driven** - add as many orchestrations as you want!
- ? Output files are **preserved** - compare before/after runs
- ? No need to modify test code - just drop files and run!
- ? Works with **real BizTalk exports** - no need for test doubles

---

**Happy Migration! ??**
