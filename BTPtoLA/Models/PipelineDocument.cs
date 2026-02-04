using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    [XmlRoot("Document")]
    public class PipelineDocument
    {
        [XmlAttribute]
        public string PolicyFilePath { get; set; }

        [XmlAttribute]
        public int MajorVersion { get; set; }

        [XmlAttribute]
        public int MinorVersion { get; set; }

        [XmlElement]
        public string Description { get; set; }

        [XmlElement]
        public string CategoryId { get; set; }

        [XmlElement]
        public string FriendlyName { get; set; }

        [XmlArray("Stages")]
        [XmlArrayItem("Stage")]
        public List<PipelineStage> Stages { get; set; }

        public PipelineDocument()
        {
            Stages = new List<PipelineStage>();
        }

        public string GetPipelineType()
        {
            if (!string.IsNullOrEmpty(CategoryId))
            {
                switch (CategoryId.ToLower())
                {
                    case "f66b9f5e-43ff-4f5f-ba46-885348ae1b4e":
                        return "Receive";
                    case "8c6b051c-0ff5-4fc2-9ae5-5016cb726282":
                        return "Send";
                }
            }

            if (!string.IsNullOrEmpty(PolicyFilePath))
            {
                if (PolicyFilePath.Contains("Receive"))
                    return "Receive";
                if (PolicyFilePath.Contains("Transmit") || PolicyFilePath.Contains("Send"))
                    return "Send";
            }

            return "Unknown";
        }
    }
}
