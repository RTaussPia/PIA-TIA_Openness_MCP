using System;
using System.Collections;
using System.Linq;
using System.Xml.Linq;
using Pia.Custom;

internal static class PlcGraphTests
{
    internal static void Run()
    {
        TestTemporaryGuard();
        TestControlCommentAdditions();
        var block = XElement.Parse("""
            <SW.Blocks.FB ID="0"><AttributeList><Interface><Sections xmlns="urn:interface"><Section Name="Static"><Member Name="Existing" Datatype="Bool" /></Section></Sections></Interface></AttributeList>
            <ObjectList><SW.Blocks.CompileUnit ID="1"><AttributeList><NetworkSource>
              <Graph xmlns="urn:graph"><PreOperations /><Sequence>
                <Steps><Step Number="1" Name="Init" Init="true"><Actions><Action /></Actions></Step><Step Number="3" Name="End" />
                  <Step Number="2" Name="Action"><Actions><Action Qualifier="N"><Access Scope="LocalVariable"><Symbol><Component Name="Existing" /></Symbol></Access></Action></Actions></Step></Steps>
                <Transitions><Transition Number="7" Name="Go"><FlgNet xmlns="urn:flg"><Parts /><Wires /></FlgNet></Transition><Transition Number="8" Name="Finish" /></Transitions>
                <Branches><Branch Number="1" Type="AltBegin" Cardinality="2" /></Branches>
                <Connections>
                  <Connection><NodeFrom><StepRef Number="1" /></NodeFrom><NodeTo><TransitionRef Number="7" /></NodeTo><LinkType>Direct</LinkType></Connection>
                  <Connection><NodeFrom><TransitionRef Number="7" /></NodeFrom><NodeTo><StepRef Number="2" /></NodeTo><LinkType>Direct</LinkType></Connection>
                  <Connection><NodeFrom><StepRef Number="2" /></NodeFrom><NodeTo><BranchRef Number="1" In="0" /></NodeTo><LinkType>Direct</LinkType></Connection>
                  <Connection><NodeFrom><BranchRef Number="1" Out="0" /></NodeFrom><NodeTo><TransitionRef Number="8" /></NodeTo><LinkType>Direct</LinkType></Connection>
                  <Connection><NodeFrom><TransitionRef Number="8" /></NodeFrom><NodeTo><StepRef Number="3" /></NodeTo><LinkType>Direct</LinkType></Connection>
                  <Connection><NodeFrom><StepRef Number="3" /></NodeFrom><NodeTo><StepRef Number="1" /></NodeTo><LinkType>Jump</LinkType></Connection>
                </Connections>
              </Sequence><PostOperations><PermanentOperation ProgrammingLanguage="FBD" /></PostOperations></Graph>
            </NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB>
            """);
        var graph = PlcGraphReader.FindGraph(block)!;
        var range = PlcGraphFlagChange.ReadRange(graph, 1, 1, 2);
        Require(range.Select(step => step.Number).SequenceEqual(new[] { 1, 2 }), "Follow connected steps instead of XML order.");
        Reject(() => PlcGraphFlagChange.ReadRange(graph, 1, 1, 3), "Reject a branch inside the write range.");
        Reject(() => PlcGraphFlagChange.ReadRange(graph, 1, 3, 1), "Reject a jump inside the write range.");
        var firstPage = PlcGraphReader.Read(graph, 1, "flow", null, 0, 5);
        Require(Field(firstPage, "complete") is true && Field(firstPage, "nextOffset") is 5, "Return a bounded page of GRAPH links.");
        var lastPage = PlcGraphReader.Read(graph, 1, "flow", null, 5, 5);
        var jump = ((IEnumerable)Field(lastPage, "items")!).Cast<PlcGraphReader.Edge>().Single();
        Require(jump.Link == "Jump" && jump.From.Number == 3 && jump.To.Number == 1, "Preserve the return jump in the readable flow.");

        var original = new XElement(block);
        Reject(() => PlcGraphFlagChange.Append(block, graph, range, "Existing", "Title", "Comment", new[] { "en-US" }),
            "Never overwrite an existing declaration.");
        Require(PlcGraphFlagChange.SameBlock(original, block), "A rejected plan leaves the block untouched.");
        PlcGraphFlagChange.Append(block, graph, range, "InRange", "Set active range", "True in the selected steps.", new[] { "en-US" });
        var operations = PlcGraphReader.Elements(graph, "PostOperations", "PermanentOperation");
        Require(operations.Length == 2 && operations[1].Descendants().Count(item => (string?)item.Attribute("Name") == "X") == 2,
            "Keep existing operations and read both selected active states in the new operation.");
        var member = block.Descendants().Single(element => (string?)element.Attribute("Name") == "InRange" && element.Name.LocalName == "Member");
        Require((string?)member.Attribute("Datatype") == "Bool" && member.Value == "True in the selected steps.", "Include the approved BOOL comment.");
        var unexpected = new XElement(block);
        unexpected.Descendants().Single(item => item.Name.LocalName == "Transition" && (int?)item.Attribute("Number") == 7)
            .SetAttributeValue("Name", "Changed");
        Require(!PlcGraphFlagChange.SameBlock(block, unexpected), "Reject changes to an existing transition during readback.");
        Require(PlcGraphFlagChange.DescribeDifference(block, unexpected)?.EndsWith("/Transitions/Transition.", StringComparison.Ordinal) == true,
            "Identify the changed transition without exposing attribute values.");
        var unexpectedAction = new XElement(block);
        unexpectedAction.Descendants().Single(item => item.Name.LocalName == "Action" && (string?)item.Attribute("Qualifier") == "N")
            .SetAttributeValue("Qualifier", "R");
        Require(!PlcGraphFlagChange.SameBlock(block, unexpectedAction), "Reject changes to an existing action during readback.");
        operations[1].Remove();
        member.Remove();
        Require(PlcGraphFlagChange.SameBlock(original, block), "The mutation consists only of the declaration and post-operation.");
        TestStepActions(block, graph, original, range);
        TestGlobalStepActions(block, graph, range);
        ConfirmBranches("SimBegin", "SimEnd");
        ConfirmBranches("AltBegin", "AltEnd");
    }

    private static void TestGlobalStepActions(XElement block, XElement graph, (int Number, string Name)[] range)
    {
        var db = XElement.Parse("""
            <SW.Blocks.GlobalDB><AttributeList><Interface><Sections><Section Name="Static">
              <Member Name="Logistics" Datatype="Struct"><Member Name="Existing" Datatype="Bool"><StartValue>true</StartValue></Member></Member>
              <Member Name="Shared" Datatype="&quot;SomeType&quot;"/>
            </Section></Sections></Interface></AttributeList></SW.Blocks.GlobalDB>
            """);
        var originalDb = new XElement(db);
        Reject(() => PlcGraphFlagChange.AppendDataFlagDeclaration(db, new[] { "Shared" }, "InRange", "Active step.", new[] { "en-US" }),
            "Do not change a referenced type.");
        var path = PlcGraphFlagChange.AppendDataFlagDeclaration(db, new[] { "logistics" }, "InRange", "Active step.", new[] { "en-US" });
        Require(path.SequenceEqual(new[] { "Logistics", "InRange" }), "Use the data block's actual member spelling.");
        Reject(() => PlcGraphFlagChange.AppendDataFlagDeclaration(db, new[] { "Logistics" }, "InRange", "Active step.", new[] { "en-US" }),
            "Do not replace an existing data block member.");

        XNamespace ns = graph.Name.Namespace;
        graph.Descendants(ns + "Actions").First().AddFirst(new XElement(ns + "Action", new XAttribute("Qualifier", "N"),
            new XElement(ns + "Access", new XAttribute("Scope", "GlobalVariable"), new XElement(ns + "Symbol",
                new XElement(ns + "Component", new XAttribute("Name", "\"StationFlags\"")),
                new XElement(ns + "Token", new XAttribute("Text", ".")),
                new XElement(ns + "Component", new XAttribute("Name", "Logistics")),
                new XElement(ns + "Token", new XAttribute("Text", ".")),
                new XElement(ns + "Component", new XAttribute("Name", "Existing"))))));
        var originalGraph = new XElement(block);
        PlcGraphFlagChange.AppendGlobalStepActions(graph, 1, range, "StationFlags", path);
        var actions = graph.Descendants(ns + "Action").Where(node => node.Descendants(ns + "Component")
            .Any(component => (string?)component.Attribute("Name") == "InRange")).ToArray();
        Require(actions.Length == 2 && actions.All(action => action.Descendants(ns + "Component").Select(component =>
            (string?)component.Attribute("Name")).SequenceEqual(new[] { "\"StationFlags\"", "Logistics", "InRange" })) &&
            actions.All(action => action.Descendants(ns + "Token").Count(token => (string?)token.Attribute("Text") == ".") == 2),
            "Use the verified global operand structure in all selected steps.");
        foreach (var action in actions) action.Remove();
        db.Descendants("Member").Single(node => (string?)node.Attribute("Name") == "InRange").Remove();
        Require(PlcGraphFlagChange.SameBlock(originalGraph, block) && PlcGraphFlagChange.SameBlock(originalDb, db),
            "The external flag changes only the data member and the selected step actions.");

        var originalOperand = graph.Descendants(ns + "Symbol").First();
        originalOperand.ReplaceNodes(new XElement(ns + "Component", new XAttribute("Name", "\"StationFlags\".Logistics.Existing")));
        PlcGraphFlagChange.AppendGlobalStepActions(graph, 1, range, "StationFlags", path);
        Require(graph.Descendants(ns + "Component").Count(component =>
            (string?)component.Attribute("Name") == "\"StationFlags\".Logistics.InRange") == 2,
            "Retain the single-component representation used for qualified GRAPH operands.");

        foreach (var action in graph.Descendants(ns + "Action").Where(action => action.Element(ns + "Access") != null).ToArray())
            action.Remove();
        graph.Descendants(ns + "Actions").First().AddFirst(new XElement(ns + "Action", new XAttribute("Qualifier", "N"),
            new XElement(ns + "Token", new XAttribute("Text", "\"StationFlags\".Logistics.Request.Existing")),
            new XElement(ns + "Token", new XAttribute("Text", "\n"))));
        PlcGraphFlagChange.AppendGlobalStepActions(graph, 1, range, "StationFlags", path);
        var tokenActions = graph.Descendants(ns + "Action").Where(action => action.Elements(ns + "Token")
            .Any(token => (string?)token.Attribute("Text") == "\"StationFlags\".Logistics.InRange")).ToArray();
        Require(tokenActions.Length == 2 && tokenActions.All(action => action.Elements().Count() == 2 &&
            (string?)action.Elements(ns + "Token").Last().Attribute("Text") == "\n"),
            "Use the exported direct token format for a qualified GRAPH action operand.");
        var comparisonCopy = new XElement(graph);
        Require(PlcGraphFlagChange.RetargetGlobalStepActions(comparisonCopy, "StationFlags", path, "TemporaryFlags") == 2 &&
            comparisonCopy.Descendants(ns + "Token").Count(token => (string?)token.Attribute("Text") == "\"TemporaryFlags\".Logistics.InRange") == 2 &&
            comparisonCopy.Descendants(ns + "Token").Any(token => (string?)token.Attribute("Text") == "\"StationFlags\".Logistics.Request.Existing"),
            "Redirect only the approved new references in the comparison copy.");
        Require(graph.Descendants(ns + "Token").Count(token => (string?)token.Attribute("Text") == "\"StationFlags\".Logistics.InRange") == 2,
            "Preserve the original target in the final write plan.");

        var copyAttributes = XElement.Parse("<SW.Blocks.GlobalDB><AttributeList><Name>Copy</Name><Number>200</Number>" +
            "<!-- export metadata --> <MemoryLayout>Optimized</MemoryLayout><Namespace /></AttributeList></SW.Blocks.GlobalDB>", LoadOptions.PreserveWhitespace);
        var sourceAttributes = XElement.Parse("<SW.Blocks.GlobalDB><AttributeList><Namespace /><Name>Original</Name><Number>1</Number>" +
            "<MemoryLayout>Optimized</MemoryLayout></AttributeList></SW.Blocks.GlobalDB>");
        Require(PlcGraphFlagChange.SameBlock(PlcGraphFlagChange.ForComparison(copyAttributes, true), PlcGraphFlagChange.ForComparison(sourceAttributes, true)),
            "Ignore only the copy identity and formatting of top-level block attributes.");
        Require(!PlcGraphFlagChange.SameBlock(PlcGraphFlagChange.ForComparison(copyAttributes), PlcGraphFlagChange.ForComparison(sourceAttributes)),
            "Retain the block identity in a comparison of the original.");
        copyAttributes.Descendants("MemoryLayout").Single().Value = "Standard";
        Require(!PlcGraphFlagChange.SameBlock(PlcGraphFlagChange.ForComparison(copyAttributes, true), PlcGraphFlagChange.ForComparison(sourceAttributes, true)),
            "Preserve actual block attributes in the comparison.");
        var comments = XElement.Parse("<FB><Comment><MultiLanguageText Lang='de-DE'>A</MultiLanguageText>" +
            "<MultiLanguageText Lang='en-US'>B</MultiLanguageText></Comment></FB>");
        var reorderedComments = new XElement(comments);
        var comment = reorderedComments.Element("Comment")!;
        comment.ReplaceNodes(comment.Elements().Reverse().ToArray());
        Require(PlcGraphFlagChange.SameBlock(PlcGraphFlagChange.ForComparison(comments), PlcGraphFlagChange.ForComparison(reorderedComments)),
            "Permit Siemens to reorder translations without changing the comment associated with each language.");
        var changedLocale = new XElement(comments);
        changedLocale.Descendants("MultiLanguageText").First().SetAttributeValue("Lang", "de-AT");
        Require(PlcGraphFlagChange.DescribeDifference(PlcGraphFlagChange.ForComparison(comments), PlcGraphFlagChange.ForComparison(changedLocale))?
            .EndsWith("expected de-DE, found de-AT.", StringComparison.Ordinal) == true,
            "Reject a change in the assigned language even when the comment text stays the same.");
        comment.Elements().First().Value = "Changed";
        Require(!PlcGraphFlagChange.SameBlock(PlcGraphFlagChange.ForComparison(comments), PlcGraphFlagChange.ForComparison(reorderedComments)),
            "Preserve the text of every approved comment.");
    }

    private static void TestTemporaryGuard()
    {
        var block = TemporaryPlcGuard.Document().Root!.Element("SW.Blocks.FB")!;
        var attributes = block.Element("AttributeList")!;
        Require(attributes.Element("Name")!.Value == "FB_DeleteTempFolder" && attributes.Element("Number") == null,
            "Give the temporary guard its required name and let TIA assign an unused number.");
        Require(attributes.Element("Namespace")?.Value == "", "Include the namespace identifier required by V21 Openness.");
        var network = block.Element("ObjectList")!.Elements("SW.Blocks.CompileUnit").Single();
        Require(network.Descendants().Any(node => node.Name.LocalName == "Component" && (string?)node.Attribute("Name") == TemporaryPlcGuard.MissingVariable) &&
            !attributes.Descendants().Any(node => node.Name.LocalName == "Member" && (string?)node.Attribute("Name") == TemporaryPlcGuard.MissingVariable),
            "The first network must use the deliberately undeclared local variable.");
        Require(network.Descendants("Text").Any(text => text.Value == TemporaryPlcGuard.NetworkTitle), "Give the guard network a clear title.");
    }

    private static void TestControlCommentAdditions()
    {
        var source = XElement.Parse("""
            <SW.Blocks.FB><AttributeList><Interface><Sections><Section Name="Input">
              <Member Name="First"><Comment><MultiLanguageText Lang="de-DE">Original</MultiLanguageText></Comment></Member>
              <Member Name="Second"><Comment><MultiLanguageText Lang="de-DE">Second</MultiLanguageText></Comment></Member>
            </Section></Sections></Interface></AttributeList></SW.Blocks.FB>
            """);
        var control = new XElement(source);
        var added = new XElement("MultiLanguageText", new XAttribute("Lang", "en-US"), "English");
        control.Descendants("Comment").First().Add(added);
        var observed = PlcGraphImportComparison.FindCommentAdditions(source, control);
        Require(observed.Length == 1 && PlcGraphImportComparison.Difference(source, control, controlAdditions: observed) == null,
            "Accept exactly the additional translation observed at this interface member in the control copy.");
        control.Descendants("MultiLanguageText").First().Value = "Changed";
        Require(PlcGraphImportComparison.Difference(source, control, controlAdditions: observed) != null,
            "Reject changes to the original translation even if a control addition is present.");
        control.Descendants("MultiLanguageText").First().Value = "Original";
        added.Value = "Different English";
        Require(PlcGraphImportComparison.Difference(source, control, controlAdditions: observed) != null,
            "Reject an unverified text in the added language.");
        added.Value = "English";
        added.Remove();
        control.Descendants("Comment").Last().Add(added);
        Require(PlcGraphImportComparison.Difference(source, control, controlAdditions: observed) != null,
            "Reject the same new translation at a different interface member.");
    }

    private static void TestStepActions(XElement block, XElement graph, XElement original, (int Number, string Name)[] range)
    {
        Reject(() => PlcGraphFlagChange.AppendStepActions(block, graph, 1, new[] { (1, "Init"), (3, "End") },
            "InRange", "True in the selected steps.", new[] { "en-US" }), "Refuse steps with missing action lists before editing.");
        Require(PlcGraphFlagChange.SameBlock(original, block), "A rejected action plan leaves the block untouched.");
        PlcGraphFlagChange.AppendStepActions(block, graph, 1, range, "InRange", "True in the selected steps.", new[] { "en-US" });
        var actions = block.Descendants().Where(node => node.Name.LocalName == "Action" && node.Descendants()
            .Any(component => (string?)component.Attribute("Name") == "InRange")).ToArray();
        Require(actions.Length == 2 && actions.All(action => (string?)action.Attribute("Qualifier") == "N" &&
            action.Descendants().Any(operand => (string?)operand.Attribute("Scope") == "LocalVariable")),
            "Append one non-storing action for the local flag in each selected step.");
        Require((int?)actions[0].Parent?.Parent?.Attribute("Number") == 1 && actions[0].NextNode is XElement empty && !empty.HasAttributes,
            "Keep the trailing empty action after the new action.");
        foreach (var action in actions) action.Remove();
        block.Descendants().Single(node => node.Name.LocalName == "Member" && (string?)node.Attribute("Name") == "InRange").Remove();
        Require(PlcGraphFlagChange.SameBlock(original, block), "A step-action plan changes only the selected action lists and one declaration.");
    }

    private static void ConfirmBranches(string opening, string closing)
    {
        var graph = XElement.Parse($"""
            <Graph><Sequence>
              <Steps><Step Number="1" Name="Begin"/><Step Number="2" Name="FirstArm"/><Step Number="9" Name="OtherArm"/><Step Number="3" Name="End"/></Steps>
              <Transitions><Transition Number="1"/><Transition Number="2"/><Transition Number="3"/><Transition Number="4"/></Transitions>
              <Branches><Branch Number="10" Type="{opening}" Cardinality="2"/><Branch Number="11" Type="{closing}" Cardinality="2"/></Branches>
              <Connections>{(opening == "SimBegin" ?
                 Link("StepRef", 1, "TransitionRef", 1) + Link("TransitionRef", 1, "BranchRef", 10, input: 0) +
                 Link("BranchRef", 10, "StepRef", 2, output: 0) + Link("BranchRef", 10, "StepRef", 9, output: 1) +
                 Link("StepRef", 2, "BranchRef", 11, input: 0) + Link("StepRef", 9, "BranchRef", 11, input: 1) +
                 Link("BranchRef", 11, "TransitionRef", 4, output: 0) + Link("TransitionRef", 4, "StepRef", 3) :
                 Link("StepRef", 1, "BranchRef", 10, input: 0) + Link("BranchRef", 10, "TransitionRef", 1, output: 0) +
                 Link("BranchRef", 10, "TransitionRef", 2, output: 1) + Link("TransitionRef", 1, "StepRef", 2) +
                 Link("TransitionRef", 2, "StepRef", 9) + Link("StepRef", 2, "TransitionRef", 3) +
                 Link("StepRef", 9, "TransitionRef", 4) + Link("TransitionRef", 3, "BranchRef", 11, input: 0) +
                 Link("TransitionRef", 4, "BranchRef", 11, input: 1) + Link("BranchRef", 11, "StepRef", 3, output: 0))}</Connections>
            </Sequence></Graph>
            """);
        try
        {
            PlcGraphFlagChange.ReadRange(graph, 1, 1, 3);
            throw new InvalidOperationException("The planner must request a decision for a branch.");
        }
        catch (PlcGraphFlagChange.BranchSelectionRequired ex)
        {
            Require(ex.Complete && ex.Branches.Length == 2 && ex.CandidateSteps.Length == 4,
                "The branch decision should include the split, merge, and all candidate steps.");
        }
        var selection = new[] { 1, 2, 3, 9 };
        Require(PlcGraphFlagChange.ReadRange(graph, 1, 1, 3, selection).Select(step => step.Number).SequenceEqual(selection),
            "Use the explicitly approved members of both arms.");
        Reject(() => PlcGraphFlagChange.ReadRange(graph, 1, 1, 3, new[] { 1, 2, 9 }), "Reject a selection missing the end step.");
        Reject(() => PlcGraphFlagChange.ReadRange(graph, 1, 1, 3, new[] { 1, 2, 3, 99 }), "Reject a step outside the range.");
        graph.Descendants("LinkType").Last().Value = "Jump";
        Reject(() => PlcGraphFlagChange.ReadRange(graph, 1, 1, 3, selection), "Approval does not override a jump in the range.");
    }

    private static string Link(string from, int fromNumber, string to, int toNumber, int? input = null, int? output = null) =>
        new XElement("Connection",
            new XElement("NodeFrom", new XElement(from, new XAttribute("Number", fromNumber), output.HasValue ? new XAttribute("Out", output.Value) : null)),
            new XElement("NodeTo", new XElement(to, new XAttribute("Number", toNumber), input.HasValue ? new XAttribute("In", input.Value) : null)),
            new XElement("LinkType", "Direct")).ToString(SaveOptions.DisableFormatting);

    private static object? Field(object result, string name) => result.GetType().GetProperty(name)?.GetValue(result);

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (FormatException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
