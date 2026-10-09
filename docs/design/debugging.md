# Debugging

Run configurations ([Workbench](workbench.md)) say how to debug a program; this document describes the debugger that does it: one
debugging model and UI in Runesmith, and one engine per runtime behind it.

## Goals

- Breakpoints (line, conditional, hit count, log points), exception breakpoints, stepping (over, into, out), pause and continue.
- Threads, call stacks and variables with lazy expansion; watches and evaluation in the paused frame.
- Starting a program under the debugger and attaching to a running one.
- The debugger never makes typing slower, and stepping shows the new position within 100 ms of the engine's answer.

## The model

The core talks the Debug Adapter Protocol (DAP) internally: it is a well-specified, language-neutral model of sessions, breakpoints,
threads, stack frames, scopes and variables, and engines that already speak it plug in without translation.

- **`IDebugger`** (exported by plugins): an id (`dotnet`, `jdwp`), and `StartAsync(DebugLaunch, ...)` returning an `IDebugSession`.
- **`IDebugSession`**: the DAP requests the UI needs (setBreakpoints, configurationDone, threads, stackTrace, scopes, variables,
  continue, next, stepIn, stepOut, pause, evaluate, disconnect) and its events (stopped, continued, output, thread, exited, terminated).
  An engine that speaks DAP over a process's standard streams is wrapped by a shared `DapProcessSession`; an engine written in C# implements
  the interface directly.
- **Breakpoints** belong to the workspace, not a session: they are saved in the folder's state, move with edits (the editor's offset
  mapping), and are sent to every session that starts.

## The engines

### .NET: netcoredbg

netcoredbg is Samsung's open-source .NET debugger (MIT), with release builds for Linux (x64, arm64), macOS (arm64) and Windows (x64), and
speaks DAP when started with its DAP interpreter option. The C# plugin downloads it on first use into Runesmith's data folder (`tools/netcoredbg/<version>`),
verified like an SDK, and starts it with the launch plan's program, arguments, working directory and environment. The .NET debugger
included with Microsoft's own tools is licensed only for Microsoft's products and is not an option.

### Java: JDWP

Every JDK has the Java Debug Wire Protocol built in. Runesmith starts the program with
`-agentlib:jdwp=transport=dt_socket,server=y,suspend=y,address=127.0.0.1:0`, reads the port from the first line it prints, connects, and
talks JDWP from C#: a small, documented binary protocol (packets of the VirtualMachine, ReferenceType, Method, ThreadReference,
StackFrame, ObjectReference, StringReference, ArrayReference and EventRequest command sets). The Java plugin's engine maps it to the
DAP model:

- line breakpoints become class prepare and location requests, using the line tables of loaded classes, and are set again as classes load;
- variables come from the frame's slots and objects' fields, with `toString()` invoked for display where the user allows it;
- evaluation covers names, field access, array indexing and method calls without side effects at first, using the Java semantic model to
  resolve names in the paused frame's scope.

Attaching to a program already started with the agent uses the same engine.

## The UI

- **Gutter:** clicking the margin toggles a breakpoint; the context menu edits its condition, hit count or log message; a disabled or
  unverified breakpoint is drawn hollow.
- **Debug tool window** (bottom area): one tab per session with Threads and frames on the left, Variables in the middle, Watches on the
  right, and the program's console; frames from code without source are greyed.
- **Toolbar:** while a session runs, the run widget shows Continue, Pause, Step over, Step into, Step out, Restart and Stop.
- **Editor:** the current line is highlighted, and variable values of the current frame show at the end of their lines.

## Delivery

1. The model, `DapProcessSession`, breakpoints in the gutter and their persistence.
2. netcoredbg: download, launch and attach, the Debug tool window, stepping.
3. JDWP: connection, breakpoints, stepping, threads, frames and variables.
4. Evaluation and watches for both, inline values.

Each step lands with tests that debug small real programs (a .NET console app and a Java class) through the engine, and with the session
measured: time from a step request to the editor showing the new line.
