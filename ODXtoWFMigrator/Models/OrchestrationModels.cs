// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

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

        /// <summary>Gets the input message names.</summary>
        public List<string> InputMessages { get; } = new List<string>();

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
}
