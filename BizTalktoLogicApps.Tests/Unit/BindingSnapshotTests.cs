// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using BizTalktoLogicApps.ODXtoWFMigrator;
using System.IO;
using System.Linq;

namespace BizTalktoLogicApps.Tests.Unit
{
    [TestClass]
    public class BindingSnapshotTests
    {
        [TestMethod]
        public void Parse_ValidBindingFile_ParsesReceiveLocations()
        {
            // Arrange
            var sampleBindingXml = CreateSampleBindingXml();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, sampleBindingXml);
            
            try
            {
                // Act
                var snapshot = BindingSnapshot.Parse(tempFile);
                
                // Assert
                Assert.IsNotNull(snapshot);
                Assert.IsTrue(snapshot.ReceiveLocations.Count > 0, 
                    "Should parse at least one receive location");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
        
        [TestMethod]
        public void Parse_ReceiveLocation_ExtractsTransportType()
        {
            // Arrange
            var bindingXml = CreateBindingWithFileAdapter();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, bindingXml);
            
            try
            {
                // Act
                var snapshot = BindingSnapshot.Parse(tempFile);
                var receiveLocation = snapshot.ReceiveLocations.FirstOrDefault();
                
                // Assert
                Assert.IsNotNull(receiveLocation);
                Assert.AreEqual("FILE", receiveLocation.TransportType, 
                    "Should extract FILE transport type");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
        
        [TestMethod]
        public void GetSendPortsForReceivePort_ReturnsMatchingPorts()
        {
            // Arrange
            var snapshot = new BindingSnapshot();
            snapshot.ReceiveLocations.Add(new BindingReceiveLocation
            {
                Name = "RL_Test",
                ReceivePortName = "RP_Test"
            });
            
            snapshot.SendPorts.Add(new BindingSendPort
            {
                Name = "SP_Test",
                Filters = new System.Collections.Generic.List<FilterCondition>
                {
                    new FilterCondition
                    {
                        Property = "BTS.ReceivePortName",
                        Operator = "0", // Equals
                        Value = "RP_Test"
                    }
                }
            });
            
            // Act
            var matchingSendPorts = snapshot.GetSendPortsForReceivePort("RP_Test");
            
            // Assert
            Assert.AreEqual(1, matchingSendPorts.Count, 
                "Should find one matching send port");
            Assert.AreEqual("SP_Test", matchingSendPorts[0].Name);
        }
        
        [TestMethod]
        public void Parse_WcfReceiveLocation_ExtractsSecurityMetadata()
        {
            // Arrange
            var wcfBindingXml = CreateWcfBindingXml();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, wcfBindingXml);
            
            try
            {
                // Act
                var snapshot = BindingSnapshot.Parse(tempFile);
                var wcfLocation = snapshot.ReceiveLocations.FirstOrDefault();
                
                // Assert
                Assert.IsNotNull(wcfLocation);
                Assert.IsNotNull(wcfLocation.SecurityMode, 
                    "Should extract WCF security mode");
                Assert.IsNotNull(wcfLocation.MessageClientCredentialType, 
                    "Should extract message credential type");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
        
        private string CreateSampleBindingXml()
        {
            return @"<?xml version='1.0'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' 
             xmlns:xsd='http://www.w3.org/2001/XMLSchema'>
  <ReceivePortCollection>
    <ReceivePort Name='TestReceivePort'>
      <ReceiveLocations>
        <ReceiveLocation Name='TestRL'>
          <ReceiveLocationTransportType Name='FILE' />
          <Address>C:\Input\*.xml</Address>
          <Enable>true</Enable>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
</BindingInfo>";
        }
        
        private string CreateBindingWithFileAdapter()
        {
            return CreateSampleBindingXml(); // Reuse for simplicity
        }
        
        private string CreateWcfBindingXml()
        {
            return @"<?xml version='1.0'?>
<BindingInfo xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <ReceivePortCollection>
    <ReceivePort Name='WcfPort'>
      <ReceiveLocations>
        <ReceiveLocation Name='WcfRL'>
          <ReceiveLocationTransportType Name='WCF-BasicHttp' />
          <Address>http://localhost/service</Address>
          <Enable>true</Enable>
          <ReceiveLocationTransportTypeData>&lt;CustomProps&gt;
            &lt;SecurityMode&gt;Transport&lt;/SecurityMode&gt;
            &lt;MessageClientCredentialType&gt;UserName&lt;/MessageClientCredentialType&gt;
          &lt;/CustomProps&gt;</ReceiveLocationTransportTypeData>
        </ReceiveLocation>
      </ReceiveLocations>
    </ReceivePort>
  </ReceivePortCollection>
</BindingInfo>";
        }
    }
}
