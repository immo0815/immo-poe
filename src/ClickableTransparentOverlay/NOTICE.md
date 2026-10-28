# ClickableTransparentOverlay - vendored copy

This folder holds ClickableTransparentOverlay 11.1.0 by zaafar
(https://github.com/zaafar/ClickableTransparentOverlay, tag `11.1.0`, commit `297f3d4`),
licensed under the Apache License 2.0 - see `LICENSE` in this folder.

It is vendored rather than referenced as a package so that PoEformance's model viewer can draw on
the overlay's own Direct3D 11 device: the package keeps the device private and accepts pictures only
as pixels in memory.

## Changes from upstream

Every change is marked in the source with a `// PoEformance:` comment.

- `ClickableTransparentOverlay.csproj` - written for this solution: `net10.0-windows`, ImageSharp
  3.2.0 instead of 3.1.6 (security advisories), nullable analysis off (upstream's own
  annotations do not hold and the solution treats warnings as errors), no packaging settings.
- `Overlay.cs`
  - `Device` and `DeviceContext`: the D3D11 device and its immediate context, protected.
  - `AddView` / `DropView`: hand ImGui a shader-resource view the caller owns, and take it back
    without releasing it.
  - The device is created at the newest of feature levels 11.1, 11.0, 10.1 and 10.0 the hardware
    offers instead of 10.0 alone, and at 10.0 if that request fails (`CreateDevice`).
  - The window is also `WS_EX_TOOLWINDOW`, which keeps it out of the taskbar and Alt+Tab.
  - Present's answer is read rather than dropped (`Presented`): written down once per change through
    `Report`, a sleep while the window is occluded, and a lost device (removed, reset, hung) made anew
    with its swap chain, ImGui's device objects and font texture, ImGui's context kept
    (`RebuildDevice`, at most once a second); `DeviceRebuilt` tells the subclass its textures went.
    A resize that throws lets the swap chain go instead of ending the process, and a loop with no
    swap chain waits for the rebuild instead of drawing on nothing.
  - `FramesDone` and `Stage`: the loop's heartbeat and what it is doing, for a watchdog on another
    thread. `WindowState`: the window and Present in one line. `KeepOnTop` and `Restore`: the window
    put back on top and shown, its styles laid on again (`Utils.ReapplyTransparency`), the device
    made anew. `WM_DISPLAYCHANGE` and `WM_DWMCOMPOSITIONCHANGED` are reported.
  - `AddView` keeps the views it was lent apart, so a rebuild forgets them without releasing them.
- `ImGuiRenderer.cs` - `RegisterTexture` and `DeRegisterTexture` are internal instead of private;
  `Rebind` moves the renderer onto a new device.
- `Win32/User32.cs` - `SetWindowPos`, `IsWindowVisible`, `GetWindowRect` and `WindowRect`.
- `Win32/Utils.cs` - `ReapplyTransparency`.

Nothing else is changed; the remaining files are upstream's byte for byte.
