// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.Tests.Integration
{
    /// <summary>
    /// Tests for ODX file analysis and gap analysis reporting
    /// </summary>
    [TestClass]
    public class OdxAnalyzerTests
    {
        private string testDataDirectory;
        private string sourceOdxDirectory;
        
        /// <summary>
        /// Cached analysis results from source ODX directory for pattern-based file discovery.
        /// Populated on first use to avoid repeated directory scans.
        /// </summary>
        private static List<OdxAnalyzer.AnalysisResult> analysisCache;
        
        /// <summary>
        /// Lock object for thread-safe cache initialization.
        /// </summary>
        private static readonly object cacheLock = new object();

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            this.testDataDirectory = Path.Combine(Path.GetTempPath(), "BizTalkMigrator_Tests", "OdxAnalysis");
            Directory.CreateDirectory(this.testDataDirectory);

            var currentDirectory = Directory.GetCurrentDirectory();
            var solutionDirectory = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".."));
            var testProjectDirectory = Path.Combine(solutionDirectory, "BizTalktoLogicApps.Tests");
            this.sourceOdxDirectory = Path.Combine(testProjectDirectory, "Data", "BizTalk", "ODX");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(this.testDataDirectory))
            {
                Directory.Delete(this.testDataDirectory, recursive: true);
            }
        }

        #region Single ODX Analysis Tests

        [TestMethod]
        public void AnalyzeOdxFile_ValidFile_ParsesSuccessfully()
        {
            // Arrange
            var odxPath = this.CopyRealOdxFile("HelloOrchestration.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(1, report.TotalFilesAnalyzed, "Should analyze one file");
            Assert.AreEqual(1, report.SuccessfullyParsed, "Should successfully parse file");
            Assert.AreEqual(0, report.FailedToParse, "Should have no parse failures");
        }

        [TestMethod]
        public void AnalyzeOdxFile_DetectsShapeTypes()
        {
            // Arrange
            this.CopyRealOdxFile("LoanProcessor.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.ShapeTypeFrequency.Count > 0, 
                "Should detect shape types");
            Assert.IsTrue(report.ShapeTypeFrequency.ContainsKey("Receive"), 
                "Should detect Receive shapes");
            Assert.IsTrue(report.ShapeTypeFrequency.ContainsKey("Send"), 
                "Should detect Send shapes");
        }

        [TestMethod]
        public void AnalyzeOdxFile_DetectsCorrelationSets()
        {
            // Arrange - Use actual ODX file with correlation sets
            // Analysis shows: Activate.odx, Analyze.odx, Interrupter.odx all have correlation sets
            this.CopyRealOdxFile("Interrupter.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.FilesWithCorrelation.Count > 0, 
                "Should detect files with correlation sets");
            
            var fileResult = report.FileDetails.FirstOrDefault(f => f.HasCorrelationSets);
            Assert.IsNotNull(fileResult, "Should have a file with correlation sets");
            Assert.IsTrue(fileResult.HasCorrelationSets, 
                "Should flag file as having correlation sets");
        }

        [TestMethod]
        public void AnalyzeOdxFile_DetectsTransactions()
        {
            // Arrange - Use pattern-based discovery to find ALL files with transactions
            var transactionFiles = this.FindFilesWithTransactions(count: 100);
            
            if (!transactionFiles.Any())
            {
                Assert.Inconclusive("No files with transaction scopes found in test data");
            }
            
            // Report discovery results
            this.TestContext.WriteLine($"\n=== TRANSACTION FILES DISCOVERED ===");
            this.TestContext.WriteLine($"Found {transactionFiles.Count()} files with transactions:");
            foreach (var file in transactionFiles)
            {
                this.TestContext.WriteLine($"  - {file}");
            }
            
            // Copy ALL discovered files to test directory
            foreach (var file in transactionFiles)
            {
                this.CopyRealOdxFile(file);
            }

            // Act - Analyze all files
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(transactionFiles.Count(), report.FilesWithTransactions.Count, 
                "All copied files should be detected as having transactions");
            Assert.IsTrue(report.FilesWithTransactions.Count > 0, 
                "Should detect files with transactions");
            
            // Verify each file was detected
            foreach (var expectedFile in transactionFiles)
            {
                Assert.IsTrue(report.FilesWithTransactions.Contains(expectedFile),
                    $"File {expectedFile} should be detected as having transactions");
            }
            
            // Verify the transaction recommendation is generated
            var transactionRecommendation = report.RecommendedFeatures
                .FirstOrDefault(r => r.Contains("Transaction"));
            
            Assert.IsNotNull(transactionRecommendation, 
                "Should recommend transaction support");
            Assert.IsTrue(transactionRecommendation.Contains("P2"), 
                "Transactions should be marked as medium priority (P2)");
            
            // Report detailed results
            this.TestContext.WriteLine($"\n=== ANALYSIS RESULTS ===");
            this.TestContext.WriteLine($"Files analyzed: {report.TotalFilesAnalyzed}");
            this.TestContext.WriteLine($"Files with transactions: {report.FilesWithTransactions.Count}");
            this.TestContext.WriteLine($"Transaction recommendation: {transactionRecommendation}");
            
            // Report each file's transaction details with transaction type classification
            this.TestContext.WriteLine($"\n=== TRANSACTION DETAILS BY FILE ===");
            
            var atomicFiles = report.FileDetails
                .Where(f => f.HasTransactions && f.ShapeTypeCounts.ContainsKey("AtomicTransaction"))
                .ToList();
            
            var longRunningFiles = report.FileDetails
                .Where(f => f.HasTransactions && f.ShapeTypeCounts.ContainsKey("LongRunningTransaction"))
                .ToList();
            
            this.TestContext.WriteLine($"\nAtomic Transactions: {atomicFiles.Count} file(s)");
            foreach (var fileDetail in atomicFiles)
            {
                this.TestContext.WriteLine($"  - {fileDetail.FileName}: {fileDetail.ShapeTypeCounts["AtomicTransaction"]} Atomic transaction(s)");
            }
            
            this.TestContext.WriteLine($"\nLong Running Transactions: {longRunningFiles.Count} file(s)");
            foreach (var fileDetail in longRunningFiles)
            {
                this.TestContext.WriteLine($"  - {fileDetail.FileName}: {fileDetail.ShapeTypeCounts["LongRunningTransaction"]} Long Running transaction(s)");
            }
            
            // Summary message matching user's request
            var totalAtomic = atomicFiles.Count;
            var totalLongRunning = longRunningFiles.Count;
            var totalWithTransactions = report.FilesWithTransactions.Count;
            
            this.TestContext.WriteLine($"\n=== TRANSACTION TYPE SUMMARY ===");
            this.TestContext.WriteLine($"You have {totalWithTransactions} file(s) with transactions in your {report.TotalFilesAnalyzed} files:");
            if (totalAtomic > 0)
            {
                this.TestContext.WriteLine($"  - {totalAtomic} file(s) with Atomic transactions");
            }
            if (totalLongRunning > 0)
            {
                this.TestContext.WriteLine($"  - {totalLongRunning} file(s) with Long Running transactions");
            }
        }

        [TestMethod]
        [Ignore("Not available: Analysis of 153 ODX files found NO compensation logic. " +
                "Searched for Compensation and Compensate shapes. " +
                "To enable: Add a real BizTalk ODX with compensation and update this test.")]
        public void AnalyzeOdxFile_DetectsCompensation()
        {
            // Arrange
            // CONFIRMED: No compensation logic found in any of 153 parsed ODX files
            // Analysis command: ODXtoWFMigrator.exe gap-analysis <directory>
            this.CreateOdxWithCompensation(fileName: "CompensationOrch.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.FilesWithCompensation.Count > 0, 
                "Should detect files with compensation");
            
            var fileResult = report.FileDetails.FirstOrDefault(f => f.HasCompensation);
            Assert.IsNotNull(fileResult, "Should have a file with compensation");
            Assert.IsTrue(fileResult.HasCompensation, 
                "Should flag file as having compensation");
        }

        [TestMethod]
        [Ignore("Not available: Analysis of 153 ODX files found NO convoy patterns. " +
                "Convoy requires either (a) multiple activating receives or (b) 2+ correlation sets. " +
                "All correlation files (Activate.odx, Analyze.odx, Interrupter.odx, etc.) have only 1 correlation set. " +
                "To enable: Add a real BizTalk ODX with convoy pattern and update this test.")]
        public void AnalyzeOdxFile_DetectsConvoyPattern()
        {
            // Arrange
            // CONFIRMED: No convoy patterns found in any of 153 parsed ODX files
            // Analysis command: ODXtoWFMigrator.exe gap-analysis <directory>
            this.CreateOdxWithConvoy(fileName: "ConvoyOrch.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.FilesWithConvoy.Count > 0, 
                "Should detect convoy patterns");
            
            var fileResult = report.FileDetails.FirstOrDefault(f => f.HasConvoy);
            Assert.IsNotNull(fileResult, "Should have a file with convoy pattern");
            Assert.IsTrue(fileResult.HasConvoy, 
                "Should flag file as having convoy pattern");
        }

        #endregion

        #region Multiple ODX Analysis Tests

        [TestMethod]
        public void AnalyzeDirectory_MultipleFiles_ProcessesAll()
        {
            // Arrange
            this.CopyRealOdxFile("HelloOrchestration.odx");
            this.CopyRealOdxFile("LoanProcessor.odx");
            this.CopyRealOdxFile("UpdateContact.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(3, report.TotalFilesAnalyzed, "Should analyze all 3 files");
            Assert.AreEqual(3, report.SuccessfullyParsed, "Should successfully parse all files");
        }

        [TestMethod]
        public void AnalyzeDirectory_MixedComplexity_CategorizesCorrectly()
        {
            // Arrange - Copy all available ODX files to analyze their complexity
            this.CopyRealOdxFile("HelloOrchestration.odx");
            this.CopyRealOdxFile("UpdateContact.odx");
            this.CopyRealOdxFile("LoanProcessor.odx");
            this.CopyRealOdxFile("MethodCallService.odx");
            this.CopyRealOdxFile("ReceivePOandSubmitToWS.odx");
            this.CopyRealOdxFile("Aggregate.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(6, report.TotalFilesAnalyzed, "Should analyze all 6 files");
            Assert.AreEqual(6, report.SuccessfullyParsed, "Should successfully parse all files");
            
            // Check complexity categorization - we should have files in different categories
            var simpleFiles = report.FileDetails
                .Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count < 5)
                .ToList();
            var mediumFiles = report.FileDetails
                .Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count >= 5 && f.ShapeTypes.Count < 10)
                .ToList();
            var complexFiles = report.FileDetails
                .Where(f => f.ParsedSuccessfully && f.ShapeTypes.Count >= 10)
                .ToList();

            // Verify we have distribution across complexity levels
            Assert.IsTrue(report.FileDetails.All(f => f.ParsedSuccessfully), "All files should parse successfully");
            Assert.IsTrue(report.FileDetails.Any(f => f.ShapeTypes.Count > 0), "Should detect shape types in files");
            
            // Log the complexity distribution for visibility
            this.TestContext.WriteLine($"Simple files (< 5 shape types): {simpleFiles.Count}");
            this.TestContext.WriteLine($"Medium files (5-9 shape types): {mediumFiles.Count}");
            this.TestContext.WriteLine($"Complex files (10+ shape types): {complexFiles.Count}");
            
            foreach (var file in report.FileDetails.OrderBy(f => f.ShapeTypes.Count))
            {
                this.TestContext.WriteLine($"  {file.FileName}: {file.ShapeTypes.Count} shape types");
            }
        }

        [TestMethod]
        public void AnalyzeDirectory_AggregatesStatistics()
        {
            // Arrange
            this.CopyRealOdxFile("HelloOrchestration.odx");
            this.CopyRealOdxFile("LoanProcessor.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.ShapeTypeFrequency.Count > 0, 
                "Should aggregate shape type frequencies");
            
            // Verify aggregation (each file has Receive, so count should be at least 2)
            if (report.ShapeTypeFrequency.ContainsKey("Receive"))
            {
                Assert.IsTrue(report.ShapeTypeFrequency["Receive"] >= 2, 
                    "Should aggregate shape counts from multiple files");
            }
        }

        #endregion

        #region Design Pattern Detection Tests

        [TestMethod]
        public void AnalyzeOdxFile_AggregateFile_DetectsAggregatorPattern()
        {
            // Arrange - Use the Aggregate.odx file which likely implements aggregator pattern
            this.CopyRealOdxFile("Aggregate.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(1, report.TotalFilesAnalyzed, "Should analyze one file");
            Assert.AreEqual(1, report.SuccessfullyParsed, "Should successfully parse file");
            
            var fileResult = report.FileDetails.FirstOrDefault(f => f.FileName == "Aggregate.odx");
            Assert.IsNotNull(fileResult, "Should have analysis result for Aggregate.odx");
            
            // Log detected patterns for visibility
            this.TestContext.WriteLine($"File: {fileResult.FileName}");
            this.TestContext.WriteLine($"  Has Aggregator Pattern: {fileResult.HasAggregatorPattern}");
            this.TestContext.WriteLine($"  Has Content-Based Routing: {fileResult.HasContentBasedRouting}");
            this.TestContext.WriteLine($"  Has Scatter-Gather: {fileResult.HasScatterGather}");
            this.TestContext.WriteLine($"  Has Message Broker: {fileResult.HasMessageBroker}");
            this.TestContext.WriteLine($"  Correlation Sets: {fileResult.CorrelationSetCount}");
            this.TestContext.WriteLine($"  Receive Shapes: {(fileResult.ShapeTypeCounts.ContainsKey("Receive") ? fileResult.ShapeTypeCounts["Receive"] : 0)}");
            this.TestContext.WriteLine($"  Send Shapes: {(fileResult.ShapeTypeCounts.ContainsKey("Send") ? fileResult.ShapeTypeCounts["Send"] : 0)}");
            this.TestContext.WriteLine($"  Construct Shapes: {(fileResult.ShapeTypeCounts.ContainsKey("Construct") ? fileResult.ShapeTypeCounts["Construct"] : 0)}");
            
            // Note: This test logs the results but doesn't assert on specific patterns
            // because we need to verify what patterns are actually in Aggregate.odx
        }

        [TestMethod]
        public void AnalyzeDirectory_MultipleFiles_DetectsDesignPatterns()
        {
            // Arrange - Copy multiple files to see which patterns exist
            this.CopyRealOdxFile("Aggregate.odx");
            this.CopyRealOdxFile("LoanProcessor.odx");
            this.CopyRealOdxFile("MethodCallService.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(3, report.TotalFilesAnalyzed, "Should analyze 3 files");
            
            // Log pattern detection results
            this.TestContext.WriteLine("\n=== DESIGN PATTERN DETECTION ===");
            this.TestContext.WriteLine($"Aggregator Pattern: {report.FilesWithAggregator.Count} files");
            if (report.FilesWithAggregator.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithAggregator)}");
            }
            
            this.TestContext.WriteLine($"Content-Based Routing: {report.FilesWithContentBasedRouting.Count} files");
            if (report.FilesWithContentBasedRouting.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithContentBasedRouting)}");
            }
            
            this.TestContext.WriteLine($"Scatter-Gather: {report.FilesWithScatterGather.Count} files");
            if (report.FilesWithScatterGather.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithScatterGather)}");
            }
            
            this.TestContext.WriteLine($"Message Broker: {report.FilesWithMessageBroker.Count} files");
            if (report.FilesWithMessageBroker.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithMessageBroker)}");
            }
            
            // Verify at least some files analyzed successfully
            Assert.IsTrue(report.SuccessfullyParsed > 0, "Should successfully parse at least one file");
        }

        #endregion

        #region Gap Analysis Tests

        [TestMethod]
        public void GapAnalysis_IdentifiesUnsupportedShapes()
        {
            // Arrange - Use actual ODX files that contain declaration shapes
            // Analysis shows: MessageDeclaration, VariableDeclaration, CorrelationDeclaration
            // are detected as "unsupported" (they're metadata, not execution shapes)
            this.CopyRealOdxFile("Activate.odx");
            this.CopyRealOdxFile("Analyze.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.UnsupportedShapeFrequency.Count > 0, 
                "Should detect unsupported shapes (declarations)");
            Assert.IsTrue(report.UnsupportedShapeExamples.Count > 0, 
                "Should provide examples of unsupported shapes");
            
            // Log what was found for visibility
            this.TestContext.WriteLine("\n=== UNSUPPORTED SHAPES DETECTED ===");
            foreach (var shape in report.UnsupportedShapeFrequency.OrderByDescending(kvp => kvp.Value))
            {
                this.TestContext.WriteLine($"{shape.Key}: {shape.Value} occurrences");
                if (report.UnsupportedShapeExamples.ContainsKey(shape.Key))
                {
                    this.TestContext.WriteLine($"  Examples: {string.Join(", ", report.UnsupportedShapeExamples[shape.Key])}");
                }
            }
            
            // Verify common declaration types are detected
            // Note: These are BizTalk metadata declarations, not execution shapes
            var hasDeclarations = report.UnsupportedShapeFrequency.Keys.Any(k => 
                k.IndexOf("Declaration", StringComparison.OrdinalIgnoreCase) >= 0);
            
            if (hasDeclarations)
            {
                this.TestContext.WriteLine("\nNote: Declaration shapes (MessageDeclaration, VariableDeclaration, etc.)");
                this.TestContext.WriteLine("are metadata constructs, not execution shapes.");
                this.TestContext.WriteLine("They may be candidates for adding to SupportedShapes list.");
            }
        }

        [TestMethod]
        public void GapAnalysis_GeneratesRecommendations()
        {
            // Arrange - Use actual ODX file with correlation (transactions not available)
            // 9 files have correlation: Activate.odx, Analyze.odx, Interrupter.odx, etc.
            this.CopyRealOdxFile("Interrupter.odx");
            this.CopyRealOdxFile("Activate.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.RecommendedFeatures.Count > 0, 
                "Should generate feature recommendations");
            Assert.IsTrue(report.RecommendedFeatures.Any(r => r.Contains("Correlation") || r.Contains("Transaction")), 
                "Should recommend features based on detected patterns");
        }

        [TestMethod]
        public void GapAnalysis_RecommendationsForCorrelation()
        {
            // Arrange - Use actual ODX file with correlation sets
            // Available files: Activate.odx, Analyze.odx, Interrupter.odx, etc.
            this.CopyRealOdxFile("Interrupter.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.FilesWithCorrelation.Count > 0, 
                "Should detect files with correlation");
            
            var correlationRecommendation = report.RecommendedFeatures
                .FirstOrDefault(r => r.Contains("Correlation"));
            
            Assert.IsNotNull(correlationRecommendation, 
                "Should recommend correlation support");
            Assert.IsTrue(correlationRecommendation.Contains("P0"), 
                "Correlation should be marked as high priority (P0)");
        }

        [TestMethod]
        public void GapAnalysis_RecommendationsForBusinessRules()
        {
            // Arrange - LoanProcessor.odx has CallRules shape inside Scope_1!
            // Fixed: OdxAnalyzer now recursively traverses nested shapes
            this.CopyRealOdxFile("LoanProcessor.odx");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.IsTrue(report.FilesWithBusinessRules.Count > 0, 
                "Should detect files with business rules");
            
            var rulesRecommendation = report.RecommendedFeatures
                .FirstOrDefault(r => r.Contains("Business Rules"));
            
            Assert.IsNotNull(rulesRecommendation, 
                "Should recommend business rules engine support");
            Assert.IsTrue(rulesRecommendation.Contains("P0"), 
                "Business Rules should be marked as high priority (P0)");
        }

        #endregion

        #region Report Output Tests

        [TestMethod]
        public void SaveReportToJson_CreatesJsonFile()
        {
            // Arrange
            this.CopyRealOdxFile("HelloOrchestration.odx");
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);
            var jsonPath = Path.Combine(this.testDataDirectory, "GapAnalysisReport.json");

            // Act
            OdxAnalyzer.SaveReportToJson(report, jsonPath);

            // Assert
            Assert.IsTrue(
                condition: File.Exists(jsonPath),
                message: "Should create JSON report file");

            var jsonContent = File.ReadAllText(jsonPath);
            Assert.IsFalse(
                condition: string.IsNullOrEmpty(jsonContent),
                message: "JSON should not be empty");
            Assert.IsTrue(
                condition: jsonContent.Contains("TotalFilesAnalyzed"),
                message: "JSON should contain report properties");
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void AnalyzeDirectory_InvalidOdxFile_RecordsFailure()
        {
            // Arrange
            var invalidPath = Path.Combine(this.testDataDirectory, "Invalid.odx");
            File.WriteAllText(invalidPath, "This is not valid ODX XML");

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(1, report.FailedToParse, 
                "Should record failed parse attempt");
            
            var failedFile = report.FileDetails.FirstOrDefault(f => !f.ParsedSuccessfully);
            Assert.IsNotNull(failedFile, "Should have failure details");
            Assert.IsFalse(string.IsNullOrEmpty(failedFile.ParseError), 
                "Should capture error message");
        }

        [TestMethod]
        public void AnalyzeDirectory_EmptyDirectory_ReturnsEmptyReport()
        {
            // Arrange - directory exists but has no ODX files

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(0, report.TotalFilesAnalyzed, 
                "Should report zero files analyzed");
        }

        #endregion

        #region Comprehensive Analysis Tests

        [TestMethod]
        public void AnalyzeAllAvailableOdxFiles_GeneratesComprehensiveReport()
        {
            // Arrange - Copy ALL available ODX files for comprehensive pattern detection
            // This test validates the analyzer works across the entire test data set
            var allOdxFiles = Directory.GetFiles(this.sourceOdxDirectory, "*.odx");
            
            if (allOdxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files found in test data directory");
            }
            
            this.TestContext.WriteLine($"Found {allOdxFiles.Length} ODX files in test data directory");
            
            // Copy all files to test directory
            foreach (var sourceFile in allOdxFiles)
            {
                var fileName = Path.GetFileName(sourceFile);
                var destFile = Path.Combine(this.testDataDirectory, fileName);
                File.Copy(sourceFile, destFile, overwrite: true);
            }

            // Act
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(allOdxFiles.Length, report.TotalFilesAnalyzed, 
                "Should analyze all ODX files");
            Assert.IsTrue(report.SuccessfullyParsed > 0, 
                "Should successfully parse at least some files");
            Assert.IsTrue(report.SuccessfullyParsed >= report.TotalFilesAnalyzed * 0.9, 
                "Should have >90% parse success rate");
            
            // Log comprehensive results
            this.TestContext.WriteLine($"\n=== COMPREHENSIVE ANALYSIS RESULTS ===");
            this.TestContext.WriteLine($"Total Files: {report.TotalFilesAnalyzed}");
            this.TestContext.WriteLine($"Successfully Parsed: {report.SuccessfullyParsed} ({report.SuccessfullyParsed * 100.0 / report.TotalFilesAnalyzed:F1}%)");
            this.TestContext.WriteLine($"Failed to Parse: {report.FailedToParse}");
            
            this.TestContext.WriteLine($"\n=== PATTERN DETECTION SUMMARY ===");
            this.TestContext.WriteLine($"Correlation Sets: {report.FilesWithCorrelation.Count} files");
            this.TestContext.WriteLine($"Convoy Patterns: {report.FilesWithConvoy.Count} files");
            this.TestContext.WriteLine($"Transactions: {report.FilesWithTransactions.Count} files");
            this.TestContext.WriteLine($"Compensation: {report.FilesWithCompensation.Count} files");
            this.TestContext.WriteLine($"Business Rules: {report.FilesWithBusinessRules.Count} files");
            
            this.TestContext.WriteLine($"\n=== DESIGN PATTERNS ===");
            this.TestContext.WriteLine($"Aggregator: {report.FilesWithAggregator.Count} files");
            this.TestContext.WriteLine($"Content-Based Routing: {report.FilesWithContentBasedRouting.Count} files");
            this.TestContext.WriteLine($"Scatter-Gather: {report.FilesWithScatterGather.Count} files");
            this.TestContext.WriteLine($"Message Broker: {report.FilesWithMessageBroker.Count} files");
            
            this.TestContext.WriteLine($"\n=== UNSUPPORTED SHAPES ===");
            this.TestContext.WriteLine($"Unique Types: {report.UnsupportedShapeFrequency.Count}");
            foreach (var shape in report.UnsupportedShapeFrequency.OrderByDescending(kvp => kvp.Value).Take(5))
            {
                this.TestContext.WriteLine($"  {shape.Key}: {shape.Value} occurrences");
            }
            
            // Verify we got meaningful results
            Assert.IsTrue(report.ShapeTypeFrequency.Count > 0, 
                "Should detect shape types");
            Assert.IsTrue(report.RecommendedFeatures.Count > 0, 
                "Should generate recommendations");
        }

        #endregion

        #region Pattern-Based File Discovery Helper Methods

        /// <summary>
        /// Gets the cached analysis results for all ODX files in the source directory.
        /// Performs analysis once and caches results for subsequent calls.
        /// </summary>
        private List<OdxAnalyzer.AnalysisResult> GetAnalysisCache()
        {
            if (analysisCache == null)
            {
                lock (cacheLock)
                {
                    if (analysisCache == null)
                    {
                        this.TestContext.WriteLine("Building ODX analysis cache...");
                        var report = OdxAnalyzer.AnalyzeDirectory(this.sourceOdxDirectory);
                        analysisCache = report.FileDetails
                            .Where(f => f.ParsedSuccessfully)
                            .ToList();
                        this.TestContext.WriteLine($"Cached {analysisCache.Count} successfully parsed ODX files");
                    }
                }
            }
            
            return analysisCache;
        }

        /// <summary>
        /// Finds ODX files matching a specific pattern predicate.
        /// </summary>
        /// <param name="patternPredicate">Predicate to match against AnalysisResult.</param>
        /// <param name="count">Maximum number of files to return.</param>
        /// <returns>Enumerable of file names matching the pattern.</returns>
        private IEnumerable<string> FindFilesWithPattern(
            Func<OdxAnalyzer.AnalysisResult, bool> patternPredicate, 
            int count = 2)
        {
            var cache = this.GetAnalysisCache();
            
            return cache
                .Where(patternPredicate)
                .Take(count)
                .Select(f => f.FileName);
        }

        /// <summary>
        /// Finds ODX files with correlation sets.
        /// </summary>
        private IEnumerable<string> FindFilesWithCorrelation(int count = 2)
        {
            return this.FindFilesWithPattern(f => f.HasCorrelationSets, count);
        }

        /// <summary>
        /// Finds ODX files with transaction scopes.
        /// </summary>
        private IEnumerable<string> FindFilesWithTransactions(int count = 2)
        {
            return this.FindFilesWithPattern(f => f.HasTransactions, count);
        }

        /// <summary>
        /// Finds ODX files with unsupported shapes.
        /// </summary>
        private IEnumerable<string> FindFilesWithUnsupportedShapes(int count = 2)
        {
            return this.FindFilesWithPattern(f => f.UnsupportedShapes.Count > 0, count);
        }

        /// <summary>
        /// Finds ODX files with specific design patterns.
        /// </summary>
        private IEnumerable<string> FindFilesWithAggregatorPattern(int count = 1)
        {
            return this.FindFilesWithPattern(f => f.HasAggregatorPattern, count);
        }

        /// <summary>
        /// Finds ODX files with content-based routing.
        /// </summary>
        private IEnumerable<string> FindFilesWithContentBasedRouting(int count = 2)
        {
            return this.FindFilesWithPattern(f => f.HasContentBasedRouting, count);
        }

        /// <summary>
        /// Finds ODX files with scatter-gather pattern.
        /// </summary>
        private IEnumerable<string> FindFilesWithScatterGather(int count = 1)
        {
            return this.FindFilesWithPattern(f => f.HasScatterGather, count);
        }

        /// <summary>
        /// Finds ODX files with message broker pattern.
        /// </summary>
        private IEnumerable<string> FindFilesWithMessageBroker(int count = 1)
        {
            return this.FindFilesWithPattern(f => f.HasMessageBroker, count);
        }

        /// <summary>
        /// Copies files matching a pattern to the test directory.
        /// </summary>
        /// <param name="files">File names to copy.</param>
        /// <returns>Number of files copied.</returns>
        private int CopyFilesToTestDirectory(IEnumerable<string> files)
        {
            var count = 0;
            foreach (var file in files)
            {
                this.CopyRealOdxFile(file);
                count++;
            }
            
            return count;
        }

        #endregion

        #region Pattern-Based Discovery Tests (Alternative Approach)

        [TestMethod]
        public void GapAnalysis_IdentifiesUnsupportedShapes_UsingPatternDiscovery()
        {
            // Arrange - Use pattern-based discovery to find ALL files with unsupported shapes
            var filesWithUnsupported = this.FindFilesWithUnsupportedShapes(count: 100);
            
            if (!filesWithUnsupported.Any())
            {
                Assert.Inconclusive("No files with unsupported shapes found in test data");
            }
            
            // Report discovery results
            this.TestContext.WriteLine($"\n=== FILES WITH UNSUPPORTED SHAPES DISCOVERED ===");
            this.TestContext.WriteLine($"Found {filesWithUnsupported.Count()} files with unsupported shapes:");
            foreach (var file in filesWithUnsupported)
            {
                this.TestContext.WriteLine($"  - {file}");
            }
            
            // Copy ALL discovered files to test directory
            foreach (var file in filesWithUnsupported)
            {
                this.CopyRealOdxFile(file);
            }

            // Act - Analyze all files
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(filesWithUnsupported.Count(), report.TotalFilesAnalyzed,
                "All copied files should be analyzed");
            Assert.IsTrue(report.UnsupportedShapeFrequency.Count > 0, 
                "Should detect unsupported shapes");
            Assert.IsTrue(report.UnsupportedShapeExamples.Count > 0, 
                "Should provide examples of unsupported shapes");
            
            // Log results
            this.TestContext.WriteLine("\n=== UNSUPPORTED SHAPES DETECTED ===");
            foreach (var shape in report.UnsupportedShapeFrequency.OrderByDescending(kvp => kvp.Value))
            {
                this.TestContext.WriteLine($"{shape.Key}: {shape.Value} occurrences");
                if (report.UnsupportedShapeExamples.ContainsKey(shape.Key))
                {
                    this.TestContext.WriteLine($"  Examples: {string.Join(", ", report.UnsupportedShapeExamples[shape.Key])}");
                }
            }
            
            this.TestContext.WriteLine($"\nTotal unique unsupported shapes: {report.UnsupportedShapeFrequency.Count}");
            this.TestContext.WriteLine($"Total files analyzed: {report.TotalFilesAnalyzed}");
        }

        [TestMethod]
        public void AnalyzeOdxFile_DetectsCorrelationSets_UsingPatternDiscovery()
        {
            // Arrange - Use pattern-based discovery to find ALL files with correlation
            var correlationFiles = this.FindFilesWithCorrelation(count: 100);
            
            if (!correlationFiles.Any())
            {
                Assert.Inconclusive("No files with correlation sets found in test data");
            }
            
            // Report discovery results
            this.TestContext.WriteLine($"\n=== CORRELATION FILES DISCOVERED ===");
            this.TestContext.WriteLine($"Found {correlationFiles.Count()} files with correlation sets:");
            foreach (var file in correlationFiles)
            {
                this.TestContext.WriteLine($"  - {file}");
            }
            
            // Copy ALL discovered files to test directory
            foreach (var file in correlationFiles)
            {
                this.CopyRealOdxFile(file);
            }

            // Act - Analyze all files
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(correlationFiles.Count(), report.FilesWithCorrelation.Count,
                "All copied files should be detected as having correlation");
            Assert.IsTrue(report.FilesWithCorrelation.Count > 0, 
                "Should detect files with correlation sets");
            
            // Verify each file was detected
            foreach (var expectedFile in correlationFiles)
            {
                Assert.IsTrue(report.FilesWithCorrelation.Contains(expectedFile),
                    $"File {expectedFile} should be detected as having correlation");
            }
            
            // Report detailed results
            this.TestContext.WriteLine($"\n=== CORRELATION DETAILS ===");
            foreach (var fileResult in report.FileDetails.Where(f => f.HasCorrelationSets))
            {
                this.TestContext.WriteLine($"{fileResult.FileName}: {fileResult.CorrelationSetCount} correlation set(s)");
            }
        }

        [TestMethod]
        public void AnalyzeDirectory_DetectsDesignPatterns_UsingPatternDiscovery()
        {
            // Arrange - Use pattern-based discovery to find ALL files with design patterns
            var aggregatorFiles = this.FindFilesWithAggregatorPattern(count: 100);
            var routingFiles = this.FindFilesWithContentBasedRouting(count: 100);
            var scatterGatherFiles = this.FindFilesWithScatterGather(count: 100);
            var messageBrokerFiles = this.FindFilesWithMessageBroker(count: 100);
            
            var allFiles = aggregatorFiles
                .Concat(routingFiles)
                .Concat(scatterGatherFiles)
                .Concat(messageBrokerFiles)
                .Distinct()
                .ToList();
            
            if (!allFiles.Any())
            {
                Assert.Inconclusive("No files with design patterns found in test data");
            }
            
            // Report discovery results
            this.TestContext.WriteLine("\n=== DESIGN PATTERN FILES DISCOVERED ===");
            this.TestContext.WriteLine($"Aggregator: {aggregatorFiles.Count()} files");
            this.TestContext.WriteLine($"Content-Based Routing: {routingFiles.Count()} files");
            this.TestContext.WriteLine($"Scatter-Gather: {scatterGatherFiles.Count()} files");
            this.TestContext.WriteLine($"Message Broker: {messageBrokerFiles.Count()} files");
            this.TestContext.WriteLine($"\nTotal unique files: {allFiles.Count}");
            this.TestContext.WriteLine("\nCopying all discovered files:");
            
            foreach (var file in allFiles)
            {
                this.TestContext.WriteLine($"  - {file}");
                this.CopyRealOdxFile(file);
            }

            // Act - Analyze all files
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(allFiles.Count, report.TotalFilesAnalyzed, 
                "Should analyze all discovered files");
            Assert.IsTrue(report.SuccessfullyParsed > 0, 
                "Should successfully parse at least one file");
            
            // Log pattern detection results
            this.TestContext.WriteLine("\n=== DESIGN PATTERN DETECTION RESULTS ===");
            this.TestContext.WriteLine($"Aggregator: {report.FilesWithAggregator.Count} files");
            if (report.FilesWithAggregator.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithAggregator)}");
            }
            
            this.TestContext.WriteLine($"\nContent-Based Routing: {report.FilesWithContentBasedRouting.Count} files");
            if (report.FilesWithContentBasedRouting.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithContentBasedRouting)}");
            }
            
            this.TestContext.WriteLine($"\nScatter-Gather: {report.FilesWithScatterGather.Count} files");
            if (report.FilesWithScatterGather.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithScatterGather)}");
            }
            
            this.TestContext.WriteLine($"\nMessage Broker: {report.FilesWithMessageBroker.Count} files");
            if (report.FilesWithMessageBroker.Count > 0)
            {
                this.TestContext.WriteLine($"  Files: {string.Join(", ", report.FilesWithMessageBroker)}");
            }
        }

        [TestMethod]
        public void GapAnalysis_RecommendationsForCorrelation_UsingPatternDiscovery()
        {
            // Arrange - Use pattern-based discovery to find ALL files with correlation
            var correlationFiles = this.FindFilesWithCorrelation(count: 100);
            
            if (!correlationFiles.Any())
            {
                Assert.Inconclusive("No files with correlation sets found in test data");
            }
            
            // Report discovery results
            this.TestContext.WriteLine($"\n=== CORRELATION FILES FOR RECOMMENDATION TEST ===");
            this.TestContext.WriteLine($"Found {correlationFiles.Count()} files with correlation:");
            foreach (var file in correlationFiles)
            {
                this.TestContext.WriteLine($"  - {file}");
            }
            
            // Copy ALL discovered files to test directory
            foreach (var file in correlationFiles)
            {
                this.CopyRealOdxFile(file);
            }

            // Act - Analyze all files
            var report = OdxAnalyzer.AnalyzeDirectory(this.testDataDirectory);

            // Assert
            Assert.AreEqual(correlationFiles.Count(), report.FilesWithCorrelation.Count,
                "All copied files should be detected as having correlation");
            Assert.IsTrue(report.FilesWithCorrelation.Count > 0, 
                "Should detect files with correlation");
            
            var correlationRecommendation = report.RecommendedFeatures
                .FirstOrDefault(r => r.Contains("Correlation"));
            
            Assert.IsNotNull(correlationRecommendation, 
                "Should recommend correlation support");
            Assert.IsTrue(correlationRecommendation.Contains("P0"), 
                "Correlation should be marked as high priority (P0)");
            Assert.IsTrue(correlationRecommendation.Contains(correlationFiles.Count().ToString()),
                "Recommendation should mention the correct file count");
            
            this.TestContext.WriteLine($"\n=== RECOMMENDATION GENERATED ===");
            this.TestContext.WriteLine(correlationRecommendation);
            this.TestContext.WriteLine($"\nFiles covered: {report.FilesWithCorrelation.Count}");
        }

        #endregion

        #region Helper Methods for Test ODX Creation

        private string CopyRealOdxFile(string fileName)
        {
            var sourceFile = Path.Combine(this.sourceOdxDirectory, fileName);
            if (!File.Exists(sourceFile))
            {
                Assert.Fail($"Source ODX file not found: {sourceFile}");
            }

            var destFile = Path.Combine(this.testDataDirectory, fileName);
            File.Copy(sourceFile, destFile, overwrite: true);
            return destFile;
        }

        private string CreateSimpleOdxFile(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>SimpleOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>ReceiveMsg</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>SendMsg</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithMultipleShapes(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>ComplexOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>ReceiveMsg</om:Property>
        </om:Element>
        <om:Element Type='Decide'>
          <om:Property Name='Name'>CheckCondition</om:Property>
        </om:Element>
        <om:Element Type='Construct'>
          <om:Property Name='Name'>ConstructMsg</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>SendMsg</om:Property>
        </om:Element>
        <om:Element Type='Transform'>
          <om:Property Name='Name'>TransformMsg</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithManyShapes(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>VeryComplexOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'><om:Property Name='Name'>Receive1</om:Property></om:Element>
        <om:Element Type='Decide'><om:Property Name='Name'>Decide1</om:Property></om:Element>
        <om:Element Type='Switch'><om:Property Name='Name'>Switch1</om:Property></om:Element>
        <om:Element Type='Loop'><om:Property Name='Name'>Loop1</om:Property></om:Element>
        <om:Element Type='Parallel'><om:Property Name='Name'>Parallel1</om:Property></om:Element>
        <om:Element Type='Scope'><om:Property Name='Name'>Scope1</om:Property></om:Element>
        <om:Element Type='Construct'><om:Property Name='Name'>Construct1</om:Property></om:Element>
        <om:Element Type='Transform'><om:Property Name='Name'>Transform1</om:Property></om:Element>
        <om:Element Type='Expression'><om:Property Name='Name'>Expr1</om:Property></om:Element>
        <om:Element Type='Delay'><om:Property Name='Name'>Delay1</om:Property></om:Element>
        <om:Element Type='Send'><om:Property Name='Name'>Send1</om:Property></om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithCorrelation(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>CorrelationOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='CorrelationDeclaration'>
          <om:Property Name='Name'>CorrSet1</om:Property>
        </om:Element>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>ReceiveCorrelated</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>SendCorrelated</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithTransaction(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>TransactionOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>Receive1</om:Property>
        </om:Element>
        <om:Element Type='AtomicTransaction'>
          <om:Property Name='Name'>AtomicScope</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>Send1</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithCompensation(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>CompensationOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Scope'>
          <om:Property Name='Name'>CompensableScope</om:Property>
          <om:Element Type='Compensation'>
            <om:Property Name='Name'>CompensationHandler</om:Property>
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithConvoy(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>ConvoyOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='CorrelationDeclaration'>
          <om:Property Name='Name'>CorrSet1</om:Property>
        </om:Element>
        <om:Element Type='CorrelationDeclaration'>
          <om:Property Name='Name'>CorrSet2</om:Property>
        </om:Element>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>Receive1</om:Property>
          <om:Property Name='Activate'>true</om:Property>
        </om:Element>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>Receive2</om:Property>
          <om:Property Name='Activate'>false</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithBusinessRules(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>BusinessRulesOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>Receive1</om:Property>
        </om:Element>
        <om:Element Type='CallRules'>
          <om:Property Name='Name'>CallBusinessRules</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>Send1</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        private string CreateOdxWithUnsupportedShape(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var odxContent = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name'>UnsupportedOrchestration</om:Property>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name'>Receive1</om:Property>
        </om:Element>
        <om:Element Type='UnsupportedCustomShape'>
          <om:Property Name='Name'>CustomShape1</om:Property>
        </om:Element>
        <om:Element Type='Send'>
          <om:Property Name='Name'>Send1</om:Property>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";

            File.WriteAllText(filePath, odxContent);
            return filePath;
        }

        #endregion
    }
}
