// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    partial class Program
    {
        static int Main(string[] args)
        {
            // Check if it's a command-based invocation (new style)
            if (args.Length > 0)
            {
                string firstArg = args[0].ToLower();

                // Handle new command structure
                switch (firstArg)
                {
                    case "migrate":
                    case "convert":
                        return HandleMigrateCommand(args);

                    case "bindings-only":
                    case "bindings":
                    case "-b":
                    case "--bindings-only":
                        return HandleBindingsOnlyCommand(args);

                    case "report":
                    case "analyze":
                        return HandleReportCommand(args);

                    case "batch":
                        return HandleBatchCommand(args);

                    case "diagnose":
                        return HandleDiagnoseCommand(args);

                    case "generate-package":
                    case "package":
                        return HandleGeneratePackageCommand(args);

                    case "analyze-odx":
                    case "gap-analysis":
                    case "odx-analysis":
                        return HandleOdxAnalysisCommand(args);

                    case "help":
                    case "--help":
                    case "-h":
                    case "/?":
                        ShowHelp();
                        return 0;

                    default:
                        // If first arg is not a command, check if it's a file (legacy mode)
                        break;
                }
            }

            // Legacy mode: if first argument exists and is a file, assume old-style invocation
            // This maintains backward compatibility
            if (args.Length >= 3 && File.Exists(args[0]))
            {
                return LegacyMain(args);
            }

            // If we get here and have arguments but not enough, show error
            if (args.Length > 0 && args.Length < 3)
            {
                Console.WriteLine("Error: Invalid number of arguments.");
                Console.WriteLine();
            }

            // Show help
            ShowHelp();
            return 1;
        }

        // Original main logic preserved for backward compatibility
        static int LegacyMain(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: BizTalkToLogicApps.exe <odxPath> <bindingPath> <outputPath> [schemaVersion]");
                return 1;
            }

            var odxPath = args[0];
            var bindingPath = args[1];
            var outputPath = args[2];
            var schemaVersion = args.Length >= 4 ? args[3] : "2016-06-01";

            if (!File.Exists(odxPath))
            {
                Console.Error.WriteLine("ODX file not found: {0}", odxPath);
                return 2;
            }
            if (!File.Exists(bindingPath))
            {
                Console.Error.WriteLine("Binding file not found: {0}", bindingPath);
                return 2;
            }

            try
            {
                Console.WriteLine("Parsing orchestration, applying bindings, and generating workflow...");

                // Check if this orchestration is callable (called by other orchestrations) - this can be improved.
                // For now, we use a simple heuristic: if the orchestration has no receive shapes with Activate="true", we consider it callable.
                bool isCallable = DetectIfCallable(odxPath);
                if (isCallable)
                {
                    Console.WriteLine("  ℹ️  Detected as callable workflow (will use Request trigger)");
                }

                var json = BizTalkOrchestrationParser.GenerateWorkflowJson(odxPath, bindingPath, "Stateful", schemaVersion, isCallable);

                var parsedJson = JObject.Parse(json);
                var definition = parsedJson["definition"];
                var triggers = definition?["triggers"] as JObject;
                var actions = definition?["actions"] as JObject;

                if (triggers != null && triggers.Count > 0)
                {
                    var firstTrigger = triggers.Properties().FirstOrDefault();
                    if (firstTrigger != null)
                    {
                        var triggerConfig = firstTrigger.Value["inputs"]?["serviceProviderConfiguration"];
                        var triggerType = firstTrigger.Value["type"]?.ToString() ?? "Unknown";
                        var serviceProvider = triggerConfig?["serviceProviderId"]?.ToString().Replace("/serviceProviders/", "") ?? "Unknown";

                        Console.WriteLine("Trigger: Name='{0}' Type='{1}' Provider='{2}'",
                            firstTrigger.Name, triggerType, serviceProvider);
                    }
                }

                if (actions != null)
                {
                    Console.WriteLine("Actions mapped: {0}", actions.Count);
                }

                // Validate the generated workflow
                Console.WriteLine("Validating workflow...");
                var validator = new WorkflowValidator();
                var validationResult = validator.Validate(json);

                Console.WriteLine("  {0}", validationResult.GetSummary());

                if (validationResult.HasErrors || validationResult.HasWarnings)
                {
                    Console.WriteLine("");
                    validationResult.PrintIssues();
                }

                if (validationResult.HasErrors)
                {
                    Console.Error.WriteLine("\nValidation failed with errors. Workflow will still be saved but may not deploy successfully.");
                }

                EnsureDirectory(outputPath);
                File.WriteAllText(outputPath, json);
                Console.WriteLine("\nWorkflow definition written to: {0}", outputPath);
                Console.WriteLine("Schema version used: {0}", schemaVersion);
                Console.WriteLine("Done.");

                return validationResult.HasErrors ? 3 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleMigrateCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Missing required arguments for migration");
                Console.WriteLine("Usage: BizTalkToLogicApps migrate <orchestration.odx> <bindings.xml> [output.json] [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --refactor              Use refactored workflow generator with optimization");
                Console.WriteLine("  --target <pattern>      Target messaging pattern (aggregator, sequential-convoy, etc.)");
                Console.WriteLine("  --messaging <system>    Messaging system (servicebus, ibmmq, sapodata, saperp)");
                return 1;
            }

            string odxPath = args[1];
            string bindingsPath = args[2];
            string outputPath = null;
            string schemaVersion = "2016-06-01";
            bool useRefactor = false;
            string targetPattern = null;
            string messagingSystem = null;

            // Parse arguments
            int argIndex = 3;
            while (argIndex < args.Length)
            {
                string arg = args[argIndex];

                if (arg == "--refactor")
                {
                    useRefactor = true;
                    argIndex++;
                }
                else if (arg == "--target" && argIndex + 1 < args.Length)
                {
                    targetPattern = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--messaging" && argIndex + 1 < args.Length)
                {
                    messagingSystem = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--schema-version" && argIndex + 1 < args.Length)
                {
                    schemaVersion = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (!arg.StartsWith("--"))
                {
                    // Positional argument (output path or schema version)
                    if (outputPath == null)
                    {
                        outputPath = arg;
                    }
                    else
                    {
                        schemaVersion = arg;
                    }
                    argIndex++;
                }
                else
                {
                    argIndex++;
                }
            }

            if (outputPath == null)
            {
                outputPath = Path.ChangeExtension(odxPath, ".workflow.json");
            }

            if (useRefactor)
            {
                Console.WriteLine("Using refactored workflow generator with optimizations");
                if (!string.IsNullOrEmpty(targetPattern))
                {
                    Console.WriteLine("  Target pattern: {0}", targetPattern);
                }
                if (!string.IsNullOrEmpty(messagingSystem))
                {
                    Console.WriteLine("  Messaging system: {0}", messagingSystem);
                }

                return HandleRefactoredMigration(odxPath, bindingsPath, outputPath, schemaVersion, targetPattern, messagingSystem);
            }

            // Reuse the legacy main logic (now includes callable detection)
            string[] legacyArgs = { odxPath, bindingsPath, outputPath, schemaVersion };
            return LegacyMain(legacyArgs);
        }

        /// <summary>
        /// Handles bindings-only workflow generation (no orchestration file required).
        /// Creates one Logic Apps workflow per receive location, matching send ports via filters.
        /// </summary>
        static int HandleBindingsOnlyCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing required bindings file");
                Console.WriteLine("Usage: BizTalkToLogicApps bindings-only <bindings.xml> [outputDirectory] [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --refactor              Use refactored workflow generator with optimization");
                Console.WriteLine("  --target <pattern>      Target messaging pattern (aggregator, sequential-convoy, etc.)");
                Console.WriteLine("  --messaging <system>    Messaging system (servicebus, ibmmq, sapodata, saperp)");
                Console.WriteLine();
                Console.WriteLine("This command generates Logic Apps workflows from BizTalk bindings WITHOUT orchestration files.");
                Console.WriteLine("One workflow is created per receive location, with filtered send ports as actions.");
                return 1;
            }

            string bindingsPath = args[1];
            string outputDir = null;
            string schemaVersion = "2016-06-01";
            bool useRefactor = false;
            string targetPattern = null;
            string messagingSystem = null;

            // Parse arguments
            int argIndex = 2;
            while (argIndex < args.Length)
            {
                string arg = args[argIndex];

                if (arg == "--refactor")
                {
                    useRefactor = true;
                    argIndex++;
                }
                else if (arg == "--target" && argIndex + 1 < args.Length)
                {
                    targetPattern = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--messaging" && argIndex + 1 < args.Length)
                {
                    messagingSystem = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--schema-version" && argIndex + 1 < args.Length)
                {
                    schemaVersion = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (!arg.StartsWith("--"))
                {
                    // Positional argument (output directory or schema version)
                    if (outputDir == null)
                    {
                        outputDir = arg;
                    }
                    else
                    {
                        schemaVersion = arg;
                    }
                    argIndex++;
                }
                else
                {
                    argIndex++;
                }
            }

            if (outputDir == null)
            {
                outputDir = Path.Combine(Path.GetDirectoryName(bindingsPath) ?? ".", "LogicAppsWorkflows");
            }

            if (useRefactor)
            {
                Console.WriteLine("Using refactored workflow generator with optimizations");
                if (!string.IsNullOrEmpty(targetPattern))
                {
                    Console.WriteLine("  Target pattern: {0}", targetPattern);
                }
                if (!string.IsNullOrEmpty(messagingSystem))
                {
                    Console.WriteLine("  Messaging system: {0}", messagingSystem);
                }
            }

            if (!File.Exists(bindingsPath))
            {
                Console.Error.WriteLine("Error: Bindings file not found: {0}", bindingsPath);
                return 2;
            }

            try
            {
                Console.WriteLine("=== BINDINGS-ONLY WORKFLOW GENERATION ===");
                Console.WriteLine("Bindings file: {0}", Path.GetFileName(bindingsPath));
                Console.WriteLine("Output directory: {0}", outputDir);
                Console.WriteLine("Schema version: {0}", schemaVersion);
                Console.WriteLine();

                // Ensure output directory exists
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Load connector registry (optional - can work without it)
                Console.WriteLine("Loading connector registry...");
                ConnectorSchemaRegistry registry = null;
                try
                {
                    var registryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schemas", "Connectors", "connector-registry.json");
                    if (File.Exists(registryPath))
                    {
                        registry = ConnectorSchemaRegistry.LoadFromFile(registryPath);
                        Console.WriteLine("  Loaded {0} connector schemas", registry.ConnectorCount);
                    }
                    else
                    {
                        Console.WriteLine("  ⚠️  Registry file not found, using legacy connector mappings");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ⚠️  Failed to load registry: {0}", ex.Message);
                    Console.WriteLine("  Using legacy connector mappings");
                }
                Console.WriteLine();

                // Parse bindings
                Console.WriteLine("Parsing bindings file...");
                var bindings = BindingSnapshot.Parse(bindingsPath);
                Console.WriteLine("  Receive Locations: {0}", bindings.ReceiveLocations.Count);
                Console.WriteLine("  Send Ports: {0}", bindings.SendPorts.Count);
                Console.WriteLine();

                // Generate workflows from bindings
                Console.WriteLine("Generating workflows from bindings...");
                var workflows = LogicAppsMapper.MapBindingsToWorkflows(bindings);
                Console.WriteLine();

                if (workflows.Count == 0)
                {
                    Console.WriteLine("⚠️  No workflows generated. Check if bindings file contains receive locations.");
                    return 1;
                }

                // Generate JSON for each workflow
                var validator = new WorkflowValidator();
                int successCount = 0;
                int warningCount = 0;
                int errorCount = 0;

                foreach (var workflow in workflows)
                {
                    try
                    {
                        Console.WriteLine();
                        Console.WriteLine("─────────────────────────────────────────────────────────");
                        Console.WriteLine("Generating workflow: {0}", workflow.Name);
                        Console.WriteLine("  Triggers: {0}", workflow.Triggers.Count);
                        Console.WriteLine("  Actions: {0}", workflow.Actions.Count);

                        // Generate JSON (use LogicAppJsonGenerator with lowercase 'j')
                        var json = LogicAppJSONGenerator.GenerateStandardWorkflow(workflow, "Stateful", schemaVersion, registry);

                        // Validate
                        Console.Write("  Validating... ");
                        var validationResult = validator.Validate(json);
                        Console.WriteLine(validationResult.GetSummary());

                        if (validationResult.HasErrors)
                        {
                            Console.WriteLine();
                            validationResult.PrintIssues();
                            errorCount++;
                        }
                        else if (validationResult.HasWarnings)
                        {
                            Console.WriteLine();
                            validationResult.PrintIssues();
                            warningCount++;
                        }
                        else
                        {
                            successCount++;
                        }

                        // Save workflow JSON
                        string workflowDir = Path.Combine(outputDir, workflow.Name);
                        if (!Directory.Exists(workflowDir))
                        {
                            Directory.CreateDirectory(workflowDir);
                        }

                        string workflowPath = Path.Combine(workflowDir, "workflow.json");
                        File.WriteAllText(workflowPath, json);
                        Console.WriteLine("  Saved: {0}", workflowPath);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("  ✗ Error generating workflow '{0}': {1}", workflow.Name, ex.Message);
                        errorCount++;
                    }
                }

                // Generate connections.json
                Console.WriteLine();
                Console.WriteLine("─────────────────────────────────────────────────────────");
                Console.Write("Generating connections.json... ");
                try
                {
                    // Use first workflow to generate connections (they all share same connectors)
                    if (workflows.Count > 0)
                    {
                        var sampleWorkflow = workflows[0];
                        var sampleJson = LogicAppJSONGenerator.GenerateStandardWorkflow(sampleWorkflow, "Stateful", schemaVersion, registry);
                        var connectionsJson = GenerateConnectionsJson(sampleJson, "BizTalkBindings");
                        File.WriteAllText(Path.Combine(outputDir, "connections.json"), connectionsJson);
                        Console.WriteLine("✓");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("✗");
                    Console.WriteLine("  Warning: Failed to generate connections.json: {0}", ex.Message);
                }

                // Generate host.json
                Console.Write("Generating host.json... ");
                try
                {
                    var hostJson = GenerateHostJson();
                    File.WriteAllText(Path.Combine(outputDir, "host.json"), hostJson);
                    Console.WriteLine("✓");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("✗");
                    Console.WriteLine("  Warning: Failed to generate host.json: {0}", ex.Message);
                }

                // Generate local.settings.json
                Console.Write("Generating local.settings.json... ");
                try
                {
                    var localSettings = GenerateLocalSettings("BizTalkBindings");
                    File.WriteAllText(Path.Combine(outputDir, "local.settings.json"), localSettings);
                    Console.WriteLine("✓");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("✗");
                    Console.WriteLine("  Warning: Failed to generate local.settings.json: {0}", ex.Message);
                }

                // Summary
                Console.WriteLine();
                Console.WriteLine("═════════════════════════════════════════════════════════");
                Console.WriteLine("BINDINGS-ONLY GENERATION COMPLETE");
                Console.WriteLine("═════════════════════════════════════════════════════════");
                Console.WriteLine("Total workflows: {0}", workflows.Count);
                Console.WriteLine("  ✓ Success: {0}", successCount);
                if (warningCount > 0)
                    Console.WriteLine("  ⚠ Warnings: {0}", warningCount);
                if (errorCount > 0)
                    Console.WriteLine("  ✗ Errors: {0}", errorCount);
                Console.WriteLine();
                Console.WriteLine("Output directory: {0}", outputDir);
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("  1. Review generated workflow.json files");
                Console.WriteLine("  2. Update connections.json with connection parameters");
                Console.WriteLine("  3. Update local.settings.json with Azure settings");
                Console.WriteLine("  4. Deploy using VS Code or Azure CLI");
                Console.WriteLine();

                return errorCount > 0 ? 3 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Error: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleReportCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing orchestration file");
                Console.WriteLine("Usage: BizTalkToLogicApps report <orchestration.odx> [--format html|markdown] [--output file]");
                return 1;
            }

            string odxPath = args[1];
            string outputPath = null;
            ReportFormat format = ReportFormat.Html;

            // Parse optional arguments
            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if ((arg == "--format" || arg == "-f") && i + 1 < args.Length)
                {
                    string formatStr = args[++i].ToLower();
                    if (formatStr == "md" || formatStr == "markdown")
                    {
                        format = ReportFormat.Markdown;
                    }
                    else if (formatStr == "html")
                    {
                        format = ReportFormat.Html;
                    }
                    else
                    {
                        Console.WriteLine("Warning: Unknown format '{0}', using HTML", formatStr);
                    }
                }
                else if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
            }

            if (!File.Exists(odxPath))
            {
                Console.WriteLine("Error: Orchestration file not found: {0}", odxPath);
                return 1;
            }

            try
            {
                Console.WriteLine("Generating migration report for: {0}", Path.GetFileName(odxPath));
                Console.WriteLine("  Format: {0}", format);

                OrchestrationReportGenerator.ExportDiagnosticReport(odxPath, outputPath, format);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error generating report: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleBatchCommand(string[] args)
        {
            if (args.Length < 2)
            {
                ShowBatchHelp();
                return 1;
            }

            string subCommand = args[1].ToLower();

            switch (subCommand)
            {
                case "report":
                    return HandleBatchReport(args);
                case "convert":
                case "migrate":
                    return HandleBatchConvert(args);
                default:
                    Console.WriteLine("Error: Unknown batch sub-command '{0}'", subCommand);
                    ShowBatchHelp();
                    return 1;
            }
        }

        static int HandleBatchReport(string[] args)
        {
            string directory = null;
            string filesStr = null;
            string bindingsPath = null;
            string outputPath = null;
            ReportFormat format = ReportFormat.Html;

            // Parse command line arguments
            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if ((arg == "--directory" || arg == "-d") && i + 1 < args.Length)
                {
                    directory = args[++i];
                }
                else if ((arg == "--files" || arg == "-f") && i + 1 < args.Length)
                {
                    filesStr = args[++i];
                }
                else if ((arg == "--bindings" || arg == "-b") && i + 1 < args.Length)
                {
                    bindingsPath = args[++i];
                }
                else if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
                else if ((arg == "--format") && i + 1 < args.Length)
                {
                    string formatStr = args[++i].ToLower();
                    if (formatStr == "md" || formatStr == "markdown")
                    {
                        format = ReportFormat.Markdown;
                    }
                }
            }

            // Validate inputs
            if (string.IsNullOrEmpty(directory) && string.IsNullOrEmpty(filesStr))
            {
                Console.WriteLine("Error: You must specify either --directory or --files");
                ShowBatchHelp();
                return 1;
            }

            string[] odxFiles = null;

            try
            {
                // Determine which orchestrations to process
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        Console.WriteLine("Error: Directory not found: {0}", directory);
                        return 1;
                    }

                    Console.WriteLine("Scanning directory: {0}", directory);
                    odxFiles = Directory.GetFiles(directory, "*.odx", SearchOption.AllDirectories);
                    Console.WriteLine("Found {0} orchestration file(s)", odxFiles.Length);
                }
                else if (!string.IsNullOrEmpty(filesStr))
                {
                    odxFiles = filesStr.Split(',').Select(f => f.Trim()).ToArray();
                    Console.WriteLine("Processing {0} specified file(s)", odxFiles.Length);
                }

                if (odxFiles == null || odxFiles.Length == 0)
                {
                    Console.WriteLine("No orchestration files found to process");
                    return 1;
                }

                // Validate all files exist
                foreach (var file in odxFiles)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine("Error: Orchestration file not found: {0}", file);
                        return 1;
                    }
                }

                // Process batch
                Console.WriteLine();
                Console.WriteLine("Starting batch report generation...");
                Console.WriteLine("  Format: {0}", format);
                if (!string.IsNullOrEmpty(bindingsPath))
                {
                    Console.WriteLine("  Bindings: {0}", Path.GetFileName(bindingsPath));
                }
                Console.WriteLine();

                OrchestrationReportGenerator.ExportBatchDiagnosticReport(odxFiles, bindingsPath, outputPath, format);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error during batch report generation: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleBatchConvert(string[] args)
        {
            string directory = null;
            string filesStr = null;
            string bindingsPath = null;
            string outputPath = null;
            string schemaVersion = "2016-06-01";
            bool useRefactor = false;
            string targetPattern = null;
            string messagingSystem = null;

            // Parse command line arguments
            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];

                if ((arg == "--directory" || arg == "-d") && i + 1 < args.Length)
                {
                    directory = args[++i];
                }
                else if ((arg == "--files" || arg == "-f") && i + 1 < args.Length)
                {
                    filesStr = args[++i];
                }
                else if ((arg == "--bindings" || arg == "-b") && i + 1 < args.Length)
                {
                    bindingsPath = args[++i];
                }
                else if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
                else if ((arg == "--schema-version") && i + 1 < args.Length)
                {
                    schemaVersion = args[++i];
                }
                else if (arg == "--refactor")
                {
                    useRefactor = true;
                }
                else if (arg == "--target" && i + 1 < args.Length)
                {
                    targetPattern = args[++i];
                }
                else if (arg == "--messaging" && i + 1 < args.Length)
                {
                    messagingSystem = args[++i];
                }
            }

            // Validate inputs
            if (string.IsNullOrEmpty(directory) && string.IsNullOrEmpty(filesStr))
            {
                Console.WriteLine("Error: You must specify either --directory or --files");
                ShowBatchHelp();
                return 1;
            }

            if (string.IsNullOrEmpty(bindingsPath))
            {
                Console.WriteLine("Error: Bindings file is required for batch conversion (--bindings)");
                return 1;
            }

            if (!File.Exists(bindingsPath))
            {
                Console.WriteLine("Error: Bindings file not found: {0}", bindingsPath);
                return 1;
            }

            string[] odxFiles = null;

            try
            {
                // Determine which orchestrations to process
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        Console.WriteLine("Error: Directory not found: {0}", directory);
                        return 1;
                    }

                    Console.WriteLine("Scanning directory: {0}", directory);
                    odxFiles = Directory.GetFiles(directory, "*.odx", SearchOption.AllDirectories);
                    Console.WriteLine("Found {0} orchestration file(s)", odxFiles.Length);
                }
                else if (!string.IsNullOrEmpty(filesStr))
                {
                    odxFiles = filesStr.Split(',').Select(f => f.Trim()).ToArray();
                    Console.WriteLine("Processing {0} specified file(s)", odxFiles.Length);
                }

                if (odxFiles == null || odxFiles.Length == 0)
                {
                    Console.WriteLine("No orchestration files found to process");
                    return 1;
                }

                // Set default output directory
                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.GetDirectoryName(odxFiles[0]) ?? Environment.CurrentDirectory;
                }

                // Validate all files exist
                foreach (var file in odxFiles)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine("Error: Orchestration file not found: {0}", file);
                        return 1;
                    }
                }

                // PASS 1: Detect callable orchestrations (those called by other orchestrations)
                Console.WriteLine();
                Console.WriteLine("Detecting callable workflows (Pass 1/2)...");
                var callableOrchestrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var odxPath in odxFiles)
                {
                    try
                    {
                        var tempModel = BizTalkOrchestrationParser.ParseOdx(odxPath);

                        // Helper function to recursively traverse all shapes in the tree
                        void TraverseShapes(ShapeModel shape, Action<ShapeModel> action)
                        {
                            action(shape);
                            if (shape.Children != null)
                            {
                                foreach (var child in shape.Children)
                                {
                                    TraverseShapes(child, action);
                                }
                            }
                        }

                        // Look for Call/Start shapes that reference other orchestrations (recursively)
                        foreach (var rootShape in tempModel.Shapes)
                        {
                            TraverseShapes(rootShape, shape =>
                            {
                                if (shape is CallShapeModel callShape && !string.IsNullOrEmpty(callShape.Invokee))
                                {
                                    // Add both FQN and simple name (e.g., "Namespace.Orchestration" and "Orchestration")
                                    callableOrchestrations.Add(callShape.Invokee);
                                    var simpleName = callShape.Invokee.Contains(".") ? callShape.Invokee.Substring(callShape.Invokee.LastIndexOf('.') + 1) : callShape.Invokee;
                                    callableOrchestrations.Add(simpleName);
                                    Console.WriteLine("  Found Call: {0} -> {1}", Path.GetFileNameWithoutExtension(odxPath), callShape.Invokee);
                                }
                                else if (shape is StartShapeModel startShape && !string.IsNullOrEmpty(startShape.Invokee))
                                {
                                    // Add both FQN and simple name (e.g., "Namespace.Orchestration" and "Orchestration")
                                    callableOrchestrations.Add(startShape.Invokee);
                                    var simpleName = startShape.Invokee.Contains(".") ? startShape.Invokee.Substring(startShape.Invokee.LastIndexOf('.') + 1) : startShape.Invokee;
                                    callableOrchestrations.Add(simpleName);
                                    Console.WriteLine("  Found Start: {0} -> {1}", Path.GetFileNameWithoutExtension(odxPath), startShape.Invokee);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  ERROR parsing {0}: {1}", Path.GetFileName(odxPath), ex.Message);
                    }
                }

                if (callableOrchestrations.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Found {0} callable workflow(s):", callableOrchestrations.Count);
                    foreach (var callable in callableOrchestrations.OrderBy(c => c))
                    {
                        Console.WriteLine("  - {0} (will use Request trigger)", callable);
                    }
                }
                else
                {
                    Console.WriteLine("  No callable workflows detected (no Call/Start shapes found)");
                }

                // PASS 2: Process batch conversions
                Console.WriteLine();
                Console.WriteLine("Converting orchestrations (Pass 2/2)...");
                Console.WriteLine("  Output directory: {0}", outputPath);
                Console.WriteLine("  Schema version: {0}", schemaVersion);
                Console.WriteLine("  Bindings: {0}", Path.GetFileName(bindingsPath));
                if (useRefactor)
                {
                    Console.WriteLine("  Using refactored generator: YES");
                    if (!string.IsNullOrEmpty(targetPattern))
                    {
                        Console.WriteLine("  Target pattern: {0}", targetPattern);
                    }
                    if (!string.IsNullOrEmpty(messagingSystem))
                    {
                        Console.WriteLine("  Messaging system: {0}", messagingSystem);
                    }
                }
                Console.WriteLine();

                int successCount = 0;
                int failCount = 0;

                // Parse bindings and load registry once for all files
                var binding = BindingSnapshot.Parse(bindingsPath);
                var registry = BizTalkOrchestrationParser.TryLoadConnectorRegistry();
                var validator = new WorkflowValidator();

                foreach (var odxPath in odxFiles)
                {
                    try
                    {
                        Console.Write("Converting {0}... ", Path.GetFileName(odxPath));

                        string json;

                        if (useRefactor)
                        {
                            // Use refactored workflow generator
                            var options = new BizTalktoLogicApps.ODXtoWFMigrator.Refactoring.RefactoringOptions
                            {
                                SchemaVersion = schemaVersion,
                                WorkflowType = "Stateful"
                            };

                            // Set messaging system if provided
                            if (!string.IsNullOrEmpty(messagingSystem))
                            {
                                var msgLower = messagingSystem.ToLowerInvariant();
                                if (msgLower == "servicebus")
                                {
                                    options.PreferredMessagingPlatform = "ServiceBus";
                                }
                                else if (msgLower == "ibmmq" || msgLower == "mq")
                                {
                                    options.PreferredMessagingPlatform = "IbmMq";
                                }
                            }

                            json = BizTalktoLogicApps.ODXtoWFMigrator.Refactoring.RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                                odxPath,
                                bindingsPath,
                                options);
                        }
                        else
                        {
                            // Parse orchestration
                            var orchestration = BizTalkOrchestrationParser.ParseOdx(odxPath);

                            // Check if this orchestration is callable (needs Request trigger for nested workflow support)
                            bool isCallable = callableOrchestrations.Contains(orchestration.Name) ||
                                             callableOrchestrations.Contains(orchestration.FullName);

                            // Map to Logic Apps with callable flag
                            var map = LogicAppsMapper.MapToLogicApp(orchestration, binding, isCallable);

                            // Generate JSON
                            json = LogicAppJSONGenerator.GenerateStandardWorkflow(map, "Stateful", schemaVersion, registry);
                        }

                        // Validate the generated workflow
                        var validationResult = validator.Validate(json);

                        // Save Logic App JSON
                        string fileName = Path.GetFileNameWithoutExtension(odxPath) + ".workflow.json";
                        string fullPath = Path.Combine(outputPath, fileName);

                        EnsureDirectory(fullPath);
                        File.WriteAllText(fullPath, json);

                        if (validationResult.HasErrors)
                        {
                            Console.WriteLine("✓ (with validation errors)");
                        }
                        else
                        {
                            Console.WriteLine("✓");
                        }

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("✗");
                        Console.WriteLine("  Error: {0}", ex.Message);
                        failCount++;
                    }
                }

                // Summary
                Console.WriteLine();
                Console.WriteLine(new string('=', 52));
                Console.WriteLine("[BATCH COMPLETE] Processed {0} orchestration(s)", odxFiles.Length);
                Console.WriteLine("  Successful: {0}", successCount);
                Console.WriteLine("  Failed: {0}", failCount);
                Console.WriteLine(new string('=', 52));

                return failCount > 0 ? 3 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error during batch conversion: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleDiagnoseCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing orchestration file");
                Console.WriteLine("Usage: BizTalkToLogicApps diagnose <orchestration.odx>");
                return 1;
            }

            string odxPath = args[1];

            if (!File.Exists(odxPath))
            {
                Console.WriteLine("Error: Orchestration file not found: {0}", odxPath);
                return 1;
            }

            try
            {
                Console.WriteLine("Running diagnostics on: {0}", Path.GetFileName(odxPath));
                BizTalkOrchestrationParser.DiagnoseOrchestration(odxPath);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error running diagnostics: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static int HandleGeneratePackageCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Missing required arguments");
                Console.WriteLine("Usage: BizTalkToLogicApps generate-package <orchestration.odx> <bindings.xml> [outputDirectory] [options]");
                Console.WriteLine("Options:");
                Console.WriteLine("  --refactor              Use refactored workflow generator with optimization");
                Console.WriteLine("  --target <pattern>      Target messaging pattern (aggregator, sequential-convoy, etc.)");
                Console.WriteLine("  --messaging <system>    Messaging system (servicebus, ibmmq, sapodata, saperp)");
                return 1;
            }

            string odxPath = args[1];
            string bindingsPath = args[2];
            string outputDir = null;
            string schemaVersion = "2016-06-01";
            bool useRefactor = false;
            string targetPattern = null;
            string messagingSystem = null;

            // Parse arguments
            int argIndex = 3;
            while (argIndex < args.Length)
            {
                string arg = args[argIndex];

                if (arg == "--refactor")
                {
                    useRefactor = true;
                    argIndex++;
                }
                else if (arg == "--target" && argIndex + 1 < args.Length)
                {
                    targetPattern = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--messaging" && argIndex + 1 < args.Length)
                {
                    messagingSystem = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (arg == "--schema-version" && argIndex + 1 < args.Length)
                {
                    schemaVersion = args[argIndex + 1];
                    argIndex += 2;
                }
                else if (!arg.StartsWith("--"))
                {
                    // Positional argument (output directory or schema version)
                    if (outputDir == null)
                    {
                        outputDir = arg;
                    }
                    else
                    {
                        schemaVersion = arg;
                    }
                    argIndex++;
                }
                else
                {
                    argIndex++;
                }
            }

            if (outputDir == null)
            {
                outputDir = Path.Combine(Path.GetDirectoryName(odxPath) ?? ".", "LogicAppsPackage");
            }

            if (!File.Exists(odxPath))
            {
                Console.WriteLine("Error: Orchestration file not found: {0}", odxPath);
                return 1;
            }

            if (!File.Exists(bindingsPath))
            {
                Console.WriteLine("Error: Bindings file not found: {0}", bindingsPath);
                return 1;
            }

            try
            {
                Console.WriteLine("Generating Logic Apps Standard deployment package...");
                Console.WriteLine("  Orchestration: {0}", Path.GetFileName(odxPath));
                Console.WriteLine("  Bindings: {0}", Path.GetFileName(bindingsPath));
                Console.WriteLine("  Output: {0}", outputDir);
                Console.WriteLine();

                string workflowName = Path.GetFileNameWithoutExtension(odxPath);
                string workflowDir = Path.Combine(outputDir, workflowName);

                // Ensure the workflow directory exists
                if (!Directory.Exists(workflowDir))
                {
                    Directory.CreateDirectory(workflowDir);
                }

                Console.Write("Generating workflow JSON... ");
                string workflowJson;

                if (useRefactor)
                {
                    var options = new BizTalktoLogicApps.ODXtoWFMigrator.Refactoring.RefactoringOptions
                    {
                        SchemaVersion = schemaVersion,
                        WorkflowType = "Stateful"
                    };

                    // Set messaging system if provided
                    if (!string.IsNullOrEmpty(messagingSystem))
                    {
                        var msgLower = messagingSystem.ToLowerInvariant();
                        if (msgLower == "servicebus")
                        {
                            options.PreferredMessagingPlatform = "ServiceBus";
                        }
                        else if (msgLower == "ibmmq" || msgLower == "mq")
                        {
                            options.PreferredMessagingPlatform = "IbmMq";
                        }
                    }

                    workflowJson = BizTalktoLogicApps.ODXtoWFMigrator.Refactoring.RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                        odxPath,
                        bindingsPath,
                        options);
                }
                else
                {
                    workflowJson = BizTalkOrchestrationParser.GenerateWorkflowJson(odxPath, bindingsPath, "Stateful", schemaVersion);
                }

                string workflowPath = Path.Combine(workflowDir, "workflow.json");
                File.WriteAllText(workflowPath, workflowJson);
                Console.WriteLine("✓");

                Console.Write("Generating connections.json... ");
                string connectionsJson = GenerateConnectionsJson(workflowJson, workflowName);
                File.WriteAllText(Path.Combine(outputDir, "connections.json"), connectionsJson);
                Console.WriteLine("✓");

                Console.Write("Generating host.json... ");
                string hostJson = GenerateHostJson();
                File.WriteAllText(Path.Combine(outputDir, "host.json"), hostJson);
                Console.WriteLine("✓");

                Console.Write("Generating local.settings.json template... ");
                string localSettings = GenerateLocalSettings(workflowName);
                File.WriteAllText(Path.Combine(outputDir, "local.settings.json"), localSettings);
                Console.WriteLine("✓");

                Console.Write("Generating deployment script... ");
                string deployScript = GenerateDeploymentScript(workflowName);
                File.WriteAllText(Path.Combine(outputDir, "deploy.ps1"), deployScript);
                Console.WriteLine("✓");

                Console.Write("Generating Azure DevOps pipeline... ");
                string pipelineYaml = GenerateAzureDevOpsPipeline(workflowName);
                File.WriteAllText(Path.Combine(outputDir, "azure-pipelines.yml"), pipelineYaml);
                Console.WriteLine("✓");

                Console.Write("Generating README... ");
                string readme = GeneratePackageReadme(workflowName, odxPath, bindingsPath);
                File.WriteAllText(Path.Combine(outputDir, "README.md"), readme);
                Console.WriteLine("✓");

                Console.WriteLine();
                Console.WriteLine(new string('=', 60));
                Console.WriteLine("[SUCCESS] Deployment package generated successfully!");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine();
                Console.WriteLine("Package location: {0}", outputDir);
                Console.WriteLine();
                Console.WriteLine("Package contents:");
                Console.WriteLine("  {0}/workflow.json       - Logic Apps workflow definition", workflowName);
                Console.WriteLine("  connections.json         - API connections configuration");
                Console.WriteLine("  host.json                - Logic Apps Standard host config");
                Console.WriteLine("  local.settings.json      - App settings template");
                Console.WriteLine("  deploy.ps1               - PowerShell deployment script");
                Console.WriteLine("  azure-pipelines.yml      - Azure DevOps CI/CD pipeline");
                Console.WriteLine("  README.md                - Deployment instructions");
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("  1. Review and update local.settings.json with your connection strings");
                Console.WriteLine("  2. Deploy using VS Code Azure Logic Apps extension, or");
                Console.WriteLine("  3. Run deploy.ps1 with required parameters, or");
                Console.WriteLine("  4. Use azure-pipelines.yml for automated CI/CD deployment");
                Console.WriteLine();

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error generating package: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static string GenerateConnectionsJson(string workflowJson, string workflowName)
        {
            var connectionsObj = new JObject();
            var serviceProviderConnections = new JObject();
            var managedApiConnections = new JObject();

            var workflow = JObject.Parse(workflowJson);
            var definition = workflow["definition"];

            var connectorsFound = new System.Collections.Generic.HashSet<string>();

            void ProcessActions(JObject actionsObj)
            {
                if (actionsObj == null) return;

                foreach (var action in actionsObj.Properties())
                {
                    var actionValue = action.Value as JObject;
                    if (actionValue == null) continue;

                    var type = actionValue["type"]?.ToString();
                    if (type == "ServiceProvider")
                    {
                        var config = actionValue["inputs"]?["serviceProviderConfiguration"] as JObject;
                        if (config != null)
                        {
                            var connectionName = config["connectionName"]?.ToString();
                            var serviceProviderId = config["serviceProviderId"]?.ToString();

                            if (!string.IsNullOrEmpty(connectionName) && !connectorsFound.Contains(connectionName))
                            {
                                connectorsFound.Add(connectionName);

                                var connectionConfig = new JObject();
                                connectionConfig["parameterValues"] = new JObject();
                                connectionConfig["serviceProvider"] = new JObject
                                {
                                    ["id"] = serviceProviderId ?? ("/serviceProviders/" + connectionName)
                                };

                                serviceProviderConnections[connectionName] = connectionConfig;
                            }
                        }
                    }

                    var childActions = actionValue["actions"] as JObject;
                    if (childActions != null)
                    {
                        ProcessActions(childActions);
                    }

                    var elseActions = actionValue["else"]?["actions"] as JObject;
                    if (elseActions != null)
                    {
                        ProcessActions(elseActions);
                    }
                }
            }

            var triggers = definition?["triggers"] as JObject;
            if (triggers != null)
            {
                foreach (var trigger in triggers.Properties())
                {
                    var triggerValue = trigger.Value as JObject;
                    if (triggerValue?["type"]?.ToString() == "ServiceProvider")
                    {
                        var config = triggerValue["inputs"]?["serviceProviderConfiguration"] as JObject;
                        if (config != null)
                        {
                            var connectionName = config["connectionName"]?.ToString();
                            var serviceProviderId = config["serviceProviderId"]?.ToString();

                            if (!string.IsNullOrEmpty(connectionName) && !connectorsFound.Contains(connectionName))
                            {
                                connectorsFound.Add(connectionName);

                                var connectionConfig = new JObject();
                                connectionConfig["parameterValues"] = new JObject();
                                connectionConfig["serviceProvider"] = new JObject
                                {
                                    ["id"] = serviceProviderId ?? ("/serviceProviders/" + connectionName)
                                };

                                serviceProviderConnections[connectionName] = connectionConfig;
                            }
                        }
                    }
                }
            }

            var actions = definition?["actions"] as JObject;
            if (actions != null)
            {
                ProcessActions(actions);
            }

            connectionsObj["serviceProviderConnections"] = serviceProviderConnections;
            connectionsObj["managedApiConnections"] = managedApiConnections;

            return JsonConvert.SerializeObject(connectionsObj, Formatting.Indented);
        }

        static string GenerateHostJson()
        {
            var hostConfig = new JObject();
            hostConfig["version"] = "2.0";

            var extensionBundle = new JObject();
            extensionBundle["id"] = "Microsoft.Azure.Functions.ExtensionBundle.Workflows";
            extensionBundle["version"] = "[1.*, 2.0.0)";
            hostConfig["extensionBundle"] = extensionBundle;

            return JsonConvert.SerializeObject(hostConfig, Formatting.Indented);
        }

        static string GenerateLocalSettings(string workflowName)
        {
            var settings = new JObject();
            settings["IsEncrypted"] = false;

            var values = new JObject();
            values["AzureWebJobsStorage"] = "UseDevelopmentStorage=true";
            values["FUNCTIONS_WORKER_RUNTIME"] = "node";
            values["WORKFLOWS_TENANT_ID"] = "<your-tenant-id>";
            values["WORKFLOWS_SUBSCRIPTION_ID"] = "<your-subscription-id>";
            values["WORKFLOWS_RESOURCE_GROUP_NAME"] = "<your-resource-group>";
            values["WORKFLOWS_LOCATION_NAME"] = "<azure-region>";
            values["WORKFLOWS_MANAGEMENT_BASE_URI"] = "https://management.azure.com/";

            settings["Values"] = values;

            return JsonConvert.SerializeObject(settings, Formatting.Indented);
        }

        static string GenerateDeploymentScript(string workflowName)
        {
            var script = new System.Text.StringBuilder();
            script.AppendLine("# Logic Apps Standard Deployment Script");
            script.AppendLine("# Generated by BizTalk to Logic Apps Migration Tool");
            script.AppendLine();
            script.AppendLine("param(");
            script.AppendLine("    [Parameter(Mandatory=$true)]");
            script.AppendLine("    [string]$ResourceGroup,");
            script.AppendLine();
            script.AppendLine("    [Parameter(Mandatory=$true)]");
            script.AppendLine("    [string]$LogicAppName,");
            script.AppendLine();
            script.AppendLine("    [Parameter(Mandatory=$true)]");
            script.AppendLine("    [string]$StorageAccountName,");
            script.AppendLine();
            script.AppendLine("    [Parameter(Mandatory=$false)]");
            script.AppendLine("    [string]$Location = \"East US\"");
            script.AppendLine(")");
            script.AppendLine();
            script.AppendLine("Write-Host \"=== Logic Apps Standard Deployment ===\" -ForegroundColor Cyan");
            script.AppendLine("Write-Host \"Workflow: " + workflowName + "\"");
            script.AppendLine("Write-Host \"Resource Group: $ResourceGroup\"");
            script.AppendLine("Write-Host \"Logic App: $LogicAppName\"");
            script.AppendLine("Write-Host");
            script.AppendLine();
            script.AppendLine("# Ensure Azure CLI is logged in");
            script.AppendLine("Write-Host \"Checking Azure CLI login status...\" -ForegroundColor Yellow");
            script.AppendLine("$account = az account show 2>$null | ConvertFrom-Json");
            script.AppendLine("if (-not $account) {");
            script.AppendLine("    Write-Host \"Not logged in. Please login to Azure CLI.\" -ForegroundColor Red");
            script.AppendLine("    az login");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("# Create resource group if it doesn't exist");
            script.AppendLine("Write-Host \"Ensuring resource group exists...\" -ForegroundColor Yellow");
            script.AppendLine("az group create --name $ResourceGroup --location $Location");
            script.AppendLine();
            script.AppendLine("# Create storage account if it doesn't exist");
            script.AppendLine("Write-Host \"Ensuring storage account exists...\" -ForegroundColor Yellow");
            script.AppendLine("$storageExists = az storage account show --name $StorageAccountName --resource-group $ResourceGroup 2>$null");
            script.AppendLine("if (-not $storageExists) {");
            script.AppendLine("    Write-Host \"Creating storage account $StorageAccountName...\" -ForegroundColor Yellow");
            script.AppendLine("    az storage account create --name $StorageAccountName --resource-group $ResourceGroup --location $Location --sku Standard_LRS");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("# Get storage connection string");
            script.AppendLine("Write-Host \"Retrieving storage connection string...\" -ForegroundColor Yellow");
            script.AppendLine("$storageConnStr = az storage account show-connection-string --name $StorageAccountName --resource-group $ResourceGroup --query connectionString -o tsv");
            script.AppendLine();
            script.AppendLine("# Create ZIP package");
            script.AppendLine("Write-Host \"Creating deployment package...\" -ForegroundColor Yellow");
            script.AppendLine("$packagePath = \"./deploy.zip\"");
            script.AppendLine("if (Test-Path $packagePath) {");
            script.AppendLine("    Remove-Item $packagePath -Force");
            script.AppendLine("}");
            script.AppendLine("Compress-Archive -Path ./* -DestinationPath $packagePath -Force");
            script.AppendLine();
            script.AppendLine("# Create Logic App if it doesn't exist");
            script.AppendLine("Write-Host \"Checking if Logic App exists...\" -ForegroundColor Yellow");
            script.AppendLine("$logicAppExists = az logicapp show --name $LogicAppName --resource-group $ResourceGroup 2>$null");
            script.AppendLine("if (-not $logicAppExists) {");
            script.AppendLine("    Write-Host \"Creating Logic App $LogicAppName...\" -ForegroundColor Yellow");
            script.AppendLine("    az logicapp create --resource-group $ResourceGroup --name $LogicAppName --storage-account $StorageAccountName");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("# Deploy workflows");
            script.AppendLine("Write-Host \"Deploying workflows to Logic App...\" -ForegroundColor Yellow");
            script.AppendLine("az logicapp deployment source config-zip --resource-group $ResourceGroup --name $LogicAppName --src $packagePath");
            script.AppendLine();
            script.AppendLine("Write-Host \"=== Deployment Complete ===\" -ForegroundColor Green");
            script.AppendLine("Write-Host \"Logic App URL: https://portal.azure.com/#resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$LogicAppName\" -ForegroundColor Cyan");

            return script.ToString();
        }

        static string GenerateAzureDevOpsPipeline(string workflowName)
        {
            var yaml = new System.Text.StringBuilder();
            yaml.AppendLine("# Azure DevOps Pipeline for Logic Apps Standard Deployment");
            yaml.AppendLine("# Generated by BizTalk to Logic Apps Migration Tool");
            yaml.AppendLine();
            yaml.AppendLine("trigger:");
            yaml.AppendLine("  branches:");
            yaml.AppendLine("    include:");
            yaml.AppendLine("      - main");
            yaml.AppendLine();
            yaml.AppendLine("variables:");
            yaml.AppendLine("  azureSubscription: 'Azure-Service-Connection'");
            yaml.AppendLine("  resourceGroup: 'rg-logicapps-prod'");
            yaml.AppendLine("  logicAppName: 'la-" + workflowName.ToLowerInvariant() + "'");
            yaml.AppendLine("  storageAccountName: 'stlogicapps$(environment)'");
            yaml.AppendLine("  location: 'East US'");
            yaml.AppendLine();
            yaml.AppendLine("stages:");
            yaml.AppendLine("  - stage: Build");
            yaml.AppendLine("    displayName: 'Build Logic Apps Package'");
            yaml.AppendLine("    jobs:");
            yaml.AppendLine("      - job: BuildPackage");
            yaml.AppendLine("        displayName: 'Build Logic Apps Package'");
            yaml.AppendLine("        pool:");
            yaml.AppendLine("          vmImage: 'ubuntu-latest'");
            yaml.AppendLine("        steps:");
            yaml.AppendLine("          - task: CopyFiles@2");
            yaml.AppendLine("            displayName: 'Copy workflow files'");
            yaml.AppendLine("            inputs:");
            yaml.AppendLine("              SourceFolder: '$(System.DefaultWorkingDirectory)'");
            yaml.AppendLine("              Contents: |");
            yaml.AppendLine("                **/*.json");
            yaml.AppendLine("                host.json");
            yaml.AppendLine("                connections.json");
            yaml.AppendLine("              TargetFolder: '$(Build.ArtifactStagingDirectory)'");
            yaml.AppendLine();
            yaml.AppendLine("          - task: ArchiveFiles@2");
            yaml.AppendLine("            displayName: 'Create deployment package'");
            yaml.AppendLine("            inputs:");
            yaml.AppendLine("              rootFolderOrFile: '$(Build.ArtifactStagingDirectory)'");
            yaml.AppendLine("              includeRootFolder: false");
            yaml.AppendLine("              archiveType: 'zip'");
            yaml.AppendLine("              archiveFile: '$(Build.ArtifactStagingDirectory)/$(Build.BuildId).zip'");
            yaml.AppendLine();
            yaml.AppendLine("          - task: PublishBuildArtifacts@1");
            yaml.AppendLine("            displayName: 'Publish artifacts'");
            yaml.AppendLine("            inputs:");
            yaml.AppendLine("              PathtoPublish: '$(Build.ArtifactStagingDirectory)/$(Build.BuildId).zip'");
            yaml.AppendLine("              ArtifactName: 'drop'");
            yaml.AppendLine();
            yaml.AppendLine("  - stage: Deploy");
            yaml.AppendLine("    displayName: 'Deploy to Azure'");
            yaml.AppendLine("    dependsOn: Build");
            yaml.AppendLine("    jobs:");
            yaml.AppendLine("      - deployment: DeployLogicApp");
            yaml.AppendLine("        displayName: 'Deploy Logic App'");
            yaml.AppendLine("        environment: 'production'");
            yaml.AppendLine("        pool:");
            yaml.AppendLine("          vmImage: 'ubuntu-latest'");
            yaml.AppendLine("        strategy:");
            yaml.AppendLine("          runOnce:");
            yaml.AppendLine("            deploy:");
            yaml.AppendLine("              steps:");
            yaml.AppendLine("                - task: AzureCLI@2");
            yaml.AppendLine("                  displayName: 'Deploy Logic Apps'");
            yaml.AppendLine("                  inputs:");
            yaml.AppendLine("                    azureSubscription: '$(azureSubscription)'");
            yaml.AppendLine("                    scriptType: 'bash'");
            yaml.AppendLine("                    scriptLocation: 'inlineScript'");
            yaml.AppendLine("                    inlineScript: |");
            yaml.AppendLine("                      az logicapp deployment source config-zip \\");
            yaml.AppendLine("                        --resource-group $(resourceGroup) \\");
            yaml.AppendLine("                        --name $(logicAppName) \\");
            yaml.AppendLine("                        --src $(Pipeline.Workspace)/drop/$(Build.BuildId).zip");

            return yaml.ToString();
        }

        static string GeneratePackageReadme(string workflowName, string odxPath, string bindingsPath)
        {
            var readme = new System.Text.StringBuilder();
            readme.AppendLine("# Logic Apps Standard Deployment Package");
            readme.AppendLine();
            readme.AppendLine("**Workflow Name:** " + workflowName);
            readme.AppendLine();
            readme.AppendLine("**Generated From:**");
            readme.AppendLine("- Orchestration: `" + Path.GetFileName(odxPath) + "`");
            readme.AppendLine("- Bindings: `" + Path.GetFileName(bindingsPath) + "`");
            readme.AppendLine("- Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
            readme.AppendLine();
            readme.AppendLine("## Package Contents");
            readme.AppendLine();
            readme.AppendLine("- **" + workflowName + "/workflow.json** - Logic Apps workflow definition");
            readme.AppendLine("- **connections.json** - API connections configuration");
            readme.AppendLine("- **host.json** - Logic Apps Standard host configuration");
            readme.AppendLine("- **local.settings.json** - App settings template (update with your values)");
            readme.AppendLine("- **deploy.ps1** - PowerShell deployment script");
            readme.AppendLine("- **azure-pipelines.yml** - Azure DevOps CI/CD pipeline");
            readme.AppendLine();
            readme.AppendLine("## Deployment Options");
            readme.AppendLine();
            readme.AppendLine("### Option 1: Visual Studio Code (Recommended for Development)");
            readme.AppendLine();
            readme.AppendLine("1. Install the Azure Logic Apps (Standard) extension in VS Code");
            readme.AppendLine("2. Open this folder in VS Code");
            readme.AppendLine("3. Update `local.settings.json` with your connection strings");
            readme.AppendLine("4. Press F5 to run locally, or right-click on the workflow and select \"Deploy to Logic App\"");
            readme.AppendLine();
            readme.AppendLine("### Option 2: PowerShell Script (Azure CLI)");
            readme.AppendLine();
            readme.AppendLine("```powershell");
            readme.AppendLine("# Login to Azure");
            readme.AppendLine("az login");
            readme.AppendLine();
            readme.AppendLine("# Run deployment script");
            readme.AppendLine(".\\deploy.ps1 -ResourceGroup \"rg-logicapps\" -LogicAppName \"la-" + workflowName.ToLowerInvariant() + "\" -StorageAccountName \"stlogicapps\"");
            readme.AppendLine("```");
            readme.AppendLine();
            readme.AppendLine("### Option 3: Azure DevOps Pipeline (Recommended for Production)");
            readme.AppendLine();
            readme.AppendLine("1. Create an Azure DevOps project");
            readme.AppendLine("2. Create a service connection to your Azure subscription");
            readme.AppendLine("3. Update `azure-pipelines.yml` with your resource names");
            readme.AppendLine("4. Create a new pipeline using the `azure-pipelines.yml` file");
            readme.AppendLine("5. Run the pipeline to deploy");
            readme.AppendLine();
            readme.AppendLine("## Prerequisites");
            readme.AppendLine();
            readme.AppendLine("- Azure subscription");
            readme.AppendLine("- Storage Account (for Logic Apps runtime)");
            readme.AppendLine("- App Service Plan (Windows, Workflow Standard SKU WS1 or higher)");
            readme.AppendLine("- Logic App (Standard) resource");
            readme.AppendLine();
            readme.AppendLine("## Configuration");
            readme.AppendLine();
            readme.AppendLine("Before deploying, update the following:");
            readme.AppendLine();
            readme.AppendLine("1. **local.settings.json**: Update placeholder values with actual Azure settings");
            readme.AppendLine("2. **connections.json**: Add connection string parameters for ServiceProvider connections");
            readme.AppendLine("3. **deploy.ps1**: Review and adjust resource names if needed");
            readme.AppendLine("4. **azure-pipelines.yml**: Update service connection and resource names");
            readme.AppendLine();
            readme.AppendLine("## Important Notes");
            readme.AppendLine();
            readme.AppendLine("- This is an auto-generated migration from BizTalk Server orchestration");
            readme.AppendLine("- Review the workflow definition for accuracy before deploying to production");
            readme.AppendLine("- Test thoroughly in a development environment first");
            readme.AppendLine("- ServiceProvider connections require proper configuration in Azure");
            readme.AppendLine("- Some BizTalk features may not have direct Logic Apps equivalents");
            readme.AppendLine();
            readme.AppendLine("## Support");
            readme.AppendLine();
            readme.AppendLine("For issues or questions about this migration:");
            readme.AppendLine("- Review the generated workflow.json for any \"// Unhandled shape type\" comments");
            readme.AppendLine("- Consult Azure Logic Apps Standard documentation");
            readme.AppendLine("- Refer to the BizTalk to Logic Apps Migration Tool documentation");

            return readme.ToString();
        }

        static int HandleOdxAnalysisCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing directory path");
                Console.WriteLine("Usage: BizTalkToLogicApps analyze-odx <directory> [--output report.json]");
                return 1;
            }

            string directory = args[1];
            string outputPath = args.Length > 3 && args[2] == "--output" ? args[3] :
                                Path.Combine(directory, "ODX_Gap_Analysis_Report.json");

            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine("Error: Directory not found: {0}", directory);
                return 2;
            }

            try
            {
                var report = OdxAnalyzer.AnalyzeDirectory(directory);
                OdxAnalyzer.PrintReport(report);
                OdxAnalyzer.SaveReportToJson(report, outputPath);

                return report.FailedToParse > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error during ODX analysis: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("");
            Console.WriteLine("BizTalk to Logic Apps Migration Tool");
            Console.WriteLine("=====================================");
            Console.WriteLine("");
            Console.WriteLine("Usage: ");
            Console.WriteLine("  New style:  BizTalkToLogicApps <command> [options]");
            Console.WriteLine("  Legacy:     BizTalkToLogicApps <odxPath> <bindingPath> <outputPath> [schemaVersion]");
            Console.WriteLine("");
            Console.WriteLine("Commands:");
            Console.WriteLine("  migrate, convert       Convert BizTalk orchestration to Logic Apps workflow");
            Console.WriteLine("  bindings-only          Generate workflows from bindings ONLY (no orchestration)");
            Console.WriteLine("  report, analyze        Generate a migration readiness report");
            Console.WriteLine("  batch                  Process multiple orchestrations");
            Console.WriteLine("  diagnose               Run diagnostics on an orchestration");
            Console.WriteLine("  generate-package       Create deployable Logic Apps Standard package");
            Console.WriteLine("  analyze-odx            Analyze ODX files for gaps and unsupported patterns");
            Console.WriteLine("  help                   Show this help message");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("");
            Console.WriteLine("  1. Convert orchestration to Logic Apps (new style):");
            Console.WriteLine("     BizTalkToLogicApps migrate MyOrch.odx bindings.xml");
            Console.WriteLine("     BizTalkToLogicApps migrate MyOrch.odx bindings.xml output.json");
            Console.WriteLine("");
            Console.WriteLine("  2. Convert with refactored generator and optimization:");
            Console.WriteLine("     BizTalkToLogicApps migrate MyOrch.odx bindings.xml --refactor");
            Console.WriteLine("     BizTalkToLogicApps migrate MyOrch.odx bindings.xml --refactor --messaging servicebus");
            Console.WriteLine("     BizTalkToLogicApps migrate MyOrch.odx bindings.xml --refactor --target aggregator");
            Console.WriteLine("");
            Console.WriteLine("  3. Convert orchestration (legacy style - still supported):");
            Console.WriteLine("     BizTalkToLogicApps MyOrch.odx bindings.xml output.json");
            Console.WriteLine("");
            Console.WriteLine("  4. Generate migration report (HTML):");
            Console.WriteLine("     BizTalkToLogicApps report MyOrch.odx");
            Console.WriteLine("     BizTalkToLogicApps report MyOrch.odx --output report.html");
            Console.WriteLine("");
            Console.WriteLine("  5. Generate migration report (Markdown):");
            Console.WriteLine("     BizTalkToLogicApps report MyOrch.odx --format markdown");
            Console.WriteLine("     BizTalkToLogicApps report MyOrch.odx -f md -o report.md");
            Console.WriteLine("");
            Console.WriteLine("  6. Batch processing:");
            Console.WriteLine("     BizTalkToLogicApps batch report --directory C:\\Orchestrations");
            Console.WriteLine("     BizTalkToLogicApps batch convert --directory C:\\Orchestrations --bindings bindings.xml");
            Console.WriteLine("     BizTalkToLogicApps batch convert -d C:\\Orchestrations -b bindings.xml --refactor");
            Console.WriteLine("");
            Console.WriteLine("  7. Run diagnostics:");
            Console.WriteLine("     BizTalkToLogicApps diagnose MyOrch.odx");
            Console.WriteLine("");
            Console.WriteLine("  8. Generate deployable package:");
            Console.WriteLine("     BizTalkToLogicApps generate-package MyOrch.odx bindings.xml");
            Console.WriteLine("     BizTalkToLogicApps generate-package MyOrch.odx bindings.xml C:\\Output");
            Console.WriteLine("     BizTalkToLogicApps package MyOrch.odx bindings.xml --refactor --messaging ibmmq");
            Console.WriteLine("");
            Console.WriteLine("  9. Generate workflows from bindings only (no orchestration):");
            Console.WriteLine("     BizTalkToLogicApps bindings-only bindings.xml");
            Console.WriteLine("     BizTalkToLogicApps bindings-only bindings.xml C:\\Output");
            Console.WriteLine("     BizTalkToLogicApps -b bindings.xml --refactor");
            Console.WriteLine("");
            Console.WriteLine("  10. Analyze ODX files for gaps and unsupported patterns:");
            Console.WriteLine("     BizTalkToLogicApps analyze-odx C:\\BizTalkOrchestrations");
            Console.WriteLine("     BizTalkToLogicApps gap-analysis C:\\ODX --output report.json");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  --refactor         Use refactored workflow generator with pattern-based optimizations");
            Console.WriteLine("  --target <pattern> Target messaging pattern (aggregator, sequential-convoy, etc.)");
            Console.WriteLine("  --messaging <sys>  Messaging system (servicebus, ibmmq, rabbitmq, kafka, sapodata, saperp)");
            Console.WriteLine("  --format, -f       Report format: html (default) or markdown/md");
            Console.WriteLine("  --output, -o       Output file path (optional)");
            Console.WriteLine("  --directory, -d    Process all .odx files in directory (batch mode)");
            Console.WriteLine("  --files            Comma-separated list of .odx files (batch mode)");
            Console.WriteLine("  --bindings, -b     Bindings file path (required for conversion)");
            Console.WriteLine("");
            Console.WriteLine("Refactored Generator Options:");
            Console.WriteLine("  The --refactor flag enables advanced pattern-based optimization:");
            Console.WriteLine("  - Connector optimization based on deployment target");
            Console.WriteLine("  - Messaging pattern detection and simplification");
            Console.WriteLine("  - Native Logic Apps pattern usage (sessions, parallel branches)");
            Console.WriteLine("  - Cleaner, more maintainable workflow definitions");
            Console.WriteLine("");
            Console.WriteLine("  Messaging Systems:");
            Console.WriteLine("    servicebus  - Azure Service Bus (cloud only)");
            Console.WriteLine("    ibmmq       - IBM MQ (on-premises or cloud)");
            Console.WriteLine("    rabbitmq    - RabbitMQ (on-premises)");
            Console.WriteLine("    kafka       - Apache Kafka (on-premises)");
            Console.WriteLine("    sapodata    - SAP OData connector");
            Console.WriteLine("    saperp      - SAP ERP connector");
            Console.WriteLine("");
            Console.WriteLine("Batch Processing:");
            Console.WriteLine("  Use 'batch' command to process multiple orchestrations:");
            Console.WriteLine("  - batch report: Generate reports for multiple orchestrations");
            Console.WriteLine("  - batch convert: Convert multiple orchestrations to Logic Apps");
            Console.WriteLine("");
            Console.WriteLine("Migration Report:");
            Console.WriteLine("  The report analyzes your orchestration and provides:");
            Console.WriteLine("  - Complexity score and migration readiness percentage");
            Console.WriteLine("  - Statistics on shapes, ports, and messages");
            Console.WriteLine("  - Identification of potential migration issues");
            Console.WriteLine("  - Specific recommendations for successful migration");
            Console.WriteLine("  - Visual hierarchy of orchestration shapes");
            Console.WriteLine("  - Batch summary when processing multiple files");
            Console.WriteLine("");
            Console.WriteLine("Generate Package:");
            Console.WriteLine("  Creates a complete deployable Logic Apps Standard package including:");
            Console.WriteLine("  - Workflow definition JSON with proper folder structure");
            Console.WriteLine("  - connections.json for API connections");
            Console.WriteLine("  - host.json for Logic Apps Standard configuration");
            Console.WriteLine("  - local.settings.json template");
            Console.WriteLine("  - PowerShell deployment script (deploy.ps1)");
            Console.WriteLine("  - Azure DevOps pipeline (azure-pipelines.yml)");
            Console.WriteLine("  - README.md with deployment instructions");
            Console.WriteLine("");
            Console.WriteLine("Bindings-Only Mode:");
            Console.WriteLine("  Generate Logic Apps workflows from BizTalk bindings WITHOUT orchestration files:");
            Console.WriteLine("  - Creates one workflow per receive location");
            Console.WriteLine("  - Uses send port filters (BTS.ReceivePortName) to match send ports to workflows");
            Console.WriteLine("  - Preserves transport metadata (WCF, HostApps, etc.)");
            Console.WriteLine("  - Ideal for customers who only have bindings exports available");
            Console.WriteLine("  - Generates connections.json, host.json, and local.settings.json");
            Console.WriteLine("");
        }

        static void ShowBatchHelp()
        {
            Console.WriteLine("");
            Console.WriteLine("Batch Processing Commands");
            Console.WriteLine("=========================");
            Console.WriteLine("");
            Console.WriteLine("Usage: BizTalkToLogicApps batch <command> [options]");
            Console.WriteLine("");
            Console.WriteLine("Commands:");
            Console.WriteLine("  report    Generate reports for multiple orchestrations");
            Console.WriteLine("  convert   Convert multiple orchestrations to Logic Apps");
            Console.WriteLine("");
            Console.WriteLine("Report Options:");
            Console.WriteLine("  --directory <path>   Process all .odx files in directory");
            Console.WriteLine("  --files <paths>      Comma-separated list of .odx files");
            Console.WriteLine("  --bindings <path>    BizTalk bindings file (optional for reports)");
            Console.WriteLine("  --output <path>      Output directory for reports");
            Console.WriteLine("  --format <type>      Report format: html (default) or markdown");
            Console.WriteLine("");
            Console.WriteLine("Convert Options:");
            Console.WriteLine("  --directory <path>   Process all .odx files in directory");
            Console.WriteLine("  --files <paths>      Comma-separated list of .odx files");
            Console.WriteLine("  --bindings <path>    BizTalk bindings file (required)");
            Console.WriteLine("  --output <path>      Output directory for Logic Apps");
            Console.WriteLine("  --schema-version     Logic Apps schema version (default: 2016-06-01)");
            Console.WriteLine("  --refactor           Use refactored workflow generator with optimizations");
            Console.WriteLine("  --target <pattern>   Target messaging pattern hint");
            Console.WriteLine("  --messaging <system> Messaging system (servicebus, ibmmq, etc.)");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("");
            Console.WriteLine("  1. Generate reports for all orchestrations in a directory:");
            Console.WriteLine("     BizTalkToLogicApps batch report --directory C:\\Orchestrations");
            Console.WriteLine("");
            Console.WriteLine("  2. Generate reports with bindings:");
            Console.WriteLine("     BizTalkToLogicApps batch report -d C:\\Orchestrations -b bindings.xml");
            Console.WriteLine("");
            Console.WriteLine("  3. Process specific files:");
            Console.WriteLine("     BizTalkToLogicApps batch report --files orch1.odx,orch2.odx,orch3.odx");
            Console.WriteLine("");
            Console.WriteLine("  4. Convert all orchestrations in directory:");
            Console.WriteLine("     BizTalkToLogicApps batch convert -d C:\\Orchestrations -b bindings.xml");
            Console.WriteLine("");
            Console.WriteLine("  5. Convert with custom output directory:");
            Console.WriteLine("     BizTalkToLogicApps batch convert -d C:\\BizTalk -b bindings.xml -o C:\\LogicApps");
            Console.WriteLine("");
        }

        /// <summary>
        /// Detects if an orchestration is callable (called by other orchestrations) by checking:
        /// 1. Common naming patterns (Reprocesamiento, *Child*, *Sub*, *Helper*)
        /// 2. Orchestration has NO activating receive shape (typical of child workflows)
        /// </summary>
        private static bool DetectIfCallable(string odxPath)
        {
            try
            {
                var orchName = Path.GetFileNameWithoutExtension(odxPath);

                // Pattern 1: Check common child workflow naming conventions
                var callablePatterns = new[] { "common", "callexternal", "inner", "child", "sub", "helper", "utility" };
                if (callablePatterns.Any(p => orchName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                // Pattern 2: Parse ODX and check if it has NO activating receive shape
                // (Child workflows typically don't have activating receives)
                var model = BizTalkOrchestrationParser.ParseOdx(odxPath);
                bool hasActivatingReceive = model.Shapes.Any(s => s is ReceiveShapeModel r && r.Activate);

                if (!hasActivatingReceive)
                {
                    // No activating receive = likely a callable child workflow
                    return true;
                }

                return false;
            }
            catch
            {
                // If detection fails, default to non-callable (safer)
                return false;
            }
        }

        private static void EnsureDirectory(string filePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning: could not ensure output directory. {0}", e.Message);
            }
        }
    }
}
    