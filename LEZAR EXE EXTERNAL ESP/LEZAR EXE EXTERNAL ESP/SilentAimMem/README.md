# ESP + Aimbot + AimbotAI (C# Version)

## Overview
C# port of Free Fire ESP/Aimbot using CR3-based physical memory reading via BstkVMM hypervisor driver. Ported from guna's original C# project + working C++ `example-mem` memory engine.

## Architecture

### Memory Engine (CR3-based)
```
BlueStacks Process (HD-Player.exe)
  → BstkVMM.dll (hypervisor module, 5MB)
    → VM found (pvm, pvcpu, cpus)
      → Kernel task walker (walks init_task.tasks linked list)
        → Finds game task (com.dts.freefireth) by cmdline/comm matching
          → Reads task.mm → CR3 (page table base)
            → Virtual → Physical address translation via CR3
              → ReadGuestUser / ReadGuestKernel / ReadGuestPhysical
```

### Key Design Decisions
- **Static singleton Memory class** — matches guna's InternalMemory pattern
- **`Memory.Read<T>()` auto-detects** read route:
  - If `guestCr3 != 0 && address < 0xFFFF000000000000` → `ReadGuestUser` (user-space via CR3)
  - If `address >= 0xFFFF000000000000` → `ReadGuestKernel` (kernel VA direct)
  - Else → `ReadGuestPhysical`
- **`Memory.ReadKernel<T>()`** — bypasses auto-detection, goes direct to kernel engine
- **`Memory.ReadUser()`** — bypasses auto-detection, goes direct to user engine
- **`Utility.IsLikelyGuestKernelPointer()`** — simple `>= 0xFFFF000000000000` check
- **All game pointers are `uint` (4 bytes)** — BlueStacks ARM guest uses 32-bit pointers
- **System.Numerics** (Vector3, Matrix4x4, Quaternion) — no custom types
- **GDI+ WinForms overlay** — replaces ImGui for ESP rendering

### C++ Reference Mapping
| C# | C++ (example-mem) |
|---|---|
| `Memory.Read<T>(addr)` | `memory->read<T>(addr)` (auto-detect) |
| `Memory.ReadKernel<T>(addr)` | `memory->read_kernel<T>(addr)` |
| `Memory.ReadUser(cr3, addr, buf, size)` | `memory->read_user(cr3, addr, buf, size)` |
| `Memory.GetCr3(mm)` | `memory->get_cr3(mm)` |
| `Memory.Setup(verbose)` | `memory->setup(verbose)` → `memory_engine.initialize()` |
| `Memory.Destroy()` | `memory->destroy()` |
| `Utility.IsLikelyGuestKernelPointer(v)` | `utility::is_likely_guest_kernel_pointer(v)` |
| `BlueStacks.Setup(pkg, verbose)` | `bluestacks.setup(pkg, verbose)` |
| `MemoryEngine.Initialize()` | `memory_engine.initialize()` (hardcoded HD-Player.exe + BstkVMM.dll) |

## File Structure

### Core Files
| File | Purpose |
|---|---|
| `Program.cs` | Entry point, starts MainForm |
| `MainForm.cs` | Main UI with tabs: Main (Connection + ESP + Aimbot), Info (debug log) |
| `Memory.cs` | Static memory wrapper (Read/Write/ReadKernel/ReadUser/GetCr3) |
| `MemoryEngine.cs` | Hypervisor bridge via BstkVMM.dll — handles kernel/user/physical reads |
| `BlueStacks.cs` | Game task finder — walks kernel task list, finds CR3, module enumeration |
| `Utility.cs` | `IsLikelyGuestKernelPointer()` helper |
| `GvaMemoryBridge.cs` | VA→PA translation |
| `NativeMethods.cs` | kernel32 P/Invoke (OpenProcess, etc.) |
| `KernelStructs.cs` | Kernel structs (CLIENT_ID, etc.) |

### Game Logic Files
| File | Purpose |
|---|---|
| `Data.cs` | Main data loop — reads all game structures every frame |
| `Config.cs` | All toggle/settings (ESP, Aimbot, etc.) |
| `Entity.cs` | Entity data class (bones, health, name, etc.) |
| `Core.cs` | Shared state (CameraMatrix, Entities dict, LocalPlayer, etc.) |
| `Offsets.cs` | All game memory offsets |
| `Bones.cs` | Bone offset enum |
| `W2S.cs` | World-to-Screen projection |
| `Transform.cs` | Unity transform reading (matrix-based) |
| `MathUtils.cs` | Math helpers |
| `WeaponIndex.cs` | Weapon ID → name mapping |

### ESP/Aimbot
| File | Purpose |
|---|---|
| `OverlayForm.cs` | GDI+ transparent overlay — draws box, skeleton, snapline, health, name, weapon, FOV, crosshair |
| `Aimbot.cs` | Traditional aimbot — reads view matrix, calculates angle delta, moves mouse |
| `AimbotAi.cs` | AI aimbot — uses head collider pointer for auto-aim |

### P/Invoke
| File | Purpose |
|---|---|
| `WinAPI.cs` | mouse_event, SetCursorPos, GetAsyncKeyState |

## Connection Flow
1. User clicks CONNECT
2. `BlueStacks.Setup("com.dts.freefireth")` called (20 attempts, 500ms delay each)
3. Each attempt:
   - `Memory.Destroy()` — clean slate
   - `Memory.Setup()` → `MemoryEngine.Initialize()`:
     - Finds `HD-Player.exe` PID
     - OpenProcess with VM_READ | VM_WRITE | VM_OPERATION
     - Finds `BstkVMM.dll` module base
     - Scans for VM structures (pvm, pvcpu, cpus)
   - `FindTask("com.dts.freefireth")`:
     - Reads `init_task.tasks` linked list head from kernel
     - Walks task list forward + backward
     - For each task: reads mm, pid, tgid, comm, cmdline
     - Scores tasks by package name match
   - `Memory.GetCr3(task.Mm)` — gets game CR3 from mm_struct
   - `Memory.SetCr3(cr3)` — enables user-space reads
4. On success: `GetModuleBase("libil2cpp.so")` → IL2CPP base
5. Starts Data.Work, Aimbot.Work, AimbotAi.Work threads

## Data Reading Chain (Data.cs)
```
Il2CppBase + 0xA986E9C → baseGameFacade
  → gameFacade
    → gameFacade + 0x5C → staticGameFacade
      → staticGameFacade → currentGame
        → currentGame + 0x50 → currentMatch
          → currentMatch + 0x94 → localPlayer
          → currentMatch + 0xB4 → observer (fallback)
            → observer + 0x28 → observerPlayer
          → localPlayer + 0x450 → followCamera
            → followCamera + 0x18 → camera
              → camera + 0x8 → cameraBase
                → cameraBase + 0xE8 → viewMatrix (Matrix4x4)
```

### Entity Reading
```
currentGame + 0x68 → entityDictionary
  → dict + 0xC → entries array
  → dict + 0x10 → count
  → entries + 0x10 + (i * 0x10) + 0xC → entity pointer
```

### Entity Processing
- AvatarManager (0x4C0) → Avatar (0xA8) → IsVisible (0x95) → AvatarData (0x14) → IsTeam (0x59)
- If avatar chain fails, entity is still marked as known (fallback)
- Bones: Head(0x458), Breast(0x454), Root(0x46C), hip(0x470), etc.
- Transform reading: node→matrix→index→TMatrix (position/rotation/scale)

## Key Offsets
| Name | Offset |
|---|---|
| InitBase | 0xA986E9C |
| StaticClass | 0x5C |
| CurrentMatch | 0x50 |
| LocalPlayer | 0x94 |
| FollowCamera | 0x450 |
| Camera | 0x18 |
| ViewMatrix | 0xE8 |
| DictionaryEntities | 0x68 |
| MainCameraTransform | 0x24C |
| Weapon | 0x348 |
| Player_IsDead | 0x50 |
| Player_Name | 0x2E0 |
| Player_Data | 0x48 |
| AvatarManager | 0x4C0 |
| Avatar | 0xA8 |
| Avatar_IsVisible | 0x95 |
| Avatar_Data_IsTeam | 0x59 |

## UI Layout
```
┌──────────────────────────┐
│  ESP + Aimbot + AimbotAI │
├──────────────────────────┤
│ [Main] [Info]            │  ← TabControl
│┌────────────────────────┐│
││ CONNECTION             ││
││ Process: [HD-Player.exe]││
││ Package: [com.dts.free] ││
││ [    CONNECT           ]││
││ Status: Connected!     ││
││ [    Toggle Overlay    ]││
││────────────────────────││
││ ESP                    ││
││ [x]Box [x]Corner [x]Ln││
││ [x]Name [x]HP [x]Dist ││
││ [x]Skel [x]Weapon[x]Cr││
││ [x]FOV [x]Snap         ││
││────────────────────────││
││ AIMBOT                 ││
││ [x]Enable [x]Rage [x]Si││
││ [x]AI [x]NoRecoil      ││
││ Target:[Neck] FOV:[200]││
│└────────────────────────┘│
└──────────────────────────┘
```

## Bugs & Fixes (Development Log)

### Bug 1: Missing `using System;` and `using System.Drawing;`
**Symptom:** Compilation errors — `Memory` class not found, `Font`/`Color`/`Point` not found
**Cause:** Initial file creation missed required using directives
**Fix:** Added `using System;`, `using System.Drawing;`, `using System.Numerics;` etc. to all files

### Bug 2: Static Memory class conflicted with guna's constructor pattern
**Symptom:** Compilation error — `Memory` class couldn't be instantiated as it was static
**Cause:** guna's original used `new InternalMemory()` but we converted to static singleton
**Fix:** Made `Memory` a static class with static methods, matching guna's usage pattern (`Memory.Read<T>()`)

### Bug 3: Custom types (Vector3f, Matrix4x4f) conflicted with System.Numerics
**Symptom:** Compilation errors — ambiguous type references between custom types and `System.Numerics`
**Cause:** Gunas project defined custom math types; we switched to System.Numerics
**Fix:** Removed custom types, used `System.Numerics.Vector3`, `Matrix4x4`, `Quaternion` everywhere

### Bug 4: "Game task NOT found!" — Task walker couldn't find game process
**Symptom:** Connection failed every time after 20 attempts — `FindTask` returned false
**Cause:** Memory engine was using `HD-Player.exe` as module name for `GetModuleBase()`, but the C++ reference used `BstkVMM.dll`
**Fix:** Changed `MemoryEngine.Initialize()` to use `BstkVMM.dll` as the module name (matching C++ `memory_engine.cpp`)

### Bug 5: "Game task NOT found!" persisted after BstkVMM fix
**Symptom:** Same error even after fixing module name
**Cause:** C# code was using `Memory.Read()` (auto-detect) for kernel reads, which routed through `ReadGuestUser` with CR3. But CR3 wasn't set yet during task walking — the task walker needs `ReadGuestKernel` for kernel addresses
**Fix:** Rewrote `BlueStacks.cs` to use `Memory.ReadKernel()` for all kernel reads (bypassing auto-detection), matching C++ `bluestacks.cpp` which uses `memory->read_kernel()` directly

### Bug 6: "Game task NOT found!" still persisted
**Symptom:** Same error after switching to ReadKernel
**Cause:** C# `Memory.Read<T>` auto-detection used `TryConvertKernelVirtualToPhysical` (complex VA→PA conversion). C++ used simple `is_likely_guest_kernel_pointer` (`>= 0xFFFF000000000000`)
**Fix:** Replaced with `Utility.IsLikelyGuestKernelPointer()` — simple `>= 0xFFFF000000000000` check. Added `ReadKernel<T>()` and `ReadUser()` methods that bypass auto-detection entirely

### Bug 7: "Game task NOT found!" — final fix
**Symptom:** Same error after multiple iterations
**Cause:** `BlueStacks.Setup()` was taking a `processName` parameter but `Memory.Setup()` also took one, and the call chain was inconsistent. C++ hardcoded `HD-Player.exe` inside `memory_engine.initialize()`
**Fix:** 
- Removed `processName` param from `Memory.Setup()` (no params, matching C++ `memory->setup()`)
- Removed `processName` param from `BlueStacks.Setup()` (only takes package name)
- `MemoryEngine.Initialize()` takes no process name — hardcodes `HD-Player.exe` and `BstkVMM.dll`
- Added `Memory.Destroy()` at start + end of each attempt (matching C++ flow)

### Bug 8: Overlay not showing
**Symptom:** ESP overlay invisible — no rendering at all
**Cause:** `repaintTimer` only called `Invalidate()` when `Core.HaveMatrix == true`. Before matrix was ready, the overlay never repainted (stayed transparent black = invisible)
**Fix:** Changed timer to always repaint: `repaintTimer.Tick += (s, e) => { if (Visible) Invalidate(); }`

### Bug 9: FOV circle not rendering
**Symptom:** FOV circle checkbox enabled but circle not drawn
**Cause:** `OverlayForm` checked `Config.Cross` (wrong field) instead of `Config.FOV`
**Fix:** Changed to `Config.FOV` in overlay rendering code

### Bug 10: No debug visibility — silent failures
**Symptom:** Connection succeeded but no ESP — impossible to diagnose why
**Cause:** All `Data.Work` failures were swallowed silently (no logging). Console output went to an external console window user couldn't see
**Fix:**
- Added Info tab to MainForm with RichTextBox debug log
- Redirected `Console.SetOut` to the Info tab via custom `LogWriter`
- Removed `AllocConsole()` from Program.cs (no more separate console window)
- Added per-step failure logging in `Data.Work` (InitBase, gameFacade, StaticClass, CurrentMatch, LocalPlayer, FollowCamera, Camera, CameraBase, ViewMatrix)

### Bug 11: LocalPlayer address wrong (`0x2E0065`)
**Symptom:** `localPlayer` address was suspiciously small (0x2E0065), FollowCamera always 0
**Cause:** CR3-based user-space reads were returning garbage. The data chain was reading through `ReadGuestUser` but the CR3 or translation was wrong
**Fix:** Added full diagnostic dump (pointer chain values, hex dump of localPlayer memory, raw FollowCamera read result). The issue resolved itself once game fully loaded — it was a timing issue, not an offset issue

### Bug 12: CurrentMatch read fails initially
**Symptom:** `FAIL: CurrentMatch (cg=0xE0D05CC0+0x50)` repeated many times
**Cause:** Game structures not initialized yet — user connecting before being in a match
**Fix:** This is expected behavior. Game needs 5-10 seconds to initialize after entering a match. The chain eventually succeeds once all game objects are created

### Bug 13: Entities found (40) but ESP shows 0 enemies
**Symptom:** `[ENT] entries: ok=40 fail=7 total=40` but overlay shows "Enemies: 0"
**Cause:** Entities were found but `IsKnown` remained `false` because the avatar visibility chain (AvatarManager→Avatar→IsVisible→AvatarData→IsTeam) failed on every entity. The overlay skipped all non-known entities
**Fix:** Added fallback — if avatar chain fails, entity is still marked as `IsKnown = true` and rendered. This means ALL entities are shown (including teammates temporarily), but ESP works

### Bug 14: Snapline not rendering
**Symptom:** Snapline checkbox enabled but no line drawn
**Cause:** `Config.Snapline` was defined in Config and toggled by UI checkbox, but `OverlayForm` never had rendering code for it
**Fix:** Added snapline rendering: `g.DrawLine(snapPen, w / 2f, h, cx, topY)` — draws line from bottom-center to enemy head

### Bug 15: FOV circle using wrong value
**Symptom:** FOV circle radius didn't match the UI numeric up-down value
**Cause:** Overlay used `Config.AimFov` (old field) instead of `Config.Fov` (set by UI numeric)
**Fix:** Changed to `Config.Fov`

### Bug 16: Aimbot section cut off (out of tab bounds)
**Symptom:** Aimbot controls overflowed outside the tab control
**Cause:** TabControl height (310px) too small for all sections (Connection + ESP + Aimbot)
**Fix:** Increased form height to 580px, TabControl height to 470px

### Bug 17: Publish fails — exe locked
**Symptom:** `System.IO.IOException: The process cannot access the file 'SilentAimMem.exe'`
**Cause:** Old exe still running when trying to publish to same folder
**Fix:** User must close the running exe before publishing. `taskkill /F` needs admin privileges

### Bug 18: `MemoryEngine.Initialize` still had processName param
**Symptom:** `Memory.Setup()` called `engine.Initialize(verbose)` but signature was `Initialize(string processName, bool verbose)` — compilation error
**Cause:** Incomplete API cleanup after removing processName from call chain
**Fix:** Changed `MemoryEngine.Initialize` to take only `bool verbose` (no process name)

### Bug 19: MatchStatus always 0 — overlay showed "Not in match..."
**Symptom:** User was in a match but `Core.MatchStatus` always read as 0, overlay showed "Not in match..." text and hid itself
**Cause:** `Offsets.MatchStatus = 0x8c` offset was incorrect or game changed the field location
**Fix:** Removed MatchStatus check entirely from overlay. Overlay now always shows without checking match status. RenderLoop always positions/shows overlay. OnPaint removed "Not in match..." early return.

### Bug 20: ESP takes 5-15 seconds to appear after overlay toggle
**Symptom:** After connecting and toggling overlay, ESP entities don't show for 5-15 seconds, then suddenly appear
**Cause:** New entities added with `IsKnown=false, IsDead=true, Health=0` — overlay filters (`!IsKnown`, `IsDead`, `Health < 1`) skip them until Data.Work reads actual values from memory
**Status:** Known limitation — entities need game memory to populate bone positions, health, and visibility state. Chain caching attempted but caused stale pointer issues and was reverted.

---

## Known Issues
- First 5-10 seconds after match start: entities may be 0 (game loading)
- Avatar team check chain sometimes fails — entities still rendered as fallback (may show teammates as enemies)
- `taskkill /F` may need admin to close exe
- No team filtering — all entities shown as enemies (avatar chain fallback)
- ESP delay: 5-15 seconds before entities appear (game must populate bone positions/health in memory)

## Project Structure
```
C# version\
  SilentAimMem.sln              ← Open this in Visual Studio
  SilentAimMem\
    SilentAimMem.csproj
    Program.cs
    MainForm.cs
    Memory.cs
    MemoryEngine.cs
    BlueStacks.cs
    Data.cs
    Config.cs
    Entity.cs
    Core.cs
    Offsets.cs
    Bones.cs
    W2S.cs
    Transform.cs
    MathUtils.cs
    WeaponIndex.cs
    OverlayForm.cs
    Aimbot.cs
    AimbotAi.cs
    WinAPI.cs
    Utility.cs
    GvaMemoryBridge.cs
    NativeMethods.cs
    KernelStructs.cs
    README.md
    bin\
    obj\
    publish\
      SilentAimMem.exe
```

## Chat Log
All development was done in this chat session. To continue from where we left off, reference the **Bugs & Fixes** section above and the **Data Reading Chain** section for context on the memory reading pipeline. Current state: ESP works, all overlays functional (box, corner box, line, snapline, name, health, distance, weapon, skeleton, crosshair, FOV circle), aimbot tab present. MatchStatus check removed from overlay — overlay always visible. Avatar team filtering is disabled as fallback (all entities shown as enemies). ESP has 5-15 sec initial delay while game populates entity data in memory.

## Build
```bash
dotnet build SilentAimMem.csproj
dotnet publish SilentAimMem.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## C++ Reference
Original C++ memory engine at:
`example\example-mem\example-mem\workspace\`
- `memory.hpp` — Memory wrapper (authoritative reference)
- `memory_engine.cpp` — Engine with hardcoded HD-Player.exe + BstkVMM.dll
- `bluestacks.cpp` — Task walker (authoritative reference)
- `kernel_structs.hpp` — Kernel structures

guna's original C# at:
`example\silentaim by guna\`
- `AotForms\Offsets.cs` — All offsets (authoritative reference)
- `AotForms\InternalMemory.cs` — Original memory class (uses AotBst.dll)
- `AotForms\Data.cs` — Original data reading logic
