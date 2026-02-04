using System;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public enum StageExecutionMode
    {
        All,
        FirstMatch,
        StopOnConsume
    }

    public class StageMetadata
    {
        public string Name { get; set; }
        public string CategoryId { get; set; }
        public StageExecutionMode ExecutionMode { get; set; }
        public int MinOccurs { get; set; }
        public int MaxOccurs { get; set; }
        public string Description { get; set; }
        public string Purpose { get; set; }
        public string Behavior { get; set; }
        public bool IsExecutionModeReadOnly { get; set; }
        public string ExecutionModeNote { get; set; }

        public static StageMetadata GetMetadata(string categoryId)
        {
            switch (categoryId?.ToLower())
            {
                case "9d0e4103-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Decode",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Decode or decrypt the message",
                        Description = "Components that decode or decrypt incoming messages from one format to another",
                        Behavior = "All components in this stage are run. Stage takes one message and produces one message.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed"
                    };

                case "9d0e4105-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Disassemble",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.FirstMatch,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Parse or disassemble the message",
                        Description = "Components that parse or disassemble messages into zero, one, or multiple messages",
                        Behavior = "Only the first component that recognizes the message format is run. Can produce 0-N messages. This is the ONLY stage with FirstMatch execution mode.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'FirstMatch' and cannot be changed. This is the ONLY stage in receive pipelines with FirstMatch mode."
                    };

                case "9d0e410d-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Validate",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Validate the message format",
                        Description = "Components that validate XML messages against schemas",
                        Behavior = "All components run. Executes once per message created by Disassemble stage.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed"
                    };

                case "9d0e410e-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "ResolveParty",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Resolve party information",
                        Description = "Placeholder for Party Resolution Pipeline Component",
                        Behavior = "All components run. Executes once per message created by Disassemble stage.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed"
                    };

                case "9d0e4101-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Pre-Assemble",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Pre-processing before assembly",
                        Description = "Custom processing before message assembly",
                        Behavior = "All components in this stage are run.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed. All send pipeline stages use 'All' execution mode."
                    };

                case "9d0e4107-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Assemble",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 1,
                        Purpose = "Assemble the message",
                        Description = "Components that serialize messages and add envelopes",
                        Behavior = "All components run. Maximum of 1 component in this stage.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed. All send pipeline stages use 'All' execution mode."
                    };

                case "9d0e4108-4cce-4536-83fa-4a5040674ad6":
                    return new StageMetadata
                    {
                        Name = "Encode",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Encode or encrypt the message",
                        Description = "Components that encode or encrypt outgoing messages",
                        Behavior = "All components in this stage are run.",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Execution mode is always 'All' and cannot be changed. All send pipeline stages use 'All' execution mode."
                    };

                default:
                    return new StageMetadata
                    {
                        Name = "Unknown",
                        CategoryId = categoryId,
                        ExecutionMode = StageExecutionMode.All,
                        MinOccurs = 0,
                        MaxOccurs = 255,
                        Purpose = "Unknown",
                        Description = "Unknown stage",
                        Behavior = "Unknown behavior",
                        IsExecutionModeReadOnly = true,
                        ExecutionModeNote = "Unknown"
                    };
            }
        }
    }
}
