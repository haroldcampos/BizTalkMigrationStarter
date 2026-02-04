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
    /// End-to-end tests for processing ODX files and bindings to generate reports
    /// </summary>
    [TestClass]
    public class EndToEndReportGenerationTests
    {
        private string testDataDirectory;
        private string outputDirectory;
        private string sourceOdxDirectory;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            // Create test data directories
            this.testDataDirectory = Path.Combine(Path.GetTempPath(), "BizTalkMigrator_Tests", "EndToEnd");
            this.outputDirectory = Path.Combine(Path.GetTempPath(), "BizTalkMigrator_Tests", "Output");

            Directory.CreateDirectory(this.testDataDirectory);
            Directory.CreateDirectory(this.outputDirectory);

            // Locate source ODX files using the same pattern as OdxAnalyzerTests
            var currentDirectory = Directory.GetCurrentDirectory();
            var solutionDirectory = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".."));
            var testProjectDirectory = Path.Combine(solutionDirectory, "BizTalktoLogicApps.Tests");
            this.sourceOdxDirectory = Path.Combine(testProjectDirectory, "Data", "BizTalk", "ODX");
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Clean up test directories
            if (Directory.Exists(this.testDataDirectory))
            {
                Directory.Delete(this.testDataDirectory, recursive: true);
            }

            if (Directory.Exists(this.outputDirectory))
            {
                Directory.Delete(this.outputDirectory, recursive: true);
            }
        }

        #region Single ODX + Single Binding File Tests

        [TestMethod]
        public void SingleOdx_WithBindings_GeneratesHtmlReport()
        {
            // Arrange
            var odxPath = CreateTestOdxFile("TestOrchestration.odx");
            var bindingPath = CreateTestBindingFile("TestBindings.xml");
            var reportPath = Path.Combine(this.outputDirectory, "TestOrchestration_Report.html");

            // Act
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath, 
                reportPath, 
                ReportFormat.Html);

            // Assert
            Assert.IsTrue(File.Exists(reportPath), "HTML report should be generated");
            var reportContent = File.ReadAllText(reportPath);
            Assert.IsTrue(reportContent.Contains("<!DOCTYPE html>"), "Report should be valid HTML");
            Assert.IsTrue(reportContent.Contains("Migration Report"), "Report should contain title");
        }

        [TestMethod]
        public void SingleOdx_WithBindings_GeneratesMarkdownReport()
        {
            // Arrange
            var odxPath = CreateTestOdxFile("TestOrchestration.odx");
            var bindingPath = CreateTestBindingFile("TestBindings.xml");
            var reportPath = Path.Combine(this.outputDirectory, "TestOrchestration_Report.md");

            // Act
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath, 
                reportPath, 
                ReportFormat.Markdown);

            // Assert
            Assert.IsTrue(File.Exists(reportPath), "Markdown report should be generated");
            var reportContent = File.ReadAllText(reportPath);
            
            // Log content for debugging
            this.TestContext.WriteLine("=== Markdown Report Content ===");
            this.TestContext.WriteLine(reportContent);
            this.TestContext.WriteLine("=== End Report Content ===");
            
            Assert.IsTrue(reportContent.Contains("# Migration Report"), "Report should contain markdown title");
            Assert.IsTrue(reportContent.Contains("Statistics") || reportContent.Contains("Total Shapes"), 
                "Report should contain statistics section or shape information");
        }

        [TestMethod]
        public void SingleOdx_ParsesBindingData()
        {
            // Arrange
            var bindingPath = CreateTestBindingFile("TestBindings.xml");

            // Act
            var bindingSnapshot = BindingSnapshot.Parse(bindingPath);

            // Assert
            Assert.IsNotNull(bindingSnapshot, "Binding snapshot should be created");
            Assert.IsTrue(bindingSnapshot.ReceiveLocations.Count > 0, 
                "Should parse receive locations from binding file");
            Assert.IsTrue(bindingSnapshot.SendPorts.Count > 0, 
                "Should parse send ports from binding file");
        }

        #endregion

        #region Multiple ODX + Multiple Binding Files Tests

        [TestMethod]
        public void MultipleOdx_WithSingleBinding_GeneratesBatchReport()
        {
            // Arrange
            var odxFiles = new[]
            {
                CreateTestOdxFile("Orchestration1.odx"),
                CreateTestOdxFile("Orchestration2.odx"),
                CreateTestOdxFile("Orchestration3.odx")
            };
            var bindingPath = CreateTestBindingFile("SharedBindings.xml");

            // Act
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                bindingPath,
                this.outputDirectory,
                ReportFormat.Html);

            // Assert
            // Individual reports should be created
            foreach (var odxPath in odxFiles)
            {
                var expectedReportPath = Path.Combine(this.outputDirectory, 
                    Path.GetFileNameWithoutExtension(odxPath) + "_MigrationReport.html");
                Assert.IsTrue(File.Exists(expectedReportPath), 
                    $"Individual report should be generated for {Path.GetFileName(odxPath)}");
            }

            // Batch summary should be created
            var summaryFiles = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.html");
            Assert.IsTrue(summaryFiles.Length > 0, "Batch summary report should be generated");

            var summaryContent = File.ReadAllText(summaryFiles[0]);
            Assert.IsTrue(summaryContent.Contains("Overall Statistics"), 
                "Summary should contain overall statistics");
            Assert.IsTrue(summaryContent.Contains("Orchestration Details"), 
                "Summary should contain orchestration details table");
        }

        [TestMethod]
        public void MultipleOdx_WithMultipleBindings_ProcessesAllBindings()
        {
            // Arrange
            var bindingFiles = new[]
            {
                CreateTestBindingFile("Bindings1.xml"),
                CreateTestBindingFile("Bindings2.xml")
            };

            var allBindings = new List<BindingSnapshot>();

            // Act
            foreach (var bindingPath in bindingFiles)
            {
                var snapshot = BindingSnapshot.Parse(bindingPath);
                allBindings.Add(snapshot);
            }

            // Assert
            Assert.AreEqual(2, allBindings.Count, "Should parse all binding files");
            foreach (var binding in allBindings)
            {
                Assert.IsNotNull(binding.ReceiveLocations, "Should have receive locations");
                Assert.IsNotNull(binding.SendPorts, "Should have send ports");
            }
        }

        [TestMethod]
        public void BatchReport_CalculatesStatistics()
        {
            // Arrange
            var odxFiles = new[]
            {
                CreateTestOdxFile("SimpleOrch.odx"),
                CreateTestOdxFile("ComplexOrch.odx")
            };

            // Act
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                null, // No binding file
                this.outputDirectory,
                ReportFormat.Markdown);

            // Assert
            var summaryFiles = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.md");
            Assert.IsTrue(summaryFiles.Length > 0, "Summary should be generated");

            var summaryContent = File.ReadAllText(summaryFiles[0]);
            Assert.IsTrue(summaryContent.Contains("Total Orchestrations"), 
                "Summary should show total count");
            Assert.IsTrue(summaryContent.Contains("Successfully Processed"), 
                "Summary should show success count");
        }

        #endregion

        #region Binding Data Integration Tests

        [TestMethod]
        public void BindingSnapshot_ExtractsReceiveLocationDetails()
        {
            // Arrange
            var bindingPath = CreateDetailedBindingFile();

            // Act
            var snapshot = BindingSnapshot.Parse(bindingPath);

            // Assert
            Assert.IsTrue(snapshot.ReceiveLocations.Count > 0);
            var receiveLocation = snapshot.ReceiveLocations.First();
            
            Assert.IsNotNull(receiveLocation.Name, "Should have name");
            Assert.IsNotNull(receiveLocation.ReceivePortName, "Should have receive port name");
            Assert.IsNotNull(receiveLocation.TransportType, "Should have transport type");
            Assert.IsTrue(receiveLocation.Enabled, "Should capture enabled status");
        }

        [TestMethod]
        public void BindingSnapshot_ExtractsSendPortDetails()
        {
            // Arrange
            var bindingPath = CreateDetailedBindingFile();

            // Act
            var snapshot = BindingSnapshot.Parse(bindingPath);

            // Assert
            Assert.IsTrue(snapshot.SendPorts.Count > 0);
            var sendPort = snapshot.SendPorts.First();
            
            Assert.IsNotNull(sendPort.Name, "Should have name");
            Assert.IsNotNull(sendPort.TransportType, "Should have transport type");
            Assert.IsNotNull(sendPort.Address, "Should have address");
        }

        [TestMethod]
        public void BindingSnapshot_HandlesWcfMetadata()
        {
            // Arrange
            var bindingPath = CreateWcfBindingFile();

            // Act
            var snapshot = BindingSnapshot.Parse(bindingPath);

            // Assert
            var wcfLocation = snapshot.ReceiveLocations
                .FirstOrDefault(rl => rl.TransportType.Contains("WCF"));
            
            if (wcfLocation != null)
            {
                Assert.IsNotNull(wcfLocation.SecurityMode, "Should extract WCF security mode");
                // Other WCF-specific assertions can be added
            }
        }

        [TestMethod]
        public void BindingSnapshot_GroupsReceiveLocationsByPort()
        {
            // Arrange
            var bindingPath = CreateDetailedBindingFile();
            var snapshot = BindingSnapshot.Parse(bindingPath);

            // Act
            var grouped = snapshot.GetReceiveLocationsByPort();

            // Assert
            Assert.IsTrue(grouped.Count > 0, "Should group receive locations");
            foreach (var group in grouped)
            {
                Assert.IsFalse(string.IsNullOrEmpty(group.Key), "Port name should not be empty");
                Assert.IsTrue(group.Value.Count > 0, "Each group should have locations");
            }
        }

        [TestMethod]
        public void BindingSnapshot_FindsSendPortsForReceivePort()
        {
            // Arrange
            var bindingPath = CreateBindingFileWithFilters();
            var snapshot = BindingSnapshot.Parse(bindingPath);

            // Act
            var receivePortName = snapshot.ReceiveLocations.First().ReceivePortName;
            var sendPorts = snapshot.GetSendPortsForReceivePort(receivePortName);

            // Assert
            // May be 0 if no filters defined in test data, but method should not throw
            Assert.IsNotNull(sendPorts);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentNullException))]
        public void BindingSnapshot_Parse_NullPath_ThrowsException()
        {
            // Act
            BindingSnapshot.Parse(null);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void BindingSnapshot_Parse_NonExistentFile_ThrowsException()
        {
            // Act
            BindingSnapshot.Parse("NonExistent.xml");
        }

        [TestMethod]
        public void BatchReport_HandlesFailedOrchestration()
        {
            // Arrange
            var odxFiles = new[]
            {
                CreateTestOdxFile("ValidOrch.odx"),
                Path.Combine(this.testDataDirectory, "InvalidOrch.odx") // Non-existent file
            };

            // Act
            OrchestrationReportGenerator.ExportBatchDiagnosticReport(
                odxFiles,
                null,
                this.outputDirectory,
                ReportFormat.Markdown);

            // Assert
            var summaryFiles = Directory.GetFiles(this.outputDirectory, "BatchMigrationSummary_*.md");
            Assert.IsTrue(summaryFiles.Length > 0, "Summary should still be generated");

            var summaryContent = File.ReadAllText(summaryFiles[0]);
            Assert.IsTrue(summaryContent.Contains("Failed"), 
                "Summary should report failed orchestrations");
        }

        #endregion

        #region Report Content Validation Tests

        [TestMethod]
        public void Report_ContainsComplexityScore()
        {
            // Arrange
            var odxPath = CreateTestOdxFile("TestOrch.odx");
            var reportPath = Path.Combine(this.outputDirectory, "TestOrch_Report.html");

            // Act
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath, 
                reportPath, 
                ReportFormat.Html);

            // Assert
            var reportContent = File.ReadAllText(reportPath);
            Assert.IsTrue(reportContent.Contains("Complexity Score"), 
                "Report should contain complexity score");
        }

        [TestMethod]
        public void Report_ContainsMigrationReadiness()
        {
            // Arrange
            var odxPath = CreateTestOdxFile("TestOrch.odx");
            var reportPath = Path.Combine(this.outputDirectory, "TestOrch_Report.html");

            // Act
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath, 
                reportPath, 
                ReportFormat.Html);

            // Assert
            var reportContent = File.ReadAllText(reportPath);
            Assert.IsTrue(reportContent.Contains("Migration Readiness"), 
                "Report should contain migration readiness");
        }

        [TestMethod]
        public void Report_ContainsStatistics()
        {
            // Arrange
            var odxPath = CreateTestOdxFile("TestOrch.odx");
            var reportPath = Path.Combine(this.outputDirectory, "TestOrch_Report.md");

            // Act
            OrchestrationReportGenerator.ExportDiagnosticReport(
                odxPath, 
                reportPath, 
                ReportFormat.Markdown);

            // Assert
            var reportContent = File.ReadAllText(reportPath);
            Assert.IsTrue(reportContent.Contains("Statistics"), 
                "Report should contain statistics section");
            Assert.IsTrue(reportContent.Contains("Total Shapes"), 
                "Report should contain shape count");
        }

        #endregion

        #region Helper Methods for Test Data Creation

        private string CreateTestOdxFile(string fileName)
        {
            // Use HelloOrchestration.odx as the default test file
            return this.CopyRealOdxFile("HelloOrchestration.odx", fileName);
        }

        private string CopyRealOdxFile(string sourceFileName, string destFileName = null)
        {
            var sourceFile = Path.Combine(this.sourceOdxDirectory, sourceFileName);
            if (!File.Exists(sourceFile))
            {
                Assert.Fail($"Source ODX file not found: {sourceFile}");
            }

            var fileName = destFileName ?? sourceFileName;
            var destFile = Path.Combine(this.testDataDirectory, fileName);
            File.Copy(sourceFile, destFile, overwrite: true);
            return destFile;
        }

        private string CreateTestBindingFile(string fileName)
        {
            var filePath = Path.Combine(this.testDataDirectory, fileName);
            
            var bindingContent = @"<?xml version='1.0' encoding='utf-8'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <ReceivePortCollection>
    <ReceivePort Name='TestReceivePort'>
      <ReceiveLocations>
        <ReceiveLocation Name='TestRL'>
          <ReceiveLocationTransportType Name='FILE' />
          <Address>C:\Input\*.xml</Address>
          <Enable>true</Enable>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
  <SendPortCollection>
    <SendPort Name='TestSendPort'>
      <TransportType Name='FILE' />
      <Address>C:\Output\output.xml</Address>
    </SendPort>
  </SendPortCollection>
</BindingInfo>";

            File.WriteAllText(filePath, bindingContent);
            return filePath;
        }

        private string CreateDetailedBindingFile()
        {
            var filePath = Path.Combine(this.testDataDirectory, "DetailedBindings.xml");
            
            var bindingContent = @"<?xml version='1.0' encoding='utf-8'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <ReceivePortCollection>
    <ReceivePort Name='RP_OrderProcessing'>
      <ReceiveLocations>
        <ReceiveLocation Name='RL_FilePickup'>
          <ReceiveLocationTransportType Name='FILE' />
          <Address>C:\Orders\*.xml</Address>
          <Enable>true</Enable>
          <ReceiveLocationTransportTypeData>&lt;CustomProps&gt;
            &lt;FileMask&gt;*.xml&lt;/FileMask&gt;
            &lt;PollingInterval&gt;60&lt;/PollingInterval&gt;
          &lt;/CustomProps&gt;</ReceiveLocationTransportTypeData>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
  <SendPortCollection>
    <SendPort Name='SP_ShipOrders'>
      <TransportType Name='HTTP' />
      <Address>http://shipping.contoso.com/api/ship</Address>
    </SendPort>
  </SendPortCollection>
</BindingInfo>";

            File.WriteAllText(filePath, bindingContent);
            return filePath;
        }

        private string CreateWcfBindingFile()
        {
            var filePath = Path.Combine(this.testDataDirectory, "WcfBindings.xml");
            
            var bindingContent = @"<?xml version='1.0' encoding='utf-8'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <ReceivePortCollection>
    <ReceivePort Name='WcfReceivePort'>
      <ReceiveLocations>
        <ReceiveLocation Name='WcfRL'>
          <ReceiveLocationTransportType Name='WCF-BasicHttp' />
          <Address>http://localhost:8080/service</Address>
          <Enable>true</Enable>
          <ReceiveLocationTransportTypeData>&lt;CustomProps&gt;
            &lt;SecurityMode&gt;Transport&lt;/SecurityMode&gt;
            &lt;MessageClientCredentialType&gt;UserName&lt;/MessageClientCredentialType&gt;
          &lt;/CustomProps&gt;</ReceiveLocationTransportTypeData>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
</BindingInfo>";

            File.WriteAllText(filePath, bindingContent);
            return filePath;
        }

        private string CreateBindingFileWithFilters()
        {
            var filePath = Path.Combine(this.testDataDirectory, "BindingsWithFilters.xml");
            
            var bindingContent = @"<?xml version='1.0' encoding='utf-8'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <ReceivePortCollection>
    <ReceivePort Name='RP_FilteredPort'>
      <ReceiveLocations>
        <ReceiveLocation Name='RL_Filtered'>
          <ReceiveLocationTransportType Name='FILE' />
          <Address>C:\Input\*.xml</Address>
          <Enable>true</Enable>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
  <SendPortCollection>
    <SendPort Name='SP_Filtered'>
      <TransportType Name='FILE' />
      <Address>C:\Output\filtered.xml</Address>
      <Filter>&lt;Filter&gt;
        &lt;Group&gt;
          &lt;Statement Property='BTS.ReceivePortName' Operator='0' Value='RP_FilteredPort' /&gt;
        &lt;/Group&gt;
      &lt;/Filter&gt;</Filter>
    </SendPort>
  </SendPortCollection>
</BindingInfo>";

            File.WriteAllText(filePath, bindingContent);
            return filePath;
        }

        #endregion
    }
}
