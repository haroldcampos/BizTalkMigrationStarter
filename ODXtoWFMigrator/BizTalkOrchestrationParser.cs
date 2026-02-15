// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Parses BizTalk Server orchestration files (.odx) and converts them to an object model.
    /// Extracts orchestration metadata, shapes, ports, messages, and control flow structures.
    /// </summary>
    public static class BizTalkOrchestrationParser
    {
        private const string DesignerDataSentinel = "#endif";

        /// <summary>
        /// Parses a BizTalk orchestration (.odx) file and creates an OrchestrationModel.
        /// Extracts XML metadata from the ODX file, parses messages, port types, ports, and shape hierarchy.
        /// </summary>
        /// <param name="filePath">The full path to the .odx orchestration file.</param>
        /// <returns>An OrchestrationModel containing all orchestration metadata and shapes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the orchestration file does not exist.</exception>
        /// <exception cref="InvalidDataException">Thrown when the ODX file structure is invalid or corrupted.</exception>
        /// <exception cref="InvalidOperationException">Thrown when parsing fails for messages, ports, or shapes.</exception>
        public static OrchestrationModel ParseOdx(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Orchestration file not found: {filePath}", filePath);

            string raw;
            try
            {
                raw = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read orchestration file '{filePath}': {ex.Message}", ex);
            }

            int xmlStart = raw.IndexOf("<?xml", StringComparison.Ordinal);
            if (xmlStart < 0)
                throw new InvalidDataException($"Invalid ODX file '{Path.GetFileName(filePath)}': Missing XML declaration. Ensure this is a valid BizTalk orchestration file.");

            int xmlEnd = raw.IndexOf(DesignerDataSentinel, xmlStart, StringComparison.Ordinal);
            if (xmlEnd < 0)
                throw new InvalidDataException($"Invalid ODX file '{Path.GetFileName(filePath)}': Missing '#endif' sentinel. The file may be corrupted or incomplete.");

            string xmlFragment = raw.Substring(xmlStart, xmlEnd - xmlStart);

            var doc = new XmlDocument();
            try
            {
                doc.LoadXml(xmlFragment);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"Failed to parse XML in orchestration file '{Path.GetFileName(filePath)}' at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}", ex);
            }

            var nav = doc.CreateNavigator();
            var nsmgr = new XmlNamespaceManager(nav.NameTable);
            nsmgr.AddNamespace("om", "http://schemas.microsoft.com/BizTalk/2003/DesignerData");

            var model = new OrchestrationModel
            {
                Namespace = Eval(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Property[@Name='Name']/@Value"),
                Name = Eval(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='ServiceDeclaration']/om:Property[@Name='Name']/@Value")
            };

            if (string.IsNullOrEmpty(model.Name))
                throw new InvalidDataException($"Failed to extract orchestration name from '{Path.GetFileName(filePath)}'. The file structure may be invalid.");

            // Parse Messages
            try
            {
                foreach (var msgNav in Select(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='ServiceDeclaration']/om:Element[@Type='MessageDeclaration']"))
                {
                    var msgName = Eval(msgNav, nsmgr, "om:Property[@Name='Name']/@Value");
                    if (string.IsNullOrEmpty(msgName))
                    {
                        continue;
                    }

                    model.Messages.Add(new MessageModel
                    {
                        Name = msgName,
                        Type = Eval(msgNav, nsmgr, "om:Property[@Name='Type']/@Value"),
                        Direction = Eval(msgNav, nsmgr, "om:Property[@Name='ParamDirection']/@Value")
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse message declarations in orchestration '{model.Name}': {ex.Message}", ex);
            }

            // Parse Service-level Variable Declarations (e.g., SendPipelineInputMessages, ReceivePipelineOutputMessages)
            // These are declared as children of ServiceDeclaration, outside ServiceBody,
            // and must be collected into model.Shapes so CollectVariableDeclarationsRecursive
            // can hoist them into InitializeVariable actions at workflow root level.
            try
            {
                foreach (var varNav in Select(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='ServiceDeclaration']/om:Element[@Type='VariableDeclaration']"))
                {
                    var varName = Eval(varNav, nsmgr, "om:Property[@Name='Name']/@Value");
                    if (string.IsNullOrEmpty(varName))
                    {
                        continue;
                    }

                    var varType = Eval(varNav, nsmgr, "om:Property[@Name='Type']/@Value");
                    var varOid = varNav.GetAttribute("OID", string.Empty);

                    model.Shapes.Add(new VariableDeclarationShapeModel
                    {
                        ShapeType = "VariableDeclaration",
                        Oid = varOid,
                        Name = varName,
                        VarType = varType,
                        UseDefault = Eval(varNav, nsmgr, "om:Property[@Name='UseDefaultConstructor']/@Value"),
                        Sequence = -1
                    });

                    Trace.TraceInformation("[PARSER] Collected service-level variable: {0} ({1})", varName, varType);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("[PARSER] Failed to parse service-level variable declarations: {0}", ex.Message);
            }

            // Parse Port Types
            try
            {
                foreach (var ptNav in Select(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='PortType']"))
                {
                    var ptName = Eval(ptNav, nsmgr, "om:Property[@Name='Name']/@Value");
                    if (string.IsNullOrEmpty(ptName))
                    {
                        continue;
                    }

                    var pt = new PortTypeModel
                    {
                        Name = ptName,
                        Modifier = Eval(ptNav, nsmgr, "om:Property[@Name='TypeModifier']/@Value")
                    };

                    foreach (var opNav in Select(ptNav, nsmgr, "om:Element[@Type='OperationDeclaration']"))
                    {
                        var opName = Eval(opNav, nsmgr, "om:Property[@Name='Name']/@Value");
                        if (string.IsNullOrEmpty(opName))
                        {
                            continue;
                        }

                        pt.Operations.Add(new OperationModel
                        {
                            Name = opName,
                            OperationType = Eval(opNav, nsmgr, "om:Property[@Name='OperationType']/@Value"),
                            RequestMessageType = Eval(opNav, nsmgr, "om:Element[@Type='MessageRef' and om:Property[@Name='Name']/@Value='Request']/om:Property[@Name='Ref']/@Value"),
                            ResponseMessageType = Eval(opNav, nsmgr, "om:Element[@Type='MessageRef' and om:Property[@Name='Name']/@Value='Response']/om:Property[@Name='Ref']/@Value"),
                            FaultMessageType = Eval(opNav, nsmgr, "om:Element[@Type='MessageRef' and om:Property[@Name='Name']/@Value='Fault']/om:Property[@Name='Ref']/@Value")
                        });
                    }
                    model.PortTypes.Add(pt);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse port types in orchestration '{model.Name}': {ex.Message}", ex);
            }

            // Parse Port Declarations
            try
            {
                foreach (var portNav in Select(nav, nsmgr, "/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='ServiceDeclaration']/om:Element[@Type='PortDeclaration']"))
                {
                    var portName = Eval(portNav, nsmgr, "om:Property[@Name='Name']/@Value");
                    if (string.IsNullOrEmpty(portName))
                    {
                        continue;
                    }

                    string modifier = Eval(portNav, nsmgr, "om:Property[@Name='PortModifier']/@Value");
                    string signal = Eval(portNav, nsmgr, "om:Property[@Name='Signal']/@Value");
                    string physicalTransport = Eval(portNav, nsmgr, "om:Element[@Type='PhysicalBindingAttribute']/om:Property[@Name='TransportType']/@Value");
                    string webTransport = Eval(portNav, nsmgr, "om:Element[@Type='WebPortBindingAttribute']/om:Property[@Name='TransportType']/@Value");

                    if (string.IsNullOrEmpty(physicalTransport))
                        physicalTransport = Eval(portNav, nsmgr, "om:Element[@Type='PhysicalBindingAttribute']/om:Property[@Name='Adapter']/@Value");
                    if (string.IsNullOrEmpty(physicalTransport))
                        physicalTransport = Eval(portNav, nsmgr, "om:Element[@Type='PhysicalBindingAttribute']/om:Property[@Name='AdapterName']/@Value");

                    var adapterName = !string.IsNullOrEmpty(physicalTransport) ? physicalTransport :
                                      !string.IsNullOrEmpty(webTransport) ? webTransport : string.Empty;

                    model.Ports.Add(new PortModel
                    {
                        Name = portName,
                        PortTypeReference = Eval(portNav, nsmgr, "om:Property[@Name='Type']/@Value"),
                        Direction = GetPortDirection(modifier, signal),
                        BindingKind =
                            Select(portNav, nsmgr, "om:Element[@Type='LogicalBindingAttribute']").Any() ? "Logical" :
                            Select(portNav, nsmgr, "om:Element[@Type='PhysicalBindingAttribute']").Any() ? "Physical" :
                            Select(portNav, nsmgr, "om:Element[@Type='DirectBindingAttribute']").Any() ? "Direct" :
                            Select(portNav, nsmgr, "om:Element[@Type='WebPortBindingAttribute']").Any() ? "Web" :
                            "Unknown",
                        AdapterName = adapterName,
                        TransportType = adapterName
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse port declarations in orchestration '{model.Name}': {ex.Message}", ex);
            }

            // Parse Service Body and Shapes
            var serviceBody = nav.SelectSingleNode("/om:MetaModel/om:Element[@Type='Module']/om:Element[@Type='ServiceDeclaration']/om:Element[@Type='ServiceBody']", nsmgr);
            int seq = 0;
            if (serviceBody != null)
            {
                var oidMap = new Dictionary<string, ShapeModel>();
                try
                {
                    ParseShapes(serviceBody, nsmgr, model, ref seq, oidMap, null);
                }
                catch (Exception ex) when (!(ex is InvalidOperationException))
                {
                    throw new InvalidOperationException($"Failed to parse shapes in orchestration '{model.Name}': {ex.Message}", ex);
                }

                // Process Correlation Declarations
                try
                {
                    foreach (var corrDecl in model.Shapes.OfType<CorrelationDeclarationShapeModel>())
                    {
                        foreach (var stmtRef in corrDecl.StatementRefs)
                        {
                            if (string.IsNullOrEmpty(stmtRef.StatementOid))
                            {
                                continue;
                            }

                            if (oidMap.TryGetValue(stmtRef.StatementOid, out var shape) && shape is ReceiveShapeModel recv)
                            {
                                if (stmtRef.Initializes)
                                    recv.InitializesCorrelationSets.Add(corrDecl.Name);
                                else
                                    recv.FollowsCorrelationSets.Add(corrDecl.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to process correlation declarations in orchestration '{model.Name}': {ex.Message}", ex);
                }
            }

            return model;
        }

        /// <summary>
        /// Generates a complete Azure Logic Apps Standard workflow JSON from a BizTalk orchestration and bindings.
        /// Main entry point for end-to-end conversion from BizTalk to Logic Apps.
        /// </summary>
        /// <param name="odxPath">The path to the BizTalk orchestration (.odx) file.</param>
        /// <param name="bindingsPath">The path to the BizTalk bindings (.xml) file.</param>
        /// <param name="workflowKind">The workflow kind ("Stateful" or "Stateless"). Defaults to "Stateful".</param>
        /// <param name="schemaVersion">The Logic Apps schema version. Defaults to "2020-05-01-preview".</param>
        /// <param name="isCallable">If true, forces Request trigger for nested workflow compatibility (called by other workflows). Defaults to false.</param>
        /// <returns>A JSON string representing the complete Logic Apps workflow definition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when odxPath or bindingsPath is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the ODX or bindings file does not exist.</exception>
        public static string GenerateWorkflowJson(string odxPath, string bindingsPath, string workflowKind = "Stateful", string schemaVersion = "2020-05-01-preview", bool isCallable = false)
        {
            if (string.IsNullOrWhiteSpace(odxPath)) throw new ArgumentNullException(nameof(odxPath));
            if (string.IsNullOrWhiteSpace(bindingsPath)) throw new ArgumentNullException(nameof(bindingsPath));
            if (!File.Exists(odxPath)) throw new FileNotFoundException("ODX file not found.", odxPath);
            if (!File.Exists(bindingsPath)) throw new FileNotFoundException("Bindings file not found.", bindingsPath);

            var orchestration = ParseOdx(odxPath);
            var binding = BindingSnapshot.Parse(bindingsPath);
            ApplyBindings(orchestration, binding);

            var map = LogicAppsMapper.MapToLogicApp(orchestration, binding, isCallable);
            var registry = TryLoadConnectorRegistry();

            return LogicAppJSONGenerator.GenerateStandardWorkflow(map, workflowKind, schemaVersion, registry);
        }

        /// <summary>
        /// Parses a BizTalk orchestration and applies binding information from a bindings file.
        /// Combines orchestration metadata with runtime binding configuration (ports, adapters, addresses).
        /// </summary>
        /// <param name="orchestrationPath">The path to the BizTalk orchestration (.odx) file.</param>
        /// <param name="bindingsPath">The path to the BizTalk bindings (.xml) file.</param>
        /// <returns>An OrchestrationModel with binding information applied to ports.</returns>
        /// <exception cref="ArgumentNullException">Thrown when orchestrationPath or bindingsPath is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the bindings file does not exist.</exception>
        public static OrchestrationModel ParseOdxWithBindings(string orchestrationPath, string bindingsPath)
        {
            if (string.IsNullOrWhiteSpace(orchestrationPath)) throw new ArgumentNullException(nameof(orchestrationPath));
            if (string.IsNullOrWhiteSpace(bindingsPath)) throw new ArgumentNullException(nameof(bindingsPath));
            if (!File.Exists(bindingsPath)) throw new FileNotFoundException("Bindings file not found.", bindingsPath);

            var model = ParseOdx(orchestrationPath);
            var binding = BindingSnapshot.Parse(bindingsPath);
            ApplyBindings(model, binding);
            return model;
        }

        /// <summary>
        /// Applies runtime binding information to orchestration ports.
        /// Maps receive locations and send ports to orchestration port declarations by name.
        /// Updates adapter types, addresses, file paths, polling intervals, and pipeline configurations.
        /// </summary>
        /// <param name="model">The orchestration model to update with binding information.</param>
        /// <param name="binding">The binding snapshot containing receive locations and send ports.</param>
        private static void ApplyBindings(OrchestrationModel model, BindingSnapshot binding)
        {
            if (model == null || binding == null) return;

            foreach (var rl in binding.ReceiveLocations)
            {
                var port = model.Ports.FirstOrDefault(p => p.Name.Equals(rl.ReceivePortName, StringComparison.OrdinalIgnoreCase))
                           ?? model.Ports.FirstOrDefault(p => p.Name.Equals(rl.Name, StringComparison.OrdinalIgnoreCase));
                if (port != null)
                {
                    port.AdapterName = rl.TransportType ?? port.AdapterName;
                    port.TransportType = rl.TransportType ?? port.TransportType;
                    port.Address = rl.Address;
                    port.FolderPath = rl.FolderPath;
                    port.FileMask = rl.FileMask;
                    port.PollingIntervalSeconds = rl.PollingIntervalSeconds;
                    port.ReceivePipelineName = rl.ReceivePipelineName;
                }
            }

            foreach (var sp in binding.SendPorts)
            {
                var port = model.Ports.FirstOrDefault(p => p.Name.Equals(sp.Name, StringComparison.OrdinalIgnoreCase));
                if (port != null)
                {
                    port.AdapterName = sp.TransportType ?? port.AdapterName;
                    port.TransportType = sp.TransportType ?? port.TransportType;
                    port.Address = sp.Address;
                    port.SendPipelineName = sp.SendPipelineName;
                }
            }
        }

        /// <summary>
        /// Recursively parses orchestration shapes from XML and builds the shape hierarchy.
        /// Handles all shape types including Receive, Send, Decide, Loop, Parallel, Scope, etc.
        /// Maintains parent-child relationships and assigns sequence numbers.
        /// </summary>
        /// <param name="node">The XPath navigator positioned at the current XML element.</param>
        /// <param name="nsmgr">The XML namespace manager for BizTalk schema namespaces.</param>
        /// <param name="model">The orchestration model to add shapes to.</param>
        /// <param name="seq">Reference to the current sequence number (incremented for each shape).</param>
        /// <param name="oidMap">Dictionary mapping object IDs (OIDs) to shape instances for correlation lookup.</param>
        /// <param name="parent">The parent shape of the current shape (null for root-level shapes).</param>
        private static void ParseShapes(XPathNavigator node, XmlNamespaceManager nsmgr, OrchestrationModel model, ref int seq, IDictionary<string, ShapeModel> oidMap, ShapeModel parent)
        {
            foreach (var child in Select(node, nsmgr, "om:Element"))
            {
                string type = child.GetAttribute("Type", "");
                string oid = child.GetAttribute("OID", "");
                ShapeModel shape = null;

                Trace.TraceInformation("[PARSE] Found shape type: {0}, OID: {1}, Parent: {2}", type, oid, parent?.Name ?? "ROOT");

                if (EqualsIgnoreCase(type, "CallRules") || EqualsIgnoreCase(type, "CallPolicy"))
                {
                    var policy =
                        Eval(child, nsmgr, "om:Property[@Name='Policy']/@Value") ??
                        Eval(child, nsmgr, "om:Property[@Name='PolicyName']/@Value") ??
                        Eval(child, nsmgr, "om:Property[@Name='Ruleset']/@Value") ?? "";

                    shape = new CallRulesShapeModel
                    {
                        ShapeType = "CallRules",
                        Oid = oid,
                        Name = string.IsNullOrWhiteSpace(policy) ? "Execute_Rules_Engine" : "Execute_Rules_Engine_" + SafePolicySegment(policy),
                        PolicyName = policy,
                        Sequence = seq++
                    };
                }
                else
                {
                    switch (type)
                    {
                        case "Parallel":
                            shape = new ParallelShapeModel { ShapeType = type, Oid = oid, Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"), Sequence = seq++ };
                            break;

                        case "ParallelBranch":
                            shape = new ParallelBranchShapeModel
                            {
                                ShapeType = "ParallelBranch",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value") ?? "ParallelBranch",
                                Sequence = seq++
                            };
                            break;

                        case "Receive":
                            shape = new ReceiveShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                PortName = Eval(child, nsmgr, "om:Property[@Name='PortName']/@Value"),
                                MessageName = Eval(child, nsmgr, "om:Property[@Name='MessageName']/@Value"),
                                OperationName = Eval(child, nsmgr, "om:Property[@Name='OperationName']/@Value"),
                                OperationMessageName = Eval(child, nsmgr, "om:Property[@Name='OperationMessageName']/@Value"),
                                Activate = Eval(child, nsmgr, "om:Property[@Name='Activate']/@Value") == "True",
                                Sequence = seq++
                            };
                            break;

                        case "Loop":
                        case "ForEach":
                            var loopShape = new LoopShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                LoopType = type,
                                Sequence = seq++
                            };

                            var loopExpr = child.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                            if (loopExpr != null)
                            {
                                loopShape.CollectionExpression = Eval(loopExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                            }

                            var itemVar = Eval(child, nsmgr, "om:Element[@Type='IteratorVariable']/om:Property[@Name='Name']/@Value");
                            loopShape.ItemVariable = string.IsNullOrEmpty(itemVar) ? "item" : itemVar;

                            shape = loopShape;
                            break;

                        case "Decision":
                        case "Decide":
                        case "If":
                        case "IfElse":
                            var decideShape = new DecideShapeModel
                            {
                                ShapeType = "Decide",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };

                            var branches = child.Select("om:Element[@Type='DecisionBranch']", nsmgr)
                                .Cast<XPathNavigator>()
                                .ToList();

                            Trace.TraceInformation("[PARSER] Decision '{0}' has {1} branches", decideShape.Name, branches.Count);

                            if (branches.Count > 0)
                            {
                                XPathNavigator ruleBranch = null;
                                string foundExpression = null;

                                // Look for expression in branches
                                for (int i = 0; i < branches.Count; i++)
                                {
                                    var branch = branches[i];
                                    var branchName = Eval(branch, nsmgr, "om:Property[@Name='Name']/@Value");

                                    // First try direct Expression property
                                    var exprValue = Eval(branch, nsmgr, "om:Property[@Name='Expression']/@Value");

                                    // If not found, look for nested Expression element
                                    if (string.IsNullOrWhiteSpace(exprValue))
                                    {
                                        var nestedExpr = branch.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                                        if (nestedExpr != null)
                                        {
                                            exprValue = Eval(nestedExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                                        Trace.TraceInformation("[PARSER]   Found nested Expression shape in branch {0}", i);
                                        }
                                    }

                                    // The first branch with an expression is the "if" condition
                                    if (!string.IsNullOrWhiteSpace(exprValue) && ruleBranch == null)
                                    {
                                        ruleBranch = branch;
                                        foundExpression = exprValue;
                                        decideShape.Expression = exprValue;
                                        Trace.TraceInformation("[PARSER]   Found Expression in branch {0} ('{1}'): {2}...", i, branchName ?? "unnamed", exprValue.Substring(0, Math.Min(60, exprValue.Length)));
                                    }
                                }

                                // If no expression found in any branch, check for Expression as a sibling to DecisionBranch
                                if (string.IsNullOrWhiteSpace(foundExpression))
                                {
                                    var siblingExpr = child.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                                    if (siblingExpr != null)
                                    {
                                        foundExpression = Eval(siblingExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                                        if (!string.IsNullOrWhiteSpace(foundExpression))
                                        {
                                        decideShape.Expression = foundExpression;
                                            Trace.TraceInformation("[PARSER]   Found Expression as sibling to branches: {0}...", foundExpression.Substring(0, Math.Min(60, foundExpression.Length)));
                                        }
                                    }
                                }

                                // Determine which branch is TRUE and which is FALSE
                                // In BizTalk, typically the first branch with expression is TRUE, second is ELSE
                                XPathNavigator trueBranchNav = null;
                                XPathNavigator falseBranchNav = null;

                                if (ruleBranch != null)
                                {
                                    trueBranchNav = ruleBranch;
                                    // The other branch (if exists) is the false branch
                                    falseBranchNav = branches.FirstOrDefault(b => b != ruleBranch);
                                }
                                else if (branches.Count > 0)
                                {
                                    // No expression found, treat first as true branch
                                    trueBranchNav = branches[0];
                                    if (branches.Count > 1)
                                    {
                                        falseBranchNav = branches[1];
                                    }
                                    Trace.TraceInformation("[PARSER]   No Expression found - using branch order");
                                }

                                // Parse TRUE branch (including any Expression shapes inside it)
                                if (trueBranchNav != null)
                                {
                                    var trueBranchModel = new OrchestrationModel();
                                    int trueBranchSeq = 0;

                                    // Parse ALL shapes in the branch, including Expression shapes
                                    ParseShapes(trueBranchNav, nsmgr, trueBranchModel, ref trueBranchSeq, oidMap, null);

                                    Trace.TraceInformation("[PARSER]   TrueBranch parsed: {0} shapes", trueBranchModel.Shapes.Count);

                                    // Check if we captured any Expression shapes
                                    var expressionShapes = trueBranchModel.Shapes.OfType<ExpressionShapeModel>().ToList();
                                    if (expressionShapes.Any())
                                    {
                                        Trace.TraceInformation("[PARSER]     Found {0} Expression shape(s) in TRUE branch", expressionShapes.Count);
                                        // If decide doesn't have expression yet, use the first Expression shape's expression
                                        if (string.IsNullOrWhiteSpace(decideShape.Expression) && expressionShapes.Any())
                                        {
                                            decideShape.Expression = expressionShapes.First().Expression;
                                            Trace.TraceInformation("[PARSER]     Using Expression from shape: {0}", expressionShapes.First().Name);
                                        }
                                    }

                                    foreach (var branchShape in trueBranchModel.Shapes)
                                    {
                                        branchShape.Parent = decideShape;
                                        if (string.IsNullOrEmpty(branchShape.UniqueId))
                                        {
                                            branchShape.UniqueId = branchShape.Oid + "_" + branchShape.Sequence + "_TRUE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                                        }
                                    }

                                    decideShape.TrueBranch.AddRange(trueBranchModel.Shapes);
                                }

                                // Parse FALSE branch (including any Expression shapes inside it)
                                if (falseBranchNav != null)
                                {
                                    var falseBranchModel = new OrchestrationModel();
                                    int falseBranchSeq = 0;

                                    // Parse ALL shapes in the branch, including Expression shapes
                                    ParseShapes(falseBranchNav, nsmgr, falseBranchModel, ref falseBranchSeq, oidMap, null);

                                    Trace.TraceInformation("[PARSER]   FalseBranch parsed: {0} shapes", falseBranchModel.Shapes.Count);

                                    // Check if we captured any Expression shapes
                                    var expressionShapes = falseBranchModel.Shapes.OfType<ExpressionShapeModel>().ToList();
                                    if (expressionShapes.Any())
                                    {
                                        Trace.TraceInformation("[PARSER]     Found {0} Expression shape(s) in FALSE branch", expressionShapes.Count);
                                    }

                                    foreach (var branchShape in falseBranchModel.Shapes)
                                    {
                                        branchShape.Parent = decideShape;
                                        if (string.IsNullOrEmpty(branchShape.UniqueId))
                                        {
                                            branchShape.UniqueId = branchShape.Oid + "_" + branchShape.Sequence + "_FALSE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                                        }
                                    }

                                    decideShape.FalseBranch.AddRange(falseBranchModel.Shapes);
                                }
                            }
                            else
                            {
                                // No DecisionBranch elements found, look for direct child shapes
                                Trace.TraceInformation("[PARSER]   No DecisionBranch elements found, looking for direct children");

                                // Check for Expression shape as direct child
                                var directExpr = child.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                                if (directExpr != null)
                                {
                                    var exprValue = Eval(directExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                                    if (!string.IsNullOrWhiteSpace(exprValue))
                                    {
                                        decideShape.Expression = exprValue;
                                        Trace.TraceInformation("[PARSER]   Found direct Expression child: {0}...", exprValue.Substring(0, Math.Min(60, exprValue.Length)));
                                    }
                                }
                            }

                            shape = decideShape;
                            break;

                        case "Switch":
                            var switchShape = new SwitchShapeModel
                            {
                                ShapeType = "Switch",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };

                            // Get the switch expression
                            switchShape.Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value");

                            // If not found as property, look for nested Expression element
                            if (string.IsNullOrWhiteSpace(switchShape.Expression))
                            {
                                var nestedExpr = child.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                                if (nestedExpr != null)
                                {
                                    switchShape.Expression = Eval(nestedExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                                }
                            }

                            Trace.TraceInformation("[PARSER] Switch '{0}' with expression: {1}...", switchShape.Name, switchShape.Expression?.Substring(0, Math.Min(60, switchShape.Expression?.Length ?? 0)));

                            // Parse all case branches
                            var caseBranches = Select(child, nsmgr, "om:Element[@Type='DecisionBranch']");
                            int caseIndex = 0;

                            foreach (var caseBranch in caseBranches)
                            {
                                var caseName = Eval(caseBranch, nsmgr, "om:Property[@Name='Name']/@Value");
                                var caseValue = Eval(caseBranch, nsmgr, "om:Property[@Name='Expression']/@Value");

                                // Check if this is the default case (usually has empty expression or specific name)
                                bool isDefaultCase = string.IsNullOrWhiteSpace(caseValue) ||
                                                    caseName?.ToLower().Contains("default") == true ||
                                                    caseName?.ToLower().Contains("else") == true;

                                Trace.TraceInformation("[PARSER]   Case {0}: '{1}' with value: {2}", ++caseIndex, caseName, caseValue ?? "(default)");

                                // Parse shapes within this case
                                var caseModel = new OrchestrationModel();
                                int caseSeq = 0;
                                ParseShapes(caseBranch, nsmgr, caseModel, ref caseSeq, oidMap, null);

                                // Assign parent and unique IDs to shapes
                                foreach (var caseShape in caseModel.Shapes)
                                {
                                    caseShape.Parent = switchShape;
                                    if (string.IsNullOrEmpty(caseShape.UniqueId))
                                    {
                                        caseShape.UniqueId = caseShape.Oid + "_" + caseShape.Sequence + "_CASE_" + caseIndex + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                                    }
                                }

                                if (isDefaultCase)
                                {
                                    switchShape.DefaultCase.AddRange(caseModel.Shapes);
                                    Trace.TraceInformation("[PARSER]     Default case parsed: {0} shapes", caseModel.Shapes.Count);
                                }
                                else
                                {
                                    // Use the case value as the key, or the name if no value
                                    string caseKey = !string.IsNullOrWhiteSpace(caseValue) ? caseValue : caseName ?? $"Case_{caseIndex}";

                                    if (!switchShape.Cases.ContainsKey(caseKey))
                                    {
                                        switchShape.Cases[caseKey] = new List<ShapeModel>();
                                    }

                                    switchShape.Cases[caseKey].AddRange(caseModel.Shapes);
                                    Trace.TraceInformation("[PARSER]     Case '{0}' parsed: {1} shapes", caseKey, caseModel.Shapes.Count);
                                }
                            }

                            Trace.TraceInformation("[PARSER] Switch complete with {0} cases and {1} default case", switchShape.Cases.Count, switchShape.DefaultCase.Count > 0 ? "a" : "no");

                            shape = switchShape;
                            break;

                        case "Listen":
                            var listenShape = new ListenShapeModel
                            {
                                ShapeType = "Listen",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value") ?? "Listen",
                                Sequence = seq++
                            };

                            // Implement Listen branch parsing
                            // BizTalk ODX files use either Task or ListenBranch elements for Listen branches
                            var listenBranches = Select(child, nsmgr, "om:Element[@Type='Task' or @Type='ListenBranch']");
                            int branchIndex = 0;
                            foreach (var branchNav in listenBranches)
                            {
                                Trace.TraceInformation("[PARSER] Parsing Listen branch {0}", ++branchIndex);
                                var branchModel = new OrchestrationModel();
                                int branchSeq = 0;
                                ParseShapes(branchNav, nsmgr, branchModel, ref branchSeq, oidMap, listenShape);

                                foreach (var branchShape in branchModel.Shapes)
                                {
                                    branchShape.Parent = listenShape;
                                    listenShape.Branches.Add(branchShape);
                                }
                            }

                            shape = listenShape;
                            break;

                        case "Send":
                            shape = new SendShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                PortName = Eval(child, nsmgr, "om:Property[@Name='PortName']/@Value"),
                                MessageName = Eval(child, nsmgr, "om:Property[@Name='MessageName']/@Value"),
                                OperationName = Eval(child, nsmgr, "om:Property[@Name='OperationName']/@Value"),
                                OperationMessageName = Eval(child, nsmgr, "om:Property[@Name='OperationMessageName']/@Value"),
                                Sequence = seq++,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };
                            break;

                        case "Construct":
                            shape = ParseConstruct(child, nsmgr, seq);
                            shape.Oid = oid;
                            shape.Sequence = seq++;
                            shape.UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                            break;

                        case "Transform":
                            shape = new TransformShapeModel
                            {
                                ShapeType = "Transform",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                ClassName = Eval(child, nsmgr, "om:Property[@Name='ClassName']/@Value"),
                                Sequence = seq++,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };
                            break;

                        case "MessageAssignment":
                            shape = new MessageAssignmentShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };
                            seq++;
                            break;

                        case "VariableAssignment":
                            shape = new VariableAssignmentShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "While":
                            shape = new WhileShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Until":
                            shape = new UntilShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Call":
                            shape = new CallShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Invokee = Eval(child, nsmgr, "om:Property[@Name='Invokee']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "CorrelationDeclaration":
                            var corrDecl = new CorrelationDeclarationShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                CorrelationTypeRef = Eval(child, nsmgr, "om:Property[@Name='Type']/@Value"),
                                Sequence = seq++
                            };
                            foreach (var stmtRef in Select(child, nsmgr, "om:Element[@Type='StatementRef']"))
                            {
                                corrDecl.StatementRefs.Add(new StatementCorrelationRef
                                {
                                    StatementOid = Eval(stmtRef, nsmgr, "om:Property[@Name='Ref']/@Value"),
                                    Initializes = Eval(stmtRef, nsmgr, "om:Property[@Name='Initializes']/@Value") == "True"
                                });
                            }
                            shape = corrDecl;
                            break;

                        case "Scope":
                            shape = new ScopeShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };
                            seq++;
                            break;

                        case "Throw":
                            shape = new TerminateShapeModel
                            {
                                ShapeType = "Throw",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                ErrorMessage = Eval(child, nsmgr, "om:Property[@Name='Exception']/@Value") ??
                                      Eval(child, nsmgr, "om:Property[@Name='ExceptionType']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Suspend":
                            shape = new TerminateShapeModel
                            {
                                ShapeType = "Suspend",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                ErrorMessage = Eval(child, nsmgr, "om:Property[@Name='ErrorMessage']/@Value") ?? "Suspended",
                                Sequence = seq++
                            };
                            break;

                        case "Terminate":
                            shape = new TerminateShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                ErrorMessage = Eval(child, nsmgr, "om:Property[@Name='ErrorMessage']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Expression":
                            shape = new ExpressionShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Expression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Delay":
                            shape = new DelayShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                DelayExpression = Eval(child, nsmgr, "om:Property[@Name='Expression']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Compensate":
                            shape = new CompensateShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Target = Eval(child, nsmgr, "om:Property[@Name='Target']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Group":
                            shape = new GroupShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "Exec":
                        case "Start":
                        case "StartOrchestration":
                            shape = new StartShapeModel
                            {
                                ShapeType = "StartOrchestration",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Invokee = Eval(child, nsmgr, "om:Property[@Name='Invokee']/@Value"),
                                Sequence = seq,
                                UniqueId = oid + "_" + seq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                            };
                            seq++;
                            break;

                        case "Task":
                            shape = new TaskShapeModel
                            {
                                ShapeType = "Task",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value") ?? "Task",
                                Sequence = seq++
                            };
                            break;

                        case "Catch":
                        case "CatchException":
                            var catchShape = new CatchShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                ExceptionType = Eval(child, nsmgr, "om:Property[@Name='ExceptionType']/@Value") ?? 
                                               Eval(child, nsmgr, "om:Property[@Name='Exception']/@Value") ??
                                               "System.Exception",
                                ExceptionVariable = Eval(child, nsmgr, "om:Property[@Name='ExceptionName']/@Value") ??
                                                   Eval(child, nsmgr, "om:Property[@Name='ExceptionVariable']/@Value") ??
                                                   "ex",
                                Sequence = seq++
                            };
                            
                            // ✅ REMOVED: Don't parse children here
                            // Standard recursion will populate shape.Children
                            
                            shape = catchShape;
                            break;

                        case "Compensation":
                            shape = new CompensationScopeShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "AtomicTransaction":
                            shape = new AtomicTransactionShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "LongRunningTransaction":
                            shape = new LongRunningTransactionShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        case "TransactionAttribute":
                            // Skip metadata shapes - don't create action for them
                            Trace.TraceInformation("[PARSE] Skipping metadata shape: TransactionAttribute");
                            continue;

                        case "VariableDeclaration":
                            shape = new VariableDeclarationShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                VarType = Eval(child, nsmgr, "om:Property[@Name='Type']/@Value"),
                                UseDefault = Eval(child, nsmgr, "om:Property[@Name='UseDefaultConstructor']/@Value"),
                                Sequence = seq++
                            };
                            break;

                        // Scope-level MessageDeclaration: BizTalk allows messages to be declared
                        // inside Scopes. In Logic Apps, messages are variables and must be
                        // initialized at workflow root level. Parse as VariableDeclarationShapeModel
                        // so CollectVariableDeclarationsRecursive hoists them as InitializeVariable.
                        case "MessageDeclaration":
                            shape = new VariableDeclarationShapeModel
                            {
                                ShapeType = "VariableDeclaration",
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value"),
                                VarType = Eval(child, nsmgr, "om:Property[@Name='Type']/@Value"),
                                Sequence = seq++
                            };
                            Trace.TraceInformation("[PARSER] Scope-level MessageDeclaration '{0}' parsed as VariableDeclaration for hoisting", shape.Name);
                            break;

                        // Catch-all for unknown shapes
                        default:
                            Trace.TraceWarning("[PARSE] Unknown shape type: {0}", type);
                            shape = new FallbackShapeModel
                            {
                                ShapeType = type,
                                Oid = oid,
                                Name = Eval(child, nsmgr, "om:Property[@Name='Name']/@Value") ?? $"Unknown_{type}",
                                Details = $"Unhandled shape type: {type}",
                                Sequence = seq++
                            };
                            break;
                    }
                }

                if (shape != null)
                {
                    shape.Parent = parent;
                    parent?.Children.Add(shape);

                    if (parent == null)
                    {
                        model.Shapes.Add(shape);
                    }

                    if (!string.IsNullOrEmpty(shape.Oid) && !oidMap.ContainsKey(shape.Oid))
                        oidMap.Add(shape.Oid, shape);

                    // Determine which shapes need recursive parsing
                    bool needsRecursion = true;

                    // These shapes handle their own children internally
                    if (shape is DecideShapeModel || shape is ConstructShapeModel ||
                        shape is MessageAssignmentShapeModel || shape is SwitchShapeModel)
                    {
                        needsRecursion = false;
                    }

                    Trace.TraceInformation("[PARSE] Shape {0} ({1}) - Will recurse: {2}", shape.Name, shape.ShapeType, needsRecursion);

                    if (needsRecursion)
                    {
                        ParseShapes(child, nsmgr, model, ref seq, oidMap, shape);
                    }
                }
            }
        }

        /// <summary>
        /// Parses a Construct message shape, extracting constructed messages and inner shapes.
        /// A Construct shape contains Transform and/or MessageAssignment shapes that build/modify messages.
        /// </summary>
        /// <param name="nav">The XPath navigator positioned at the Construct element.</param>
        /// <param name="nsmgr">The XML namespace manager.</param>
        /// <param name="parentSeq">The parent sequence number for inner shapes.</param>
        /// <returns>A ConstructShapeModel containing constructed messages and inner transformation/assignment shapes.</returns>
        private static ConstructShapeModel ParseConstruct(XPathNavigator nav, XmlNamespaceManager nsmgr, int parentSeq)
        {
            var shape = new ConstructShapeModel
            {
                ShapeType = "Construct",
                Name = Eval(nav, nsmgr, "om:Property[@Name='Name']/@Value"),
                Sequence = parentSeq
            };

            foreach (var msgRef in Select(nav, nsmgr, "om:Element[@Type='MessageRef']"))
            {
                var m = Eval(msgRef, nsmgr, "om:Property[@Name='Ref']/@Value");
                if (!string.IsNullOrEmpty(m)) shape.ConstructedMessages.Add(m);
            }

            foreach (var inner in Select(nav, nsmgr, "om:Element[@Type='Transform']"))
            {
                var t = ParseTransform(inner, nsmgr, parentSeq);
                t.UniqueId = t.Oid + "_" + parentSeq + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                shape.InnerShapes.Add(t);
            }

            foreach (var msgAssign in Select(nav, nsmgr, "om:Element[@Type='MessageAssignment']"))
            {
                var ma = new MessageAssignmentShapeModel
                {
                    ShapeType = "MessageAssignment",
                    Oid = Eval(msgAssign, nsmgr, "om:Property[@Name='OID']/@Value") ?? "",
                    Name = Eval(msgAssign, nsmgr, "om:Property[@Name='Name']/@Value"),
                    Expression = Eval(msgAssign, nsmgr, "om:Property[@Name='Expression']/@Value"),
                    Sequence = parentSeq,
                    UniqueId = Guid.NewGuid().ToString("N").Substring(0, 8)
                };
                shape.InnerShapes.Add(ma);
            }

            return shape;
        }

        /// <summary>
        /// Parses a Transform shape, extracting the XSLT map class name and input/output messages.
        /// Transform shapes apply XSLT maps to convert message formats.
        /// </summary>
        /// <param name="nav">The XPath navigator positioned at the Transform element.</param>
        /// <param name="nsmgr">The XML namespace manager.</param>
        /// <param name="seq">The sequence number for this shape.</param>
        /// <returns>A TransformShapeModel containing the map class name and message references.</returns>
        private static TransformShapeModel ParseTransform(XPathNavigator nav, XmlNamespaceManager nsmgr, int seq)
        {
            var shape = new TransformShapeModel
            {
                ShapeType = "Transform",
                Name = Eval(nav, nsmgr, "om:Property[@Name='Name']/@Value"),
                ClassName = Eval(nav, nsmgr, "om:Property[@Name='ClassName']/@Value"),
                Sequence = seq
            };
            foreach (var partRef in Select(nav, nsmgr, "om:Element[@Type='MessagePartRef']"))
            {
                var msgRef = Eval(partRef, nsmgr, "om:Property[@Name='MessageRef']/@Value");
                if (!string.IsNullOrEmpty(msgRef)) shape.InputMessages.Add(msgRef);
            }
            if (shape.InputMessages.Count == 2)
            {
                shape.OutputMessages.Add(shape.InputMessages[1]);
                var firstInput = shape.InputMessages[0];
                shape.InputMessages.Clear();
                shape.InputMessages.Add(firstInput);
            }
            return shape;
        }

        /// <summary>
        /// Executes an XPath query and returns all matching nodes as an enumerable sequence.
        /// Helper method to simplify XPath queries with namespace management.
        /// </summary>
        /// <param name="nav">The XPath navigator to query.</param>
        /// <param name="nsmgr">The XML namespace manager.</param>
        /// <param name="xpath">The XPath expression to execute.</param>
        /// <returns>An enumerable of XPathNavigator instances for all matching nodes.</returns>
        private static IEnumerable<XPathNavigator> Select(XPathNavigator nav, XmlNamespaceManager nsmgr, string xpath)
        {
            var it = nav.Select(xpath, nsmgr);
            while (it.MoveNext()) yield return it.Current;
        }

        /// <summary>
        /// Evaluates an XPath expression and returns the string value of the first matching node.
        /// Returns an empty string if no match is found.
        /// </summary>
        /// <param name="nav">The XPath navigator to query.</param>
        /// <param name="nsmgr">The XML namespace manager.</param>
        /// <param name="xpath">The XPath expression to evaluate.</param>
        /// <returns>The string value of the first matching node, or an empty string if no match.</returns>
        private static string Eval(XPathNavigator nav, XmlNamespaceManager nsmgr, string xpath)
        {
            var it = nav.Select(xpath, nsmgr);
            return it.MoveNext() ? it.Current.Value : string.Empty;
        }

        /// <summary>
        /// Determines the port direction based on BizTalk port modifier and signal properties.
        /// Implements ports are typically receive ports; Uses ports are typically send ports.
        /// </summary>
        /// <param name="portModifier">The port modifier ("Implements" or "Uses").</param>
        /// <param name="portSignal">The port signal value ("True" for one-way, otherwise request-response).</param>
        /// <returns>A PortDirection enum value indicating the port's communication pattern.</returns>
        private static PortDirection GetPortDirection(string portModifier, string portSignal)
        {
            if (portModifier == "Implements")
                return portSignal == "True" ? PortDirection.Receive : PortDirection.ReceiveSend;
            if (portModifier == "Uses")
                return portSignal == "True" ? PortDirection.SendReceive : PortDirection.Send;
            return PortDirection.None;
        }

        /// <summary>
        /// Finds the message type for a given logical message name in the orchestration.
        /// Used to resolve message schema types from message variable names.
        /// </summary>
        /// <param name="model">The orchestration model containing message declarations.</param>
        /// <param name="logicalName">The logical message variable name.</param>
        /// <returns>The message type (schema) if found; otherwise returns the logical name.</returns>
        public static string FindMessageType(OrchestrationModel model, string logicalName)
        {
            var m = model.Messages.FirstOrDefault(x => x.Name == logicalName);
            return m != null ? m.Type : logicalName;
        }

        /// <summary>
        /// Sanitizes a policy name for use in shape names by removing non-alphanumeric characters.
        /// Truncates to 40 characters maximum to ensure valid action names.
        /// </summary>
        /// <param name="policy">The policy name to sanitize.</param>
        /// <returns>A sanitized policy name safe for use in identifiers.</returns>
        private static string SafePolicySegment(string policy)
        {
            var chars = policy.Where(char.IsLetterOrDigit).ToArray();
            var sanitized = new string(chars);
            return sanitized.Length > 40 ? sanitized.Substring(0, 40) : sanitized;
        }

        /// <summary>
        /// Performs a case-insensitive string equality comparison.
        /// Helper method for comparing shape types and element names.
        /// </summary>
        /// <param name="a">The first string to compare.</param>
        /// <param name="b">The second string to compare.</param>
        /// <returns>True if the strings are equal ignoring case; otherwise false.</returns>
        private static bool EqualsIgnoreCase(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to load the connector schema registry from multiple search paths.
        /// Searches application directory, current directory, and standard schema locations.
        /// Returns null if the registry file cannot be found or loaded.
        /// </summary>
        /// <returns>A ConnectorSchemaRegistry instance loaded from file, or null if not found.</returns>
        /// <remarks>
        /// <para>
        /// The connector-registry.json file is the ONLY source of connector definitions.
        /// There is no fallback to hardcoded defaults - callers must handle null gracefully.
        /// </para>
        /// <para>Search paths (in order):</para>
        /// <list type="number">
        /// <item>Application base directory + Schemas/Connectors/connector-registry.json</item>
        /// <item>Current directory + Schemas/Connectors/connector-registry.json</item>
        /// <item>Application base directory + connector-registry.json</item>
        /// <item>Current directory + connector-registry.json</item>
        /// </list>
        /// <para>
        /// Logs warnings to console when registry cannot be loaded.
        /// Callers should check for null and handle missing connector definitions appropriately.
        /// </para>
        /// </remarks>
        public static ConnectorSchemaRegistry TryLoadConnectorRegistry()
        {
            var baseDirs = new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory };
            var relativePaths = new[]
            {
                Path.Combine("Schemas", "Connectors", "connector-registry.json"),
                "connector-registry.json"
            };

            var searchPaths = new List<string>();
            foreach (var baseDir in baseDirs)
            {
                foreach (var relativePath in relativePaths)
                {
                    searchPaths.Add(Path.Combine(baseDir, relativePath));
                }
            }

            var pathsArray = searchPaths.ToArray();

            foreach (var path in pathsArray)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        Trace.TraceInformation("[INFO] Loading connector registry from: {0}", path);
                        var registry = ConnectorSchemaRegistry.LoadFromFile(path);

                        if (registry != null)
                        {
                            Trace.TraceInformation("[SUCCESS] Connector registry loaded successfully with {0} connector(s)", registry.ConnectorCount);
                        }

                        return registry;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning("Failed to load connector registry from {0}: {1}", path, ex.Message);
                    }
                }
            }

            Trace.TraceWarning("Connector registry file (connector-registry.json) not found in any search path.");
            foreach (var path in pathsArray)
            {
                Trace.TraceWarning("  Searched: {0}", path);
            }
            Trace.TraceWarning("Workflow generation will proceed without connector schema information.");
            Trace.TraceWarning("Generated workflows may require manual connector configuration.");

            return null;
        }

        /// <summary>
        /// Analyzes receive shape patterns in a BizTalk orchestration for Logic Apps migration planning.
        /// </summary>
        /// <param name="model">The parsed orchestration model to analyze.</param>
        /// <returns>A ReceivePatternAnalysis describing the detected pattern and migration requirements.</returns>
        /// <remarks>
        /// Detects patterns such as:
        /// - Single trigger (standard migration)
        /// - Convoy (correlation-based sequential receives)
        /// - Listen (first-to-complete with timeout)
        /// - Invalid patterns (multiple activating receives in Parallel or sequential)
        /// 
        /// Use this before calling MapToLogicApp to understand migration complexity and requirements.
        /// </remarks>
        public static ReceivePatternAnalysis AnalyzeReceives(OrchestrationModel model)
        {
            return ReceivePatternAnalyzer.AnalyzeReceivePattern(model);
        }

        /// <summary>
        /// Delegates to <see cref="Diagnostics.OrchestrationDiagnostics.DiagnoseOrchestration"/>.
        /// Kept for backward compatibility with existing call sites.
        /// </summary>
        /// <param name="odxPath">The path to the BizTalk orchestration (.odx) file to diagnose.</param>
        public static void DiagnoseOrchestration(string odxPath)
        {
            Diagnostics.OrchestrationDiagnostics.DiagnoseOrchestration(odxPath);
        }
    }
}