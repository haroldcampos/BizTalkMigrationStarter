using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BizTalktoLogicApps.BTPtoLA.Models
{
    public class PipelineComponent
    {
        [XmlElement]
        public string Name { get; set; }

        [XmlElement]
        public string ComponentName { get; set; }

        [XmlElement]
        public string Description { get; set; }

        [XmlElement]
        public string Version { get; set; }

        [XmlArray("Properties")]
        [XmlArrayItem("Property")]
        public List<ComponentProperty> Properties { get; set; }

        [XmlElement]
        public string CachedDisplayName { get; set; }

        [XmlElement]
        public bool CachedIsManaged { get; set; }

        public PipelineComponent()
        {
            Properties = new List<ComponentProperty>();
        }

        public ComponentMetadata GetMetadata()
        {
            return ComponentMetadata.GetMetadata(Name ?? ComponentName);
        }
    }
}

