// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Specifies the output format for migration reports.
    /// </summary>
    public enum ReportFormat
    {
        /// <summary>
        /// HTML format with styled presentation.
        /// </summary>
        Html,
        
        /// <summary>
        /// Markdown format for documentation systems.
        /// </summary>
        Markdown
    }

    /// <summary>
    /// Generates comprehensive migration readiness reports for BizTalk orchestrations.
    /// </summary>
    /// <remarks>
    /// Produces detailed HTML or Markdown reports including complexity analysis, pattern detection,
    /// migration readiness scoring, and actionable recommendations for Logic Apps migration.
    /// </remarks>
    public static class OrchestrationReportGenerator
    {
        /// <summary>
        /// Exports detected integration patterns as machine-readable data for refactored workflow generation.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <returns>List of detected patterns with metadata for both cloud and on-premises deployments.</returns>
        public static List<IntegrationPattern> ExportDetectedPatterns(OrchestrationModel model)
        {
            var readiness = AnalyzeMigrationReadiness(model);
            return readiness.DetectedPatterns;
        }

        /// <summary>
        /// Exports a diagnostic migration report for a single BizTalk orchestration.
        /// </summary>
        /// <param name="odxPath">Path to the ODX orchestration file.</param>
        /// <param name="outputPath">Optional output path for the report file. If null, uses the ODX directory with "_MigrationReport" suffix.</param>
        /// <param name="format">Report format (HTML or Markdown).</param>
        public static void ExportDiagnosticReport(string odxPath, string outputPath = null, ReportFormat format = ReportFormat.Html)
        {
            var model = BizTalkOrchestrationParser.ParseOdx(odxPath);

            if (string.IsNullOrEmpty(outputPath))
            {
                var directory = Path.GetDirectoryName(odxPath) ?? Environment.CurrentDirectory;
                var baseName = Path.GetFileNameWithoutExtension(odxPath);
                var extension = format == ReportFormat.Html ? ".html" : ".md";
                outputPath = Path.Combine(directory, baseName + "_MigrationReport" + extension);
            }

            string report = format == ReportFormat.Html
                ? GenerateHtmlReport(model, odxPath)
                : GenerateMarkdownReport(model, odxPath);

            File.WriteAllText(outputPath, report);
            Console.WriteLine("[SUCCESS] Migration report generated: " + outputPath);
        }

        /// <summary>
        /// Processes multiple orchestration files and generates individual reports plus a batch summary.
        /// </summary>
        /// <param name="odxPaths">Array of paths to ODX orchestration files to process.</param>
        /// <param name="bindingsFilePath">Optional path to BizTalk bindings file for enhanced analysis.</param>
        /// <param name="outputDirectory">Directory for output reports. If null, uses the first orchestration's directory.</param>
        /// <param name="format">Report format (HTML or Markdown).</param>
        /// <exception cref="ArgumentException">Thrown when no orchestration files are provided.</exception>
        public static void ExportBatchDiagnosticReport(string[] odxPaths, string bindingsFilePath, string outputDirectory, ReportFormat format)
        {
            if (odxPaths == null || odxPaths.Length == 0)
            {
                throw new ArgumentException("At least one orchestration file must be provided.", nameof(odxPaths));
            }

            // Use the directory of the first orchestration if no output directory specified
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Path.GetDirectoryName(odxPaths[0]) ?? Environment.CurrentDirectory;
            }

            var batchResults = new List<OrchestrationBatchResult>();
            var failedOrchestrations = new List<string>();

            // Process each orchestration
            foreach (var odxPath in odxPaths)
            {
                try
                {
                    Console.WriteLine($"Processing: {Path.GetFileName(odxPath)}");
                    var model = BizTalkOrchestrationParser.ParseOdx(odxPath);

                    // Apply bindings if available
                    if (!string.IsNullOrEmpty(bindingsFilePath) && File.Exists(bindingsFilePath))
                    {
                        // TODO: Apply bindings to the model here
                        // This would require extending the parser to support binding application
                    }

                    var stats = CalculateStatistics(model);
                    var complexityScore = CalculateComplexityScore(model, stats);
                    var migrationReadiness = AnalyzeMigrationReadiness(model);

                    batchResults.Add(new OrchestrationBatchResult
                    {
                        FilePath = odxPath,
                        Model = model,
                        Statistics = stats,
                        ComplexityScore = complexityScore,
                        MigrationReadiness = migrationReadiness,
                        Success = true
                    });

                    // Generate individual report
                    var individualOutputPath = Path.Combine(outputDirectory,
                        Path.GetFileNameWithoutExtension(odxPath) + "_MigrationReport" + (format == ReportFormat.Html ? ".html" : ".md"));

                    string individualReport = format == ReportFormat.Html
                        ? GenerateHtmlReport(model, odxPath)
                        : GenerateMarkdownReport(model, odxPath);

                    File.WriteAllText(individualOutputPath, individualReport);
                    Console.WriteLine($"  ✓ Report generated: {Path.GetFileName(individualOutputPath)}");
                }
                catch (Exception ex)
                {
                    failedOrchestrations.Add(odxPath);
                    batchResults.Add(new OrchestrationBatchResult
                    {
                        FilePath = odxPath,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    Console.WriteLine($"  ✗ Failed: {ex.Message}");
                }
            }

            // Generate batch summary report
            var summaryPath = Path.Combine(outputDirectory,
                $"BatchMigrationSummary_{DateTime.Now:yyyyMMdd_HHmmss}" + (format == ReportFormat.Html ? ".html" : ".md"));

            string summaryReport = format == ReportFormat.Html
                ? GenerateBatchHtmlSummary(batchResults, bindingsFilePath)
                : GenerateBatchMarkdownSummary(batchResults, bindingsFilePath);

            File.WriteAllText(summaryPath, summaryReport);

            Console.WriteLine();
            Console.WriteLine("=" + new string('=', 50));
            Console.WriteLine($"[BATCH COMPLETE] Processed {batchResults.Count} orchestration(s)");
            Console.WriteLine($"  Successful: {batchResults.Count(r => r.Success)}");
            Console.WriteLine($"  Failed: {batchResults.Count(r => !r.Success)}");
            Console.WriteLine($"  Summary report: {summaryPath}");
            Console.WriteLine("=" + new string('=', 50));
        }

        /// <summary>
        /// Generates an HTML batch summary report for multiple orchestrations.
        /// </summary>
        /// <param name="results">Collection of batch processing results.</param>
        /// <param name="bindingsFilePath">Path to the bindings file used (if any).</param>
        /// <returns>Complete HTML document as a string.</returns>
        private static string GenerateBatchHtmlSummary(List<OrchestrationBatchResult> results, string bindingsFilePath)
        {
            var sb = new StringBuilder();
            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            var avgComplexity = results.Where(r => r.Success).Average(r => (double?)r.ComplexityScore?.Score) ?? 0;
            var avgReadiness = results.Where(r => r.Success).Average(r => (double?)r.MigrationReadiness?.ReadinessPercentage) ?? 0;

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Batch Migration Report Summary</title>");
            AppendHtmlStyles(sb);
            AppendBatchStyles(sb);
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            // Header
            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("        <h1>📊 Batch Migration Report Summary</h1>");
            sb.AppendLine("        <p>Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</p>");
            if (!string.IsNullOrEmpty(bindingsFilePath))
            {
                sb.AppendLine("        <p>Bindings File: " + WebUtility.HtmlEncode(Path.GetFileName(bindingsFilePath)) + "</p>");
            }
            sb.AppendLine("    </div>");

            // Overall Statistics
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>📈 Overall Statistics</h2>");
            sb.AppendLine("        <div class=\"stats-grid\">");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + results.Count + "</div><div class=\"stat-label\">Total Orchestrations</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + successCount + "</div><div class=\"stat-label\">Successfully Processed</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + failCount + "</div><div class=\"stat-label\">Failed</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + avgComplexity.ToString("F0") + "/100</div><div class=\"stat-label\">Avg Complexity</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + avgReadiness.ToString("F0") + "%</div><div class=\"stat-label\">Avg Readiness</div></div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");

            // Orchestration Details Table
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>📋 Orchestration Details</h2>");
            sb.AppendLine("        <table>");
            sb.AppendLine("            <thead>");
            sb.AppendLine("                <tr>");
            sb.AppendLine("                    <th>Orchestration</th>");
            sb.AppendLine("                    <th>Status</th>");
            sb.AppendLine("                    <th>Complexity</th>");
            sb.AppendLine("                    <th>Readiness</th>");
            sb.AppendLine("                    <th>Total Shapes</th>");
            sb.AppendLine("                    <th>Ports</th>");
            sb.AppendLine("                    <th>Issues</th>");
            sb.AppendLine("                </tr>");
            sb.AppendLine("            </thead>");
            sb.AppendLine("            <tbody>");

            foreach (var result in results.OrderBy(r => Path.GetFileName(r.FilePath)))
            {
                sb.AppendLine("                <tr>");
                sb.AppendLine("                    <td>" + WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(result.FilePath)) + "</td>");

                if (result.Success)
                {
                    var complexityClass = result.ComplexityScore.Level.ToLower();
                    var readinessClass = result.MigrationReadiness.ReadinessPercentage >= 70 ? "high" :
                                       result.MigrationReadiness.ReadinessPercentage >= 40 ? "medium" : "low";

                    sb.AppendLine("                    <td><span class=\"badge badge-success\">✓ Success</span></td>");
                    sb.AppendLine("                    <td><span class=\"badge badge-" + complexityClass + "\">" + result.ComplexityScore.Score + "/100</span></td>");
                    sb.AppendLine("                    <td><span class=\"readiness-" + readinessClass + "\">" + result.MigrationReadiness.ReadinessPercentage + "%</span></td>");
                    sb.AppendLine("                    <td>" + result.Statistics.TotalShapes + "</td>");
                    sb.AppendLine("                    <td>" + result.Statistics.Ports + "</td>");
                    sb.AppendLine("                    <td>" + result.MigrationReadiness.Issues.Count + "</td>");
                }
                else
                {
                    sb.AppendLine("                    <td><span class=\"badge badge-error\">✗ Failed</span></td>");
                    sb.AppendLine("                    <td colspan=\"5\">" + WebUtility.HtmlEncode(result.ErrorMessage) + "</td>");
                }

                sb.AppendLine("                </tr>");
            }

            sb.AppendLine("            </tbody>");
            sb.AppendLine("        </table>");
            sb.AppendLine("    </div>");

            // Migration Readiness Distribution
            if (successCount > 0)
            {
                sb.AppendLine("    <div class=\"container\">");
                sb.AppendLine("        <h2>🎯 Migration Readiness Distribution</h2>");
                var highReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage >= 70);
                var mediumReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage >= 40 && r.MigrationReadiness.ReadinessPercentage < 70);
                var lowReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage < 40);

                sb.AppendLine("        <div class=\"readiness-chart\">");
                sb.AppendLine("            <div class=\"readiness-bar high\" style=\"width: " + (highReadiness * 100.0 / successCount).ToString("F1") + "%\">");
                sb.AppendLine("                High (" + highReadiness + ")");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"readiness-bar medium\" style=\"width: " + (mediumReadiness * 100.0 / successCount).ToString("F1") + "%\">");
                sb.AppendLine("                Medium (" + mediumReadiness + ")");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"readiness-bar low\" style=\"width: " + (lowReadiness * 100.0 / successCount).ToString("F1") + "%\">");
                sb.AppendLine("                Low (" + lowReadiness + ")");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");
                sb.AppendLine("    </div>");
            }

            // Common Issues
            var allIssues = results.Where(r => r.Success)
                .SelectMany(r => r.MigrationReadiness.Issues)
                .GroupBy(i => i.Description)
                .OrderByDescending(g => g.Count())
                .Take(10);

            if (allIssues.Any())
            {
                sb.AppendLine("    <div class=\"container\">");
                sb.AppendLine("        <h2>⚠️ Common Migration Issues</h2>");
                sb.AppendLine("        <ul>");
                foreach (var issueGroup in allIssues)
                {
                    sb.AppendLine("            <li>" + WebUtility.HtmlEncode(issueGroup.Key) + " (found in " + issueGroup.Count() + " orchestration(s))</li>");
                }
                sb.AppendLine("        </ul>");
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a Markdown batch summary report for multiple orchestrations.
        /// </summary>
        /// <param name="results">Collection of batch processing results.</param>
        /// <param name="bindingsFilePath">Path to the bindings file used (if any).</param>
        /// <returns>Complete Markdown document as a string.</returns>
        private static string GenerateBatchMarkdownSummary(List<OrchestrationBatchResult> results, string bindingsFilePath)
        {
            var sb = new StringBuilder();
            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            var avgComplexity = results.Where(r => r.Success).Average(r => (double?)r.ComplexityScore?.Score) ?? 0;
            var avgReadiness = results.Where(r => r.Success).Average(r => (double?)r.MigrationReadiness?.ReadinessPercentage) ?? 0;

            sb.AppendLine("# Batch Migration Report Summary");
            sb.AppendLine();
            sb.AppendLine("**Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!string.IsNullOrEmpty(bindingsFilePath))
            {
                sb.AppendLine("**Bindings File:** " + Path.GetFileName(bindingsFilePath));
            }
            sb.AppendLine();

            sb.AppendLine("## 📈 Overall Statistics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine("| Total Orchestrations | " + results.Count + " |");
            sb.AppendLine("| Successfully Processed | " + successCount + " |");
            sb.AppendLine("| Failed | " + failCount + " |");
            sb.AppendLine("| Average Complexity | " + avgComplexity.ToString("F0") + "/100 |");
            sb.AppendLine("| Average Readiness | " + avgReadiness.ToString("F0") + "% |");
            sb.AppendLine();

            sb.AppendLine("## 📋 Orchestration Details");
            sb.AppendLine();
            sb.AppendLine("| Orchestration | Status | Complexity | Readiness | Total Shapes | Ports | Issues |");
            sb.AppendLine("|---------------|--------|------------|-----------|--------------|-------|--------|");

            foreach (var result in results.OrderBy(r => Path.GetFileName(r.FilePath)))
            {
                var name = Path.GetFileNameWithoutExtension(result.FilePath);
                if (result.Success)
                {
                    sb.AppendLine("| " + name + " | ✓ Success | " + result.ComplexityScore.Score + "/100 | " +
                                result.MigrationReadiness.ReadinessPercentage + "% | " +
                                result.Statistics.TotalShapes + " | " +
                                result.Statistics.Ports + " | " +
                                result.MigrationReadiness.Issues.Count + " |");
                }
                else
                {
                    sb.AppendLine("| " + name + " | ✗ Failed | - | - | - | - | " + result.ErrorMessage + " |");
                }
            }
            sb.AppendLine();

            // Migration Readiness Distribution
            if (successCount > 0)
            {
                sb.AppendLine("## 🎯 Migration Readiness Distribution");
                sb.AppendLine();
                var highReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage >= 70);
                var mediumReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage >= 40 && r.MigrationReadiness.ReadinessPercentage < 70);
                var lowReadiness = results.Count(r => r.Success && r.MigrationReadiness.ReadinessPercentage < 40);

                sb.AppendLine("- **High Readiness (≥70%):** " + highReadiness + " orchestration(s)");
                sb.AppendLine("- **Medium Readiness (40-69%):** " + mediumReadiness + " orchestration(s)");
                sb.AppendLine("- **Low Readiness (<40%):** " + lowReadiness + " orchestration(s)");
                sb.AppendLine();
            }

            // Common Issues
            var allIssues = results.Where(r => r.Success)
                .SelectMany(r => r.MigrationReadiness.Issues)
                .GroupBy(i => i.Description)
                .OrderByDescending(g => g.Count())
                .Take(10);

            if (allIssues.Any())
            {
                sb.AppendLine("## ⚠️ Common Migration Issues");
                sb.AppendLine();
                foreach (var issueGroup in allIssues)
                {
                    sb.AppendLine("- " + issueGroup.Key + " (found in " + issueGroup.Count() + " orchestration(s))");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Appends additional CSS styles specific to batch summary reports.
        /// </summary>
        /// <param name="sb">StringBuilder to append styles to.</param>
        private static void AppendBatchStyles(StringBuilder sb)
        {
            sb.AppendLine("    <style>");
            sb.AppendLine("        .badge-success { background: #28a745; color: white; }");
            sb.AppendLine("        .badge-error { background: #dc3545; color: white; }");
            sb.AppendLine("        .readiness-high { color: #28a745; font-weight: bold; }");
            sb.AppendLine("        .readiness-medium { color: #ffc107; font-weight: bold; }");
            sb.AppendLine("        .readiness-low { color: #dc3545; font-weight: bold; }");
            sb.AppendLine("        .readiness-chart { display: flex; height: 40px; border-radius: 5px; overflow: hidden; }");
            sb.AppendLine("        .readiness-bar { display: flex; align-items: center; justify-content: center; color: white; font-weight: bold; }");
            sb.AppendLine("        .readiness-bar.high { background: #28a745; }");
            sb.AppendLine("        .readiness-bar.medium { background: #ffc107; }");
            sb.AppendLine("        .readiness-bar.low { background: #dc3545; }");
            sb.AppendLine("    </style>");
        }

        /// <summary>
        /// Generates a complete HTML migration report for a single orchestration.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <param name="odxPath">Path to the source ODX file.</param>
        /// <returns>Complete HTML document as a string.</returns>
        private static string GenerateHtmlReport(OrchestrationModel model, string odxPath)
        {
            var sb = new StringBuilder();
            var stats = CalculateStatistics(model);
            var complexityScore = CalculateComplexityScore(model, stats);
            var migrationReadiness = AnalyzeMigrationReadiness(model);

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Migration Report - " + WebUtility.HtmlEncode(model.FullName) + "</title>");
            AppendHtmlStyles(sb);
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            // Header
            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("        <h1>📊 Migration Report: " + WebUtility.HtmlEncode(model.FullName) + "</h1>");
            sb.AppendLine("        <p>Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</p>");
            sb.AppendLine("        <p>Source: " + WebUtility.HtmlEncode(Path.GetFileName(odxPath)) + "</p>");
            sb.AppendLine("    </div>");

            // Executive Summary
            AppendHtmlExecutiveSummary(sb, model, complexityScore, migrationReadiness);

            // Statistics
            AppendHtmlStatistics(sb, stats, model);

            // Migration Issues
            AppendHtmlMigrationIssues(sb, migrationReadiness);

            // Pattern Recommendations
            if (migrationReadiness.DetectedPatterns.Any())
            {
                AppendHtmlPatternRecommendations(sb, migrationReadiness.DetectedPatterns);
            }

            // Port Details
            if (model.Ports.Any())
            {
                AppendHtmlPortDetails(sb, model);
            }

            // Shape Hierarchy Preview
            AppendHtmlShapeHierarchy(sb, model);

            // Recommendations
            AppendHtmlRecommendations(sb, model, complexityScore, migrationReadiness);

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a complete Markdown migration report for a single orchestration.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <param name="odxPath">Path to the source ODX file.</param>
        /// <returns>Complete Markdown document as a string.</returns>
        private static string GenerateMarkdownReport(OrchestrationModel model, string odxPath)
        {
            var sb = new StringBuilder();
            var stats = CalculateStatistics(model);
            var complexityScore = CalculateComplexityScore(model, stats);
            var migrationReadiness = AnalyzeMigrationReadiness(model);

            sb.AppendLine("# Migration Report: " + model.FullName);
            sb.AppendLine();
            sb.AppendLine("**Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ");
            sb.AppendLine("**Source:** " + Path.GetFileName(odxPath));
            sb.AppendLine();

            sb.AppendLine("## 📋 Executive Summary");
            sb.AppendLine();
            sb.AppendLine("- **Orchestration:** " + model.FullName);
            sb.AppendLine("- **Complexity Score:** " + complexityScore.Score + "/100 (" + complexityScore.Level + ")");
            sb.AppendLine("- **Migration Readiness:** " + migrationReadiness.ReadinessPercentage + "%");
            sb.AppendLine();

            AppendMarkdownStatistics(sb, stats, model);
            AppendMarkdownMigrationIssues(sb, migrationReadiness);

            if (migrationReadiness.DetectedPatterns.Any())
            {
                AppendMarkdownPatternRecommendations(sb, migrationReadiness.DetectedPatterns);
            }

            if (model.Ports.Any())
            {
                AppendMarkdownPortDetails(sb, model);
            }

            AppendMarkdownShapeHierarchy(sb, model);
            AppendMarkdownRecommendations(sb, model, complexityScore, migrationReadiness);

            return sb.ToString();
        }

        /// <summary>
        /// Calculates statistical metrics for an orchestration including shape counts and port information.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <returns>Statistics object containing counts of various orchestration elements.</returns>
        private static OrchestrationStatistics CalculateStatistics(OrchestrationModel model)
        {
            var stats = new OrchestrationStatistics();
            CountShapesRecursive(model.Shapes, stats, 0);
            
            // Count ALL shapes using GetAllShapes to get accurate total
            stats.TotalShapes = GetAllShapes(model.Shapes).Count();
            
            stats.Ports = model.Ports.Count;
            stats.Messages = model.Messages.Count;
            
            // Count correlation sets from root shapes
            stats.Correlations = model.Shapes.OfType<CorrelationDeclarationShapeModel>().Count();
            
            return stats;
        }

        /// <summary>
        /// Recursively counts shapes in the orchestration hierarchy, handling nested structures.
        /// </summary>
        /// <param name="shapes">Collection of shapes to process.</param>
        /// <param name="stats">Statistics object to accumulate counts into.</param>
        /// <param name="depth">Current recursion depth for tracking nesting level.</param>
        private static void CountShapesRecursive(IEnumerable<ShapeModel> shapes, OrchestrationStatistics stats, int depth)
        {
            foreach (var shape in shapes)
            {
                if (shape is DecideShapeModel decide)
                {
                    stats.Decisions++;
                    CountShapesRecursive(decide.TrueBranch, stats, depth + 1);
                    CountShapesRecursive(decide.FalseBranch, stats, depth + 1);
                }
                else if (shape is SwitchShapeModel switchShape)
                {
                    stats.Switches++;
                    foreach (var caseShapes in switchShape.Cases.Values)
                    {
                        CountShapesRecursive(caseShapes, stats, depth + 1);
                    }
                    if (switchShape.DefaultCase.Count > 0)
                    {
                        CountShapesRecursive(switchShape.DefaultCase, stats, depth + 1);
                    }
                }
                else if (shape is ConstructShapeModel construct)
                {
                    stats.Constructs++;
                    foreach (var inner in construct.InnerShapes)
                    {
                        if (inner is MessageAssignmentShapeModel) stats.MessageAssignments++;
                    }
                }
                else if (shape is MessageAssignmentShapeModel)
                {
                    stats.MessageAssignments++;
                }
                else if (shape is StartShapeModel)
                {
                    stats.Starts++;
                }
                else if (shape is ReceiveShapeModel)
                {
                    stats.Receives++;
                }
                else if (shape is SendShapeModel)
                {
                    stats.Sends++;
                }
                else if (shape is ScopeShapeModel || shape is AtomicTransactionShapeModel || shape is LongRunningTransactionShapeModel)
                {
                    stats.Scopes++;
                }
                else if (shape is ParallelShapeModel)
                {
                    stats.Parallels++;
                }
                else if (shape is TransformShapeModel)
                {
                    stats.Transforms++;
                }
                else if (shape is CallShapeModel)
                {
                    stats.CallOrchestrations++;
                }

                if (shape.Children.Count > 0)
                {
                    CountShapesRecursive(shape.Children, stats, depth + 1);
                }
            }
        }

        /// <summary>
        /// Calculates a complexity score for the orchestration based on control flow structures and configuration.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <param name="stats">Calculated orchestration statistics.</param>
        /// <returns>Complexity result with score (0-100) and level (LOW/MEDIUM/HIGH).</returns>
        /// <remarks>
        /// Score starts at 100 and deductions are made for:
        /// - Decisions (-3 each): Conditional branches add complexity
        /// - Switches (-4 each): Multi-branch switches are more complex
        /// - Scopes (-2 each): Transaction scopes add moderate complexity
        /// - Parallel shapes (-5 each): Parallel branches complicate migration significantly
        /// - Correlations (-8 each): Manual session mapping required in Logic Apps
        /// - Transforms (-2 each): Each map requires migration/testing
        /// - Call Orchestrations (-3 each): Nested workflow complexity
        /// - Unbound ports (-5 each): Unconfigured ports indicate incomplete migration preparation
        /// </remarks>
        private static ComplexityResult CalculateComplexityScore(OrchestrationModel model, OrchestrationStatistics stats)
        {
            int score = 100;
            score -= stats.Decisions * 3;
            score -= stats.Switches * 4;
            score -= stats.Scopes * 2;
            score -= stats.Parallels * 5;
            score -= stats.Correlations * 8;
            score -= stats.Transforms * 2;
            score -= stats.CallOrchestrations * 3;
            score -= model.Ports.Count(p => string.IsNullOrEmpty(p.AdapterName)) * 5;

            score = Math.Max(0, Math.Min(100, score));

            string level = score >= 70 ? "LOW" : score >= 40 ? "MEDIUM" : "HIGH";
            return new ComplexityResult { Score = score, Level = level };
        }

        /// <summary>
        /// Analyzes migration readiness by detecting potential issues and integration patterns.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <returns>Analysis containing readiness percentage, issues list, and detected patterns.</returns>
        /// <remarks>
        /// Evaluates readiness based on:
        /// - Unbound ports (warning, -5 per port)
        /// - Unsupported shapes (error, -10 per shape)
        /// - Atomic transactions (warning, -15 total)
        /// - Correlation sets (info, -5 total)
        /// - Complex expressions (warning, -5 total)
        /// Also detects 10 common integration patterns with Logic Apps recommendations.
        /// </remarks>
        private static MigrationReadinessAnalysis AnalyzeMigrationReadiness(OrchestrationModel model)
        {
            var analysis = new MigrationReadinessAnalysis();
            int readiness = 100;
            var unboundPorts = model.Ports.Where(p => string.IsNullOrEmpty(p.AdapterName)).ToList();
            if (unboundPorts.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Severity = "Warning",
                    Description = unboundPorts.Count + " port(s) are not bound to physical adapters"
                });
                readiness -= unboundPorts.Count * 5;
            }

            var unsupportedShapes = GetAllShapes(model.Shapes).OfType<FallbackShapeModel>().ToList();
            if (unsupportedShapes.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Severity = "Error",
                    Description = unsupportedShapes.Count + " unsupported shape type(s) found"
                });
                readiness -= unsupportedShapes.Count * 10;
            }

            var atomicTransactions = GetAllShapes(model.Shapes).OfType<AtomicTransactionShapeModel>().ToList();
            if (atomicTransactions.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Severity = "Warning",
                    Description = "Atomic transactions require manual review for Logic Apps migration"
                });
                readiness -= 15;
            }

            var correlations = model.Shapes.OfType<CorrelationDeclarationShapeModel>().ToList();
            if (correlations.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Severity = "Info",
                    Description = correlations.Count + " correlation set(s) need to be mapped to Logic Apps correlation features"
                });
                readiness -= 5;
            }

            var complexExpressions = GetAllShapes(model.Shapes).OfType<ExpressionShapeModel>()
                .Where(e => !string.IsNullOrEmpty(e.Expression) && e.Expression.Length > 200).ToList();
            if (complexExpressions.Any())
            {
                analysis.Issues.Add(new MigrationIssue
                {
                    Severity = "Warning",
                    Description = complexExpressions.Count + " complex expression(s) may need refactoring"
                });
                readiness -= 5;
            }

            analysis.DetectedPatterns = DetectIntegrationPatterns(model);

            analysis.ReadinessPercentage = Math.Max(0, Math.Min(100, readiness));
            return analysis;
        }

        /// <summary>
        /// Recursively flattens the shape hierarchy into a single enumerable collection.
        /// </summary>
        /// <param name="shapes">Root collection of shapes to flatten.</param>
        /// <returns>All shapes in the hierarchy including nested shapes within Decide, Switch, and Construct shapes.</returns>
        private static IEnumerable<ShapeModel> GetAllShapes(IEnumerable<ShapeModel> shapes)
        {
            foreach (var shape in shapes)
            {
                yield return shape;
                if (shape is DecideShapeModel decide)
                {
                    foreach (var s in GetAllShapes(decide.TrueBranch))
                        yield return s;
                    foreach (var s in GetAllShapes(decide.FalseBranch))
                        yield return s;
                }
                else if (shape is SwitchShapeModel switchShape)
                {
                    foreach (var caseShapes in switchShape.Cases.Values)
                        foreach (var s in GetAllShapes(caseShapes))
                            yield return s;
                    foreach (var s in GetAllShapes(switchShape.DefaultCase))
                        yield return s;
                }
                else if (shape is ConstructShapeModel construct)
                {
                    foreach (var s in GetAllShapes(construct.InnerShapes))
                        yield return s;
                }

                // Process regular children
                foreach (var s in GetAllShapes(shape.Children))
                    yield return s;
            }
        }

        /// <summary>
        /// Generates actionable migration recommendations based on complexity and readiness analysis.
        /// </summary>
        /// <param name="model">The parsed orchestration model.</param>
        /// <param name="complexity">Calculated complexity score and level.</param>
        /// <param name="readiness">Migration readiness analysis results.</param>
        /// <returns>List of specific, actionable recommendations for successful migration.</returns>
        private static List<string> GenerateRecommendations(OrchestrationModel model,
            ComplexityResult complexity, MigrationReadinessAnalysis readiness)
        {
            var recommendations = new List<string>();

            if (complexity.Level == "HIGH")
            {
                recommendations.Add("Consider breaking down this orchestration into smaller Logic Apps workflows for better maintainability");
            }

            if (readiness.ReadinessPercentage < 50)
            {
                recommendations.Add("Significant manual intervention will be required for this migration. Consider a phased approach.");
            }

            if (model.Ports.Any(p => string.IsNullOrEmpty(p.AdapterName)))
            {
                recommendations.Add("Configure all port bindings before migration to ensure proper connector mapping");
            }

            var atomicTransactions = GetAllShapes(model.Shapes).OfType<AtomicTransactionShapeModel>().Any();
            if (atomicTransactions)
            {
                recommendations.Add("Review atomic transaction scopes and implement appropriate compensation logic in Logic Apps");
            }

            if (model.Shapes.OfType<CorrelationDeclarationShapeModel>().Any())
            {
                recommendations.Add("Map correlation sets to Logic Apps session management or correlation features");
            }

            var customCode = GetAllShapes(model.Shapes).OfType<ExpressionShapeModel>().Count() +
                            GetAllShapes(model.Shapes).OfType<MessageAssignmentShapeModel>().Count();
            if (customCode > 10)
            {
                recommendations.Add("Review " + customCode + " custom code blocks for Logic Apps expression compatibility");
            }

            recommendations.Add("After migration, explore Logic Apps workflow templates in Azure portal to refactor and optimize common patterns (https://learn.microsoft.com/azure/logic-apps/create-workflows-from-templates)");

            if (!recommendations.Any())
            {
                recommendations.Add("This orchestration appears to be a good candidate for automated migration");
            }

            return recommendations;
        }

        /// <summary>
        /// Appends CSS stylesheet definitions for HTML report formatting.
        /// </summary>
        /// <param name="sb">StringBuilder to append styles to.</param>
        private static void AppendHtmlStyles(StringBuilder sb)
        {
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; }");
            sb.AppendLine("        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; border-radius: 10px; margin-bottom: 20px; }");
            sb.AppendLine("        .container { background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); margin-bottom: 20px; }");
            sb.AppendLine("        h1 { margin: 0; }");
            sb.AppendLine("        h2 { color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px; }");
            sb.AppendLine("        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin: 20px 0; }");
            sb.AppendLine("        .stat-card { background: #f8f9fa; padding: 15px; border-radius: 8px; border-left: 4px solid #667eea; }");
            sb.AppendLine("        .stat-value { font-size: 24px; font-weight: bold; color: #667eea; }");
            sb.AppendLine("        .stat-label { color: #666; font-size: 14px; margin-top: 5px; }");
            sb.AppendLine("        .warning { background: #fff3cd; border-left: 4px solid #ffc107; padding: 10px; margin: 10px 0; border-radius: 4px; }");
            sb.AppendLine("        .error { background: #f8d7da; border-left: 4px solid #dc3545; padding: 10px; margin: 10px 0; border-radius: 4px; }");
            sb.AppendLine("        .success { background: #d4edda; border-left: 4px solid #28a745; padding: 10px; margin: 10px 0; border-radius: 4px; }");
            sb.AppendLine("        .info { background: #d1ecf1; border-left: 4px solid #17a2b8; padding: 10px; margin: 10px 0; border-radius: 4px; }");
            sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
            sb.AppendLine("        th { background: #f8f9fa; padding: 12px; text-align: left; border-bottom: 2px solid #dee2e6; }");
            sb.AppendLine("        td { padding: 10px; border-bottom: 1px solid #dee2e6; }");
            sb.AppendLine("        .tree { font-family: monospace; background: #f8f9fa; padding: 15px; border-radius: 5px; overflow-x: auto; }");
            sb.AppendLine("        .badge { display: inline-block; padding: 3px 8px; border-radius: 12px; font-size: 12px; font-weight: bold; }");
            sb.AppendLine("        .badge-high { background: #dc3545; color: white; }");
            sb.AppendLine("        .badge-medium { background: #ffc107; color: black; }");
            sb.AppendLine("        .badge-low { background: #28a745; color: white; }");
            sb.AppendLine("        .progress { width: 100%; height: 20px; background: #e9ecef; border-radius: 10px; overflow: hidden; }");
            sb.AppendLine("        .progress-bar { height: 100%; background: linear-gradient(90deg, #28a745, #667eea); transition: width 0.3s; }");
            sb.AppendLine("        .pattern-card { background: #f8f9fa; border-left: 4px solid #667eea; padding: 20px; margin: 15px 0; border-radius: 8px; box-shadow: 0 2px 5px rgba(0,0,0,0.05); }");
            sb.AppendLine("        .pattern-card h3 { margin-top: 0; color: #667eea; }");
            sb.AppendLine("        .pattern-card ol { margin: 10px 0; padding-left: 25px; }");
            sb.AppendLine("        .pattern-card a { color: #667eea; text-decoration: none; font-weight: bold; }");
            sb.AppendLine("        .pattern-card a:hover { text-decoration: underline; }");
            sb.AppendLine("    </style>");
        }

        /// <summary>
        /// Generates the executive summary section of the HTML report.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="model">Orchestration model.</param>
        /// <param name="complexityScore">Calculated complexity.</param>
        /// <param name="migrationReadiness">Readiness analysis.</param>
        private static void AppendHtmlExecutiveSummary(StringBuilder sb, OrchestrationModel model,
            ComplexityResult complexityScore, MigrationReadinessAnalysis migrationReadiness)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>📋 Executive Summary</h2>");
            sb.AppendLine("        <p><strong>Orchestration:</strong> " + WebUtility.HtmlEncode(model.FullName) + "</p>");
            sb.AppendLine("        <p><strong>Complexity Score:</strong> <span class=\"badge badge-" + complexityScore.Level.ToLower() + "\">" + complexityScore.Score + "/100 (" + complexityScore.Level + ")</span></p>");
            sb.AppendLine("        <p><strong>Migration Readiness:</strong></p>");
            sb.AppendLine("        <div class=\"progress\">");
            sb.AppendLine("            <div class=\"progress-bar\" style=\"width: " + migrationReadiness.ReadinessPercentage + "%\"></div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <p>" + migrationReadiness.ReadinessPercentage + "% Ready for Migration</p>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates the statistics grid section of the HTML report.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="stats">Calculated statistics.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendHtmlStatistics(StringBuilder sb, OrchestrationStatistics stats, OrchestrationModel model)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>📈 Orchestration Statistics</h2>");
            sb.AppendLine("        <div class=\"stats-grid\">");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.TotalShapes + "</div><div class=\"stat-label\">Total Shapes</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Ports + "</div><div class=\"stat-label\">Ports</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Messages + "</div><div class=\"stat-label\">Messages</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Receives + "</div><div class=\"stat-label\">Receive Shapes</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Sends + "</div><div class=\"stat-label\">Send Shapes</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Decisions + "</div><div class=\"stat-label\">Decisions</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Switches + "</div><div class=\"stat-label\">Switches</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Scopes + "</div><div class=\"stat-label\">Scopes</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Parallels + "</div><div class=\"stat-label\">Parallel Shapes</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Correlations + "</div><div class=\"stat-label\">Correlations</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.Transforms + "</div><div class=\"stat-label\">Transforms</div></div>");
            sb.AppendLine("            <div class=\"stat-card\"><div class=\"stat-value\">" + stats.CallOrchestrations + "</div><div class=\"stat-label\">Call Orchestrations</div></div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates the migration issues section with severity-coded warnings.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="analysis">Migration readiness analysis.</param>
        private static void AppendHtmlMigrationIssues(StringBuilder sb, MigrationReadinessAnalysis analysis)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>⚠️ Migration Considerations</h2>");

            foreach (var issue in analysis.Issues)
            {
                string cssClass = issue.Severity == "Error" ? "error" : issue.Severity == "Warning" ? "warning" : "info";
                sb.AppendLine("        <div class=\"" + cssClass + "\"><strong>" + issue.Severity + ":</strong> " + WebUtility.HtmlEncode(issue.Description) + "</div>");
            }

            if (!analysis.Issues.Any())
            {
                sb.AppendLine("        <div class=\"success\">✅ No major migration issues detected</div>");
            }
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates the port configuration table in the HTML report.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendHtmlPortDetails(StringBuilder sb, OrchestrationModel model)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>🔌 Port Configuration</h2>");
            sb.AppendLine("        <table>");
            sb.AppendLine("            <thead><tr><th>Port Name</th><th>Direction</th><th>Binding</th><th>Adapter</th><th>Status</th></tr></thead>");
            sb.AppendLine("            <tbody>");
            foreach (var port in model.Ports)
            {
                string status = string.IsNullOrEmpty(port.AdapterName) ? "⚠️ Unbound" : "✅ Configured";
                sb.AppendLine("                <tr><td>" + WebUtility.HtmlEncode(port.Name) + "</td><td>" + port.Direction + "</td><td>" + port.BindingKind + "</td><td>" + WebUtility.HtmlEncode(port.AdapterName ?? "N/A") + "</td><td>" + status + "</td></tr>");
            }
            sb.AppendLine("            </tbody>");
            sb.AppendLine("        </table>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates a visual representation of the shape hierarchy.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendHtmlShapeHierarchy(StringBuilder sb, OrchestrationModel model)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>🌳 Shape Hierarchy (Top Level)</h2>");
            sb.AppendLine("        <div class=\"tree\">");
            foreach (var shape in model.Shapes.Take(10))
            {
                AppendHtmlShapeTree(sb, shape, 0, maxDepth: 3);
            }
            if (model.Shapes.Count > 10)
            {
                sb.AppendLine("            <div>... and " + (model.Shapes.Count - 10) + " more shapes</div>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Recursively generates HTML for the shape tree structure.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="shape">Current shape to render.</param>
        /// <param name="indent">Current indentation level.</param>
        /// <param name="maxDepth">Maximum depth to render.</param>
        private static void AppendHtmlShapeTree(StringBuilder sb, ShapeModel shape, int indent, int maxDepth)
        {
            if (indent > maxDepth) return;

            string prefix = new string(' ', indent * 4);
            string icon = GetShapeIcon(shape.ShapeType);
            sb.AppendLine("            <div>" + WebUtility.HtmlEncode(prefix) + icon + " " + WebUtility.HtmlEncode(shape.Name ?? shape.ShapeType) + "</div>");

            if (shape is DecideShapeModel decide)
            {
                if (decide.TrueBranch.Any())
                    sb.AppendLine("            <div>" + WebUtility.HtmlEncode(prefix + "  ") + "✓ True (" + decide.TrueBranch.Count + " shapes)</div>");
                if (decide.FalseBranch.Any())
                    sb.AppendLine("            <div>" + WebUtility.HtmlEncode(prefix + "  ") + "✗ False (" + decide.FalseBranch.Count + " shapes)</div>");
            }
            else if (shape.Children.Any() && indent < maxDepth)
            {
                foreach (var child in shape.Children.Take(5))
                {
                    AppendHtmlShapeTree(sb, child, indent + 1, maxDepth);
                }
                if (shape.Children.Count > 5)
                    sb.AppendLine("            <div>" + WebUtility.HtmlEncode(prefix + "  ") + "... +" + (shape.Children.Count - 5) + " more</div>");
            }
        }

        /// <summary>
        /// Generates the detected integration patterns section with Logic Apps recommendations.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="patterns">Detected integration patterns.</param>
        private static void AppendHtmlPatternRecommendations(StringBuilder sb, List<IntegrationPattern> patterns)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>🎯 Detected Integration Patterns & Logic Apps Recommendations</h2>");
            sb.AppendLine("        <p>The following integration patterns have been detected in this orchestration. Consider using the recommended Logic Apps features to optimize your migrated workflow:</p>");

            foreach (var pattern in patterns)
            {
                sb.AppendLine("        <div class=\"pattern-card\">");
                sb.AppendLine("            <h3>" + WebUtility.HtmlEncode(pattern.PatternName) + "</h3>");
                sb.AppendLine("            <p><strong>Description:</strong> " + WebUtility.HtmlEncode(pattern.Description) + "</p>");
                sb.AppendLine("            <p><strong>BizTalk Implementation:</strong> " + WebUtility.HtmlEncode(pattern.BizTalkImplementation) + "</p>");
                sb.AppendLine("            <p><strong>Logic Apps Recommendation:</strong> " + WebUtility.HtmlEncode(pattern.LogicAppsRecommendation) + "</p>");

                if (pattern.OptimizationSteps.Any())
                {
                    sb.AppendLine("            <p><strong>Optimization Steps:</strong></p>");
                    sb.AppendLine("            <ol>");
                    foreach (var step in pattern.OptimizationSteps)
                    {
                        sb.AppendLine("                <li>" + WebUtility.HtmlEncode(step) + "</li>");
                    }
                    sb.AppendLine("            </ol>");
                }

                if (!string.IsNullOrEmpty(pattern.DocumentationUrl))
                {
                    sb.AppendLine("            <p><strong>Documentation:</strong> <a href=\"" + WebUtility.HtmlEncode(pattern.DocumentationUrl) + "\" target=\"_blank\">Learn more</a></p>");
                }
                sb.AppendLine("        </div>");
            }
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates the general recommendations section.
        /// </summary>
        /// <param name="sb">StringBuilder for HTML output.</param>
        /// <param name="model">Orchestration model.</param>
        /// <param name="complexityScore">Complexity analysis.</param>
        /// <param name="migrationReadiness">Readiness analysis.</param>
        private static void AppendHtmlRecommendations(StringBuilder sb, OrchestrationModel model,
            ComplexityResult complexityScore, MigrationReadinessAnalysis migrationReadiness)
        {
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h2>💡 General Recommendations</h2>");
            sb.AppendLine("        <ul>");
            foreach (var rec in GenerateRecommendations(model, complexityScore, migrationReadiness))
            {
                sb.AppendLine("            <li>" + WebUtility.HtmlEncode(rec) + "</li>");
            }
            sb.AppendLine("        </ul>");
            sb.AppendLine("    </div>");
        }

        /// <summary>
        /// Generates the statistics table in Markdown format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="stats">Calculated statistics.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendMarkdownStatistics(StringBuilder sb, OrchestrationStatistics stats, OrchestrationModel model)
        {
            sb.AppendLine("## 📈 Statistics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Count |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine("| Total Shapes | " + stats.TotalShapes + " |");
            sb.AppendLine("| Ports | " + stats.Ports + " |");
            sb.AppendLine("| Messages | " + stats.Messages + " |");
            sb.AppendLine("| Receive Shapes | " + stats.Receives + " |");
            sb.AppendLine("| Send Shapes | " + stats.Sends + " |");
            sb.AppendLine("| Decisions | " + stats.Decisions + " |");
            sb.AppendLine("| Switches | " + stats.Switches + " |");
            sb.AppendLine("| Scopes | " + stats.Scopes + " |");
            sb.AppendLine("| Parallel Shapes | " + stats.Parallels + " |");
            sb.AppendLine("| Correlations | " + stats.Correlations + " |");
            sb.AppendLine("| Transforms | " + stats.Transforms + " |");
            sb.AppendLine("| Call Orchestrations | " + stats.CallOrchestrations + " |");
            sb.AppendLine();
        }

        /// <summary>
        /// Generates the migration issues section in Markdown format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="analysis">Migration readiness analysis.</param>
        private static void AppendMarkdownMigrationIssues(StringBuilder sb, MigrationReadinessAnalysis analysis)
        {
            sb.AppendLine("## ⚠️ Migration Considerations");
            sb.AppendLine();

            if (analysis.Issues.Any())
            {
                foreach (var issue in analysis.Issues)
                {
                    string icon = issue.Severity == "Error" ? "❌" : issue.Severity == "Warning" ? "⚠️" : "ℹ️";
                    sb.AppendLine("- " + icon + " **" + issue.Severity + ":** " + issue.Description);
                }
            }
            else
            {
                sb.AppendLine("✅ No major migration issues detected");
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Generates the port configuration table in Markdown format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendMarkdownPortDetails(StringBuilder sb, OrchestrationModel model)
        {
            sb.AppendLine("## 🔌 Port Configuration");
            sb.AppendLine();
            sb.AppendLine("| Port Name | Direction | Binding | Adapter | Status |");
            sb.AppendLine("|-----------|-----------|---------|---------|--------|");
            foreach (var port in model.Ports)
            {
                string status = string.IsNullOrEmpty(port.AdapterName) ? "⚠️ Unbound" : "✅ Configured";
                sb.AppendLine("| " + port.Name + " | " + port.Direction + " | " + port.BindingKind + " | " + (port.AdapterName ?? "N/A") + " | " + status + " |");
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Generates the shape hierarchy in Markdown code block format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="model">Orchestration model.</param>
        private static void AppendMarkdownShapeHierarchy(StringBuilder sb, OrchestrationModel model)
        {
            sb.AppendLine("## 🌳 Shape Hierarchy (Top Level)");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var shape in model.Shapes.Take(10))
            {
                AppendMarkdownShapeTree(sb, shape, 0, maxDepth: 3);
            }
            if (model.Shapes.Count > 10)
            {
                sb.AppendLine("... and " + (model.Shapes.Count - 10) + " more shapes");
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        /// <summary>
        /// Recursively generates Markdown for the shape tree structure.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="shape">Current shape to render.</param>
        /// <param name="indent">Current indentation level.</param>
        /// <param name="maxDepth">Maximum depth to render.</param>
        private static void AppendMarkdownShapeTree(StringBuilder sb, ShapeModel shape, int indent, int maxDepth)
        {
            if (indent > maxDepth) return;

            string prefix = new string(' ', indent * 2);
            string icon = GetShapeIcon(shape.ShapeType);
            sb.AppendLine(prefix + icon + " " + (shape.Name ?? shape.ShapeType));

            if (shape is DecideShapeModel decide)
            {
                if (decide.TrueBranch.Any())
                    sb.AppendLine(prefix + "  ✓ True (" + decide.TrueBranch.Count + " shapes)");
                if (decide.FalseBranch.Any())
                    sb.AppendLine(prefix + "  ✗ False (" + decide.FalseBranch.Count + " shapes)");
            }
            else if (shape.Children.Any() && indent < maxDepth)
            {
                foreach (var child in shape.Children.Take(5))
                {
                    AppendMarkdownShapeTree(sb, child, indent + 1, maxDepth);
                }
                if (shape.Children.Count > 5)
                    sb.AppendLine(prefix + "  ... +" + (shape.Children.Count - 5) + " more");
            }
        }

        /// <summary>
        /// Generates the detected integration patterns section in Markdown format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="patterns">Detected integration patterns.</param>
        private static void AppendMarkdownPatternRecommendations(StringBuilder sb, List<IntegrationPattern> patterns)
        {
            sb.AppendLine("## 🎯 Detected Integration Patterns & Logic Apps Recommendations");
            sb.AppendLine();
            sb.AppendLine("The following integration patterns have been detected in this orchestration. Consider using the recommended Logic Apps features to optimize your migrated workflow:");
            sb.AppendLine();

            foreach (var pattern in patterns)
            {
                sb.AppendLine("### " + pattern.PatternName);
                sb.AppendLine();
                sb.AppendLine("**Description:** " + pattern.Description);
                sb.AppendLine();
                sb.AppendLine("**BizTalk Implementation:** " + pattern.BizTalkImplementation);
                sb.AppendLine();
                sb.AppendLine("**Logic Apps Recommendation:** " + pattern.LogicAppsRecommendation);
                sb.AppendLine();

                if (pattern.OptimizationSteps.Any())
                {
                    sb.AppendLine("**Optimization Steps:**");
                    sb.AppendLine();
                    for (int i = 0; i < pattern.OptimizationSteps.Count; i++)
                    {
                        sb.AppendLine((i + 1) + ". " + pattern.OptimizationSteps[i]);
                    }
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(pattern.DocumentationUrl))
                {
                    sb.AppendLine("**Documentation:** [Learn more](" + pattern.DocumentationUrl + ")");
                    sb.AppendLine();
                }
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Generates the general recommendations section in Markdown format.
        /// </summary>
        /// <param name="sb">StringBuilder for Markdown output.</param>
        /// <param name="model">Orchestration model.</param>
        /// <param name="complexityScore">Complexity analysis.</param>
        /// <param name="migrationReadiness">Readiness analysis.</param>
        private static void AppendMarkdownRecommendations(StringBuilder sb, OrchestrationModel model,
            ComplexityResult complexityScore, MigrationReadinessAnalysis migrationReadiness)
        {
            sb.AppendLine("## 💡 General Recommendations");
            sb.AppendLine();
            foreach (var rec in GenerateRecommendations(model, complexityScore, migrationReadiness))
            {
                sb.AppendLine("- " + rec);
            }
        }

        /// <summary>
        /// Returns an emoji icon representing the shape type for visual reports.
        /// </summary>
        /// <param name="shapeType">The shape type identifier.</param>
        /// <returns>Unicode emoji character appropriate for the shape type.</returns>
        private static string GetShapeIcon(string shapeType)
        {
            switch (shapeType)
            {
                case "Receive":
                    return "📥";
                case "Send":
                    return "📤";
                case "Decide":
                    return "🔀";
                case "Switch":
                    return "🔄";
                case "Loop":
                case "ForEach":
                case "While":
                    return "🔁";
                case "Construct":
                    return "🔨";
                case "Transform":
                    return "🔄";
                case "Scope":
                    return "📦";
                case "Terminate":
                case "Throw":
                    return "🛑";
                case "Delay":
                    return "⏰";
                case "Expression":
                    return "📝";
                default:
                    return "▶️";
            }
        }

        /// <summary>
        /// Contains statistical counts of orchestration elements.
        /// </summary>
        private class OrchestrationStatistics
        {
            /// <summary>
            /// Gets or sets the total number of shapes in the orchestration.
            /// </summary>
            public int TotalShapes { get; set; }
            
            /// <summary>
            /// Gets or sets the number of port definitions.
            /// </summary>
            public int Ports { get; set; }
            
            /// <summary>
            /// Gets or sets the number of message declarations.
            /// </summary>
            public int Messages { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Decide (conditional) shapes.
            /// </summary>
            public int Decisions { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Switch shapes.
            /// </summary>
            public int Switches { get; set; }
            
            /// <summary>
            /// Gets or sets the number of MessageAssignment shapes.
            /// </summary>
            public int MessageAssignments { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Start shapes.
            /// </summary>
            public int Starts { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Receive shapes.
            /// </summary>
            public int Receives { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Send shapes.
            /// </summary>
            public int Sends { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Construct shapes.
            /// </summary>
            public int Constructs { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Scope (transaction) shapes.
            /// </summary>
            public int Scopes { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Parallel shapes.
            /// </summary>
            public int Parallels { get; set; }
            
            /// <summary>
            /// Gets or sets the number of correlation sets.
            /// </summary>
            public int Correlations { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Transform shapes.
            /// </summary>
            public int Transforms { get; set; }
            
            /// <summary>
            /// Gets or sets the number of Call Orchestration shapes.
            /// </summary>
            public int CallOrchestrations { get; set; }
        }

        /// <summary>
        /// Contains migration readiness assessment results.
        /// </summary>
        private class MigrationReadinessAnalysis
        {
            /// <summary>
            /// Gets or sets the overall migration readiness percentage (0-100).
            /// </summary>
            public int ReadinessPercentage { get; set; }
            
            /// <summary>
            /// Gets the collection of detected migration issues.
            /// </summary>
            public List<MigrationIssue> Issues { get; } = new List<MigrationIssue>();
            
            /// <summary>
            /// Gets or sets the collection of detected integration patterns with recommendations.
            /// </summary>
            public List<IntegrationPattern> DetectedPatterns { get; set; } = new List<IntegrationPattern>();
        }

        /// <summary>
        /// Represents a single migration concern or blocker.
        /// </summary>
        private class MigrationIssue
        {
            /// <summary>
            /// Gets or sets the severity level: "Error", "Warning", or "Info".
            /// </summary>
            public string Severity { get; set; }
            
            /// <summary>
            /// Gets or sets the human-readable description of the issue.
            /// </summary>
            public string Description { get; set; }
        }

        /// <summary>
        /// Contains the calculated complexity score and categorization.
        /// </summary>
        private class ComplexityResult
        {
            /// <summary>
            /// Gets or sets the numeric complexity score (0-100, where 100 is least complex).
            /// </summary>
            public int Score { get; set; }
            
            /// <summary>
            /// Gets or sets the complexity level: "LOW", "MEDIUM", or "HIGH".
            /// </summary>
            public string Level { get; set; }
        }

        /// <summary>
        /// Contains the processing result for a single orchestration in batch mode.
        /// </summary>
        private class OrchestrationBatchResult
        {
            /// <summary>
            /// Gets or sets the full file path to the orchestration.
            /// </summary>
            public string FilePath { get; set; }
            
            /// <summary>
            /// Gets or sets the parsed orchestration model.
            /// </summary>
            public OrchestrationModel Model { get; set; }
            
            /// <summary>
            /// Gets or sets the calculated statistics for this orchestration.
            /// </summary>
            public OrchestrationStatistics Statistics { get; set; }
            
            /// <summary>
            /// Gets or sets the complexity analysis result.
            /// </summary>
            public ComplexityResult ComplexityScore { get; set; }
            
            /// <summary>
            /// Gets or sets the migration readiness analysis.
            /// </summary>
            public MigrationReadinessAnalysis MigrationReadiness { get; set; }
            
            /// <summary>
            /// Gets or sets a value indicating whether processing succeeded.
            /// </summary>
            public bool Success { get; set; }
            
            /// <summary>
            /// Gets or sets the error message if processing failed.
            /// </summary>
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Represents a detected Enterprise Integration Pattern with migration guidance.
        /// </summary>
        public class IntegrationPattern
        {
            /// <summary>
            /// Gets or sets the pattern name (e.g., "Sequential Convoy", "Scatter-Gather").
            /// </summary>
            public string PatternName { get; set; }
            
            /// <summary>
            /// Gets or sets a brief description of the pattern's purpose.
            /// </summary>
            public string Description { get; set; }
            
            /// <summary>
            /// Gets or sets how this pattern is typically implemented in BizTalk.
            /// </summary>
            public string BizTalkImplementation { get; set; }
            
            /// <summary>
            /// Gets or sets the recommended Logic Apps approach for this pattern.
            /// </summary>
            public string LogicAppsRecommendation { get; set; }
            
            /// <summary>
            /// Gets or sets the Logic Apps template or action type to use.
            /// </summary>
            public string LogicAppsTemplate { get; set; }
            
            /// <summary>
            /// Gets the step-by-step optimization instructions for migration.
            /// </summary>
            public List<string> OptimizationSteps { get; set; } = new List<string>();
            
            /// <summary>
            /// Gets or sets the URL to Microsoft documentation for this pattern in Logic Apps.
            /// </summary>
            public string DocumentationUrl { get; set; }
        }

        /// <summary>
        /// Detects common Enterprise Integration Patterns in BizTalk orchestrations and provides Logic Apps migration recommendations.
        /// </summary>
        /// <param name="model">The parsed orchestration model to analyze.</param>
        /// <returns>List of detected patterns with implementation details and optimization steps.</returns>
        /// <remarks>
        /// Detects 10 common patterns:
        /// 1. Sequential Convoy - Correlated message ordering
        /// 2. Scatter-Gather - Parallel requests with aggregation
        /// 3. Aggregator - Message collection and combination
        /// 4. Content-Based Router - Message routing by content
        /// 5. Request-Reply (Sync) - Synchronous request-response
        /// 6. Message Translator - Schema transformation
        /// 7. Splitter - Single message to multiple messages
        /// 8. Exception Handling - Error handling and compensation
        /// 9. Async Request-Reply - Long-running callbacks
        /// 10. Publish-Subscribe - Message broadcasting
        /// </remarks>
        private static List<IntegrationPattern> DetectIntegrationPatterns(OrchestrationModel model)
        {
            var patterns = new List<IntegrationPattern>();

            // Pattern 1: Convoy Pattern
            if (DetectConvoyPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Sequential Convoy",
                    Description = "Multiple correlated messages processed in order",
                    BizTalkImplementation = "Correlation sets with convoy receives in orchestration",
                    LogicAppsRecommendation = "Use Stateful workflow with session-based messaging (Service Bus, RabbitMQ, or Kafka) for guaranteed message ordering",
                    LogicAppsTemplate = "Service Bus/RabbitMQ/Kafka Session-enabled Queue/Topic trigger",
                    OptimizationSteps = new List<string>
                    {
                        "1. Choose messaging platform: Service Bus (sessions), RabbitMQ (consumer groups), or Kafka (partitions)",
                        "2. Service Bus: Enable sessions on queue/topic and use 'When messages are received (peek-lock)' trigger",
                        "3. RabbitMQ: Use RabbitMQ connector with consumer groups for message ordering",
                        "4. Kafka: Use Kafka connector with partition keys for ordered message processing",
                        "5. Set correlation ID (SessionId for Service Bus, CorrelationId for RabbitMQ/Kafka)",
                        "6. Remove convoy receive shapes - Logic Apps handles ordering natively with sessions/partitions"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/send-related-messages-sequential-convoy"
                });
            }

            // Pattern 2: Scatter-Gather
            if (DetectScatterGatherPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Scatter-Gather",
                    Description = "Send requests to multiple endpoints and aggregate responses",
                    BizTalkImplementation = "Parallel shapes with multiple sends, followed by receives and aggregation",
                    LogicAppsRecommendation = "Use Parallel Branch action with built-in join synchronization",
                    LogicAppsTemplate = "Parallel branches pattern",
                    OptimizationSteps = new List<string>
                    {
                        "1. Replace BizTalk Parallel shape with Logic Apps 'Parallel Branch' action",
                        "2. Each branch sends to one endpoint and waits for response",
                        "3. Automatic join after all branches complete (no manual correlation needed)",
                        "4. Use 'Compose' action after parallel branches to aggregate results",
                        "5. Configure timeout and error handling per branch as needed"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-control-flow-branches"
                });
            }

            // Pattern 3: Aggregator
            if (DetectAggregatorPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Aggregator",
                    Description = "Collect and combine multiple messages into a single message",
                    BizTalkImplementation = "Loop with receives, append to array/list variable, send aggregated result",
                    LogicAppsRecommendation = "Use Batch trigger (Service Bus/RabbitMQ/Kafka) or stateful workflow with Until loop",
                    LogicAppsTemplate = "Batch message trigger or Until loop with array aggregation",
                    OptimizationSteps = new List<string>
                    {
                        "1. Option A: Service Bus Batch trigger - automatic aggregation with release criteria",
                        "2. Option B: RabbitMQ connector - collect messages with consumer acknowledgment",
                        "3. Option C: Kafka connector - aggregate messages from topic partitions",
                        "4. Option D: Stateful workflow with 'Until' loop and 'Append to array variable'",
                        "5. Set batch release criteria (count, size, or timeout)",
                        "6. Use 'Compose' action to format aggregated output",
                        "7. Leverage platform-native batching to simplify correlation logic"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-batch-process-send-receive-messages"
                });
            }

            // Pattern 4: Content-Based Router
            if (DetectContentBasedRouterPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Content-Based Router",
                    Description = "Route messages to different destinations based on content",
                    BizTalkImplementation = "Decide/Switch shapes with multiple send ports based on message properties",
                    LogicAppsRecommendation = "Use Switch action or simplified If conditions with dynamic connectors",
                    LogicAppsTemplate = "Switch control action",
                    OptimizationSteps = new List<string>
                    {
                        "1. Replace nested BizTalk Decide shapes with single Switch action",
                        "2. Use expressions like @{triggerBody()?['MessageType']} for routing",
                        "3. Dynamic connector selection: Use variables to select target endpoint",
                        "4. Simplify conditions - Logic Apps expressions are more concise than BizTalk",
                        "5. Consider Azure API Management for complex routing rules"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-control-flow-switch-statement"
                });
            }

            // Pattern 5: Request-Reply (Synchronous)
            if (DetectRequestReplyPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Request-Reply (Synchronous)",
                    Description = "Synchronous request-response pattern",
                    BizTalkImplementation = "Request-response port with activating receive and send",
                    LogicAppsRecommendation = "Use Request trigger with Response action (built-in pattern)",
                    LogicAppsTemplate = "HTTP Request-Response",
                    OptimizationSteps = new List<string>
                    {
                        "1. Use 'When an HTTP request is received' trigger",
                        "2. Process business logic in between",
                        "3. Use 'Response' action to return result (no manual correlation needed)",
                        "4. Set timeout appropriate for your scenario",
                        "5. Logic Apps automatically maintains request-response correlation"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-http-endpoint"
                });
            }

            // Pattern 6: Message Translator
            if (DetectMessageTranslatorPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Message Translator",
                    Description = "Transform message format from one schema to another",
                    BizTalkImplementation = "Transform shape with BizTalk maps (.btm files)",
                    LogicAppsRecommendation = "Use Data Mapper for visual transformations, Parse XML/Compose XML for schema validation, or Liquid templates for complex mapping",
                    LogicAppsTemplate = "Transform XML/JSON action, Data Mapper, or Parse/Compose XML",
                    OptimizationSteps = new List<string>
                    {
                        "1. Simple transforms: Use 'Compose' action with expressions",
                        "2. BizTalk map migration: Use Data Mapper (GA) with visual mapping - converts BTM files",
                        "3. XML schema validation: Use 'Parse XML' action with schema from integration account",
                        "4. Complex XSLT: Use 'Transform XML' action (upload .xslt to integration account)",
                        "5. XML construction with schema: Use 'Compose XML' action with schema validation",
                        "6. JSON transformations: Use Liquid templates for complex mapping scenarios",
                        "7. Flat file: Use Flat File connector for encoding/decoding operations"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-enterprise-integration-transform"
                });
            }

            // Pattern 7: Splitter
            if (DetectSplitterPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Splitter",
                    Description = "Split a single message into multiple messages",
                    BizTalkImplementation = "Loop shape iterating over message parts with multiple sends",
                    LogicAppsRecommendation = "Use For-Each loop with dynamic array from message",
                    LogicAppsTemplate = "For each control action",
                    OptimizationSteps = new List<string>
                    {
                        "1. Use 'For each' action to iterate over array items",
                        "2. Enable concurrency for parallel processing (up to 50 concurrent iterations)",
                        "3. Each iteration sends one message (simpler than BizTalk loops)",
                        "4. Use 'Parse JSON' or 'Parse XML' first to extract array",
                        "5. No need for loop counters or complex iteration logic"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-control-flow-loops"
                });
            }

            // Pattern 8: Exception Handling / Compensation
            if (DetectCompensationPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Exception Handling & Compensation",
                    Description = "Handle errors and perform compensating actions",
                    BizTalkImplementation = "Atomic/Long-running scopes with exception handlers and compensation blocks",
                    LogicAppsRecommendation = "Use Scope action with Configure Run After for error handling",
                    LogicAppsTemplate = "Scope with error handling",
                    OptimizationSteps = new List<string>
                    {
                        "1. Wrap logic in 'Scope' action for transaction-like behavior",
                        "2. Add parallel scope with 'Configure run after' → Run after failure",
                        "3. Implement compensation logic in the error-handling scope",
                        "4. Use Try-Catch-Finally pattern with scopes",
                        "5. Note: Logic Apps doesn't have distributed transactions - design for eventual consistency"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/logic-apps-exception-handling"
                });
            }

            // Pattern 9: Asynchronous Request-Reply
            if (DetectAsyncRequestReplyPattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Asynchronous Request-Reply",
                    Description = "Long-running request with delayed response via callback",
                    BizTalkImplementation = "Solicit-response port with correlation to match request/reply",
                    LogicAppsRecommendation = "Use stateful workflow with callbacks and state persistence",
                    LogicAppsTemplate = "Callback pattern with Service Bus or HTTP webhook",
                    OptimizationSteps = new List<string>
                    {
                        "1. Use stateful workflow for long-running operations with built-in state persistence",
                        "2. Workflow receives request, sends to backend, and waits for callback",
                        "3. Use Service Bus or Event Grid for callback notifications",
                        "4. Correlation via workflow instance ID or custom tracking property",
                        "5. Set appropriate timeout for long-running operations",
                        "6. Leverage workflow run history for monitoring and debugging"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/logic-apps/single-tenant-overview-compare"
                });
            }

            // Pattern 10: Publish-Subscribe
            if (DetectPublishSubscribePattern(model))
            {
                patterns.Add(new IntegrationPattern
                {
                    PatternName = "Publish-Subscribe",
                    Description = "Broadcast messages to multiple subscribers",
                    BizTalkImplementation = "Send to Service Bus topic or multiple send ports",
                    LogicAppsRecommendation = "Use Service Bus Topics with subscription filters (native support)",
                    LogicAppsTemplate = "Service Bus Topic connector",
                    OptimizationSteps = new List<string>
                    {
                        "1. Send to Service Bus Topic using 'Send message' action",
                        "2. Each subscriber is a separate Logic App with topic subscription trigger",
                        "3. Use SQL filters on subscriptions for content-based routing",
                        "4. No need for BizTalk orchestration - subscribers are decoupled",
                        "5. Subscribers scale independently"
                    },
                    DocumentationUrl = "https://learn.microsoft.com/azure/service-bus-messaging/service-bus-queues-topics-subscriptions"
                });
            }

            return patterns;
        }

        /// <summary>
        /// Detects the Sequential Convoy integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria:
        /// - Multiple non-activating receives with correlation sets, OR
        /// - Loop containing receive shapes (sequential message processing)
        /// BizTalk implementation: Correlation sets with convoy receives
        /// Logic Apps alternative: Service Bus sessions for guaranteed ordering
        /// </remarks>
        private static bool DetectConvoyPattern(OrchestrationModel model)
        {
            var correlations = model.Shapes.OfType<CorrelationDeclarationShapeModel>().ToList();
            var receives = GetAllShapes(model.Shapes).OfType<ReceiveShapeModel>().ToList();
            
            var correlatedReceives = receives.Count(r => !r.Activate) >= 2;
            var loopWithReceive = GetAllShapes(model.Shapes).Any(s => 
                (s is LoopShapeModel || s is WhileShapeModel) && 
                s.Children.Any(c => c is ReceiveShapeModel));
            
            return (correlations.Any() && correlatedReceives) || loopWithReceive;
        }

        /// <summary>
        /// Detects the Scatter-Gather integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Parallel shape with send operations in multiple branches (2+)
        /// BizTalk implementation: Parallel shapes with sends followed by receives
        /// Logic Apps alternative: Parallel Branch action with automatic join
        /// </remarks>
        private static bool DetectScatterGatherPattern(OrchestrationModel model)
        {
            var parallelShapes = GetAllShapes(model.Shapes).OfType<ParallelShapeModel>().ToList();
            foreach (var parallel in parallelShapes)
            {
                var branchesWithSends = parallel.Children.Count(branch => 
                    GetAllShapes(new[] { branch }).OfType<SendShapeModel>().Any());
                
                if (branchesWithSends >= 2)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Detects the Aggregator integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Loop containing both receive shapes and variable assignments
        /// BizTalk implementation: Loop with receives, append to array/list variable
        /// Logic Apps alternative: Batch trigger or Until loop with Append to array
        /// </remarks>
        private static bool DetectAggregatorPattern(OrchestrationModel model)
        {
            var loops = GetAllShapes(model.Shapes)
                .Where(s => s is LoopShapeModel || s is WhileShapeModel)
                .ToList();
            
            foreach (var loop in loops)
            {
                var hasReceiveInLoop = GetAllShapes(loop.Children).OfType<ReceiveShapeModel>().Any();
                var hasVariableAssignment = GetAllShapes(loop.Children).OfType<VariableAssignmentShapeModel>().Any();
                
                if (hasReceiveInLoop && hasVariableAssignment)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Detects the Content-Based Router integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Decide or Switch shapes with send operations in branches
        /// BizTalk implementation: Decide/Switch shapes with multiple send ports
        /// Logic Apps alternative: Switch action or If conditions with dynamic connectors
        /// </remarks>
        private static bool DetectContentBasedRouterPattern(OrchestrationModel model)
        {
            var decisions = GetAllShapes(model.Shapes).OfType<DecideShapeModel>().ToList();
            var switches = GetAllShapes(model.Shapes).OfType<SwitchShapeModel>().ToList();
            foreach (var decide in decisions)
            {
                var sendsInTrue = GetAllShapes(decide.TrueBranch).OfType<SendShapeModel>().Any();
                var sendsInFalse = GetAllShapes(decide.FalseBranch).OfType<SendShapeModel>().Any();
                if (sendsInTrue || sendsInFalse)
                    return true;
            }
            
            foreach (var switchShape in switches)
            {
                var casesWithSends = switchShape.Cases.Values
                    .Count(caseShapes => GetAllShapes(caseShapes).OfType<SendShapeModel>().Any());
                if (casesWithSends >= 2)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Detects the Request-Reply (Synchronous) integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Activating receive followed by send on the same port (request-response port)
        /// BizTalk implementation: Request-response port with activating receive and send
        /// Logic Apps alternative: HTTP Request trigger with Response action
        /// </remarks>
        private static bool DetectRequestReplyPattern(OrchestrationModel model)
        {
            var receives = GetAllShapes(model.Shapes).OfType<ReceiveShapeModel>().ToList();
            var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
            
            var activatingReceive = receives.FirstOrDefault(r => r.Activate);
            if (activatingReceive != null && sends.Any())
            {
                var send = sends.FirstOrDefault(s => s.PortName == activatingReceive.PortName);
                return send != null;
            }
            return false;
        }

        /// <summary>
        /// Detects the Message Translator integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Presence of Transform shapes
        /// BizTalk implementation: Transform shape with BizTalk maps (.btm files)
        /// Logic Apps alternative: Liquid templates, Transform XML action, or Data Mapper
        /// </remarks>
        private static bool DetectMessageTranslatorPattern(OrchestrationModel model)
        {
            return GetAllShapes(model.Shapes).OfType<TransformShapeModel>().Any();
        }

        /// <summary>
        /// Detects the Splitter integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Loop containing send operations in iterations
        /// BizTalk implementation: Loop shape iterating over message parts with multiple sends
        /// Logic Apps alternative: For-Each loop with dynamic array from message
        /// </remarks>
        private static bool DetectSplitterPattern(OrchestrationModel model)
        {
            var loops = GetAllShapes(model.Shapes)
                .Where(s => s is LoopShapeModel || s is WhileShapeModel)
                .ToList();
            
            foreach (var loop in loops)
            {
                if (GetAllShapes(loop.Children).OfType<SendShapeModel>().Any())
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Detects the Exception Handling and Compensation integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria: Scopes with catch blocks or compensation blocks
        /// BizTalk implementation: Atomic/Long-running scopes with exception handlers
        /// Logic Apps alternative: Scope action with Configure Run After for error handling
        /// </remarks>
        private static bool DetectCompensationPattern(OrchestrationModel model)
        {
            var scopes = GetAllShapes(model.Shapes)
                .Where(s => s is ScopeShapeModel || 
                           s is AtomicTransactionShapeModel || 
                           s is LongRunningTransactionShapeModel)
                .ToList();
            
            foreach (var scope in scopes)
            {
                var hasCatchBlocks = scope.Children.Any(c => c is CatchShapeModel);
                var hasCompensation = scope.Children.Any(c => c.ShapeType == "Compensation");
                
                if (hasCatchBlocks || hasCompensation)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Detects the Asynchronous Request-Reply integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria:
        /// - Correlation sets present
        /// - Activating receive (initial request)
        /// - Non-activating correlated receive (callback response)
        /// - Send operation between the receives (sending request to backend)
        /// This more accurately detects async request-reply vs generic correlation usage.
        /// BizTalk implementation: Solicit-response port with correlation to match request/reply
        /// Logic Apps alternative: Stateful workflows with built-in state persistence and callbacks
        /// </remarks>
        private static bool DetectAsyncRequestReplyPattern(OrchestrationModel model)
        {
            var correlations = model.Shapes.OfType<CorrelationDeclarationShapeModel>().ToList();
            if (!correlations.Any())
                return false;
            
            var receives = GetAllShapes(model.Shapes).OfType<ReceiveShapeModel>().ToList();
            var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
            
            // Check for classic async request-reply pattern:
            // 1. Activating receive (initial request)
            // 2. Send operation (forward to backend)
            // 3. Non-activating correlated receive (wait for callback)
            var hasActivatingReceive = receives.Any(r => r.Activate);
            var hasNonActivatingReceive = receives.Any(r => !r.Activate);
            var hasSendOperation = sends.Any();
            
            // Must have all three components for async request-reply
            return hasActivatingReceive && hasNonActivatingReceive && hasSendOperation;
        }

        /// <summary>
        /// Detects the Publish-Subscribe integration pattern.
        /// </summary>
        /// <param name="model">The orchestration model to analyze.</param>
        /// <returns>True if the pattern is detected; otherwise false.</returns>
        /// <remarks>
        /// Detection criteria:
        /// - Sending to Service Bus Topics (native pub/sub)
        /// - Sending to RabbitMQ exchanges (fanout/topic type)
        /// - Sending to Kafka topics
        /// - Sending to Event Grid topics
        /// - Sending to IBM MQ in pub/sub mode (topic-based addressing)
        /// EXCLUDES:
        /// - MSMQ (no pub/sub support - point-to-point only)
        /// - IBM MQ traditional queues (point-to-point only)
        /// - Azure Event Hub (streaming pattern, handled separately)
        /// BizTalk implementation: Send to Service Bus topic or multiple send ports
        /// Logic Apps alternative: Service Bus Topics with subscription filters
        /// </remarks>
        private static bool DetectPublishSubscribePattern(OrchestrationModel model)
        {
            // Check for Service Bus Topic ports (primary indicator)
            var serviceBusTopics = model.Ports.Where(p =>
                !string.IsNullOrEmpty(p.AdapterName) &&
                (p.AdapterName.Contains("ServiceBus") || p.AdapterName.Contains("SB-Messaging")) &&
                (p.Address != null && (
                    p.Address.IndexOf("/topic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.BindingKind != null && p.BindingKind.IndexOf("Topic", StringComparison.OrdinalIgnoreCase) >= 0)
                ))
            ).ToList();

            if (serviceBusTopics.Any())
            {
                var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
                if (sends.Any(s => serviceBusTopics.Any(t => t.Name == s.PortName)))
                    return true;
            }

            // Check for RabbitMQ exchange ports (fanout/topic exchanges)
            var rabbitMQExchanges = model.Ports.Where(p =>
                !string.IsNullOrEmpty(p.AdapterName) &&
                p.AdapterName.IndexOf("RabbitMQ", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (p.Address != null && p.Address.IndexOf("exchange", StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();

            if (rabbitMQExchanges.Any())
            {
                var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
                if (sends.Any(s => rabbitMQExchanges.Any(e => e.Name == s.PortName)))
                    return true;
            }

            // Check for Kafka topic ports
            var kafkaTopics = model.Ports.Where(p =>
                !string.IsNullOrEmpty(p.AdapterName) &&
                p.AdapterName.IndexOf("Kafka", StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();

            if (kafkaTopics.Any())
            {
                var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
                if (sends.Any(s => kafkaTopics.Any(k => k.Name == s.PortName)))
                    return true;
            }

            // Check for Event Grid topic ports
            var eventGridTopics = model.Ports.Where(p =>
                !string.IsNullOrEmpty(p.AdapterName) &&
                (p.AdapterName.IndexOf("EventGrid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (p.Address != null && p.Address.IndexOf("eventgrid", StringComparison.OrdinalIgnoreCase) >= 0))
            ).ToList();

            if (eventGridTopics.Any())
            {
                var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
                if (sends.Any(s => eventGridTopics.Any(e => e.Name == s.PortName)))
                    return true;
            }

            // Check for IBM MQ in pub/sub mode (topic addressing only)
            var ibmMQTopics = model.Ports.Where(p =>
                !string.IsNullOrEmpty(p.AdapterName) &&
                (p.AdapterName.IndexOf("MQSeries", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 p.AdapterName.IndexOf("IBMMQ", StringComparison.OrdinalIgnoreCase) >= 0) &&
                (p.Address != null && p.Address.IndexOf("/topic/", StringComparison.OrdinalIgnoreCase) >= 0)
            ).ToList();

            if (ibmMQTopics.Any())
            {
                var sends = GetAllShapes(model.Shapes).OfType<SendShapeModel>().ToList();
                if (sends.Any(s => ibmMQTopics.Any(i => i.Name == s.PortName)))
                    return true;
            }

            // DO NOT detect MSMQ or traditional IBM MQ queues as pub/sub
            // These are point-to-point messaging only
            
            return false;
        }
    }
}