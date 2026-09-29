using System;
using System.Collections.Generic;
using System.Drawing;
using HopperWire;
using GH_IO.Serialization;
using Grasshopper.Kernel;

int assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new Exception(description);
}

var groupA = Guid.NewGuid();
var groupB = Guid.NewGuid();
Check(WireLayoutRules.DecideDisplay(1600, 800, 1500, true) == WireDecision.Hidden, "Length hides despite layout fainting");
Check(WireLayoutRules.DecideDisplay(1500, 800, 1500, false) == WireDecision.Faint, "Hidden threshold is strict");
Check(WireLayoutRules.DecideDisplay(800, 800, 1500, true) == WireDecision.Faint, "Layout faints a short wire");
Check(WireLayoutRules.DecideDisplay(800, 800, 1500, false) == WireDecision.Default, "Faint threshold is strict");
Check(!WireLayoutRules.CrossesGroupBoundary(null, null), "Ungrouped wires stay local");
Check(WireLayoutRules.CrossesGroupBoundary(new HashSet<Guid> { groupA }, null), "Group exit is a boundary");
Check(WireLayoutRules.CrossesGroupBoundary(null, new HashSet<Guid> { groupA }), "Group entry is a boundary");
Check(WireLayoutRules.CrossesGroupBoundary(new HashSet<Guid> { groupA }, new HashSet<Guid> { groupB }), "Distinct groups form a boundary");
Check(!WireLayoutRules.CrossesGroupBoundary(new HashSet<Guid> { groupA, groupB }, new HashSet<Guid> { groupA }), "Shared group stays local");

Check(WireLayoutRules.IsBackward(new PointF(20, 10), new PointF(10, 20)), "Right-to-left path is backward");
Check(!WireLayoutRules.IsBackward(new PointF(10, 10), new PointF(20, 20)), "Left-to-right path is forward");
Check(!WireLayoutRules.IsBackward(new PointF(10, 10), new PointF(10, 20)), "Vertical path is not backward");

var node = new RectangleF(5, 5, 10, 10);
Check(WireLayoutRules.PassesThroughRectangle(new[] { new PointF(0, 10), new PointF(20, 10) }, node), "Segment crossing component interior");
Check(WireLayoutRules.PassesThroughRectangle(new[] { new PointF(7, 7), new PointF(20, 10) }, node), "Segment starting inside component");
Check(!WireLayoutRules.PassesThroughRectangle(new[] { new PointF(0, 5), new PointF(20, 5) }, node), "Touching component edge is not a pass-through");
Check(!WireLayoutRules.PassesThroughRectangle(new[] { new PointF(0, 0), new PointF(20, 0) }, node), "Passing above component");
Check(!WireLayoutRules.PassesThroughRectangle(new[] { new PointF(0, 0), new PointF(5, 5) }, node), "Touching component corner");

WireDisplayState Reopen(WireDisplayState state, bool binary = false)
{
    var saved = new GH_LooseChunk("HopperWire");
    state.Write(saved);
    var loaded = new GH_LooseChunk("HopperWire");
    if (binary) loaded.Deserialize_Binary(saved.Serialize_Binary());
    else loaded.Deserialize_Xml(saved.Serialize_Xml());
    var reopened = new WireDisplayState();
    reopened.Read(loaded);
    return reopened;
}

foreach (bool binary in new[] { false, true })
{
    var id = Guid.NewGuid();
    var state = new WireDisplayState();
    var mode = state.Resolve(id, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.hidden);
    Check(mode == GH_ParamWireDisplay.hidden, "Long wire becomes hidden");
    state = Reopen(state, binary);
    mode = state.Resolve(id, mode, GH_ParamWireDisplay.faint);
    Check(mode == GH_ParamWireDisplay.faint, "Reopened hidden wire becomes faint after moving closer");
    state = Reopen(state, binary);
    mode = state.Resolve(id, mode, GH_ParamWireDisplay.@default);
    Check(mode == GH_ParamWireDisplay.@default, "Reopened wire restores default after moving closer again");
    Check(state.ManagedTargets.Count == 0, "Restored wire releases ownership");

    state.Resolve(id, GH_ParamWireDisplay.faint, GH_ParamWireDisplay.hidden);
    state = Reopen(state, binary);
    Check(state.Resolve(id, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.faint,
        "Reopening preserves a user's original faint setting");

    state.Resolve(id, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.faint);
    state = Reopen(state, binary);
    Check(state.Resolve(id, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.hidden,
        "Manual hidden override survives reopening and refresh");
    Check(state.ManagedTargets.Count == 0, "Manual override releases ownership");

    state.Resolve(id, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.hidden);
    state = Reopen(state, binary);
    Check(state.TryGetOriginal(id, GH_ParamWireDisplay.hidden, out var original) && original == GH_ParamWireDisplay.@default,
        "Disconnected target can restore its original display after reopening");
    Check(!state.TryGetOriginal(id, GH_ParamWireDisplay.faint, out _), "Disconnect cleanup respects manual changes");
}

var legacy = new WireDisplayState();
legacy.Read(new GH_LooseChunk("HopperWire"));
Check(legacy.Resolve(Guid.NewGuid(), GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.hidden,
    "Legacy files retain display settings when ownership is unknown");
var live = new WireDisplayState();
var liveId = Guid.NewGuid();
var liveMode = GH_ParamWireDisplay.@default;
foreach (var requested in new[] { GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.faint, GH_ParamWireDisplay.@default,
    GH_ParamWireDisplay.faint, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default })
{
    liveMode = live.Resolve(liveId, liveMode, requested);
    Check(liveMode == requested, "Repeated moves update display in the same session");
}

Console.WriteLine($"Passed {assertions} wire layout and display-state assertions.");
