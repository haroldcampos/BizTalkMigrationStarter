// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.BTMtoLMLMigrator;

namespace BizTalktoLogicApps.Tests.Integration.BTMtoLML
{
    /// <summary>
    /// Integration tests for the BTM to LML migration.
    /// Tests end-to-end conversion of BizTalk Maps (.btm) to Liquid Maps (.lml).
    /// </summary>
    [TestClass]
    public class BTMtoLMLMigratorTests
    {
        private string testDataDirectory;
        private string mapsDirectory;
        private string schemasDirectory;
        private string outputDirectory;

        [TestInitialize]
        public void Setup()
        {
            // Get the directory where the test assembly is located
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

            // Use test project directory for test data (2 levels up from bin\Debug)
            this.testDataDirectory = Path.Combine(assemblyDirectory, "..", "..", "Data");
            this.mapsDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "Maps");
            this.schemasDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "Schemas");

            // Output to the LogicApps/LMLs directory
            this.outputDirectory = Path.Combine(this.testDataDirectory, "LogicApps", "LMLs");
            Directory.CreateDirectory(this.outputDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Do not delete the output directory - keep generated Liquid maps for reference
        }

        #region Argument Validation

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void ConvertBtmToLml_NonExistentBtmFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentBtm = Path.Combine(this.mapsDirectory, "NonExistent.btm");
            var migrator = new BtmMigrator();

            // Act
            migrator.ConvertBtmToLml(
                btmFilePath: nonExistentBtm,
                sourceSchemaPath: null,
                targetSchemaPath: null);
        }

        #endregion

        #region Schema Validation

        [TestMethod]
        public void ConvertBtmToLml_ValidateSchemasExist_VerifiesSchemaDirectory()
        {
            // Arrange
            Assert.IsTrue(Directory.Exists(this.schemasDirectory), 
                $"Schemas directory should exist: {this.schemasDirectory}");

            // Act
            var schemaFiles = Directory.GetFiles(this.schemasDirectory, "*.xsd");

            // Assert
            Assert.IsTrue(schemaFiles.Length > 0, 
                "At least one schema file should exist for map conversion");
        }

        #endregion

        #region Successful Conversion - Process All Files

        [TestMethod]
        public void ConvertAllBtmFiles_GeneratesLiquidMaps()
        {
            // Arrange - Get all BTM files from the Data/BizTalk/Maps directory
            var btmFiles = Directory.GetFiles(this.mapsDirectory, "*.btm");
            Assert.IsTrue(btmFiles.Length > 0, "No BTM files found in test data directory");

            var successCount = 0;
            var failureCount = 0;
            var results = new System.Text.StringBuilder();

            results.AppendLine($"Processing {btmFiles.Length} BTM files from {this.mapsDirectory}");
            results.AppendLine(new string('-', 80));

            // Act - Process each BTM file
            foreach (var btmPath in btmFiles)
            {
                var btmFileName = Path.GetFileName(btmPath);
                var baseName = Path.GetFileNameWithoutExtension(btmPath);
                var outputFileName = $"{baseName}.lml";
                var outputPath = Path.Combine(this.outputDirectory, outputFileName);

                results.AppendLine($"\nProcessing: {btmFileName}");

                try
                {
                    // Find associated schemas based on map name pattern
                    // Expected pattern: SourceSchema_To_TargetSchema.btm
                    var schemaPaths = this.FindSchemasForMap(btmPath);

                    var migrator = new BtmMigrator();

                    // Convert BTM to Liquid Map
                    var lmlContent = migrator.ConvertBtmToLml(
                        btmFilePath: btmPath,
                        sourceSchemaPath: schemaPaths.Item1,
                        targetSchemaPath: schemaPaths.Item2);

                    // Save output
                    File.WriteAllText(outputPath, lmlContent);

                    // Verify output
                    Assert.IsTrue(File.Exists(outputPath), $"Output file should exist: {outputFileName}");
                    Assert.IsFalse(string.IsNullOrEmpty(lmlContent), 
                        $"Liquid map should not be empty for {btmFileName}");

                    results.AppendLine($"  ? SUCCESS: Generated {outputFileName}");
                    var fileInfo = new FileInfo(outputPath);
                    results.AppendLine($"  ? Size: {fileInfo.Length} bytes");

                    if (!string.IsNullOrEmpty(schemaPaths.Item1))
                    {
                        results.AppendLine($"  ? Source Schema: {Path.GetFileName(schemaPaths.Item1)}");
                    }

                    if (!string.IsNullOrEmpty(schemaPaths.Item2))
                    {
                        results.AppendLine($"  ? Target Schema: {Path.GetFileName(schemaPaths.Item2)}");
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
            results.AppendLine($"  Total BTM files: {btmFiles.Length}");
            results.AppendLine($"  Successful: {successCount}");
            results.AppendLine($"  Failed: {failureCount}");
            results.AppendLine(new string('=', 80));

            Console.WriteLine(results.ToString());

            // Assert at least one file was processed successfully
            Assert.IsTrue(successCount > 0, "At least one BTM file should be converted successfully");
        }

        #endregion

        #region File Output - Process All Files

        [TestMethod]
        public void ConvertBtmToLml_ProcessAllFiles_CreatesAllLiquidMaps()
        {
            // Arrange - Get all BTM files
            var btmFiles = Directory.GetFiles(this.mapsDirectory, "*.btm");
            Assert.IsTrue(btmFiles.Length > 0, "No BTM files found");

            var results = new System.Text.StringBuilder();
            results.AppendLine($"Generating Liquid maps for {btmFiles.Length} BizTalk maps");
            results.AppendLine(new string('-', 80));

            var generatedFiles = new System.Collections.Generic.List<string>();

            // Act - Generate Liquid maps for each BTM file
            foreach (var btmPath in btmFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(btmPath);
                var outputPath = Path.Combine(this.outputDirectory, $"{baseName}.lml");

                try
                {
                    var schemaPaths = this.FindSchemasForMap(btmPath);
                    var migrator = new BtmMigrator();

                    var lmlContent = migrator.ConvertBtmToLml(
                        btmFilePath: btmPath,
                        sourceSchemaPath: schemaPaths.Item1,
                        targetSchemaPath: schemaPaths.Item2);

                    File.WriteAllText(outputPath, lmlContent);

                    generatedFiles.Add(outputPath);
                    results.AppendLine($"? Generated: {Path.GetFileName(outputPath)}");

                    var fileInfo = new FileInfo(outputPath);
                    results.AppendLine($"  Size: {fileInfo.Length} bytes");
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

            Assert.IsTrue(generatedFiles.Count > 0, "At least one Liquid map should be generated");

            // Verify all generated files exist and are not empty
            foreach (var file in generatedFiles)
            {
                Assert.IsTrue(File.Exists(file), $"File should exist: {file}");
                var content = File.ReadAllText(file);
                Assert.IsFalse(string.IsNullOrEmpty(content), $"File should not be empty: {file}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds source and target schema paths for a BTM file.
        /// Looks for schemas based on the map name pattern: SourceSchema_To_TargetSchema.btm
        /// </summary>
        /// <param name="btmPath">Path to the BTM file.</param>
        /// <returns>Tuple of (sourceSchemaPath, targetSchemaPath). Paths may be null if not found.</returns>
        private Tuple<string, string> FindSchemasForMap(string btmPath)
        {
            var mapName = Path.GetFileNameWithoutExtension(btmPath);
            string sourceSchemaPath = null;
            string targetSchemaPath = null;

            // Try to parse the map name pattern: SourceSchema_To_TargetSchema
            var parts = mapName.Split(new[] { "_To_", "_to_" }, StringSplitOptions.None);

            if (parts.Length == 2)
            {
                // Look for source schema
                var sourceSchemaName = parts[0] + ".xsd";
                var potentialSourcePath = Path.Combine(this.schemasDirectory, sourceSchemaName);
                if (File.Exists(potentialSourcePath))
                {
                    sourceSchemaPath = potentialSourcePath;
                }

                // Look for target schema
                var targetSchemaName = parts[1] + ".xsd";
                var potentialTargetPath = Path.Combine(this.schemasDirectory, targetSchemaName);
                if (File.Exists(potentialTargetPath))
                {
                    targetSchemaPath = potentialTargetPath;
                }
            }

            // If schemas not found by pattern, try to find any available schemas
            if (string.IsNullOrEmpty(sourceSchemaPath) || string.IsNullOrEmpty(targetSchemaPath))
            {
                var availableSchemas = Directory.GetFiles(this.schemasDirectory, "*.xsd");
                
                if (availableSchemas.Length > 0 && string.IsNullOrEmpty(sourceSchemaPath))
                {
                    sourceSchemaPath = availableSchemas[0];
                }

                if (availableSchemas.Length > 1 && string.IsNullOrEmpty(targetSchemaPath))
                {
                    targetSchemaPath = availableSchemas[1];
                }
                else if (availableSchemas.Length > 0 && string.IsNullOrEmpty(targetSchemaPath))
                {
                    targetSchemaPath = availableSchemas[0];
                }
            }

            return Tuple.Create(sourceSchemaPath, targetSchemaPath);
        }

        #endregion
    }
}

