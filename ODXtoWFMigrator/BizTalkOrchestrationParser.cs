// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace BizTalktoLogicApps.ODXtoWFMigrator
{
    /// <summary>
    /// Represents the complete model of a parsed BizTalk orchestration.
    /// </summary>
    /// <remarks>
    /// Contains all orchestration metadata including messages, port types, ports, and the complete shape hierarchy.
    /// This is the root model returned by <see cref="BizTalkOrchestrationParser.ParseOdx"/>.
    /// </remarks>
    public sealed class OrchestrationModel
    {
        /// <summary>
        /// Gets or sets the namespace of the orchestration.
        /// </summary>
        public string Namespace { get; set; }
        
        /// <summary>
        /// Gets or sets the simple name of the orchestration.
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets the fully qualified name of the orchestration (Namespace.Name).
        /// </summary>
        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
        
        /// <summary>
        /// Gets the collection of message declarations in the orchestration.
        /// </summary>
        public List<MessageModel> Messages { get; } = new List<MessageModel>();
        
        /// <summary>
        /// Gets the collection of port type definitions.
        /// </summary>
        public List<PortTypeModel> PortTypes { get; } = new List<PortTypeModel>();
        
        /// <summary>
        /// Gets the collection of port declarations (runtime instances of port types).
        /// </summary>
        public List<PortModel> Ports { get; } = new List<PortModel>();
        
        /// <summary>
        /// Gets the root-level shapes (orchestration body).
        /// </summary>
        /// <remarks>
        /// This collection contains only top-level shapes. Nested shapes are in <see cref="ShapeModel.Children"/>.
        /// </remarks>
        public List<ShapeModel> Shapes { get; } = new List<ShapeModel>();
    }

    /// <summary>
    /// Represents a message declaration in a BizTalk orchestration.
    /// </summary>
    public sealed class MessageModel
    {
        /// <summary>Gets or sets the message variable name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the message schema type (fully qualified).</summary>
        public string Type { get; set; }
        
        /// <summary>Gets or sets the parameter direction (In, Out, InOut).</summary>
        public string Direction { get; set; }
    }

    /// <summary>
    /// Represents a port type definition with operations.
    /// </summary>
    public sealed class PortTypeModel
    {
        /// <summary>Gets or sets the port type name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the type modifier (Public, Private, Internal).</summary>
        public string Modifier { get; set; }
        
        /// <summary>Gets the collection of operations defined in this port type.</summary>
        public List<OperationModel> Operations { get; } = new List<OperationModel>();
    }

    /// <summary>
    /// Represents a port operation (one-way or request-response).
    /// </summary>
    public sealed class OperationModel
    {
        /// <summary>Gets or sets the operation name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the operation type (OneWay, RequestResponse).</summary>
        public string OperationType { get; set; }
        
        /// <summary>Gets or sets the request message type.</summary>
        public string RequestMessageType { get; set; }
        
        /// <summary>Gets or sets the response message type (for request-response operations).</summary>
        public string ResponseMessageType { get; set; }
        
        /// <summary>Gets or sets the fault message type.</summary>
        public string FaultMessageType { get; set; }
    }

    /// <summary>
    /// Defines the communication direction of a BizTalk port.
    /// </summary>
    public enum PortDirection 
    { 
        /// <summary>No direction specified.</summary>
        None, 
        /// <summary>One-way receive port.</summary>
        Receive, 
        /// <summary>One-way send port.</summary>
        Send, 
        /// <summary>Request-response port (receive request, send response).</summary>
        ReceiveSend, 
        /// <summary>Solicit-response port (send request, receive response).</summary>
        SendReceive 
    }

    /// <summary>
    /// Represents a port declaration instance with binding information.
    /// </summary>
    public sealed class PortModel
    {
        /// <summary>Gets or sets the port instance name.</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the reference to the port type.</summary>
        public string PortTypeReference { get; set; }
        
        /// <summary>Gets or sets the port communication direction.</summary>
        public PortDirection Direction { get; set; }
        
        /// <summary>Gets or sets the binding kind (Logical, Physical, Direct, Web).</summary>
        public string BindingKind { get; set; }
        
        /// <summary>Gets or sets the adapter name (FILE, FTP, HTTP, etc.).</summary>
        public string AdapterName { get; set; }
        
        /// <summary>Gets or sets the transport type.</summary>
        public string TransportType { get; set; }
        
        /// <summary>Gets or sets the transport address (from bindings).</summary>
        public string Address { get; set; }
        
        /// <summary>Gets or sets the folder path (for FILE adapter).</summary>
        public string FolderPath { get; set; }
        
        /// <summary>Gets or sets the file mask pattern (for FILE adapter).</summary>
        public string FileMask { get; set; }
        
        /// <summary>Gets or sets the polling interval in seconds (for FILE/FTP adapters).</summary>
        public int? PollingIntervalSeconds { get; set; }
        
        /// <summary>Gets or sets the receive pipeline name.</summary>
        public string ReceivePipelineName { get; set; }
        
        /// <summary>Gets or sets the send pipeline name.</summary>
        public string SendPipelineName { get; set; }
    }

    /// <summary>
    /// Abstract base class for all orchestration shapes.
    /// </summary>
    /// <remarks>
    /// Shapes form a hierarchy with parent-child relationships representing control flow.
    /// Each shape has a unique identifier (Oid), sequence number, and can have child shapes.
    /// </remarks>
    public abstract class ShapeModel
    {
        /// <summary>Gets or sets the BizTalk object identifier (OID) from the ODX file.</summary>
        public string Oid { get; set; }
        
        /// <summary>Gets or sets the shape name (designer name).</summary>
        public string Name { get; set; }
        
        /// <summary>Gets or sets the shape type identifier.</summary>
        public string ShapeType { get; set; }
        
        /// <summary>Gets or sets the sequence number in the orchestration flow.</summary>
        public int Sequence { get; set; }
        
        /// <summary>Gets or sets the parent shape (null for root-level shapes).</summary>
        public ShapeModel Parent { get; set; }
        
        /// <summary>Gets the child shapes contained within this shape.</summary>
        public List<ShapeModel> Children { get; } = new List<ShapeModel>();
        
        /// <summary>Gets or sets a unique identifier for duplicate shape name handling.</summary>
        public string UniqueId { get; set; }
    }

    /// <summary>
    /// Represents a Receive shape that receives messages from a port.
    /// </summary>
    public sealed class ReceiveShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the port name to receive from.</summary>
        public string PortName { get; set; }
        
        /// <summary>Gets or sets the message variable name.</summary>
        public string MessageName { get; set; }
        
        /// <summary>Gets or sets the operation name.</summary>
        public string OperationName { get; set; }
        
        /// <summary>Gets or sets the operation message name.</summary>
        public string OperationMessageName { get; set; }
        
        /// <summary>Gets or sets whether this receive activates the orchestration instance.</summary>
        public bool Activate { get; set; }
        
        /// <summary>Gets the correlation sets initialized by this receive.</summary>
        public List<string> InitializesCorrelationSets { get; } = new List<string>();
        
        /// <summary>Gets the correlation sets followed by this receive.</summary>
        public List<string> FollowsCorrelationSets { get; } = new List<string>();
    }

    /// <summary>
    /// Represents a Send shape that sends messages to a port.
    /// </summary>
    public sealed class SendShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the port name to send to.</summary>
        public string PortName { get; set; }
        
        /// <summary>Gets or sets the message variable name.</summary>
        public string MessageName { get; set; }
        
        /// <summary>Gets or sets the operation name.</summary>
        public string OperationName { get; set; }
        
        /// <summary>Gets or sets the operation message name.</summary>
        public string OperationMessageName { get; set; }
    }

    /// <summary>
    /// Represents a Construct Message shape that builds or modifies messages.
    /// </summary>
    public sealed class ConstructShapeModel : ShapeModel
    {
        /// <summary>Gets the list of message names being constructed.</summary>
        public List<string> ConstructedMessages { get; } = new List<string>();
        
        /// <summary>Gets the inner shapes (Transform, MessageAssignment) that build the message.</summary>
        public List<ShapeModel> InnerShapes { get; } = new List<ShapeModel>();
    }

    /// <summary>
    /// Represents a Transform shape that applies XSLT maps to messages.
    /// </summary>
    public sealed class TransformShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the XSLT map class name (fully qualified).</summary>
        public string ClassName { get; set; }
        
        /// <summary>Gets or sets the input message names.</summary>
        public List<string> InputMessages { get; set; } = new List<string>();
        
        /// <summary>Gets or sets the output message names.</summary>
        public List<string> OutputMessages { get; } = new List<string>();
    }

    /// <summary>
    /// Represents a Variable Assignment shape.
    /// </summary>
    public sealed class VariableAssignmentShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the C# assignment expression.</summary>
        public string Expression { get; set; }
    }

    /// <summary>
    /// Represents a Message Assignment shape within a Construct block.
    /// </summary>
    public sealed class MessageAssignmentShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the C# expression that assigns message properties.</summary>
        public string Expression { get; set; }
    }

    /// <summary>
    /// Represents a While loop shape.
    /// </summary>
    public sealed class WhileShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the while condition expression.</summary>
        public string Expression { get; set; }
    }

    /// <summary>
    /// Represents an Until loop shape.
    /// </summary>
    public sealed class UntilShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the until condition expression.</summary>
        public string Expression { get; set; }
    }

    /// <summary>
    /// Represents a Call Orchestration shape.
    /// </summary>
    public sealed class CallShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the called orchestration name.</summary>
        public string Invokee { get; set; }
    }

    /// <summary>
    /// Represents a correlation set declaration.
    /// </summary>
    public sealed class CorrelationDeclarationShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the correlation type reference.</summary>
        public string CorrelationTypeRef { get; set; }
        
        /// <summary>Gets the statement references that use this correlation set.</summary>
        public List<StatementCorrelationRef> StatementRefs { get; } = new List<StatementCorrelationRef>();
    }

    /// <summary>
    /// Represents a Loop or ForEach shape.
    /// </summary>
    public sealed class LoopShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the loop type (Loop, ForEach).</summary>
        public string LoopType { get; set; }
        
        /// <summary>Gets or sets the collection expression to iterate over.</summary>
        public string CollectionExpression { get; set; }
        
        /// <summary>Gets or sets the iterator variable name.</summary>
        public string ItemVariable { get; set; }
    }

    /// <summary>
    /// Represents a Decide (If/Else) shape with conditional branching.
    /// </summary>
    public sealed class DecideShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the decision condition expression.</summary>
        public string Expression { get; set; }
        
        /// <summary>Gets or sets the shapes to execute when condition is true.</summary>
        public List<ShapeModel> TrueBranch { get; set; } = new List<ShapeModel>();
        
        /// <summary>Gets or sets the shapes to execute when condition is false (else branch).</summary>
        public List<ShapeModel> FalseBranch { get; set; } = new List<ShapeModel>();
    }

    /// <summary>
    /// Represents a Switch shape with multiple case branches.
    /// </summary>
    public sealed class SwitchShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the switch expression to evaluate.</summary>
        public string Expression { get; set; }
        
        /// <summary>Gets or sets the case branches keyed by case value.</summary>
        public Dictionary<string, List<ShapeModel>> Cases { get; set; } = new Dictionary<string, List<ShapeModel>>();
        
        /// <summary>Gets or sets the default case shapes.</summary>
        public List<ShapeModel> DefaultCase { get; set; } = new List<ShapeModel>();
    }

    /// <summary>
    /// Represents a Listen shape that waits for the first of multiple events.
    /// </summary>
    public sealed class ListenShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the listen branches (first to complete wins).</summary>
        public List<ShapeModel> Branches { get; set; } = new List<ShapeModel>();
    }

    /// <summary>
    /// Represents a correlation statement reference.
    /// </summary>
    public sealed class StatementCorrelationRef
    {
        /// <summary>Gets or sets the statement OID that uses the correlation set.</summary>
        public string StatementOid { get; set; }
        
        /// <summary>Gets or sets whether this statement initializes the correlation set.</summary>
        public bool Initializes { get; set; }
    }

    /// <summary>Represents a Scope shape for grouping and error handling.</summary>
    public sealed class ScopeShapeModel : ShapeModel { }
    
    /// <summary>Represents a Terminate, Suspend, or Throw shape.</summary>
    public sealed class TerminateShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the error message or exception details.</summary>
        public string ErrorMessage { get; set; } 
    }
    
    /// <summary>Represents an unknown or unsupported shape type.</summary>
    public sealed class FallbackShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets details about the unhandled shape.</summary>
        public string Details { get; set; } 
    }
    
    /// <summary>Represents an Expression shape for executing C# code.</summary>
    public sealed class ExpressionShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the C# expression to execute.</summary>
        public string Expression { get; set; } 
    }
    
    /// <summary>Represents a Delay shape for waiting a specified duration.</summary>
    public sealed class DelayShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the delay duration expression (e.g., "System.TimeSpan.FromMinutes(5)").</summary>
        public string DelayExpression { get; set; } 
    }
    
    /// <summary>Represents a Compensate shape for invoking compensation logic.</summary>
    public sealed class CompensateShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the target scope to compensate.</summary>
        public string Target { get; set; } 
    }
    /// <summary>Represents a Group shape for logical organization.</summary>
    public sealed class GroupShapeModel : ShapeModel { }
    
    /// <summary>Represents a Parallel shape with concurrent execution branches.</summary>
    public sealed class ParallelShapeModel : ShapeModel { }
    
    /// <summary>Represents a branch within a Parallel shape.</summary>
    public sealed class ParallelBranchShapeModel : ShapeModel { }
    
    /// <summary>Represents a Start Orchestration shape (asynchronous call).</summary>
    public sealed class StartShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the orchestration to start.</summary>
        public string Invokee { get; set; } 
    }
    
    /// <summary>Represents a Task shape within a Listen shape.</summary>
    public sealed class TaskShapeModel : ShapeModel { }
    /// <summary>Represents a Catch exception handler shape.</summary>
    public sealed class CatchShapeModel : ShapeModel 
    { 
        /// <summary>Gets or sets the exception type to catch (e.g., "System.Exception").</summary>
        public string ExceptionType { get; set; }
        
        /// <summary>Gets or sets the exception variable name.</summary>
        public string ExceptionVariable { get; set; }
        
        /// <summary>Gets or sets the exception handler shapes.</summary>
        public List<ShapeModel> ExceptionHandlers { get; set; } = new List<ShapeModel>();
    }
    
    /// <summary>Represents a Compensation scope shape.</summary>
    public sealed class CompensationScopeShapeModel : ShapeModel { }
    
    /// <summary>Represents an Atomic Transaction scope.</summary>
    public sealed class AtomicTransactionShapeModel : ShapeModel { }
    
    /// <summary>Represents a Long Running Transaction scope.</summary>
    public sealed class LongRunningTransactionShapeModel : ShapeModel { }

    /// <summary>
    /// Represents transaction attributes metadata.
    /// </summary>
    public sealed class TransactionAttributeShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the transaction timeout.</summary>
        public string Timeout { get; set; }
        
        /// <summary>Gets or sets the isolation level.</summary>
        public string Isolation { get; set; }
        
        /// <summary>Gets or sets whether to retry on failure.</summary>
        public bool Retry { get; set; }
        
        /// <summary>Gets or sets whether to batch transactions.</summary>
        public bool Batch { get; set; }
    }

    /// <summary>
    /// Represents a variable declaration shape.
    /// </summary>
    public sealed class VariableDeclarationShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the variable type (e.g., "System.String", "System.Xml.XmlDocument").</summary>
        public string VarType { get; set; }
        
        /// <summary>Gets or sets whether to use the default constructor.</summary>
        public string UseDefault { get; set; }
    }

    /// <summary>
    /// Represents a Call Rules (Business Rules Engine) shape.
    /// </summary>
    public sealed class CallRulesShapeModel : ShapeModel
    {
        /// <summary>Gets or sets the BRE policy name to execute.</summary>
        public string PolicyName { get; set; }
    }

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

                // Debug logging
                Console.WriteLine($"[PARSE] Found shape type: {type}, OID: {oid}, Parent: {parent?.Name ?? "ROOT"}");

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

                            Console.WriteLine($"[PARSER] Decision '{decideShape.Name}' has {branches.Count} branches");

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
                                            Console.WriteLine($"[PARSER]   Found nested Expression shape in branch {i}");
                                        }
                                    }

                                    // The first branch with an expression is the "if" condition
                                    if (!string.IsNullOrWhiteSpace(exprValue) && ruleBranch == null)
                                    {
                                        ruleBranch = branch;
                                        foundExpression = exprValue;
                                        decideShape.Expression = exprValue;
                                        Console.WriteLine($"[PARSER]   ✅ Found Expression in branch {i} ('{branchName ?? "unnamed"}'): {exprValue.Substring(0, Math.Min(60, exprValue.Length))}...");
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
                                            Console.WriteLine($"[PARSER]   ✅ Found Expression as sibling to branches: {foundExpression.Substring(0, Math.Min(60, foundExpression.Length))}...");
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
                                    Console.WriteLine($"[PARSER]   ⚠️ No Expression found - using branch order");
                                }

                                // Parse TRUE branch (including any Expression shapes inside it)
                                if (trueBranchNav != null)
                                {
                                    var trueBranchModel = new OrchestrationModel();
                                    int trueBranchSeq = 0;

                                    // Parse ALL shapes in the branch, including Expression shapes
                                    ParseShapes(trueBranchNav, nsmgr, trueBranchModel, ref trueBranchSeq, oidMap, null);

                                    Console.WriteLine($"[PARSER]   TrueBranch parsed: {trueBranchModel.Shapes.Count} shapes");

                                    // Check if we captured any Expression shapes
                                    var expressionShapes = trueBranchModel.Shapes.OfType<ExpressionShapeModel>().ToList();
                                    if (expressionShapes.Any())
                                    {
                                        Console.WriteLine($"[PARSER]     Found {expressionShapes.Count} Expression shape(s) in TRUE branch");
                                        // If decide doesn't have expression yet, use the first Expression shape's expression
                                        if (string.IsNullOrWhiteSpace(decideShape.Expression) && expressionShapes.Any())
                                        {
                                            decideShape.Expression = expressionShapes.First().Expression;
                                            Console.WriteLine($"[PARSER]     Using Expression from shape: {expressionShapes.First().Name}");
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

                                    Console.WriteLine($"[PARSER]   FalseBranch parsed: {falseBranchModel.Shapes.Count} shapes");

                                    // Check if we captured any Expression shapes
                                    var expressionShapes = falseBranchModel.Shapes.OfType<ExpressionShapeModel>().ToList();
                                    if (expressionShapes.Any())
                                    {
                                        Console.WriteLine($"[PARSER]     Found {expressionShapes.Count} Expression shape(s) in FALSE branch");
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
                                Console.WriteLine($"[PARSER]   No DecisionBranch elements found, looking for direct children");

                                // Check for Expression shape as direct child
                                var directExpr = child.SelectSingleNode("om:Element[@Type='Expression']", nsmgr);
                                if (directExpr != null)
                                {
                                    var exprValue = Eval(directExpr, nsmgr, "om:Property[@Name='Expression']/@Value");
                                    if (!string.IsNullOrWhiteSpace(exprValue))
                                    {
                                        decideShape.Expression = exprValue;
                                        Console.WriteLine($"[PARSER]   Found direct Expression child: {exprValue.Substring(0, Math.Min(60, exprValue.Length))}...");
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

                            Console.WriteLine($"[PARSER] Switch '{switchShape.Name}' with expression: {switchShape.Expression?.Substring(0, Math.Min(60, switchShape.Expression?.Length ?? 0))}...");

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

                                Console.WriteLine($"[PARSER]   Case {++caseIndex}: '{caseName}' with value: {caseValue ?? "(default)"}");

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
                                    Console.WriteLine($"[PARSER]     Default case parsed: {caseModel.Shapes.Count} shapes");
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
                                    Console.WriteLine($"[PARSER]     Case '{caseKey}' parsed: {caseModel.Shapes.Count} shapes");
                                }
                            }

                            Console.WriteLine($"[PARSER] Switch complete with {switchShape.Cases.Count} cases and {(switchShape.DefaultCase.Count > 0 ? "a" : "no")} default case");

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
                            var listenBranches = Select(child, nsmgr, "om:Element[@Type='Task']");
                            int branchIndex = 0;
                            foreach (var branchNav in listenBranches)
                            {
                                Console.WriteLine($"[PARSER] Parsing Listen branch {++branchIndex}");
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
                            Console.WriteLine($"[PARSE] Skipping metadata shape: TransactionAttribute");
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

                        // Catch-all for unknown shapes
                        default:
                            Console.WriteLine($"[PARSE] ⚠️ Unknown shape type: {type}");
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

                    Console.WriteLine($"[PARSE] Shape {shape.Name} ({shape.ShapeType}) - Will recurse: {needsRecursion}");

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
                shape.InputMessages = new List<string> { shape.InputMessages[0] };
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
                        Console.WriteLine($"[INFO] Loading connector registry from: {path}");
                        var registry = ConnectorSchemaRegistry.LoadFromFile(path);

                        if (registry != null)
                        {
                            Console.WriteLine($"[SUCCESS] Connector registry loaded successfully with {registry.ConnectorCount} connector(s)");
                        }

                        return registry;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Failed to load connector registry from {path}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("[WARNING] Connector registry file (connector-registry.json) not found in any search path:");
            foreach (var path in pathsArray)
            {
                Console.WriteLine($"  - {path}");
            }
            Console.WriteLine("[WARNING] Workflow generation will proceed without connector schema information.");
            Console.WriteLine("[WARNING] Generated workflows may require manual connector configuration.");

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
        /// Performs diagnostic analysis on a BizTalk orchestration and outputs detailed shape hierarchy.
        /// Prints shape counts, complete hierarchy tree, and decision/switch details to console.
        /// Useful for understanding orchestration structure and troubleshooting parsing issues.
        /// </summary>
        /// <param name="odxPath">The path to the BizTalk orchestration (.odx) file to diagnose.</param>
        public static void DiagnoseOrchestration(string odxPath)
        {
            var model = ParseOdx(odxPath);

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
            Console.WriteLine($"  ✓ Decision/If Shapes: {totalDecisions}");
            Console.WriteLine($"  ✓ Switch Shapes: {totalSwitches}");
            Console.WriteLine($"  ✓ Construct Shapes: {totalConstructs}");
            Console.WriteLine($"  ✓ MessageAssignment Shapes: {totalMessageAssignments}");
            Console.WriteLine($"  ✓ Start Orchestration Shapes: {totalStarts}");
            Console.WriteLine($"  ✓ Receive Shapes: {totalReceives}");
            Console.WriteLine($"  ✓ Send Shapes: {totalSends}");
            Console.WriteLine($"  ✓ Scope Shapes: {totalScopes}");

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
        /// <param name="shapes">The collection of shapes to count.</param>
        /// <param name="decisions">Reference counter for Decision/If shapes.</param>
        /// <param name="switches">Reference counter for Switch shapes.</param>
        /// <param name="msgAssignments">Reference counter for MessageAssignment shapes.</param>
        /// <param name="starts">Reference counter for Start Orchestration shapes.</param>
        /// <param name="receives">Reference counter for Receive shapes.</param>
        /// <param name="sends">Reference counter for Send shapes.</param>
        /// <param name="constructs">Reference counter for Construct shapes.</param>
        /// <param name="scopes">Reference counter for Scope/Transaction shapes.</param>
        /// <param name="depth">Current recursion depth (for tracking nesting level).</param>
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
        /// <param name="shape">The shape to print.</param>
        /// <param name="indent">The indentation level (number of levels deep in hierarchy).</param>
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
                    Console.WriteLine($"{prefix}  ✓ TRUE branch ({decide.TrueBranch.Count} shapes):");
                    foreach (var child in decide.TrueBranch.OrderBy(c => c.Sequence))
                        PrintShapeTree(child, indent + 2);
                }
                else
                {
                    Console.WriteLine($"{prefix}  ✓ TRUE branch (empty)");
                }

                if (decide.FalseBranch.Count > 0)
                {
                    Console.WriteLine($"{prefix}  ✗ FALSE branch ({decide.FalseBranch.Count} shapes):");
                    foreach (var child in decide.FalseBranch.OrderBy(c => c.Sequence))
                        PrintShapeTree(child, indent + 2);
                }
                else
                {
                    Console.WriteLine($"{prefix}  ✗ FALSE branch (empty)");
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
        /// <param name="shapes">The collection of shapes to analyze for decisions.</param>
        /// <param name="level">The indentation level for formatted output.</param>
        private static void PrintDecisionDetails(IEnumerable<ShapeModel> shapes, int level)
        {
            foreach (var shape in shapes.OrderBy(s => s.Sequence))
            {
                if (shape is DecideShapeModel decide)
                {
                    string indent = new string(' ', level * 2);
                    Console.WriteLine($"{indent}▶ Decision: '{decide.Name}'");
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
                    Console.WriteLine($"{indent}▶ Switch: '{switchShape.Name}'");
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