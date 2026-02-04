// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using System.Linq;

namespace BizTalktoLogicApps.Tests.Unit
{
    [TestClass]
    public class LogicAppsMapperTests
    {
        [TestMethod]
        public void MapToLogicApp_WithValidOrchestrationAndBinding_ReturnsWorkflowMap()
        {
            // Arrange
            var orchestration = new OrchestrationModel
            {
                Name = "TestOrchestration",
                Namespace = "MyCompany.Orchestrations"
            };

            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "TestReceiveLocation",
                Enabled = true,
                TransportType = "HTTP",
                Address = "http://localhost/test"
            });

            // Act
            var result = LogicAppsMapper.MapToLogicApp(orchestration, binding);

            // Assert
            Assert.IsNotNull(result, "Should return a workflow map");
            Assert.AreEqual("MyCompany.Orchestrations.TestOrchestration", result.Name);
            Assert.IsTrue(result.Triggers.Count > 0, "Should have at least one trigger");
        }

        [TestMethod]
        public void MapToLogicApp_WithCallableFlag_ForcesRequestTrigger()
        {
            // Arrange
            var orchestration = new OrchestrationModel
            {
                Name = "CallableOrchestration",
                Namespace = "MyCompany.Orchestrations"
            };

            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "FileReceiveLocation",
                Enabled = true,
                TransportType = "FILE",
                Address = "C:\\temp\\*.xml"
            });

            // Act
            var result = LogicAppsMapper.MapToLogicApp(orchestration, binding, isCallable: true);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Triggers.Count > 0);
            var trigger = result.Triggers.First();
            Assert.AreEqual("Request", trigger.Kind, "Callable workflow should have Request trigger");
        }

        [TestMethod]
        public void MapBindingsToWorkflows_WithReceiveLocations_CreatesWorkflows()
        {
            // Arrange
            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "ReceiveLocation1",
                Enabled = true,
                TransportType = "FILE",
                Address = "C:\\temp\\in\\*.xml",
                ReceivePortName = "ReceivePort1"
            });
            
            var sendPort = new BindingSendPort
            {
                Name = "SendPort1",
                TransportType = "FILE",
                Address = "C:\\temp\\out\\output.xml"
            };
            sendPort.Filters.Add(new FilterCondition
            {
                Property = "BTS.ReceivePortName",
                Operator = "0",
                Value = "ReceivePort1"
            });
            binding.SendPorts.Add(sendPort);

            // Act
            var result = LogicAppsMapper.MapBindingsToWorkflows(binding);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count > 0, "Should create at least one workflow");
            var workflow = result.First();
            Assert.IsTrue(workflow.Triggers.Count > 0, "Workflow should have triggers");
            Assert.IsTrue(workflow.Actions.Count > 0, "Workflow should have actions from send ports");
        }

        [TestMethod]
        public void MapToLogicApp_WithSelfRecursiveCall_ConvertsToUntilLoop()
        {
            // Arrange
            var orchestration = new OrchestrationModel
            {
                Name = "RecursiveOrchestration",
                Namespace = "MyCompany.Orchestrations"
            };
            
            orchestration.Shapes.Add(new CallShapeModel
            {
                Name = "SelfCall",
                ShapeType = "Call",
                Invokee = "RecursiveOrchestration",
                Sequence = 0
            });

            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "HttpReceive",
                Enabled = true,
                TransportType = "HTTP",
                Address = "http://localhost/test"
            });

            // Act
            var result = LogicAppsMapper.MapToLogicApp(orchestration, binding);

            // Assert
            Assert.IsNotNull(result);
            var untilActions = result.Actions.Where(a => a.Type == "Until").ToList();
            Assert.IsTrue(untilActions.Count > 0, 
                "Self-recursive call should be converted to Until loop");
            Assert.IsTrue(untilActions.Any(a => a.Details.Contains("Self-recursive call detected")),
                "Until action should contain warning comment about self-recursion");
        }
    }
}
