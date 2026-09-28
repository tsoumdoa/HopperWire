using System;
using System.Collections.Generic;
using System.Drawing;
using HopperWire;

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

Console.WriteLine($"Passed {assertions} wire layout assertions.");
