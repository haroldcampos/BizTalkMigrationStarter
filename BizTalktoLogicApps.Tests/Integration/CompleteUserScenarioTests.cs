// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.Tests.Integration
{
    /// <summary>
    /// Tests the complete user scenario: Feed one or many ODX files and one or many binding files to get reports
    /// </summary>
    [TestClass]
    public class CompleteUserScenarioTests
    {
        private string testDataDirectory;
        private string odxDirectory;
        private string bindingsDirectory;
        private string outputDirectory;
        private string sourceOdxDirectory;
        private string sourceBindingsDirectory;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "BizTalkMigrator_Tests", "UserScenarios");
            this.testDataDirectory = baseDir;
            this.odxDirectory = Path.Combine(baseDir, "Orchestrations");
            this.bindingsDirectory = Path.Combine(baseDir, "Bindings");
            this.outputDirectory = Path.Combine(baseDir, "Reports");

            Directory.CreateDirectory(this.odxDirectory);
            Directory.CreateDirectory(this.bindingsDirectory);
            Directory.CreateDirectory(this.outputDirectory);

            // Locate source ODX and binding files in the test data directory
            var currentDirectory = Directory.GetCurrentDirectory();
            var solutionDirectory = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".."));
            var testProjectDirectory = Path.Combine(solutionDirectory, "BizTalktoLogicApps.Tests");
            this.sourceOdxDirectory = Path.Combine(testProjectDirectory, "Data", "BizTalk", "ODX");
            this.sourceBindingsDirectory = Path.Combine(testProjectDirectory, "Data", "BizTalk", "Bindings");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(this.testDataDirectory))
            {
                Directory.Delete(this.testDataDirectory, recursive: true);
            }
        }

        #region Scenario 1: Single ODX + Single Binding ? Single Report

        [TestMethod]
        public void Scenario_SingleOdxSingleBinding_GeneratesCompleteReport()
        {
            // Arrange - Find first ODX that has a matching real binding file
            var pair = this.FindFirstOdxBindingPair();
            if (pair == null)
            {
                Assert.Inconclusive("No ODX files with matching binding files found in Data/BizTalk/. Add matching .odx and .xml files.");
                return;
            }

            var odxPath = pair.Value.OdxPath;
            var bindingPath = pair.Value.BindingPath;
            var baseName = Path.GetFileNameWithoutExtension(odxPath);

            // Act
            // Step 1: Parse binding file
            var bindings = BindingSnapshot.Parse(bindingPath);
            
            // Step 2: Generate orchestration report (HTML format)
            var htmlReportPath = Path.Combine(this.outputDirectory, baseName + "_Report.html");
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath,
                htmlReportPath,
                ReportFormat.Html);

            // Step 3: Generate orchestration report (Markdown format)
            var mdReportPath = Path.Combine(this.outputDirectory, baseName + "_Report.md");
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath,
                mdReportPath,
                ReportFormat.Markdown);

            // Assert
            Assert.IsNotNull(bindings, "Should parse bindings successfully");
            Assert.IsTrue(bindings.ReceiveLocations.Count > 0, "Should have receive locations");

            Assert.IsTrue(File.Exists(htmlReportPath), "HTML report should be generated");
            Assert.IsTrue(File.Exists(mdReportPath), "Markdown report should be generated");

            // Verify HTML report content
            var htmlContent = File.ReadAllText(htmlReportPath);
            Assert.IsTrue(htmlContent.Contains("Migration Report"), "Report should have title");
            Assert.IsTrue(htmlContent.Contains("Complexity Score"), "Report should show complexity");
            Assert.IsTrue(htmlContent.Contains("Migration Readiness"), "Report should show readiness");

            // Verify Markdown report content
            var mdContent = File.ReadAllText(mdReportPath);
            
            // Log content for debugging
            this.TestContext.WriteLine("=== Markdown Report Content ===");
            this.TestContext.WriteLine(mdContent);
            this.TestContext.WriteLine("=== End Report Content ===");
            
            Assert.IsTrue(mdContent.Contains("# Migration Report"), "MD report should have title");
            Assert.IsTrue(mdContent.Contains("Statistics") || mdContent.Contains("Total Shapes"), 
                "MD report should have statistics or shape information");
        }

        #endregion

        #region Scenario 2: Multiple ODX + Single Binding ? Batch Report

        [TestMethod]
        public void Scenario_MultipleOdxSingleBinding_GeneratesBatchReport()
        {
            // Arrange - Use all real ODX files with matching bindings
            var pairs = this.FindAllOdxBindingPairs();
            if (pairs.Count == 0)
            {
                Assert.Inconclusive("No ODX files with matching binding files found in Data/BizTalk/.");
                return;
            }

            // Copy ODX files to test directory
            var odxFiles = new List<string>();
            foreach (var pair in pairs)
            {
                var destOdx = Path.Combine(this.odxDirectory, Path.GetFileName(pair.OdxPath));
                File.Copy(pair.OdxPath, destOdx, overwrite: true);
                odxFiles.Add(destOdx);
            }

            // Use the first binding file as the shared binding
            var sharedBindingPath = pairs[0].BindingPath;

            // Act
            var bindings = BindingSnapshot.Parse(sharedBindingPath);

            // Generate batch report
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles.ToArray(),
                sharedBindingPath,
                this.outputDirectory,
                ReportFormat.Html);

            // Assert
            Assert.IsNotNull(bindings, "Should parse bindings");

            // Verify individual reports generated
            foreach (var odxPath in odxFiles)
            {
                var expectedReport = Path.Combine(this.outputDirectory,
                    Path.GetFileNameWithoutExtension(odxPath) + "_MigrationReport.html");
                Assert.IsTrue(File.Exists(expectedReport),
                    $"Individual report should exist for {Path.GetFileName(odxPath)}");
            }

            // Verify batch summary report
            var summaryFiles = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.html");
            Assert.IsTrue(summaryFiles.Length > 0, "Batch summary should be generated");

            var summaryContent = File.ReadAllText(summaryFiles[0]);
            Assert.IsTrue(summaryContent.Contains("Overall Statistics"), "Should have overall stats");
            Assert.IsTrue(summaryContent.Contains("Orchestration Details"), "Should have details table");
        }

        #endregion

        #region Scenario 3: Multiple ODX + Multiple Bindings ? Comprehensive Report

        [TestMethod]
        public void Scenario_MultipleOdxMultipleBindings_GeneratesComprehensiveReports()
        {
            // Arrange - Use all real binding files and ODX files from Data/
            var pairs = this.FindAllOdxBindingPairs();
            if (pairs.Count < 2)
            {
                Assert.Inconclusive($"Need at least 2 ODX+binding pairs for this scenario. Found {pairs.Count}. Add matching .odx and .xml files to Data/BizTalk/.");
                return;
            }

            // Copy ODX files to test directory
            var odxFiles = new List<string>();
            foreach (var pair in pairs)
            {
                var destOdx = Path.Combine(this.odxDirectory, Path.GetFileName(pair.OdxPath));
                File.Copy(pair.OdxPath, destOdx, overwrite: true);
                odxFiles.Add(destOdx);
            }

            // Use each real binding file as a separate "environment"
            var bindingFiles = pairs.Select(p => p.BindingPath).Distinct().ToArray();

            // Act
            var allBindings = new Dictionary<string, BindingSnapshot>();
            foreach (var bindingPath in bindingFiles)
            {
                var bindingName = Path.GetFileNameWithoutExtension(bindingPath);
                allBindings[bindingName] = BindingSnapshot.Parse(bindingPath);
            }

            // Generate reports for each binding file
            foreach (var kvp in allBindings)
            {
                var envOutputDir = Path.Combine(this.outputDirectory, kvp.Key);
                Directory.CreateDirectory(envOutputDir);

                OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                    odxFiles.ToArray(),
                    bindingFiles.First(b => Path.GetFileNameWithoutExtension(b) == kvp.Key),
                    envOutputDir,
                    ReportFormat.Html);
            }

            // Assert
            Assert.IsTrue(allBindings.Count >= 1, "Should parse at least one binding file");

            // Verify each binding context has reports
            foreach (var kvp in allBindings)
            {
                var envDir = Path.Combine(this.outputDirectory, kvp.Key);
                Assert.IsTrue(Directory.Exists(envDir), $"{kvp.Key} directory should exist");

                var reports = Directory.GetFiles(envDir, "*.html");
                Assert.IsTrue(reports.Length >= 2,
                    $"{kvp.Key} should have at least 2 reports (individual + summary)");
                Assert.IsTrue(kvp.Value.ReceiveLocations.Count > 0, $"{kvp.Key} should have receive locations");
            }
        }

        #endregion

        #region Scenario 4: Directory-Based Processing (Real User Workflow)

        [TestMethod]
        public void Scenario_ProcessEntireDirectory_GeneratesAllReports()
        {
            // Arrange - Use real ODX files from Data/BizTalk/ODX and real bindings from Data/BizTalk/Bindings
            if (!Directory.Exists(this.sourceOdxDirectory))
            {
                Assert.Inconclusive("Source ODX directory not found: " + this.sourceOdxDirectory);
                return;
            }

            var sourceOdxFiles = Directory.GetFiles(this.sourceOdxDirectory, "*.odx");
            if (sourceOdxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files found in Data/BizTalk/ODX/.");
                return;
            }

            // Copy real ODX files to test directory
            foreach (var sourceOdx in sourceOdxFiles)
            {
                var destPath = Path.Combine(this.odxDirectory, Path.GetFileName(sourceOdx));
                File.Copy(sourceOdx, destPath, overwrite: true);
            }

            // Copy real binding files to test directory
            var sourceBindingFiles = Directory.Exists(this.sourceBindingsDirectory)
                ? Directory.GetFiles(this.sourceBindingsDirectory, "*.xml")
                : new string[0];

            if (sourceBindingFiles.Length == 0)
            {
                Assert.Inconclusive("No binding files found in Data/BizTalk/Bindings/.");
                return;
            }

            foreach (var sourceBinding in sourceBindingFiles)
            {
                var destPath = Path.Combine(this.bindingsDirectory, Path.GetFileName(sourceBinding));
                File.Copy(sourceBinding, destPath, overwrite: true);
            }

            // Act
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            var bindingFiles = Directory.GetFiles(this.bindingsDirectory, "*.xml");

            // Parse all bindings
            var allBindings = new List<BindingSnapshot>();
            foreach (var bindingPath in bindingFiles)
            {
                allBindings.Add(BindingSnapshot.Parse(bindingPath));
            }

            // Generate reports using the first binding file
            var contextOutputDir = Path.Combine(this.outputDirectory, "AllOrchestrations");
            Directory.CreateDirectory(contextOutputDir);

            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                bindingFiles[0],
                contextOutputDir,
                ReportFormat.Html);

            // Assert
            Assert.IsTrue(allBindings.Count > 0, "Should parse at least one binding file");

            var reports = Directory.GetFiles(contextOutputDir, "*.html", SearchOption.AllDirectories);
            Assert.IsTrue(reports.Length >= 2,
                "Should generate at least individual + batch summary reports");

            this.TestContext.WriteLine($"ODX files: {odxFiles.Length}");
            this.TestContext.WriteLine($"Binding files: {bindingFiles.Length}");
            this.TestContext.WriteLine($"Reports generated: {reports.Length}");
        }

        #endregion

        #region Scenario 5: Gap Analysis + Reporting

        [TestMethod]
        public void Scenario_GapAnalysisPlusReporting_ProvidesCompleteInsights()
        {
            // Arrange - Use real ODX files from Data/BizTalk/ODX
            if (!Directory.Exists(this.sourceOdxDirectory))
            {
                Assert.Inconclusive("Source ODX directory not found: " + this.sourceOdxDirectory);
                return;
            }

            var sourceOdxFiles = Directory.GetFiles(this.sourceOdxDirectory, "*.odx");
            if (sourceOdxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files found in Data/BizTalk/ODX/.");
                return;
            }

            // Copy real ODX files to test directory
            foreach (var sourceOdx in sourceOdxFiles)
            {
                var destPath = Path.Combine(this.odxDirectory, Path.GetFileName(sourceOdx));
                File.Copy(sourceOdx, destPath, overwrite: true);
            }

            // Use first matching real binding file (if any) for report generation
            string bindingPath = null;
            if (Directory.Exists(this.sourceBindingsDirectory))
            {
                var bindingFiles = Directory.GetFiles(this.sourceBindingsDirectory, "*.xml");
                if (bindingFiles.Length > 0)
                {
                    bindingPath = bindingFiles[0];
                }
            }

            // Act
            // Step 1: Run gap analysis
            var gapReport = OdxAnalyzer.AnalyzeDirectory(this.odxDirectory);
            
            // Step 2: Save gap analysis report
            var gapReportPath = Path.Combine(this.outputDirectory, "GapAnalysis.json");
            OdxAnalyzer.SaveReportToJson(gapReport, gapReportPath);

            // Step 3: Generate migration reports
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                bindingPath,
                this.outputDirectory,
                ReportFormat.Html);

            // Assert
            Assert.AreEqual(sourceOdxFiles.Length, gapReport.TotalFilesAnalyzed, "Should analyze all ODX files");
            Assert.AreEqual(sourceOdxFiles.Length, gapReport.SuccessfullyParsed, "Should successfully parse all files");
            Assert.IsTrue(gapReport.ShapeTypeFrequency.Count > 0, "Should detect shape types");
            Assert.IsTrue(gapReport.RecommendedFeatures.Count > 0, "Should provide recommendations");
            
            // Log what was actually detected for visibility
            this.TestContext.WriteLine($"Files analyzed: {gapReport.TotalFilesAnalyzed}");
            this.TestContext.WriteLine($"Files with correlation: {gapReport.FilesWithCorrelation.Count}");
            this.TestContext.WriteLine($"Files with transactions: {gapReport.FilesWithTransactions.Count}");
            this.TestContext.WriteLine($"Total shape types detected: {gapReport.ShapeTypeFrequency.Count}");
            this.TestContext.WriteLine($"Recommended features: {gapReport.RecommendedFeatures.Count}");

            // Report generation assertions
            Assert.IsTrue(File.Exists(gapReportPath), "Gap analysis JSON should exist");
            
            var batchSummary = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.html");
            Assert.IsTrue(batchSummary.Length > 0, "Batch summary should exist");

            var individualReports = Directory.GetFiles(this.outputDirectory, "*_MigrationReport.html");
            Assert.AreEqual(sourceOdxFiles.Length, individualReports.Length, "Should have one report per ODX file");

            // Verify gap report content
            var gapJson = File.ReadAllText(gapReportPath);
            Assert.IsTrue(gapJson.Contains("TotalFilesAnalyzed"), "Gap JSON should have totals");
            Assert.IsTrue(gapJson.Contains("RecommendedFeatures"), "Gap JSON should have recommendations");
        }

        #endregion

        #region Scenario 6: Error Handling in Batch Processing

        [TestMethod]
        public void Scenario_BatchProcessingWithErrors_ContinuesAndReports()
        {
            // Arrange - Copy real ODX files and add an invalid one
            if (!Directory.Exists(this.sourceOdxDirectory))
            {
                Assert.Inconclusive("Source ODX directory not found.");
                return;
            }

            var sourceOdxFiles = Directory.GetFiles(this.sourceOdxDirectory, "*.odx");
            if (sourceOdxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files found in Data/BizTalk/ODX/.");
                return;
            }

            // Copy at least one real ODX file
            var validOdxFile = sourceOdxFiles[0];
            var destValid = Path.Combine(this.odxDirectory, Path.GetFileName(validOdxFile));
            File.Copy(validOdxFile, destValid, overwrite: true);

            // Create invalid ODX file
            var invalidPath = Path.Combine(this.odxDirectory, "InvalidOrch.odx");
            File.WriteAllText(invalidPath, "<Invalid>Not a valid ODX file</Invalid>");

            // Use real binding file if available, otherwise null
            string bindingPath = null;
            if (Directory.Exists(this.sourceBindingsDirectory))
            {
                var bindingFiles = Directory.GetFiles(this.sourceBindingsDirectory, "*.xml");
                if (bindingFiles.Length > 0)
                {
                    bindingPath = bindingFiles[0];
                }
            }

            // Act
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            
            // This should not throw - should handle errors gracefully
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                bindingPath,
                this.outputDirectory,
                ReportFormat.Html);

            // Assert
            var batchSummary = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.html");
            Assert.IsTrue(batchSummary.Length > 0, "Batch summary should still be generated");

            var summaryContent = File.ReadAllText(batchSummary[0]);
            Assert.IsTrue(summaryContent.Contains("Failed"), "Summary should report failures");
            Assert.IsTrue(summaryContent.Contains("Successfully Processed"), "Summary should report successes");
        }

        #endregion

        #region Helper Methods

        private struct OdxBindingPair
        {
            public string OdxPath;
            public string BindingPath;
        }

        /// <summary>
        /// Finds the first ODX file in Data/BizTalk/ODX/ that has a matching .xml binding file
        /// in Data/BizTalk/Bindings/ (same base name).
        /// </summary>
        private OdxBindingPair? FindFirstOdxBindingPair()
        {
            var pairs = this.FindAllOdxBindingPairs();
            return pairs.Count > 0 ? pairs[0] : (OdxBindingPair?)null;
        }

        /// <summary>
        /// Finds all ODX files that have matching real binding files (same base name).
        /// </summary>
        private List<OdxBindingPair> FindAllOdxBindingPairs()
        {
            var result = new List<OdxBindingPair>();

            if (!Directory.Exists(this.sourceOdxDirectory) || !Directory.Exists(this.sourceBindingsDirectory))
                return result;

            var odxFiles = Directory.GetFiles(this.sourceOdxDirectory, "*.odx");
            foreach (var odxPath in odxFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(odxPath);
                var bindingPath = Path.Combine(this.sourceBindingsDirectory, baseName + ".xml");
                if (File.Exists(bindingPath))
                {
                    result.Add(new OdxBindingPair { OdxPath = odxPath, BindingPath = bindingPath });
                }
            }

            return result;
        }

        #endregion
    }
}
