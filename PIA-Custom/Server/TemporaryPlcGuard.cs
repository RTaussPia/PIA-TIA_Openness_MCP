using System.Xml.Linq;

namespace Pia.Custom;

internal static class TemporaryPlcGuard
{
    internal const string BlockName = "FB_DeleteTempFolder";
    internal const string MissingVariable = "DeletePiaTempFolderBeforeCompiling";
    internal const string NetworkTitle = "Remove temporary PLC blocks before compiling";

    internal static XDocument Document() => XDocument.Parse($"""
        <Document>
          <Engineering version="V21" />
          <DocumentInfo><ExportSetting>None</ExportSetting><InstalledProducts /></DocumentInfo>
          <SW.Blocks.FB ID="0">
            <AttributeList>
              <Interface>
                <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                  <Section Name="Input" /><Section Name="Output" /><Section Name="InOut" />
                  <Section Name="Static" /><Section Name="Temp" /><Section Name="Constant" />
                </Sections>
              </Interface>
              <MemoryLayout>Optimized</MemoryLayout>
              <Name>{BlockName}</Name>
              <Namespace />
              <ProgrammingLanguage>SCL</ProgrammingLanguage>
              <SetENOAutomatically>false</SetENOAutomatically>
            </AttributeList>
            <ObjectList>
              <SW.Blocks.CompileUnit ID="1" CompositionName="CompileUnits">
                <AttributeList>
                  <NetworkSource>
                    <StructuredText xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4">
                      <Access Scope="LocalVariable" UId="21"><Symbol UId="22"><Component Name="{MissingVariable}" UId="23" /></Symbol></Access>
                      <Blank UId="24" /><Token Text=":=" UId="25" /><Blank UId="26" />
                      <Access Scope="LiteralConstant" UId="27"><Constant UId="28"><ConstantValue UId="29">TRUE</ConstantValue></Constant></Access>
                      <Token Text=";" UId="30" /><NewLine UId="31" />
                    </StructuredText>
                  </NetworkSource>
                  <ProgrammingLanguage>SCL</ProgrammingLanguage>
                </AttributeList>
                <ObjectList>
                  <MultilingualText ID="2" CompositionName="Title">
                    <ObjectList><MultilingualTextItem ID="3" CompositionName="Items">
                      <AttributeList><Culture>en-US</Culture><Text>{NetworkTitle}</Text></AttributeList>
                    </MultilingualTextItem></ObjectList>
                  </MultilingualText>
                </ObjectList>
              </SW.Blocks.CompileUnit>
            </ObjectList>
          </SW.Blocks.FB>
        </Document>
        """);
}
