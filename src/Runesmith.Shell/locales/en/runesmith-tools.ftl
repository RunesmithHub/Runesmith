### Messages of the tool windows: Run, Debug, Tests, Terminal, Explorer, Search, Problems, Output, plugin tree views and web views.
### Their ids start with tools-, the area this file is named after.

# The categories of the commands below in the command palette.
tools-category-run = Run
tools-category-debug = Debug
tools-category-test = Test
tools-category-terminal = Terminal
tools-category-view = View

# Commands of RunCommands.cs
tools-run-run = Run
tools-run-run-description = Run the selected run configuration.
tools-run-debug = Debug
tools-run-debug-description = Debug the selected run configuration.
tools-run-stop = Stop
tools-run-stop-description = Stop the selected configuration's run, or the last one that runs.
tools-run-edit-configurations = Edit Configurations...
tools-run-edit-configurations-description = Add, change and remove run configurations.

# Commands of DebugCommands.cs
tools-debug-toggle-breakpoint = Toggle Breakpoint
tools-debug-toggle-breakpoint-description = Add a breakpoint to the line, or remove its breakpoint.
tools-debug-edit-breakpoint = Edit Breakpoint...
tools-debug-edit-breakpoint-description = Set the breakpoint's condition, hit count or log message.
tools-debug-add-log-point = Add Log Point...
tools-debug-add-log-point-description = Write a message to the debug console when the line runs, without stopping.
tools-debug-enable-or-disable-breakpoint = Enable or Disable Breakpoint
tools-debug-enable-or-disable-breakpoint-description = Turn the line's breakpoint off without removing it, or on again.
tools-debug-remove-all-breakpoints = Remove All Breakpoints
tools-debug-remove-all-breakpoints-description = Remove every breakpoint in the folder.
tools-debug-add-watch = Add Watch...
tools-debug-add-watch-description = Watch an expression's value while debugging.
tools-debug-continue = Continue
tools-debug-continue-description = Resume the paused program, or start debugging the selected configuration.
tools-debug-pause = Pause
tools-debug-pause-description = Pause the running program.
tools-debug-step-over = Step Over
tools-debug-step-over-description = Run the current line and pause at the next one.
tools-debug-step-into = Step Into
tools-debug-step-into-description = Step into the call on the current line.
tools-debug-step-out = Step Out
tools-debug-step-out-description = Run until the current function returns.
tools-debug-restart-debugging = Restart Debugging
tools-debug-restart-debugging-description = Stop the program and debug it again.
tools-debug-stop-debugging = Stop Debugging
tools-debug-stop-debugging-description = End the program and the debugger.

# Commands of TestCommands.cs
tools-tests-run-all-tests = Run All Tests
tools-tests-run-all-tests-description = Run every test in the folder.
tools-tests-debug-all-tests = Debug All Tests
tools-tests-debug-all-tests-description = Debug every test in the folder.
tools-tests-rerun-failed-tests = Rerun Failed Tests
tools-tests-rerun-failed-tests-description = Run the tests that failed again.
tools-tests-rerun-last-tests = Rerun Last Tests
tools-tests-rerun-last-tests-description = Run the tests of the last run again.
tools-tests-run-tests-at-caret = Run Tests at Caret
tools-tests-run-tests-at-caret-description = Run the test or class the caret is in.
tools-tests-debug-tests-at-caret = Debug Tests at Caret
tools-tests-debug-tests-at-caret-description = Debug the test or class the caret is in.
tools-tests-run-tests-in-file = Run Tests in File
tools-tests-run-tests-in-file-description = Run the tests of the file in the editor.
tools-tests-stop-tests = Stop Tests
tools-tests-stop-tests-description = Stop the tests that run.
tools-tests-refresh-tests = Refresh Tests
tools-tests-refresh-tests-description = Find the folder's tests again.
tools-tests-run-test = Run Test
tools-tests-debug-test = Debug Test
tools-tests-show-test-result = Show Test Result

# Commands of TerminalCommands.cs
tools-terminal-new-terminal = New Terminal
tools-terminal-new-terminal-description = Start your shell in a new terminal tab.
tools-terminal-kill-terminal = Kill Terminal
tools-terminal-kill-terminal-description = End the selected terminal's program and close its tab.
tools-terminal-clear-terminal = Clear Terminal
tools-terminal-clear-terminal-description = Clear the selected terminal's screen and scrollback.
tools-terminal-run-selected-text-in-terminal = Run Selected Text in Terminal
tools-terminal-run-selected-text-in-terminal-description = Type the editor's selection, or its line, into the terminal and press Enter.
tools-terminal-open-in-terminal = Open in Terminal
tools-terminal-open-in-terminal-description = Start your shell in this folder.

## Debug

tools-debug-remove-all-title = Remove all breakpoints
# Variables:
#   $count (Number) - how many breakpoints
tools-debug-remove-all-question = Remove all { $count } breakpoints in the folder?
tools-debug-remove-all-button = Remove All
tools-debug-add-watch-title = Add Watch
tools-debug-edit-watch-title = Edit Watch
# The label of the field in the Add Watch and Edit Watch dialogs.
tools-debug-expression = Expression
tools-debug-expression-required = Type an expression.
# Why the program paused, after "paused".
tools-debug-reason-breakpoint = at a breakpoint
tools-debug-reason-step = after a step
tools-debug-reason-exception = on an exception
tools-debug-reason-pause = on request
tools-debug-reason-entry = at the start
# The title of the Debug panel while a program is debugged.
# Variables:
#   $name - the run configuration's name
#   $reason - why it paused, one of tools-debug-reason, or nothing
tools-debug-title-paused = { $name }, paused { $reason }
# Variables:
#   $name - the run configuration's name
tools-debug-title-paused-plain = { $name }, paused
# Variables:
#   $name - the run configuration's name
tools-debug-title-starting = { $name }, starting
# Variables:
#   $name - the run configuration's name
tools-debug-title-running = { $name }, running
tools-debug-console-empty = No output yet
tools-debug-console-empty-hint = The program's output and your evaluations show here while you debug.
tools-debug-console-input = Evaluate an expression in the paused frame and press Enter
tools-debug-tab-variables = Variables
tools-debug-tab-console = Console
tools-debug-tab-breakpoints = Breakpoints
tools-debug-add-watch-tip = Add a watch expression
tools-debug-pane-call-stack = Call Stack
tools-debug-pane-variables = Variables
tools-debug-pane-watch = Watch
tools-debug-watch-hint = Press + to watch an expression, or right-click a variable and choose Add to Watch.
tools-debug-no-source = No source for this frame
tools-debug-copy-value = Copy Value
tools-debug-add-to-watch = Add to Watch
tools-debug-edit-watch = Edit Watch...
tools-debug-remove-watch = Remove Watch
tools-copy-line = Copy Line
tools-copy-all = Copy All
tools-clear = Clear
# Before the exception filters of a debugger, such as "All exceptions".
tools-debug-stop-on = Stop on
tools-debug-enable-all = Enable all breakpoints
tools-debug-disable-all = Disable all breakpoints
tools-debug-remove-all = Remove all breakpoints
tools-debug-no-breakpoints = No breakpoints
tools-debug-no-breakpoints-hint = Click beside a line number, or press F9 on a line, to add one.
tools-debug-breakpoint-not-set = Not set yet
tools-debug-go-to-line = Go to Line
tools-remove = Remove
# The breakpoint dialog.
tools-breakpoint-condition-placeholder = Such as count > 3
tools-breakpoint-hit-count-placeholder = Such as 5
tools-breakpoint-log-placeholder = Such as count is {"{"}count{"}"}
tools-breakpoint-enabled = Enabled
tools-ok = OK
tools-cancel = Cancel
# Variables:
#   $file - the file's name
#   $line (Number) - the line, counted from 1
tools-breakpoint-title = Breakpoint at { $file }:{ NUMBER($line, useGrouping: "false") }
tools-breakpoint-condition = Condition
tools-breakpoint-condition-hint = Stops only when this expression is true.
tools-breakpoint-hit-count = Hit count
tools-breakpoint-hit-count-hint = Stops only once the line has run this many times.
tools-breakpoint-log = Log message
tools-breakpoint-log-hint = Writes this to the debug console instead of stopping; put expressions in braces.

## Run

tools-run-choose-first = Choose a run configuration first.
# Variables:
#   $type - the configuration type, such as .NET project
tools-run-cannot-debug-type = { $type } configurations cannot be debugged.
tools-run-no-debugger = No installed plugin provides a debugger.
tools-run-save-failed = The run configurations could not be saved
# Variables:
#   $name - the run configuration's name
#   $seconds (Number) - how long the build took
tools-run-built-in = { $name } built in { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
# Variables:
#   $errors (Number) - how many errors
#   $warnings (Number) - how many warnings
tools-run-build-problems =
    { $errors ->
        [one] 1 error
       *[other] { $errors } errors
    } and { $warnings ->
        [one] 1 warning
       *[other] { $warnings } warnings
    }.
tools-run-build-succeeded = Build succeeded
tools-run-build-failed = Build failed
# Variables:
#   $name - the run configuration's name
tools-run-rerun-title = { $name } is running
# Variables:
#   $name - the run configuration's name
tools-run-rerun-question = Stop { $name } and run it again?
tools-run-rerun-button = Stop and Rerun
# Background work in the status bar.
# Variables:
#   $name - the run configuration's name
tools-run-building-task = Building { $name }
# Variables:
#   $name - the run configuration's name
tools-run-running-task = Running { $name }
# Lines the run console writes itself.
# Variables:
#   $step - what failed, one of the tools-run-step messages
#   $name - the run configuration's name
tools-run-step-failed = { $step } failed, so { $name } did not start.
tools-run-step-build = The build
# Variables:
#   $name - the run configuration that runs first
tools-run-step-configuration = Running { $name }
# Variables:
#   $command - the command line
tools-run-step-command = The command { $command }
# Variables:
#   $seconds (Number) - how long the program ran
tools-run-stopped-after = Stopped after { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
# Variables:
#   $code (Number) - the exit code
#   $seconds (Number) - how long the program ran
tools-run-finished-with-code = Process finished with exit code { NUMBER($code, useGrouping: "false") } in { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
# Variables:
#   $seconds (Number) - how long the program ran
tools-run-debugging-ended = Debugging ended after { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
# Variables:
#   $seconds (Number) - how long the program ran
tools-run-finished = Process finished in { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
tools-run-in-terminal = The program runs in the Terminal panel.
# The status line of a run.
# Variables:
#   $seconds (Number) - the time
tools-run-time = { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s
# As tools-run-time, from a minute on.
# Variables:
#   $seconds (Number) - the time
tools-run-time-whole = { NUMBER($seconds, maximumFractionDigits: 0) } s
# Variables:
#   $time - how long, tools-run-time
tools-run-status-preparing = Preparing  { $time }
# Variables:
#   $time - how long, tools-run-time
tools-run-status-running = Running  { $time }
# Variables:
#   $time - how long, tools-run-time
tools-run-status-stopped = Stopped after { $time }
tools-run-status-not-started = Did not start
# Variables:
#   $code (Number) - the exit code
#   $time - how long, tools-run-time
tools-run-status-exit-code = Exit code { NUMBER($code, useGrouping: "false") }  { $time }
tools-run-input = Type input for the program and press Enter
tools-run-nothing-yet = Nothing has run yet
tools-run-nothing-yet-hint = Choose a run configuration in the toolbar and press Run, or Shift+F10.
tools-run-close-tip = Close; stops the run when it still runs
# The run toolbar.
tools-run-finding = Finding configurations...
tools-run-add-configuration = Add Configuration...
tools-run-choose-tip = Choose or add a run configuration
# Variables:
#   $name - the run configuration's name
#   $type - its type
tools-run-selector-tip = { $name } ({ $type })
tools-run-no-configurations-yet = No configurations yet
# Tooltips of the run toolbar's buttons.
# Variables:
#   $name - the selected run configuration's name
tools-run-tip-run = Run { $name }
# Variables:
#   $name - the selected run configuration's name
tools-run-tip-debug = Debug { $name }
# Variables:
#   $name - the selected run configuration's name
#   $reason - why it can't be debugged
tools-run-tip-debug-reason = Debug { $name }: { $reason }
tools-run-tip-debug-none = Debug
# Variables:
#   $reason - why it can't be debugged
tools-run-tip-debug-none-reason = Debug: { $reason }
tools-run-tip-stop = Stop
tools-run-tip-build-none = Build
# Variables:
#   $name - the selected run configuration's name
tools-run-tip-build = Build { $name }
# The Run Configurations dialog.
tools-run-dialog-title = Run Configurations
tools-run-add-tip = Add a configuration
tools-run-remove-tip = Remove the configuration
tools-run-copy-tip = Copy the configuration
tools-run-none = No run configurations
tools-run-none-hint = Add one with the + button. Configurations for runnable projects appear here by themselves.
# Variables:
#   $type - the configuration type's id
tools-run-missing-type = No plugin provides the configuration type { $type }; turn on the plugin that adds it to change the settings.
tools-run-option-name = Name
tools-run-option-name-placeholder = My configuration
tools-run-option-store = Store as
tools-run-option-shared = Shared
tools-run-option-shared-description = Saved in .runesmith/run.json, so it can be committed with the folder.
tools-run-option-local = Local
tools-run-option-local-description = Saved only for you, outside the folder.
tools-run-option-store-description = Shared configurations are saved in .runesmith/run.json; local ones only for you.
# Variables:
#   $name - the name that is taken
tools-run-name-taken = Another configuration is named { $name }.
tools-run-detected-note = Found in the folder. Changing it saves it as your own.
tools-run-add-step = Add step
tools-run-before-launch = Before launch
tools-run-no-steps = Nothing runs first; the program starts as it is.
tools-run-step-menu-build = Build
tools-run-step-menu-build-tip = Build the configuration's project
tools-run-step-menu-configuration = Run Configuration
tools-run-step-menu-command = Command
tools-run-step-menu-command-tip = Run a command line in the folder
tools-run-command-placeholder = A command line, such as npm install
tools-run-move-up = Move up
tools-run-move-down = Move down
tools-run-remove-step = Remove the step
tools-run-no-types = No plugin adds configuration types
# The tag beside a configuration in the list.
tools-run-tag-found = found
tools-run-tag-local = local

## Tool window names

tools-window-debug = Debug
tools-window-hierarchy = Hierarchy
tools-window-run = Run
tools-window-plugins = Plugins
tools-window-explorer = Explorer
tools-window-references = References
tools-window-problems = Problems
tools-window-test-explorer = Test Explorer
tools-window-outline = Outline
tools-window-output = Output
tools-window-terminal = Terminal
tools-window-search = Search
# The command that shows a tool window.
# Variables:
#   $name - the tool window's name
tools-window-show = Show { $name }

## Terminal

tools-terminal-start-failed = The terminal could not start
tools-terminal-none = No terminal is open
tools-terminal-none-hint = Start your shell in the open folder.
tools-terminal-close-tip = Close; ends the terminal's program
# The tooltip of a terminal tab whose program ended.
# Variables:
#   $title - the terminal's title
tools-terminal-ended = { $title } (ended)
# A tooltip with the key that runs it.
# Variables:
#   $title - what the button does
#   $keys - the key binding, such as Ctrl+Shift+`
tools-with-keys = { $title } ({ $keys })

## Tests

tools-tests-collapse-all = Collapse All
tools-tests-filter = Filter by name, or @tag
tools-tests-failed-only = Show only failed tests
tools-tests-group-project = Group by project
tools-tests-group-file = Group by file
tools-tests-group-status = Group by status
tools-tests-group-tag = Group by tag
tools-tests-no-provider = No test provider
tools-tests-no-provider-hint = Install a plugin that finds tests, such as the plugin for your language, from the plugin hub.
tools-tests-no-folder = No folder is open
tools-tests-no-folder-hint = Open a folder to see its tests.
tools-tests-finding = Finding tests...
tools-tests-finding-hint = The tests show here as they are found.
tools-tests-no-match = No tests match
tools-tests-no-match-hint = Change the filter, or show every test.
tools-tests-none = No tests found
tools-tests-none-hint = Build the folder, then refresh the tests.
# The counts in the Test Explorer's summary, as tooltips.
# Variables:
#   $count (Number) - how many tests
tools-tests-count-passed = { $count } passed
# Variables:
#   $count (Number) - how many tests
tools-tests-count-failed = { $count } failed
# Variables:
#   $count (Number) - how many tests
tools-tests-count-skipped = { $count } skipped
# Variables:
#   $count (Number) - how many tests
tools-tests-count-running = { $count } running or waiting
# Variables:
#   $count (Number) - how many tests
tools-tests-count-not-run = { $count } not run
# Variables:
#   $count (Number) - how many tests
tools-tests-total =
    { $count ->
        [one] 1 test
       *[other] { $count } tests
    }
# Variables:
#   $shown (Number) - how many tests show
#   $count (Number) - how many tests there are
tools-tests-shown-of =
    { $count ->
        [one] { $shown } of 1 test
       *[other] { $shown } of { $count } tests
    }
# After the summary.
# Variables:
#   $time - how long, such as 1.2 s
tools-tests-running-for = running { $time }
# Variables:
#   $time - how long, such as 1.2 s
tools-tests-last-run = last run { $time }
tools-tests-go-to-source = Go to Source
tools-tests-not-run = This test has not run yet.
tools-tests-running = Running...
tools-tests-waiting = Waiting to run...
# Variables:
#   $reason - why the test was skipped
tools-tests-skipped-reason = Skipped: { $reason }
tools-tests-skipped = Skipped.
tools-tests-stack-trace = Stack trace
tools-tests-debugging = Debugging tests
tools-tests-running-title = Running tests
tools-tests-stopped = The last run was stopped
tools-tests-last-run-title = Last run
tools-tests-select = Select a test to see its result and output. Run tests with the buttons above or from the gutter beside them.
tools-tests-run-output = Run output
# Variables:
#   $path - the file's path
tools-open-path = Open { $path }

## Explorer, Search, Problems and Output

tools-explorer-new-file-tip = New file
tools-explorer-new-folder-tip = New folder
tools-refresh = Refresh
tools-collapse-all = Collapse all
tools-explorer-none = No folder open
tools-explorer-none-hint = Open a folder to see its files here.
tools-explorer-open-folder = Open Folder
tools-explorer-new-file = New File
tools-explorer-new-folder = New Folder
tools-explorer-copy-path = Copy Path
tools-explorer-copy-relative-path = Copy Relative Path
tools-explorer-reveal-finder = Reveal in Finder
tools-explorer-reveal = Reveal in File Manager
# Variables:
#   $folder - the folder's name
tools-explorer-name-in = Name, in { $folder }
tools-explorer-rename = Rename
# Variables:
#   $name - the file's or folder's name
tools-explorer-new-name = New name for { $name }
# Variables:
#   $name - the file's or folder's name
tools-explorer-create-failed = { $name } could not be created
# Variables:
#   $name - the file's or folder's name
tools-explorer-rename-failed = { $name } could not be renamed
# Variables:
#   $name - the file's or folder's name
tools-explorer-delete-failed = { $name } could not be deleted
tools-explorer-delete-title = Delete?
# Variables:
#   $name - the file's name
tools-explorer-delete-file = Delete { $name }?
# Variables:
#   $name - the folder's name
tools-explorer-delete-folder = Delete the folder { $name } and everything in it?
tools-explorer-delete = Delete
tools-search-placeholder = Search
tools-search-include = Files to include, such as *.cs; src/**
tools-search-exclude = Files to exclude
tools-search-match-case = Match case
tools-search-whole-word = Match whole word
tools-search-regex = Use regular expression
tools-search-open-folder = Open a folder to search its files.
tools-search-searching = Searching...
tools-search-no-results = No results.
# Variables:
#   $results (Number) - how many matches
#   $files (Number) - in how many files
tools-search-summary =
    { $results ->
        [one] 1 result
       *[other] { $results } results
    } in { $files ->
        [one] 1 file
       *[other] { $files } files
    }.
# As tools-search-summary, when the search stopped at its limit.
# Variables:
#   $results (Number) - how many matches
#   $files (Number) - in how many files
#   $limit (Number) - the limit
tools-search-summary-limited =
    { $results ->
        [one] 1 result
       *[other] { $results } results
    } in { $files ->
        [one] 1 file
       *[other] { $files } files
    }, stopped at { $limit }.
# Variables:
#   $error - what is wrong with it
tools-search-invalid = The pattern is not valid: { $error }
tools-problems-none = No problems
tools-problems-none-hint = Errors and warnings from the language analyzers and builds show here.
# Where a problem is.
# Variables:
#   $source - what reported it, such as C# CS0029
#   $line (Number) - the line
#   $column (Number) - the column
tools-problems-where = { $source }  [Ln { NUMBER($line, useGrouping: "false") }, Col { NUMBER($column, useGrouping: "false") }]
tools-output-none-yet = No output yet
tools-output-clear = Clear the channel
tools-output-none = No output
tools-output-none-hint = Builds, language analyzers and plugins write their logs here.

## Plugin tree views, web views and diffs

tools-tree-nothing = Nothing to show
tools-tree-filter = Filter
# Variables:
#   $name - the tree view's name
tools-tree-filter-name = Filter { $name }
tools-tree-load-failed = The items could not be loaded
tools-tree-no-match = Nothing matches the filter
tools-tree-loading = Loading…
# Variables:
#   $problem - what went wrong
tools-tree-could-not-load = Could not load the items: { $problem }
tools-web-unavailable = Web content cannot be shown
tools-diff-previous = Previous difference (Shift+F7)
tools-diff-next = Next difference (F7)
tools-diff-none = No differences
# Variables:
#   $count (Number) - how many differences
tools-diff-count =
    { $count ->
        [one] 1 difference
       *[other] { $count } differences
    }
# Variables:
#   $position (Number) - the difference shown, counted from 1
#   $count (Number) - how many differences
tools-diff-position = { $position } of { $count }
