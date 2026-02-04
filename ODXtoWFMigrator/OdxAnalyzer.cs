// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Analyzes BizTalk orchestration files to identify unsupported patterns, gaps, and feature requirements for Logic Apps migration.
    /// </summary>
    /// <remarks>
    /// Provides directory-wide ODX file analysis, pattern detection, and migration readiness recommendations.
    /// Generates detailed reports on shape usage, unsupported features, and complexity metrics.
    /// </remarks>
    public class OdxAnalyzer
    {
        /// <summary>
        /// Represents the analysis result for a single ODX orchestration file.
        /// </summary>
        public class AnalysisResult
        {
            /// <summary>
            /// Gets or sets the file name.
            /// </summary>
            public string FileName { get; set; }
            
            /// <summary>
            /// Gets or sets the file size in bytes.
            /// </summary>
            public long FileSizeBytes { get; set; }
            
            /// <summary>
            /// Gets or sets whether the file was successfully parsed.
            /// </summary>
            public bool ParsedSuccessfully { get; set; }
            
            /// <summary>
            /// Gets or sets the parsing error message if parsing failed.
            /// </summary>
            public string ParseError { get; set; }
            
            /// <summary>
            /// Gets or sets the list of unique shape types found in the orchestration.
            /// </summary>
            public List<string> ShapeTypes { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the count of each shape type found in the orchestration.
            /// </summary>
            public Dictionary<string, int> ShapeTypeCounts { get; set; } = new Dictionary<string, int>();
            
            /// <summary>
            /// Gets or sets the list of unsupported shape types found.
            /// </summary>
            public List<string> UnsupportedShapes { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of partially supported shape types found.
            /// </summary>
            public List<string> PartialySupportedShapes { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of warnings generated during analysis.
            /// </summary>
            public List<string> Warnings { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets whether the orchestration uses correlation sets.
            /// </summary>
            public bool HasCorrelationSets { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses dynamic ports.
            /// </summary>
            public bool HasDynamicPorts { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses transactions (atomic or long-running).
            /// </summary>
            public bool HasTransactions { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration has exception handling.
            /// </summary>
            public bool HasExceptionHandling { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration calls the Business Rules Engine.
            /// </summary>
            public bool HasBusinessRules { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses compensation blocks.
            /// </summary>
            public bool HasCompensation { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration contains loops.
            /// </summary>
            public bool HasLoops { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses parallel shapes.
            /// </summary>
            public bool HasParallel { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses listen shapes.
            /// </summary>
            public bool HasListen { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses delay shapes.
            /// </summary>
            public bool HasDelay { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration calls other orchestrations.
            /// </summary>
            public bool HasCallOrchestration { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses transform shapes.
            /// </summary>
            public bool HasTransform { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration uses solicit-response port patterns.
            /// </summary>
            public bool HasSolicitResponse { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration implements convoy patterns (sequential or parallel).
            /// </summary>
            public bool HasConvoy { get; set; }
            
            /// <summary>
            /// Gets or sets the total number of ports in the orchestration.
            /// </summary>
            public int PortCount { get; set; }
            
            /// <summary>
            /// Gets or sets the total number of messages in the orchestration.
            /// </summary>
            public int MessageCount { get; set; }
            
            /// <summary>
            /// Gets or sets the number of correlation sets declared in the orchestration.
            /// </summary>
            public int CorrelationSetCount { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration implements the Aggregator design pattern.
            /// </summary>
            /// <remarks>
            /// Detected by: Multiple Receive shapes with correlation + message construction.
            /// See: https://learn.microsoft.com/en-us/biztalk/core/implementing-design-patterns-in-orchestrations
            /// </remarks>
            public bool HasAggregatorPattern { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration implements Content-Based Routing.
            /// </summary>
            /// <remarks>
            /// Detected by: Decide/If shapes with multiple Send branches.
            /// See: https://learn.microsoft.com/en-us/biztalk/core/implementing-design-patterns-in-orchestrations
            /// </remarks>
            public bool HasContentBasedRouting { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration implements the Scatter-Gather pattern.
            /// </summary>
            /// <remarks>
            /// Detected by: Parallel shape with multiple sends followed by receives.
            /// See: https://learn.microsoft.com/en-us/biztalk/core/implementing-design-patterns-in-orchestrations
            /// </remarks>
            public bool HasScatterGather { get; set; }
            
            /// <summary>
            /// Gets or sets whether the orchestration implements the Message Broker pattern.
            /// </summary>
            /// <remarks>
            /// Detected by: Multiple receives with routing logic (Decide + Send combinations).
            /// See: https://learn.microsoft.com/en-us/biztalk/core/implementing-design-patterns-in-orchestrations
            /// </remarks>
            public bool HasMessageBroker { get; set; }
        }

        /// <summary>
        /// Represents the aggregated gap analysis report for multiple ODX files.
        /// </summary>
        public class GapAnalysisReport
        {
            /// <summary>
            /// Gets or sets the total number of files analyzed.
            /// </summary>
            public int TotalFilesAnalyzed { get; set; }
            
            /// <summary>
            /// Gets or sets the number of files successfully parsed.
            /// </summary>
            public int SuccessfullyParsed { get; set; }
            
            /// <summary>
            /// Gets or sets the number of files that failed to parse.
            /// </summary>
            public int FailedToParse { get; set; }
            
            /// <summary>
            /// Gets or sets the frequency count of each shape type across all files.
            /// </summary>
            public Dictionary<string, int> ShapeTypeFrequency { get; set; } = new Dictionary<string, int>();
            
            /// <summary>
            /// Gets or sets the frequency count of unsupported shape types.
            /// </summary>
            public Dictionary<string, int> UnsupportedShapeFrequency { get; set; } = new Dictionary<string, int>();
            
            /// <summary>
            /// Gets or sets example file names for each unsupported shape type (max 3 examples per type).
            /// </summary>
            public Dictionary<string, List<string>> UnsupportedShapeExamples { get; set; } = new Dictionary<string, List<string>>();
            
            /// <summary>
            /// Gets or sets the list of files containing correlation sets.
            /// </summary>
            public List<string> FilesWithCorrelation { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files containing dynamic ports.
            /// </summary>
            public List<string> FilesWithDynamicPorts { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files containing transactions.
            /// </summary>
            public List<string> FilesWithTransactions { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files calling the Business Rules Engine.
            /// </summary>
            public List<string> FilesWithBusinessRules { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files containing compensation logic.
            /// </summary>
            public List<string> FilesWithCompensation { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files implementing convoy patterns.
            /// </summary>
            public List<string> FilesWithConvoy { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files implementing the Aggregator design pattern.
            /// </summary>
            public List<string> FilesWithAggregator { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files implementing Content-Based Routing.
            /// </summary>
            public List<string> FilesWithContentBasedRouting { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files implementing the Scatter-Gather pattern.
            /// </summary>
            public List<string> FilesWithScatterGather { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of files implementing the Message Broker pattern.
            /// </summary>
            public List<string> FilesWithMessageBroker { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the list of recommended features and migration guidance.
            /// </summary>
            public List<string> RecommendedFeatures { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the detailed analysis results for each file.
            /// </summary>
            public List<AnalysisResult> FileDetails { get; set; } = new List<AnalysisResult>();
        }

        /// <summary>
        /// Set of shape types that are fully or partially supported for Logic Apps migration.
        /// </summary>
        private static readonly HashSet<string> SupportedShapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Receive", "Send", "Construct", "Transform", "MessageAssignment",
            "VariableAssignment", "Expression", "Decide", "If", "Else",
            "Loop", "Parallel", "Scope", "Catch", "CatchException",
            "Throw", "Terminate", "Suspend", "Call", "Start",
            "Task", "Listen", "Delay", "While", "Until",
            "AtomicTransaction", "LongRunningTransaction", "Compensation",
            "Compensate", "CallRules", "CallPolicy", "Group"
        };

        /// <summary>
        /// Set of shape types that are partially supported and require manual review or conversion.
        /// </summary>
        /// <remarks>
        /// Includes: CallRules, CallPolicy (BRE), Compensation, Compensate, AtomicTransaction, LongRunningTransaction.
        /// These shapes require special handling or alternative Logic Apps implementations.
        /// </remarks>
        private static readonly HashSet<string> PartialySupportedShapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CallRules", "CallPolicy", "Compensation", "Compensate",
            "AtomicTransaction", "LongRunningTransaction"
        };

        /// <summary>
        /// Analyzes all ODX files in a directory and generates a comprehensive gap analysis report.
        /// </summary>
        /// <param name="odxDirectory">Directory path containing ODX files to analyze.</param>
        /// <returns>Aggregated gap analysis report with statistics, patterns, and recommendations.</returns>
        /// <remarks>
        /// Performs the following analysis:
        /// - Parses all .odx files in the directory
        /// - Counts shape types and identifies unsupported shapes
        /// - Detects enterprise integration patterns (correlation, convoy, transactions, etc.)
        /// - Generates migration recommendations based on detected features
        /// - Provides console output with colored status indicators
        /// </remarks>
        public static GapAnalysisReport AnalyzeDirectory(string odxDirectory)
        {
            Console.WriteLine($"\n=== ODX FILE GAP ANALYSIS ===");
            Console.WriteLine($"Directory: {odxDirectory}\n");

            var report = new GapAnalysisReport();
            var files = Directory.GetFiles(odxDirectory, "*.odx");
            report.TotalFilesAnalyzed = files.Length;

            Console.WriteLine($"Found {files.Length} ODX files\n");

            foreach (var filePath in files.OrderBy(f => f))
            {
                var fileName = Path.GetFileName(filePath);
                var fileSize = new FileInfo(filePath).Length;
                
                Console.Write($"Analyzing {fileName}... ");

                var result = AnalyzeOdxFile(filePath, fileName, fileSize);
                report.FileDetails.Add(result);

                if (result.ParsedSuccessfully)
                {
                    report.SuccessfullyParsed++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"? ({result.ShapeTypes.Count} shape types)");
                    Console.ResetColor();

                    // Aggregate statistics
                    foreach (var kvp in result.ShapeTypeCounts)
                    {
                        if (!report.ShapeTypeFrequency.ContainsKey(kvp.Key))
                            report.ShapeTypeFrequency[kvp.Key] = 0;
                        report.ShapeTypeFrequency[kvp.Key] += kvp.Value;
                    }

                    foreach (var unsupported in result.UnsupportedShapes)
                    {
                        if (!report.UnsupportedShapeFrequency.ContainsKey(unsupported))
                        {
                            report.UnsupportedShapeFrequency[unsupported] = 0;
                            report.UnsupportedShapeExamples[unsupported] = new List<string>();
                        }
                        report.UnsupportedShapeFrequency[unsupported]++;
                        
                        if (report.UnsupportedShapeExamples[unsupported].Count < 3)
                            report.UnsupportedShapeExamples[unsupported].Add(fileName);
                    }

                    // Track pattern files
                    if (result.HasCorrelationSets) report.FilesWithCorrelation.Add(fileName);
                    if (result.HasDynamicPorts) report.FilesWithDynamicPorts.Add(fileName);
                    if (result.HasTransactions) report.FilesWithTransactions.Add(fileName);
                    if (result.HasBusinessRules) report.FilesWithBusinessRules.Add(fileName);
                    if (result.HasCompensation) report.FilesWithCompensation.Add(fileName);
                    if (result.HasConvoy) report.FilesWithConvoy.Add(fileName);
                    if (result.HasAggregatorPattern) report.FilesWithAggregator.Add(fileName);
                    if (result.HasContentBasedRouting) report.FilesWithContentBasedRouting.Add(fileName);
                    if (result.HasScatterGather) report.FilesWithScatterGather.Add(fileName);
                    if (result.HasMessageBroker) report.FilesWithMessageBroker.Add(fileName);
                }
                else
                {
                    report.FailedToParse++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"? {result.ParseError}");
                    Console.ResetColor();
                }
            }

            // Generate recommendations
            GenerateRecommendations(report);

            return report;
        }

        /// <summary>
        /// Analyzes a single ODX file for shape types, patterns, and migration concerns.
        /// </summary>
        /// <param name="filePath">Full path to the ODX file.</param>
        /// <param name="fileName">File name for reporting.</param>
        /// <param name="fileSize">File size in bytes.</param>
        /// <returns>Analysis result containing shape counts, patterns, and support status.</returns>
        /// <remarks>
        /// Analysis includes:
        /// - Shape type enumeration and counting
        /// - Supported vs unsupported shape identification
        /// - Pattern detection (correlation, transactions, convoy, etc.)
        /// - Port and message counting
        /// - Request-response pattern detection
        /// </remarks>
        private static AnalysisResult AnalyzeOdxFile(string filePath, string fileName, long fileSize)
        {
            var result = new AnalysisResult
            {
                FileName = fileName,
                FileSizeBytes = fileSize
            };

            try
            {
                // Parse the orchestration
                var orchestration = BizTalkOrchestrationParser.ParseOdx(filePath);
                result.ParsedSuccessfully = true;

                // Helper to recursively process all shapes (including nested ones)
                void ProcessShapeRecursively(ShapeModel shape)
                {
                    var shapeType = shape.ShapeType ?? "Unknown";
                    
                    if (!result.ShapeTypes.Contains(shapeType))
                        result.ShapeTypes.Add(shapeType);

                    if (!result.ShapeTypeCounts.ContainsKey(shapeType))
                        result.ShapeTypeCounts[shapeType] = 0;
                    result.ShapeTypeCounts[shapeType]++;

                    // Check if supported
                    if (!SupportedShapes.Contains(shapeType) && shapeType != "Unknown")
                    {
                        if (!result.UnsupportedShapes.Contains(shapeType))
                            result.UnsupportedShapes.Add(shapeType);
                    }
                    else if (PartialySupportedShapes.Contains(shapeType))
                    {
                        if (!result.PartialySupportedShapes.Contains(shapeType))
                            result.PartialySupportedShapes.Add(shapeType);
                    }

                    // Detect patterns
                    DetectPatterns(shape, result);
                    
                    // Recursively process child shapes
                    if (shape.Children != null)
                    {
                        foreach (var child in shape.Children)
                        {
                            ProcessShapeRecursively(child);
                        }
                    }
                }

                // Process all shapes recursively starting from root shapes
                foreach (var rootShape in orchestration.Shapes)
                {
                    ProcessShapeRecursively(rootShape);
                }

                // Count entities
                result.PortCount = orchestration.Ports?.Count ?? 0;
                result.MessageCount = orchestration.Messages?.Count ?? 0;
                
                // Count correlation sets from CorrelationDeclarationShapeModel shapes
                var correlationShapes = orchestration.Shapes
                    .OfType<CorrelationDeclarationShapeModel>()
                    .ToList();
                result.CorrelationSetCount = correlationShapes.Count;

                if (result.CorrelationSetCount > 0)
                    result.HasCorrelationSets = true;

                // Detect convoy pattern (multiple activating receives)
                var activatingReceives = orchestration.Shapes
                    .OfType<ReceiveShapeModel>()
                    .Where(r => r.Activate)
                    .ToList();
                
                if (activatingReceives.Count > 1 || result.CorrelationSetCount > 1)
                    result.HasConvoy = true;

                // Check for request-response patterns (ReceiveSend or SendReceive ports)
                result.HasSolicitResponse = orchestration.Ports?.Any(p => 
                    p.Direction == PortDirection.SendReceive || 
                    p.Direction == PortDirection.ReceiveSend) ?? false;

                // Detect BizTalk design patterns
                DetectDesignPatterns(orchestration, result);

            }
            catch (Exception ex)
            {
                result.ParsedSuccessfully = false;
                result.ParseError = ex.Message;
            }

            return result;
        }
        
        /// <summary>
        /// Detects BizTalk design patterns in an orchestration.
        /// </summary>
        /// <param name="orchestration">The orchestration model to analyze.</param>
        /// <param name="result">The analysis result to update with detected patterns.</param>
        /// <remarks>
        /// Detects patterns documented at:
        /// https://learn.microsoft.com/en-us/biztalk/core/implementing-design-patterns-in-orchestrations
        /// 
        /// Patterns detected:
        /// - Aggregator: Multiple receives with correlation + message construction
        /// - Content-Based Routing: Decide/If shapes with multiple Send branches
        /// - Scatter-Gather: Parallel sends followed by receives (with correlation)
        /// - Message Broker: Multiple receives with routing logic (Decide + Send combinations)
        /// </remarks>
        private static void DetectDesignPatterns(OrchestrationModel orchestration, AnalysisResult result)
        {
            // Helper to recursively traverse all shapes
            var allShapes = new List<ShapeModel>();
            void CollectShapes(ShapeModel shape)
            {
                allShapes.Add(shape);
                if (shape.Children != null)
                {
                    foreach (var child in shape.Children)
                    {
                        CollectShapes(child);
                    }
                }
            }
            
            foreach (var rootShape in orchestration.Shapes)
            {
                CollectShapes(rootShape);
            }
            
            // Count shape types
            var receiveCount = allShapes.Count(s => string.Equals(s.ShapeType, "Receive", StringComparison.OrdinalIgnoreCase));
            var sendCount = allShapes.Count(s => string.Equals(s.ShapeType, "Send", StringComparison.OrdinalIgnoreCase));
            var decideCount = allShapes.Count(s => string.Equals(s.ShapeType, "Decide", StringComparison.OrdinalIgnoreCase) || 
                                                     string.Equals(s.ShapeType, "If", StringComparison.OrdinalIgnoreCase));
            var parallelCount = allShapes.Count(s => string.Equals(s.ShapeType, "Parallel", StringComparison.OrdinalIgnoreCase));
            var constructCount = allShapes.Count(s => string.Equals(s.ShapeType, "Construct", StringComparison.OrdinalIgnoreCase));
            var transformCount = allShapes.Count(s => string.Equals(s.ShapeType, "Transform", StringComparison.OrdinalIgnoreCase));
            
            // Aggregator Pattern: Multiple receives + correlation + message construction
            // Typically: Receive in loop, correlation to match messages, construct aggregated message
            if (receiveCount >= 2 && result.HasCorrelationSets && (constructCount > 0 || transformCount > 0))
            {
                result.HasAggregatorPattern = true;
            }
            
            // Content-Based Routing: Decide shapes with multiple Send branches
            // Typically: Receive -> Decide/If -> multiple Send shapes
            if (decideCount > 0 && sendCount >= 2)
            {
                result.HasContentBasedRouting = true;
            }
            
            // Scatter-Gather Pattern: Parallel sends + receives (fan-out/fan-in)
            // Typically: Parallel shape with multiple sends, followed by receives with correlation
            if (parallelCount > 0 && sendCount >= 2 && receiveCount >= 2)
            {
                result.HasScatterGather = true;
            }
            
            // Message Broker Pattern: Multiple receives + routing logic
            // Typically: Multiple receive locations, routing based on content, transformation
            if (receiveCount >= 2 && decideCount > 0 && sendCount >= 2)
            {
                result.HasMessageBroker = true;
            }
        }

        /// <summary>
        /// Detects enterprise integration patterns in a shape for migration planning.
        /// </summary>
        /// <param name="shape">The shape to analyze for patterns.</param>
        /// <param name="result">The analysis result to update with detected patterns.</param>
        /// <remarks>
        /// Detected patterns include:
        /// - Correlation sets (CorrelationDeclaration, InitializeCorrelation, FollowsCorrelation)
        /// - Dynamic ports
        /// - Transactions (Atomic, Long-running)
        /// - Exception handling (Catch, CatchException)
        /// - Business Rules Engine (CallRules, CallPolicy)
        /// - Compensation logic
        /// - Loops (Loop, ForEach, While, Until)
        /// - Parallel execution
        /// - Listen shapes
        /// - Delays
        /// - Orchestration calls (Call, Start)
        /// - Transformations
        /// </remarks>
        private static void DetectPatterns(ShapeModel shape, AnalysisResult result)
        {
            switch (shape.ShapeType?.ToLowerInvariant())
            {
                case "correlationdeclaration":
                case "initializecorrelation":
                case "followscorrelation":
                    result.HasCorrelationSets = true;
                    break;

                case "dynamicport":
                    result.HasDynamicPorts = true;
                    break;

                case "atomictransaction":
                case "longrunningtransaction":
                    result.HasTransactions = true;
                    break;

                case "catch":
                case "catchexception":
                    result.HasExceptionHandling = true;
                    break;

                case "callrules":
                case "callpolicy":
                    result.HasBusinessRules = true;
                    break;

                case "compensation":
                case "compensate":
                    result.HasCompensation = true;
                    break;

                case "loop":
                case "foreach":
                case "while":
                case "until":
                    result.HasLoops = true;
                    break;

                case "parallel":
                    result.HasParallel = true;
                    break;

                case "listen":
                    result.HasListen = true;
                    break;

                case "delay":
                    result.HasDelay = true;
                    break;

                case "call":
                case "start":
                case "startorchestration":
                    result.HasCallOrchestration = true;
                    break;

                case "transform":
                    result.HasTransform = true;
                    break;
            }
        }

        /// <summary>
        /// Generates prioritized migration recommendations based on detected patterns and unsupported shapes.
        /// </summary>
        /// <param name="report">The gap analysis report to populate with recommendations.</param>
        /// <remarks>
        /// Recommendations are prioritized as:
        /// - P0 (Critical): Business Rules Engine, Advanced Correlation
        /// - P1 (High): Convoy patterns, Dynamic ports, Compensation
        /// - P2 (Medium): Transaction scopes
        /// - P3 (Informational): Deployment options, Hybrid scenarios
        /// - P? (Unknown): Unsupported shapes requiring investigation
        /// Each recommendation includes affected file count and suggested Logic Apps alternatives.
        /// </remarks>
        private static void GenerateRecommendations(GapAnalysisReport report)
        {
            // Business Rules
            if (report.FilesWithBusinessRules.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P0 - Business Rules Engine Support: {report.FilesWithBusinessRules.Count} files use CallRules. " +
                    "Implement Logic Apps Rules Engine.");
            }

            // Correlation Sets
            if (report.FilesWithCorrelation.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P0 - Advanced Correlation Support: {report.FilesWithCorrelation.Count} files use correlation sets. " +
                    "Enhance correlation mapping to use Logic Apps state management (stateful).");
            }

            // Convoy Pattern
            if (report.FilesWithConvoy.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P1 - Convoy Pattern Support: {report.FilesWithConvoy.Count} files use convoy patterns. " +
                    "Implement sequential/parallel convoy conversion using Service Bus sessions.");
            }

            // Dynamic Ports
            if (report.FilesWithDynamicPorts.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P1 - Dynamic Port Support: {report.FilesWithDynamicPorts.Count} files use dynamic ports. " +
                    "Implement late-binding connector selection using Logic Apps dynamic content or Azure Functions.");
            }

            // Compensation
            if (report.FilesWithCompensation.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P1 - Compensation Logic Support: {report.FilesWithCompensation.Count} files use compensation. " +
                    "Implement compensating transactions using Scope error handlers.");
            }

            // Transactions
            if (report.FilesWithTransactions.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P2 - Transaction Scope Support: {report.FilesWithTransactions.Count} files use transactions. " +
                    "Document transaction boundary conversion to Logic Apps Scope with error handling.");
            }
            
            // Design Patterns
            if (report.FilesWithAggregator.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P2 - Aggregator Pattern: {report.FilesWithAggregator.Count} files implement aggregator pattern. " +
                    "Convert using stateful Logic Apps with session-enabled Service Bus queues for message correlation and aggregation.");
            }
            
            if (report.FilesWithContentBasedRouting.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P2 - Content-Based Routing: {report.FilesWithContentBasedRouting.Count} files use content-based routing. " +
                    "Implement using Switch/Condition actions with expression-based routing logic.");
            }
            
            if (report.FilesWithScatterGather.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P2 - Scatter-Gather Pattern: {report.FilesWithScatterGather.Count} files implement scatter-gather. " +
                    "Convert using parallel branches for fan-out and Compose action for fan-in aggregation.");
            }
            
            if (report.FilesWithMessageBroker.Count > 0)
            {
                report.RecommendedFeatures.Add(
                    $"P2 - Message Broker Pattern: {report.FilesWithMessageBroker.Count} files implement message broker. " +
                    "Consider using Service Bus topics with subscriptions and filters for routing, or API Management for transformation.");
            }

            // Hybrid Deployment Option (always included as informational)
            report.RecommendedFeatures.Add(
                "P3 - Hybrid Deployment Option: Consider Azure Logic Apps Hybrid deployment model (Kubernetes) " +
                "for on-premises deployment if required by industry regulations, data residency requirements, or latency constraints. " +
                "This allows running Logic Apps in your own datacenter while maintaining cloud-native features.");

            // Unsupported shapes
            foreach (var unsupported in report.UnsupportedShapeFrequency.OrderByDescending(kvp => kvp.Value))
            {
                var examples = string.Join(", ", report.UnsupportedShapeExamples[unsupported.Key]);
                report.RecommendedFeatures.Add(
                    $"P? - Support for '{unsupported.Key}' shape: Found {unsupported.Value} occurrences in {examples}");
            }
        }

        /// <summary>
        /// Prints a comprehensive gap analysis report to the console with formatted output.
        /// </summary>
        /// <param name="report">The gap analysis report to print.</param>
        /// <remarks>
        /// Report sections include:
        /// - Analysis summary (files analyzed, parse success rate)
        /// - Pattern detection summary (correlation, dynamic ports, transactions, etc.)
        /// - Top 20 shape types by frequency with support indicators
        /// - Unsupported shapes with examples
        /// - Recommended features and enhancements with priorities
        /// - Detailed file analysis grouped by complexity (simple/medium/complex)
        /// - Most complex orchestrations with unsupported shapes highlighted
        /// Output uses color coding: Green for success, Red for errors, Yellow for warnings, Cyan for recommendations.
        /// </remarks>
        public static void PrintReport(GapAnalysisReport report)
        {
            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("ANALYSIS SUMMARY");
            Console.WriteLine(new string('=', 80));
            Console.WriteLine($"Total files analyzed: {report.TotalFilesAnalyzed}");
            Console.WriteLine($"Successfully parsed: {report.SuccessfullyParsed}");
            Console.WriteLine($"Failed to parse: {report.FailedToParse}");
            Console.WriteLine($"Parse success rate: {(report.SuccessfullyParsed * 100.0 / report.TotalFilesAnalyzed):F1}%");

            Console.WriteLine("\n" + new string('-', 80));
            Console.WriteLine("PATTERN DETECTION");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"Correlation Sets: {report.FilesWithCorrelation.Count} files");
            Console.WriteLine($"Dynamic Ports: {report.FilesWithDynamicPorts.Count} files");
            Console.WriteLine($"Transactions: {report.FilesWithTransactions.Count} files");
            Console.WriteLine($"Business Rules: {report.FilesWithBusinessRules.Count} files");
            Console.WriteLine($"Compensation: {report.FilesWithCompensation.Count} files");
            Console.WriteLine($"Convoy Pattern: {report.FilesWithConvoy.Count} files");
            
            Console.WriteLine("\n" + new string('-', 80));
            Console.WriteLine("DESIGN PATTERNS (from Microsoft Documentation)");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"Aggregator: {report.FilesWithAggregator.Count} files");
            Console.WriteLine($"Content-Based Routing: {report.FilesWithContentBasedRouting.Count} files");
            Console.WriteLine($"Scatter-Gather: {report.FilesWithScatterGather.Count} files");
            Console.WriteLine($"Message Broker: {report.FilesWithMessageBroker.Count} files");

            Console.WriteLine("\n" + new string('-', 80));
            Console.WriteLine("TOP 20 SHAPE TYPES BY FREQUENCY");
            Console.WriteLine(new string('-', 80));
            var topShapes = report.ShapeTypeFrequency
                .OrderByDescending(kvp => kvp.Value)
                .Take(20);

            foreach (var shape in topShapes)
            {
                var supported = SupportedShapes.Contains(shape.Key) ? "?" : "?";
                var partial = PartialySupportedShapes.Contains(shape.Key) ? " (partial)" : "";
                Console.WriteLine($"{supported} {shape.Key,-30} {shape.Value,5} occurrences{partial}");
            }

            if (report.UnsupportedShapeFrequency.Count > 0)
            {
                Console.WriteLine("\n" + new string('-', 80));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("UNSUPPORTED SHAPES");
                Console.ResetColor();
                Console.WriteLine(new string('-', 80));

                foreach (var unsupported in report.UnsupportedShapeFrequency.OrderByDescending(kvp => kvp.Value))
                {
                    var examples = string.Join(", ", report.UnsupportedShapeExamples[unsupported.Key]);
                    Console.WriteLine($"? {unsupported.Key,-30} {unsupported.Value,5} occurrences");
                    Console.WriteLine($"  Examples: {examples}");
                }
            }

            if (report.RecommendedFeatures.Count > 0)
            {
                Console.WriteLine("\n" + new string('=', 80));
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("RECOMMENDED FEATURES & ENHANCEMENTS");
                Console.ResetColor();
                Console.WriteLine(new string('=', 80));

                foreach (var feature in report.RecommendedFeatures)
                {
                    Console.WriteLine($"\n* {feature}");
                }
            }

            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("DETAILED FILE ANALYSIS");
            Console.WriteLine(new string('=', 80));

            // Group files by complexity
            var simpleFiles = report.FileDetails.Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count < 5).ToList();
            var mediumFiles = report.FileDetails.Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count >= 5 && f.ShapeTypes.Count < 10).ToList();
            var complexFiles = report.FileDetails.Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count >= 10).ToList();

            Console.WriteLine($"\nSimple orchestrations (< 5 shape types): {simpleFiles.Count}");
            Console.WriteLine($"Medium orchestrations (5-9 shape types): {mediumFiles.Count}");
            Console.WriteLine($"Complex orchestrations (10+ shape types): {complexFiles.Count}");

            if (complexFiles.Count > 0)
            {
                Console.WriteLine("\nMost Complex Orchestrations:");
                foreach (var file in complexFiles.OrderByDescending(f => f.ShapeTypes.Count).Take(10))
                {
                    Console.WriteLine($"  * {file.FileName} ({file.ShapeTypes.Count} shape types, {file.FileSizeBytes / 1024}KB)");
                    if (file.UnsupportedShapes.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    Unsupported: {string.Join(", ", file.UnsupportedShapes)}");
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine("\n" + new string('=', 80));
        }

        /// <summary>
        /// Saves the gap analysis report to a JSON file for programmatic consumption.
        /// </summary>
        /// <param name="report">The gap analysis report to serialize.</param>
        /// <param name="outputPath">Path where the JSON file will be written.</param>
        /// <remarks>
        /// JSON output includes all analysis data:
        /// - File-level details with shape counts and patterns
        /// - Aggregated statistics and frequencies
        /// - Unsupported shape examples
        /// - Recommended features list
        /// Useful for tooling integration, dashboards, or further analysis.
        /// </remarks>
        public static void SaveReportToJson(GapAnalysisReport report, string outputPath)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"\nDetailed report saved to: {outputPath}");
        }
    }
}
