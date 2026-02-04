// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;

namespace BizTalktoLogicApps.Tests.Unit
{
    /// <summary>
    /// Unit tests for the WorkflowValidator class.
    /// Tests validation of Logic Apps workflow JSON against schema and best practices.
    /// </summary>
    [TestClass]
    public class WorkflowValidatorTests
    {
        private WorkflowValidator validator;

        [TestInitialize]
        public void Setup()
        {
            this.validator = new WorkflowValidator();
        }

        #region Validate - Empty and Invalid JSON

        [TestMethod]
        public void Validate_NullJson_ReturnsInvalidWithError()
        {
            // Act
            var result = this.validator.Validate(workflowJson: null);

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid for null JSON");
            Assert.IsTrue(result.HasErrors, "Should have errors");
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "EMPTY_WORKFLOW", StringComparison.Ordinal)),
                "Should have EMPTY_WORKFLOW error");
        }

        [TestMethod]
        public void Validate_EmptyString_ReturnsInvalidWithError()
        {
            // Act
            var result = this.validator.Validate(workflowJson: string.Empty);

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid for empty string");
            Assert.IsTrue(result.HasErrors, "Should have errors");
        }

        [TestMethod]
        public void Validate_WhitespaceOnly_ReturnsInvalidWithError()
        {
            // Act
            var result = this.validator.Validate(workflowJson: "   ");

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid for whitespace-only string");
        }

        [TestMethod]
        public void Validate_MalformedJson_ReturnsInvalidWithError()
        {
            // Arrange
            var malformedJson = "{ this is not valid json }";

            // Act
            var result = this.validator.Validate(malformedJson);

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid for malformed JSON");
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "INVALID_JSON", StringComparison.Ordinal)),
                "Should have INVALID_JSON error");
        }

        #endregion

        #region Validate - Structure Validation

        [TestMethod]
        public void Validate_MissingKind_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {},
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_KIND", StringComparison.Ordinal)),
                "Should have MISSING_KIND error");
        }

        [TestMethod]
        public void Validate_InvalidKind_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""InvalidKind"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {},
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "INVALID_KIND", StringComparison.Ordinal)),
                "Should have INVALID_KIND error");
        }

        [TestMethod]
        public void Validate_MissingDefinition_ReturnsError()
        {
            // Arrange
            var json = @"{ ""kind"": ""Stateful"" }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_DEFINITION", StringComparison.Ordinal)),
                "Should have MISSING_DEFINITION error");
        }

        [TestMethod]
        public void Validate_MissingTriggers_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_TRIGGERS", StringComparison.Ordinal)),
                "Should have MISSING_TRIGGERS error");
        }

        [TestMethod]
        public void Validate_MissingActions_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_ACTIONS", StringComparison.Ordinal)),
                "Should have MISSING_ACTIONS error");
        }

        #endregion

        #region Validate - Valid Workflows

        [TestMethod]
        public void Validate_ValidStatefulWorkflow_ReturnsValid()
        {
            // Arrange
            var json = this.CreateValidWorkflowJson(kind: "Stateful");

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsFalse(result.HasErrors, "Should not have errors for valid workflow");
        }

        [TestMethod]
        public void Validate_ValidStatelessWorkflow_ReturnsValid()
        {
            // Arrange
            var json = this.CreateValidWorkflowJson(kind: "Stateless");

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsFalse(result.HasErrors, "Should not have errors for valid workflow");
        }

        #endregion

        #region Validate - Trigger Validation

        [TestMethod]
        public void Validate_NoTriggers_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {},
                    ""actions"": {
                        ""Initialize_variable"": {
                            ""type"": ""InitializeVariable"",
                            ""inputs"": {}
                        }
                    }
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "NO_TRIGGERS", StringComparison.Ordinal)),
                "Should have NO_TRIGGERS error");
        }

        [TestMethod]
        public void Validate_MultipleTriggers_ReturnsWarning()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {
                        ""trigger1"": { ""type"": ""Request"", ""kind"": ""Http"" },
                        ""trigger2"": { ""type"": ""Request"", ""kind"": ""Http"" }
                    },
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MULTIPLE_TRIGGERS", StringComparison.Ordinal)),
                "Should have MULTIPLE_TRIGGERS warning");
        }

        [TestMethod]
        public void Validate_TriggerNameTooLong_ReturnsError()
        {
            // Arrange
            var longTriggerName = new string('a', 81);
            var json = $@"{{
                ""kind"": ""Stateful"",
                ""definition"": {{
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {{
                        ""{longTriggerName}"": {{ ""type"": ""Request"", ""kind"": ""Http"" }}
                    }},
                    ""actions"": {{}}
                }}
            }}";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "TRIGGER_NAME_TOO_LONG", StringComparison.Ordinal)),
                "Should have TRIGGER_NAME_TOO_LONG error");
        }

        [TestMethod]
        public void Validate_TriggerMissingType_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {
                        ""When_a_request_is_received"": { ""kind"": ""Http"" }
                    },
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_TRIGGER_TYPE", StringComparison.Ordinal)),
                "Should have MISSING_TRIGGER_TYPE error");
        }

        #endregion

        #region Validate - Action Validation

        [TestMethod]
        public void Validate_ActionNameTooLong_ReturnsError()
        {
            // Arrange
            var longActionName = new string('b', 81);
            var json = $@"{{
                ""kind"": ""Stateful"",
                ""definition"": {{
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {{
                        ""trigger"": {{ ""type"": ""Request"", ""kind"": ""Http"" }}
                    }},
                    ""actions"": {{
                        ""{longActionName}"": {{ ""type"": ""Compose"", ""inputs"": {{}} }}
                    }}
                }}
            }}";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "ACTION_NAME_TOO_LONG", StringComparison.Ordinal)),
                "Should have ACTION_NAME_TOO_LONG error");
        }

        [TestMethod]
        public void Validate_ActionMissingType_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {
                        ""trigger"": { ""type"": ""Request"", ""kind"": ""Http"" }
                    },
                    ""actions"": {
                        ""MyAction"": { ""inputs"": {} }
                    }
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "MISSING_ACTION_TYPE", StringComparison.Ordinal)),
                "Should have MISSING_ACTION_TYPE error");
        }

        [TestMethod]
        public void Validate_InvalidDependency_ReturnsError()
        {
            // Arrange
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {
                        ""trigger"": { ""type"": ""Request"", ""kind"": ""Http"" }
                    },
                    ""actions"": {
                        ""Action1"": { 
                            ""type"": ""Compose"",
                            ""inputs"": {},
                            ""runAfter"": {
                                ""NonExistentAction"": [""Succeeded""]
                            }
                        }
                    }
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(
                result.Issues.Any(i => string.Equals(i.Code, "INVALID_DEPENDENCY", StringComparison.Ordinal)),
                "Should have INVALID_DEPENDENCY error");
        }

        #endregion

        #region ValidationResult Tests

        [TestMethod]
        public void ValidationResult_GetSummary_ReturnsFormattedString()
        {
            // Arrange
            var json = this.CreateValidWorkflowJson(kind: "Stateful");
            var result = this.validator.Validate(json);

            // Act
            var summary = result.GetSummary();

            // Assert
            Assert.IsNotNull(summary, "Summary should not be null");
            Assert.IsTrue(summary.Contains("error"), "Summary should mention errors");
            Assert.IsTrue(summary.Contains("warning"), "Summary should mention warnings");
        }

        [TestMethod]
        public void ValidationResult_HasErrors_ReturnsTrueWhenErrors()
        {
            // Arrange
            var json = "invalid json";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(result.HasErrors, "HasErrors should be true when there are errors");
        }

        [TestMethod]
        public void ValidationResult_HasWarnings_ReturnsTrueWhenWarnings()
        {
            // Arrange - workflow with multiple triggers generates warning
            var json = @"{
                ""kind"": ""Stateful"",
                ""definition"": {
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {
                        ""trigger1"": { ""type"": ""Request"", ""kind"": ""Http"" },
                        ""trigger2"": { ""type"": ""Request"", ""kind"": ""Http"" }
                    },
                    ""actions"": {}
                }
            }";

            // Act
            var result = this.validator.Validate(json);

            // Assert
            Assert.IsTrue(result.HasWarnings, "HasWarnings should be true when there are warnings");
        }

        #endregion

        #region Helper Methods

        private string CreateValidWorkflowJson(string kind)
        {
            return $@"{{
                ""kind"": ""{kind}"",
                ""definition"": {{
                    ""$schema"": ""https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#"",
                    ""contentVersion"": ""1.0.0.0"",
                    ""triggers"": {{
                        ""When_a_HTTP_request_is_received"": {{
                            ""type"": ""Request"",
                            ""kind"": ""Http"",
                            ""inputs"": {{
                                ""schema"": {{}}
                            }}
                        }}
                    }},
                    ""actions"": {{
                        ""Response"": {{
                            ""type"": ""Response"",
                            ""inputs"": {{
                                ""statusCode"": 200,
                                ""body"": ""OK""
                            }},
                            ""runAfter"": {{}}
                        }}
                    }},
                    ""outputs"": {{}}
                }}
            }}";
        }

        #endregion
    }
}
