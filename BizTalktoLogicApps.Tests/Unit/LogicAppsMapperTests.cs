// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BizTalktoLogicApps.Tests.Unit
{
    /// <summary>
    /// Unit tests for LogicAppsMapper orchestration-to-workflow mapping logic.
    /// Tests shape conversion, message flow wiring, trigger selection, and condition inversion.
    /// </summary>
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
            Assert.IsNotNull(value: result, message: "Should return a workflow map");
            Assert.AreEqual(expected: "MyCompany.Orchestrations.TestOrchestration", actual: result.Name);
            Assert.IsTrue(condition: result.Triggers.Count > 0, message: "Should have at least one trigger");
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
            Assert.IsNotNull(value: result);
            Assert.IsTrue(condition: result.Triggers.Count > 0);
            var trigger = result.Triggers.First();
            Assert.AreEqual(expected: "Request", actual: trigger.Kind, message: "Callable workflow should have Request trigger");
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
            Assert.IsNotNull(value: result);
            Assert.IsTrue(condition: result.Count > 0, message: "Should create at least one workflow");
            var workflow = result.First();
            Assert.IsTrue(condition: workflow.Triggers.Count > 0, message: "Workflow should have triggers");
            Assert.IsTrue(condition: workflow.Actions.Count > 0, message: "Workflow should have actions from send ports");
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
            Assert.IsNotNull(value: result);
            var untilActions = result.Actions.Where(a => a.Type == "Until").ToList();
            Assert.IsTrue(condition: untilActions.Count > 0,
                message: "Self-recursive call should be converted to Until loop");
            Assert.IsTrue(condition: untilActions.Any(a => a.Details.Contains("Self-recursive call detected")),
                message: "Until action should contain warning comment about self-recursion");
        }

        [TestMethod]
        public void MapToLogicApp_WithTransformAndSend_WiresMessageFlow()
        {
            // Arrange: Receive(Order_In) -> Transform(In=Order_In, Out=Invoice_Out) -> Send(Invoice_Out)
            var orchestration = new OrchestrationModel
            {
                Name = "OrderToInvoice",
                Namespace = "MyCompany.Orchestrations"
            };

            // Activation receive produces Order_In
            var receiveShape = new ReceiveShapeModel
            {
                Name = "ReceiveOrder",
                ShapeType = "Receive",
                Sequence = 0,
                Activate = true,
                MessageName = "Order_In",
                PortName = "ReceivePort",
                UniqueId = "00000001"
            };
            orchestration.Shapes.Add(receiveShape);

            // Transform consumes Order_In, produces Invoice_Out
            var transformShape = new TransformShapeModel
            {
                Name = "MapOrderToInvoice",
                ShapeType = "Transform",
                Sequence = 1,
                ClassName = "MyCompany.Maps.OrderToInvoiceMap",
                UniqueId = "00000002"
            };
            transformShape.InputMessages.Add("Order_In");
            transformShape.OutputMessages.Add("Invoice_Out");

            // Wrap transform in a Construct
            var constructShape = new ConstructShapeModel
            {
                Name = "ConstructInvoice",
                ShapeType = "Construct",
                Sequence = 1,
                UniqueId = "00000003"
            };
            constructShape.ConstructedMessages.Add("Invoice_Out");
            constructShape.InnerShapes.Add(transformShape);
            orchestration.Shapes.Add(constructShape);

            // Send uses Invoice_Out
            var sendShape = new SendShapeModel
            {
                Name = "SendInvoice",
                ShapeType = "Send",
                Sequence = 2,
                MessageName = "Invoice_Out",
                PortName = "SendPort",
                UniqueId = "00000004"
            };
            orchestration.Shapes.Add(sendShape);

            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "HttpReceive",
                Enabled = true,
                TransportType = "HTTP",
                Address = "http://localhost/orders"
            });

            // Act
            var result = LogicAppsMapper.MapToLogicApp(orchestration, binding);

            // Assert
            Assert.IsNotNull(value: result);

            // Find the transform action
            var xsltAction = result.Actions.FirstOrDefault(a => a.Type == "Xslt");
            Assert.IsNotNull(value: xsltAction, message: "Should have an Xslt action from the Transform shape");
            Assert.AreEqual(expected: "Invoice_Out", actual: xsltAction.OutputMessageName,
                message: "Transform action should track its output message name");

            // Find the send action from the orchestration shapes (not binding send ports)
            var sendAction = result.Actions.FirstOrDefault(a =>
                a.Type == "SendConnector" && a.OutputMessageName == "Invoice_Out");
            Assert.IsNotNull(value: sendAction, message: "Should have a SendConnector action with OutputMessageName=Invoice_Out");

            // The send action should reference the transform action's output, not triggerBody()
            Assert.IsNotNull(value: sendAction.InputMessageSourceAction,
                message: "Send action should have InputMessageSourceAction set (not null/triggerBody). Actual: " + (sendAction.InputMessageSourceAction ?? "null"));
            Assert.IsTrue(condition: sendAction.InputMessageSourceAction.Contains("ConstructInvoice"),
                message: "Send action should reference the construct/transform action that produced Invoice_Out. Actual InputMessageSourceAction: " + sendAction.InputMessageSourceAction);
        }

        [TestMethod]
        public void MapToLogicApp_WithNoTransform_SendUsesTriggerBody()
        {
            // Arrange: Receive(Msg_In) -> Send(Msg_In) — no transform, same message
            var orchestration = new OrchestrationModel
            {
                Name = "PassThrough",
                Namespace = "MyCompany.Orchestrations"
            };

            var receiveShape = new ReceiveShapeModel
            {
                Name = "ReceiveMsg",
                ShapeType = "Receive",
                Sequence = 0,
                Activate = true,
                MessageName = "Msg_In",
                PortName = "ReceivePort",
                UniqueId = "00000001"
            };
            orchestration.Shapes.Add(receiveShape);

            var sendShape = new SendShapeModel
            {
                Name = "SendMsg",
                ShapeType = "Send",
                Sequence = 1,
                MessageName = "Msg_In",
                PortName = "SendPort",
                UniqueId = "00000002"
            };
            orchestration.Shapes.Add(sendShape);

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
            var sendAction = result.Actions.FirstOrDefault(a =>
                a.Type == "SendConnector" && a.OutputMessageName == "Msg_In");
            Assert.IsNotNull(value: sendAction, message: "Should have a SendConnector for Msg_In");

            // Msg_In is produced by the activation receive, so InputMessageSourceAction should be null
            // (null = @triggerBody())
            Assert.IsNull(value: sendAction.InputMessageSourceAction,
                message: "Send of activation message should have null InputMessageSourceAction (falls back to triggerBody)");
        }

        [TestMethod]
        public void ConvertShape_ListenShape_ProducesListenContainerType()
        {
            // Arrange: A Listen shape with two branches (Receive vs Delay)
            var orchestration = new OrchestrationModel
            {
                Name = "ListenOrchestration",
                Namespace = "MyCompany.Orchestrations"
            };

            // Activation receive (skipped by mapper)
            orchestration.Shapes.Add(new ReceiveShapeModel
            {
                Name = "ActivationReceive",
                ShapeType = "Receive",
                Sequence = 0,
                Activate = true,
                MessageName = "Msg_In",
                PortName = "ReceivePort",
                UniqueId = "00000001"
            });

            var listenShape = new ListenShapeModel
            {
                Name = "WaitForResponseOrTimeout",
                ShapeType = "Listen",
                Sequence = 1,
                UniqueId = "00000010"
            };

            // Branch 1: Receive response
            var receiveBranch = new TaskShapeModel
            {
                Name = "ResponseBranch",
                ShapeType = "Task",
                Sequence = 0
            };
            receiveBranch.Children.Add(new ReceiveShapeModel
            {
                Name = "ReceiveResponse",
                ShapeType = "Receive",
                Sequence = 0,
                Activate = false,
                MessageName = "Response_Msg",
                PortName = "ResponsePort",
                UniqueId = "00000011"
            });
            listenShape.Children.Add(receiveBranch);

            // Branch 2: Delay timeout
            var timeoutBranch = new TaskShapeModel
            {
                Name = "TimeoutBranch",
                ShapeType = "Task",
                Sequence = 1
            };
            timeoutBranch.Children.Add(new DelayShapeModel
            {
                Name = "TimeoutDelay",
                ShapeType = "Delay",
                Sequence = 0,
                DelayExpression = "PT5M",
                UniqueId = "00000012"
            });
            listenShape.Children.Add(timeoutBranch);

            orchestration.Shapes.Add(listenShape);

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
            Assert.IsNotNull(value: result);

            // The Listen shape should produce a ListenContainer, NOT a ParallelContainer
            var listenActions = result.Actions.Where(a => a.Type == "ListenContainer").ToList();
            Assert.IsTrue(condition: listenActions.Count > 0,
                message: "Listen shape should produce ListenContainer type, not ParallelContainer. " +
                "Actual types: " + string.Join(", ", result.Actions.Select(a => a.Type)));

            var listenAction = listenActions.First();
            Assert.IsTrue(condition: listenAction.Details.Contains("WARNING"),
                message: "ListenContainer should contain a migration warning about first-one-wins semantics");
            Assert.IsTrue(condition: listenAction.Details.Contains("MANUAL REVIEW"),
                message: "ListenContainer should require manual review");
        }

        [TestMethod]
        public void MapToLogicApp_WithSendShape_DoesNotDuplicateFromBindings()
        {
            // Arrange: Orchestration with a Send shape bound to a port named "SendPort"
            var orchestration = new OrchestrationModel
            {
                Name = "NoDuplicateSends",
                Namespace = "MyCompany.Orchestrations"
            };

            orchestration.Shapes.Add(new ReceiveShapeModel
            {
                Name = "ReceiveMsg",
                ShapeType = "Receive",
                Sequence = 0,
                Activate = true,
                MessageName = "Msg_In",
                PortName = "ReceivePort",
                UniqueId = "00000001"
            });

            orchestration.Shapes.Add(new SendShapeModel
            {
                Name = "SendOutput",
                ShapeType = "Send",
                Sequence = 1,
                MessageName = "Msg_In",
                PortName = "OutputSendPort",
                UniqueId = "00000002"
            });

            var binding = new BindingSnapshot();
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "HttpReceive",
                Enabled = true,
                TransportType = "HTTP",
                Address = "http://localhost/test"
            });

            // Add a matching send port in the binding
            binding.SendPorts.Add(new BindingSendPort
            {
                Name = "OutputSendPort",
                TransportType = "FILE",
                Address = "C:\\out\\output.xml"
            });

            // Act
            var result = LogicAppsMapper.MapToLogicApp(orchestration, binding);

            // Assert: Should NOT have two SendConnector actions for the same logical send
            var sendActions = result.Actions.Where(a => a.Type == "SendConnector").ToList();

            // Before the fix, this would be 2 (one from shape, one from binding).
            // After the fix, the binding enriches the shape-based action OR adds only unmatched ports.
            // The key invariant: no duplicate send actions for the same port.
            var distinctNames = sendActions.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Assert.AreEqual(expected: sendActions.Count, actual: distinctNames,
                message: "Should not have duplicate SendConnector actions. Found: " +
                string.Join(", ", sendActions.Select(a => a.Name + "(" + a.ConnectorKind + ")")));
        }

        [TestMethod]
        public void InvertCondition_CompoundAnd_AppliesDeMorgansLaw()
        {
            // Arrange: A While shape with compound && condition
            var orchestration = new OrchestrationModel
            {
                Name = "CompoundWhileTest",
                Namespace = "MyCompany.Orchestrations"
            };

            orchestration.Shapes.Add(new WhileShapeModel
            {
                Name = "WhileLoop",
                ShapeType = "While",
                Sequence = 0,
                Expression = "counter < 10 && flag == true",
                UniqueId = "00000001"
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
            var untilAction = result.Actions.FirstOrDefault(a => a.Type == "Until");
            Assert.IsNotNull(value: untilAction, message: "While shape should produce an Until action");

            // The inverted expression should use De Morgan's law, NOT !()
            // !(A && B) -> (!A || !B) -> "counter >= 10 || flag != true"
            Assert.IsFalse(condition: untilAction.Details.Contains("!("),
                message: "Inverted compound expression should NOT use !() wrapper (ExpressionMapper can't parse it). " +
                "Actual: " + untilAction.Details);

            // Should contain the individual inverted operators
            Assert.IsTrue(condition: untilAction.Details.Contains(">=") || untilAction.Details.Contains("!=") || untilAction.Details.Contains("||"),
                message: "Inverted compound expression should use De Morgan's law with individual operator inversions. " +
                "Actual: " + untilAction.Details);
        }

        [TestMethod]
        public void InvertCondition_ParenthesizedCompound_RespectsGrouping()
        {
            // Arrange: "(a < 5 && b > 3) || c == 1"
            // The top-level operator is ||, so De Morgan should produce:
            // !(a < 5 && b > 3) && !(c == 1)
            // = (a >= 5 || b <= 3) && c != 1
            // Key: the inner "&&" must NOT be split at the top level.
            var orchestration = new OrchestrationModel
            {
                Name = "ParenGroupTest",
                Namespace = "MyCompany.Orchestrations"
            };

            orchestration.Shapes.Add(new WhileShapeModel
            {
                Name = "WhileLoop",
                ShapeType = "While",
                Sequence = 0,
                Expression = "(a < 5 && b > 3) || c == 1",
                UniqueId = "00000001"
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
            var untilAction = result.Actions.FirstOrDefault(a => a.Type == "Until");
            Assert.IsNotNull(value: untilAction, message: "While shape should produce an Until action");

            // The top-level || should be inverted to &&
            Assert.IsTrue(condition: untilAction.Details.Contains("&&"),
                message: "Top-level || should be inverted to && via De Morgan's law. Actual: " + untilAction.Details);

            // The inner (a < 5 && b > 3) group should be inverted as a unit
            // producing (a >= 5 || b <= 3) — the || proves inner && was handled
            Assert.IsTrue(condition: untilAction.Details.Contains("||"),
                message: "Inner && group should be inverted to || via De Morgan's law. Actual: " + untilAction.Details);

            // c == 1 should be inverted to c != 1
            Assert.IsTrue(condition: untilAction.Details.Contains("!="),
                message: "c == 1 should be inverted to c != 1. Actual: " + untilAction.Details);
        }

        [TestMethod]
        public void InvertCondition_TernaryAnd_InvertsAllThreeParts()
        {
            // Arrange: "a < 5 && b > 3 && c == 1" (3-way &&)
            // De Morgan: a >= 5 || b <= 3 || c != 1
            var orchestration = new OrchestrationModel
            {
                Name = "TernaryAndTest",
                Namespace = "MyCompany.Orchestrations"
            };

            orchestration.Shapes.Add(new WhileShapeModel
            {
                Name = "WhileLoop",
                ShapeType = "While",
                Sequence = 0,
                Expression = "a < 5 && b > 3 && c == 1",
                UniqueId = "00000001"
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
            var untilAction = result.Actions.FirstOrDefault(a => a.Type == "Until");
            Assert.IsNotNull(value: untilAction, message: "While shape should produce an Until action");

            // All three parts should be inverted and joined with ||
            Assert.IsTrue(condition: untilAction.Details.Contains(">="),
                message: "a < 5 should invert to a >= 5. Actual: " + untilAction.Details);
            Assert.IsTrue(condition: untilAction.Details.Contains("<="),
                message: "b > 3 should invert to b <= 3. Actual: " + untilAction.Details);
            Assert.IsTrue(condition: untilAction.Details.Contains("!="),
                message: "c == 1 should invert to c != 1. Actual: " + untilAction.Details);

            // Should NOT use !() wrapper
            Assert.IsFalse(condition: untilAction.Details.Contains("!("),
                message: "Should not use !() wrapper. Actual: " + untilAction.Details);
        }
    }
}
