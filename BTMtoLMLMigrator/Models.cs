// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BizTalktoLogicApps.BTMtoLMLMigrator
{
    /// <summary>
    /// Represents the complete BizTalk Server map structure parsed from a BTM file.
    /// </summary>
    /// <remarks>
    /// Contains all elements required for map translation including schemas, functoids, links, and relationship lookups.
    /// </remarks>
    public class BtmMapData
    {
        public string SourceSchema { get; set; }
        public string TargetSchema { get; set; }
        public Dictionary<string, string> SourceNamespaces { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> TargetNamespaces { get; set; } = new Dictionary<string, string>();
        public List<BtmFunctoid> Functoids { get; set; } = new List<BtmFunctoid>();
        public List<BtmLink> Links { get; set; } = new List<BtmLink>();
        public BtmSchemaTree SourceTree { get; set; }
        public BtmSchemaTree TargetTree { get; set; }
        
        /// <summary>
        /// Gets or sets the functoid lookup dictionary for rapid functoid retrieval by ID.
        /// </summary>
        public Dictionary<string, BtmFunctoid> FunctoidLookup { get; set; } = new Dictionary<string, BtmFunctoid>();
        
        /// <summary>
        /// Gets or sets the link lookup dictionary for rapid link retrieval by ID.
        /// </summary>
        public Dictionary<string, BtmLink> LinkLookup { get; set; } = new Dictionary<string, BtmLink>();
        
        /// <summary>
        /// Gets or sets the number of pages in the map (1-based index).
        /// </summary>
        public int PageCount { get; set; } = 1;
    }

    /// <summary>
    /// Represents a BizTalk Server functoid element from a BTM file.
    /// </summary>
    /// <remarks>
    /// Functoids are transformation functions that process data during mapping operations.
    /// This class captures all functoid properties including type, parameters, and connection information.
    /// </remarks>
    public class BtmFunctoid
    {
        public string FunctoidId { get; set; }
        public string FunctoidType { get; set; }
        public string FunctoidFid { get; set; }
        public string FunctoidClsid { get; set; }
        public int XCell { get; set; }
        public int YCell { get; set; }
        public List<BtmParameter> InputParameters { get; set; } = new List<BtmParameter>();
        public string ScripterCode { get; set; }
        public string ScripterLanguage { get; set; }
        public string ScripterAssembly { get; set; }
        public string ScripterClass { get; set; }
        public string ScripterFunction { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// Gets or sets the collection of input links feeding data into this functoid.
        /// </summary>
        public List<BtmLink> InputLinks { get; set; } = new List<BtmLink>();
        
        /// <summary>
        /// Gets or sets the collection of output links sending data from this functoid.
        /// </summary>
        public List<BtmLink> OutputLinks { get; set; } = new List<BtmLink>();
        
        /// <summary>
        /// Gets or sets the page number where this functoid appears in the visual designer (1-based index).
        /// </summary>
        public int PageNumber { get; set; } = 1;
    }

    /// <summary>
    /// Represents an input parameter for a functoid, which can be either a link reference or a constant value.
    /// </summary>
    public class BtmParameter
    {
        /// <summary>
        /// Gets or sets the parameter type ("link" or "constant").
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// Gets or sets the parameter value (link ID for links, actual value for constants).
        /// </summary>
        public string Value { get; set; }
        
        /// <summary>
        /// Gets or sets the zero-based index indicating the parameter's position in the input order.
        /// </summary>
        public int LinkIndex { get; set; }
        
        /// <summary>
        /// Gets or sets the unique identifier for this parameter connection.
        /// </summary>
        public string Guid { get; set; }
    }

    /// <summary>
    /// Represents a connection link between schema elements or functoids in a BizTalk map.
    /// </summary>
    /// <remarks>
    /// Links define the data flow connections in a map, connecting source schema fields,
    /// functoids, and target schema fields.
    /// </remarks>
    public class BtmLink
    {
        public string LinkId { get; set; }
        public string LinkFrom { get; set; }
        public string LinkTo { get; set; }
        public string Label { get; set; }
        public string SourceCopyDirective { get; set; }
        public string TargetDirective { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the link source is a functoid rather than a schema element.
        /// </summary>
        public bool IsFromFunctoid { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the link target is a functoid rather than a schema element.
        /// </summary>
        public bool IsToFunctoid { get; set; }
        
        /// <summary>
        /// Gets or sets the source functoid object when <see cref="IsFromFunctoid"/> is true.
        /// </summary>
        public BtmFunctoid SourceFunctoid { get; set; }
        
        /// <summary>
        /// Gets or sets the target functoid object when <see cref="IsToFunctoid"/> is true.
        /// </summary>
        public BtmFunctoid TargetFunctoid { get; set; }
    }

    /// <summary>
    /// Represents a hierarchical schema tree structure for either source or target schemas.
    /// </summary>
    public class BtmSchemaTree
    {
        public string SchemaName { get; set; }
        public BtmSchemaNode Root { get; set; }
        public Dictionary<string, BtmSchemaNode> NodeLookup { get; set; } = new Dictionary<string, BtmSchemaNode>();
    }

    /// <summary>
    /// Represents an element or attribute node within a schema tree hierarchy.
    /// </summary>
    public class BtmSchemaNode
    {
        public string NodeId { get; set; }
        public string Name { get; set; }
        public string XPath { get; set; }
        public string DataType { get; set; }
        public bool IsRepeating { get; set; }
        public List<BtmSchemaNode> Children { get; set; } = new List<BtmSchemaNode>();
        public BtmSchemaNode Parent { get; set; }
    }

    /// <summary>
    /// Represents the intermediate translated map data ready for Liquid Mapping Language (LML) generation.
    /// </summary>
    /// <remarks>
    /// This class contains the processed mapping information after functoid translation
    /// and before final LML output generation.
    /// </remarks>
    public class TranslatedMapData
    {
        public string Version { get; set; } = "1";
        public string InputFormat { get; set; } = "XML";
        public string OutputFormat { get; set; } = "XML";
        public string SourceSchema { get; set; }
        public string TargetSchema { get; set; }
        public string BtmFilePath { get; set; }
        public string SourceSchemaFilePath { get; set; }
        public string TargetSchemaFilePath { get; set; }
        public Dictionary<string, string> SourceNamespaces { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> TargetNamespaces { get; set; } = new Dictionary<string, string>();
        public List<LmlMapping> Mappings { get; set; } = new List<LmlMapping>();
    }

    /// <summary>
    /// Represents a single field or structural mapping element in the LML output.
    /// </summary>
    /// <remarks>
    /// Supports simple field mappings, loops, conditional mappings, and hierarchical structures.
    /// </remarks>
    public class LmlMapping
    {
        public string TargetPath { get; set; }
        public string SourceExpression { get; set; }
        public List<LmlMapping> Children { get; set; } = new List<LmlMapping>();
        public string LoopExpression { get; set; }
        public string LoopVariable { get; set; }
        public string ConditionalExpression { get; set; }
        public bool IsConditional { get; set; }
        public bool IsLoop { get; set; }
        public bool IsAttribute { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// Gets or sets the target parent path for loop structures used to identify child mapping nesting locations.
        /// </summary>
        /// <example>/Basket/OrderForms/OrderForm/LineItems</example>
        public string LoopTargetParentPath { get; set; }
        
        /// <summary>
        /// Gets or sets the target element name that appears inside the loop structure.
        /// </summary>
        /// <example>LineItem</example>
        public string LoopTargetElementName { get; set; }
    }
    
    /// <summary>
    /// Resolves and tracks the relationships between functoids and links in a BizTalk map.
    /// </summary>
    /// <remarks>
    /// This class builds the dependency graph for functoid chains and determines execution order.
    /// </remarks>
    public class FunctoidRelationshipResolver
    {
        private BtmMapData _mapData;
        
        public FunctoidRelationshipResolver(BtmMapData mapData)
        {
            _mapData = mapData;
        }
        
        /// <summary>
        /// Resolves all functoid and link relationships in the map.
        /// </summary>
        /// <remarks>
        /// This method must be called after parsing the BTM file to populate functoid input/output
        /// links and build the lookup dictionaries.
        /// </remarks>
        public void ResolveRelationships()
        {
            // Build functoid lookup dictionary
            _mapData.FunctoidLookup.Clear();
            foreach (var functoid in _mapData.Functoids)
            {
                _mapData.FunctoidLookup[functoid.FunctoidId] = functoid;
            }
            
            // Build link lookup dictionary
            _mapData.LinkLookup.Clear();
            foreach (var link in _mapData.Links)
            {
                _mapData.LinkLookup[link.LinkId] = link;
            }
            
            // Resolve link endpoints (determine if they point to functoids or schema nodes)
            foreach (var link in _mapData.Links)
            {
                // Check if LinkFrom is a functoid ID (simple numeric string or matches a functoid)
                if (_mapData.FunctoidLookup.TryGetValue(link.LinkFrom, out var sourceFunctoid))
                {
                    link.IsFromFunctoid = true;
                    link.SourceFunctoid = sourceFunctoid;
                    sourceFunctoid.OutputLinks.Add(link);
                }
                else
                {
                    link.IsFromFunctoid = false;
                }
                
                // Check if LinkTo is a functoid ID
                if (_mapData.FunctoidLookup.TryGetValue(link.LinkTo, out var targetFunctoid))
                {
                    link.IsToFunctoid = true;
                    link.TargetFunctoid = targetFunctoid;
                    targetFunctoid.InputLinks.Add(link);
                }
                else
                {
                    link.IsToFunctoid = false;
                }
            }
            
            // Resolve parameter links (connect functoid inputs to their source links)
            foreach (var functoid in _mapData.Functoids)
            {
                foreach (var param in functoid.InputParameters)
                {
                    if (param.Type == "link" && _mapData.LinkLookup.TryGetValue(param.Value, out var paramLink))
                    {
                        // This parameter references a link - add it to input links if not already there
                        if (!functoid.InputLinks.Contains(paramLink))
                        {
                            functoid.InputLinks.Add(paramLink);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the complete chain of functoids from source to target for a given final link.
        /// </summary>
        /// <param name="finalLink">The link for which to retrieve the functoid chain.</param>
        /// <returns>A list of functoids in execution order from source to target.</returns>
        public List<BtmFunctoid> GetFunctoidChain(BtmLink finalLink)
        {
            var chain = new List<BtmFunctoid>();
            var visited = new HashSet<string>();
            
            TraverseFunctoidChain(finalLink.LinkFrom, chain, visited);
            
            return chain;
        }
        
        private void TraverseFunctoidChain(string nodeId, List<BtmFunctoid> chain, HashSet<string> visited)
        {
            // Avoid infinite loops
            if (visited.Contains(nodeId))
                return;
            
            visited.Add(nodeId);
            
            // Check if this is a functoid
            if (_mapData.FunctoidLookup.TryGetValue(nodeId, out var functoid))
            {
                // Recursively traverse all inputs to this functoid
                foreach (var inputLink in functoid.InputLinks)
                {
                    TraverseFunctoidChain(inputLink.LinkFrom, chain, visited);
                }
                
                // Add this functoid after its inputs (ensures correct execution order)
                chain.Add(functoid);
            }
        }
        
        /// <summary>
        /// Gets all target schema nodes that this functoid ultimately connects to.
        /// </summary>
        /// <param name="functoid">The functoid for which to retrieve target nodes.</param>
        /// <returns>A list of target node identifiers.</returns>
        public List<string> GetTargetNodes(BtmFunctoid functoid)
        {
            var targets = new List<string>();
            var visited = new HashSet<string>();
            
            TraverseOutputs(functoid.FunctoidId, targets, visited);
            
            return targets;
        }
        
        private void TraverseOutputs(string nodeId, List<string> targets, HashSet<string> visited)
        {
            if (visited.Contains(nodeId))
                return;
            
            visited.Add(nodeId);
            
            // Find all links that start from this node
            var outgoingLinks = _mapData.Links.Where(l => l.LinkFrom == nodeId).ToList();
            
            foreach (var link in outgoingLinks)
            {
                if (link.IsToFunctoid)
                {
                    // Goes to another functoid - continue traversal
                    TraverseOutputs(link.LinkTo, targets, visited);
                }
                else
                {
                    // Goes to a schema node - this is a target
                    targets.Add(link.LinkTo);
                }
            }
        }
    }
}
