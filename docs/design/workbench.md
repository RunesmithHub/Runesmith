# Workbench

The workbench is everything around the editor: the window's layout, its toolbars, the tool windows, the status bar, and the features
that turn an editor into an IDE: run, build and debug configurations, new projects from templates, and the SDKs projects build with.
This document describes how Runesmith's workbench looks and works, and how plugins extend it.

## Direction

Runesmith should feel calm and fast first, and rich second: a quiet window that shows only the editor and what the user asked for, with
depth one click away. Three rules decide most questions:

1. **The editor is the largest thing on screen.** Chrome is thin, uses the same surface color as the editor, and never draws borders
   where spacing separates well enough.
2. **Every feature has one obvious place.** Running is in the toolbar, tool windows are on the stripes, state is in the status bar.
   Nothing is reachable only from a menu.
3. **Rich, not busy.** Options exist, but behind a disclosure: a form shows the five fields most people change and an Advanced section for
   the rest.

### Visual language

- **Surfaces.** Three levels: the window (`Surface0`), panels and the editor (`Surface1`), and popups, menus and dialogs (`Surface2`, with
  a soft shadow and a 1 px outline at low contrast). Light and dark themes define the same levels.
- **Color.** One accent color, used for the focused element, the primary button, the selected item and progress, never for decoration.
  State colors (error, warning, success, information) appear only on what has that state.
- **Spacing.** A 4 px grid. Toolbars are 40 px high, stripes 40 px wide, rows in lists 28 px, form rows 32 px.
- **Type.** Inter for the interface at 13 px, 12 px for secondary text; the editor font stays separate. Titles use weight, not size.
- **Icons.** One stroke icon set at 16 px (HammerUI's), outline by default, filled only for an active toggle.
- **Motion.** Panels slide and popups fade in 120 ms; nothing animates while the user types.

## Layout

```text
+----------------------------------------------------------------------------------------------------------+
| [=] [Project v] [branch v]          [ Search everywhere ]        [Config v] [Run] [Debug] [Stop] [Build] [Gear] |
+----+--------------------+---------------------------------------------------+-------------------+------+
|    |                    | Tab  Tab  Tab                                      |                   |      |
| L  |  left tool window  | breadcrumbs                                        | right tool window | R    |
| s  |  (Explorer)        |                                                    | (Structure)       | s    |
| t  |                    |  editor                                            |                   | t    |
| r  |                    |                                                    |                   | r    |
| i  |                    +---------------------------------------------------+                   | i    |
| p  |                    |  bottom tool window (Run, Problems, Output, ...)   |                   | p    |
| e  |                    |                                                    |                   | e    |
+----+--------------------+---------------------------------------------------+-------------------+------+
| path > Type > Member            [background task: Loading C# projects 12/31]    Ln 12, Col 5  LF  UTF-8  4 spaces  C#  [bell] |
+----------------------------------------------------------------------------------------------------------+
```

### Main toolbar

The main toolbar replaces the menu bar. From left to right:

- **Main menu button.** Opens the full menu (File, Edit, View, Go, Run, Build, Tools, Help) as a popup. Pressing Alt shows it too. A
  setting puts the menu back in its own bar for people who prefer that.
- **Project widget.** The open folder's name; its popup lists recent folders, New Project, Open and Close.
- **Branch widget.** Reserved for version control; hidden until a plugin provides it.
- **Search everywhere.** One field that searches files, symbols, commands and settings, the command palette given a home.
- **Run widget.** The selected run configuration and its actions (see [Run, build and debug](#run-build-and-debug)).
- **Settings button.** Settings, plugins, SDKs and themes.

The toolbar is also the window's title bar on Windows and Linux, where `window.titleBar` can bring back the system's; the window's buttons
sit at its right end.

### Tool window stripes

Tool windows no longer live in tab groups of their own. Each one has a button on a stripe, a narrow column of icons at the left and right
edges of the window:

- The left stripe's upper group opens windows in the left area (Explorer, Search, Structure); its lower group opens windows in the bottom
  area (Run, Problems, Output, Terminal). The right stripe holds windows of the right area (Notifications, Database, and what plugins add).
- Clicking a button shows its window, or hides it when it is the one shown; one window per area shows at a time, and Shift+click shows a
  second one beside it.
- Buttons can be dragged between groups and stripes, and a window's menu has Move to Left, Right and Bottom.
- A button shows a badge when its window wants attention: the Problems count, a running process in Run.
- Tool windows can still float in their own window, and the editor area keeps HammerUI's split and drag behavior.

`ToolWindowDefinition.Side` is the default area, and the area decides the button's stripe and group.

### Editor area

- **Tabs** are compact, show the file icon, an unsaved dot, and the folder when two tabs have the same name. Overflowing tabs scroll and
  a list button shows them all.
- **Breadcrumbs** under the tabs show the file's path and, from the language analyzer, the type and member at the caret; each part opens
  a list of its siblings.

### Status bar

From left to right: the navigation path (when breadcrumbs are hidden), the **background task indicator** (what the analyzers and builds
are doing, such as "Loading C# projects 12 of 31", with a popup listing all tasks and a cancel button for each), and the document's caret
position, line ending, encoding, indentation and language, each a button that changes it. The notification bell at the end opens the
notification history.

### Welcome screen

With no folder open, the window shows a welcome screen instead of an empty editor: a list of recent projects with search, and the actions
New Project, Open and Clone. A left column switches between Projects, Customize (theme, font, keymap) and Plugins.

## Options and forms

New project templates, run configurations and SDK installs all need forms whose fields come from a plugin. One model describes them and
one renderer draws them, so they look and behave the same everywhere.

An `OptionSet` is a list of `Option`s:

| Kind | Control | Example |
| --- | --- | --- |
| `Text` | Text field, with an optional pattern | Project name, main class |
| `Path` | Text field with a browse button, for a file or a folder | Location, working directory |
| `Choice` | Drop-down; 2 to 4 short choices show as a segmented control | Target framework, language, build system |
| `Toggle` | Check box | Create Git repository |
| `Number` | Number field with a range | Port |
| `List` | Editable list of values or of key and value pairs | Program arguments, environment variables |
| `Sdk` | Drop-down of installed SDKs of a kind, with Download... at its end | .NET SDK, JDK |

Each option has an id, a label, an optional description shown as a hint below the field, a default, a `Group` (`Main` or `Advanced`;
advanced options sit in a collapsible section), and `VisibleWhen` and `EnabledWhen` conditions on other options' values. Validation
messages show under the field and keep the dialog's main button disabled.

The renderer lays out a two-column form: labels right-aligned in the first column, fields in the second, hints under the fields.

## Run, build and debug

A **run configuration** is a named, saved way to run something: a project with arguments, a test suite, a Maven goal. The toolbar's run
widget selects the active one and runs, debugs, stops and builds it.

### The model

- A `RunConfigurationType`, exported by a plugin, describes a kind of configuration: its id, name, icon, the `OptionSet` of its settings
  in a folder (such as a choice of its projects), how to detect configurations in a folder (one per runnable project), and how to
  turn a configuration into a launch.
- A `RunConfiguration` is a type id, a name and option values. Configurations are saved in `.runesmith/run.json` in the folder, so they
  can be committed and shared; ones marked as local are saved in the folder's state instead.
- **Before launch** steps run first, in order: build the configuration's project (the default), run another configuration, or run a
  command. A failing step stops the launch.
- Launching returns a `LaunchPlan`: the program, arguments, working directory and environment, and for debugging, what the debugger
  needs to start or attach.

### Running

The runner starts the process with its output redirected, and the **Run** tool window shows one tab per run, with the configuration's
name and icon, the output with ANSI colors, paths and stack frames as links, and Stop, Rerun and the exit code and run time. Running a
configuration that runs already asks to stop it or run another instance, per type.

### Building

The Build button builds the active configuration's project, and its menu has Build Solution, Rebuild and Clean. The existing build
providers stay as the way a whole folder builds; a configuration type can build its own target more precisely.

### Debugging

Debug configurations are part of the model from the start: each type's options include its debugging options, and its launch plan says
how to debug it. The debugger itself is a separate piece of work with its own design, [Debugging](debugging.md): a Debug Adapter Protocol client with a .NET adapter,
and a Java debugger over JDWP. Until it lands, the Debug button is disabled with a tooltip saying why.

### Configurations shipped

- **C#.** .NET project (project, target framework, launch profile from `launchSettings.json`, configuration, arguments, environment,
  working directory, SDK), .NET tests (`dotnet test` with a filter) and .NET command (any `dotnet` command).
- **Java.** Application (main class and module, JDK, VM options, program arguments, class path from the project), Maven goals and Gradle
  tasks, and JUnit tests.

### The configurations dialog

Run > Edit Configurations opens a dialog with the configurations listed on the left, grouped by type, with Add, Remove, Copy and Move,
and the selected one's form on the right, then its Before launch list.

## New projects

File > New Project, and New Project on the welcome screen, open a large dialog:

- **Left: the templates.** A search field, then filter chips for language (C#, Java, ...) and kind (Console, Library, Web, Desktop, Test,
  Other). The list below groups templates by kind; each row shows the icon, the name and the language, and a second line with the short
  description. Rows are dense (two lines in 44 px), so a 700 px dialog shows about 15 templates at once instead of a handful, and search
  narrows hundreds in one keystroke. Recently used templates come first.
- **Right: the form.** Name, Location with a hint showing where the project will be created, Create Git repository, then the template's
  options from its `OptionSet`, then two collapsible sections: About this template (description, author, tags, source) and Advanced.
- **Bottom:** Create and Cancel. Creating shows progress in the dialog and then opens the new folder.

Values the user changes are remembered per template.

### Templates come from plugins

A `ProjectTemplateProvider` export supplies templates. A `ProjectTemplate` has an id, name, description, language, kind, tags, icon,
its `OptionSet`, and a method that creates the project into a folder and reports progress. Templates ship in their own plugins, so a user
can switch a language's templates off in the Plugins settings without losing the language:

- **`runesmith.csharp-templates`** reads the templates the .NET SDK itself ships and the ones the user installed with
  `dotnet new install`: the template packages in the SDK's `templates/<version>` folder and in the template engine's packages folder. Each
  package's `.template.config/template.json` describes the template's name, identity, short name, classifications, language and
  parameters, and its `localize` files the display text. Parameters become options: a `choice` becomes a `Choice`, a `bool` a `Toggle`,
  text and numbers a `Text` or `Number`; a template's `Framework` parameter becomes Target framework next to the SDK picker. Creating runs
  `dotnet new` with the SDK the user picked, the template's identity and the options, then creates a `.slnx` solution and adds the
  project unless the user put both in one folder. Every template a user has installed appears without Runesmith knowing about it.
- **`runesmith.java-templates`** has Runesmith's own Java templates: Application, Library, Compact source file, and Multi-module
  project. Their options: build system (Maven, Gradle or none), Gradle's script language (Kotlin or Groovy), group and artifact, JDK,
  Java release, module declaration, JUnit tests and sample code.

## SDKs

The SDKs settings page lists every SDK Runesmith found or installed, per kind, with its version, vendor, location and source (found on
the system or installed by Runesmith), and an update badge when a newer patch exists. Download opens a form: kind, vendor, version and
architecture, from the vendor's own metadata. Installs are verified against the published checksum and unpacked into Runesmith's data
folder, `sdks/<kind>/`.

An `SdkProvider` export, one per kind, finds installed SDKs, lists available releases, installs and removes them. The `Sdk` option kind
lists them in forms, so templates and run configurations pick an SDK the same way.

- **.NET** (in the C# plugin). Releases come from Microsoft's release metadata: the release index lists the channels (11.0, 10.0, ...) with
  their support phase and latest SDK, and each channel's `releases.json` lists every SDK with its download per platform and its SHA-512
  hash. Runesmith installs SDKs side by side in one .NET root, as the official install script does, so one `dotnet` serves them all and
  `global.json` picks between them. Found SDKs come from `DOTNET_ROOT`, the `dotnet` on the `PATH` and the usual install folders. Update
  means a newer patch in the same channel; Runesmith offers it and keeps the older one until the user removes it.
- **Java** (in the Java plugin). Distributions and packages come from the foojay Discovery API, which lists builds of OpenJDK from many
  vendors (Temurin, Zulu, Corretto, Liberica, Microsoft, GraalVM and others) with their checksums. The download form filters by vendor,
  major version (marked LTS where it is), package (JDK or JDK with JavaFX) and architecture. Found JDKs come from `JAVA_HOME`, the `java`
  on the `PATH`, `/usr/lib/jvm`, `~/.jdks`, `~/.sdkman/candidates/java` and the platform's usual folders. Projects pick a JDK, and the Java
  analyzer reads that JDK's API for the project's release.

Runesmith reaches these services only when the user opens the SDKs page or a Download form, and, if the user allows it, once a day to
check for updates. On NixOS, downloaded SDKs run through `nix-ld`.

## Extension points

| Export | Lives in | Supplies |
| --- | --- | --- |
| `IToolWindowProvider` | `Runesmith.Sdk` (exists) | A tool window; its definition gains the stripe group |
| `RunConfigurationType` | `Runesmith.Sdk` | A kind of run configuration |
| `ProjectTemplateProvider` | `Runesmith.Sdk` | Project templates |
| `SdkProvider` | `Runesmith.Sdk` | Found, available and installable SDKs of a kind |
| `IBackgroundTasks` (service) | `Runesmith.Sdk` | Reporting a long task to the status bar's indicator |

## Delivery

1. **The shell.** Main toolbar with the main menu button, project widget and search; stripes; the status bar with the task indicator;
   the refined theme; the welcome screen.
2. **Options and forms.** The model and the renderer.
3. **SDKs.** The provider contract, .NET and Java providers, and the SDKs page with the download form.
4. **New projects.** The provider contract, the dialog, and the C# and Java template plugins.
5. **Run, build and debug configurations.** The model, the runner and the Run tool window, the configurations dialog, the run widget, and
   the C# and Java configuration types.
6. **Debugging**, designed separately.

Each step lands with tests (template and metadata parsing against recorded files, option conditions, the runner with a real process,
layout persistence) and with screenshots of the new screens, taken under Xvfb, checked against this document.
