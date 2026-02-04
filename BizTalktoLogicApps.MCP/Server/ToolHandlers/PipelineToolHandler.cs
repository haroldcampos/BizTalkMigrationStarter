using BizTalktoLogicApps.BTPtoLA.Services;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    public class PipelineToolHandler
    {
        public void RegisterTools(ToolRegistry registry)
        {
            RegisterAnalyzePipeline(registry);
            RegisterConvertPipelineToWorkflow(registry);
            RegisterBatchConvertPipelines(registry);
            RegisterValidatePipeline(registry);
        }

        private void RegisterAnalyzePipeline(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "analyze_biztalk_pipeline",
                Description = "Analyzes BizTalk pipeline files (.btp) and reports structure, stages, components, and default pipeline pattern detection",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btpFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .btp pipeline file"),
                        ["includeMetadata"] = ToolSchemas.BoolProperty("Include detailed stage and component metadata", true),
                        ["detectPattern"] = ToolSchemas.BoolProperty("Detect if pipeline matches default patterns (PassThru, XMLReceive, etc.)", true)
                    },
                    new[] { "btpFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btpPath = args["btpFilePath"]?.ToString();
                    var includeMetadata = args["includeMetadata"]?.ToObject<bool>() ?? true;
                    var detectPattern = args["detectPattern"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(btpPath) || !File.Exists(btpPath))
                    {
                        return CreateErrorResult($"BTP file not found: {btpPath}");
                    }

                    var parser = new PipelineParser();
                    var pipeline = parser.ParsePipelineFile(btpPath);

                    var result = new JObject
                    {
                        ["pipelineFile"] = Path.GetFileName(btpPath),
                        ["pipelineType"] = pipeline.GetPipelineType(),
                        ["policyFile"] = pipeline.PolicyFilePath,
                        ["version"] = $"{pipeline.MajorVersion}.{pipeline.MinorVersion}",
                        ["stageCount"] = pipeline.Stages.Count,
                        ["totalComponents"] = pipeline.Stages.Sum(s => s.Components.Count)
                    };

                    if (!string.IsNullOrEmpty(pipeline.Description))
                    {
                        result["description"] = pipeline.Description;
                    }

                    if (detectPattern)
                    {
                        var patternInfo = DefaultPipelineInfo.DetectDefaultPipeline(pipeline);
                        result["pattern"] = new JObject
                        {
                            ["name"] = patternInfo.Name,
                            ["type"] = patternInfo.Type.ToString(),
                            ["assembly"] = patternInfo.Assembly,
                            ["description"] = patternInfo.Description,
                            ["useCases"] = patternInfo.UseCases,
                            ["limitations"] = patternInfo.Limitations ?? ""
                        };

                        if (!string.IsNullOrEmpty(patternInfo.TemplateFile))
                        {
                            result["pattern"]["templateFile"] = patternInfo.TemplateFile;
                        }
                    }

                    if (includeMetadata)
                    {
                        var stages = new JArray();
                        foreach (var stage in pipeline.Stages)
                        {
                            var metadata = stage.GetMetadata();
                            var category = ComponentCategory.GetCategory(stage.CategoryId);

                            var stageObj = new JObject
                            {
                                ["categoryId"] = stage.CategoryId,
                                ["name"] = metadata.Name,
                                ["category"] = category.Name,
                                ["purpose"] = metadata.Purpose,
                                ["executionMode"] = metadata.ExecutionMode.ToString(),
                                ["behavior"] = metadata.Behavior,
                                ["componentCount"] = stage.Components.Count,
                                ["minOccurs"] = metadata.MinOccurs,
                                ["maxOccurs"] = metadata.MaxOccurs
                            };

                            var components = new JArray();
                            foreach (var component in stage.Components)
                            {
                                var compMetadata = component.GetMetadata();
                                var compObj = new JObject
                                {
                                    ["name"] = component.ComponentName,
                                    ["type"] = component.Name,
                                    ["componentType"] = compMetadata.Type.ToString(),
                                    ["messageFlow"] = compMetadata.MessageFlow,
                                    ["supportsProbing"] = compMetadata.SupportsProbing,
                                    ["version"] = component.Version,
                                    ["description"] = component.Description,
                                    ["behavior"] = compMetadata.Behavior,
                                    ["propertyCount"] = component.Properties.Count
                                };

                                if (component.Properties.Count > 0)
                                {
                                    var properties = new JObject();
                                    foreach (var prop in component.Properties)
                                    {
                                        properties[prop.Name] = new JObject
                                        {
                                            ["value"] = prop.Value.Text,
                                            ["type"] = prop.Value.Type
                                        };
                                    }
                                    compObj["properties"] = properties;
                                }

                                components.Add(compObj);
                            }

                            stageObj["components"] = components;
                            stages.Add(stageObj);
                        }

                        result["stages"] = stages;
                    }

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
                    return CreateErrorResult($"Pipeline analysis failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void RegisterConvertPipelineToWorkflow(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "convert_pipeline_to_workflow",
                Description = "Converts BizTalk pipeline (.btp) to Azure Logic Apps Standard workflow JSON with component mapping",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btpFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .btp pipeline file"),
                        ["outputPath"] = ToolSchemas.StringProperty("Output path for generated workflow.json file"),
                        ["workflowType"] = ToolSchemas.EnumProperty("Workflow type", "Stateful", "Stateless"),
                        ["workflowName"] = ToolSchemas.StringProperty("Optional custom workflow name (defaults to pipeline filename)"),
                        ["validateOutput"] = ToolSchemas.BoolProperty("Validate generated workflow structure", true)
                    },
                    new[] { "btpFilePath", "outputPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btpPath = args["btpFilePath"]?.ToString();
                    var outputPath = args["outputPath"]?.ToString();
                    var workflowType = args["workflowType"]?.ToString() ?? "Stateful";
                    var workflowName = args["workflowName"]?.ToString();
                    var validateOutput = args["validateOutput"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(btpPath) || !File.Exists(btpPath))
                    {
                        return CreateErrorResult($"BTP file not found: {btpPath}");
                    }

                    // Parse the pipeline
                    var parser = new PipelineParser();
                    var pipeline = parser.ParsePipelineFile(btpPath);

                    // Determine workflow name
                    var finalWorkflowName = !string.IsNullOrEmpty(workflowName) 
                        ? workflowName 
                        : Path.GetFileNameWithoutExtension(btpPath);

                    // Map to workflow model
                    var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(pipeline, finalWorkflowName);

                    // Generate JSON
                    var json = PipelineJSONGenerator.GenerateWorkflowJSON(workflow, workflowType);

                    // Ensure output directory exists
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Save to file
                    File.WriteAllText(outputPath, json);

                    var result = new JObject
                    {
                        ["success"] = true,
                        ["outputPath"] = outputPath,
                        ["workflowName"] = workflow.Name,
                        ["workflowType"] = workflowType,
                        ["pipelineType"] = pipeline.GetPipelineType(),
                        ["triggerCount"] = workflow.Triggers.Count,
                        ["actionCount"] = workflow.Actions.Count,
                        ["jsonSize"] = json.Length
                    };

                    if (validateOutput)
                    {
                        // Basic validation
                        var validationIssues = new List<string>();
                        
                        if (workflow.Triggers.Count == 0)
                        {
                            validationIssues.Add("Warning: No triggers defined in workflow");
                        }

                        if (workflow.Actions.Count == 0)
                        {
                            validationIssues.Add("Warning: No actions defined in workflow");
                        }

                        result["validation"] = new JObject
                        {
                            ["issueCount"] = validationIssues.Count,
                            ["issues"] = JArray.FromObject(validationIssues),
                            ["valid"] = validationIssues.Count == 0
                        };
                    }

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
                    return CreateErrorResult($"Pipeline conversion failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void RegisterBatchConvertPipelines(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "batch_convert_pipelines",
                Description = "Converts multiple BizTalk pipeline files (.btp) to Logic Apps workflows in a single operation",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["directoryPath"] = ToolSchemas.StringProperty("Directory containing .btp pipeline files"),
                        ["outputDirectory"] = ToolSchemas.StringProperty("Output directory for generated workflow files"),
                        ["workflowType"] = ToolSchemas.EnumProperty("Workflow type for all pipelines", "Stateful", "Stateless"),
                        ["recursive"] = ToolSchemas.BoolProperty("Search subdirectories recursively", true),
                        ["continueOnError"] = ToolSchemas.BoolProperty("Continue processing if a pipeline fails", true)
                    },
                    new[] { "directoryPath", "outputDirectory" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var directoryPath = args["directoryPath"]?.ToString();
                    var outputDir = args["outputDirectory"]?.ToString();
                    var workflowType = args["workflowType"]?.ToString() ?? "Stateful";
                    var recursive = args["recursive"]?.ToObject<bool>() ?? true;
                    var continueOnError = args["continueOnError"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                    {
                        return CreateErrorResult($"Directory not found: {directoryPath}");
                    }

                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var btpFiles = Directory.GetFiles(directoryPath, "*.btp", searchOption);

                    var results = new List<object>();
                    int successCount = 0;
                    int failCount = 0;

                    var parser = new PipelineParser();

                    foreach (var btpPath in btpFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileNameWithoutExtension(btpPath);
                            var outputFileName = fileName + "_workflow.json";
                            var outputPath = Path.Combine(outputDir, outputFileName);

                            // Parse
                            var pipeline = parser.ParsePipelineFile(btpPath);

                            // Map
                            var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(pipeline, fileName);

                            // Generate
                            var json = PipelineJSONGenerator.GenerateWorkflowJSON(workflow, workflowType);

                            // Save
                            File.WriteAllText(outputPath, json);

                            results.Add(new 
                            { 
                                file = Path.GetFileName(btpPath), 
                                status = "success", 
                                outputPath,
                                pipelineType = pipeline.GetPipelineType()
                            });
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new 
                            { 
                                file = Path.GetFileName(btpPath), 
                                status = "failed", 
                                error = ex.Message 
                            });
                            failCount++;

                            if (!continueOnError)
                            {
                                break;
                            }
                        }
                    }

                    var result = new JObject
                    {
                        ["totalFiles"] = btpFiles.Length,
                        ["successCount"] = successCount,
                        ["failCount"] = failCount,
                        ["outputDirectory"] = outputDir,
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
                    return CreateErrorResult($"Batch pipeline conversion failed: {ex.Message}");
                }
            });
        }

        private void RegisterValidatePipeline(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "validate_biztalk_pipeline",
                Description = "Validates BizTalk pipeline structure and components before migration to identify potential issues",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btpFilePath"] = ToolSchemas.StringProperty("Path to .btp pipeline file"),
                        ["checkComponents"] = ToolSchemas.BoolProperty("Validate component compatibility for Logic Apps", true),
                        ["checkConfiguration"] = ToolSchemas.BoolProperty("Validate component configurations", true)
                    },
                    new[] { "btpFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btpPath = args["btpFilePath"]?.ToString();
                    var checkComponents = args["checkComponents"]?.ToObject<bool>() ?? true;
                    var checkConfiguration = args["checkConfiguration"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(btpPath) || !File.Exists(btpPath))
                    {
                        return CreateErrorResult($"BTP file not found: {btpPath}");
                    }

                    var issues = new List<string>();
                    var warnings = new List<string>();

                    // Parse pipeline
                    var parser = new PipelineParser();
                    var pipeline = parser.ParsePipelineFile(btpPath);

                    // Basic validation
                    if (pipeline.Stages.Count == 0)
                    {
                        issues.Add("Pipeline has no stages defined");
                    }

                    // Check components
                    if (checkComponents)
                    {
                        var connectorRegistry = PipelineConnectorRegistry.Instance;
                        
                        foreach (var stage in pipeline.Stages)
                        {
                            foreach (var component in stage.Components)
                            {
                                var connector = connectorRegistry.GetMapping(component.Name);
                                
                                if (connector == null)
                                {
                                    warnings.Add($"Component '{component.ComponentName}' ({component.Name}) has no Logic Apps mapping defined");
                                }
                                else if (connector.CustomCodeRequired)
                                {
                                    warnings.Add($"Component '{component.ComponentName}' requires custom code: {connector.Description}");
                                }
                                else if (connector.Complexity == "High")
                                {
                                    warnings.Add($"Component '{component.ComponentName}' has high migration complexity");
                                }
                            }
                        }
                    }

                    // Check configuration
                    if (checkConfiguration)
                    {
                        foreach (var stage in pipeline.Stages)
                        {
                            var metadata = stage.GetMetadata();
                            
                            if (stage.Components.Count < metadata.MinOccurs)
                            {
                                issues.Add($"Stage '{metadata.Name}' has {stage.Components.Count} components but requires at least {metadata.MinOccurs}");
                            }
                            
                            if (metadata.MaxOccurs != -1 && stage.Components.Count > metadata.MaxOccurs)
                            {
                                issues.Add($"Stage '{metadata.Name}' has {stage.Components.Count} components but allows maximum {metadata.MaxOccurs}");
                            }
                        }
                    }

                    var result = new JObject
                    {
                        ["valid"] = issues.Count == 0,
                        ["pipelineFile"] = Path.GetFileName(btpPath),
                        ["pipelineType"] = pipeline.GetPipelineType(),
                        ["errorCount"] = issues.Count,
                        ["warningCount"] = warnings.Count,
                        ["errors"] = JArray.FromObject(issues),
                        ["warnings"] = JArray.FromObject(warnings)
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
                    return CreateErrorResult($"Pipeline validation failed: {ex.Message}");
                }
            });
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
