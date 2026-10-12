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

- **`IDebugAdapterProvider`** (exported by plugins): the debuggers it serves, by name, how to start a debug adapter process or reach
  one on a socket, and the launch or attach request for a `DebugLaunch`. Starting a process needs the `process` capability.
- **`DebugSession`** (in the shell): the DAP requests the UI needs (setBreakpoints, configurationDone, threads, stackTrace, scopes,
  variables, continue, next, stepIn, stepOut, pause, evaluate, terminate, disconnect) and its events (stopped, continued, output, thread,
  breakpoint, exited, terminated), over `DebugAdapterClient` from `Runesmith.Dap`. An engine a plugin writes itself, for a runtime whose
  debugger speaks another protocol, serves DAP from a process or a socket of its own.
- **Breakpoints** belong to the workspace, not a session: they are saved in the folder's state, move with edits (the editor's offset
  mapping), and are sent to every session that starts.

## The engines

Runesmith ships no debug engine. A language plugin brings the engine of its runtime: a debugger that speaks DAP already, which it starts
or downloads, or a translation of the runtime's own debugging protocol to DAP. The designs of the official engines are in their plugins'
repositories.

## The UI

- **Gutter:** clicking the margin toggles a breakpoint; the context menu edits its condition, hit count or log message; a disabled or
  unverified breakpoint is drawn hollow.
- **Debug tool window** (bottom area): the current session's threads and frames on the left, Variables in the middle and Watches on the
  right, with tabs for the debug console and the breakpoints; frames from code without source are greyed.
- **Toolbar:** while a session runs, a debug toolbar beside the run widget shows Continue or Pause, Step over, Step into and Step out; the
  run widget's Stop ends the session, and the Debug tool window's header has Restart and Stop too.
- **Editor:** the current line is highlighted, and variable values of the current frame show at the end of their lines.

## Delivery

1. The model, `DapProcessSession`, breakpoints in the gutter and their persistence.
2. The Debug tool window, launch and attach, and stepping, with the first engine a plugin brings.
3. Evaluation and watches, inline values.

Each step lands with tests that debug a small real program through a debug adapter made for the tests, and with the session
measured: time from a step request to the editor showing the new line.
