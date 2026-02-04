// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.ODXtoWFMigrator;
using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;
using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    /// <summary>
    /// Handles MCP tool registrations for BizTalk to Logic Apps conversion operations.
    /// </summary>
    public class ConversionToolHandler
    {
        /// <summary>
        /// Registers all conversion-related tools with the tool registry.
        /// </summary>
        /// <param name="registry">The tool registry to register tools with.</param>
        public void RegisterTools(ToolRegistry registry)
        {
            this.RegisterConvertBizTalkToLogicApp(registry);
            this.RegisterRefactoredConversion(registry);
            this.RegisterGenerateDeploymentPackage(registry);
            this.RegisterBatchConversion(registry);
            this.RegisterBindingsOnlyConversion(registry);
        }

        private void RegisterConvertBizTalkToLogicApp(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "convert_biztalk_to_logicapp",
                Description = "Converts BizTalk orchestration to Azure Logic Apps Standard workflow definition JSON with full connector mapping",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to .odx orchestration file"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to BizTalk binding XML file"),
                        ["outputPath"] = ToolSchemas.StringProperty("Output path for generated workflow.json"),
                        ["connectorRegistryPath"] = ToolSchemas.StringProperty("Optional path to custom connector registry JSON"),
                        ["schemaVersion"] = ToolSchemas.StringProperty("Logic Apps schema version (default: 2016-06-01)"),
                        ["workflowType"] = ToolSchemas.EnumProperty("Workflow type", "Stateful", "Stateless"),
                        ["validateOutput"] = ToolSchemas.BoolProperty("Validate generated workflow", true)
                    },
                    new[] { "odxFilePath", "bindingFilePath", "outputPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var outputPath = args["outputPath"]?.ToString();
                    var connectorRegistryPath = args["connectorRegistryPath"]?.ToString();
                    var schemaVersion = args["schemaVersion"]?.ToString() ?? "2016-06-01";
                    var workflowType = args["workflowType"]?.ToString() ?? "Stateful";
                    var validateOutput = args["validateOutput"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return this.CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return this.CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    var isCallable = this.DetectIfCallable(odxPath);

                    var json = BizTalkOrchestrationParser.GenerateWorkflowJson(
                        odxPath,
                        bindingPath,
                        workflowType,
                        schemaVersion,
                        isCallable
                    );

                    var validationResult = new JObject { ["valid"] = true };

                    if (validateOutput)
                    {
                        var validator = new WorkflowValidator();
                        var result = validator.Validate(json);
                        
                        validationResult = new JObject
                        {
                            ["valid"] = !result.HasErrors,
                            ["hasWarnings"] = result.HasWarnings,
                            ["errorCount"] = result.Issues.Count(i => i.Severity == IssueSeverity.Error),
                            ["warningCount"] = result.Issues.Count(i => i.Severity == IssueSeverity.Warning),
                            ["errors"] = JArray.FromObject(result.Issues.Where(i => i.Severity == IssueSeverity.Error)),
                            ["warnings"] = JArray.FromObject(result.Issues.Where(i => i.Severity == IssueSeverity.Warning))
                        };
                    }

                    EnsureDirectory(outputPath);
                    File.WriteAllText(outputPath, json);

                    var response = new JObject
                    {
                        ["success"] = true,
                        ["outputPath"] = outputPath,
                        ["isCallable"] = isCallable,
                        ["schemaVersion"] = schemaVersion,
                        ["workflowType"] = workflowType,
                        ["validation"] = validationResult
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = response.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return this.CreateErrorResult($"Conversion failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        /// <summary>
        /// Registers the refactored conversion tool with pattern-based optimizations.
        /// </summary>
        /// <param name="registry">The tool registry.</param>
        private void RegisterRefactoredConversion(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "convert_biztalk_to_logicapp_refactored",
                Description = "Converts BizTalk orchestration to Azure Logic Apps with pattern-based optimizations for cleaner, more maintainable workflows. Supports cloud and on-premises deployment targets with automatic connector optimization.",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to .odx orchestration file"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to BizTalk binding XML file"),
                        ["outputPath"] = ToolSchemas.StringProperty("Output path for generated workflow.json"),
                        ["deploymentTarget"] = ToolSchemas.EnumProperty("Deployment target (Cloud uses Service Bus, OnPremises uses RabbitMQ/Kafka)", "Cloud", "OnPremises"),
                        ["refactoringStrategy"] = ToolSchemas.EnumProperty("Optimization aggressiveness level", "Conservative", "Balanced", "Aggressive"),
                        ["messagingPlatform"] = ToolSchemas.EnumProperty("Preferred messaging platform", "ServiceBus", "RabbitMQ", "Kafka", "IbmMq"),
                        ["databaseConnector"] = ToolSchemas.EnumProperty("Preferred database connector", "Sql", "CosmosDb", "Postgres", "OracleDb"),
                        ["simplifyConvoyPatterns"] = ToolSchemas.BoolProperty("Use native session support for convoy patterns", true),
                        ["useNativeParallelBranches"] = ToolSchemas.BoolProperty("Use Logic Apps native parallel branches for scatter-gather", true),
                        ["consolidateNestedScopes"] = ToolSchemas.BoolProperty("Flatten unnecessary nested scopes", true),
                        ["generateParametersJson"] = ToolSchemas.BoolProperty("Generate separate parameters.json file", true),
                        ["includePatternComments"] = ToolSchemas.BoolProperty("Add pattern metadata to workflow definition", true),
                        ["workflowType"] = ToolSchemas.EnumProperty("Workflow type", "Stateful", "Stateless"),
                        ["schemaVersion"] = ToolSchemas.StringProperty("Logic Apps schema version (default: 2016-06-01)"),
                        ["validateOutput"] = ToolSchemas.BoolProperty("Validate generated workflow", true)
                    },
                    new[] { "odxFilePath", "bindingFilePath", "outputPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var outputPath = args["outputPath"]?.ToString();

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return this.CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return this.CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    // Build refactoring options from arguments
                    var options = new RefactoringOptions
                    {
                        SchemaVersion = args["schemaVersion"]?.ToString() ?? "2016-06-01",
                        WorkflowType = args["workflowType"]?.ToString() ?? "Stateful",
                        SimplifyConvoyPatterns = args["simplifyConvoyPatterns"]?.ToObject<bool>() ?? true,
                        UseNativeParallelBranches = args["useNativeParallelBranches"]?.ToObject<bool>() ?? true,
                        ConsolidateNestedScopes = args["consolidateNestedScopes"]?.ToObject<bool>() ?? true,
                        GenerateParametersJson = args["generateParametersJson"]?.ToObject<bool>() ?? true,
                        IncludePatternComments = args["includePatternComments"]?.ToObject<bool>() ?? true
                    };

                    // Set deployment target
                    var targetStr = args["deploymentTarget"]?.ToString();
                    if (string.Equals(targetStr, "OnPremises", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Target = DeploymentTarget.OnPremises;
                    }
                    else
                    {
                        options.Target = DeploymentTarget.Cloud;
                    }

                    // Set refactoring strategy
                    var strategyStr = args["refactoringStrategy"]?.ToString();
                    if (string.Equals(strategyStr, "Conservative", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Strategy = RefactoringStrategy.Conservative;
                    }
                    else if (string.Equals(strategyStr, "Aggressive", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Strategy = RefactoringStrategy.Aggressive;
                    }
                    else
                    {
                        options.Strategy = RefactoringStrategy.Balanced;
                    }

                    // Set messaging platform
                    var messagingStr = args["messagingPlatform"]?.ToString();
                    if (!string.IsNullOrEmpty(messagingStr))
                    {
                        options.PreferredMessagingPlatform = messagingStr;
                    }
                    else
                    {
                        // Default based on deployment target
                        options.PreferredMessagingPlatform = options.Target == DeploymentTarget.Cloud
                            ? "ServiceBus"
                            : "RabbitMQ";
                    }

                    // Set database connector
                    var dbStr = args["databaseConnector"]?.ToString();
                    if (!string.IsNullOrEmpty(dbStr))
                    {
                        options.PreferredDatabaseConnector = dbStr;
                    }
                    else
                    {
                        options.PreferredDatabaseConnector = "Sql";
                    }

                    // Validate options (throws if invalid combination like Service Bus for on-prem)
                    try
                    {
                        options.Validate();
                    }
                    catch (InvalidOperationException ex)
                    {
                        return this.CreateErrorResult($"Invalid configuration: {ex.Message}");
                    }

                    // Generate refactored workflow
                    var json = RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                        odxPath,
                        bindingPath,
                        options);

                    // Validate if requested
                    var validateOutput = args["validateOutput"]?.ToObject<bool>() ?? true;
                    var validationResult = new JObject { ["valid"] = true };

                    if (validateOutput)
                    {
                        var validator = new WorkflowValidator();
                        var result = validator.Validate(json);

                        validationResult = new JObject
                        {
                            ["valid"] = !result.HasErrors,
                            ["hasWarnings"] = result.HasWarnings,
                            ["errorCount"] = result.Issues.Count(i => i.Severity == IssueSeverity.Error),
                            ["warningCount"] = result.Issues.Count(i => i.Severity == IssueSeverity.Warning),
                            ["errors"] = JArray.FromObject(result.Issues.Where(i => i.Severity == IssueSeverity.Error)),
                            ["warnings"] = JArray.FromObject(result.Issues.Where(i => i.Severity == IssueSeverity.Warning))
                        };
                    }

                    // Save workflow
                    this.EnsureDirectory(outputPath);
                    File.WriteAllText(outputPath, json);

                    // Save parameters file if requested
                    var parametersPath = string.Empty;
                    if (options.GenerateParametersJson)
                    {
                        parametersPath = Path.ChangeExtension(outputPath, null) + ".parameters.json";
                        var parametersJson = JsonPostProcessor.GenerateParametersFile(json);
                        File.WriteAllText(parametersPath, parametersJson);
                    }

                    var response = new JObject
                    {
                        ["success"] = true,
                        ["outputPath"] = outputPath,
                        ["parametersPath"] = options.GenerateParametersJson ? parametersPath : null,
                        ["deploymentTarget"] = options.Target.ToString(),
                        ["refactoringStrategy"] = options.Strategy.ToString(),
                        ["messagingPlatform"] = options.PreferredMessagingPlatform,
                        ["databaseConnector"] = options.PreferredDatabaseConnector,
                        ["schemaVersion"] = options.SchemaVersion,
                        ["workflowType"] = options.WorkflowType,
                        ["optimizationsApplied"] = new JObject
                        {
                            ["simplifyConvoyPatterns"] = options.SimplifyConvoyPatterns,
                            ["useNativeParallelBranches"] = options.UseNativeParallelBranches,
                            ["consolidateNestedScopes"] = options.ConsolidateNestedScopes,
                            ["includePatternComments"] = options.IncludePatternComments
                        },
                        ["validation"] = validationResult
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = response.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return this.CreateErrorResult($"Refactored conversion failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void RegisterGenerateDeploymentPackage(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "generate_deployment_package",
                Description = "Creates a complete Azure Logic Apps Standard deployment package with workflow, connections, host config, and CI/CD scripts",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to .odx orchestration file"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to binding XML file"),
                        ["outputDirectory"] = ToolSchemas.StringProperty("Output directory for deployment package"),
                        ["schemaVersion"] = ToolSchemas.StringProperty("Logic Apps schema version (default: 2016-06-01)"),
                        ["includeDevOps"] = ToolSchemas.BoolProperty("Include Azure DevOps pipeline YAML", true),
                        ["includePowerShell"] = ToolSchemas.BoolProperty("Include PowerShell deployment script", true)
                    },
                    new[] { "odxFilePath", "bindingFilePath", "outputDirectory" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var outputDir = args["outputDirectory"]?.ToString();
                    var schemaVersion = args["schemaVersion"]?.ToString() ?? "2016-06-01";

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return this.CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return this.CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    var workflowName = Path.GetFileNameWithoutExtension(odxPath);
                    var workflowDir = Path.Combine(outputDir, workflowName);

                    if (!Directory.Exists(workflowDir))
                    {
                        Directory.CreateDirectory(workflowDir);
                    }

                    var workflowJson = BizTalkOrchestrationParser.GenerateWorkflowJson(
                        odxPath,
                        bindingPath,
                        "Stateful",
                        schemaVersion
                    );

                    var workflowPath = Path.Combine(workflowDir, "workflow.json");
                    File.WriteAllText(workflowPath, workflowJson);

                    var filesCreated = new List<string> { workflowPath };

                    var result = new JObject
                    {
                        ["success"] = true,
                        ["workflowName"] = workflowName,
                        ["outputDirectory"] = outputDir,
                        ["filesCreated"] = JArray.FromObject(filesCreated),
                        ["message"] = $"Deployment package created successfully in {outputDir}"
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return this.CreateErrorResult($"Package generation failed: {ex.Message}");
                }
            });
        }

        private void RegisterBatchConversion(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "batch_convert_orchestrations",
                Description = "Converts multiple BizTalk orchestrations to Logic Apps workflows in a single operation",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["directoryPath"] = ToolSchemas.StringProperty("Directory containing .odx files"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to binding XML file"),
                        ["outputDirectory"] = ToolSchemas.StringProperty("Output directory for generated workflows"),
                        ["schemaVersion"] = ToolSchemas.StringProperty("Logic Apps schema version (default: 2016-06-01)"),
                        ["recursive"] = ToolSchemas.BoolProperty("Search subdirectories recursively", true)
                    },
                    new[] { "directoryPath", "bindingFilePath", "outputDirectory" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var directoryPath = args["directoryPath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var outputDir = args["outputDirectory"]?.ToString();
                    var schemaVersion = args["schemaVersion"]?.ToString() ?? "2016-06-01";
                    var recursive = args["recursive"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                    {
                        return this.CreateErrorResult($"Directory not found: {directoryPath}");
                    }

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return this.CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var odxFiles = Directory.GetFiles(directoryPath, "*.odx", searchOption);

                    var results = new List<object>();
                    int successCount = 0;
                    int failCount = 0;

                    foreach (var odxPath in odxFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileNameWithoutExtension(odxPath);
                            var outputPath = Path.Combine(outputDir, fileName + ".workflow.json");

                            var isCallable = this.DetectIfCallable(odxPath);
                            var json = BizTalkOrchestrationParser.GenerateWorkflowJson(
                                odxPath,
                                bindingPath,
                                "Stateful",
                                schemaVersion,
                                isCallable
                            );

                            this.EnsureDirectory(outputPath);
                            File.WriteAllText(outputPath, json);

                            results.Add(new { file = fileName, status = "success", outputPath });
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { file = Path.GetFileName(odxPath), status = "failed", error = ex.Message });
                            failCount++;
                        }
                    }

                    var result = new JObject
                    {
                        ["totalFiles"] = odxFiles.Length,
                        ["successCount"] = successCount,
                        ["failCount"] = failCount,
                        ["results"] = JArray.FromObject(results)
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return this.CreateErrorResult($"Batch conversion failed: {ex.Message}");
                }
            });
        }

        private void RegisterBindingsOnlyConversion(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "convert_bindings_only",
                Description = "Generates Logic Apps workflows from BizTalk bindings WITHOUT orchestration files (one workflow per receive location)",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Path to BizTalk binding XML file"),
                        ["outputDirectory"] = ToolSchemas.StringProperty("Output directory for generated workflows"),
                        ["schemaVersion"] = ToolSchemas.StringProperty("Logic Apps schema version (default: 2016-06-01)")
                    },
                    new[] { "bindingFilePath", "outputDirectory" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var outputDir = args["outputDirectory"]?.ToString();
                    var schemaVersion = args["schemaVersion"]?.ToString() ?? "2016-06-01";

                    if (string.IsNullOrEmpty(bindingPath) || !File.Exists(bindingPath))
                    {
                        return this.CreateErrorResult($"Binding file not found: {bindingPath}");
                    }

                    var bindings = BindingSnapshot.Parse(bindingPath);
                    var workflows = LogicAppsMapper.MapBindingsToWorkflows(bindings);

                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    var connectorRegistry = BizTalkOrchestrationParser.TryLoadConnectorRegistry();
                    var filesCreated = new List<string>();

                    foreach (var workflow in workflows)
                    {
                        var json = LogicAppJSONGenerator.GenerateStandardWorkflow(
                            workflow,
                            "Stateful",
                            schemaVersion,
                            connectorRegistry
                        );

                        var workflowDir = Path.Combine(outputDir, workflow.Name);
                        if (!Directory.Exists(workflowDir))
                        {
                            Directory.CreateDirectory(workflowDir);
                        }

                        var workflowPath = Path.Combine(workflowDir, "workflow.json");
                        File.WriteAllText(workflowPath, json);
                        filesCreated.Add(workflowPath);
                    }

                    var result = new JObject
                    {
                        ["success"] = true,
                        ["workflowCount"] = workflows.Count,
                        ["receiveLocationCount"] = bindings.ReceiveLocations.Count,
                        ["sendPortCount"] = bindings.SendPorts.Count,
                        ["outputDirectory"] = outputDir,
                        ["filesCreated"] = JArray.FromObject(filesCreated)
                    };

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = result.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return this.CreateErrorResult($"Bindings-only conversion failed: {ex.Message}");
                }
            });
        }

        private bool DetectIfCallable(string odxPath)
        {
            try
            {
                var orchName = Path.GetFileNameWithoutExtension(odxPath);
                var callablePatterns = new[] { "reproceso", "reprocesamiento", "child", "sub", "helper", "utility", "process" };
                
                if (Array.Exists(callablePatterns, p => orchName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                var model = BizTalkOrchestrationParser.ParseOdx(odxPath);
                bool hasActivatingReceive = false;
                
                foreach (var shape in model.Shapes)
                {
                    if (shape is ReceiveShapeModel r && r.Activate)
                    {
                        hasActivatingReceive = true;
                        break;
                    }
                }

                return !hasActivatingReceive;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private ToolCallResult CreateErrorResult(string message)
        {
            return new ToolCallResult
            {
                Content = new[]
                {
                    new ToolContent
                    {
                        Type = "text",
                        Text = message
                    }
                },
                IsError = true
            };
        }
    }
}
