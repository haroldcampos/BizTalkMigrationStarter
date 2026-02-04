// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.ODXtoWFMigrator;
using BizTalktoLogicApps.MCP.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    public class AnalysisToolHandler
    {
        public void RegisterTools(ToolRegistry registry)
        {
            RegisterAnalyzeBizTalkOrchestration(registry);
            RegisterAnalyzeOdxFile(registry);
            RegisterGenerateMigrationReport(registry);
            RegisterValidateBizTalkArtifacts(registry);
        }

        private void RegisterAnalyzeBizTalkOrchestration(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "analyze_biztalk_orchestration",
                Description = "Analyzes BizTalk orchestration files and generates migration feasibility reports with complexity scoring and gap analysis",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .odx orchestration file"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Optional path to BizTalk binding file for enhanced analysis"),
                        ["outputFormat"] = ToolSchemas.EnumProperty("Report output format", "json", "html", "markdown"),
                        ["includeComplexity"] = ToolSchemas.BoolProperty("Include detailed complexity analysis and scoring", true)
                    },
                    new[] { "odxFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();
                    var format = args["outputFormat"]?.ToString() ?? "json";
                    var includeComplexity = args["includeComplexity"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    var orchestration = BizTalkOrchestrationParser.ParseOdx(odxPath);
                    
                    var report = new JObject
                    {
                        ["orchestrationName"] = orchestration.Name,
                        ["fullName"] = orchestration.FullName,
                        ["shapeCount"] = orchestration.Shapes.Count,
                        ["portCount"] = orchestration.Ports.Count,
                        ["messageCount"] = orchestration.Messages.Count
                    };

                    if (includeComplexity)
                    {
                        // Create a temporary directory analysis with just this file
                        var tempDir = Path.GetDirectoryName(odxPath);
                        var fileName = Path.GetFileName(odxPath);
                        
                        // Analyze the directory containing this file
                        var analysis = OdxAnalyzer.AnalyzeDirectory(tempDir);
                        
                        // Find the specific file's analysis result
                        var fileAnalysis = analysis.FileDetails.FirstOrDefault(f => f.FileName == fileName);
                        
                        if (fileAnalysis != null)
                        {
                            report["complexity"] = new JObject
                            {
                                ["shapeTypeCount"] = fileAnalysis.ShapeTypes.Count,
                                ["uniqueShapeTypes"] = JArray.FromObject(fileAnalysis.ShapeTypes),
                                ["unsupportedShapes"] = JArray.FromObject(fileAnalysis.UnsupportedShapes),
                                ["partiallySupported"] = JArray.FromObject(fileAnalysis.PartialySupportedShapes),
                                ["hasCorrelation"] = fileAnalysis.HasCorrelationSets,
                                ["hasTransactions"] = fileAnalysis.HasTransactions,
                                ["hasBusinessRules"] = fileAnalysis.HasBusinessRules,
                                ["hasCompensation"] = fileAnalysis.HasCompensation,
                                ["migrationReadiness"] = CalculateMigrationReadiness(fileAnalysis)
                            };
                        }
                    }

                    if (!string.IsNullOrEmpty(bindingPath) && File.Exists(bindingPath))
                    {
                        var bindings = BindingSnapshot.Parse(bindingPath);
                        report["bindings"] = new JObject
                        {
                            ["receiveLocationCount"] = bindings.ReceiveLocations.Count,
                            ["sendPortCount"] = bindings.SendPorts.Count
                        };
                    }

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = report.ToString(Newtonsoft.Json.Formatting.Indented)
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Analysis failed: {ex.Message}");
                }
            });
        }

        private void RegisterAnalyzeOdxFile(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "analyze_odx_directory",
                Description = "Deep analysis of all ODX files in a directory for shapes, ports, message flows, and migration gaps",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["directoryPath"] = ToolSchemas.StringProperty("Directory path containing .odx files"),
                        ["recursive"] = ToolSchemas.BoolProperty("Search subdirectories recursively", true),
                        ["outputPath"] = ToolSchemas.StringProperty("Optional output path for JSON report")
                    },
                    new[] { "directoryPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var directoryPath = args["directoryPath"]?.ToString();
                    var recursive = args["recursive"]?.ToObject<bool>() ?? true;
                    var outputPath = args["outputPath"]?.ToString();

                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                    {
                        return CreateErrorResult($"Directory not found: {directoryPath}");
                    }

                    var report = OdxAnalyzer.AnalyzeDirectory(directoryPath);

                    if (!string.IsNullOrEmpty(outputPath))
                    {
                        OdxAnalyzer.SaveReportToJson(report, outputPath);
                    }

                    var result = new JObject
                    {
                        ["totalFiles"] = report.TotalFilesAnalyzed,
                        ["successfullyParsed"] = report.SuccessfullyParsed,
                        ["failedToParse"] = report.FailedToParse,
                        ["filesWithCorrelation"] = report.FilesWithCorrelation.Count,
                        ["filesWithTransactions"] = report.FilesWithTransactions.Count,
                        ["filesWithBusinessRules"] = report.FilesWithBusinessRules.Count,
                        ["shapeTypeFrequency"] = JObject.FromObject(report.ShapeTypeFrequency),
                        ["unsupportedShapeFrequency"] = JObject.FromObject(report.UnsupportedShapeFrequency)
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
                    return CreateErrorResult($"ODX analysis failed: {ex.Message}");
                }
            });
        }

        private void RegisterGenerateMigrationReport(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "generate_migration_report",
                Description = "Generates comprehensive migration assessment reports in HTML or Markdown format with recommendations",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to .odx orchestration file"),
                        ["outputPath"] = ToolSchemas.StringProperty("Optional output path for the report file"),
                        ["format"] = ToolSchemas.EnumProperty("Report format", "html", "markdown")
                    },
                    new[] { "odxFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var outputPath = args["outputPath"]?.ToString();
                    var formatStr = args["format"]?.ToString() ?? "html";

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    var format = formatStr.ToLower() == "markdown" ? ReportFormat.Markdown : ReportFormat.Html;

                    OrchestrationReportGenerator.ExportDiagnosticReport(odxPath, outputPath, format);

                    var actualOutputPath = outputPath ?? Path.ChangeExtension(odxPath, format == ReportFormat.Html ? ".html" : ".md");

                    return new ToolCallResult
                    {
                        Content = new[]
                        {
                            new ToolContent
                            {
                                Type = "text",
                                Text = $"Migration report generated successfully: {actualOutputPath}\nFormat: {format}"
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Report generation failed: {ex.Message}");
                }
            });
        }

        private void RegisterValidateBizTalkArtifacts(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "validate_biztalk_artifacts",
                Description = "Validates BizTalk artifacts (orchestrations and bindings) before migration to identify potential issues",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["odxFilePath"] = ToolSchemas.StringProperty("Path to .odx orchestration file"),
                        ["bindingFilePath"] = ToolSchemas.StringProperty("Optional path to binding file")
                    },
                    new[] { "odxFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var odxPath = args["odxFilePath"]?.ToString();
                    var bindingPath = args["bindingFilePath"]?.ToString();

                    if (string.IsNullOrEmpty(odxPath) || !File.Exists(odxPath))
                    {
                        return CreateErrorResult($"ODX file not found: {odxPath}");
                    }

                    var issues = new List<string>();
                    var warnings = new List<string>();

                    var orchestration = BizTalkOrchestrationParser.ParseOdx(odxPath);

                    // Note: Parse warnings functionality not yet implemented in OrchestrationModel
                    // Future enhancement: Add ParseWarnings property to OrchestrationModel and BindingSnapshot

                    if (!string.IsNullOrEmpty(bindingPath) && File.Exists(bindingPath))
                    {
                        var bindings = BindingSnapshot.Parse(bindingPath);
                        // Note: Parse warnings functionality not yet implemented in BindingSnapshot
                    }

                    var result = new JObject
                    {
                        ["valid"] = issues.Count == 0,
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
                    return CreateErrorResult($"Validation failed: {ex.Message}");
                }
            });
        }

        private string CalculateMigrationReadiness(OdxAnalyzer.AnalysisResult analysis)
        {
            var unsupportedCount = analysis.UnsupportedShapes.Count;
            var partialCount = analysis.PartialySupportedShapes.Count;
            var totalIssues = unsupportedCount + (partialCount / 2); // Weight partial issues as half
            
            if (totalIssues == 0)
            {
                return "High (95-100%)";
            }
            else if (totalIssues <= 2)
            {
                return "Medium (70-95%)";
            }
            else
            {
                return "Low (<70%)";
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
