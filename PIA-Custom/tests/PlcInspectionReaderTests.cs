using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Pia.Custom;

internal static class PlcInspectionReaderTests
{
    internal static void Run()
    {
        ReadsNestedMemberFromReferencedType();
        SeparatesWiredPinsFromOpenPins();
    }

    private static void ReadsNestedMemberFromReferencedType()
    {
        var db = XElement.Parse("""
            <Sections xmlns="urn:example:interface">
              <Section Name="Static">
                <Member Name="Sequence" Datatype="Struct">
                  <Member Name="Process" Datatype="&quot;UDT_TestSequence&quot;" />
                </Member>
              </Section>
              <Section Name="Constant">
                <Member Name="StationId" Datatype="WString"><StartValue>WSTRING#'A1'</StartValue></Member>
              </Section>
            </Sections>
            """);
        var type = XElement.Parse("""
            <Sections xmlns="urn:example:interface">
              <Section Name="None">
                <Member Name="StepNo" Datatype="Int" />
              </Section>
            </Sections>
            """);
        string[] path = { "sequence", "Process", "StepNo" };
        var result = PlcInterfaceReader.ResolveMember(db, path, "", name =>
        {
            Require(name == "\"UDT_TestSequence\"", "Resolve the type referenced by the declaration.");
            return (type, null);
        });
        Require(Read<string>(result, "status") == "found", "Resolve the exact nested member.");
        var declaration = Read<PlcInterfaceReader.Declaration>(result, "declaration");
        Require(declaration.Path == "Sequence.Process.StepNo" && declaration.DataType == "Int", "Return the canonical path and type.");
        Require(declaration.TypeChain.SequenceEqual(new[] { "\"UDT_TestSequence\"" }), "Expose the UDT that proves the type.");
        var unresolved = PlcInterfaceReader.ResolveMember(db, path, "", _ => (null, "Type is ambiguous."));
        Require(Read<string>(unresolved, "status") == "typeResolutionIncomplete", "An unknown type must not be reported as a missing member.");
        Require(PlcInterfaceReader.ReadDeclarations(db, "Constant").Single().StartValue == "WSTRING#'A1'",
            "Read a local constant's declared value.");
    }

    private static void SeparatesWiredPinsFromOpenPins()
    {
        var block = XElement.Parse("""
            <SW.Blocks.FB><ObjectList>
              <SW.Blocks.CompileUnit><AttributeList><NetworkSource><StructuredText /></NetworkSource></AttributeList></SW.Blocks.CompileUnit>
              <SW.Blocks.CompileUnit>
                <AttributeList><NetworkSource><FlgNet xmlns="urn:example:network">
                  <Parts>
                    <Access UId="1" Scope="GlobalVariable"><Symbol><Component Name="Settings" /><Component Name="Station" /></Symbol></Access>
                    <Call UId="2"><CallInfo Name="FB_Test" BlockType="FB">
                      <Instance Scope="LocalVariable"><Component Name="TestInstance" /></Instance>
                      <Parameter Name="StationName" Section="Input" Type="WString" />
                      <Parameter Name="Flag" Section="Input" Type="Bool" />
                    </CallInfo></Call>
                  </Parts>
                  <Wires>
                    <Wire><IdentCon UId="1" /><NameCon UId="2" Name="StationName" /></Wire>
                    <Wire><OpenCon UId="3" /><NameCon UId="2" Name="Flag" /></Wire>
                  </Wires>
                </FlgNet></NetworkSource></AttributeList>
                <ObjectList><MultilingualText CompositionName="Title"><ObjectList><MultilingualTextItem>
                  <AttributeList><Culture>de-DE</Culture><Text>Station call</Text></AttributeList>
                </MultilingualTextItem></ObjectList></MultilingualText></ObjectList>
              </SW.Blocks.CompileUnit>
            </ObjectList></SW.Blocks.FB>
            """);
        var unread = new List<int>();
        var calls = PlcCallReader.Read(block, 0, "FB_Test", unread, out int count);
        var call = calls.Single();
        Require(call.Network == 2 && count == 2 && call.NetworkTitle == "Station call", "Preserve network position and title.");
        Require(unread.SequenceEqual(new[] { 1 }), "An unparsed SCL network makes an all-networks scan incomplete.");
        var stationPin = call.Pins.Single(pin => Read<string>(pin, "name") == "StationName");
        var stationConnection = Read<object[]>(stationPin, "connections").Single();
        Require(Read<string>(stationConnection, "kind") == "symbol" &&
            Read<string[]>(stationConnection, "components").SequenceEqual(new[] { "Settings", "Station" }),
            "Preserve the exact wired symbol components.");
        var flagPin = call.Pins.Single(pin => Read<string>(pin, "name") == "Flag");
        Require(Read<string>(Read<object[]>(flagPin, "connections").Single(), "kind") == "open",
            "Distinguish a deliberately open pin from an unreadable operand.");
        unread.Clear();
        PlcCallReader.Read(block, 2, "FB_Test", unread, out _);
        Require(unread.Count == 0, "A targeted FBD lookup is complete even if another network is SCL.");
    }

    private static T Read<T>(object item, string property) => (T)item.GetType().GetProperty(property)!.GetValue(item)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
