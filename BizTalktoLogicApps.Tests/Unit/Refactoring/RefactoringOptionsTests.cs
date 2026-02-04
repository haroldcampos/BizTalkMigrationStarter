// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator.Refactoring;

namespace BizTalktoLogicApps.Tests.Unit.Refactoring
{
    /// <summary>
    /// Unit tests for the RefactoringOptions class.
    /// Tests configuration validation and default values.
    /// </summary>
    [TestClass]
    public class RefactoringOptionsTests
    {
        #region Default Values

        [TestMethod]
        public void RefactoringOptions_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var options = new RefactoringOptions();

            // Assert
            Assert.AreEqual(DeploymentTarget.Cloud, options.Target, "Default target should be Cloud");
            Assert.AreEqual("ServiceBus", options.PreferredMessagingPlatform, "Default messaging should be ServiceBus");
            Assert.AreEqual("Sql", options.PreferredDatabaseConnector, "Default database should be Sql");
            Assert.IsTrue(options.PreferManagedConnectors, "PreferManagedConnectors should default to true");
            Assert.IsTrue(options.SimplifyConvoyPatterns, "SimplifyConvoyPatterns should default to true");
            Assert.IsTrue(options.UseNativeParallelBranches, "UseNativeParallelBranches should default to true");
            Assert.IsTrue(options.ConsolidateNestedScopes, "ConsolidateNestedScopes should default to true");
            Assert.IsTrue(options.OptimizeTransforms, "OptimizeTransforms should default to true");
            Assert.IsTrue(options.IncludePatternComments, "IncludePatternComments should default to true");
            Assert.IsTrue(options.GenerateParametersJson, "GenerateParametersJson should default to true");
            Assert.IsTrue(options.UseDataMapper, "UseDataMapper should default to true");
            Assert.AreEqual(RefactoringStrategy.Balanced, options.Strategy, "Default strategy should be Balanced");
            Assert.IsNull(options.ConnectorRegistryPath, "ConnectorRegistryPath should default to null");
            Assert.AreEqual("2016-06-01", options.SchemaVersion, "Default schema version should be 2016-06-01");
            Assert.AreEqual("Stateful", options.WorkflowType, "Default workflow type should be Stateful");
        }

        #endregion

        #region Validate - Cloud Deployment

        [TestMethod]
        public void Validate_CloudWithServiceBus_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.Cloud,
                PreferredMessagingPlatform = "ServiceBus"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_CloudWithCosmosDb_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.Cloud,
                PreferredDatabaseConnector = "CosmosDb"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_CloudWithRabbitMQ_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.Cloud,
                PreferredMessagingPlatform = "RabbitMQ"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        #endregion

        #region Validate - OnPremises Deployment

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Validate_OnPremisesWithServiceBus_ThrowsInvalidOperationException()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "ServiceBus"
            };

            // Act
            options.Validate();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Validate_OnPremisesWithCosmosDb_ThrowsInvalidOperationException()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredDatabaseConnector = "CosmosDb"
            };

            // Act
            options.Validate();
        }

        [TestMethod]
        public void Validate_OnPremisesWithRabbitMQ_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "RabbitMQ"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_OnPremisesWithKafka_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "Kafka"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_OnPremisesWithSql_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredDatabaseConnector = "Sql",
                PreferredMessagingPlatform = "RabbitMQ"  // Must specify valid on-prem messaging when target is OnPremises
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_OnPremisesWithPostgres_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredDatabaseConnector = "Postgres",
                PreferredMessagingPlatform = "Kafka"  // Must specify valid on-prem messaging when target is OnPremises
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        #endregion

        #region Validate - Workflow Type

        [TestMethod]
        public void Validate_StatefulWorkflowType_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                WorkflowType = "Stateful"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_StatelessWorkflowType_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                WorkflowType = "Stateless"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Validate_InvalidWorkflowType_ThrowsInvalidOperationException()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                WorkflowType = "InvalidType"
            };

            // Act
            options.Validate();
        }

        #endregion

        #region Validate - Case Insensitivity

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Validate_ServiceBusCaseInsensitive_ThrowsForOnPremises()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "SERVICEBUS"
            };

            // Act
            options.Validate();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Validate_CosmosDbCaseInsensitive_ThrowsForOnPremises()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredDatabaseConnector = "cosmosdb"
            };

            // Act
            options.Validate();
        }

        [TestMethod]
        public void Validate_StatefulCaseInsensitive_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                WorkflowType = "STATEFUL"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        [TestMethod]
        public void Validate_StatelessCaseInsensitive_DoesNotThrow()
        {
            // Arrange
            var options = new RefactoringOptions
            {
                WorkflowType = "stateless"
            };

            // Act & Assert - should not throw
            options.Validate();
        }

        #endregion

        #region Property Setters

        [TestMethod]
        public void RefactoringOptions_SetAllProperties_ValuesArePersisted()
        {
            // Arrange & Act
            var options = new RefactoringOptions
            {
                Target = DeploymentTarget.OnPremises,
                PreferredMessagingPlatform = "RabbitMQ",
                PreferredDatabaseConnector = "Postgres",
                PreferManagedConnectors = false,
                SimplifyConvoyPatterns = false,
                UseNativeParallelBranches = false,
                ConsolidateNestedScopes = false,
                OptimizeTransforms = false,
                IncludePatternComments = false,
                GenerateParametersJson = false,
                UseDataMapper = false,
                Strategy = RefactoringStrategy.Aggressive,
                ConnectorRegistryPath = "/path/to/registry.json",
                SchemaVersion = "2020-05-01",
                WorkflowType = "Stateless"
            };

            // Assert
            Assert.AreEqual(DeploymentTarget.OnPremises, options.Target);
            Assert.AreEqual("RabbitMQ", options.PreferredMessagingPlatform);
            Assert.AreEqual("Postgres", options.PreferredDatabaseConnector);
            Assert.IsFalse(options.PreferManagedConnectors);
            Assert.IsFalse(options.SimplifyConvoyPatterns);
            Assert.IsFalse(options.UseNativeParallelBranches);
            Assert.IsFalse(options.ConsolidateNestedScopes);
            Assert.IsFalse(options.OptimizeTransforms);
            Assert.IsFalse(options.IncludePatternComments);
            Assert.IsFalse(options.GenerateParametersJson);
            Assert.IsFalse(options.UseDataMapper);
            Assert.AreEqual(RefactoringStrategy.Aggressive, options.Strategy);
            Assert.AreEqual("/path/to/registry.json", options.ConnectorRegistryPath);
            Assert.AreEqual("2020-05-01", options.SchemaVersion);
            Assert.AreEqual("Stateless", options.WorkflowType);
        }

        #endregion

        #region Enum Values

        [TestMethod]
        public void DeploymentTarget_HasExpectedValues()
        {
            // Assert
            Assert.IsTrue(Enum.IsDefined(typeof(DeploymentTarget), DeploymentTarget.Cloud));
            Assert.IsTrue(Enum.IsDefined(typeof(DeploymentTarget), DeploymentTarget.OnPremises));
        }

        [TestMethod]
        public void RefactoringStrategy_HasExpectedValues()
        {
            // Assert
            Assert.IsTrue(Enum.IsDefined(typeof(RefactoringStrategy), RefactoringStrategy.Conservative));
            Assert.IsTrue(Enum.IsDefined(typeof(RefactoringStrategy), RefactoringStrategy.Balanced));
            Assert.IsTrue(Enum.IsDefined(typeof(RefactoringStrategy), RefactoringStrategy.Aggressive));
        }

        #endregion
    }
}
