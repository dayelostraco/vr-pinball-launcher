; vpx_vr_controls.ahk -- Quest -> VPX controls (no vJoy driver needed)
; Reads Touch controllers via auto_oculus_touch and sends VPX's default keys.
; Place next to auto_oculus_touch.ahk + auto_oculus_touch.dll + vJoyInterface.dll
; (auto_oculus_touch.dll links vJoy, so vJoyInterface.dll must be present to load,
;  even though we never use vJoy). Run with the Oculus runtime active.
;
; Mapping (only active while the VPinballX window is focused):
;   Left trigger        -> Left Shift   (left flipper)
;   Right trigger       -> Right Shift  (right flipper)
;   X button (left)     -> 1            (start game)
;   B button (right)    -> 5            (insert coin)
;   Y button (left)     -> Esc          (exit table -> back to launcher)
;   Right stick back    -> Enter (held) (plunger: pull back, release to launch)
;   Left stick click    -> Numpad 5     (reset VR player position)
;   Left stick up       -> Numpad 8 (held) (move VR player up)
;   Left stick down     -> Numpad 2 (held) (move VR player down)
; These are VPX defaults. If yours differ, check VPX > Preferences > Configure Keys.

#NoEnv
#SingleInstance Force
#include auto_oculus_touch.ahk
SendMode Input            ; if VPX misses inputs, change to: SendMode Play
OnExit, Cleanup

; VPX executable -- keys are only sent when this window is in the foreground,
; so the controls never leak into the launcher menu or any other app.
GroupAdd, VPXPlayers, ahk_exe VPinballX_GL64.exe
GroupAdd, VPXPlayers, ahk_exe VPinballX_BGFX64.exe
GroupAdd, VPXPlayers, ahk_exe VPinballX64.exe
vpxExe := "ahk_group VPXPlayers"

; thresholds (tune to taste)
trigPress := 0.7
trigRel   := 0.4
plungPull := -0.5         ; right stick Y past this (pulled back) = charge plunger
plungRel  := -0.3
posPush   := 0.5          ; left stick Y past this = move player (hold Numpad 8/2)
posRel    := 0.3

; held-key states
leftFlip  := false
rightFlip := false
plunger   := false
posUp     := false
posDown   := false
inputArmed := false

; auto-focus state (see below)
vpxSeenAt    := 0
lastActivate := 0
lastWinCount := 0

InitOculus()

Loop
{
    Poll()

    ; --- Auto-focus the VPX player window when it appears ---
    ; The launcher's SetForegroundWindow call is unreliable (Windows blocks
    ; foreground changes from background processes, and with -minimized the
    ; process's "main window" is the minimized editor, not the player), so
    ; the bridge pulls the player window to the front itself. Retries are
    ; limited to the first 15 s after the window appears, so alt-tabbing
    ; away during play still works.
    playerWin := FindPlayerWindow(vpxWinCount)
    if (playerWin)
    {
        if (vpxSeenAt = 0)
        {
            vpxSeenAt := A_TickCount
            LogEvent("VPX player window appeared -- auto-focusing. Windows: " WinListDesc())
        }
        else if (vpxWinCount != lastWinCount)
        {
            ; a companion window (e.g. the Freezy Virtual DMD, which lives in
            ; the same VPX process) opened or closed and may have stolen
            ; focus -- re-arm the auto-focus timer so we take it back.
            vpxSeenAt := A_TickCount
            LogEvent("VPX window set changed -- re-arming auto-focus. Windows: " WinListDesc())
        }
        lastWinCount := vpxWinCount

        ; NOTE: must check the player window specifically -- WinActive(vpxExe)
        ; is true even when the DMD window has focus, which left keys going
        ; to the DMD instead of the player.
        if (!WinActive("ahk_id " playerWin) && A_TickCount - vpxSeenAt < 15000 && A_TickCount - lastActivate > 500)
        {
            lastActivate := A_TickCount
            WinActivate, ahk_id %playerWin%
        }
    }
    else if (vpxSeenAt != 0)
    {
        vpxSeenAt := 0
        lastWinCount := 0
        LogEvent("VPX player window gone")
    }

    lt := GetAxis(AxisIndexTriggerLeft)
    rt := GetAxis(AxisIndexTriggerRight)
    ry := GetAxis(AxisYRight)
    ly := GetAxis(AxisYLeft)

    focused := playerWin && WinActive("ahk_id " playerWin)
    tapHeld := IsDown(ovrX) || IsDown(ovrB) || IsDown(ovrY) || IsDown(ovrLThumb)
    inputArmed := BridgeInputReady(inputArmed, focused, lt, rt, ly, ry, tapHeld)
    if (focused && inputArmed)
    {
        ; --- Flippers (hold while squeezed) ---
        UpdateHeldKey(leftFlip, lt, trigPress, trigRel, "LShift")

        UpdateHeldKey(rightFlip, rt, trigPress, trigRel, "RShift")

        ; --- Start / Coin / Exit (one tap per button press) ---
        ; VPX/PinMAME polls the keyboard state, so an instantaneous
        ; down+up from SendInput can fall between polls and be lost
        ; (the start switch especially). Hold each tapped key briefly.
        if IsPressed(ovrX)
        {
            LogEvent("X pressed -> sending 1 (start)")
            TapKey("1")     ; start game (X on left controller)
        }
        if IsPressed(ovrB)
        {
            LogEvent("B pressed -> sending 5 (coin)")
            TapKey("5")     ; insert coin
        }
        if IsPressed(ovrY)
        {
            LogEvent("Y pressed -> sending Esc (exit)")
            TapKey("Esc")   ; exit table
        }

        ; --- VR player position (left stick) ---
        ; Click resets; push up/down holds Numpad 8/2 so VPX moves the
        ; viewpoint continuously until the stick returns to center.
        if IsPressed(ovrLThumb)
        {
            LogEvent("LThumb click -> sending Numpad5 (reset VR position)")
            TapKey("Numpad5")
        }

        UpdateHeldKey(posUp, ly, posPush, posRel, "Numpad8")

        UpdateHeldKey(posDown, -ly, posPush, posRel, "Numpad2")

        ; --- Plunger: pull right stick back to charge, release to launch ---
        UpdateHeldKey(plunger, -ry, -plungPull, -plungRel, "Enter")

        focusState := "YES"
    }
    else
    {
        ; VPX not focused -- release anything still held so no key sticks
        ; down at the OS level, then forget the states.
        ReleaseHeldKey(leftFlip, "LShift")
        ReleaseHeldKey(rightFlip, "RShift")
        ReleaseHeldKey(plunger, "Enter")
        ReleaseHeldKey(posUp, "Numpad8")
        ReleaseHeldKey(posDown, "Numpad2")
        leftFlip  := false
        rightFlip := false
        plunger   := false
        posUp     := false
        posDown   := false
        focusState := focused ? "waiting for controls to return to neutral" : "no (menu/other window)"

        ; diagnostic: a button press that lands here was seen by the bridge
        ; but ignored because VPX was not the foreground window.
        if IsPressed(ovrX)
            LogEvent("X pressed but IGNORED -- VPX window not focused (active: " ActiveWinDesc() ")")
        if IsPressed(ovrB)
            LogEvent("B pressed but IGNORED -- VPX window not focused (active: " ActiveWinDesc() ")")
    }

    ; diagnostic: which buttons are currently held (helps verify A detection)
    btns := ""
    if IsDown(ovrA)
        btns .= "A "
    if IsDown(ovrB)
        btns .= "B "
    if IsDown(ovrX)
        btns .= "X "
    if IsDown(ovrY)
        btns .= "Y "
    if IsDown(ovrRThumb)
        btns .= "RThumb "
    if IsDown(ovrLThumb)
        btns .= "LThumb "
    if (btns = "")
        btns := "(none)"

    ToolTip, % "VPX focused: " focusState "`nL trig: " Round(lt,2) "   R trig: " Round(rt,2) "   L stick Y: " Round(ly,2) "`nButtons down: " btns "`n(Ctrl+Alt+Q to quit)"
    Sleep 5
}

^!q::ExitApp        ; Ctrl+Alt+Q quits

; A held trigger/stick/button must not become a fresh press after focus returns.
; Once armed, ordinary hysteresis below owns release thresholds until focus leaves.
BridgeInputReady(ready, focused, lt, rt, ly, ry, tapHeld)
{
    global trigRel, posRel, plungRel
    if (!focused)
        return false
    return ready || (!tapHeld && lt < trigRel && rt < trigRel && Abs(ly) < posRel && ry > plungRel)
}

; The optional sink lets decision tests observe output without sending OS keys.
UpdateHeldKey(ByRef held, axis, press, release, key, sink := "", focused := "")
{
    next := held ? axis >= release : axis > press
    if (next != held)
    {
        if BridgeSend(key, next, sink, focused)
            held := next
    }
}

ReleaseHeldKey(ByRef held, key, sink := "")
{
    if (held)
    {
        held := false
        BridgeSend(key, false, sink)
    }
}

BridgeSend(key, down, sink := "", focused := "")
{
    global playerWin
    ; A tap may outlive its polling frame. Recheck before every new key-down,
    ; including actions after an Exit tap; always release an already owned key.
    if (down)
    {
        active := IsObject(focused) ? focused.Call() : (IsObject(sink) ? true : WinActive("ahk_id " playerWin))
        if (!active)
            return false
    }
    if IsObject(sink)
        sink.Call(key, down)
    else if (down)
        Send {%key% down}
    else
        Send {%key% up}
    return true
}

; Hold taps across several typical 60 Hz VPX/PinMAME polling intervals.
TapKey(key, sink := "", focused := "", pause := "")
{
    if !BridgeSend(key, true, sink, focused)
        return
    try
    {
        if IsObject(pause)
            pause.Call()
        else
            Sleep 80
    }
    finally
        BridgeSend(key, false, sink, focused)
}

; Find the VPX *player* window to focus. The VPX process owns several
; windows: the editor (minimized via -minimized), the SDL player/VR preview
; window, and possibly the Freezy Virtual DMD. Only the SDL player window
; may receive keyboard focus. Prefer the SDL window class, then a "Player"
; title; never pick DMD/backglass windows. Also returns the total window
; count via ByRef so the caller can notice new windows appearing.
; Returns an hwnd, or 0 if none.
FindPlayerWindow(ByRef count)
{
    global vpxExe
    fallback := 0
    WinGet, ids, List, %vpxExe%
    count := ids
    Loop %ids%
    {
        id := ids%A_Index%
        WinGet, mm, MinMax, ahk_id %id%
        if (mm = -1)                     ; minimized editor -- never focus this
            continue
        WinGetClass, cls, ahk_id %id%
        if (cls = "SDL_app" || cls = "VPPlayer")             ; the GL player / VR preview window
            return id
        WinGetTitle, title, ahk_id %id%
        if (title = "Visual Pinball Player")
            return id
        ; never fall back to DMD or backglass companion windows
        if (InStr(title, "DMD") || InStr(title, "B2S") || InStr(title, "Backglass"))
            continue

    }
    return fallback
}

; One-line description of every window owned by the VPX process, for the log.
WinListDesc()
{
    global vpxExe
    desc := ""
    WinGet, ids, List, %vpxExe%
    Loop %ids%
    {
        id := ids%A_Index%
        WinGetClass, cls, ahk_id %id%
        WinGetTitle, title, ahk_id %id%
        WinGet, mm, MinMax, ahk_id %id%
        desc .= "[class=" cls " title=" title " min=" mm "] "
    }
    return desc
}

; Append a timestamped line to bridge_debug.log next to this script.
LogEvent(msg)
{
    FormatTime, ts,, yyyy-MM-dd HH:mm:ss
    FileAppend, %ts%  %msg%`n, %A_ScriptDir%\bridge_debug.log
}

; Name of the current foreground window, for the not-focused diagnostic.
ActiveWinDesc()
{
    WinGet, exe, ProcessName, A
    WinGetTitle, title, A
    return exe " / " title
}

Cleanup:
    ; Release only keys owned by this bridge, not unrelated physical key presses.
    ReleaseHeldKey(leftFlip, "LShift")
    ReleaseHeldKey(rightFlip, "RShift")
    ReleaseHeldKey(plunger, "Enter")
    ReleaseHeldKey(posUp, "Numpad8")
    ReleaseHeldKey(posDown, "Numpad2")
    ExitApp
return
