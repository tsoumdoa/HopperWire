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

GH_ParamWireDisplay Apply(WireDisplayState state, Guid id, GH_ParamWireDisplay current, GH_ParamWireDisplay requested)
{
    var result = current;
    state.Apply(id, current, requested, value => result = value);
    return result;
}

foreach (bool binary in new[] { false, true })
{
    var id = Guid.NewGuid();
    var state = new WireDisplayState();
    var mode = GH_ParamWireDisplay.@default;
    foreach (var requested in new[] { GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.faint,
        GH_ParamWireDisplay.@default, GH_ParamWireDisplay.faint, GH_ParamWireDisplay.hidden,
        GH_ParamWireDisplay.@default })
    {
        mode = Apply(state, id, mode, requested);
        Check(mode == requested, "Repeated moves apply the current layout across reopening");
        state = Reopen(state, binary);
        Check(state.ManagedTargets.Contains(id), "Managed input survives serialization even at Default");
    }

    // A wire-only undo restores Hidden after cleanup; the managed input must remain discoverable.
    mode = GH_ParamWireDisplay.hidden;
    foreach (var tracked in state.ManagedTargets)
        mode = Apply(state, tracked, mode, GH_ParamWireDisplay.@default);
    Check(mode == GH_ParamWireDisplay.@default, "Disconnected cleanup still works after undo and reopening");

    Apply(state, id, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.faint);
    state = Reopen(state, binary);
    Check(Apply(state, id, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.@default,
        "A differing manual value does not become a permanent preference");
    Apply(state, id, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.hidden);
    Check(Apply(state, id, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.@default,
        "A matching manual value follows the same layout policy");

    var saved = new GH_LooseChunk("HopperWire");
    state.Write(saved);
    var item = saved.FindChunk("ManagedWireDisplays").FindChunk("Target", 0);
    Check(item.GetInt32("Applied") == (int)GH_ParamWireDisplay.@default, "Last applied Default is persisted");
    Check(!item.ItemExists("Original"), "No inferred preference is written");
    state.Remove(id);
    Check(Reopen(state, binary).ManagedTargets.Count == 0, "Deleted input is removed from saved records");
}

// An archive from the previous version must not turn its Original value into a display floor.
var legacyId = Guid.NewGuid();
var oldArchive = new GH_LooseChunk("HopperWire");
var oldState = oldArchive.CreateChunk("ManagedWireDisplays");
oldState.SetInt32("Count", 1);
var oldTarget = oldState.CreateChunk("Target", 0);
oldTarget.SetGuid("Id", legacyId);
oldTarget.SetInt32("Original", (int)GH_ParamWireDisplay.faint);
oldTarget.SetInt32("Applied", (int)GH_ParamWireDisplay.hidden);
var legacy = new WireDisplayState();
legacy.Read(oldArchive);
Check(legacy.ManagedTargets.Contains(legacyId), "Version 1 managed inputs migrate");
Check(Apply(legacy, legacyId, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.@default,
    "Version 1 original preference is ignored");
legacy.Read(new GH_LooseChunk("HopperWire"));
Check(legacy.ManagedTargets.Count == 0, "Reading an older file clears stale ownership");
Check(Apply(legacy, legacyId, GH_ParamWireDisplay.hidden, GH_ParamWireDisplay.@default) == GH_ParamWireDisplay.@default,
    "Files without ownership records need no manual reset");

var metadata = new WireDisplayState();
var metadataId = Guid.NewGuid();
long revision = metadata.Revision;
Check(!metadata.Apply(metadataId, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.@default,
    _ => throw new Exception("Unchanged display must not create an undo action")), "No unnecessary display assignment");
Check(metadata.Revision != revision, "Acquiring an already-Default input still requires persisting metadata");
revision = metadata.Revision;
metadata.Apply(metadataId, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.@default, _ => { });
Check(metadata.Revision == revision, "An unchanged pass does not request another save");
metadata.Remove(metadataId);
Check(metadata.Revision != revision, "Pruning a deleted input requires persisting metadata");

var failed = new WireDisplayState();
bool threw = false;
try
{
    failed.Apply(Guid.NewGuid(), GH_ParamWireDisplay.@default, GH_ParamWireDisplay.hidden,
        _ => throw new InvalidOperationException("Display write failed"));
}
catch (InvalidOperationException) { threw = true; }
Check(threw && failed.ManagedTargets.Count == 0, "Failed display writes do not record an unapplied mode");

// Even if two historical records are present, their past modes cannot pin the current result.
var first = new WireDisplayState();
var second = new WireDisplayState();
var sharedId = Guid.NewGuid();
var sharedMode = Apply(first, sharedId, GH_ParamWireDisplay.@default, GH_ParamWireDisplay.faint);
sharedMode = Apply(second, sharedId, sharedMode, GH_ParamWireDisplay.hidden);
sharedMode = Apply(first, sharedId, sharedMode, GH_ParamWireDisplay.@default);
sharedMode = Apply(second, sharedId, sharedMode, GH_ParamWireDisplay.@default);
Check(sharedMode == GH_ParamWireDisplay.@default, "Historical multiple-component state cannot keep a wire faint");

var damaged = new GH_LooseChunk("HopperWire");
var damagedState = damaged.CreateChunk("ManagedWireDisplays");
damagedState.SetInt32("Count", int.MaxValue);
damagedState.CreateChunk("Target", 0).SetGuid("Id", Guid.NewGuid());
var invalidMode = damagedState.CreateChunk("Target", 1);
invalidMode.SetGuid("Id", Guid.NewGuid());
invalidMode.SetInt32("Applied", 99);
var valid = damagedState.CreateChunk("Target", 2);
valid.SetGuid("Id", sharedId);
valid.SetInt32("Applied", (int)GH_ParamWireDisplay.faint);
var recovered = new WireDisplayState();
recovered.Read(damaged);
Check(recovered.ManagedTargets.Count == 1 && recovered.ManagedTargets.Contains(sharedId),
    "Malformed records and bogus counts do not prevent reading valid targets");

WireGeometryChecks.Run(Check);
Console.WriteLine($"Passed {assertions} wire geometry, layout and display-state assertions.");
