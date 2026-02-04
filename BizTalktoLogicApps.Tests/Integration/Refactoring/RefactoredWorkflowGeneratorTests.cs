// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;

namespace BizTalktoLogicApps.Tests.Integration.Refactoring
{
    /// <summary>
    /// Integration tests for the RefactoredWorkflowGenerator class.
    /// Tests end-to-end refactored workflow generation with different options.
    /// </summary>
    [TestClass]
    public class RefactoredWorkflowGeneratorTests
    {
        private string testDataDirectory;
        private string odxDirectory;
        private string bindingsDirectory;
        private string outputDirectory;

        [TestInitialize]
        public void Setup()
        {
            // Get the directory where the test assembly is located
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

            // Navigate to the Data folder relative to the test assembly (2 levels up from bin\Debug)
            this.testDataDirectory = Path.Combine(assemblyDirectory, "..", "..", "Data");
            this.odxDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "ODX");
            this.bindingsDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "Bindings");

            // Output to the LogicApps/Workflows directory
            this.outputDirectory = Path.Combine(this.testDataDirectory, "LogicApps", "Workflows");

            // Create all necessary directories
            Directory.CreateDirectory(this.odxDirectory);
            Directory.CreateDirectory(this.bindingsDirectory);
            Directory.CreateDirectory(this.outputDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Do not delete the output directory - keep generated workflows for reference
        }

        #region Argument Validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateRefactoredWorkflow_NullOdxPath_ThrowsArgumentNullException()
        {
            // Arrange
            var bindingFiles = Directory.GetFiles(this.bindingsDirectory, "*.xml");
            if (bindingFiles.Length == 0)
            {
                Assert.Inconclusive("No binding files available for testing");
            }

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: null,
                bindingsFilePath: bindingFiles[0]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateRefactoredWorkflow_EmptyOdxPath_ThrowsArgumentNullException()
        {
            // Arrange
            var bindingFiles = Directory.GetFiles(this.bindingsDirectory, "*.xml");
            if (bindingFiles.Length == 0)
            {
                Assert.Inconclusive("No binding files available for testing");
            }

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: string.Empty,
                bindingsFilePath: bindingFiles[0]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateRefactoredWorkflow_NullBindingsPath_ThrowsArgumentNullException()
        {
            // Arrange
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files available for testing");
            }

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: odxFiles[0],
                bindingsFilePath: null);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void GenerateRefactoredWorkflow_NonExistentOdxFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentOdx = Path.Combine(this.odxDirectory, "NonExistent.odx");
            var bindingFiles = Directory.GetFiles(this.bindingsDirectory, "*.xml");
            if (bindingFiles.Length == 0)
            {
                Assert.Inconclusive("No binding files available for testing");
            }

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: nonExistentOdx,
                bindingsFilePath: bindingFiles[0]);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void GenerateRefactoredWorkflow_NonExistentBindingsFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files available for testing");
            }

            var nonExistentBindings = Path.Combine(this.bindingsDirectory, "NonExistent.xml");

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: odxFiles[0],
                bindingsFilePath: nonExistentBindings);
        }

        #endregion

        #region Options Validation

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GenerateRefactoredWorkflow_OnPremisesWithServiceBus_ThrowsInvalidOperationException()
        {
            // Arrange
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files available for testing");
            }

            var odxPath = odxFiles[0];
            var baseName = Path.GetFileNameWithoutExtension(odxPath);
            var bindingsPath = Path.Combine(this.bindingsDirectory, baseName + ".xml");

            if (!File.Exists(bindingsPath))
            {
                Assert.Inconclusive("No matching bindings file found for testing");
            }

            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "ServiceBus"
            };

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: odxPath,
                bindingsFilePath: bindingsPath,
                options: options);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GenerateRefactoredWorkflow_OnPremisesWithCosmosDb_ThrowsInvalidOperationException()
        {
            // Arrange
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files available for testing");
            }

            var odxPath = odxFiles[0];
            var baseName = Path.GetFileNameWithoutExtension(odxPath);
            var bindingsPath = Path.Combine(this.bindingsDirectory, baseName + ".xml");

            if (!File.Exists(bindingsPath))
            {
                Assert.Inconclusive("No matching bindings file found for testing");
            }

            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredDatabaseConnector = "CosmosDb"
            };

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                odxPath: odxPath,
                bindingsFilePath: bindingsPath,
                options: options);
        }

        #endregion

        #region Successful Generation - Process All Files

        [TestMethod]
        public void GenerateRefactoredWorkflows_ProcessAllOdxFiles_GeneratesAllWorkflows()
        {
            // Arrange - Get all ODX files from the Data/BizTalk/ODX directory
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            Assert.IsTrue(odxFiles.Length > 0, "No ODX files found in test data directory");

            var successCount = 0;
            var failureCount = 0;
            var results = new System.Text.StringBuilder();

            results.AppendLine($"Processing {odxFiles.Length} ODX files from {this.odxDirectory}");
            results.AppendLine(new string('-', 80));

            // Act - Process each ODX file with its corresponding binding file
            foreach (var odxPath in odxFiles)
            {
                var odxFileName = Path.GetFileName(odxPath);
                var baseName = Path.GetFileNameWithoutExtension(odxPath);
                var bindingsFileName = baseName + ".xml";
                var bindingsPath = Path.Combine(this.bindingsDirectory, bindingsFileName);

                results.AppendLine($"\nProcessing: {odxFileName}");

                try
                {
                    // Check if corresponding bindings file exists
                    if (!File.Exists(bindingsPath))
                    {
                        results.AppendLine($"  ? WARNING: Bindings file not found: {bindingsFileName}");
                        results.AppendLine($"  Skipping {odxFileName}");
                        continue;
                    }

                    // Generate workflow with default options
                    var workflowJson = RefactoredWorkflowGenerator.GenerateRefactoredWorkflow(
                        odxPath: odxPath,
                        bindingsFilePath: bindingsPath,
                        options: null);

                    // Save to output directory
                    var outputFileName = $"{baseName}_workflow.json";
                    var outputPath = Path.Combine(this.outputDirectory, outputFileName);

                    RefactoredWorkflowGenerator.GenerateRefactoredWorkflowToFile(
                        odxPath: odxPath,
                        bindingsFilePath: bindingsPath,
                        outputPath: outputPath,
                        options: new RefactoringOptions
                        {
                            GenerateParametersJson = true
                        });

                    // Verify output
                    Assert.IsTrue(File.Exists(outputPath), $"Output file should exist: {outputFileName}");
                    Assert.IsFalse(string.IsNullOrEmpty(workflowJson), $"Workflow JSON should not be empty for {odxFileName}");

                    results.AppendLine($"  ? SUCCESS: Generated {outputFileName}");
                    var parametersFile = Path.ChangeExtension(outputPath, null) + ".parameters.json";
                    if (File.Exists(parametersFile))
                    {
                        results.AppendLine($"  ? Generated parameters file");
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  ? FAILED: {ex.Message}");
                    failureCount++;
                }
            }

            // Assert - Report summary
            results.AppendLine();
            results.AppendLine(new string('=', 80));
            results.AppendLine($"SUMMARY:");
            results.AppendLine($"  Total ODX files: {odxFiles.Length}");
            results.AppendLine($"  Successful: {successCount}");
            results.AppendLine($"  Failed: {failureCount}");
            results.AppendLine(new string('=', 80));

            Console.WriteLine(results.ToString());

            // Assert at least one file was processed successfully
            Assert.IsTrue(successCount > 0, "At least one ODX file should be processed successfully");
        }

        #endregion

        #region File Output - Process All Files

        [TestMethod]
        public void GenerateRefactoredWorkflowToFile_ProcessAllFiles_CreatesAllWorkflows()
        {
            // Arrange - Get all ODX files
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            Assert.IsTrue(odxFiles.Length > 0, "No ODX files found");

            var results = new System.Text.StringBuilder();
            results.AppendLine($"Generating workflow files for {odxFiles.Length} orchestrations");
            results.AppendLine(new string('-', 80));

            var generatedFiles = new System.Collections.Generic.List<string>();

            // Act - Generate workflow files for each orchestration
            foreach (var odxPath in odxFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(odxPath);
                var bindingsPath = Path.Combine(this.bindingsDirectory, baseName + ".xml");

                if (!File.Exists(bindingsPath))
                {
                    results.AppendLine($"? Skipping {baseName} - no bindings file found");
                    continue;
                }

                try
                {
                    var outputPath = Path.Combine(this.outputDirectory, $"{baseName}_workflow.json");

                    RefactoredWorkflowGenerator.GenerateRefactoredWorkflowToFile(
                        odxPath: odxPath,
                        bindingsFilePath: bindingsPath,
                        outputPath: outputPath,
                        options: new RefactoringOptions
                        {
                            GenerateParametersJson = true,
                            IncludePatternComments = true
                        });

                    generatedFiles.Add(outputPath);
                    results.AppendLine($"? Generated: {Path.GetFileName(outputPath)}");

                    var parametersPath = Path.ChangeExtension(outputPath, null) + ".parameters.json";
                    if (File.Exists(parametersPath))
                    {
                        generatedFiles.Add(parametersPath);
                        results.AppendLine($"  + Parameters: {Path.GetFileName(parametersPath)}");
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"? Failed: {baseName} - {ex.Message}");
                }
            }

            // Assert
            results.AppendLine(new string('=', 80));
            results.AppendLine($"Total files generated: {generatedFiles.Count}");
            results.AppendLine($"Output directory: {this.outputDirectory}");
            Console.WriteLine(results.ToString());

            Assert.IsTrue(generatedFiles.Count > 0, "At least one workflow file should be generated");

            // Verify all generated files exist and are not empty
            foreach (var file in generatedFiles)
            {
                Assert.IsTrue(File.Exists(file), $"File should exist: {file}");
                var content = File.ReadAllText(file);
                Assert.IsFalse(string.IsNullOrEmpty(content), $"File should not be empty: {file}");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerateRefactoredWorkflowToFile_NullOutputPath_ThrowsArgumentNullException()
        {
            // Arrange
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");
            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive("No ODX files available for testing");
            }

            var odxPath = odxFiles[0];
            var baseName = Path.GetFileNameWithoutExtension(odxPath);
            var bindingsPath = Path.Combine(this.bindingsDirectory, baseName + ".xml");

            if (!File.Exists(bindingsPath))
            {
                Assert.Inconclusive("No matching bindings file found for testing");
            }

            // Act
            RefactoredWorkflowGenerator.GenerateRefactoredWorkflowToFile(
                odxPath: odxPath,
                bindingsFilePath: bindingsPath,
                outputPath: null);
        }

        #endregion

    }
}
