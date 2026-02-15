// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator.Diagnostics
{
    /// <summary>
    /// Provides diagnostic output for BizTalk orchestration analysis.
    /// Prints shape counts, hierarchy trees, and decision details to console.
    /// </summary>
    /// <remarks>
    /// This is a CLI-only utility intended for the <c>diagnose</c> command.
    /// Console output is intentional — this class is never loaded by the MCP server.
    /// </remarks>
    internal static class OrchestrationDiagnostics
    {
        /// <summary>
        /// Performs diagnostic analysis on a BizTalk orchestration and outputs detailed shape hierarchy.
        /// Prints shape counts, complete hierarchy tree, and decision/switch details to console.
        /// Useful for understanding orchestration structure and troubleshooting parsing issues.
        /// </summary>
        /// <param name="odxPath">The path to the BizTalk orchestration (.odx) file to diagnose.</param>
        public static void DiagnoseOrchestration(string odxPath)
        {
            var model = BizTalkOrchestrationParser.ParseOdx(odxPath);

            Console.WriteLine("\n================================================================================");
            Console.WriteLine($"=== Orchestration Diagnostic: {model.FullName} ===");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"Top-Level Shapes: {model.Shapes.Count}");

            int totalDecisions = 0;
            int totalSwitches = 0;
            int totalMessageAssignments = 0;
            int totalStarts = 0;
            int totalReceives = 0;
            int totalSends = 0;
            int totalConstructs = 0;
            int totalScopes = 0;

            CountShapesRecursive(model.Shapes, ref totalDecisions, ref totalSwitches, ref totalMessageAssignments,
                ref totalStarts, ref totalReceives, ref totalSends, ref totalConstructs, ref totalScopes, 0);

            Console.WriteLine("\n=== TOTAL SHAPE COUNTS (including nested) ===");
            Console.WriteLine($"  \u2713 Decision/If Shapes: {totalDecisions}");
            Console.WriteLine($"  \u2713 Switch Shapes: {totalSwitches}");
            Console.WriteLine($"  \u2713 Construct Shapes: {totalConstructs}");
            Console.WriteLine($"  \u2713 MessageAssignment Shapes: {totalMessageAssignments}");
            Console.WriteLine($"  \u2713 Start Orchestration Shapes: {totalStarts}");
            Console.WriteLine($"  \u2713 Receive Shapes: {totalReceives}");
            Console.WriteLine($"  \u2713 Send Shapes: {totalSends}");
            Console.WriteLine($"  \u2713 Scope Shapes: {totalScopes}");

            Console.WriteLine("\n=== COMPLETE SHAPE HIERARCHY ===");
            foreach (var shape in model.Shapes.OrderBy(s => s.Sequence))
            {
                PrintShapeTree(shape, 0);
            }

            Console.WriteLine("\n================================================================================");
            Console.WriteLine("=== DECISION DETAILS ===");
            Console.WriteLine("================================================================================");
            PrintDecisionDetails(model.Shapes, 0);
        }

        /// <summary>
        /// Recursively counts shapes by type throughout the entire orchestration hierarchy.
        /// Traverses decision branches, switch cases, construct inner shapes, and all child shapes.
        /// </summary>
        private static void CountShapesRecursive(
            IEnumerable<ShapeModel> shapes,
            ref int decisions,
            ref int switches,
            ref int msgAssignments,
            ref int starts,
            ref int receives,
            ref int sends,
            ref int constructs,
            ref int scopes,
            int depth)
        {
            foreach (var shape in shapes)
            {
                if (shape is DecideShapeModel decide)
                {
                    decisions++;
                    CountShapesRecursive(decide.TrueBranch, ref decisions, ref switches, ref msgAssignments, ref starts, ref receives, ref sends, ref constructs, ref scopes, depth + 1);
                    CountShapesRecursive(decide.FalseBranch, ref decisions, ref switches, ref msgAssignments, ref starts, ref receives, ref sends, ref constructs, ref scopes, depth + 1);
                }
                else if (shape is SwitchShapeModel switchShape)
                {
                    switches++;
                    foreach (var caseShapes in switchShape.Cases.Values)
                    {
                        CountShapesRecursive(caseShapes, ref decisions, ref switches, ref msgAssignments, ref starts, ref receives, ref sends, ref constructs, ref scopes, depth + 1);
                    }
                    if (switchShape.DefaultCase.Count > 0)
                    {
                        CountShapesRecursive(switchShape.DefaultCase, ref decisions, ref switches, ref msgAssignments, ref starts, ref receives, ref sends, ref constructs, ref scopes, depth + 1);
                    }
                }
                else if (shape is ConstructShapeModel construct)
                {
                    constructs++;
                    foreach (var inner in construct.InnerShapes)
                    {
                        if (inner is MessageAssignmentShapeModel) msgAssignments++;
                    }
                }
                else if (shape is MessageAssignmentShapeModel)
                {
                    msgAssignments++;
                }
                else if (shape is StartShapeModel)
                {
                    starts++;
                }
                else if (shape is ReceiveShapeModel)
                {
                    receives++;
                }
                else if (shape is SendShapeModel)
                {
                    sends++;
                }
                else if (shape is ScopeShapeModel || shape is AtomicTransactionShapeModel || shape is LongRunningTransactionShapeModel)
                {
                    scopes++;
                }

                if (shape.Children.Count > 0)
                {
                    CountShapesRecursive(shape.Children, ref decisions, ref switches, ref msgAssignments, ref starts, ref receives, ref sends, ref constructs, ref scopes, depth + 1);
                }
            }
        }

        /// <summary>
        /// Recursively prints the shape hierarchy tree with indentation to console.
        /// Shows shape type, name, sequence, unique ID, and special details for Decision/Switch/Construct shapes.
        /// </summary>
        private static void PrintShapeTree(ShapeModel shape, int indent)
        {
            string prefix = new string(' ', indent * 2);
            string id = !string.IsNullOrEmpty(shape.UniqueId) ? $" [{shape.UniqueId.Substring(0, 8)}]" : "";
            string seq = $" [Seq:{shape.Sequence}]";

            Console.WriteLine($"{prefix}[{shape.ShapeType}] {shape.Name}{id}{seq}");

            if (shape is DecideShapeModel decide)
            {
                Console.WriteLine($"{prefix}  Expression: {(string.IsNullOrEmpty(decide.Expression) ? "(no expression)" : decide.Expression.Substring(0, Math.Min(60, decide.Expression.Length)))}");

                if (decide.TrueBranch.Count > 0)
                {
                    Console.WriteLine($"{prefix}  \u2713 TRUE branch ({decide.TrueBranch.Count} shapes):");
                    foreach (var child in decide.TrueBranch.OrderBy(c => c.Sequence))
                        PrintShapeTree(child, indent + 2);
                }
                else
                {
                    Console.WriteLine($"{prefix}  \u2713 TRUE branch (empty)");
                }

                if (decide.FalseBranch.Count > 0)
                {
                    Console.WriteLine($"{prefix}  \u2717 FALSE branch ({decide.FalseBranch.Count} shapes):");
                    foreach (var child in decide.FalseBranch.OrderBy(c => c.Sequence))
                        PrintShapeTree(child, indent + 2);
                }
                else
                {
                    Console.WriteLine($"{prefix}  \u2717 FALSE branch (empty)");
                }
            }

            if (shape is SwitchShapeModel switchShape)
            {
                Console.WriteLine($"{prefix}  Expression: {(string.IsNullOrEmpty(switchShape.Expression) ? "(no expression)" : switchShape.Expression.Substring(0, Math.Min(60, switchShape.Expression.Length)))}");

                if (switchShape.Cases.Count > 0)
                {
                    Console.WriteLine($"{prefix}  Cases ({switchShape.Cases.Count}):");
                    foreach (var caseEntry in switchShape.Cases)
                    {
                        Console.WriteLine($"{prefix}    Case '{caseEntry.Key}': {caseEntry.Value.Count} shapes");
                        foreach (var caseShape in caseEntry.Value.OrderBy(c => c.Sequence))
                        {
                            PrintShapeTree(caseShape, indent + 3);
                        }
                    }
                }

                if (switchShape.DefaultCase.Count > 0)
                {
                    Console.WriteLine($"{prefix}  Default case ({switchShape.DefaultCase.Count} shapes):");
                    foreach (var defaultShape in switchShape.DefaultCase.OrderBy(c => c.Sequence))
                    {
                        PrintShapeTree(defaultShape, indent + 2);
                    }
                }
            }

            if (shape is ConstructShapeModel construct && construct.InnerShapes.Count > 0)
            {
                Console.WriteLine($"{prefix}  Inner shapes ({construct.InnerShapes.Count}):");
                foreach (var inner in construct.InnerShapes)
                    PrintShapeTree(inner, indent + 2);
            }

            if (shape is CatchShapeModel catchModel)
            {
                Console.WriteLine($"{prefix}  Exception Type: {catchModel.ExceptionType}");
                Console.WriteLine($"{prefix}  Exception Variable: {catchModel.ExceptionVariable}");
                if (catchModel.ExceptionHandlers.Count > 0)
                {
                    Console.WriteLine($"{prefix}  Exception Handlers ({catchModel.ExceptionHandlers.Count} shapes):");
                    foreach (var handler in catchModel.ExceptionHandlers.OrderBy(h => h.Sequence))
                    {
                        PrintShapeTree(handler, indent + 2);
                    }
                }
            }

            if (!(shape is DecideShapeModel) && !(shape is SwitchShapeModel))
            {
                foreach (var child in shape.Children.OrderBy(c => c.Sequence))
                {
                    PrintShapeTree(child, indent + 1);
                }
            }
        }

        /// <summary>
        /// Recursively prints detailed information about Decision and Switch shapes.
        /// Shows expressions, branch counts, case values, and recursively explores nested decisions.
        /// </summary>
        private static void PrintDecisionDetails(IEnumerable<ShapeModel> shapes, int level)
        {
            foreach (var shape in shapes.OrderBy(s => s.Sequence))
            {
                if (shape is DecideShapeModel decide)
                {
                    string indent = new string(' ', level * 2);
                    Console.WriteLine($"{indent}\u25B6 Decision: '{decide.Name}'");
                    Console.WriteLine($"{indent}  Sequence: {decide.Sequence}");
                    Console.WriteLine($"{indent}  UniqueId: {decide.UniqueId}");
                    Console.WriteLine($"{indent}  Expression: {decide.Expression}");
                    Console.WriteLine($"{indent}  True Branch: {decide.TrueBranch.Count} shapes");
                    Console.WriteLine($"{indent}  False Branch: {decide.FalseBranch.Count} shapes");

                    if (decide.TrueBranch.Count > 0)
                    {
                        Console.WriteLine($"{indent}  TRUE:");
                        PrintDecisionDetails(decide.TrueBranch, level + 2);
                    }

                    if (decide.FalseBranch.Count > 0)
                    {
                        Console.WriteLine($"{indent}  FALSE:");
                        PrintDecisionDetails(decide.FalseBranch, level + 2);
                    }

                    Console.WriteLine();
                }
                else if (shape is SwitchShapeModel switchShape)
                {
                    string indent = new string(' ', level * 2);
                    Console.WriteLine($"{indent}\u25B6 Switch: '{switchShape.Name}'");
                    Console.WriteLine($"{indent}  Sequence: {switchShape.Sequence}");
                    Console.WriteLine($"{indent}  UniqueId: {switchShape.UniqueId}");
                    Console.WriteLine($"{indent}  Expression: {switchShape.Expression}");
                    Console.WriteLine($"{indent}  Cases: {switchShape.Cases.Count}");
                    Console.WriteLine($"{indent}  Has Default: {(switchShape.DefaultCase.Count > 0 ? "Yes" : "No")}");

                    foreach (var caseEntry in switchShape.Cases)
                    {
                        Console.WriteLine($"{indent}  CASE '{caseEntry.Key}':");
                        PrintDecisionDetails(caseEntry.Value, level + 2);
                    }

                    if (switchShape.DefaultCase.Count > 0)
                    {
                        Console.WriteLine($"{indent}  DEFAULT:");
                        PrintDecisionDetails(switchShape.DefaultCase, level + 2);
                    }

                    Console.WriteLine();
                }
                else if (shape.Children.Count > 0)
                {
                    PrintDecisionDetails(shape.Children, level);
                }
            }
        }
    }
}
