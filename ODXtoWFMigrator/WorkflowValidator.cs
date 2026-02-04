// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Validates generated Logic Apps workflow JSON against schema and best practices.
    /// C# 7.3 / .NET Framework 4.7.2 compatible
    /// </summary>
    public class WorkflowValidator
    {
        private readonly List<ValidationIssue> _issues;

        public WorkflowValidator()
        {
            _issues = new List<ValidationIssue>();
        }

        public ValidationResult Validate(string workflowJson)
        {
            _issues.Clear();

            if (string.IsNullOrWhiteSpace(workflowJson))
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "EMPTY_WORKFLOW",
                    Message = "Workflow JSON is empty or null"
                });
                return new ValidationResult { Issues = _issues, IsValid = false };
            }

            JObject workflow;
            try
            {
                workflow = JObject.Parse(workflowJson);
            }
            catch (JsonException ex)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "INVALID_JSON",
                    Message = "Workflow JSON is malformed: " + ex.Message
                });
                return new ValidationResult { Issues = _issues, IsValid = false };
            }

            // Perform validation checks
            ValidateStructure(workflow);
            ValidateDefinition(workflow);
            ValidateTriggers(workflow);
            ValidateActions(workflow);
            ValidateBestPractices(workflow);

            var hasErrors = _issues.Any(i => i.Severity == IssueSeverity.Error);
            return new ValidationResult
            {
                Issues = _issues,
                IsValid = !hasErrors
            };
        }

        private void ValidateStructure(JObject workflow)
        {
            // Check required top-level properties
            if (workflow["kind"] == null)
            {
                AddError("MISSING_KIND", "Workflow is missing required 'kind' property");
            }
            else
            {
                var kind = workflow["kind"].Value<string>();
                if (kind != "Stateful" && kind != "Stateless")
                {
                    AddError("INVALID_KIND", "Workflow 'kind' must be 'Stateful' or 'Stateless', got: " + kind);
                }
            }

            if (workflow["definition"] == null)
            {
                AddError("MISSING_DEFINITION", "Workflow is missing required 'definition' property");
                return;
            }

            var definition = workflow["definition"] as JObject;
            if (definition == null)
            {
                AddError("INVALID_DEFINITION", "Workflow 'definition' must be an object");
                return;
            }

            // Check required definition properties
            if (definition["$schema"] == null)
            {
                AddWarning("MISSING_SCHEMA", "Workflow definition is missing '$schema' property");
            }

            if (definition["contentVersion"] == null)
            {
                AddWarning("MISSING_VERSION", "Workflow definition is missing 'contentVersion' property");
            }

            if (definition["triggers"] == null)
            {
                AddError("MISSING_TRIGGERS", "Workflow definition is missing 'triggers' property");
            }
            else if (!(definition["triggers"] is JObject))
            {
                AddError("INVALID_TRIGGERS", "Workflow 'triggers' must be an object");
            }

            if (definition["actions"] == null)
            {
                AddError("MISSING_ACTIONS", "Workflow definition is missing 'actions' property");
            }
            else if (!(definition["actions"] is JObject))
            {
                AddError("INVALID_ACTIONS", "Workflow 'actions' must be an object");
            }

            if (definition["outputs"] != null && !(definition["outputs"] is JObject))
            {
                AddError("INVALID_OUTPUTS", "Workflow 'outputs' must be an object");
            }
        }

        private void ValidateDefinition(JObject workflow)
        {
            var definition = workflow["definition"] as JObject;
            if (definition == null) return;

            // Validate schema URL format
            var schemaUrl = definition["$schema"]?.Value<string>();
            if (!string.IsNullOrEmpty(schemaUrl))
            {
                if (!schemaUrl.StartsWith("https://schema.management.azure.com/", StringComparison.OrdinalIgnoreCase))
                {
                    AddWarning("INVALID_SCHEMA_URL", "Schema URL should start with 'https://schema.management.azure.com/'");
                }

                if (!schemaUrl.Contains("workflowdefinition.json"))
                {
                    AddWarning("INVALID_SCHEMA_TYPE", "Schema URL should reference 'workflowdefinition.json'");
                }
            }

            // Validate content version format
            var contentVersion = definition["contentVersion"]?.Value<string>();
            if (!string.IsNullOrEmpty(contentVersion))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(contentVersion, @"^\d+\.\d+\.\d+\.\d+$"))
                {
                    AddWarning("INVALID_CONTENT_VERSION", "Content version should be in format 'X.X.X.X' (e.g., '1.0.0.0')");
                }
            }
        }

        private void ValidateTriggers(JObject workflow)
        {
            var definition = workflow["definition"] as JObject;
            if (definition == null) return;

            var triggers = definition["triggers"] as JObject;
            if (triggers == null) return;

            var triggerCount = triggers.Properties().Count();

            if (triggerCount == 0)
            {
                AddError("NO_TRIGGERS", "Workflow must have at least one trigger");
                return;
            }

            if (triggerCount > 1)
            {
                AddWarning("MULTIPLE_TRIGGERS", string.Format("Workflow has {0} triggers. Only the first will execute.", triggerCount));
            }

            // Validate each trigger
            foreach (var triggerProp in triggers.Properties())
            {
                ValidateTrigger(triggerProp.Name, triggerProp.Value as JObject);
            }
        }

        private void ValidateTrigger(string triggerName, JObject trigger)
        {
            if (trigger == null) return;

            var context = "Trigger '" + triggerName + "'";

            // Check trigger name length (max 80 characters)
            if (triggerName.Length > 80)
            {
                AddError("TRIGGER_NAME_TOO_LONG", string.Format("{0}: Name exceeds 80 characters ({1})", context, triggerName.Length));
            }

            // Check for invalid characters in trigger name
            if (!System.Text.RegularExpressions.Regex.IsMatch(triggerName, @"^[a-zA-Z0-9_\-]+$"))
            {
                AddWarning("TRIGGER_NAME_INVALID_CHARS", context + ": Name contains invalid characters (use only letters, numbers, underscores, and hyphens)");
            }

            // Validate trigger type
            var triggerType = trigger["type"]?.Value<string>();
            if (string.IsNullOrEmpty(triggerType))
            {
                AddError("MISSING_TRIGGER_TYPE", context + ": Missing 'type' property");
                return;
            }

            var validTypes = new[] { "Request", "Recurrence", "ServiceProvider", "ApiConnection", "Http", "HttpWebhook" };
            if (!validTypes.Contains(triggerType, StringComparer.OrdinalIgnoreCase))
            {
                AddWarning("UNKNOWN_TRIGGER_TYPE", string.Format("{0}: Unknown trigger type '{1}'", context, triggerType));
            }

            // Validate ServiceProvider triggers
            if (triggerType.Equals("ServiceProvider", StringComparison.OrdinalIgnoreCase))
            {
                ValidateServiceProviderTrigger(triggerName, trigger);
            }

            // Validate Request triggers
            if (triggerType.Equals("Request", StringComparison.OrdinalIgnoreCase))
            {
                var kind = trigger["kind"]?.Value<string>();
                if (string.IsNullOrEmpty(kind))
                {
                    AddWarning("MISSING_REQUEST_KIND", context + ": Request trigger should have 'kind' property (e.g., 'Http')");
                }
            }

            // Validate recurrence for polling triggers
            var triggerKind = trigger["kind"]?.Value<string>();
            if (triggerKind != null && triggerKind.Equals("Polling", StringComparison.OrdinalIgnoreCase))
            {
                if (trigger["recurrence"] == null)
                {
                    AddWarning("MISSING_RECURRENCE", context + ": Polling trigger should have 'recurrence' property");
                }
                else
                {
                    ValidateRecurrence(triggerName, trigger["recurrence"] as JObject);
                }
            }
        }

        private void ValidateRecurrence(string triggerName, JObject recurrence)
        {
            if (recurrence == null) return;

            var context = "Trigger '" + triggerName + "' recurrence";

            if (recurrence["frequency"] == null)
            {
                AddError("MISSING_FREQUENCY", context + ": Missing 'frequency' property");
            }
            else
            {
                var frequency = recurrence["frequency"].Value<string>();
                var validFrequencies = new[] { "Second", "Minute", "Hour", "Day", "Week", "Month", "Year" };
                if (!validFrequencies.Contains(frequency, StringComparer.OrdinalIgnoreCase))
                {
                    AddError("INVALID_FREQUENCY", context + ": Invalid frequency '" + frequency + "'");
                }
            }

            if (recurrence["interval"] == null)
            {
                AddError("MISSING_INTERVAL", context + ": Missing 'interval' property");
            }
            else
            {
                var interval = recurrence["interval"].Value<int>();
                if (interval <= 0)
                {
                    AddError("INVALID_INTERVAL", context + ": Interval must be greater than 0");
                }
            }
        }

        private void ValidateServiceProviderTrigger(string triggerName, JObject trigger)
        {
            var context = "Trigger '" + triggerName + "'";
            var inputs = trigger["inputs"] as JObject;

            if (inputs == null)
            {
                AddError("MISSING_TRIGGER_INPUTS", context + ": ServiceProvider trigger must have 'inputs' property");
                return;
            }

            var config = inputs["serviceProviderConfiguration"] as JObject;
            if (config == null)
            {
                AddError("MISSING_SP_CONFIG", context + ": Missing 'serviceProviderConfiguration' in inputs");
                return;
            }

            // Validate required configuration properties
            if (config["serviceProviderId"] == null)
            {
                AddError("MISSING_PROVIDER_ID", context + ": Missing 'serviceProviderId' in configuration");
            }
            else
            {
                var serviceProviderId = config["serviceProviderId"].Value<string>();
                if (!serviceProviderId.StartsWith("/serviceProviders/", StringComparison.OrdinalIgnoreCase))
                {
                    AddWarning("INVALID_PROVIDER_ID_FORMAT", context + ": serviceProviderId should start with '/serviceProviders/'");
                }
            }

            if (config["operationId"] == null)
            {
                AddError("MISSING_OPERATION_ID", context + ": Missing 'operationId' in configuration");
            }

            if (config["connectionName"] == null)
            {
                AddWarning("MISSING_CONNECTION_NAME", context + ": Missing 'connectionName' in configuration");
            }

            // Validate parameters
            if (inputs["parameters"] == null)
            {
                AddWarning("MISSING_TRIGGER_PARAMETERS", context + ": ServiceProvider trigger should have 'parameters' in inputs");
            }
            else if (!(inputs["parameters"] is JObject))
            {
                AddError("INVALID_TRIGGER_PARAMETERS", context + ": 'parameters' must be an object");
            }
        }

        private void ValidateActions(JObject workflow)
        {
            var definition = workflow["definition"] as JObject;
            if (definition == null) return;

            var actions = definition["actions"] as JObject;
            if (actions == null) return;

            if (actions.Properties().Count() == 0)
            {
                AddWarning("NO_ACTIONS", "Workflow has no actions defined");
                return;
            }

            // Validate each action
            foreach (var actionProp in actions.Properties())
            {
                ValidateAction(actionProp.Name, actionProp.Value as JObject);
            }

            // Check for circular dependencies
            ValidateActionDependencies(actions);
        }

        private void ValidateAction(string actionName, JObject action)
        {
            if (action == null) return;

            var context = "Action '" + actionName + "'";

            // Check action name length
            if (actionName.Length > 80)
            {
                AddError("ACTION_NAME_TOO_LONG", string.Format("{0}: Name exceeds 80 characters ({1})", context, actionName.Length));
            }

            // Check for invalid characters in action name
            if (!System.Text.RegularExpressions.Regex.IsMatch(actionName, @"^[a-zA-Z0-9_\-]+$"))
            {
                AddWarning("ACTION_NAME_INVALID_CHARS", context + ": Name contains invalid characters (use only letters, numbers, underscores, and hyphens)");
            }

            // Validate action type
            var actionType = action["type"]?.Value<string>();
            if (string.IsNullOrEmpty(actionType))
            {
                AddError("MISSING_ACTION_TYPE", context + ": Missing 'type' property");
                return;
            }

            // Validate ServiceProvider actions
            if (actionType.Equals("ServiceProvider", StringComparison.OrdinalIgnoreCase))
            {
                ValidateServiceProviderAction(actionName, action);
            }

            // Validate runAfter structure
            var runAfter = action["runAfter"];
            if (runAfter != null && !(runAfter is JObject))
            {
                AddError("INVALID_RUNAFTER", context + ": 'runAfter' must be an object");
            }
        }

        private void ValidateServiceProviderAction(string actionName, JObject action)
        {
            var context = "Action '" + actionName + "'";
            var inputs = action["inputs"] as JObject;

            if (inputs == null)
            {
                AddError("MISSING_ACTION_INPUTS", context + ": ServiceProvider action must have 'inputs' property");
                return;
            }

            var config = inputs["serviceProviderConfiguration"] as JObject;
            if (config == null)
            {
                AddError("MISSING_SP_CONFIG", context + ": Missing 'serviceProviderConfiguration' in inputs");
                return;
            }

            // Validate required configuration properties
            if (config["serviceProviderId"] == null)
            {
                AddError("MISSING_PROVIDER_ID", context + ": Missing 'serviceProviderId' in configuration");
            }
            else
            {
                var serviceProviderId = config["serviceProviderId"].Value<string>();
                if (!serviceProviderId.StartsWith("/serviceProviders/", StringComparison.OrdinalIgnoreCase))
                {
                    AddWarning("INVALID_PROVIDER_ID_FORMAT", context + ": serviceProviderId should start with '/serviceProviders/'");
                }
            }

            if (config["operationId"] == null)
            {
                AddError("MISSING_OPERATION_ID", context + ": Missing 'operationId' in configuration");
            }

            if (config["connectionName"] == null)
            {
                AddWarning("MISSING_CONNECTION_NAME", context + ": Missing 'connectionName' in configuration");
            }

            // Validate parameters
            if (inputs["parameters"] != null && !(inputs["parameters"] is JObject))
            {
                AddError("INVALID_ACTION_PARAMETERS", context + ": 'parameters' must be an object");
            }
        }

        private void ValidateActionDependencies(JObject actions)
        {
            var actionNames = new HashSet<string>(actions.Properties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var actionProp in actions.Properties())
            {
                var action = actionProp.Value as JObject;
                if (action == null) continue;

                var runAfter = action["runAfter"] as JObject;
                if (runAfter == null) continue;

                foreach (var dependency in runAfter.Properties())
                {
                    if (!actionNames.Contains(dependency.Name))
                    {
                        AddError("INVALID_DEPENDENCY", string.Format("Action '{0}' depends on non-existent action '{1}'", actionProp.Name, dependency.Name));
                    }

                    // Validate status array
                    var statuses = dependency.Value as JArray;
                    if (statuses != null)
                    {
                        var validStatuses = new[] { "Succeeded", "Failed", "Skipped", "TimedOut" };
                        foreach (var status in statuses)
                        {
                            var statusStr = status.Value<string>();
                            if (!validStatuses.Contains(statusStr, StringComparer.OrdinalIgnoreCase))
                            {
                                AddWarning("INVALID_RUNAFTER_STATUS", string.Format("Action '{0}': Unknown runAfter status '{1}'", actionProp.Name, statusStr));
                            }
                        }
                    }
                }
            }
        }

        private void ValidateBestPractices(JObject workflow)
        {
            var definition = workflow["definition"] as JObject;
            if (definition == null) return;

            // Check for error handling
            var actions = definition["actions"] as JObject;
            if (actions != null)
            {
                var hasScopeAction = false;
                var hasErrorHandling = false;

                foreach (var actionProp in actions.Properties())
                {
                    var action = actionProp.Value as JObject;
                    if (action == null) continue;

                    var actionType = action["type"]?.Value<string>();
                    if (actionType != null && actionType.Equals("Scope", StringComparison.OrdinalIgnoreCase))
                    {
                        hasScopeAction = true;
                    }

                    // Check if any action has failure handling in runAfter
                    var runAfter = action["runAfter"] as JObject;
                    if (runAfter != null)
                    {
                        foreach (var dep in runAfter.Properties())
                        {
                            var statuses = dep.Value as JArray;
                            if (statuses != null)
                            {
                                foreach (var status in statuses)
                                {
                                    var statusStr = status.Value<string>();
                                    if (statusStr != null && (statusStr.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                                        statusStr.Equals("TimedOut", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        hasErrorHandling = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!hasScopeAction)
                {
                    AddInfo("NO_SCOPE_ACTIONS", "Consider using 'Scope' actions to group related actions and improve error handling");
                }

                if (!hasErrorHandling)
                {
                    AddInfo("NO_ERROR_HANDLING", "Consider adding error handling with 'runAfter' conditions on Failed/TimedOut statuses");
                }
            }

            // Check for outputs
            var outputs = definition["outputs"] as JObject;
            if (outputs == null || outputs.Properties().Count() == 0)
            {
                AddInfo("NO_OUTPUTS", "Workflow has no outputs defined. Consider adding outputs for monitoring and debugging.");
            }
        }

        private void AddError(string code, string message)
        {
            _issues.Add(new ValidationIssue
            {
                Severity = IssueSeverity.Error,
                Code = code,
                Message = message
            });
        }

        private void AddWarning(string code, string message)
        {
            _issues.Add(new ValidationIssue
            {
                Severity = IssueSeverity.Warning,
                Code = code,
                Message = message
            });
        }

        private void AddInfo(string code, string message)
        {
            _issues.Add(new ValidationIssue
            {
                Severity = IssueSeverity.Info,
                Code = code,
                Message = message
            });
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationIssue> Issues { get; set; }

        public ValidationResult()
        {
            Issues = new List<ValidationIssue>();
        }

        public bool HasErrors
        {
            get { return Issues.Any(i => i.Severity == IssueSeverity.Error); }
        }

        public bool HasWarnings
        {
            get { return Issues.Any(i => i.Severity == IssueSeverity.Warning); }
        }

        public string GetSummary()
        {
            var errors = Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = Issues.Count(i => i.Severity == IssueSeverity.Warning);
            var infos = Issues.Count(i => i.Severity == IssueSeverity.Info);

            return string.Format("Validation: {0} error(s), {1} warning(s), {2} info(s)",
                errors, warnings, infos);
        }

        public void PrintIssues()
        {
            foreach (var issue in Issues.OrderBy(i => i.Severity))
            {
                var prefix = issue.Severity == IssueSeverity.Error ? "ERROR" :
                            issue.Severity == IssueSeverity.Warning ? "WARNING" : "INFO";
                Console.WriteLine("[{0}] {1}: {2}", prefix, issue.Code, issue.Message);
            }
        }
    }

    public class ValidationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }

    public enum IssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }
}