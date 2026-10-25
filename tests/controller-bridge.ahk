; Passed through /include, so this fixture exits before the real bridge's
; auto-execute section loads native libraries, polls a headset or sends keys.
#NoEnv
#SingleInstance Off
trigRel := 0.4
posRel := 0.3
plungRel := -0.3
AssertReady(false, BridgeInputReady(true, false, 0, 0, 0, 0, false), "focus loss disarms")
AssertReady(true, BridgeInputReady(false, true, 0, 0, 0, 0, false), "neutral controls rearm")
AssertReady(false, BridgeInputReady(false, true, 0.8, 0, 0, 0, false), "held left flipper")
AssertReady(false, BridgeInputReady(false, true, 0, 0.8, 0, 0, false), "held right flipper")
AssertReady(false, BridgeInputReady(false, true, 0, 0, 0.8, 0, false), "held up")
AssertReady(false, BridgeInputReady(false, true, 0, 0, -0.8, 0, false), "held down")
AssertReady(false, BridgeInputReady(false, true, 0, 0, 0, -0.8, false), "held plunger")
AssertReady(false, BridgeInputReady(false, true, 0, 0, 0, 0, true), "held tap button")
AssertReady(true, BridgeInputReady(true, true, 0.8, 0.8, 0.8, -0.8, true), "armed controls stay active")
AssertReady(false, BridgeInputReady(false, true, 0.4, 0, 0, 0, false), "release boundary is strict")
events := []
sink := Func("RecordKey")
for _, key in ["LShift", "RShift", "Numpad8", "Numpad2", "Enter"]
{
    held := false
    events := []
    UpdateHeldKey(held, 0.7, 0.7, 0.4, key, sink)
    AssertReady(0, events.Length(), "press threshold is strict")
    UpdateHeldKey(held, 0.8, 0.7, 0.4, key, sink)
    AssertReady(true, held, "pressed state")
    AssertReady(key ":1", events[1], "one owned key down")
    UpdateHeldKey(held, 0.8, 0.7, 0.4, key, sink)
    UpdateHeldKey(held, 0.4, 0.7, 0.4, key, sink)
    AssertReady(1, events.Length(), "hysteresis retains key without repeating")
    UpdateHeldKey(held, 0.3, 0.7, 0.4, key, sink)
    AssertReady(false, held, "release state")
    AssertReady(key ":0", events[2], "one owned key up")
    ReleaseHeldKey(held, key, sink)
    AssertReady(2, events.Length(), "cleanup ignores unowned keys")
    UpdateHeldKey(held, 0.9, 0.7, 0.4, key, sink)
    ReleaseHeldKey(held, key, sink)
    ReleaseHeldKey(held, key, sink)
    AssertReady(4, events.Length(), "focus loss and shutdown release exactly once")
    AssertReady(false, held, "cleanup clears ownership")
}
AssertReady(false, BridgeInputReady(false, true, 0, 0, 0.3, 0, false), "positive stick boundary")
AssertReady(false, BridgeInputReady(false, true, 0, 0, -0.3, 0, false), "negative stick boundary")
AssertReady(false, BridgeInputReady(false, true, 0, 0, 0, -0.3, false), "plunger boundary")
events := []
active := false
held := false
pauseCount := 0
focus := Func("FixtureFocus")
pause := Func("FocusLostDuringTap")
UpdateHeldKey(held, 1, 0.7, 0.4, "LShift", sink, focus)
AssertReady(false, held, "denied press does not acquire ownership")
AssertReady(0, events.Length(), "denied press sends no event")
TapKey("1", sink, focus, pause)
AssertReady(0, pauseCount, "denied tap does not wait")
AssertReady(0, events.Length(), "denied tap sends no event")
active := true
TapKey("Esc", sink, focus, pause)
AssertReady(1, pauseCount, "accepted tap waits once")
AssertReady("Esc:1", events[1], "accepted tap presses")
AssertReady("Esc:0", events[2], "tap releases after focus loss")
TapKey("5", sink, focus, pause)
AssertReady(2, events.Length(), "later tap cannot leak into new foreground app")
events := []
active := true
caught := false
try
    TapKey("1", sink, focus, Func("FailedTapWait"))
catch error
    caught := true
AssertReady(true, caught, "tap failure is visible")
AssertReady("1:1", events[1], "failed tap acquired key")
AssertReady("1:0", events[2], "failed tap still releases its key")
FileAppend, % "PASS: " assertionCount " controller input assertions; no native libraries loaded or keys sent.`n", *
ExitApp, 0

FixtureFocus()
{
    global active
    return active
}

FailedTapWait()
{
    throw Exception("fixture wait failed")
}

FocusLostDuringTap()
{
    global active, pauseCount
    active := false
    pauseCount++
}

RecordKey(key, down)
{
    global events
    events.Push(key ":" (down ? 1 : 0))
}

AssertReady(expected, actual, label)
{
    global assertionCount
    assertionCount++
    if (actual != expected)
    {
        FileAppend, % "FAIL: " label "`n", *
        ExitApp, 1
    }
}
