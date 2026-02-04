// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    /// <summary>
    /// Tests thread-safety of LogicAppsMapper after ThreadLocal fix.
    /// Verifies that concurrent orchestration processing doesn't cause race conditions.
    /// </summary>
    [TestClass]
    public class LogicAppsMapperThreadSafetyTests
    {
        /// <summary>
        /// Verifies that processing two orchestrations concurrently doesn't cause
        /// race conditions in self-recursion detection or workflow generation.
        /// This test validates the ThreadLocal&lt;T&gt; fix for static mutable state.
        /// </summary>
        [TestMethod]
        public void MapToLogicApp_ConcurrentExecution_ThreadSafe()
        {
            // Arrange - Create two different orchestrations with different names
            var orchestration1 = CreateTestOrchestration("OrderProcessing");
            var orchestration2 = CreateTestOrchestration("InventoryManagement");
            var binding = CreateTestBinding();
            
            LogicAppWorkflowMap result1 = null;
            LogicAppWorkflowMap result2 = null;
            Exception exception1 = null;
            Exception exception2 = null;
            
            // Act - Process two orchestrations concurrently on different threads
            var task1 = Task.Run(() =>
            {
                try
                {
                    result1 = LogicAppsMapper.MapToLogicApp(orchestration1, binding);
                }
                catch (Exception ex)
                {
                    exception1 = ex;
                }
            });
            
            var task2 = Task.Run(() =>
            {
                try
                {
                    // Add small delay to increase chance of concurrent execution
                    Thread.Sleep(10);
                    result2 = LogicAppsMapper.MapToLogicApp(orchestration2, binding);
                }
                catch (Exception ex)
                {
                    exception2 = ex;
                }
            });
            
            Task.WaitAll(task1, task2);
            
            // Assert - Both orchestrations should complete successfully
            Assert.IsNull(exception1, "First orchestration should map without error: " + (exception1?.Message ?? ""));
            Assert.IsNull(exception2, "Second orchestration should map without error: " + (exception2?.Message ?? ""));
            Assert.IsNotNull(result1, "First result should not be null");
            Assert.IsNotNull(result2, "Second result should not be null");
            
            // Verify orchestration names are preserved correctly (no cross-contamination)
            Assert.IsTrue(result1.Name.Contains("OrderProcessing"), 
                "First orchestration name should be preserved, got: " + result1.Name);
            Assert.IsTrue(result2.Name.Contains("InventoryManagement"), 
                "Second orchestration name should be preserved, got: " + result2.Name);
            
            // Verify workflows are independent
            Assert.AreNotEqual(result1.Name, result2.Name, 
                "Workflows should have different names");
        }

        /// <summary>
        /// Stress test with many concurrent orchestration processing requests.
        /// Simulates MCP server handling multiple simultaneous conversion requests.
        /// </summary>
        [TestMethod]
        public void MapToLogicApp_HighConcurrency_ThreadSafe()
        {
            // Arrange
            var binding = CreateTestBinding();
            var orchestrationNames = new[] 
            { 
                "OrderProcessing", "InventoryManagement", "Shipping", 
                "Billing", "CustomerService", "Reporting", 
                "DataSync", "Notifications", "Audit", "Monitoring" 
            };
            
            var tasks = new Task<LogicAppWorkflowMap>[orchestrationNames.Length];
            var results = new LogicAppWorkflowMap[orchestrationNames.Length];
            var exceptions = new Exception[orchestrationNames.Length];
            
            // Act - Process 10 orchestrations concurrently
            for (int i = 0; i < orchestrationNames.Length; i++)
            {
                var index = i;
                var orchName = orchestrationNames[i];
                
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        var orch = CreateTestOrchestration(orchName);
                        return LogicAppsMapper.MapToLogicApp(orch, binding);
                    }
                    catch (Exception ex)
                    {
                        exceptions[index] = ex;
                        return null;
                    }
                });
            }
            
            Task.WaitAll(tasks);
            
            // Collect results
            for (int i = 0; i < tasks.Length; i++)
            {
                results[i] = tasks[i].Result;
            }
            
            // Assert - All orchestrations should complete successfully
            for (int i = 0; i < orchestrationNames.Length; i++)
            {
                Assert.IsNull(exceptions[i], 
                    string.Format("Orchestration '{0}' should map without error: {1}", 
                        orchestrationNames[i], 
                        exceptions[i]?.Message ?? ""));
                
                Assert.IsNotNull(results[i], 
                    string.Format("Result for '{0}' should not be null", orchestrationNames[i]));
                
                Assert.IsTrue(results[i].Name.Contains(orchestrationNames[i]), 
                    string.Format("Orchestration '{0}' name should be preserved in result, got: {1}", 
                        orchestrationNames[i], 
                        results[i].Name));
            }
            
            // Verify all results are unique
            for (int i = 0; i < results.Length; i++)
            {
                for (int j = i + 1; j < results.Length; j++)
                {
                    Assert.AreNotEqual(results[i].Name, results[j].Name, 
                        string.Format("Workflows {0} and {1} should have different names", i, j));
                }
            }
        }

        /// <summary>
        /// Creates a minimal test orchestration with the specified name.
        /// </summary>
        private OrchestrationModel CreateTestOrchestration(string name)
        {
            return new OrchestrationModel
            {
                Namespace = "BizTalk.TestProject",
                Name = name
            };
        }

        /// <summary>
        /// Creates a minimal test binding snapshot with one receive location.
        /// </summary>
        private BindingSnapshot CreateTestBinding()
        {
            var binding = new BindingSnapshot();
            
            binding.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "TestReceiveLocation",
                TransportType = "FILE",
                Address = "C:\\Temp\\Input\\*.xml",
                Enabled = true
            });
            
            return binding;
        }
    }
}
