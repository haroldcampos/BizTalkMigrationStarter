// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BizTalktoLogicApps.MCP.Models;
using BizTalktoLogicApps.BTMtoLMLMigrator;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.MCP.Server.ToolHandlers
{
    /// <summary>
    /// Tool handler for BizTalk Map (BTM) to Logic Apps Liquid Map (LML) conversion
    /// </summary>
    public class MapConversionToolHandler
    {
        public void RegisterTools(ToolRegistry registry)
        {
            RegisterConvertBtmToLml(registry);
            RegisterBatchConvertMaps(registry);
            RegisterAnalyzeBtmFile(registry);
            RegisterValidateBtmFile(registry);
        }

        private void RegisterConvertBtmToLml(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "convert_btm_to_lml",
                Description = "Converts BizTalk Map (BTM) file to Azure Logic Apps Liquid Mapping Language (LML) format",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btmFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .btm map file"),
                        ["outputLmlPath"] = ToolSchemas.StringProperty("Optional output path for generated .lml file (defaults to same directory as BTM)"),
                        ["sourceSchemaPath"] = ToolSchemas.StringProperty("Optional path to source XSD schema file for namespace extraction"),
                        ["targetSchemaPath"] = ToolSchemas.StringProperty("Optional path to target XSD schema file for namespace extraction"),
                        ["preserveFormatting"] = ToolSchemas.BoolProperty("Preserve formatting and whitespace in output", true)
                    },
                    new[] { "btmFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btmFilePath = args["btmFilePath"]?.ToString();
                    var outputLmlPath = args["outputLmlPath"]?.ToString();
                    var sourceSchemaPath = args["sourceSchemaPath"]?.ToString();
                    var targetSchemaPath = args["targetSchemaPath"]?.ToString();

                    if (string.IsNullOrEmpty(btmFilePath) || !File.Exists(btmFilePath))
                    {
                        return CreateErrorResult($"BTM file not found: {btmFilePath}");
                    }

                    // Validate source schema if provided
                    if (!string.IsNullOrEmpty(sourceSchemaPath) && !File.Exists(sourceSchemaPath))
                    {
                        return CreateErrorResult($"Source schema file not found: {sourceSchemaPath}");
                    }

                    // Validate target schema if provided
                    if (!string.IsNullOrEmpty(targetSchemaPath) && !File.Exists(targetSchemaPath))
                    {
                        return CreateErrorResult($"Target schema file not found: {targetSchemaPath}");
                    }

                    // Default output path
                    if (string.IsNullOrEmpty(outputLmlPath))
                    {
                        outputLmlPath = Path.ChangeExtension(btmFilePath, ".lml");
                    }

                    // Perform conversion
                    var migrator = new BtmMigrator();
                    var lmlContent = migrator.ConvertBtmToLml(btmFilePath, sourceSchemaPath, targetSchemaPath);

                    // Ensure output directory exists
                    var outputDir = Path.GetDirectoryName(outputLmlPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Write output file
                    File.WriteAllText(outputLmlPath, lmlContent);

                    var result = new JObject
                    {
                        ["success"] = true,
                        ["btmFilePath"] = btmFilePath,
                        ["outputLmlPath"] = outputLmlPath,
                        ["sourceSchemaUsed"] = !string.IsNullOrEmpty(sourceSchemaPath),
                        ["targetSchemaUsed"] = !string.IsNullOrEmpty(targetSchemaPath),
                        ["lmlSize"] = lmlContent.Length,
                        ["message"] = $"Successfully converted BTM to LML: {Path.GetFileName(outputLmlPath)}"
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
                    return CreateErrorResult($"BTM to LML conversion failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void RegisterBatchConvertMaps(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "batch_convert_btm_to_lml",
                Description = "Batch converts multiple BizTalk Map files to Logic Apps Liquid Mapping Language format",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["directoryPath"] = ToolSchemas.StringProperty("Directory containing .btm files"),
                        ["outputDirectory"] = ToolSchemas.StringProperty("Optional output directory for generated .lml files (defaults to same as input)"),
                        ["recursive"] = ToolSchemas.BoolProperty("Search subdirectories recursively", true),
                        ["sourceSchemaDirectory"] = ToolSchemas.StringProperty("Optional directory containing source XSD schemas"),
                        ["targetSchemaDirectory"] = ToolSchemas.StringProperty("Optional directory containing target XSD schemas")
                    },
                    new[] { "directoryPath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var directoryPath = args["directoryPath"]?.ToString();
                    var outputDirectory = args["outputDirectory"]?.ToString();
                    var recursive = args["recursive"]?.ToObject<bool>() ?? true;
                    var sourceSchemaDir = args["sourceSchemaDirectory"]?.ToString();
                    var targetSchemaDir = args["targetSchemaDirectory"]?.ToString();

                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                    {
                        return CreateErrorResult($"Directory not found: {directoryPath}");
                    }

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var btmFiles = Directory.GetFiles(directoryPath, "*.btm", searchOption);

                    if (btmFiles.Length == 0)
                    {
                        return CreateErrorResult($"No BTM files found in directory: {directoryPath}");
                    }

                    var results = new List<object>();
                    int successCount = 0;
                    int failCount = 0;

                    var migrator = new BtmMigrator();

                    foreach (var btmFilePath in btmFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileNameWithoutExtension(btmFilePath);
                            var outputPath = string.IsNullOrEmpty(outputDirectory)
                                ? Path.ChangeExtension(btmFilePath, ".lml")
                                : Path.Combine(outputDirectory, fileName + ".lml");

                            // Try to find matching schemas
                            string sourceSchemaPath = null;
                            string targetSchemaPath = null;

                            if (!string.IsNullOrEmpty(sourceSchemaDir) && Directory.Exists(sourceSchemaDir))
                            {
                                sourceSchemaPath = FindMatchingSchema(fileName, sourceSchemaDir, "Source");
                            }

                            if (!string.IsNullOrEmpty(targetSchemaDir) && Directory.Exists(targetSchemaDir))
                            {
                                targetSchemaPath = FindMatchingSchema(fileName, targetSchemaDir, "Target");
                            }

                            var lmlContent = migrator.ConvertBtmToLml(btmFilePath, sourceSchemaPath, targetSchemaPath);

                            var outputDir = Path.GetDirectoryName(outputPath);
                            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                            {
                                Directory.CreateDirectory(outputDir);
                            }

                            File.WriteAllText(outputPath, lmlContent);

                            results.Add(new 
                            { 
                                file = fileName, 
                                status = "success", 
                                outputPath,
                                usedSourceSchema = sourceSchemaPath != null,
                                usedTargetSchema = targetSchemaPath != null
                            });
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new 
                            { 
                                file = Path.GetFileName(btmFilePath), 
                                status = "failed", 
                                error = ex.Message 
                            });
                            failCount++;
                        }
                    }

                    var result = new JObject
                    {
                        ["totalFiles"] = btmFiles.Length,
                        ["successCount"] = successCount,
                        ["failCount"] = failCount,
                        ["outputDirectory"] = outputDirectory ?? directoryPath,
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
                    return CreateErrorResult($"Batch conversion failed: {ex.Message}");
                }
            });
        }

        private void RegisterAnalyzeBtmFile(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "analyze_btm_file",
                Description = "Analyzes a BizTalk Map file and provides statistics about functoids, links, and complexity",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btmFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .btm map file"),
                        ["includeDetails"] = ToolSchemas.BoolProperty("Include detailed functoid and link information", false)
                    },
                    new[] { "btmFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btmFilePath = args["btmFilePath"]?.ToString();
                    var includeDetails = args["includeDetails"]?.ToObject<bool>() ?? false;

                    if (string.IsNullOrEmpty(btmFilePath) || !File.Exists(btmFilePath))
                    {
                        return CreateErrorResult($"BTM file not found: {btmFilePath}");
                    }

                    var parser = new BtmParser();
                    var mapData = parser.Parse(btmFilePath, null, null);

                    // Count functoids by type
                    var functoidTypes = new Dictionary<string, int>();
                    foreach (var functoid in mapData.Functoids)
                    {
                        var type = functoid.FunctoidType ?? "Unknown";
                        if (!functoidTypes.ContainsKey(type))
                            functoidTypes[type] = 0;
                        functoidTypes[type]++;
                    }

                    var result = new JObject
                    {
                        ["btmFile"] = Path.GetFileName(btmFilePath),
                        ["sourceSchema"] = mapData.SourceSchema,
                        ["targetSchema"] = mapData.TargetSchema,
                        ["totalFunctoids"] = mapData.Functoids.Count,
                        ["totalLinks"] = mapData.Links.Count,
                        ["functoidTypeCount"] = functoidTypes.Count,
                        ["functoidsByType"] = JObject.FromObject(functoidTypes),
                        ["sourceNamespaces"] = JObject.FromObject(mapData.SourceNamespaces),
                        ["targetNamespaces"] = JObject.FromObject(mapData.TargetNamespaces),
                        ["complexity"] = CalculateMapComplexity(mapData)
                    };

                    if (includeDetails)
                    {
                        var functoidDetails = new JArray();
                        foreach (var functoid in mapData.Functoids)
                        {
                            functoidDetails.Add(new JObject
                            {
                                ["id"] = functoid.FunctoidId,
                                ["type"] = functoid.FunctoidType,
                                ["inputCount"] = functoid.InputParameters.Count,
                                ["hasScript"] = !string.IsNullOrEmpty(functoid.ScripterCode)
                            });
                        }
                        result["functoidDetails"] = functoidDetails;
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
                    return CreateErrorResult($"BTM analysis failed: {ex.Message}");
                }
            });
        }

        private void RegisterValidateBtmFile(ToolRegistry registry)
        {
            var tool = new Tool
            {
                Name = "validate_btm_file",
                Description = "Validates a BizTalk Map file for common issues before conversion to LML",
                InputSchema = ToolSchemas.CreateObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["btmFilePath"] = ToolSchemas.StringProperty("Path to the BizTalk .btm map file"),
                        ["sourceSchemaPath"] = ToolSchemas.StringProperty("Optional path to source XSD schema file"),
                        ["targetSchemaPath"] = ToolSchemas.StringProperty("Optional path to target XSD schema file")
                    },
                    new[] { "btmFilePath" }
                )
            };

            registry.RegisterTool(tool, args =>
            {
                try
                {
                    var btmFilePath = args["btmFilePath"]?.ToString();
                    var sourceSchemaPath = args["sourceSchemaPath"]?.ToString();
                    var targetSchemaPath = args["targetSchemaPath"]?.ToString();

                    if (string.IsNullOrEmpty(btmFilePath) || !File.Exists(btmFilePath))
                    {
                        return CreateErrorResult($"BTM file not found: {btmFilePath}");
                    }

                    var issues = new List<string>();
                    var warnings = new List<string>();

                    // Parse the BTM file
                    var parser = new BtmParser();
                    var mapData = parser.Parse(btmFilePath, sourceSchemaPath, targetSchemaPath);

                    // Validate source schema
                    if (string.IsNullOrEmpty(mapData.SourceSchema))
                    {
                        warnings.Add("Source schema not specified in BTM file");
                    }

                    // Validate target schema
                    if (string.IsNullOrEmpty(mapData.TargetSchema))
                    {
                        warnings.Add("Target schema not specified in BTM file");
                    }

                    // Check for orphaned functoids (no incoming or outgoing links)
                    foreach (var functoid in mapData.Functoids)
                    {
                        bool hasIncoming = mapData.Links.Any(l => l.LinkTo == functoid.FunctoidId);
                        bool hasOutgoing = mapData.Links.Any(l => l.LinkFrom == functoid.FunctoidId);

                        if (!hasIncoming && !hasOutgoing)
                        {
                            warnings.Add($"Orphaned functoid detected: {functoid.FunctoidId} ({functoid.FunctoidType})");
                        }
                    }

                    // Check for unknown functoid types
                    foreach (var functoid in mapData.Functoids)
                    {
                        if (functoid.FunctoidType == "Unknown")
                        {
                            warnings.Add($"Unknown functoid type: FID={functoid.FunctoidFid}, ID={functoid.FunctoidId}");
                        }
                    }

                    // Check for scripting functoids
                    var scriptingFunctoids = mapData.Functoids.Where(f => f.FunctoidType == "Scripting").ToList();
                    if (scriptingFunctoids.Any())
                    {
                        warnings.Add($"Map contains {scriptingFunctoids.Count} scripting functoid(s) - may require manual review");
                    }

                    // Validate namespace declarations
                    if (mapData.SourceNamespaces.Count == 0)
                    {
                        warnings.Add("No source namespaces found - XPath generation may be incomplete");
                    }

                    if (mapData.TargetNamespaces.Count == 0)
                    {
                        warnings.Add("No target namespaces found - XPath generation may be incomplete");
                    }

                    var result = new JObject
                    {
                        ["valid"] = issues.Count == 0,
                        ["errorCount"] = issues.Count,
                        ["warningCount"] = warnings.Count,
                        ["errors"] = JArray.FromObject(issues),
                        ["warnings"] = JArray.FromObject(warnings),
                        ["canConvert"] = issues.Count == 0,
                        ["message"] = issues.Count == 0 
                            ? "BTM file is valid and ready for conversion" 
                            : "BTM file has errors that must be resolved before conversion"
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

        private string FindMatchingSchema(string mapFileName, string schemaDirectory, string schemaType)
        {
            // Try to find a matching schema file based on map name
            var possibleNames = new[]
            {
                $"{mapFileName}_{schemaType}.xsd",
                $"{mapFileName}{schemaType}.xsd",
                $"{schemaType}.xsd"
            };

            foreach (var name in possibleNames)
            {
                var path = Path.Combine(schemaDirectory, name);
                if (File.Exists(path))
                    return path;
            }

            // If no match found, return first XSD file in directory
            var xsdFiles = Directory.GetFiles(schemaDirectory, "*.xsd");
            return xsdFiles.Length > 0 ? xsdFiles[0] : null;
        }

        private string CalculateMapComplexity(BtmMapData mapData)
        {
            // Simple complexity calculation based on functoid and link counts
            var totalElements = mapData.Functoids.Count + mapData.Links.Count;

            if (totalElements < 10)
                return "Low";
            else if (totalElements < 50)
                return "Medium";
            else if (totalElements < 100)
                return "High";
            else
                return "Very High";
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
