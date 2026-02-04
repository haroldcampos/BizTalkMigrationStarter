// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    [TestClass]
    public class ReceivePatternAnalysisTests
    {
        [TestMethod]
        [Owner("github-hcampos")]
        public void AnalyzeReceives_NoActivatingReceives_ReturnsCallablePattern()
        {
            // Arrange
            var model = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "Test"
            };

            // Add a non-activating receive
            var receive = new ReceiveShapeModel
            {
                Name = "Receive_Response",
                Activate = false,
                PortName = "ResponsePort"
            };
            model.Shapes.Add(receive);

            // Act
            var analysis = BizTalkOrchestrationParser.AnalyzeReceives(model);

            // Assert
            Assert.AreEqual(ReceivePattern.Callable, analysis.Pattern);
            Assert.IsTrue(analysis.RequiresRequestTrigger);
            Assert.IsTrue(analysis.MigrationWarnings.Count > 0);
        }

        [TestMethod]
        public void AnalyzeReceives_SingleActivatingReceive_ReturnsSingleTriggerPattern()
        {
            // Arrange
            var model = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "Test"
            };

            var receive = new ReceiveShapeModel
            {
                Name = "Receive_Order",
                Activate = true,
                PortName = "OrderPort"
            };
            model.Shapes.Add(receive);

            // Act
            var analysis = BizTalkOrchestrationParser.AnalyzeReceives(model);

            // Assert
            Assert.AreEqual(ReceivePattern.SingleTrigger, analysis.Pattern);
            Assert.IsNotNull(analysis.PrimaryReceive);
            Assert.AreEqual("Receive_Order", analysis.PrimaryReceive.Name);
            Assert.AreEqual(0, analysis.SecondaryReceives.Count);
            Assert.IsTrue(analysis.IsValid);
        }

        [TestMethod]
        public void AnalyzeReceives_ConvoyPattern_ReturnsConvoyWithSessionSupport()
        {
            // Arrange
            var model = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "Test"
            };

            // First receive initializes correlation
            var receive1 = new ReceiveShapeModel
            {
                Name = "Receive_OrderHeader",
                Activate = true,
                PortName = "OrderPort"
            };
            receive1.InitializesCorrelationSets.Add("OrderCorrelation");
            model.Shapes.Add(receive1);

            // Second receive follows correlation
            var receive2 = new ReceiveShapeModel
            {
                Name = "Receive_OrderLine",
                Activate = false,
                PortName = "OrderPort"
            };
            receive2.FollowsCorrelationSets.Add("OrderCorrelation");
            model.Shapes.Add(receive2);

            // Act
            var analysis = BizTalkOrchestrationParser.AnalyzeReceives(model);

            // Assert
            Assert.AreEqual(ReceivePattern.Convoy, analysis.Pattern);
            Assert.IsNotNull(analysis.PrimaryReceive);
            Assert.AreEqual("Receive_OrderHeader", analysis.PrimaryReceive.Name);
            Assert.AreEqual(1, analysis.SecondaryReceives.Count);
            Assert.AreEqual("Receive_OrderLine", analysis.SecondaryReceives[0].Name);
            Assert.IsTrue(analysis.RequiresSessionSupport);
            Assert.IsTrue(analysis.IsValid);
        }

        [TestMethod]
        public void AnalyzeReceives_ListenPattern_ReturnsListenFirstToComplete()
        {
            // Arrange
            var model = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "Test"
            };

            var listen = new ListenShapeModel
            {
                Name = "Listen_For_Messages",
                Sequence = 0
            };

            // Two activating receives in Listen branches
            var receive1 = new ReceiveShapeModel
            {
                Name = "Receive_File",
                Activate = true,
                PortName = "FilePort",
                Parent = listen
            };

            var receive2 = new ReceiveShapeModel
            {
                Name = "Receive_Timeout",
                Activate = true,
                PortName = "TimeoutPort",
                Parent = listen
            };

            listen.Branches.Add(receive1);
            listen.Branches.Add(receive2);
            model.Shapes.Add(listen);

            // Act
            var analysis = BizTalkOrchestrationParser.AnalyzeReceives(model);

            // Assert
            Assert.AreEqual(ReceivePattern.ListenFirstToComplete, analysis.Pattern);
            Assert.IsNotNull(analysis.PrimaryReceive);
            Assert.AreEqual(1, analysis.SecondaryReceives.Count);
            Assert.IsTrue(analysis.RequiresTimeoutHandling);
            Assert.IsTrue(analysis.IsValid);
        }

        [TestMethod]
        public void AnalyzeReceives_MultipleSequentialActivating_ReturnsInvalidPattern()
        {
            // Arrange
            var model = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "Test"
            };

            // Two sequential activating receives (INVALID)
            var receive1 = new ReceiveShapeModel
            {
                Name = "Receive_First",
                Activate = true,
                PortName = "Port1"
            };
            model.Shapes.Add(receive1);

            var receive2 = new ReceiveShapeModel
            {
                Name = "Receive_Second",
                Activate = true,
                PortName = "Port2"
            };
            model.Shapes.Add(receive2);

            // Act
            var analysis = BizTalkOrchestrationParser.AnalyzeReceives(model);

            // Assert
            Assert.AreEqual(ReceivePattern.Invalid, analysis.Pattern);
            Assert.IsFalse(analysis.IsValid);
            Assert.IsNotNull(analysis.MigrationError);
            Assert.IsTrue(analysis.MigrationError.Contains("INVALID PATTERN"));
        }

        [TestMethod]
        public void ReceivePatternAnalysis_PropertiesWork_Correctly()
        {
            // Arrange
            var analysis = new ReceivePatternAnalysis
            {
                Pattern = ReceivePattern.SingleTrigger,
                PrimaryReceive = new ReceiveShapeModel { Name = "Receive1" }
            };
            analysis.SecondaryReceives.Add(new ReceiveShapeModel { Name = "Receive2" });
            analysis.SecondaryReceives.Add(new ReceiveShapeModel { Name = "Receive3" });

            // Assert
            Assert.AreEqual(3, analysis.TotalReceiveCount);
            Assert.IsTrue(analysis.IsValid);
        }
    }
}
