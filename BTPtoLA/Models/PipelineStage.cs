using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public class PipelineStage
    {
        [XmlAttribute]
        public string CategoryId { get; set; }

        [XmlArray("Components")]
        [XmlArrayItem("Component")]
        public List<PipelineComponent> Components { get; set; }

        public PipelineStage()
        {
            Components = new List<PipelineComponent>();
        }

        public string GetStageName()
        {
            var metadata = StageMetadata.GetMetadata(CategoryId);
            return metadata.Name;
        }

        public StageMetadata GetMetadata()
        {
            return StageMetadata.GetMetadata(CategoryId);
        }
    }
}
