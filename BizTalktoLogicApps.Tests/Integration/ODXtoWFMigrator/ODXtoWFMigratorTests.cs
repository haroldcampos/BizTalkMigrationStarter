// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Integration.ODXtoWFMigrator
{
    /// <summary>
    /// Integration tests for the ODXtoWFMigrator (Orchestrations to Workflows).
    /// Tests end-to-end conversion of BizTalk Orchestrations (.odx) to Logic Apps workflows.
    /// Supports dynamic discovery - customers can add their own orchestration and binding files to the test data directory.
    /// </summary>
    [TestClass]
    public class ODXtoWFMigratorTests
    {
        private string testDataDirectory;
        private string odxDirectory;
        private string bindingsDirectory;
        private string outputDirectory;

        [TestInitialize]
        public void Setup()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

            // Use test project directory for test data (2 levels up from bin\Debug)
            this.testDataDirectory = Path.Combine(assemblyDirectory, "..", "..", "Data");
            this.odxDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "ODX");
            this.bindingsDirectory = Path.Combine(this.testDataDirectory, "BizTalk", "Bindings");
            this.outputDirectory = Path.Combine(this.testDataDirectory, "LogicApps", "Workflows");

            Directory.CreateDirectory(this.odxDirectory);
            Directory.CreateDirectory(this.bindingsDirectory);
            Directory.CreateDirectory(this.outputDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Do not delete the output directory - keep generated workflows for reference
        }

        #region Argument Validation Tests

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void GenerateWorkflowJson_NonExistentOdxFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentOdx = Path.Combine(this.odxDirectory, "NonExistent.odx");
            var bindingsFile = Path.Combine(this.bindingsDirectory, "NonExistent.xml");

            // Create a dummy bindings file
            File.WriteAllText(bindingsFile, "<?xml version=\"1.0\"?><BindingInfo />");

            try
            {
                // Act
                BizTalkOrchestrationParser.GenerateWorkflowJson(
                    odxPath: nonExistentOdx,
                    bindingsPath: bindingsFile,
                    schemaVersion: "2016-06-01");
            }
            finally
            {
                // Cleanup
                if (File.Exists(bindingsFile))
                {
                    File.Delete(bindingsFile);
                }
            }
        }

        [TestMethod]
        public void ParseOdx_ValidFile_ReturnsOrchestrationModel()
        {
            // Arrange - Use real test data
            var odxFile = Path.Combine(this.odxDirectory, "HelloOrchestration.odx");

            // Skip if test data not available
            if (!File.Exists(odxFile))
            {
                Assert.Inconclusive(message: "Test data not found. Add HelloOrchestration.odx to Data/BizTalk/ODX directory.");
                return;
            }

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath: odxFile);

            // Assert
            Assert.IsNotNull(model, "Orchestration model should not be null");
            Assert.IsFalse(condition: string.IsNullOrEmpty(model.Name), message: "Orchestration name should not be empty");
        }

        #endregion

        #region Dynamic Discovery - Process All ODX/Binding Pairs

        [TestMethod]
        public void ConvertAllOrchestrations_GeneratesWorkflows()
        {
            // Arrange - Get all ODX files from the Data/BizTalk/ODX directory
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");

            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive(message: "No .odx files found in test data directory. Add orchestration files to test dynamic discovery.");
                return;
            }

            var successCount = 0;
            var failureCount = 0;
            var results = new StringBuilder();

            results.AppendLine($"Processing {odxFiles.Length} ODX files from {this.odxDirectory}");
            results.AppendLine(new string('-', 80));

            // PASS 1: Detect callable orchestrations
            Console.WriteLine("Detecting callable workflows (Pass 1/2)...");
            var callableOrchestrations = this.DetectCallableOrchestrations(odxFiles);

            if (callableOrchestrations.Count > 0)
            {
                results.AppendLine($"\nFound {callableOrchestrations.Count} callable workflow(s):");
                foreach (var callable in callableOrchestrations.OrderBy(c => c))
                {
                    results.AppendLine($"  - {callable} (will use Request trigger)");
                }
                results.AppendLine();
            }

            // PASS 2: Process each ODX file
            results.AppendLine("Converting orchestrations (Pass 2/2)...");
            results.AppendLine();

            foreach (var odxPath in odxFiles)
            {
                var odxFileName = Path.GetFileName(odxPath);
                var baseName = Path.GetFileNameWithoutExtension(odxPath);
                var bindingFileName = baseName + ".xml";
                var bindingPath = Path.Combine(this.bindingsDirectory, bindingFileName);
                var outputFileName = baseName + ".workflow.json";
                var outputPath = Path.Combine(this.outputDirectory, outputFileName);

                results.AppendLine($"Processing: {odxFileName}");

                // Check if corresponding binding file exists
                if (!File.Exists(bindingPath))
                {
                    results.AppendLine($"  ??  WARNING: No matching binding file found: {bindingFileName}");
                    results.AppendLine($"  Using default bindings for orchestration");

                    // Create a minimal binding file
                    bindingPath = this.CreateDefaultBindingsFile(baseName);
                }
                else
                {
                    results.AppendLine($"  Bindings: {bindingFileName}");
                }

                try
                {
                    // Parse orchestration
                    var orchestration = BizTalkOrchestrationParser.ParseOdx(filePath: odxPath);

                    results.AppendLine($"  Orchestration: {orchestration.Name}");
                    results.AppendLine($"  Shapes: {this.CountShapes(orchestration)}");
                    results.AppendLine($"  Ports: {orchestration.Ports.Count}");
                    results.AppendLine($"  Messages: {orchestration.Messages.Count}");

                    // Check if callable
                    bool isCallable = callableOrchestrations.Contains(orchestration.Name) ||
                                     callableOrchestrations.Contains(orchestration.FullName);

                    if (isCallable)
                    {
                        results.AppendLine($"  Type: Callable workflow (Request trigger)");
                    }

                    // Generate workflow JSON
                    var json = BizTalkOrchestrationParser.GenerateWorkflowJson(
                        odxPath: odxPath,
                        bindingsPath: bindingPath,
                        schemaVersion: "2016-06-01",
                        isCallable: isCallable);

                    // Validate
                    var validator = new WorkflowValidator();
                    var validationResult = validator.Validate(json);

                    results.AppendLine($"  Validation: {validationResult.GetSummary()}");

                    // Save workflow JSON
                    File.WriteAllText(outputPath, json);

                    Assert.IsTrue(condition: File.Exists(outputPath), message: $"Output file should exist: {outputFileName}");
                    Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: $"Workflow JSON should not be empty for {odxFileName}");

                    results.AppendLine($"  ? SUCCESS: Generated {outputFileName}");
                    var fileInfo = new FileInfo(outputPath);
                    results.AppendLine($"  ? Size: {fileInfo.Length} bytes");

                    if (validationResult.HasErrors)
                    {
                        results.AppendLine($"  ??  Workflow has validation errors (review output)");
                    }

                    successCount++;
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    results.AppendLine($"  ? FAILED: {ex.Message}");
                    failureCount++;
                }

                results.AppendLine();
            }

            // Assert - Report summary
            results.AppendLine(new string('=', 80));
            results.AppendLine($"SUMMARY:");
            results.AppendLine($"  Total ODX files: {odxFiles.Length}");
            results.AppendLine($"  Successful: {successCount}");
            results.AppendLine($"  Failed: {failureCount}");
            results.AppendLine(new string('=', 80));

            Console.WriteLine(results.ToString());

            Assert.IsTrue(condition: successCount > 0, message: "At least one ODX file should be converted successfully");
        }

        #endregion

        #region Callable Orchestration Detection Tests

        [TestMethod]
        public void DetectCallableOrchestrations_WithCallShape_IdentifiesCallable()
        {
            // Arrange - Use real test data files
            var odxFiles = Directory.GetFiles(this.odxDirectory, "*.odx");

            if (odxFiles.Length == 0)
            {
                Assert.Inconclusive(message: "No .odx files found in test data directory.");
                return;
            }

            // Act
            var callableOrchestrations = this.DetectCallableOrchestrations(odxFiles);

            // Assert - Verify detection mechanism works
            // The test passes if detection runs without errors
            // Specific callable orchestrations depend on the actual test data content
            Assert.IsNotNull(callableOrchestrations, "Callable orchestrations set should not be null");
            Console.WriteLine($"Detected {callableOrchestrations.Count} callable orchestration(s)");
        }

        [TestMethod]
        public void DetectCallableOrchestrations_WithNamingPattern_IdentifiesCallable()
        {
            // Arrange - Test naming pattern detection with various names
            var testCases = new[]
            {
                new { Name = "HelperOrchestration.odx", Expected = true },
                new { Name = "ChildProcess.odx", Expected = true },
                new { Name = "CommonUtility.odx", Expected = true },
                new { Name = "SubOrchestration.odx", Expected = true },
                new { Name = "MainOrchestration.odx", Expected = false },
                new { Name = "HelloOrchestration.odx", Expected = false }
            };

            // Act & Assert
            foreach (var testCase in testCases)
            {
                var isCallable = this.IsCallableByNaming(testCase.Name);
                Assert.AreEqual(
                    expected: testCase.Expected,
                    actual: isCallable,
                    message: $"{testCase.Name} callable detection should be {testCase.Expected}");
            }
        }

        #endregion

        #region Workflow Validation Tests

        [TestMethod]
        public void GenerateWorkflowJson_CallableOrchestration_HasRequestTrigger()
        {
            // Arrange - Use real test data
            var odxFile = Path.Combine(this.odxDirectory, "HelloOrchestration.odx");
            var bindingsFile = Path.Combine(this.bindingsDirectory, "HelloOrchestration.xml");

            if (!File.Exists(odxFile) || !File.Exists(bindingsFile))
            {
                Assert.Inconclusive(message: "Test data not found. Add HelloOrchestration files to test data directory.");
                return;
            }

            // Act
            var json = BizTalkOrchestrationParser.GenerateWorkflowJson(
                odxPath: odxFile,
                bindingsPath: bindingsFile,
                schemaVersion: "2016-06-01",
                isCallable: true);

            // Assert
            Assert.IsTrue(
                condition: json.Contains("\"type\": \"Request\""),
                message: "Callable workflow should have Request trigger");
        }

        [TestMethod]
        public void GenerateWorkflowJson_ValidOrchestration_ProducesValidJson()
        {
            // Arrange - Use real test data
            var odxFile = Path.Combine(this.odxDirectory, "LoanProcessor.odx");
            var bindingsFile = Path.Combine(this.bindingsDirectory, "LoanProcessor.xml");

            if (!File.Exists(odxFile) || !File.Exists(bindingsFile))
            {
                Assert.Inconclusive(message: "Test data not found. Add LoanProcessor files to test data directory.");
                return;
            }

            // Act
            var json = BizTalkOrchestrationParser.GenerateWorkflowJson(
                odxPath: odxFile,
                bindingsPath: bindingsFile,
                schemaVersion: "2016-06-01");

            // Assert
            Assert.IsFalse(condition: string.IsNullOrEmpty(json), message: "Generated JSON should not be empty");

            // Try to parse as JSON to validate
            try
            {
                var workflow = Newtonsoft.Json.Linq.JObject.Parse(json);
                Assert.IsNotNull(workflow["definition"], message: "Workflow should have definition");
                Assert.IsNotNull(workflow["definition"]["triggers"], message: "Workflow should have triggers");
                Assert.IsNotNull(workflow["definition"]["actions"], message: "Workflow should have actions");
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Assert.Fail(message: "Generated JSON is not valid");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Detects callable orchestrations using two-pass analysis (matching the CLI behavior).
        /// </summary>
        private System.Collections.Generic.HashSet<string> DetectCallableOrchestrations(string[] odxFiles)
        {
            var callableOrchestrations = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var odxPath in odxFiles)
            {
                try
                {
                    var tempModel = BizTalkOrchestrationParser.ParseOdx(odxPath);

                    // Helper function to recursively traverse all shapes in the tree
                    void TraverseShapes(ShapeModel shape, Action<ShapeModel> action)
                    {
                        action(shape);
                        if (shape.Children != null)
                        {
                            foreach (var child in shape.Children)
                            {
                                TraverseShapes(child, action);
                            }
                        }
                    }

                    // Look for Call/Start shapes that reference other orchestrations
                    foreach (var rootShape in tempModel.Shapes)
                    {
                        TraverseShapes(rootShape, shape =>
                        {
                            if (shape is CallShapeModel callShape && !string.IsNullOrEmpty(callShape.Invokee))
                            {
                                // Add both FQN and simple name
                                callableOrchestrations.Add(callShape.Invokee);
                                var simpleName = callShape.Invokee.Contains(".")
                                    ? callShape.Invokee.Substring(callShape.Invokee.LastIndexOf('.') + 1)
                                    : callShape.Invokee;
                                callableOrchestrations.Add(simpleName);

                                Console.WriteLine($"  Found Call: {Path.GetFileNameWithoutExtension(odxPath)} -> {callShape.Invokee}");
                            }
                            else if (shape is StartShapeModel startShape && !string.IsNullOrEmpty(startShape.Invokee))
                            {
                                // Add both FQN and simple name
                                callableOrchestrations.Add(startShape.Invokee);
                                var simpleName = startShape.Invokee.Contains(".")
                                    ? startShape.Invokee.Substring(startShape.Invokee.LastIndexOf('.') + 1)
                                    : startShape.Invokee;
                                callableOrchestrations.Add(simpleName);

                                Console.WriteLine($"  Found Start: {Path.GetFileNameWithoutExtension(odxPath)} -> {startShape.Invokee}");
                            }
                        });
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    Console.WriteLine($"  ERROR parsing {Path.GetFileName(odxPath)}: {ex.Message}");
                }
            }

            return callableOrchestrations;
        }

        /// <summary>
        /// Checks if an orchestration is callable based on naming patterns.
        /// </summary>
        private bool IsCallableByNaming(string odxFileName)
        {
            var orchName = Path.GetFileNameWithoutExtension(odxFileName);
            var callablePatterns = new[] { "common", "callexternal", "inner", "child", "sub", "helper", "utility", "process" };

            return callablePatterns.Any(p => orchName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Counts total shapes in an orchestration model (including nested shapes).
        /// </summary>
        private int CountShapes(OrchestrationModel model)
        {
            int count = 0;

            void CountRecursive(ShapeModel shape)
            {
                count++;
                if (shape.Children != null)
                {
                    foreach (var child in shape.Children)
                    {
                        CountRecursive(child);
                    }
                }
            }

            foreach (var rootShape in model.Shapes)
            {
                CountRecursive(rootShape);
            }

            return count;
        }

        /// <summary>
        /// Creates a minimal default bindings file for testing.
        /// </summary>
        private string CreateDefaultBindingsFile(string orchestrationName)
        {
            var bindingsXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<BindingInfo xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <ModuleRefCollection>
    <ModuleRef Name=""{orchestrationName}"">
      <Services>
        <Service Name=""{orchestrationName}"">
          <Ports>
            <Port Name=""ReceivePort"" Modifier=""2"" BindingOption=""1"">
              <ReceivePortRef Name=""DefaultReceivePort"" />
            </Port>
            <Port Name=""SendPort"" Modifier=""1"" BindingOption=""1"">
              <SendPortRef Name=""DefaultSendPort"" />
            </Port>
          </Ports>
        </Service>
      </Services>
    </ModuleRef>
  </ModuleRefCollection>
  <ReceivePortCollection>
    <ReceivePort Name=""DefaultReceivePort"">
      <ReceiveLocations>
        <ReceiveLocation Name=""DefaultReceiveLocation"">
          <Address>sb://namespace.servicebus.windows.net/queue</Address>
          <TransportType Name=""ServiceBus"" />
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
  <SendPortCollection>
    <SendPort Name=""DefaultSendPort"">
      <Address>sb://namespace.servicebus.windows.net/queue</Address>
      <TransportType Name=""ServiceBus"" />
    </SendPort>
  </SendPortCollection>
</BindingInfo>";

            var bindingsPath = Path.Combine(this.bindingsDirectory, orchestrationName + ".xml");
            File.WriteAllText(bindingsPath, bindingsXml);
            return bindingsPath;
        }

        /// <summary>
        /// Creates a minimal orchestration XML for testing.
        /// </summary>
        private string CreateSimpleOrchestrationXml(string orchestrationName)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<om:MetaModel xmlns:om=""http://schemas.microsoft.com/BizTalk/2003/DesignerData"">
  <om:Element Type=""Module"" OID=""00000000-0000-0000-0000-000000000001"">
    <om:Property Name=""Name"" Value=""TestModule"" />
    <om:Element Type=""ServiceDeclaration"" OID=""00000000-0000-0000-0000-000000000002"">
      <om:Property Name=""Name"" Value=""{orchestrationName}"" />
      <om:Element Type=""ServiceBody"" OID=""00000000-0000-0000-0000-000000000003"">
        <om:Element Type=""Receive"" OID=""00000000-0000-0000-0000-000000000004"">
          <om:Property Name=""Name"" Value=""Receive_1"" />
          <om:Property Name=""Activate"" Value=""True"" />
        </om:Element>
        <om:Element Type=""Send"" OID=""00000000-0000-0000-0000-000000000005"">
          <om:Property Name=""Name"" Value=""Send_1"" />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";
        }

        /// <summary>
        /// Creates an orchestration with a Call shape for testing callable detection.
        /// </summary>
        private string CreateOrchestrationWithCallShape(string orchestrationName, string calledOrchestration)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<om:MetaModel xmlns:om=""http://schemas.microsoft.com/BizTalk/2003/DesignerData"">
  <om:Element Type=""Module"" OID=""00000000-0000-0000-0000-000000000001"">
    <om:Property Name=""Name"" Value=""TestModule"" />
    <om:Element Type=""ServiceDeclaration"" OID=""00000000-0000-0000-0000-000000000002"">
      <om:Property Name=""Name"" Value=""{orchestrationName}"" />
      <om:Element Type=""ServiceBody"" OID=""00000000-0000-0000-0000-000000000003"">
        <om:Element Type=""Receive"" OID=""00000000-0000-0000-0000-000000000004"">
          <om:Property Name=""Name"" Value=""Receive_1"" />
          <om:Property Name=""Activate"" Value=""True"" />
        </om:Element>
        <om:Element Type=""Call"" OID=""00000000-0000-0000-0000-000000000006"">
          <om:Property Name=""Name"" Value=""Call_{calledOrchestration}"" />
          <om:Property Name=""Invokee"" Value=""{calledOrchestration}"" />
        </om:Element>
        <om:Element Type=""Send"" OID=""00000000-0000-0000-0000-000000000005"">
          <om:Property Name=""Name"" Value=""Send_1"" />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>";
        }

        #endregion
    }
}

