// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Represents an XSD schema element with its properties and child hierarchy.
    /// </summary>
    internal class XsdElement
    {
        /// <summary>
        /// Gets or sets the element name.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether this element can repeat (maxOccurs &gt; 1 or unbounded).
        /// </summary>
        public bool IsRepeating { get; set; }
        
        /// <summary>
        /// Gets or sets the collection of child elements.
        /// </summary>
        public List<XsdElement> Children { get; set; } = new List<XsdElement>();
    }
    
    /// <summary>
    /// Translates BizTalk functoid elements to Azure Data Mapper compatible expressions.
    /// </summary>
    /// <remarks>
    /// This class converts BizTalk's proprietary functoid system to XPath/LML expressions,
    /// handling string operations, math functions, logical operations, loops, and custom scripts.
    /// </remarks>
    public class FunctoidTranslator
    {
        private BtmMapData _mapData;
        private Dictionary<string, string> _functoidOutputs;
        private string _sourceSchemaFilePath;
        private string _targetSchemaFilePath;

        /// <summary>
        /// Translates all functoids in the map to LML-compatible expressions.
        /// </summary>
        /// <param name="mapData">The parsed BTM map data containing functoids and links.</param>
        /// <param name="sourceSchemaFilePath">Optional path to the source XSD schema file.</param>
        /// <param name="targetSchemaFilePath">Optional path to the target XSD schema file.</param>
        /// <returns>A <see cref="TranslatedMapData"/> object ready for LML generation.</returns>
        public TranslatedMapData TranslateFunctoids(BtmMapData mapData, string sourceSchemaFilePath = null, string targetSchemaFilePath = null)
        {
            _mapData = mapData;
            _functoidOutputs = new Dictionary<string, string>();
            _sourceSchemaFilePath = sourceSchemaFilePath;
            _targetSchemaFilePath = targetSchemaFilePath;

            var translatedMap = new TranslatedMapData
            {
                SourceSchema = mapData.SourceSchema,
                TargetSchema = mapData.TargetSchema,
                SourceNamespaces = mapData.SourceNamespaces,
                TargetNamespaces = new Dictionary<string, string>(mapData.TargetNamespaces),
                SourceSchemaFilePath = sourceSchemaFilePath,
                TargetSchemaFilePath = targetSchemaFilePath
            };
            
            // CRITICAL FIX: Track original target namespaces BEFORE copying from source
            // This is needed to detect if PropertySchema was originally in target schema
            var originalTargetNamespaces = new Dictionary<string, string>(mapData.TargetNamespaces);
            
            // CRITICAL FIX: Copy all source namespaces to target namespaces
            // This is needed for schemas like PropertySchema that are referenced in both source and target
            foreach (var sourceNs in mapData.SourceNamespaces)
            {
                if (!translatedMap.TargetNamespaces.ContainsKey(sourceNs.Key))
                {
                    translatedMap.TargetNamespaces[sourceNs.Key] = sourceNs.Value;
                    Console.WriteLine($"Copied source namespace to target: {sourceNs.Key} = {sourceNs.Value}");
                }
            }

            // Build mapping structure from links
            BuildMappings(translatedMap);

            // CRITICAL FIX: Enforce target schema's primary namespace as ns0
            // Pass original target namespaces to detect shared namespaces correctly
            EnforceTargetNamespaceAsNs0(translatedMap, originalTargetNamespaces);

            return translatedMap;
        }

        private void BuildMappings(TranslatedMapData translatedMap)
        {
            // Strategy 1: Try to build mappings using schema tree (original approach)
            var targetLinks = _mapData.Links
                .Where(l => IsTargetSchemaNode(l.LinkTo))
                .ToList();

            if (targetLinks.Any())
            {
                // Build flat mappings first
                var flatMappings = new List<LmlMapping>();
                
                foreach (var targetLink in targetLinks)
                {
                    var targetNode = GetSchemaNode(targetLink.LinkTo, _mapData.TargetTree);
                    if (targetNode == null) continue;

                    var mapping = new LmlMapping
                    {
                        TargetPath = BuildTargetPath(targetNode),
                        SourceExpression = ResolveSourceExpression(targetLink.LinkFrom)
                    };

                    flatMappings.Add(mapping);
                }

                // Build hierarchical structure
                var hierarchy = BuildHierarchicalMappings(flatMappings);
                translatedMap.Mappings.AddRange(hierarchy);

                // Process repeating structures
                ProcessRepeatingStructures(translatedMap);
            }
            else
            {
                // Strategy 2: XPath-based mapping (for BTM files without NodeLookup)
                BuildMappingsFromXPath(translatedMap);
            }
        }

        private List<LmlMapping> BuildHierarchicalMappings(List<LmlMapping> flatMappings)
        {
            // Build a tree structure from flat paths
            var root = new Dictionary<string, object>();
            var mappingsByPath = new Dictionary<string, LmlMapping>();

            foreach (var mapping in flatMappings)
            {
                mappingsByPath[mapping.TargetPath] = mapping;
            }

            // Group by common parent paths
            var pathGroups = new Dictionary<string, List<LmlMapping>>();
            
            foreach (var mapping in flatMappings)
            {
                var parts = mapping.TargetPath.Split('/');
                
                // Add to appropriate groups
                for (int i = 1; i < parts.Length; i++)
                {
                    var parentPath = string.Join("/", parts.Take(i));
                    
                    if (!pathGroups.ContainsKey(parentPath))
                    {
                        pathGroups[parentPath] = new List<LmlMapping>();
                    }
                    
                    if (i == parts.Length - 1)
                    {
                        // This is the actual mapping
                        pathGroups[parentPath].Add(mapping);
                    }
                }
            }

            // Build hierarchy starting from root
            var result = new List<LmlMapping>();
            var rootPaths = flatMappings
                .Select(m => m.TargetPath.Split('/')[0])
                .Distinct()
                .ToList();

            foreach (var rootPath in rootPaths)
            {
                var rootMapping = new LmlMapping
                {
                    TargetPath = rootPath,
                    SourceExpression = "",
                    Children = BuildChildMappings(rootPath, pathGroups, flatMappings)
                };

                result.Add(rootMapping);
            }

            return result;
        }

        private List<LmlMapping> BuildChildMappings(string parentPath, 
            Dictionary<string, List<LmlMapping>> pathGroups, 
            List<LmlMapping> allMappings)
        {
            var children = new List<LmlMapping>();
            
            // Find all mappings that are direct children of parentPath
            var directChildren = allMappings
                .Where(m =>
                {
                    var parts = m.TargetPath.Split('/');
                    var mappingParent = parts.Length > 1 
                        ? string.Join("/", parts.Take(parts.Length - 1))
                        : "";
                    return mappingParent == parentPath;
                })
                .ToList();

            foreach (var child in directChildren)
            {
                // Check if this child has its own children
                var grandchildren = allMappings
                    .Where(m => m != child && m.TargetPath.StartsWith(child.TargetPath + "/"))
                    .ToList();

                if (grandchildren.Any())
                {
                    // This is a parent element
                    var parentMapping = new LmlMapping
                    {
                        TargetPath = child.TargetPath,
                        SourceExpression = "",
                        Children = BuildChildMappings(child.TargetPath, pathGroups, allMappings)
                    };
                    children.Add(parentMapping);
                }
                else
                {
                    // This is a leaf mapping
                    children.Add(child);
                }
            }

            return children;
        }

        private void BuildMappingsFromXPath(TranslatedMapData translatedMap)
        {
            // For BTM files that use XPath strings directly in links
            var parser = new BtmParser();
            var flatMappings = new List<LmlMapping>();
            
            Console.WriteLine("DEBUG: Building mappings from XPath");
            
            // First pass: identify looping functoids and their target connections
            var loopingFunctoids = _mapData.Functoids.Where(f => f.FunctoidType == "Looping" || f.FunctoidType == "MassCopy").ToList();
            var loopTargets = new Dictionary<string, BtmFunctoid>();
            var massCopyInfo = new List<(string targetPath, string sourcePath)>();
            
            foreach (var loopFunctoid in loopingFunctoids)
            {
                // Find what this looping functoid is connected to on the output side
                var outputLinks = _mapData.Links.Where(l => l.LinkFrom == loopFunctoid.FunctoidId).ToList();
                foreach (var outLink in outputLinks)
                {
                    loopTargets[outLink.LinkTo] = loopFunctoid;
                    
                    // Extract source path for mass copy
                    var sourceParam = loopFunctoid.InputParameters.FirstOrDefault();
                    if (sourceParam != null)
                    {
                        string loopSourcePath = ResolveParameter(sourceParam);
                        if (loopSourcePath.StartsWith("\"") && loopSourcePath.EndsWith("\""))
                        {
                            loopSourcePath = loopSourcePath.Substring(1, loopSourcePath.Length - 2);
                        }
                        loopSourcePath = CleanupSourceXPath(loopSourcePath);
                        var targetPath = CleanupTargetXPath(outLink.LinkTo);
                        
                        massCopyInfo.Add((targetPath, loopSourcePath));
                        Console.WriteLine($"  DEBUG: Mass copy detected: {targetPath} from {loopSourcePath}");
                    }
                }
            }
            
            foreach (var link in _mapData.Links)
            {
                // Skip links between functoids
                var sourceFunctoid = _mapData.Functoids.FirstOrDefault(f => f.FunctoidId == link.LinkFrom);
                var targetFunctoid = _mapData.Functoids.FirstOrDefault(f => f.FunctoidId == link.LinkTo);
                
                if (targetFunctoid != null)
                {
                    // Link to functoid, not final target
                    Console.WriteLine($"  Skipping link to functoid: {link.LinkId}");
                    continue;
                }

                // Parse and clean up XPath expressions
                var sourceXPath = CleanupSourceXPath(link.LinkFrom);
                var targetXPath = CleanupTargetXPath(link.LinkTo);

                // Skip mass copy links - they'll be handled specially
                if (sourceFunctoid != null && (sourceFunctoid.FunctoidType == "Looping" || sourceFunctoid.FunctoidType == "MassCopy"))
                {
                    Console.WriteLine($"  Skipping mass copy link (will be expanded later): {link.LinkId}");
                    continue;
                }

                // Resolve source expression (could be functoid)
                string sourceExpression;
                if (sourceFunctoid != null)
                {
                    Console.WriteLine($"  DEBUG: Processing functoid {sourceFunctoid.FunctoidId}, Type={sourceFunctoid.FunctoidType}");
                    sourceExpression = TranslateFunctoid(sourceFunctoid);
                    Console.WriteLine($"  Link {link.LinkId}: {targetXPath} = {sourceExpression} (from functoid)");
                    
                    // Special handling for Looping functoids - they generate $for structures
                    if (sourceFunctoid.FunctoidType == "Looping" && sourceExpression.StartsWith("$for("))
                    {
                        // Extract the loop expression from $for(...)
                        var loopExpr = sourceExpression.Substring(5, sourceExpression.Length - 6); // Remove "$for(" and ")"
                        
                        // Create a loop mapping structure
                        var loopMapping = new LmlMapping
                        {
                            TargetPath = "$for(" + loopExpr + ")",
                            IsLoop = true,
                            LoopExpression = loopExpr,
                            SourceExpression = "",
                            Children = new List<LmlMapping>(),
                            Attributes = new Dictionary<string, string>()
                        };
                        
                        flatMappings.Add(loopMapping);
                        Console.WriteLine($"  Created $for loop: {loopMapping.TargetPath}");
                        continue; // Skip regular processing for loop functoids
                    }
                }
                else
                {
                    sourceExpression = sourceXPath;
                    Console.WriteLine($"  Link {link.LinkId}: {targetXPath} = {sourceExpression}");
                }

                var mapping = new LmlMapping
                {
                    TargetPath = targetXPath,
                    SourceExpression = sourceExpression
                };

                flatMappings.Add(mapping);
            }

            Console.WriteLine($"DEBUG: Created {flatMappings.Count} flat mappings");
            foreach (var m in flatMappings)
            {
                Console.WriteLine($"  {m.TargetPath} = {m.SourceExpression}");
            }
            
            // Expand mass copy operations to explicit child field mappings
            // Filter to keep only the outermost loop (avoid nested/sibling loop duplicates)
            var uniqueMassCopyInfo = new List<(string targetPath, string sourcePath)>();
            foreach (var (targetPath, sourcePath) in massCopyInfo)
            {
                // Check if this is a deeper nested loop than another mass copy with the same target
                // We want to keep the shortest (outermost) source path for each target
                var hasShorterPath = massCopyInfo.Any(other => 
                    other.targetPath == targetPath && 
                    other.sourcePath != sourcePath && 
                    other.sourcePath.Length < sourcePath.Length);
                
                if (!hasShorterPath)
                {
                    uniqueMassCopyInfo.Add((targetPath, sourcePath));
                    Console.WriteLine($"  Keeping mass copy: {targetPath} from {sourcePath}");
                }
                else
                {
                    Console.WriteLine($"  Skipping nested/deeper mass copy: {targetPath} from {sourcePath}");
                }
            }
            
            foreach (var (targetPath, sourcePath) in uniqueMassCopyInfo)
            {
                var loopMapping = CreateMassCopyLoopMapping(targetPath, sourcePath, translatedMap);
                if (loopMapping != null)
                {
                    flatMappings.Add(loopMapping);
                }
            }

            // Build hierarchical structure from flat mappings
            var hierarchy = BuildHierarchyFromFlatMappings(flatMappings);
            
            Console.WriteLine($"DEBUG: Built hierarchy with {hierarchy.Count} root nodes");
            foreach (var h in hierarchy)
            {
                Console.WriteLine($"  Root: {h.TargetPath}, Children: {h.Children?.Count ?? 0}, Attributes: {h.Attributes?.Count ?? 0}");
            }
            
            // Post-process: Move child mappings into $for loops and make XPaths relative
            ProcessLoopChildMappings(hierarchy);
            
            translatedMap.Mappings.AddRange(hierarchy);

            // Detect and create loops based on XPath patterns
            DetectLoopsFromXPath(translatedMap);
        }

        private string CleanupXPath(string xpath)
        {
            return CleanupXPathInternal(xpath, false);  // Default: don't add namespace prefix
        }

        /// <summary>
        /// Cleans up and normalizes a source XPath expression, adding namespace prefixes as needed.
        /// </summary>
        /// <param name="xpath">The XPath expression to clean up.</param>
        /// <returns>The normalized XPath with proper namespace prefixes.</returns>
        private string CleanupSourceXPath(string xpath)
        {
            return CleanupXPathInternal(xpath, true);
        }

        private string CleanupTargetXPath(string xpath)
        {
            return CleanupXPathInternal(xpath, false);  // Don't add namespace prefix for target XPaths
        }

        private string CleanupXPathInternal(string xpath, bool addSourceNamespacePrefix)
        {
            if (string.IsNullOrEmpty(xpath))
                return xpath;

            // Pattern to match BTM-style XPath: *[local-name()='ElementName'] or @*[local-name()='AttributeName']
            var elementPattern = @"\*\[local-name\(\)='([^']+)'\]";
            var attrPattern = @"@\*\[local-name\(\)='([^']+)'\]";

            // Replace attribute patterns first
            xpath = System.Text.RegularExpressions.Regex.Replace(xpath, attrPattern, "@$1");
            
            // Replace element patterns
            xpath = System.Text.RegularExpressions.Regex.Replace(xpath, elementPattern, "$1");

            // Remove <Schema> placeholder
            xpath = xpath.Replace("/<Schema>/", "/").Replace("<Schema>/", "/").Replace("<Schema>", "");

            // Ensure the XPath starts with / if it doesn't already
            if (!xpath.StartsWith("/"))
            {
                xpath = "/" + xpath;
            }

            
            
            if (addSourceNamespacePrefix && _mapData != null && _mapData.SourceNamespaces != null && _mapData.SourceNamespaces.Count > 0)
            {
                // Check if ANY of the source namespaces is an EDI namespace
                bool hasEdiNamespace = _mapData.SourceNamespaces.Values.Any(ns => 
                    ns.Contains("/EDI/") || ns.Contains("/X12/") || ns.Contains("/EDIFACT/"));
                
                if (hasEdiNamespace)
                {
                    // EDI SCHEMA PATTERN: /ns0:Root/ns0:Loop/ns0:Segment/Field
                    // Only the FINAL LEAF element has no prefix
                    
                    var ediNsPrefix = _mapData.SourceNamespaces
                        .Where(kvp => kvp.Value.Contains("/EDI/") || kvp.Value.Contains("/X12/") || kvp.Value.Contains("/EDIFACT/"))
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault();
                    
                    if (!string.IsNullOrEmpty(ediNsPrefix))
                    {
                        var parts = xpath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (parts.Length > 0)
                        {
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i].StartsWith("@") || parts[i].Contains("("))
                                    continue;
                                
                                var segmentName = parts[i].Contains(":") 
                                    ? parts[i].Substring(parts[i].IndexOf(':') + 1) 
                                    : parts[i];
                                
                                bool isLastElement = (i == parts.Length - 1);
                                
                                if (!isLastElement)
                                {
                                    // ALL non-leaf segments get namespace prefix
                                    parts[i] = $"{ediNsPrefix}:{segmentName}";
                                    Console.WriteLine($"  EDI segment with prefix: {parts[i]}");
                                }
                                else
                                {
                                    // ONLY the final leaf element has no prefix
                                    parts[i] = segmentName;
                                    Console.WriteLine($"  EDI leaf element (no prefix): {parts[i]}");
                                }
                            }
                            
                            xpath = "/" + string.Join("/", parts);
                            Console.WriteLine($"  Final EDI XPath: {xpath}");
                        }
                    }
                }
                else
                {
                    // NON-EDI SCHEMA PATTERN: Add namespace prefix to source root element
                    // For regular XML schemas with a defined namespace, add prefix to root
                    // Example: /Order/CustNum -> /ns1:Order/CustNum
                    
                    // CRITICAL FIX: Find the source schema's namespace by matching the schema name
                    // Don't just pick the first ns* namespace - find the one that matches the actual source schema
                    var sourceNsPrefix = string.Empty;
                    
                    // Strategy 1: Try to match by schema name
                    if (!string.IsNullOrEmpty(_mapData.SourceSchema))
                    {
                        var schemaName = Path.GetFileNameWithoutExtension(_mapData.SourceSchema);
                        Console.WriteLine($"  Looking for source namespace matching schema: {schemaName}");
                        
                        // Find namespace that contains this schema name
                        var matchingEntry = _mapData.SourceNamespaces
                            .FirstOrDefault(kvp => kvp.Value.Contains(schemaName) && 
                                                   kvp.Key.StartsWith("ns") &&
                                                   !kvp.Value.Contains("PropertySchema") &&
                                                   !kvp.Value.Contains("XMLSchema"));
                        
                        if (!string.IsNullOrEmpty(matchingEntry.Key))
                        {
                            sourceNsPrefix = matchingEntry.Key;
                            Console.WriteLine($"  Found source namespace by schema name: {sourceNsPrefix} = {matchingEntry.Value}");
                        }
                    }
                    
                    // Strategy 2: Fallback to first non-utility namespace (excluding ns0 if it's PropertySchema)
                    if (string.IsNullOrEmpty(sourceNsPrefix))
                    {
                        sourceNsPrefix = _mapData.SourceNamespaces
                            .Where(kvp => kvp.Key.StartsWith("ns") && 
                                          !kvp.Value.Contains("PropertySchema") && 
                                          !kvp.Value.Contains("XMLSchema") && 
                                          !kvp.Value.Contains("BizTalk/2003"))
                            .OrderBy(kvp => kvp.Key)
                            .Select(kvp => kvp.Key)
                            .FirstOrDefault();
                        
                        if (!string.IsNullOrEmpty(sourceNsPrefix))
                        {
                            Console.WriteLine($"  Using first non-utility namespace: {sourceNsPrefix}");
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(sourceNsPrefix))
                    {
                        var parts = xpath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (parts.Length > 0 && !parts[0].StartsWith("@") && !parts[0].Contains("(") && !parts[0].Contains(":"))
                        {
                            // Add namespace prefix to the root element only
                            parts[0] = $"{sourceNsPrefix}:{parts[0]}";
                            xpath = "/" + string.Join("/", parts);
                            Console.WriteLine($"  Added namespace prefix to source root: {xpath}");
                        }
                        else
                        {
                            Console.WriteLine($"  Source XPath unchanged: {xpath}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  No source namespace prefix found - using XPath as-is: {xpath}");
                    }
                }
            }

            return xpath;
        }

        private List<LmlMapping> BuildHierarchyFromFlatMappings(List<LmlMapping> flatMappings)
        {
            var result = new List<LmlMapping>();
            var pathTree = new Dictionary<string, LmlMapping>();

            // Separate loop mappings and collection mappings (with $for children) from regular mappings
            var loopMappings = flatMappings.Where(m => m.IsLoop || m.TargetPath.StartsWith("$for(")).ToList();
            var collectionMappings = flatMappings.Where(m => !m.IsLoop && !m.TargetPath.StartsWith("$for(") && m.Children.Any(c => c.TargetPath == "$for" || c.TargetPath.StartsWith("$for("))).ToList();
            var regularMappings = flatMappings.Where(m => !m.IsLoop && !m.TargetPath.StartsWith("$for(") && !m.Children.Any(c => c.TargetPath == "$for" || c.TargetPath.StartsWith("$for("))).ToList();

            // Process loop mappings first to build the structure
            foreach (var loopMapping in loopMappings)
            {
                // Find child mappings that belong to this loop based on XPath hierarchy
                var loopXPath = loopMapping.LoopExpression;
                if (!string.IsNullOrEmpty(loopXPath))
                {
                    // Find all mappings whose source starts with this loop path
                    var childMappings = regularMappings.Where(m => 
                        !string.IsNullOrEmpty(m.SourceExpression) && 
                        m.SourceExpression.StartsWith(loopXPath + "/") &&
                        !m.SourceExpression.StartsWith("$")
                    ).ToList();
                    
                    foreach (var childMapping in childMappings)
                    {
                        // Convert absolute XPath to relative within the loop
                        var relativeXPath = MakeXPathRelative(childMapping.SourceExpression, loopXPath);
                        childMapping.SourceExpression = relativeXPath;
                        
                        // Add as child to loop
                        loopMapping.Children.Add(childMapping);
                        regularMappings.Remove(childMapping);
                        Console.WriteLine($"  Added child to loop: {childMapping.TargetPath} = {relativeXPath}");
                    }
                }
            }

            foreach (var mapping in regularMappings)
            {
                // Remove leading / from target path before processing
                var cleanedPath = mapping.TargetPath.TrimStart('/');
                var parts = cleanedPath.Split('/');
                var currentPath = "";
                LmlMapping parentMapping = null;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    
                    // Skip empty parts
                    if (string.IsNullOrEmpty(part))
                        continue;
                        
                    currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;
                    var isLast = (i == parts.Length - 1);
                    var isAttribute = part.StartsWith("@");

                    if (!pathTree.TryGetValue(currentPath, out var existingMapping))
                    {
                        // Create new mapping node
                        var newMapping = new LmlMapping
                        {
                            TargetPath = currentPath,
                            SourceExpression = (isLast && !isAttribute) ? mapping.SourceExpression : "",
                            IsAttribute = isAttribute,
                            IsLoop = false,
                            Children = new List<LmlMapping>(),
                            Attributes = new Dictionary<string, string>()
                        };

                        pathTree[currentPath] = newMapping;

                        if (parentMapping == null)
                        {
                            // Root level - only add if not an attribute
                            if (!isAttribute)
                            {
                                result.Add(newMapping);
                                parentMapping = newMapping;
                            }
                        }
                        else
                        {
                            // Add as child of parent
                            if (isAttribute && isLast)
                            {
                                // Attributes go into the parent's Attributes dictionary
                                var attrName = part; // Keep the @ prefix
                                parentMapping.Attributes[attrName] = mapping.SourceExpression;
                            }
                            else
                            {
                                // Regular child element
                                if (!isAttribute)
                                {
                                    parentMapping.Children.Add(newMapping);
                                    parentMapping = newMapping;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Node exists
                        if (isLast)
                        {
                            if (isAttribute)
                            {
                                // Add to parent's attributes if this is an attribute
                                if (parentMapping != null)
                                {
                                    // CRITICAL FIX: Detect duplicate mappings and warn the user
                                    if (parentMapping.Attributes.ContainsKey(part) && 
                                        !string.IsNullOrEmpty(parentMapping.Attributes[part]) &&
                                        !string.IsNullOrEmpty(mapping.SourceExpression) &&
                                        parentMapping.Attributes[part] != mapping.SourceExpression)
                                    {
                                        var firstSource = parentMapping.Attributes[part];
                                        var secondSource = mapping.SourceExpression;
                                        Console.WriteLine($"  WARNING: Duplicate mapping detected to '{currentPath}'");
                                        Console.WriteLine($"    First mapping:  {firstSource}");
                                        Console.WriteLine($"    Second mapping: {secondSource}");
                                        Console.WriteLine($"    Keeping FIRST mapping (last mapping wins disabled)");
                                        // Don't overwrite - keep the first mapping
                                    }
                                    else
                                    {
                                        parentMapping.Attributes[part] = mapping.SourceExpression;
                                    }
                                }
                            }
                            else
                            {
                                // CRITICAL FIX: Detect duplicate mappings and warn the user
                                if (!string.IsNullOrEmpty(existingMapping.SourceExpression) &&
                                    !string.IsNullOrEmpty(mapping.SourceExpression) &&
                                    existingMapping.SourceExpression != mapping.SourceExpression)
                                {
                                    var firstSource = existingMapping.SourceExpression;
                                    var secondSource = mapping.SourceExpression;
                                    Console.WriteLine($"  WARNING: Duplicate mapping detected to '{currentPath}'");
                                    Console.WriteLine($"    First mapping:  {firstSource}");
                                    Console.WriteLine($"    Second mapping: {secondSource}");
                                    Console.WriteLine($"    Keeping FIRST mapping (last mapping wins disabled)");
                                    // Don't overwrite - keep the first mapping
                                }
                                else if (!string.IsNullOrEmpty(mapping.SourceExpression))
                                {
                                    // Update with source expression for elements
                                    existingMapping.SourceExpression = mapping.SourceExpression;
                                }
                            }
                        }
                        
                        // Only update parentMapping for non-attributes
                        if (!isAttribute)
                        {
                            parentMapping = existingMapping;
                        }
                    }
                }
            }

            // Now add collection mappings (with $for loops) as children of their parent elements
            foreach (var collectionMapping in collectionMappings)
            {
                var cleanedPath = collectionMapping.TargetPath.TrimStart('/');
                var parts = cleanedPath.Split('/');
                
                if (parts.Length == 1)
                {
                    // Root-level collection (shouldn't happen, but handle it)
                    result.Add(collectionMapping);
                }
                else
                {
                    // Find parent and add as child
                    var parentPath = string.Join("/", parts.Take(parts.Length - 1));
                    
                    if (pathTree.TryGetValue(parentPath, out var parentMapping))
                    {
                        // CRITICAL FIX: Check if parent already has a child with this name
                        var collectionName = parts[parts.Length - 1];
                        var existingChild = parentMapping.Children.FirstOrDefault(c => c.TargetPath == collectionName);
                        
                        if (existingChild != null)
                        {
                            // Merge the $for loop into the existing child instead of creating a duplicate
                            Console.WriteLine($"  MERGE: Found existing collection '{collectionName}' - merging $for loop into it");
                            Console.WriteLine($"    Existing child has {existingChild.Children.Count} children, $for has {collectionMapping.Children.Count} children");
                            
                            // Add all children from the collection mapping (which includes the $for loop) to the existing child
                            foreach (var child in collectionMapping.Children)
                            {
                                existingChild.Children.Add(child);
                                Console.WriteLine($"    Added child '{child.TargetPath}' to existing collection");
                            }
                        }
                        else
                        {
                            // Create a simplified version with just the collection name
                            var simplifiedCollection = new LmlMapping
                            {
                                TargetPath = collectionName,
                                IsLoop = false,
                                SourceExpression = "",
                                Children = collectionMapping.Children,  // Preserve $for children!
                                Attributes = new Dictionary<string, string>()
                            };
                            
                            Console.WriteLine($"  Creating new collection '{collectionName}' with {collectionMapping.Children.Count} children");
                            parentMapping.Children.Add(simplifiedCollection);
                        }
                    }
                    else
                    {
                        // Parent not found, add to result
                        Console.WriteLine($"  WARNING: Parent '{parentPath}' not found for collection '{parts[parts.Length - 1]}', adding to root");
                        result.Add(collectionMapping);
                    }
                }
            }

            // Add loop mappings as children of their parent elements
            foreach (var loopMapping in loopMappings)
            {
                var cleanedPath = loopMapping.TargetPath.TrimStart('/');
                
                // Skip if this is already a $for expression
                if (cleanedPath.StartsWith("$for("))
                {
                    // This is a standalone $for - needs to be added under the correct parent
                    // For now, add it to result (will be moved to correct parent in LmlGenerator)
                    result.Add(loopMapping);
                    continue;
                }
                
                var parts = cleanedPath.Split('/');
                
                if (parts.Length == 1)
                {
                    // Root-level loop (shouldn't happen, but handle it)
                    result.Add(loopMapping);
                }
                else
                {
                    // Find parent and add as child
                    var parentPath = string.Join("/", parts.Take(parts.Length - 1));
                    
                    if (pathTree.TryGetValue(parentPath, out var parentMapping))
                    {
                        // Preserve the loop mapping with its children
                        var simplifiedLoop = new LmlMapping
                        {
                            TargetPath = parts[parts.Length - 1],
                            IsLoop = true,
                            LoopExpression = loopMapping.LoopExpression,
                            LoopVariable = loopMapping.LoopVariable,
                            SourceExpression = "",
                            Children = loopMapping.Children,  // Preserve children!
                            Attributes = new Dictionary<string, string>()
                        };
                        
                        parentMapping.Children.Add(simplifiedLoop);
                    }
                    else
                    {
                        // Parent not found, add to result
                        result.Add(loopMapping);
                    }
                }
            }

            return result;
        }
        
        /// <summary>
        /// Converts an absolute XPath to a relative XPath within a loop context.
        /// </summary>
        /// <param name="absolutePath">The absolute XPath to convert.</param>
        /// <param name="loopBasePath">The base XPath of the loop context.</param>
        /// <returns>The relative XPath with namespace prefixes on first segment only.</returns>
        /// <remarks>
        /// Azure Data Mapper uses implicit loop context without $item/ prefix.
        /// Only the first segment retains its namespace prefix; nested segments have prefixes removed.
        /// </remarks>
        /// <example>
        /// /ns0:X12_00401_850/ns0:PO1Loop1/ns0:PO1/ns0:PO102 relative to /ns0:X12_00401_850/ns0:PO1Loop1
        /// returns ns0:PO1/PO102
        /// </example>
        private string MakeXPathRelative(string absolutePath, string loopBasePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(loopBasePath))
                return absolutePath;
            
            Console.WriteLine($"    DEBUG MakeXPathRelative: abs='{absolutePath}', base='{loopBasePath}'");
                
            // Remove leading slash for comparison
            var absPath = absolutePath.TrimStart('/');
            var basePath = loopBasePath.TrimStart('/');
            
            
            // Check if absolute path starts with the loop base path
            if (absPath.StartsWith(basePath + "/"))
            {
                // Extract the relative part
                var relativePart = absPath.Substring(basePath.Length + 1); // +1 for the slash
                Console.WriteLine($"      Extracted relative part: '{relativePart}'");
                
                var segments = relativePart.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    // Keep namespace prefix ONLY on the first segment
                    if (i > 0 && segments[i].Contains(":") && !segments[i].StartsWith("@"))
                    {
                        var colonIndex = segments[i].IndexOf(':');
                        segments[i] = segments[i].Substring(colonIndex + 1);
                    }
                }
                
                relativePart = string.Join("/", segments);
                Console.WriteLine($"      Final relative path: '{relativePart}'");
                
                // The loop context is IMPLICIT - just return the relative path
                return relativePart;
            }
            
            // If it doesn't start with the loop path, return as-is (might be an external reference)
            Console.WriteLine($"      Path doesn't start with loop base - returning unchanged");
            return absolutePath;
        }
        
        /// <summary>
        /// Makes XPath relative even if it's inside a function call like abs(/ns0:X12.../ns0:PO1/PO102)
        /// </summary>
        private string MakeXPathRelativeInExpression(string expression, string loopBasePath)
        {
            if (string.IsNullOrEmpty(expression) || string.IsNullOrEmpty(loopBasePath))
                return expression;
            
            // Check if this expression contains the loop base path
            if (!expression.Contains(loopBasePath))
                return expression;
            
            // If it's a simple XPath (no function calls), use the simpler method
            if (!expression.Contains("("))
            {
                return MakeXPathRelative(expression, loopBasePath);
            }
            
            // For function calls like abs(/ns0:X12_00401_850/ns0:PO1Loop1/ns0:PO1/ns0:PO102)
            // We need to find and replace the XPath inside the function
            // Simple approach: replace all occurrences of the absolute path with relative
            var normalizedLoop = loopBasePath.TrimStart('/');
            
            // Find the absolute path pattern in the expression
            // Look for the loop path followed by additional segments
            var startIndex = expression.IndexOf(loopBasePath);
            if (startIndex >= 0)
            {
                // Find the end of the XPath (ends at ), comma, or end of string)
                var endIndex = startIndex + loopBasePath.Length;
                while (endIndex < expression.Length && 
                       expression[endIndex] != ')' && 
                       expression[endIndex] != ',' &&
                       !char.IsWhiteSpace(expression[endIndex]))
                {
                    endIndex++;
                }
                
                // Extract the full absolute path
                var absolutePath = expression.Substring(startIndex, endIndex - startIndex);
                
                // Make it relative
                var relativePath = MakeXPathRelative(absolutePath, loopBasePath);
                
                // Replace in the expression
                return expression.Replace(absolutePath, relativePath);
            }
            
            return expression;
        }

        private void DetectLoopsFromXPath(TranslatedMapData translatedMap)
        {
            // Group mappings by potential parent repeating structures
            var mappingGroups = new Dictionary<string, List<LmlMapping>>();

            foreach (var mapping in translatedMap.Mappings.ToList())
            {
                // Try to identify parent path that might be repeating
                var targetParts = mapping.TargetPath.Split('/');
                
                // Look for potential array element (heuristic: path depth > 3)
                if (targetParts.Length > 3)
                {
                    // Take parent 2 levels up as potential loop root
                    var potentialLoopPath = string.Join("/", targetParts.Take(targetParts.Length - 1));
                    
                    if (!mappingGroups.ContainsKey(potentialLoopPath))
                    {
                        mappingGroups[potentialLoopPath] = new List<LmlMapping>();
                    }
                    
                    mappingGroups[potentialLoopPath].Add(mapping);
                }
            }

            // Create loops for groups with multiple children
            foreach (var group in mappingGroups.Where(g => g.Value.Count > 2))
            {
                // Find common source path prefix
                var sourcePaths = group.Value
                    .Select(m => m.SourceExpression)
                    .Where(s => !s.Contains("(") && s.StartsWith("/")) // Only actual paths, not function calls
                    .ToList();

                if (sourcePaths.Count > 1)
                {
                    var commonSourcePrefix = FindCommonPathPrefix(sourcePaths);
                    
                    if (!string.IsNullOrEmpty(commonSourcePrefix))
                    {
                        // Create loop
                        var loopMapping = new LmlMapping
                        {
                            IsLoop = true,
                            LoopExpression = commonSourcePrefix,
                            TargetPath = group.Key,
                            Children = new List<LmlMapping>()
                        };

                        // Move children under loop and make paths relative
                        foreach (var child in group.Value)
                        {
                            // Make source path relative to loop
                            if (child.SourceExpression.StartsWith(commonSourcePrefix))
                            {
                                var relativePath = child.SourceExpression.Substring(commonSourcePrefix.Length).TrimStart('/');
                                child.SourceExpression = "./" + relativePath;
                            }

                            // Make target path relative
                            if (child.TargetPath.StartsWith(group.Key))
                            {
                                var relativePath = child.TargetPath.Substring(group.Key.Length).TrimStart('/');
                                child.TargetPath = relativePath;
                            }

                            loopMapping.Children.Add(child);
                            translatedMap.Mappings.Remove(child);
                        }

                        translatedMap.Mappings.Add(loopMapping);
                    }
                }
            }
        }

        private string FindCommonPathPrefix(List<string> paths)
        {
            if (paths.Count == 0) return string.Empty;
            if (paths.Count == 1) return paths[0];

            var parts = paths[0].Split('/');
            var commonParts = new List<string>();

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (paths.All(p => p.Split('/').Length > i && p.Split('/')[i] == part))
                {
                    commonParts.Add(part);
                }
                else
                {
                    break;
                }
            }

            // Return common prefix, but exclude the last element (which is likely the item itself)
            if (commonParts.Count > 1)
            {
                return string.Join("/", commonParts.Take(commonParts.Count - 1));
            }

            return string.Empty;
        }

        private string ResolveSourceExpression(string linkFrom)
        {
            Console.WriteLine($"DEBUG ResolveSourceExpression: linkFrom={linkFrom}");
            
            // Check if linkFrom is a schema node
            var sourceNode = GetSchemaNode(linkFrom, _mapData.SourceTree);
            if (sourceNode != null)
            {
                Console.WriteLine($"  Found schema node: {sourceNode.Name}, XPath={sourceNode.XPath}");
                var result = BuildSourceXPath(sourceNode);
                Console.WriteLine($"  Returning XPath: {result}");
                return result;
            }

            // Check if linkFrom is a functoid
            var functoid = _mapData.Functoids.FirstOrDefault(f => f.FunctoidId == linkFrom);
            if (functoid != null)
            {
                Console.WriteLine($"  Found functoid: Type={functoid.FunctoidType}, ID={functoid.FunctoidId}");
                var result = TranslateFunctoid(functoid);
                Console.WriteLine($"  Functoid translated to: {result}");
                return result;
            }

            Console.WriteLine($"  Not found as schema node or functoid, returning as-is: {linkFrom}");
            return linkFrom;
        }

        /// <summary>
        /// Translates a single BizTalk functoid to its LML expression equivalent.
        /// </summary>
        /// <param name="functoid">The functoid to translate.</param>
        /// <returns>The translated expression string compatible with Azure Data Mapper.</returns>
        /// <remarks>
        /// Supports string, math, date, logical, cumulative, scientific, and custom functoid types.
        /// Results are cached to avoid redundant translation of reused functoids.
        /// </remarks>
        private string TranslateFunctoid(BtmFunctoid functoid)
        {
            if (_functoidOutputs.TryGetValue(functoid.FunctoidId, out string cached))
            {
                return cached;
            }

            // Resolve input parameters
            var resolvedParams = functoid.InputParameters
                .Select(p => ResolveParameter(p))
                .ToList();

            string expression;
            
            // Convert switch expression to traditional switch statement for C# 7.3 compatibility
            // Functoid type names match BaseFunctoidIDs enum from BizTalk Server source
            switch (functoid.FunctoidType)
            {
                // String functoids (StringFunctoids.cs - FID 101-110)
                case "StringFind":
                    expression = resolvedParams.Count >= 2 ? $"contains({resolvedParams[0]}, {resolvedParams[1]})" : $"/* StringFind requires 2 params */";
                    break;
                case "StringLeft":
                    expression = resolvedParams.Count >= 2 ? $"substring({resolvedParams[0]}, 1, {resolvedParams[1]})" : $"/* StringLeft requires 2 params */";
                    break;
                case "StringLowerCase":
                    expression = resolvedParams.Count >= 1 ? $"lower-case({resolvedParams[0]})" : $"/* StringLowerCase requires 1 param */";
                    break;
                case "StringRight":
                    expression = resolvedParams.Count >= 2 ? $"substring({resolvedParams[0]}, string-length({resolvedParams[0]}) - {resolvedParams[1]} + 1)" : $"/* StringRight requires 2 params */";
                    break;
                case "StringSize":
                    expression = resolvedParams.Count >= 1 ? $"string-length({resolvedParams[0]})" : $"/* StringSize requires 1 param */";
                    break;
                case "StringSubstring":
                    expression = resolvedParams.Count == 3 
                        ? $"substring({resolvedParams[0]}, {resolvedParams[1]}, {resolvedParams[2]})"
                        : resolvedParams.Count == 2
                        ? $"substring({resolvedParams[0]}, {resolvedParams[1]})"
                        : $"/* StringSubstring requires 2-3 params */";
                    break;
                case "StringConcatenate":
                    expression = $"concat({string.Join(", ", resolvedParams)})";
                    break;
                case "StringTrimLeft":
                    expression = resolvedParams.Count >= 1 ? $"trim-left({resolvedParams[0]})" : $"/* StringTrimLeft requires 1 param */";
                    break;
                case "StringTrimRight":
                    expression = resolvedParams.Count >= 1 ? $"trim-right({resolvedParams[0]})" : $"/* StringTrimRight requires 1 param */";
                    break;
                case "StringUpperCase":
                    expression = resolvedParams.Count >= 1 ? $"upper-case({resolvedParams[0]})" : $"/* StringUpperCase requires 1 param */";
                    break;
                
                // Additional String functoids (Azure Data Mapper patterns)
                case "StringReplace":
                    expression = resolvedParams.Count >= 3 
                        ? $"replace({resolvedParams[0]}, {resolvedParams[1]}, {resolvedParams[2]})"
                        : $"/* StringReplace requires 3 params */";
                    break;
                case "StringStartsWith":
                    expression = resolvedParams.Count >= 2 
                        ? $"starts-with({resolvedParams[0]}, {resolvedParams[1]})"
                        : $"/* StringStartsWith requires 2 params */";
                    break;
                case "StringEndsWith":
                    expression = resolvedParams.Count >= 2 
                        ? $"ends-with({resolvedParams[0]}, {resolvedParams[1]})"
                        : $"/* StringEndsWith requires 2 params */";
                    break;
                case "StringNormalize":
                case "StringNormalizeSpace":
                    expression = resolvedParams.Count >= 1 
                        ? $"normalize-space({resolvedParams[0]})"
                        : $"/* StringNormalize requires 1 param */";
                    break;
                case "StringMatches":
                case "RegexMatches":
                    expression = resolvedParams.Count >= 2 
                        ? $"matches({resolvedParams[0]}, {resolvedParams[1]})"
                        : $"/* Matches requires 2 params */";
                    break;
                case "StringTokenize":
                    expression = resolvedParams.Count >= 2 
                        ? $"tokenize({resolvedParams[0]}, {resolvedParams[1]})"
                        : $"/* Tokenize requires 2 params */";
                    break;

                // Math functoids (MathFunctoids.cs - FID 111-121)
                case "MathAbs":
                    expression = resolvedParams.Count >= 1 ? $"abs({resolvedParams[0]})" : $"/* MathAbs requires 1 param */";
                    break;
                case "MathInt":
                    expression = resolvedParams.Count >= 1 ? $"floor({resolvedParams[0]})" : $"/* MathInt requires 1 param */";
                    break;
                case "MathMax":
                    expression = $"max({string.Join(", ", resolvedParams)})";
                    break;
                case "MathMin":
                    expression = $"min({string.Join(", ", resolvedParams)})";
                    break;
                case "MathMod":
                    expression = resolvedParams.Count >= 2 ? $"modulo({resolvedParams[0]}, {resolvedParams[1]})" : $"/* MathMod requires 2 params */";
                    break;
                case "MathRound":
                    expression = resolvedParams.Count >= 1 ? $"round({resolvedParams[0]})" : $"/* MathRound requires 1 param */";
                    break;
                case "MathSqrt":
                    expression = resolvedParams.Count >= 1 ? $"sqrt({resolvedParams[0]})" : $"/* MathSqrt requires 1 param */";
                    break;
                case "MathCeiling":
                case "Ceiling":
                    expression = resolvedParams.Count >= 1 ? $"ceiling({resolvedParams[0]})" : $"/* MathCeiling requires 1 param */";
                    break;
                case "MathAdd":
                    expression = $"add({string.Join(", ", resolvedParams)})";
                    break;
                case "MathSubtract":
                    expression = resolvedParams.Count >= 2 ? $"subtract({resolvedParams[0]}, {resolvedParams[1]})" : $"/* MathSubtract requires 2 params */";
                    break;
                case "MathMultiply":
                    expression = $"multiply({string.Join(", ", resolvedParams)})";
                    break;
                case "MathDivide":
                    expression = resolvedParams.Count >= 2 ? $"divide({resolvedParams[0]}, {resolvedParams[1]})" : $"/* MathDivide requires 2 params */";
                    break;

                // Date functoids (DateFunctoids.cs - FID 122-125)
                case "DateAddDays":
                    expression = resolvedParams.Count >= 2 ? $"add-days({resolvedParams[0]}, {resolvedParams[1]})" : $"/* DateAddDays requires 2 params */";
                    break;
                case "DateCurrentDate":
                    expression = $"current-date()";
                    break;
                case "DateCurrentTime":
                    expression = $"current-time()";
                    break;
                case "DateCurrentDateTime":
                    expression = $"current-dateTime()";
                    break;
                case "DateFormat":
                case "DateFormatDateTime":
                    expression = resolvedParams.Count >= 2 
                        ? $"format-dateTime({resolvedParams[0]}, {resolvedParams[1]})"
                        : resolvedParams.Count >= 1
                        ? $"format-dateTime({resolvedParams[0]}, '[Y0001]-[M01]-[D01]T[H01]:[m01]:[s01]')"
                        : $"/* DateFormat requires 1-2 params */";
                    break;
                case "DateFormatDate":
                    expression = resolvedParams.Count >= 2 
                        ? $"format-date({resolvedParams[0]}, {resolvedParams[1]})"
                        : resolvedParams.Count >= 1
                        ? $"format-date({resolvedParams[0]}, '[Y0001]-[M01]-[D01]')"
                        : $"/* DateFormatDate requires 1-2 params */";
                    break;
                case "DateFormatTime":
                    expression = resolvedParams.Count >= 2 
                        ? $"format-time({resolvedParams[0]}, {resolvedParams[1]})"
                        : resolvedParams.Count >= 1
                        ? $"format-time({resolvedParams[0]}, '[H01]:[m01]:[s01]')"
                        : $"/* DateFormatTime requires 1-2 params */";
                    break;

                // Conversion functoids (ConversionFunctoids.cs - FID 126-129)
                case "ConvertAsc":
                    expression = resolvedParams.Count >= 1 ? $"string-to-codepoints({resolvedParams[0]})" : $"/* ConvertAsc requires 1 param */";
                    break;
                case "ConvertChr":
                    expression = resolvedParams.Count >= 1 ? $"codepoints-to-string({resolvedParams[0]})" : $"/* ConvertChr requires 1 param */";
                    break;
                case "ConvertHex":
                    expression = resolvedParams.Count >= 1 ? $"to-hex({resolvedParams[0]})" : $"/* ConvertHex requires 1 param */";
                    break;
                case "ConvertOct":
                    expression = resolvedParams.Count >= 1 ? $"to-octal({resolvedParams[0]})" : $"/* ConvertOct requires 1 param */";
                    break;

                // Scientific functoids (ScientificFunctoids.cs - FID 130-139)
                case "SciArcTan":
                    expression = resolvedParams.Count >= 1 ? $"atan({resolvedParams[0]})" : $"/* SciArcTan requires 1 param */";
                    break;
                case "SciCos":
                    expression = resolvedParams.Count >= 1 ? $"cos({resolvedParams[0]})" : $"/* SciCos requires 1 param */";
                    break;
                case "SciSin":
                    expression = resolvedParams.Count >= 1 ? $"sin({resolvedParams[0]})" : $"/* SciSin requires 1 param */";
                    break;
                case "SciTan":
                    expression = resolvedParams.Count >= 1 ? $"tan({resolvedParams[0]})" : $"/* SciTan requires 1 param */";
                    break;
                case "SciExp":
                    expression = resolvedParams.Count >= 1 ? $"exp({resolvedParams[0]})" : $"/* SciExp requires 1 param */";
                    break;
                case "SciLog":
                    expression = resolvedParams.Count >= 1 ? $"log({resolvedParams[0]})" : $"/* SciLog requires 1 param */";
                    break;
                case "SciExp10":
                    expression = resolvedParams.Count >= 1 ? $"pow(10, {resolvedParams[0]})" : $"/* SciExp10 requires 1 param */";
                    break;
                case "SciLog10":
                    expression = resolvedParams.Count >= 1 ? $"log10({resolvedParams[0]})" : $"/* SciLog10 requires 1 param */";
                    break;
                case "SciPow":
                    expression = resolvedParams.Count >= 2 ? $"pow({resolvedParams[0]}, {resolvedParams[1]})" : $"/* SciPow requires 2 params */";
                    break;
                case "SciLogn":
                    expression = resolvedParams.Count >= 2 ? $"logn({resolvedParams[0]}, {resolvedParams[1]})" : $"/* SciLogn requires 2 params */";
                    break;

                // Logical functoids (LogicalFunctoids.cs - FID 311-321, 701, 705, 706)
                // CRITICAL: Logical functoids when connected to a target field imply if-then-else logic
                // BizTalk Pattern: [Source] -> [LogicalEq] -> [Target] means "if equal, map source, else null"
                // We detect this pattern by checking if the functoid output links to a target field
                case "LogicalGt":
                    expression = DetectConditionalMappingPattern(functoid, resolvedParams, "greater-than");
                    break;
                case "LogicalGte":
                    expression = DetectConditionalMappingPattern(functoid, resolvedParams, "greater-than-or-equal");
                    break;
                case "LogicalLt":
                    expression = DetectConditionalMappingPattern(functoid, resolvedParams, "less-than");
                    break;
                case "LogicalLte":
                    expression = DetectConditionalMappingPattern(functoid, resolvedParams, "less-than-or-equal");
                    break;
                case "LogicalEq":
                    expression = DetectConditionalMappingPattern(functoid, resolvedParams, "is-equal");
                    break;
                case "LogicalNe":
                    expression = resolvedParams.Count >= 2 ? $"not(is-equal({resolvedParams[0]}, {resolvedParams[1]}))" : $"/* LogicalNe requires 2 params */";
                    break;
                case "LogicalIsString":
                    expression = resolvedParams.Count >= 1 ? $"is-string({resolvedParams[0]})" : $"/* LogicalIsString requires 1 param */";
                    break;
                case "LogicalIsDate":
                    expression = resolvedParams.Count >= 1 ? $"is-date({resolvedParams[0]})" : $"/* LogicalIsDate requires 1 param */";
                    break;
                case "LogicalIsNumeric":
                    expression = resolvedParams.Count >= 1 ? $"is-number({resolvedParams[0]})" : $"/* LogicalIsNumeric requires 1 param */";
                    break;
                case "LogicalOr":
                    expression = $"or({string.Join(", ", resolvedParams)})";
                    break;
                case "LogicalAnd":
                    expression = $"and({string.Join(", ", resolvedParams)})";
                    break;
                case "LogicalExistence":
                    expression = resolvedParams.Count >= 1 ? $"exists({resolvedParams[0]})" : $"/* LogicalExistence requires 1 param */";
                    break;
                case "LogicalNot":
                    expression = resolvedParams.Count >= 1 ? $"not({resolvedParams[0]})" : $"/* LogicalNot requires 1 param */";
                    break;
                case "IsNil":
                    expression = resolvedParams.Count >= 1 ? $"is-null({resolvedParams[0]})" : $"/* IsNil requires 1 param */";
                    break;

                // Advanced functoids - Index/Count (FID 322-323)
                case "Count":
                    expression = resolvedParams.Count >= 1 ? $"count({resolvedParams[0]})" : $"/* Count requires 1 param */";
                    break;
                case "Index":
                    expression = resolvedParams.Count >= 2 ? $"position-at({resolvedParams[0]}, {resolvedParams[1]})" : $"position()";
                    break;

                // Cumulative functoids (CumulativeFunctoids.cs - FID 324-328)
                case "CumulativeSum":
                    expression = resolvedParams.Count >= 1 ? $"sum({resolvedParams[0]})" : $"/* CumulativeSum requires 1 param */";
                    break;
                case "CumulativeAvg":
                    expression = resolvedParams.Count >= 1 ? $"avg({resolvedParams[0]})" : $"/* CumulativeAvg requires 1 param */";
                    break;
                case "CumulativeMin":
                    expression = resolvedParams.Count >= 1 ? $"min({resolvedParams[0]})" : $"/* CumulativeMin requires 1 param */";
                    break;
                case "CumulativeMax":
                    expression = resolvedParams.Count >= 1 ? $"max({resolvedParams[0]})" : $"/* CumulativeMax requires 1 param */";
                    break;
                case "CumulativeConcat":
                    expression = resolvedParams.Count >= 1 ? $"string-join({resolvedParams[0]}, '')" : $"/* CumulativeConcat requires 1 param */";
                    break;

                // Advanced functoids - Value Mapping (FID 374-376)
                case "ValueMappingFlattening":
                    expression = resolvedParams.Count == 2 
                        ? $"if-then-else({resolvedParams[0]}, {resolvedParams[1]}, null)"
                        : $"/* ValueMappingFlattening requires 2 params */";
                    break;
                case "ValueMapping":
                    expression = resolvedParams.Count == 2 
                        ? $"if-then-else({resolvedParams[0]}, {resolvedParams[1]}, null)"
                        : resolvedParams.Count >= 3
                        ? $"if-then-else({resolvedParams[0]}, {resolvedParams[1]}, {resolvedParams[2]})"
                        : $"/* ValueMapping requires 2-3 params */";
                    break;
                case "NilValue":
                    expression = $"null /* Nil Value */";
                    break;

                // Advanced functoids - Looping/Iteration (FID 424, 474)
                case "Looping":
                    // For Looping functoids, return the source XPath that will be used in $for loops
                    expression = resolvedParams.Count >= 1 ? $"$for({resolvedParams[0]})" : "$loop";
                    break;
                case "Iteration":
                    expression = $"position()";
                    break;

                // Scripting functoid (FID 260)
                case "Scripting":
                    expression = TranslateScriptingFunctoid(functoid, resolvedParams);
                    break;

                // Database functoids (FID 524, 574-575)
                case "DBLookup":
                    expression = $"/* DBLookup - requires manual review */";
                    break;
                case "DBValueExtract":
                    expression = $"/* DBValueExtract - requires manual review */";
                    break;
                case "DBErrorExtract":
                    expression = $"/* DBErrorExtract - requires manual review */";
                    break;

                // Advanced functoids - XPath, Table, Assert (FID 702-707)
                case "XPath":
                    expression = resolvedParams.Count >= 1 ? resolvedParams[0] : $"/* XPath requires 1 param */";
                    break;
                case "TableLooping":
                    expression = $"/* TableLooping - requires manual review */";
                    break;
                case "TableExtractor":
                    expression = $"/* TableExtractor - requires manual review */";
                    break;
                case "Assert":
                    expression = resolvedParams.Count >= 3 
                        ? $"if-then-else({resolvedParams[0]}, {resolvedParams[1]}, {resolvedParams[2]})"
                        : $"/* Assert requires 3 params */";
                    break;

                // Advanced functoids - Mass operations (FID 800-802)
                case "KeyMatch":
                    expression = $"/* KeyMatch - requires manual review */";
                    break;
                case "ExistenceLooping":
                    expression = "$loop";  // Similar to Looping
                    break;
                case "MassCopy":
                    expression = resolvedParams.Count >= 1 ? $"$copy({resolvedParams[0]})" : "$copy()";
                    break;

                // Legacy support - keep old names for backward compatibility
                case "Add":
                    expression = $"add({string.Join(", ", resolvedParams)})";
                    break;
                case "Subtract":
                    expression = resolvedParams.Count >= 2 ? $"subtract({resolvedParams[0]}, {resolvedParams[1]})" : $"/* Subtract requires 2 params */";
                    break;
                case "Multiply":
                    expression = $"multiply({string.Join(", ", resolvedParams)})";
                    break;
                case "Divide":
                    expression = resolvedParams.Count >= 2 ? $"divide({resolvedParams[0]}, {resolvedParams[1]})" : $"/* Divide requires 2 params */";
                    break;
                case "Abs":
                    expression = resolvedParams.Count >= 1 ? $"abs({resolvedParams[0]})" : $"/* Abs requires 1 param */";
                    break;
                case "Concatenate":
                    expression = $"concat({string.Join(", ", resolvedParams)})";
                    break;
                case "UpperCase":
                    expression = resolvedParams.Count >= 1 ? $"upper-case({resolvedParams[0]})" : $"/* UpperCase requires 1 param */";
                    break;
                case "LowerCase":
                    expression = resolvedParams.Count >= 1 ? $"lower-case({resolvedParams[0]})" : $"/* LowerCase requires 1 param */";
                    break;
                case "DateTime":
                    expression = $"current-dateTime()";
                    break;
                case "Date":
                    expression = $"current-date()";
                    break;
                case "Time":
                    expression = $"current-time()";
                    break;

                default:
                    expression = $"/* Unknown functoid: {functoid.FunctoidType} */";
                    break;
            }

            _functoidOutputs[functoid.FunctoidId] = expression;
            return expression;
        }

        /// <summary>
        /// Detects if a logical functoid is used in a conditional mapping pattern and generates appropriate if-then-else syntax.
        /// </summary>
        /// <param name="functoid">The logical functoid to analyze.</param>
        /// <param name="resolvedParams">The resolved parameter expressions.</param>
        /// <param name="comparisonFunction">The comparison function name (e.g., "is-equal", "greater-than").</param>
        /// <returns>An if-then-else expression if the pattern is detected; otherwise a simple comparison expression.</returns>
        /// <remarks>
        /// BizTalk pattern: When a logical functoid connects directly to a target field, it implies:
        /// If condition is true: map the first input value; If condition is false: don't map (null).
        /// Azure Data Mapper requires explicit if-then-else syntax for this pattern.
        /// </remarks>
        private string DetectConditionalMappingPattern(BtmFunctoid functoid, List<string> resolvedParams, string comparisonFunction)
        {
            // Check if this functoid's output links to a target field (not another functoid)
            var outputLinks = _mapData.Links.Where(l => l.LinkFrom == functoid.FunctoidId).ToList();
            
            bool linksToTarget = outputLinks.Any(link => 
            {
                // Check if LinkTo points to a target schema node (not another functoid)
                var targetFunctoid = _mapData.Functoids.FirstOrDefault(f => f.FunctoidId == link.LinkTo);
                return targetFunctoid == null; // If not a functoid, it's likely a target field
            });
            
            if (linksToTarget && resolvedParams.Count >= 2)
            {
                // PATTERN DETECTED: This is a conditional mapping
                // The comparison result determines if the first parameter value should be mapped
                // Format: if-then-else(comparison, valueToMap, null)
                
                Console.WriteLine($"  CONDITIONAL PATTERN: {functoid.FunctoidType} functoid links to target field");
                Console.WriteLine($"    Generating if-then-else({comparisonFunction}({resolvedParams[0]}, {resolvedParams[1]}), {resolvedParams[0]}, null)");
                
                return $"if-then-else({comparisonFunction}({resolvedParams[0]}, {resolvedParams[1]}), {resolvedParams[0]}, null)";
            }
            
            // Default: standalone comparison (used as input to another functoid)
            if (resolvedParams.Count >= 2)
            {
                return $"{comparisonFunction}({resolvedParams[0]}, {resolvedParams[1]})";
            }
            
            return $"/* {comparisonFunction} requires 2 params */";
        }
        
        private string TranslateScriptingFunctoid(BtmFunctoid functoid, List<string> resolvedParams)
        {
            if (!string.IsNullOrEmpty(functoid.ScripterCode))
            {
                // Detect common regex patterns in inline code
                var code = functoid.ScripterCode;
                
                // Check for Regex.IsMatch pattern
                if (code.Contains("Regex.IsMatch") || code.Contains("Regex.Match"))
                {
                    // Try to extract pattern
                    var match = System.Text.RegularExpressions.Regex.Match(code, @"Regex\.IsMatch\([^,]+,\s*""([^""]+)""\)");
                    if (match.Success && resolvedParams.Count >= 1)
                    {
                        var pattern = match.Groups[1].Value;
                        return $"matches({resolvedParams[0]}, '{pattern}') /* Extracted from Regex.IsMatch */";
                    }
                }
                
                // Check for Regex.Replace pattern
                if (code.Contains("Regex.Replace"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(code, @"Regex\.Replace\([^,]+,\s*""([^""]+)"",\s*""([^""]*)""\)");
                    if (match.Success && resolvedParams.Count >= 1)
                    {
                        var pattern = match.Groups[1].Value;
                        var replacement = match.Groups[2].Value;
                        return $"replace({resolvedParams[0]}, '{pattern}', '{replacement}') /* Extracted from Regex.Replace */";
                    }
                }
                
                // Check for String.Replace pattern
                if (code.Contains(".Replace("))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(code, @"\.Replace\(""([^""]+)"",\s*""([^""]*)""\)");
                    if (match.Success && resolvedParams.Count >= 1)
                    {
                        var oldValue = match.Groups[1].Value;
                        var newValue = match.Groups[2].Value;
                        return $"replace({resolvedParams[0]}, '{oldValue}', '{newValue}') /* Extracted from String.Replace */";
                    }
                }
                
                // Check for StartsWith/EndsWith patterns
                if (code.Contains(".StartsWith("))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(code, @"\.StartsWith\(""([^""]+)""\)");
                    if (match.Success && resolvedParams.Count >= 1)
                    {
                        var prefix = match.Groups[1].Value;
                        return $"starts-with({resolvedParams[0]}, '{prefix}') /* Extracted from StartsWith */";
                    }
                }
                
                if (code.Contains(".EndsWith("))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(code, @"\.EndsWith\(""([^""]+)""\)");
                    if (match.Success && resolvedParams.Count >= 1)
                    {
                        var suffix = match.Groups[1].Value;
                        return $"ends-with({resolvedParams[0]}, '{suffix}') /* Extracted from EndsWith */";
                    }
                }
                
                // For inline C# code that couldn't be extracted, preserve it as a comment and provide a template
                var sanitizedCode = code
                    .Replace("\r\n", " ")
                    .Replace("\n", " ")
                    .Replace("\"", "'");
                
                return $"custom-function({string.Join(", ", resolvedParams)}) /* Original code: {sanitizedCode} */";
            }
            else if (!string.IsNullOrEmpty(functoid.ScripterFunction))
            {
                // External assembly reference
                return $"{functoid.ScripterClass}.{functoid.ScripterFunction}({string.Join(", ", resolvedParams)})";
            }

            return $"/* Scripting functoid - manual translation required */";
        }

        /// <summary>
        /// Resolves a functoid parameter to its actual value or expression.
        /// </summary>
        /// <param name="param">The parameter to resolve.</param>
        /// <returns>The resolved parameter as a quoted constant or XPath expression.</returns>
        private string ResolveParameter(BtmParameter param)
        {
            Console.WriteLine($"DEBUG ResolveParameter: Type={param.Type}, Value={param.Value}");
            
            if (param.Type == "constant")
            {
                // Return constant value as quoted string
                return $"\"{param.Value}\"";
            }
            else if (param.Type == "link")
            {
                // The param.Value is a Link ID - we need to find that link and get its LinkFrom
                Console.WriteLine($"  Looking for link with ID: {param.Value}");
                Console.WriteLine($"  Total links in map: {_mapData.Links.Count}");
                
                // Trim and compare to handle any whitespace issues
                var linkId = param.Value?.Trim();
                var link = _mapData.Links.FirstOrDefault(l => l.LinkId?.Trim() == linkId);
                
                if (link != null)
                {
                    Console.WriteLine($"  Found link! LinkFrom={link.LinkFrom}, LinkTo={link.LinkTo}");
                    // Now resolve the link's source (LinkFrom)
                    var resolved = ResolveSourceExpression(link.LinkFrom);
                    
                    // CRITICAL FIX: Clean up XPaths, but NOT function calls
                    // Check if this looks like a BTM XPath pattern that needs cleanup
                    if (resolved.Contains("local-name()") || resolved.Contains("<Schema>"))
                    {
                        // This is a BTM-format XPath that needs cleanup
                        resolved = CleanupSourceXPath(resolved);
                        Console.WriteLine($"  Cleaned up BTM XPath to: {resolved}");
                    }
                    else if (!resolved.Contains("(") || 
                             resolved.StartsWith("/ns") || 
                             resolved.StartsWith("ns"))
                    {
                        // This looks like an XPath (starts with /ns or ns, or has no parentheses)
                        // Apply namespace prefix if needed
                        resolved = CleanupSourceXPath(resolved);
                        Console.WriteLine($"  Cleaned up XPath to: {resolved}");
                    }
                    else
                    {
                        // This is a function call (contains parentheses, doesn't look like XPath)
                        // Don't modify it
                        Console.WriteLine($"  Keeping function as-is: {resolved}");
                    }
                    
                    return resolved;
                }
                else
                {
                    Console.WriteLine($"  Link NOT FOUND with ID: {param.Value}");
                    Console.WriteLine($"  Available link IDs: {string.Join(", ", _mapData.Links.Select(l => $"'{l.LinkId}'"))}");
                }
                
                // Fallback: try to resolve the param value directly and clean it up
                var fallbackResolved = ResolveSourceExpression(param.Value);
                return CleanupSourceXPath(fallbackResolved);
            }

            // For unknown parameter types, cleanup and return (as source XPath)
            return CleanupSourceXPath(param.Value);
        }

        private bool IsTargetSchemaNode(string nodeId)
        {
            return _mapData.TargetTree?.NodeLookup.ContainsKey(nodeId) ?? false;
        }

        private BtmSchemaNode GetSchemaNode(string nodeId, BtmSchemaTree tree)
        {
            if (tree == null || string.IsNullOrEmpty(nodeId))
                return null;

            tree.NodeLookup.TryGetValue(nodeId, out var node);
            return node;
        }

        private string BuildTargetPath(BtmSchemaNode node)
        {
            // Use the XPath from the schema node (which is already normalized)
            if (!string.IsNullOrEmpty(node.XPath))
            {
                return node.XPath.TrimStart('/');
            }

            // Fallback: Build namespaced path
            var parts = new List<string>();
            var current = node;
            
            while (current != null)
            {
                if (!string.IsNullOrEmpty(current.Name))
                {
                    // Add namespace prefix if available
                    var ns = GetNamespacePrefix(current.Name, _mapData.TargetNamespaces);
                    parts.Insert(0, string.IsNullOrEmpty(ns) ? current.Name : $"{ns}:{current.Name}");
                }
                current = current.Parent;
            }

            return string.Join("/", parts);
        }

        private string BuildSourceXPath(BtmSchemaNode node)
        {
            // Use the XPath from the schema node (which is already normalized)
            if (!string.IsNullOrEmpty(node.XPath))
            {
                return node.XPath;
            }

            // Fallback: Build XPath with namespace prefixes
            var parts = new List<string>();
            var current = node;
            
            while (current != null)
            {
                if (!string.IsNullOrEmpty(current.Name))
                {
                    var ns = GetNamespacePrefix(current.Name, _mapData.SourceNamespaces);
                    parts.Insert(0, string.IsNullOrEmpty(ns) ? current.Name : $"{ns}:{current.Name}");
                }
                current = current.Parent;
            }

            return "/" + string.Join("/", parts);
        }

        private string GetNamespacePrefix(string nodeName, Dictionary<string, string> namespaces)
        {
            // Try to find the appropriate namespace prefix
            // This is a simplified implementation - may need enhancement
            if (namespaces.Count > 0)
            {
                var firstNs = namespaces.FirstOrDefault(kvp => kvp.Key != "xs" && kvp.Key != "msdata");
                return firstNs.Key;
            }
            return null;
        }

        private void ProcessRepeatingStructures(TranslatedMapData translatedMap)
        {
            // Group mappings by their parent repeating structures
            // This would identify loops and nest them properly
            // Simplified implementation - in real scenario, would need more sophisticated analysis
            
            var repeatingNodes = _mapData.TargetTree?.NodeLookup.Values
                .Where(n => n.IsRepeating)
                .ToList();

            if (repeatingNodes == null || !repeatingNodes.Any())
                return;

            // For each repeating node, find source repeating node and create $for loop
            foreach (var repeatNode in repeatingNodes)
            {
                // Find links that target children of this repeating node
                var childMappings = translatedMap.Mappings
                    .Where(m => m.TargetPath.StartsWith(BuildTargetPath(repeatNode)))
                    .ToList();

                if (childMappings.Any())
                {
                    // Find the source repeating structure
                    var sourceRepeatingNode = FindSourceRepeatingNode(repeatNode);
                    if (sourceRepeatingNode != null)
                    {
                        // Create a loop mapping
                        var loopMapping = new LmlMapping
                        {
                            IsLoop = true,
                            LoopExpression = BuildSourceXPath(sourceRepeatingNode),
                            Children = childMappings
                        };

                        // Remove child mappings from root and add to loop
                        foreach (var child in childMappings)
                        {
                            translatedMap.Mappings.Remove(child);
                        }

                        translatedMap.Mappings.Add(loopMapping);
                    }
                }
            }
        }

        private BtmSchemaNode FindSourceRepeatingNode(BtmSchemaNode targetNode)
        {
            // Find links that connect to this target node or its children
            var relevantLinks = _mapData.Links
                .Where(l => IsDescendantOf(l.LinkTo, targetNode))
                .ToList();

            foreach (var link in relevantLinks)
            {
                var sourceNode = GetSchemaNode(link.LinkFrom, _mapData.SourceTree);
                if (sourceNode != null)
                {
                    // Walk up to find repeating parent
                    var current = sourceNode;
                    while (current != null)
                    {
                        if (current.IsRepeating)
                            return current;
                        current = current.Parent;
                    }
                }
            }

            return null;
        }

        private bool IsDescendantOf(string nodeId, BtmSchemaNode ancestor)
        {
            var node = GetSchemaNode(nodeId, _mapData.TargetTree);
            if (node == null) return false;

            var current = node;
            while (current != null)
            {
                if (current.NodeId == ancestor.NodeId)
                    return true;
                current = current.Parent;
            }

            return false;
        }
        
        /// <summary>
        /// Creates a loop mapping structure for BizTalk mass copy operations.
        /// </summary>
        /// <param name="targetCollectionPath">The target XPath for the collection.</param>
        /// <param name="sourceCollectionPath">The source XPath for the collection.</param>
        /// <param name="translatedMap">The translated map data for namespace and schema access.</param>
        /// <returns>An <see cref="LmlMapping"/> with loop structure and child field mappings.</returns>
        /// <remarks>
        /// Attempts to parse XSD schemas to generate explicit child field mappings.
        /// Falls back to direct mapping if schema parsing fails.
        /// </remarks>
        private LmlMapping CreateMassCopyLoopMapping(string targetCollectionPath, string sourceCollectionPath, TranslatedMapData translatedMap)
        {
            Console.WriteLine($"DEBUG: Creating mass copy loop for {targetCollectionPath} from {sourceCollectionPath}");
            
            var sourcePathParts = sourceCollectionPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var loopSegmentIndex = -1;
            
            for (int i = sourcePathParts.Length - 1; i >= 0; i--)
            {
                // Remove namespace prefix for checking
                var segmentName = sourcePathParts[i].Contains(":") 
                    ? sourcePathParts[i].Substring(sourcePathParts[i].IndexOf(':') + 1)
                    : sourcePathParts[i];
                    
                // Check if this segment contains "Loop" (EDI convention for repeating structures)
                if (segmentName.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    loopSegmentIndex = i;
                    break;
                }
            }
            
            if (loopSegmentIndex >= 0)
            {
                
                var loopPathBuilder = new List<string>();
                for (int i = 0; i <= loopSegmentIndex; i++)
                {
                    var segment = sourcePathParts[i];
                    // If segment doesn't already have namespace prefix, add it
                    if (!segment.Contains(":"))
                    {
                        var ediNsPrefix = _mapData.SourceNamespaces
                            .Where(kvp => kvp.Value.Contains("/EDI/") || kvp.Value.Contains("/X12/") || kvp.Value.Contains("/EDIFACT/"))
                            .Select(kvp => kvp.Key)
                            .FirstOrDefault();
                        if (!string.IsNullOrEmpty(ediNsPrefix))
                        {
                            segment = $"{ediNsPrefix}:{segment}";
                        }
                    }
                    loopPathBuilder.Add(segment);
                }
                var loopPath = "/" + string.Join("/", loopPathBuilder);
                
                Console.WriteLine($"  Found Loop segment at index {loopSegmentIndex}: {sourcePathParts[loopSegmentIndex]}");
                Console.WriteLine($"  Creating $for loop at: {loopPath}");
                
                // Extract the target element name (e.g., "LineItem" from "/Basket/OrderForms/OrderForm/LineItems/LineItem")
                var targetPathParts = targetCollectionPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var targetElementName = targetPathParts.Last();
                var targetParentPath = "/" + string.Join("/", targetPathParts.Take(targetPathParts.Length - 1));
                
                // Create the parent collection wrapper (e.g., "LineItems")
                var collectionMapping = new LmlMapping
                {
                    TargetPath = targetParentPath,
                    IsLoop = false,
                    SourceExpression = "",
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>()
                };
                
                // Create the $for loop
                var forLoopMapping = new LmlMapping
                {
                    TargetPath = "$for",
                    IsLoop = true,
                    LoopExpression = loopPath,
                    SourceExpression = "",
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>(),
                    LoopTargetParentPath = targetCollectionPath, // Track the full target path for child matching
                    LoopTargetElementName = targetElementName
                };
                
                // Create the item element (e.g., "LineItem")
                var itemMapping = new LmlMapping
                {
                    TargetPath = targetElementName,
                    SourceExpression = "",
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>()
                };
                
                // The child mappings will be added later by the BuildHierarchyFromFlatMappings logic
                // For now, just create the structure
                
                forLoopMapping.Children.Add(itemMapping);
                collectionMapping.Children.Add(forLoopMapping);
                
                Console.WriteLine($"  Created loop structure: {targetParentPath} -> $for({loopPath}) -> {targetElementName}");
                
                return collectionMapping;
            }
            
            // Fallback to original logic if no Loop segment found
            var sourceSchemaPath = translatedMap.SourceSchemaFilePath;
            var targetSchemaPath = translatedMap.TargetSchemaFilePath;
            
            if (string.IsNullOrEmpty(sourceSchemaPath) || string.IsNullOrEmpty(targetSchemaPath))
            {
                Console.WriteLine($"  WARNING: Schema file paths not available, falling back to direct mapping");
                return new LmlMapping
                {
                    TargetPath = targetCollectionPath,
                    IsLoop = false,
                    SourceExpression = sourceCollectionPath,
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>()
                };
            }
            
            // Parse XSD files to find child elements
            var sourceChildren = ParseXsdForChildren(sourceSchemaPath, sourceCollectionPath);
            var targetChildren = ParseXsdForChildren(targetSchemaPath, targetCollectionPath);
            
            Console.WriteLine($"  Found {sourceChildren.Count} source children and {targetChildren.Count} target children");
            
            if (sourceChildren.Count == 0 || targetChildren.Count == 0)
            {
                Console.WriteLine($"  WARNING: No children found in schemas, falling back to direct mapping");
                return new LmlMapping
                {
                    TargetPath = targetCollectionPath,
                    IsLoop = false,
                    SourceExpression = sourceCollectionPath,
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>()
                };
            }
            
            // Find the child collection element name (e.g., "OrderForm" from "OrderForms")
            var sourceChildElement = sourceChildren.FirstOrDefault(c => c.IsRepeating);
            var targetChildElement = targetChildren.FirstOrDefault(c => c.IsRepeating);
            
            if (sourceChildElement == null || targetChildElement == null)
            {
                Console.WriteLine($"  WARNING: No repeating child element found, falling back to direct mapping");
                return new LmlMapping
                {
                    TargetPath = targetCollectionPath,
                    IsLoop = false,
                    SourceExpression = sourceCollectionPath,
                    Children = new List<LmlMapping>(),
                    Attributes = new Dictionary<string, string>()
                };
            }
            
            Console.WriteLine($"  Source child: {sourceChildElement.Name}, Target child: {targetChildElement.Name}");
            
            // Create the collection wrapper (OrderForms)
            var collectionMapping2 = new LmlMapping
            {
                TargetPath = targetCollectionPath,
                IsLoop = false,
                SourceExpression = "",
                Children = new List<LmlMapping>(),
                Attributes = new Dictionary<string, string>()
            };
            
            // Create the $for loop inside the collection
            var loopPath2 = $"{sourceCollectionPath}/{sourceChildElement.Name}";
            var forLoopMapping2 = new LmlMapping
            {
                TargetPath = "$for",  // Special marker for $for loop
                IsLoop = true,
                LoopExpression = loopPath2,
                SourceExpression = "",
                Children = new List<LmlMapping>(),
                Attributes = new Dictionary<string, string>()
            };
            
            // Inside the $for, create the OrderForm element
            var childItemMapping = new LmlMapping
            {
                TargetPath = targetChildElement.Name,
                SourceExpression = "",
                Children = new List<LmlMapping>(),
                Attributes = new Dictionary<string, string>()
            };
            
            // Map attributes from the XSD (these are the actual data fields on OrderForm)
            // Look for attributes in the XSD schema
            var sourceAttributes = ParseXsdAttributes(sourceSchemaPath, $"{sourceCollectionPath}/{sourceChildElement.Name}");
            var targetAttributes = ParseXsdAttributes(targetSchemaPath, $"{targetCollectionPath}/{targetChildElement.Name}");
            
            Console.WriteLine($"  Found {sourceAttributes.Count} source attributes and {targetAttributes.Count} target attributes");
            
            foreach (var targetAttr in targetAttributes)
            {
                var sourceAttr = sourceAttributes.FirstOrDefault(a => 
                    a.Equals(targetAttr, StringComparison.OrdinalIgnoreCase));
                
                if (!string.IsNullOrEmpty(sourceAttr))
                {
                    // CRITICAL FIX: In a $for loop context, attribute XPaths should NOT be quoted
                    // Azure Data Mapper expects: @AttributeName (not '@AttributeName')
                    childItemMapping.Attributes[$"@{targetAttr}"] = $"@{sourceAttr}";
                    Console.WriteLine($"    Mapped attribute: @{targetAttr} = @{sourceAttr}");
                }
            }
            
            forLoopMapping2.Children.Add(childItemMapping);
            collectionMapping2.Children.Add(forLoopMapping2);
            
            return collectionMapping2;
        }
        
        private List<string> ParseXsdAttributes(string xsdFilePath, string elementPath)
        {
            var attributes = new List<string>();
            
            if (!File.Exists(xsdFilePath))
            {
                return attributes;
            }
            
            try
            {
                var xsdDoc = new XmlDocument();
                xsdDoc.Load(xsdFilePath);
                
                var nsMgr = new XmlNamespaceManager(xsdDoc.NameTable);
                nsMgr.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
                
                // Extract element name from path and REMOVE namespace prefix
                var pathWithoutLeadingSlash = elementPath.TrimStart('/');
                var elementNameWithNs = pathWithoutLeadingSlash.Split('/').Last();
                
                // Remove namespace prefix (everything before and including the colon)
                var elementName = elementNameWithNs.Contains(":") 
                    ? elementNameWithNs.Substring(elementNameWithNs.IndexOf(':') + 1) 
                    : elementNameWithNs;
                
                // Find the element definition
                var elementNode = xsdDoc.SelectSingleNode($"//xs:element[@name='{elementName}']", nsMgr);
                
                if (elementNode != null)
                {
                    // Navigate to the complex type
                    var complexTypeNode = elementNode.SelectSingleNode(".//xs:complexType", nsMgr);
                    if (complexTypeNode == null)
                    {
                        var typeName = elementNode.Attributes["type"]?.Value;
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            var localTypeName = typeName.Contains(":") ? typeName.Split(':')[1] : typeName;
                            complexTypeNode = xsdDoc.SelectSingleNode($"//xs:complexType[@name='{localTypeName}']", nsMgr);
                        }
                    }
                    
                    if (complexTypeNode != null)
                    {
                        // Find all attribute definitions
                        var attrNodes = complexTypeNode.SelectNodes(".//xs:attribute", nsMgr);
                        foreach (XmlNode attrNode in attrNodes)
                        {
                            var attrName = attrNode.Attributes["name"]?.Value;
                            if (!string.IsNullOrEmpty(attrName))
                            {
                                attributes.Add(attrName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR parsing XSD attributes: {ex.Message}");
            }
            
            return attributes;
        }
        
        /// <summary>
        /// Parses an XSD schema file to extract child elements of a given parent element.
        /// </summary>
        /// <param name="xsdFilePath">Path to the XSD schema file.</param>
        /// <param name="parentPath">The XPath of the parent element to analyze.</param>
        /// <returns>A list of <see cref="XsdElement"/> objects representing the child elements.</returns>
        private List<XsdElement> ParseXsdForChildren(string xsdFilePath, string parentPath)
        {
            var children = new List<XsdElement>();
            
            if (!File.Exists(xsdFilePath))
            {
                Console.WriteLine($"  WARNING: XSD file not found: {xsdFilePath}");
                return children;
            }
            
            try
            {
                var xsdDoc = new XmlDocument();
                xsdDoc.Load(xsdFilePath);
                
                var nsMgr = new XmlNamespaceManager(xsdDoc.NameTable);
                nsMgr.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
                
                // Extract element name from path and REMOVE namespace prefix
                // e.g., "/ns0:X12_00401_850/ns0:PO1Loop1" -> "PO1Loop1"
                var pathWithoutLeadingSlash = parentPath.TrimStart('/');
                var elementNameWithNs = pathWithoutLeadingSlash.Split('/').Last();
                
                // Remove namespace prefix (everything before and including the colon)
                var elementName = elementNameWithNs.Contains(":") 
                    ? elementNameWithNs.Substring(elementNameWithNs.IndexOf(':') + 1) 
                    : elementNameWithNs;
                
                Console.WriteLine($"  Searching XSD for element: '{elementName}' (from path: '{parentPath}')");
                
                // Find the element definition in the XSD
                var elementNode = xsdDoc.SelectSingleNode($"//xs:element[@name='{elementName}']", nsMgr);
                
                if (elementNode != null)
                {
                    // Navigate to the complex type
                    var complexTypeNode = elementNode.SelectSingleNode(".//xs:complexType", nsMgr);
                    if (complexTypeNode == null)
                    {
                        var typeName = elementNode.Attributes["type"]?.Value;
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            // Remove namespace prefix from type name
                            var localTypeName = typeName.Contains(":") ? typeName.Split(':')[1] : typeName;
                            complexTypeNode = xsdDoc.SelectSingleNode($"//xs:complexType[@name='{localTypeName}']", nsMgr);
                        }
                    }
                    
                    if (complexTypeNode != null)
                    {
                        // Find all DIRECT child elements (not descendants)
                        var sequence = complexTypeNode.SelectSingleNode("xs:sequence", nsMgr);
                        var choice = complexTypeNode.SelectSingleNode("xs:choice", nsMgr);
                        var all = complexTypeNode.SelectSingleNode("xs:all", nsMgr);
                        
                        var containerNode = sequence ?? choice ?? all;
                        if (containerNode != null)
                        {
                            var childElements = containerNode.SelectNodes("xs:element", nsMgr);
                            foreach (XmlNode childNode in childElements)
                            {
                                var childName = childNode.Attributes["name"]?.Value;
                                var maxOccurs = childNode.Attributes["maxOccurs"]?.Value;
                                
                                if (!string.IsNullOrEmpty(childName))
                                {
                                    var child = new XsdElement
                                    {
                                        Name = childName,
                                        IsRepeating = maxOccurs == "unbounded" || (int.TryParse(maxOccurs, out var max) && max > 1)
                                    };
                                    
                                    // Recursively get grandchildren (to detect nested collections)
                                    var grandchildComplexType = childNode.SelectSingleNode(".//xs:complexType", nsMgr);
                                    if (grandchildComplexType == null)
                                    {
                                        var childTypeName = childNode.Attributes["type"]?.Value;
                                        if (!string.IsNullOrEmpty(childTypeName))
                                        {
                                            // Remove namespace prefix if present
                                            var localTypeName = childTypeName.Contains(":") ? childTypeName.Split(':')[1] : childTypeName;
                                            grandchildComplexType = xsdDoc.SelectSingleNode($"//xs:complexType[@name='{localTypeName}']", nsMgr);
                                        }
                                    }
                                    
                                    if (grandchildComplexType != null)
                                    {
                                        var gcSequence = grandchildComplexType.SelectSingleNode("xs:sequence", nsMgr);
                                        var gcChoice = grandchildComplexType.SelectSingleNode("xs:choice", nsMgr);
                                        var gcAll = grandchildComplexType.SelectSingleNode("xs:all", nsMgr);
                                        
                                        var gcContainer = gcSequence ?? gcChoice ?? gcAll;
                                        if (gcContainer != null)
                                        {
                                            var grandchildren = gcContainer.SelectNodes("xs:element", nsMgr);
                                            foreach (XmlNode grandchildNode in grandchildren)
                                            {
                                                var grandchildName = grandchildNode.Attributes["name"]?.Value;
                                                var grandchildMaxOccurs = grandchildNode.Attributes["maxOccurs"]?.Value;
                                                
                                                if (!string.IsNullOrEmpty(grandchildName))
                                                {
                                                    var grandchildElement = new XsdElement 
                                                    { 
                                                        Name = grandchildName,
                                                        IsRepeating = grandchildMaxOccurs == "unbounded" || (int.TryParse(grandchildMaxOccurs, out var maxGc) && maxGc > 1)
                                                    };
                                                    child.Children.Add(grandchildElement);
                                                }
                                            }
                                        }
                                    }
                                    
                                    children.Add(child);
                                    Console.WriteLine($"    Found child: {childName} (IsRepeating: {child.IsRepeating})");
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  WARNING: No complexType found for element '{elementName}'");
                    }
                }
                else
                {
                    Console.WriteLine($"  WARNING: Element '{elementName}' not found in XSD");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR parsing XSD: {ex.Message}");
            }
            
            return children;
        }
        
        /// <summary>
        /// Post-processes the hierarchy to move child mappings into $for loops and make XPaths relative
        /// </summary>
        private void ProcessLoopChildMappings(List<LmlMapping> hierarchy)
        {
            // Find all $for loop mappings
            var forLoops = new List<LmlMapping>();
            FindAllForLoops(hierarchy, forLoops);
            
            Console.WriteLine($"DEBUG: Found {forLoops.Count} $for loops to process");
            
            foreach (var forLoop in forLoops)
            {
                if (string.IsNullOrEmpty(forLoop.LoopTargetParentPath) || string.IsNullOrEmpty(forLoop.LoopExpression))
                {
                    Console.WriteLine($"  Skipping $for loop with missing LoopTargetParentPath or LoopExpression");
                    continue;
                }
                    
                Console.WriteLine($"  Processing $for loop: {forLoop.LoopExpression} targeting {forLoop.LoopTargetParentPath}");
                
                // Find the item element inside the $for loop
                var itemElement = forLoop.Children.FirstOrDefault(c => c.TargetPath == forLoop.LoopTargetElementName);
                if (itemElement == null)
                {
                    Console.WriteLine($"    WARNING: No item element found in $for loop (expected '{forLoop.LoopTargetElementName}')");
                    continue;
                }
                
                // Make XPaths in the item element's attributes relative to the loop
                // These attributes were added during BuildHierarchyFromFlatMappings but have absolute XPaths
                if (itemElement.Attributes != null && itemElement.Attributes.Count > 0)
                {
                    Console.WriteLine($"    Making {itemElement.Attributes.Count} attributes relative to loop");
                    var relativeAttributes = new Dictionary<string, string>();
                    foreach (var attr in itemElement.Attributes)
                    {
                        var originalValue = attr.Value;
                        var relativeValue = MakeXPathRelativeInExpression(originalValue, forLoop.LoopExpression);
                        relativeAttributes[attr.Key] = relativeValue;
                        Console.WriteLine($"      Attribute {attr.Key}: '{originalValue}' -> '{relativeValue}'");
                    }
                    itemElement.Attributes = relativeAttributes;
                }
                
                // Also process child elements' source expressions
                if (itemElement.Children != null && itemElement.Children.Count > 0)
                {
                    Console.WriteLine($"    Making {itemElement.Children.Count} child element XPaths relative to loop");
                    MakeChildPathsRelative(itemElement.Children, forLoop.LoopExpression);
                }
                
                // Find all child mappings that belong to this loop from the outer hierarchy
                // They will have TargetPaths like: /Basket/OrderForms/OrderForm/LineItems/LineItem/@ProductID
                var childMappings = new List<LmlMapping>();
                FindChildMappingsForLoop(hierarchy, forLoop.LoopTargetParentPath, childMappings);
                
                Console.WriteLine($"    Found {childMappings.Count} potential child mappings for this loop from outer hierarchy");
                
                // Move matching children into the item element and make XPaths relative
                foreach (var childMapping in childMappings)
                {
                    // Make the source XPath relative to the loop
                    if (!string.IsNullOrEmpty(childMapping.SourceExpression))
                    {
                        var originalExpr = childMapping.SourceExpression;
                        childMapping.SourceExpression = MakeXPathRelativeInExpression(childMapping.SourceExpression, forLoop.LoopExpression);
                        Console.WriteLine($"      Child expression: '{originalExpr}' -> '{childMapping.SourceExpression}'");
                    }
                    
                    // Make attribute XPaths relative too
                    if (childMapping.Attributes != null && childMapping.Attributes.Count > 0)
                    {
                        var relativeAttributes = new Dictionary<string, string>();
                        foreach (var attr in childMapping.Attributes)
                        {
                            var attrValue = MakeXPathRelativeInExpression(attr.Value, forLoop.LoopExpression);
                            Console.WriteLine($"      Made attribute XPath relative: {attr.Key} = {attrValue}");
                            relativeAttributes[attr.Key] = attrValue;
                        }
                        childMapping.Attributes = relativeAttributes;
                    }
                    
                    // Add to item element (attributes go to Attributes dict, children to Children list)
                    if (childMapping.Attributes != null && childMapping.Attributes.Count > 0)
                    {
                        foreach (var attr in childMapping.Attributes)
                        {
                            itemElement.Attributes[attr.Key] = attr.Value;
                        }
                    }
                    if (!string.IsNullOrEmpty(childMapping.SourceExpression) && !childMapping.IsAttribute)
                    {
                        itemElement.Children.Add(childMapping);
                    }
                }
            }
            
            // Remove duplicate child mappings that are now inside the loops
            RemoveDuplicateLoopMappings(hierarchy);
        }
        
        /// <summary>
        /// Recursively makes all child paths relative to the loop expression
        /// </summary>
        private void MakeChildPathsRelative(List<LmlMapping> children, string loopExpression)
        {
            foreach (var child in children)
            {
                // Make source expression relative
                if (!string.IsNullOrEmpty(child.SourceExpression))
                {
                    var originalExpr = child.SourceExpression;
                    child.SourceExpression = MakeXPathRelativeInExpression(originalExpr, loopExpression);
                    if (originalExpr != child.SourceExpression)
                    {
                        Console.WriteLine($"        Child '{child.TargetPath}': '{originalExpr}' -> '{child.SourceExpression}'");
                    }
                }
                
                // Make attributes relative
                if (child.Attributes != null && child.Attributes.Count > 0)
                {
                    var relativeAttributes = new Dictionary<string, string>();
                    foreach (var attr in child.Attributes)
                    {
                        relativeAttributes[attr.Key] = MakeXPathRelativeInExpression(attr.Value, loopExpression);
                    }
                    child.Attributes = relativeAttributes;
                }
                
                // Recursively process grandchildren
                if (child.Children != null && child.Children.Count > 0)
                {
                    MakeChildPathsRelative(child.Children, loopExpression);
                }
            }
        }
        
        private void FindAllForLoops(List<LmlMapping> mappings, List<LmlMapping> result)
        {
            foreach (var mapping in mappings)
            {
                if (mapping.IsLoop && mapping.TargetPath == "$for")
                {
                    result.Add(mapping);
                }
                
                if (mapping.Children != null && mapping.Children.Count > 0)
                {
                    FindAllForLoops(mapping.Children, result);
                }
            }
        }
        
        private void FindChildMappingsForLoop(List<LmlMapping> mappings, string loopTargetPath, List<LmlMapping> result)
        {
            // Normalize the loop target path (remove leading slash for comparison)
            var normalizedLoopPath = loopTargetPath.TrimStart('/');
            
            foreach (var mapping in mappings)
            {
                // Normalize the mapping target path (remove leading slash for comparison)
                var normalizedMappingPath = mapping.TargetPath?.TrimStart('/');

                if (!string.IsNullOrEmpty(normalizedMappingPath) && 
                    !mapping.IsLoop &&
                    mapping.TargetPath != "$for")
                {
                    // Exact match or child of the loop target (including attributes with /@)
                    if (normalizedMappingPath == normalizedLoopPath || 
                        normalizedMappingPath.StartsWith(normalizedLoopPath + "/") ||
                        normalizedMappingPath.StartsWith(normalizedLoopPath + "/@"))
                    {
                        result.Add(mapping);
                        Console.WriteLine($"      Found potential child: {mapping.TargetPath}");
                    }
                }
                
                // Recursively search children (but don't go into other $for loops)
                if (mapping.Children != null && mapping.Children.Count > 0 && mapping.TargetPath != "$for")
                {
                    FindChildMappingsForLoop(mapping.Children, loopTargetPath, result);
                }
            }
        }
        
        private void RemoveDuplicateLoopMappings(List<LmlMapping> hierarchy)
        {
            Console.WriteLine($"DEBUG: RemoveDuplicateLoopMappings called on hierarchy with {hierarchy.Count} nodes");
            
            // Remove mappings that have already been moved into $for loops
            for (int i = hierarchy.Count - 1; i >= 0; i--)
            {
                var mapping = hierarchy[i];
                
                Console.WriteLine($"  Processing node: {mapping.TargetPath}, Children: {mapping.Children?.Count ?? 0}");
                
                // Recursively clean children first (bottom-up approach)
                if (mapping.Children != null && mapping.Children.Count > 0)
                {
                    RemoveDuplicateLoopMappings(mapping.Children);
                }
                
                // Check for duplicate SIBLING elements with the same name
                // This handles cases where the same element (e.g., "LineItems") appears twice at the same level
                if (mapping.Children != null && mapping.Children.Count > 1)
                {
                    Console.WriteLine($"  Checking for duplicates in {mapping.Children.Count} children of '{mapping.TargetPath}'");
                    
                    // CRITICAL: Extract just the last segment of each child's TargetPath for comparison
                    // This handles cases where some children have full paths and others have just the element name
                    var duplicateGroups = mapping.Children
                        .GroupBy(c => {
                            var lastSegment = c.TargetPath?.Split('/').Last() ?? c.TargetPath;
                            Console.WriteLine($"    Child path '{c.TargetPath}' -> segment '{lastSegment}'");
                            return lastSegment;
                        })
                        .Where(g => g.Count() > 1)
                        .ToList();
                    
                    foreach (var duplicateGroup in duplicateGroups)
                    {
                        Console.WriteLine($"  DUPLICATE SIBLINGS FOUND: {duplicateGroup.Count()}x '{duplicateGroup.Key}' under '{mapping.TargetPath}'");
                        
                        // Keep the one WITH a $for loop child (preferred)
                        var withForLoop = duplicateGroup.FirstOrDefault(c => 
                            c.Children != null && c.Children.Any(ch => ch.TargetPath == "$for" || ch.IsLoop));
                        
                        if (withForLoop != null)
                        {
                            Console.WriteLine($"    Keeping version with $for loop");
                            
                            // Remove all others
                            foreach (var duplicate in duplicateGroup)
                            {
                                if (duplicate != withForLoop)
                                {
                                    mapping.Children.Remove(duplicate);
                                    Console.WriteLine($"    Removed duplicate '{duplicate.TargetPath}' without $for loop");
                                }
                            }
                        }
                        else
                        {
                            // No $for loop in any - keep the first one and warn
                            Console.WriteLine($"    WARNING: No version with $for loop found - keeping first instance");
                            var toKeep = duplicateGroup.First();
                            foreach (var duplicate in duplicateGroup.Skip(1))
                            {
                                mapping.Children.Remove(duplicate);
                                Console.WriteLine($"    Removed duplicate '{duplicate.TargetPath}'");
                            }
                        }
                    }
                }
                
                // If this mapping has children that include $for loops, handle duplicates
                if (mapping.Children != null && mapping.Children.Any(c => c.TargetPath == "$for" || c.IsLoop))
                {
                    // Find all $for loop children (including those marked IsLoop)
                    var forLoops = mapping.Children.Where(c => c.TargetPath == "$for" || c.IsLoop).ToList();
                    
                    // Get the target element names that are inside $for loops
                    // Also track the parent path to avoid removing sibling elements with same name
                    var targetsCoveredByForLoops = new HashSet<string>();
                    foreach (var forLoop in forLoops)
                    {
                        foreach (var child in forLoop.Children)
                        {
                            // Use full path to avoid removing wrong elements
                            var fullPath = mapping.TargetPath + "/" + child.TargetPath;
                            targetsCoveredByForLoops.Add(child.TargetPath);
                            Console.WriteLine($"      Marking as covered by $for: {child.TargetPath}");
                        }
                    }
                    
                    // Remove any direct children that are covered by $for loops
                    var keptChildren = new List<LmlMapping>();
                    
                    foreach (var child in mapping.Children)
                    {
                        if (child.TargetPath == "$for" || child.IsLoop)
                        {
                            // Always keep $for loops
                            keptChildren.Add(child);
                        }
                        else if (targetsCoveredByForLoops.Contains(child.TargetPath))
                        {
                            // CRITICAL FIX: Always remove children that are covered by $for loops
                            // The $for loop version is the correct one - any duplicate outside the loop should be removed
                            // This handles cases where mass copy creates both a direct mapping AND a $for loop
                            Console.WriteLine($"    Removing duplicate mapping: {child.TargetPath} (covered by $for loop)");
                            // Don't add to keptChildren - effectively removing it
                        }
                        else
                        {
                            // Not covered by a $for loop, keep it
                            keptChildren.Add(child);
                        }
                    }
                    
                    mapping.Children = keptChildren;
                    
                    // If we have multiple $for loops targeting the same element, keep only the first one
                    // (This handles the case where we have both PO1Loop1 and PIDLoop1 loops)
                    if (forLoops.Count > 1)
                    {
                        Console.WriteLine($"    Found {forLoops.Count} $for loops, keeping only the first one");
                        var firstForLoop = forLoops.First();
                        
                        // Remove all but the first $for loop
                        keptChildren = keptChildren.Where(c => 
                            (c.TargetPath != "$for" && !c.IsLoop) || c == firstForLoop
                        ).ToList();
                        
                        mapping.Children = keptChildren;
                    }
                }
            }
        }
        
        /// <summary>
        /// Enforces Azure Data Mapper's namespace convention by ensuring the target schema's primary namespace is ns0.
        /// </summary>
        /// <param name="translatedMap">The translated map data to modify.</param>
        /// <param name="originalTargetNamespaces">The original target namespaces before source namespaces were copied.</param>
        /// <remarks>
        /// The target schema's primary business namespace must be ns0 for Azure Data Mapper.
        /// Shared utility namespaces (PropertySchema, XMLSchema) should not occupy ns0.
        /// This method swaps namespace prefixes when the target's unique namespace is not at ns0.
        /// </remarks>
        private void EnforceTargetNamespaceAsNs0(TranslatedMapData translatedMap, Dictionary<string, string> originalTargetNamespaces)
        {
            if (translatedMap.TargetNamespaces == null || translatedMap.TargetNamespaces.Count == 0)
            {
                Console.WriteLine("  EnforceTargetNamespaceAsNs0: No target namespaces to process");
                return;
            }
            
            Console.WriteLine("DEBUG: Enforcing target schema's primary namespace as ns0");
            Console.WriteLine($"  Current target namespaces:");
            foreach (var ns in translatedMap.TargetNamespaces)
            {
                Console.WriteLine($"    {ns.Key} = {ns.Value}");
            }
            
            // STEP 1: Find the target schema's primary business namespace
            // This is the namespace that is NOT PropertySchema, xs, b, or other utility namespaces
            // and is different from the source namespaces
            
            string targetPrimaryNamespace = null;
            string targetPrimaryPrefix = null;
            
            // Look for target schema's unique business namespace
            // Priority: ns1, ns2, etc. that are NOT in source namespaces and NOT PropertySchema
            var candidatePrefixes = translatedMap.TargetNamespaces.Keys
                .Where(k => k.StartsWith("ns") && k != "ns0" && k.Length >= 3 && char.IsDigit(k[2]))
                .OrderBy(k => k)
                .ToList();
            
            foreach (var prefix in candidatePrefixes)
            {
                var namespaceUri = translatedMap.TargetNamespaces[prefix];
                
                // Skip utility namespaces
                if (namespaceUri.Contains("PropertySchema") ||
                    namespaceUri.Contains("XMLSchema") ||
                    namespaceUri.Contains("BizTalk/2003"))
                {
                    Console.WriteLine($"  Skipping utility namespace {prefix}: {namespaceUri}");
                    continue;
                }
                
                // CRITICAL FIX: Check if PropertySchema is already at ns0 in ORIGINAL target
                // If so, DON'T swap - the target schema intentionally imports PropertySchema
                if (originalTargetNamespaces.ContainsKey("ns0"))
                {
                    var originalNs0 = originalTargetNamespaces["ns0"];
                    if (originalNs0.Contains("PropertySchema"))
                    {
                        Console.WriteLine($"  Original target has PropertySchema at ns0 - schema imports PropertySchema");
                        Console.WriteLine($"  This means target namespace should stay in {prefix}, NOT swap to ns0");
                        Console.WriteLine($"  Skipping namespace enforcement for this map");
                        return; // Don't swap!
                    }
                }
                
                // Check if this namespace was in ORIGINAL target schema
                bool wasInOriginalTarget = originalTargetNamespaces.Values.Contains(namespaceUri);
                bool isInSourceValues = translatedMap.SourceNamespaces.Values.Contains(namespaceUri);
                
                Console.WriteLine($"  Checking namespace {prefix}: {namespaceUri}");
                Console.WriteLine($"    Was in original target: {wasInOriginalTarget}");
                Console.WriteLine($"    Is in source values: {isInSourceValues}");
                
                // If the namespace was in BOTH original target AND source, it's shared
                if (wasInOriginalTarget && isInSourceValues)
                {
                    Console.WriteLine($"  Namespace {prefix} is SHARED (in both original target and source) - will NOT swap");
                    continue;
                }
                
                // If namespace is only in source (copied later), it's not unique to target
                if (isInSourceValues && !wasInOriginalTarget)
                {
                    Console.WriteLine($"  Namespace {prefix} was copied from source (not in original target) - will NOT swap");
                    continue;
                }
                
                // This namespace is unique to target
                targetPrimaryNamespace = namespaceUri;
                targetPrimaryPrefix = prefix;
                Console.WriteLine($"  Found target primary namespace: {targetPrimaryPrefix} = {targetPrimaryNamespace} (UNIQUE to target)");
                break;
            }
            
            // STEP 2: If no unique target namespace found, don't change anything
            if (string.IsNullOrEmpty(targetPrimaryNamespace) || string.IsNullOrEmpty(targetPrimaryPrefix))
            {
                Console.WriteLine("  No unique target business namespace found - keeping current namespace assignments");
                return;
            }
            
            // STEP 3: Check if target primary namespace is already ns0
            if (targetPrimaryPrefix == "ns0")
            {
                Console.WriteLine($"  Target primary namespace is already ns0 - no changes needed");
                return;
            }
            
            // STEP 4: Swap namespaces so target primary namespace becomes ns0
            Console.WriteLine($"  Swapping namespace prefixes:");
            Console.WriteLine($"    Before: ns0 = {(translatedMap.TargetNamespaces.ContainsKey("ns0") ? translatedMap.TargetNamespaces["ns0"] : "(not set)")}");
            Console.WriteLine($"    Before: {targetPrimaryPrefix} = {targetPrimaryNamespace}");
            
            var tempNamespaces = new Dictionary<string, string>(translatedMap.TargetNamespaces);
            
            // Get the current ns0 value (if any)
            string currentNs0Value = null;
            if (tempNamespaces.ContainsKey("ns0"))
            {
                currentNs0Value = tempNamespaces["ns0"];
            }
            
            // Remove both keys
            tempNamespaces.Remove("ns0");
            tempNamespaces.Remove(targetPrimaryPrefix);
            
            // Add them back with swapped prefixes
            tempNamespaces["ns0"] = targetPrimaryNamespace; // Target primary → ns0
            
            if (!string.IsNullOrEmpty(currentNs0Value))
            {
                // Move old ns0 to the target primary prefix
                tempNamespaces[targetPrimaryPrefix] = currentNs0Value;
            }
            
            // Replace the target namespaces
            translatedMap.TargetNamespaces = tempNamespaces;
            
            Console.WriteLine($"    After: ns0 = {translatedMap.TargetNamespaces["ns0"]}");
            Console.WriteLine($"    After: {targetPrimaryPrefix} = {(translatedMap.TargetNamespaces.ContainsKey(targetPrimaryPrefix) ? translatedMap.TargetNamespaces[targetPrimaryPrefix] : "(removed)")}");
            
            Console.WriteLine($"  Final target namespaces:");
            foreach (var ns in translatedMap.TargetNamespaces)
            {
                Console.WriteLine($"    {ns.Key} = {ns.Value}");
            }
        }
    }
}
