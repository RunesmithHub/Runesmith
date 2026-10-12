### Messages of the window: its toolbar, menus, status bar, command palette, quick input and the core commands.
### Their ids start with shell-, the area this file is named after.

# The name of the product. Translators normally keep it.
shell-product = Runesmith

# The menus of the main menu.
shell-menu-file = File
shell-menu-edit = Edit
shell-menu-selection = Selection
shell-menu-view = View
shell-menu-go = Go
shell-menu-run = Run
shell-menu-build = Build
shell-menu-tools = Tools
shell-menu-help = Help

# Commands of CoreCommands.cs
shell-new-file = New File
shell-category-file = File
shell-new-project = New Project...
shell-open-file = Open File...
shell-open-folder = Open Folder...
shell-save = Save
shell-save-as = Save As...
shell-save-all = Save All
shell-close-editor = Close Editor
shell-close-folder = Close Folder
shell-settings = Settings
shell-exit = Exit
shell-undo = Undo
shell-category-edit = Edit
shell-redo = Redo
shell-undo-workspace-edit = Undo Workspace Edit
shell-redo-workspace-edit = Redo Workspace Edit
shell-cut = Cut
shell-copy = Copy
shell-paste = Paste
shell-find = Find
shell-replace = Replace
shell-find-next = Find Next
shell-find-previous = Find Previous
shell-find-in-files = Find in Files
shell-toggle-line-comment = Toggle Line Comment
shell-show-code-actions = Show Code Actions
shell-category-editor = Editor
shell-rename-symbol = Rename Symbol
shell-format-document = Format Document
shell-format-selection = Format Selection
shell-select-all = Select All
shell-category-selection = Selection
shell-move-line-up = Move Line Up
shell-move-line-down = Move Line Down
shell-duplicate-line = Duplicate Line
shell-delete-line = Delete Line
shell-indent = Indent
shell-outdent = Outdent
shell-command-palette = Command Palette
shell-category-view = View
shell-toggle-light-and-dark-theme = Toggle Light and Dark Theme
shell-zoom-in = Zoom In
shell-zoom-out = Zoom Out
shell-reset-zoom = Reset Zoom
shell-reset-layout = Reset Layout
shell-search-everywhere = Search Everywhere
shell-category-go = Go
shell-search-everywhere-description = Search files, symbols, commands and settings in one list; pressing Shift twice opens it too.
shell-go-to-file = Go to File...
shell-go-to-line = Go to Line...
shell-go-to-definition = Go to Definition
shell-back = Back
shell-forward = Forward
shell-trigger-suggestions = Trigger Suggestions
shell-show-parameter-hints = Show Parameter Hints
shell-next-change = Next Change
shell-previous-change = Previous Change
shell-rollback-lines = Rollback Lines
shell-show-changes = Show Changes
shell-build = Build
shell-category-build = Build
shell-cancel-build = Cancel Build
shell-welcome = Welcome
shell-category-help = Help
shell-keyboard-shortcuts = Keyboard Shortcuts
shell-documentation = Documentation
shell-about-runesmith = About Runesmith

# Commands of NavigationCommands.cs
shell-go-to-implementation = Go to Implementation
shell-find-references = Find References
shell-go-to-symbol-in-file = Go to Symbol in File...
shell-go-to-symbol-in-workspace = Go to Symbol in Workspace...
shell-show-call-hierarchy = Show Call Hierarchy
shell-show-type-hierarchy = Show Type Hierarchy
shell-expand-selection = Expand Selection
shell-shrink-selection = Shrink Selection
shell-fold = Fold
shell-unfold = Unfold
shell-fold-all = Fold All
shell-unfold-all = Unfold All

# Commands of SdkCommands.cs
shell-sdks = SDKs...
shell-sdks-description = Show, download, update and remove the SDKs of your languages.

# Commands of PerformanceMonitor.cs
shell-performance-report = Performance Report
shell-performance-report-description = Show how long typing, drawing, highlighting and language feature requests take, as percentiles.
shell-go-to-implementation-description = Go to the implementations of the interface, abstract member or virtual member at the caret.
shell-find-references-description = List every use of the symbol at the caret in the References panel.
shell-go-to-symbol-in-file-description = Find a class, function or other symbol of the active file by name.
shell-go-to-symbol-in-workspace-description = Find a class, function or other symbol anywhere in the open folder by name.
shell-show-call-hierarchy-description = Show the functions that call the function at the caret, and what they call, in the Hierarchy panel.
shell-show-type-hierarchy-description = Show the base types and derived types of the type at the caret in the Hierarchy panel.
shell-expand-selection-description = Grow the selection to the next larger element around it, such as a word, its expression or its block.
shell-shrink-selection-description = Shrink the selection back to what it was before the last expand.
shell-fold-description = Fold the innermost block around the caret.
shell-unfold-description = Unfold the folded block on the caret's line.

## Shared words

shell-ok = OK
shell-cancel = Cancel
# A dialog or button tooltip for an action with its key.
# Variables:
#   $title - the action
#   $keys - its key binding
shell-with-keys = { $title } ({ $keys })

## The toolbar

shell-main-menu = Main menu (Alt)
shell-settings-menu = Settings, plugins and themes
shell-search-label = Search everywhere
# The key to search everywhere; Shift pressed twice.
shell-search-shift-shift = Shift Shift
shell-search-tip = Search files, commands and settings (Shift Shift)
# Variables:
#   $keys - the other key binding
shell-search-tip-keys = Search files, commands and settings (Shift Shift or { $keys })
shell-no-folder = No folder
shell-recent-folders = Recent folders
shell-plugins = Plugins
shell-minimize = Minimize
shell-maximize = Maximize
shell-restore = Restore
shell-close = Close
# Hints in the empty editor area.
shell-hint-search-everywhere = Search everywhere
shell-hint-go-to-file = Go to file
shell-hint-find-in-files = Find in files
shell-hint-explorer = Show the Explorer
shell-hint-command-palette = Command palette

## The status bar

shell-tasks-tip = Show the tasks that run
# Variables:
#   $size (Number) - how many spaces an indentation step is
shell-indent-spaces = { $size } spaces
# Variables:
#   $size (Number) - how wide a tab is
shell-indent-tab = Tab size { $size }
# After an encoding such as UTF-8.
shell-with-bom = with BOM
# Variables:
#   $count (Number) - how many characters are selected
shell-selected = ({ $count } selected)
shell-running-tasks = Running tasks
shell-nothing-runs = Nothing runs now.
shell-clear-all = Clear all
shell-notifications = Notifications
shell-no-notifications = No notifications yet.
shell-line-endings = Line endings
# Variables:
#   $file - the file's name
shell-line-endings-question = Which line endings should { $file } be saved with?
shell-line-endings-lf = LF (Linux, macOS)
shell-line-endings-crlf = CRLF (Windows)
shell-line-endings-tip = Line endings; click to change them
shell-encoding-tip = The file's encoding
shell-indent-tip = Indentation; click to change it in the settings
shell-language-tip = The file's language; click to change it
# Variables:
#   $path - the folder
shell-reveal-path = Reveal { $path } in the Explorer

## Notifications of the window

# Variables:
#   $command - the command's title
shell-command-failed = { $command } failed
# Variables:
#   $name - the plugin's name
shell-plugin-not-loaded = The plugin { $name } could not load
# Variables:
#   $count (Number) - how many plugins
shell-plugins-not-loaded = { $count } plugins could not load
shell-extensions-not-loaded = Some extensions could not load
shell-see-plugins-output = The Plugins output says why.
shell-show-output = Show output
shell-plugin-start-failed = A plugin failed to start
shell-folder-missing = The folder does not exist
shell-nothing-to-open = Nothing to open
# Variables:
#   $path - the path
shell-path-missing = { $path } does not exist.
shell-build-succeeded = Build succeeded
shell-build-failed = Build failed
# Variables:
#   $seconds (Number) - how long the build took
shell-build-finished = Finished in { NUMBER($seconds, minimumFractionDigits: 1, maximumFractionDigits: 1) } s.
# Variables:
#   $errors (Number) - how many errors
#   $warnings (Number) - how many warnings
shell-build-problems =
    { $errors ->
        [one] 1 error
       *[other] { $errors } errors
    } and { $warnings ->
        [one] 1 warning
       *[other] { $warnings } warnings
    }.
shell-theme-default = Theme default

## The command palette and quick input

shell-nothing-matches = Nothing matches
shell-navigate = Navigate
shell-open = Open
shell-check = Check
shell-pick = Pick
shell-confirm = Confirm
shell-back-tip = Back (Alt+Left)
shell-check-all = Check all
shell-filter-pick-many = Type to filter, check the rows to pick
shell-filter = Type to filter
shell-input = Input
shell-press-enter = Press Enter to confirm or Esc to cancel.
# Variables:
#   $step (Number) - the step, counted from 1
#   $count (Number) - how many steps
shell-step = Step { $step } of { $count }
# Variables:
#   $plugin - the plugin that asks
shell-asked-by = Asked by { $plugin }
# Variables:
#   $problem - what went wrong
shell-list-failed = The list could not be loaded: { $problem }
shell-searching = Searching…
# Variables:
#   $count (Number) - how many rows are checked
shell-ok-count = OK ({ $count })
# For screen readers.
# Variables:
#   $label - the group's name
shell-group = Group { $label }
shell-checked = checked
shell-not-checked = not checked
shell-value-not-checked = This value could not be checked.
shell-source-everywhere = Everywhere
shell-source-everywhere-placeholder = Search files, symbols, commands and settings
shell-source-line = Go to line
# Variables:
#   $count (Number) - how many lines the file has
shell-source-line-placeholder = Type a line number between 1 and { $count }, and optionally :column
shell-source-line-no-file = Open a file first to go to a line in it
# Variables:
#   $line (Number) - the line
#   $column (Number) - the column
shell-source-line-current = Current line { $line }, column { $column }
# Variables:
#   $line (Number) - the line
shell-source-line-go = Go to line { $line }
# Variables:
#   $line (Number) - the line
#   $column (Number) - the column
shell-source-line-go-column = Go to line { $line }, column { $column }
shell-source-files = Files
shell-source-files-no-folder = Open a folder to search its files, or type > for commands
shell-source-files-placeholder = Search files by name
shell-source-commands = Commands
shell-source-commands-placeholder = Search commands
shell-source-symbols = Symbols
shell-source-symbols-no-file = Open a file first to go to its symbols
shell-source-symbols-placeholder = Type the name of a symbol in this file
# Variables:
#   $file - the file's name
shell-source-symbols-none = Nothing lists the symbols of { $file }
shell-source-workspace = Workspace
shell-source-workspace-placeholder = Type the name of a symbol
shell-source-settings = Settings
shell-source-settings-placeholder = Search settings
# Variables:
#   $category - the setting's category
shell-source-setting = Setting: { $category }

## References, hierarchies and the outline

# Variables:
#   $name - the symbol
shell-references-to = References to { $name }
# Variables:
#   $name - the symbol
shell-implementations-of = Implementations of { $name }
shell-no-references-found = No references found.
shell-no-implementations-found = No implementations found.
# Variables:
#   $kind - reference or implementation
#   $count (Number) - how many places
#   $files (Number) - in how many files
shell-references-summary =
    { $kind ->
        [implementation] { $count ->
            [one] 1 implementation
           *[other] { $count } implementations
        }
       *[reference] { $count ->
            [one] 1 reference
           *[other] { $count } references
        }
    } in { $files ->
        [one] 1 file
       *[other] { $files } files
    }
shell-no-references = No references
shell-no-references-hint = Find References (Shift+F12) and Go to Implementation (Ctrl+F12) list what they find here.
shell-no-call-target = No function to show the calls of here.
shell-no-type-target = No type to show the hierarchy of here.
shell-no-hierarchy = No hierarchy
shell-no-hierarchy-hint = Right-click a function and choose Show Call Hierarchy, or a type and choose Show Type Hierarchy.
# Variables:
#   $name - the function or type
shell-hierarchy-callers-of = Callers of { $name }
# Variables:
#   $name - the function
shell-hierarchy-calls-from = Calls from { $name }
# Variables:
#   $name - the type
shell-hierarchy-supertypes-of = Supertypes of { $name }
# Variables:
#   $name - the type
shell-hierarchy-subtypes-of = Subtypes of { $name }
shell-hierarchy-callers = Callers
shell-hierarchy-callees = Callees
shell-hierarchy-supertypes = Supertypes
shell-hierarchy-subtypes = Subtypes
shell-hierarchy-loading = Loading...
shell-hierarchy-too-deep = The hierarchy is too deep to show more.
shell-hierarchy-no-callers = No callers
shell-hierarchy-no-calls = No calls
shell-hierarchy-no-supertypes = No supertypes
shell-hierarchy-no-subtypes = No subtypes
# Variables:
#   $count (Number) - how many times it is called there
shell-hierarchy-calls = { $count } calls
shell-outline-filter = Filter symbols
shell-outline-sort = Sort by name
shell-outline-follow = Follow the caret
shell-outline-no-file = No file open
shell-outline-no-file-hint = The outline shows the symbols of the active file.
shell-outline-none = No outline
# Variables:
#   $file - the file's name
shell-outline-none-hint = Nothing lists the symbols of { $file }. A language server or plugin for its language adds them.
shell-outline-no-symbols = No symbols
# Variables:
#   $file - the file's name
shell-outline-no-symbols-hint = { $file } declares nothing to list.
shell-outline-no-match = No symbol matches the filter.

# Names of keys on key badges.
shell-key-esc = Esc
shell-key-enter = Enter
shell-key-alt-left = Alt+←
shell-undo-workspace-edit-description = Undo the last change a refactoring, code action, plugin or the Explorer made to files, with the files it created, renamed or deleted.
shell-redo-workspace-edit-description = Make the last undone workspace edit again.
shell-show-code-actions-description = Show the quick fixes and refactorings for the caret's line or the selection.
shell-rename-symbol-description = Rename the symbol at the caret everywhere it is used.
shell-format-document-description = Format the whole file with the formatter of its language.
shell-format-selection-description = Format the selected lines with the formatter of their language.
shell-next-change-description = Move to the next lines that differ from the base, such as the last commit.
shell-rollback-lines-description = Put the changed lines at the caret or in the selection back as they are in the base.
shell-show-changes-description = Show the file beside its base, such as the last commit.
shell-build-description = Build the selected run configuration's project, or the open folder.

## Leftovers of the toolbar, status bar and appearance

shell-clone-repository = Clone Repository...
shell-go-to-line-tip = Go to line
shell-current = Current
shell-name-required = Type a name.
shell-building = Building
shell-follow-system = Follow the system
# Variables:
#   $dark - the dark theme
#   $light - the light theme
shell-dark-or-light = { $dark } or { $light }
shell-theme-own = The color theme's own
shell-not-installed = Not installed
shell-dark = Dark
shell-light = Light
# Variables:
#   $kind - dark or light
#   $plugin - the plugin it comes from
shell-kind-from-plugin = { $kind }, { $plugin }
