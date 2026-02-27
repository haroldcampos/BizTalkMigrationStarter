using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BizTalktoLogicApps.BTPtoLA;
using BizTalktoLogicApps.BTPtoLA.Models;
using BizTalktoLogicApps.BTPtoLA.Services;

namespace BizTalktoLogicApps.BTPtoLA
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("BizTalk Pipeline Parser");
            Console.WriteLine("=======================");
            Console.WriteLine();

            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  BTPtoLA.exe <pipeline-file.btp>                    - Parse and analyze pipeline");
                Console.WriteLine("  BTPtoLA.exe <pipeline-file.btp> <output-dir>       - Generate Logic Apps workflow");
                Console.WriteLine("  BTPtoLA.exe /workflow <pipeline-file.btp> [dir]    - Generate workflow (explicit)");
                Console.WriteLine("  BTPtoLA.exe /batch <pipeline-file.btp> <output-dir> - Non-interactive batch mode");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  BTPtoLA.exe XMLReceive.btp                  - Analyze XMLReceive.btp");
                Console.WriteLine("  BTPtoLA.exe XMLReceive.btp C:\\Output        - Generate workflow to C:\\Output");
                Console.WriteLine("  BTPtoLA.exe /workflow XMLReceive.btp        - Generate to current directory");
                Console.WriteLine("  BTPtoLA.exe /test C:\\TestData\\Pipelines    - Test all pipelines");
                Console.WriteLine("  BTPtoLA.exe /batch XMLReceive.btp C:\\Output - Batch mode (no prompts)");
                Console.WriteLine();
                
                DemoWithSampleXml();
                return;
            }

            // Check for batch mode (non-interactive)
            if (args.Length >= 3 && args[0].Equals("/batch", StringComparison.OrdinalIgnoreCase))
            {
                string pipelineFile = args[1];
                string outputDir = args[2];
                GenerateWorkflow(pipelineFile, outputDir, batchMode: true);
                return;
            }

            // Check for workflow generation mode (explicit)
            if (args.Length >= 2 && args[0].Equals("/workflow", StringComparison.OrdinalIgnoreCase))
            {
                string pipelineFile = args[1];
                string outputDir = args.Length > 2 ? args[2] : Path.GetDirectoryName(pipelineFile);
                GenerateWorkflow(pipelineFile, outputDir);
                return;
            }

           

            // Single file mode - analyze or generate based on argument count
            string pipelineFilePath = args[0];
            
            // If output directory specified, generate workflow
            if (args.Length >= 2)
            {
                string outputDirectory = args[1];
                GenerateWorkflow(pipelineFilePath, outputDirectory);
                return;
            }
            
            // Otherwise, just analyze the pipeline
            try
            {
                PipelineParser parser = new PipelineParser();
                PipelineDocument pipeline = parser.ParsePipelineFile(pipelineFilePath);
                
                parser.DisplayPipelineInfo(pipeline);

                Console.WriteLine();
                Console.WriteLine("Pipeline parsing completed successfully!");
                Console.WriteLine();
                Console.WriteLine("TIP: To generate a Logic Apps workflow, run:");
                Console.WriteLine("     BTPtoLA.exe " + Path.GetFileName(pipelineFilePath) + " <output-directory>");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }


        static void DemoWithSampleXml()
        {
            Console.WriteLine("No pipeline file specified. Running demo with sample pipelines...");
            Console.WriteLine();

            // Demo 1: PassThruReceive
            Console.WriteLine("========================================");
            Console.WriteLine("DEMO 1: PASSTHRU RECEIVE PIPELINE");
            Console.WriteLine("========================================");
            Console.WriteLine();

            string passThruReceiveXml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4105-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410d-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410e-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
  </Stages>
</Document>";

            RunPipelineDemo(passThruReceiveXml);

            // Demo 2: XMLReceive
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("DEMO 2: XML RECEIVE PIPELINE");
            Console.WriteLine("========================================");
            Console.WriteLine();

            string sampleReceiveXml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSReceivePolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4103-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.MIME_SMIME_Decoder</Name>
          <ComponentName>MIME/SMIME decoder</ComponentName>
          <Description>MIME/SMIME decoder component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""AllowNonMIMEMessage"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""ValidateCRL"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>MIME/SMIME decoder</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e4105-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.XmlDasmComp</Name>
          <ComponentName>XML disassembler</ComponentName>
          <Description>Streaming XML disassembler</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""EnvelopeSpecNames"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""DocumentSpecNames"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""AllowUnrecognizedMessage"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""ValidateDocument"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>XML disassembler</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e410d-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e410e-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.PartyRes</Name>
          <ComponentName>Party resolution</ComponentName>
          <Description>Party resolution component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""AllowBySID"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""AllowByCertName"">
              <Value xsi:type=""xsd:boolean"">true</Value>
            </Property>
          </Properties>
          <CachedDisplayName>Party resolution</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
  </Stages>
</Document>";

            RunPipelineDemo(sampleReceiveXml);

            // Demo 3: PassThruTransmit
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("DEMO 3: PASSTHRU SEND PIPELINE");
            Console.WriteLine("========================================");
            Console.WriteLine();

            string passThruSendXml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSTransmitPolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4101-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4107-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4108-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
  </Stages>
</Document>";

            RunPipelineDemo(passThruSendXml);

            // Demo 4: XMLTransmit
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("DEMO 4: XML SEND PIPELINE");
            Console.WriteLine("========================================");
            Console.WriteLine();

            string sampleSendXml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<Document xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" PolicyFilePath=""BTSTransmitPolicy.xml"" MajorVersion=""1"" MinorVersion=""0"">
  <Description />
  <Stages>
    <Stage CategoryId=""9d0e4101-4cce-4536-83fa-4a5040674ad6"">
      <Components />
    </Stage>
    <Stage CategoryId=""9d0e4107-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.XmlAsmComp</Name>
          <ComponentName>XML assembler</ComponentName>
          <Description>XML assembler component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""EnvelopeDocSpecNames"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""EnvelopeSpecTargetNamespaces"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""DocumentSpecNames"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""DocumentSpecTargetNamespaces"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""ProcessingInstructionsOptions"">
              <Value xsi:type=""xsd:int"">0</Value>
            </Property>
            <Property Name=""AddXmlDeclaration"">
              <Value xsi:type=""xsd:boolean"">true</Value>
            </Property>
            <Property Name=""TargetCharset"">
              <Value xsi:type=""xsd:string"" />
            </Property>
            <Property Name=""TargetCodePage"">
              <Value xsi:type=""xsd:int"">0</Value>
            </Property>
          </Properties>
          <CachedDisplayName>XML assembler</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
    <Stage CategoryId=""9d0e4108-4cce-4536-83fa-4a5040674ad6"">
      <Components>
        <Component>
          <Name>Microsoft.BizTalk.Component.MIME_SMIME_Encoder</Name>
          <ComponentName>MIME/SMIME encoder</ComponentName>
          <Description>MIME/SMIME encoder component.</Description>
          <Version>1.0</Version>
          <Properties>
            <Property Name=""SendBodyPartAsAttachment"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""ContentTransferEncoding"">
              <Value xsi:type=""xsd:string"">base64</Value>
            </Property>
            <Property Name=""SignatureType"">
              <Value xsi:type=""xsd:int"">2</Value>
            </Property>
            <Property Name=""EnableEncryption"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
            <Property Name=""EncryptionAlgorithm"">
              <Value xsi:type=""xsd:int"">0</Value>
            </Property>
            <Property Name=""AddSigningCertToMessage"">
              <Value xsi:type=""xsd:boolean"">true</Value>
            </Property>
            <Property Name=""ValidateCRL"">
              <Value xsi:type=""xsd:boolean"">false</Value>
            </Property>
          </Properties>
          <CachedDisplayName>MIME/SMIME encoder</CachedDisplayName>
          <CachedIsManaged>true</CachedIsManaged>
        </Component>
      </Components>
    </Stage>
  </Stages>
</Document>";

            RunPipelineDemo(sampleSendXml);

            Console.WriteLine();
            Console.WriteLine("All demos completed successfully!");
        }

        static void RunPipelineDemo(string pipelineXml)
        {
            try
            {
                PipelineParser parser = new PipelineParser();
                PipelineDocument pipeline = parser.ParsePipelineXml(pipelineXml);
                
                parser.DisplayPipelineInfo(pipeline);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Generates an Azure Logic Apps Standard workflow JSON from a BizTalk pipeline file.
        /// Performs end-to-end conversion: Parse → Map → Generate JSON → Save.
        /// </summary>
        /// <param name="pipelineFile">Path to the BizTalk pipeline (.btp) file.</param>
        /// <param name="outputDir">Directory where the workflow.json will be saved.</param>
        /// <param name="batchMode">If true, runs in non-interactive mode (no prompts).</param>
        static void GenerateWorkflow(string pipelineFile, string outputDir, bool batchMode = false)
        {
            if (!batchMode)
            {
                Console.WriteLine("=================================================================");
                Console.WriteLine("  BizTalk Pipeline to Azure Logic Apps Workflow Generator");
                Console.WriteLine("=================================================================");
                Console.WriteLine();
            }

            try
            {
                // Validate input file
                if (!File.Exists(pipelineFile))
                {
                    if (!batchMode)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ERROR: Pipeline file not found: " + pipelineFile);
                        Console.ResetColor();
                        Console.WriteLine();
                        Console.WriteLine("Press any key to exit...");
                        Console.ReadKey();
                    }
                    throw new FileNotFoundException("Pipeline file not found: " + pipelineFile, pipelineFile);
                }

                // Create output directory if it doesn't exist
                if (!Directory.Exists(outputDir))
                {
                    if (!batchMode)
                    {
                        Console.WriteLine("Creating output directory: " + outputDir);
                    }
                    Directory.CreateDirectory(outputDir);
                    if (!batchMode)
                    {
                        Console.WriteLine();
                    }
                }

                // STEP 1: Parse the pipeline
                if (!batchMode)
                {
                    Console.WriteLine("STEP 1: Parsing BizTalk pipeline...");
                    Console.WriteLine("  File: " + Path.GetFileName(pipelineFile));
                }
                
                var parser = new PipelineParser();
                var pipeline = parser.ParsePipelineFile(pipelineFile);
                
                if (!batchMode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✓ Pipeline parsed successfully");
                    Console.ResetColor();
                    Console.WriteLine("  Type: " + pipeline.GetPipelineType());
                    Console.WriteLine("  Stages: " + pipeline.Stages.Count);
                    
                    int totalComponents = pipeline.Stages.Sum(s => s.Components.Count);
                    Console.WriteLine("  Components: " + totalComponents);
                    Console.WriteLine();
                }

                // STEP 2: Map to workflow model
                if (!batchMode)
                {
                    Console.WriteLine("STEP 2: Mapping pipeline to workflow model...");
                }
                
                // Use the filename (without extension) as the default workflow name
                var defaultWorkflowName = Path.GetFileNameWithoutExtension(pipelineFile);
                var workflow = PipelineWorkflowMapper.MapPipelineToWorkflow(pipeline, defaultWorkflowName);
                
                if (!batchMode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✓ Workflow model created");
                    Console.ResetColor();
                    Console.WriteLine("  Workflow name: " + workflow.Name);
                    Console.WriteLine("  Triggers: " + workflow.Triggers.Count);
                    Console.WriteLine("  Actions: " + workflow.Actions.Count);
                    Console.WriteLine();
                }

                // STEP 3: Generate JSON
                if (!batchMode)
                {
                    Console.WriteLine("STEP 3: Generating Logic Apps workflow JSON...");
                }
                
                var json = PipelineJSONGenerator.GenerateWorkflowJSON(workflow, "Stateful");
                
                if (!batchMode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✓ JSON generated successfully");
                    Console.ResetColor();
                    Console.WriteLine("  Size: " + json.Length + " characters");
                    Console.WriteLine();
                }

                // STEP 4: Save to file
                if (!batchMode)
                {
                    Console.WriteLine("STEP 4: Saving workflow to file...");
                }
                
                var outputFileName = workflow.Name + "_workflow.json";
                var outputPath = Path.Combine(outputDir, outputFileName);
                
                File.WriteAllText(outputPath, json);
                
                if (!batchMode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✓ SUCCESS!");
                    Console.ResetColor();
                    Console.WriteLine();
                    Console.WriteLine("=================================================================");
                    Console.WriteLine("  Workflow saved to:");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("  " + outputPath);
                    Console.ResetColor();
                    Console.WriteLine("=================================================================");
                    Console.WriteLine();
                    
                    // Display next steps
                    Console.WriteLine("Next Steps:");
                    Console.WriteLine("  1. Review the generated workflow.json file");
                    Console.WriteLine("  2. Configure Integration Account for schemas (if using XML/FF)");
                    Console.WriteLine("  3. Deploy to Azure Logic Apps Standard");
                    Console.WriteLine("  4. Test with sample messages");
                    Console.WriteLine();
                    
                    // Offer to display the JSON
                    Console.WriteLine("Would you like to see the generated JSON? (y/n): ");
                    var response = Console.ReadKey();
                    Console.WriteLine();
                    
                    if (response.KeyChar == 'y' || response.KeyChar == 'Y')
                    {
                        Console.WriteLine();
                        Console.WriteLine("=================================================================");
                        Console.WriteLine("  Generated Workflow JSON:");
                        Console.WriteLine("=================================================================");
                        Console.WriteLine(json);
                        Console.WriteLine("=================================================================");
                    }
                }
                else
                {
                    // Batch mode - write to stderr so stdout (MCP JSON channel) is not polluted
                    Console.Error.WriteLine($"✓ SUCCESS: {Path.GetFileName(pipelineFile)} → {outputFileName}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Stack Trace:");
                Console.Error.WriteLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Inner Exception:");
                    Console.Error.WriteLine(ex.InnerException.Message);
                }

                if (!batchMode)
                {
                    Console.WriteLine();
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }

                // Re-throw so callers (e.g. MCP tool handler) receive the exception
                // instead of the process being terminated via Environment.Exit.
                throw;
            }

            if (!batchMode)
            {
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}
