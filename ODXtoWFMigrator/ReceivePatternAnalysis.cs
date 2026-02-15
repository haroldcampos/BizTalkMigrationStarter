// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Defines the types of receive patterns detected in BizTalk orchestrations.
    /// </summary>
    /// <remarks>
    /// These patterns determine how receive shapes are mapped to Logic Apps triggers and actions.
    /// Logic Apps workflows can only have ONE trigger, so pattern detection is critical for correct migration.
    /// </remarks>
    public enum ReceivePattern
    {
        /// <summary>
        /// No activating receive found. Workflow will use HTTP Request trigger (callable workflow).
        /// </summary>
        Callable,

        /// <summary>
        /// Single activating receive with no correlation. Maps to single trigger.
        /// </summary>
        SingleTrigger,

        /// <summary>
        /// Activating receive initializes correlation, subsequent receives follow correlation (Convoy pattern).
        /// Maps to session-enabled trigger with correlated receive actions.
        /// </summary>
        Convoy,

        /// <summary>
        /// Multiple activating receives in Listen shape (first-to-complete wins).
        /// Maps to single trigger with Switch/timeout handling.
        /// </summary>
        ListenFirstToComplete,

        /// <summary>
        /// Multiple activating receives in Parallel shape (all must complete).
        /// INVALID for Logic Apps - requires split into multiple workflows.
        /// </summary>
        ParallelAllMustComplete,

        /// <summary>
        /// Multiple sequential activating receives (invalid pattern).
        /// INVALID for Logic Apps - requires redesign.
        /// </summary>
        Invalid
    }

    /// <summary>
    /// Results of analyzing receive shape patterns in a BizTalk orchestration.
    /// </summary>
    /// <remarks>
    /// Provides pattern classification, primary/secondary receives, and migration guidance.
    /// Used by LogicAppsMapper to select appropriate trigger and action mapping strategies.
    /// </remarks>
    public sealed class ReceivePatternAnalysis
    {
        /// <summary>
        /// Gets or sets the detected receive pattern type.
        /// </summary>
        public ReceivePattern Pattern { get; set; }

        /// <summary>
        /// Gets or sets the primary activating receive shape (becomes the Logic Apps trigger).
        /// </summary>
        public ReceiveShapeModel PrimaryReceive { get; set; }

        /// <summary>
        /// Gets or sets additional receive shapes (become Logic Apps actions).
        /// </summary>
        public List<ReceiveShapeModel> SecondaryReceives { get; set; } = new List<ReceiveShapeModel>();

        /// <summary>
        /// Gets or sets whether the pattern requires session-based messaging (Service Bus sessions).
        /// </summary>
        public bool RequiresSessionSupport { get; set; }

        /// <summary>
        /// Gets or sets whether the pattern requires HTTP Request trigger (callable workflow).
        /// </summary>
        public bool RequiresRequestTrigger { get; set; }

        /// <summary>
        /// Gets or sets whether the pattern requires timeout handling (Listen shape).
        /// </summary>
        public bool RequiresTimeoutHandling { get; set; }

        /// <summary>
        /// Gets or sets the error message if the pattern is invalid or unsupported.
        /// </summary>
        public string MigrationError { get; set; }

        /// <summary>
        /// Gets or sets warning messages for the migration (non-blocking issues).
        /// </summary>
        public List<string> MigrationWarnings { get; set; } = new List<string>();

        /// <summary>
        /// Gets whether the pattern is valid for Logic Apps migration.
        /// </summary>
        public bool IsValid => this.Pattern != ReceivePattern.Invalid && 
                              this.Pattern != ReceivePattern.ParallelAllMustComplete;

        /// <summary>
        /// Gets the count of all receive shapes (primary + secondary).
        /// </summary>
        public int TotalReceiveCount => (this.PrimaryReceive != null ? 1 : 0) + this.SecondaryReceives.Count;
    }

    /// <summary>
    /// Analyzer for detecting and classifying receive shape patterns in BizTalk orchestrations.
    /// </summary>
    /// <remarks>
    /// Detects patterns such as single receive, convoy (correlation), Listen (first-to-complete),
    /// and invalid patterns that cannot be migrated to Logic Apps without redesign.
    /// </remarks>
    public static class ReceivePatternAnalyzer
    {
        /// <summary>
        /// Analyzes receive shapes in an orchestration to determine the migration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>A ReceivePatternAnalysis describing the detected pattern and migration requirements.</returns>
        /// <remarks>
        /// Pattern detection priority:
        /// 1. No activating receives ? Callable (Request trigger)
        /// 2. Single activating receive ? SingleTrigger or Convoy (if correlation used)
        /// 3. Multiple in Listen ? ListenFirstToComplete
        /// 4. Multiple in Parallel ? ParallelAllMustComplete (INVALID)
        /// 5. Multiple sequential ? Invalid
        /// </remarks>
        public static ReceivePatternAnalysis AnalyzeReceivePattern(OrchestrationModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var allShapes = GetAllShapesRecursive(model.Shapes).ToList();
            var allReceives = allShapes.OfType<ReceiveShapeModel>().ToList();
            var activatingReceives = allReceives.Where(r => r.Activate).ToList();

            Trace.TraceInformation("[RECEIVE-ANALYSIS] Orchestration: {0}, Total receives: {1}, Activating: {2}",
                model.FullName, allReceives.Count, activatingReceives.Count);

            // Case 1: No activating receives ? Callable workflow
            if (activatingReceives.Count == 0)
            {
                return new ReceivePatternAnalysis
                {
                    Pattern = ReceivePattern.Callable,
                    RequiresRequestTrigger = true,
                    MigrationWarnings = new List<string>
                    {
                        "No activating Receive shapes found. Workflow will use HTTP Request trigger (callable workflow)."
                    }
                };
            }

            // Case 2: Single activating receive
            if (activatingReceives.Count == 1)
            {
                var receive = activatingReceives[0];
                
                // Check for convoy pattern (correlation initialization + following receives)
                if (receive.InitializesCorrelationSets.Any())
                {
                    var followingReceives = allReceives
                        .Where(r => !r.Activate && r.FollowsCorrelationSets.Any())
                        .ToList();
                    
                    if (followingReceives.Any())
                    {
                        return new ReceivePatternAnalysis
                        {
                            Pattern = ReceivePattern.Convoy,
                            PrimaryReceive = receive,
                            SecondaryReceives = followingReceives,
                            RequiresSessionSupport = true,
                            MigrationWarnings = new List<string>
                            {
                                $"Convoy pattern detected with {followingReceives.Count} correlated receive(s). " +
                                "Requires Service Bus with session support or custom correlation implementation."
                            }
                        };
                    }
                }
                
                // Simple single receive
                return new ReceivePatternAnalysis
                {
                    Pattern = ReceivePattern.SingleTrigger,
                    PrimaryReceive = receive
                };
            }

            // Case 3: Multiple activating receives - analyze context

            // Check if all are in Listen shape (first-to-complete)
            var listenParents = new List<ListenShapeModel>();
            foreach (var receive in activatingReceives)
            {
                var listenParent = FindAncestorOfType<ListenShapeModel>(receive);
                if (listenParent != null)
                {
                    listenParents.Add(listenParent);
                }
            }

            if (listenParents.Count == activatingReceives.Count &&
                listenParents.Distinct().Count() == 1)
            {
                return new ReceivePatternAnalysis
                {
                    Pattern = ReceivePattern.ListenFirstToComplete,
                    PrimaryReceive = activatingReceives[0],
                    SecondaryReceives = activatingReceives.Skip(1).ToList(),
                    RequiresTimeoutHandling = true,
                    MigrationWarnings = new List<string>
                    {
                        $"Listen shape with {activatingReceives.Count} activating receives detected. " +
                        "Will map first receive to trigger and others to Switch/timeout actions. " +
                        "Note: Logic Apps does not natively support 'first-to-complete cancels others' semantics."
                    }
                };
            }

            // Check if all are in Parallel shape (all must complete) - INVALID
            var parallelParents = new List<ParallelShapeModel>();
            foreach (var receive in activatingReceives)
            {
                var parallelParent = FindAncestorOfType<ParallelShapeModel>(receive);
                if (parallelParent != null)
                {
                    parallelParents.Add(parallelParent);
                }
            }

            if (parallelParents.Count == activatingReceives.Count)
            {
                return new ReceivePatternAnalysis
                {
                    Pattern = ReceivePattern.ParallelAllMustComplete,
                    PrimaryReceive = activatingReceives[0],
                    SecondaryReceives = activatingReceives.Skip(1).ToList(),
                    MigrationError = $"INVALID PATTERN: {activatingReceives.Count} activating Receive shapes in Parallel branches detected. " +
                        "Azure Logic Apps workflows can only have ONE trigger. " +
                        "Recommendation: Split into multiple workflows or use correlation-based sequential receives."
                };
            }

            // Case 4: Multiple sequential activating receives - INVALID
            return new ReceivePatternAnalysis
            {
                Pattern = ReceivePattern.Invalid,
                PrimaryReceive = activatingReceives[0],
                SecondaryReceives = activatingReceives.Skip(1).ToList(),
                MigrationError = $"INVALID PATTERN: {activatingReceives.Count} sequential activating Receive shapes detected. " +
                    "Azure Logic Apps workflows can only have ONE trigger. " +
                    "Recommendation: Redesign to use correlation (convoy pattern) or split into multiple workflows."
            };
        }

        /// <summary>
        /// Recursively collects all shapes from a shape hierarchy.
        /// </summary>
        /// <param name="shapes">The root-level shapes to traverse.</param>
        /// <returns>All shapes including nested children, decision branches, etc.</returns>
        private static IEnumerable<ShapeModel> GetAllShapesRecursive(IEnumerable<ShapeModel> shapes)
        {
            foreach (var shape in shapes)
            {
                yield return shape;

                // Recursively process children
                foreach (var child in GetAllShapesRecursive(shape.Children))
                {
                    yield return child;
                }

                // Process Decision branches
                if (shape is DecideShapeModel decide)
                {
                    foreach (var trueShape in GetAllShapesRecursive(decide.TrueBranch))
                    {
                        yield return trueShape;
                    }
                    foreach (var falseShape in GetAllShapesRecursive(decide.FalseBranch))
                    {
                        yield return falseShape;
                    }
                }

                // Process Switch cases
                if (shape is SwitchShapeModel switchShape)
                {
                    foreach (var caseShapes in switchShape.Cases.Values)
                    {
                        foreach (var caseShape in GetAllShapesRecursive(caseShapes))
                        {
                            yield return caseShape;
                        }
                    }
                    foreach (var defaultShape in GetAllShapesRecursive(switchShape.DefaultCase))
                    {
                        yield return defaultShape;
                    }
                }

                // Process Listen branches
                if (shape is ListenShapeModel listen)
                {
                    foreach (var branch in listen.Branches)
                    {
                        foreach (var branchShape in GetAllShapesRecursive(new[] { branch }))
                        {
                            yield return branchShape;
                        }
                    }
                }

                // Process Construct inner shapes
                if (shape is ConstructShapeModel construct)
                {
                    foreach (var inner in construct.InnerShapes)
                    {
                        yield return inner;
                    }
                }

                // Process Catch exception handlers
                if (shape is CatchShapeModel catchModel)
                {
                    foreach (var handler in GetAllShapesRecursive(catchModel.ExceptionHandlers))
                    {
                        yield return handler;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the first ancestor shape of a specific type.
        /// </summary>
        /// <typeparam name="T">The shape type to find (must derive from ShapeModel).</typeparam>
        /// <param name="shape">The starting shape to search from.</param>
        /// <returns>The first ancestor of type T, or null if not found.</returns>
        private static T FindAncestorOfType<T>(ShapeModel shape) where T : ShapeModel
        {
            var current = shape.Parent;
            while (current != null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = current.Parent;
            }
            return null;
        }
    }
}
