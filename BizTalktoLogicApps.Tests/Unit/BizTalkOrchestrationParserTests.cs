// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    /// <summary>
    /// Unit tests for the BizTalkOrchestrationParser class.
    /// Tests parsing of ODX files, shape extraction, and orchestration model generation.
    /// </summary>
    [TestClass]
    public class BizTalkOrchestrationParserTests
    {
        private string testDirectory;

        [TestInitialize]
        public void Setup()
        {
            this.testDirectory = Path.Combine(Path.GetTempPath(), "BizTalkMigrator_Tests", "ParserTests");
            Directory.CreateDirectory(this.testDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(this.testDirectory))
            {
                Directory.Delete(this.testDirectory, recursive: true);
            }
        }

        #region ParseOdx - Argument Validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void ParseOdx_NullFilePath_ThrowsArgumentNullException()
        {
            // Act
            BizTalkOrchestrationParser.ParseOdx(filePath: null);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void ParseOdx_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(this.testDirectory, "NonExistent.odx");

            // Act
            BizTalkOrchestrationParser.ParseOdx(nonExistentPath);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidDataException))]
        public void ParseOdx_MissingXmlDeclaration_ThrowsInvalidDataException()
        {
            // Arrange
            var filePath = this.CreateTestFile("NoXml.odx", "This is not XML content");

            // Act
            BizTalkOrchestrationParser.ParseOdx(filePath);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidDataException))]
        public void ParseOdx_MissingSentinel_ThrowsInvalidDataException()
        {
            // Arrange
            var content = "<?xml version='1.0'?><root>No endif sentinel</root>";
            var filePath = this.CreateTestFile("NoSentinel.odx", content);

            // Act
            BizTalkOrchestrationParser.ParseOdx(filePath);
        }

        #endregion

        #region ParseOdx - Basic Parsing

        [TestMethod]
        public void ParseOdx_ValidSimpleOrchestration_ReturnsOrchestrationModel()
        {
            // Arrange
            var filePath = this.CreateSimpleOdxFile("Simple.odx", "TestOrchestration", "MyCompany.BizTalk");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            Assert.IsNotNull(model, "Model should not be null");
            Assert.AreEqual("TestOrchestration", model.Name, "Orchestration name should match");
            Assert.AreEqual("MyCompany.BizTalk", model.Namespace, "Namespace should match");
            Assert.AreEqual("MyCompany.BizTalk.TestOrchestration", model.FullName, "FullName should be namespace.name");
        }

        [TestMethod]
        public void ParseOdx_OrchestrationWithMessages_ParsesMessages()
        {
            // Arrange
            var filePath = this.CreateOdxWithMessages("WithMessages.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            Assert.IsNotNull(model.Messages, "Messages collection should not be null");
            Assert.IsTrue(model.Messages.Count > 0, "Should have at least one message");
            
            var message = model.Messages.FirstOrDefault(m => string.Equals(m.Name, "InputMessage", StringComparison.Ordinal));
            Assert.IsNotNull(message, "Should find InputMessage");
        }

        [TestMethod]
        public void ParseOdx_OrchestrationWithPorts_ParsesPorts()
        {
            // Arrange
            var filePath = this.CreateOdxWithPorts("WithPorts.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            Assert.IsNotNull(model.Ports, "Ports collection should not be null");
            Assert.IsTrue(model.Ports.Count > 0, "Should have at least one port");
        }

        #endregion

        #region ParseOdx - Shape Parsing

        [TestMethod]
        public void ParseOdx_WithReceiveShape_ParsesReceiveShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithShapes("WithReceive.odx", includeReceive: true);

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var receiveShape = model.Shapes.OfType<ReceiveShapeModel>().FirstOrDefault();
            Assert.IsNotNull(receiveShape, "Should have a Receive shape");
            Assert.AreEqual("Receive", receiveShape.ShapeType, "ShapeType should be Receive");
        }

        [TestMethod]
        public void ParseOdx_WithSendShape_ParsesSendShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithShapes("WithSend.odx", includeSend: true);

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var sendShape = model.Shapes.OfType<SendShapeModel>().FirstOrDefault();
            Assert.IsNotNull(sendShape, "Should have a Send shape");
            Assert.AreEqual("Send", sendShape.ShapeType, "ShapeType should be Send");
        }

        [TestMethod]
        public void ParseOdx_WithDecideShape_ParsesDecideShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithDecide("WithDecide.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var decideShape = model.Shapes.OfType<DecideShapeModel>().FirstOrDefault();
            Assert.IsNotNull(decideShape, "Should have a Decide shape");
            Assert.AreEqual("Decide", decideShape.ShapeType, "ShapeType should be Decide");
        }

        [TestMethod]
        public void ParseOdx_WithLoopShape_ParsesLoopShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithLoop("WithLoop.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var loopShape = model.Shapes.OfType<LoopShapeModel>().FirstOrDefault();
            Assert.IsNotNull(loopShape, "Should have a Loop shape");
        }

        [TestMethod]
        public void ParseOdx_WithConstructShape_ParsesConstructShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithConstruct("WithConstruct.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var constructShape = model.Shapes.OfType<ConstructShapeModel>().FirstOrDefault();
            Assert.IsNotNull(constructShape, "Should have a Construct shape");
            Assert.AreEqual("Construct", constructShape.ShapeType, "ShapeType should be Construct");
        }

        [TestMethod]
        public void ParseOdx_WithCorrelation_ParsesCorrelationDeclaration()
        {
            // Arrange
            var filePath = this.CreateOdxWithCorrelation("WithCorrelation.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var correlationShape = model.Shapes.OfType<CorrelationDeclarationShapeModel>().FirstOrDefault();
            Assert.IsNotNull(correlationShape, "Should have a CorrelationDeclaration shape");
        }

        [TestMethod]
        public void ParseOdx_WithScope_ParsesScopeShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithScope("WithScope.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var scopeShape = model.Shapes.OfType<ScopeShapeModel>().FirstOrDefault();
            Assert.IsNotNull(scopeShape, "Should have a Scope shape");
        }

        [TestMethod]
        public void ParseOdx_WithParallel_ParsesParallelShape()
        {
            // Arrange
            var filePath = this.CreateOdxWithParallel("WithParallel.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var parallelShape = model.Shapes.OfType<ParallelShapeModel>().FirstOrDefault();
            Assert.IsNotNull(parallelShape, "Should have a Parallel shape");
        }

        #endregion

        #region ParseOdx - Complex Scenarios

        [TestMethod]
        public void ParseOdx_ComplexOrchestration_ParsesAllShapeTypes()
        {
            // Arrange
            var filePath = this.CreateComplexOdxFile("Complex.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            Assert.IsNotNull(model, "Model should not be null");
            Assert.IsTrue(model.Shapes.Count > 0, "Should have shapes");
            Assert.IsNotNull(model.Name, "Should have orchestration name");
        }

        [TestMethod]
        public void ParseOdx_NestedShapes_PreservesHierarchy()
        {
            // Arrange
            var filePath = this.CreateOdxWithNestedShapes("Nested.odx");

            // Act
            var model = BizTalkOrchestrationParser.ParseOdx(filePath);

            // Assert
            var scopeShape = model.Shapes.OfType<ScopeShapeModel>().FirstOrDefault();
            Assert.IsNotNull(scopeShape, "Should have a Scope shape");
            Assert.IsTrue(scopeShape.Children.Count > 0, "Scope should have children");
        }

        #endregion

        #region FindMessageType

        [TestMethod]
        public void FindMessageType_ExistingMessage_ReturnsType()
        {
            // Arrange
            var model = new OrchestrationModel();
            model.Messages.Add(new MessageModel
            {
                Name = "InputMsg",
                Type = "MyCompany.Schemas.OrderRequest"
            });

            // Act
            var result = BizTalkOrchestrationParser.FindMessageType(model, logicalName: "InputMsg");

            // Assert
            Assert.AreEqual("MyCompany.Schemas.OrderRequest", result, "Should return the message type");
        }

        [TestMethod]
        public void FindMessageType_NonExistingMessage_ReturnsLogicalName()
        {
            // Arrange
            var model = new OrchestrationModel();

            // Act
            var result = BizTalkOrchestrationParser.FindMessageType(model, logicalName: "UnknownMsg");

            // Assert
            Assert.AreEqual("UnknownMsg", result, "Should return the logical name when message not found");
        }

        #endregion

        #region Helper Methods

        private string CreateTestFile(string fileName, string content)
        {
            var filePath = Path.Combine(this.testDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        private string CreateSimpleOdxFile(string fileName, string orchestrationName, string namespaceName)
        {
            var content = $@"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='{namespaceName}' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='{orchestrationName}' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name' Value='Receive_Input' />
          <om:Property Name='Activate' Value='True' />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithMessages(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='MessageDeclaration'>
        <om:Property Name='Name' Value='InputMessage' />
        <om:Property Name='Type' Value='MyCompany.Schemas.Request' />
        <om:Property Name='ParamDirection' Value='In' />
      </om:Element>
      <om:Element Type='MessageDeclaration'>
        <om:Property Name='Name' Value='OutputMessage' />
        <om:Property Name='Type' Value='MyCompany.Schemas.Response' />
        <om:Property Name='ParamDirection' Value='Out' />
      </om:Element>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name' Value='Receive1' />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithPorts(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='PortType'>
      <om:Property Name='Name' Value='RequestPortType' />
      <om:Property Name='TypeModifier' Value='Public' />
      <om:Element Type='OperationDeclaration'>
        <om:Property Name='Name' Value='Operation_1' />
        <om:Property Name='OperationType' Value='OneWay' />
      </om:Element>
    </om:Element>
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='PortDeclaration'>
        <om:Property Name='Name' Value='ReceivePort' />
        <om:Property Name='Type' Value='RequestPortType' />
        <om:Property Name='PortModifier' Value='Implements' />
        <om:Property Name='Signal' Value='True' />
        <om:Element Type='LogicalBindingAttribute' />
      </om:Element>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive'>
          <om:Property Name='Name' Value='Receive1' />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithShapes(string fileName, bool includeReceive = false, bool includeSend = false)
        {
            var receiveShape = includeReceive ? @"
        <om:Element Type='Receive' OID='1'>
          <om:Property Name='Name' Value='Receive_Message' />
          <om:Property Name='Activate' Value='True' />
          <om:Property Name='PortName' Value='ReceivePort' />
          <om:Property Name='MessageName' Value='InputMsg' />
        </om:Element>" : "";

            var sendShape = includeSend ? @"
        <om:Element Type='Send' OID='2'>
          <om:Property Name='Name' Value='Send_Message' />
          <om:Property Name='PortName' Value='SendPort' />
          <om:Property Name='MessageName' Value='OutputMsg' />
        </om:Element>" : "";

            var content = $@"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        {receiveShape}
        {sendShape}
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithDecide(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive' OID='1'>
          <om:Property Name='Name' Value='Receive1' />
        </om:Element>
        <om:Element Type='Decision' OID='2'>
          <om:Property Name='Name' Value='CheckCondition' />
          <om:Element Type='DecisionBranch'>
            <om:Property Name='Name' Value='TrueBranch' />
            <om:Property Name='Expression' Value='true' />
          </om:Element>
          <om:Element Type='DecisionBranch'>
            <om:Property Name='Name' Value='FalseBranch' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithLoop(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Loop' OID='1'>
          <om:Property Name='Name' Value='ProcessItems' />
          <om:Element Type='Expression'>
            <om:Property Name='Expression' Value='counter &lt; 10' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithConstruct(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Construct' OID='1'>
          <om:Property Name='Name' Value='ConstructResponse' />
          <om:Element Type='MessageRef'>
            <om:Property Name='Ref' Value='ResponseMsg' />
          </om:Element>
          <om:Element Type='MessageAssignment'>
            <om:Property Name='Name' Value='AssignValues' />
            <om:Property Name='Expression' Value='ResponseMsg = InputMsg;' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithCorrelation(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='CorrelationDeclaration' OID='1'>
          <om:Property Name='Name' Value='OrderCorrelation' />
          <om:Property Name='Type' Value='OrderCorrelationType' />
        </om:Element>
        <om:Element Type='Receive' OID='2'>
          <om:Property Name='Name' Value='Receive1' />
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithScope(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Scope' OID='1'>
          <om:Property Name='Name' Value='TransactionScope' />
          <om:Element Type='Receive' OID='2'>
            <om:Property Name='Name' Value='Receive1' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithParallel(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Parallel' OID='1'>
          <om:Property Name='Name' Value='ParallelActions' />
          <om:Element Type='ParallelBranch' OID='2'>
            <om:Property Name='Name' Value='Branch1' />
          </om:Element>
          <om:Element Type='ParallelBranch' OID='3'>
            <om:Property Name='Name' Value='Branch2' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateComplexOdxFile(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='ComplexNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='ComplexOrchestration' />
      <om:Element Type='MessageDeclaration'>
        <om:Property Name='Name' Value='InputMsg' />
        <om:Property Name='Type' Value='Schema.Input' />
      </om:Element>
      <om:Element Type='ServiceBody'>
        <om:Element Type='Receive' OID='1'>
          <om:Property Name='Name' Value='ReceiveInput' />
          <om:Property Name='Activate' Value='True' />
        </om:Element>
        <om:Element Type='Decision' OID='2'>
          <om:Property Name='Name' Value='CheckValid' />
          <om:Element Type='DecisionBranch'>
            <om:Property Name='Name' Value='Valid' />
            <om:Property Name='Expression' Value='isValid' />
            <om:Element Type='Send' OID='3'>
              <om:Property Name='Name' Value='SendResponse' />
            </om:Element>
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        private string CreateOdxWithNestedShapes(string fileName)
        {
            var content = @"<?xml version='1.0' encoding='utf-8'?>
<om:MetaModel xmlns:om='http://schemas.microsoft.com/BizTalk/2003/DesignerData'>
  <om:Element Type='Module'>
    <om:Property Name='Name' Value='TestNamespace' />
    <om:Element Type='ServiceDeclaration'>
      <om:Property Name='Name' Value='TestOrchestration' />
      <om:Element Type='ServiceBody'>
        <om:Element Type='Scope' OID='1'>
          <om:Property Name='Name' Value='OuterScope' />
          <om:Element Type='Receive' OID='2'>
            <om:Property Name='Name' Value='NestedReceive' />
          </om:Element>
          <om:Element Type='Send' OID='3'>
            <om:Property Name='Name' Value='NestedSend' />
          </om:Element>
        </om:Element>
      </om:Element>
    </om:Element>
  </om:Element>
</om:MetaModel>
#endif";

            return this.CreateTestFile(fileName, content);
        }

        #endregion
    }
}
