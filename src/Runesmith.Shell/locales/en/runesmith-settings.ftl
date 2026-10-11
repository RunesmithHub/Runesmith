### Messages of the settings page, the welcome screen, the shortcuts page, new projects and forms.
### Their ids start with settings-, the area this file is named after.

# Settings of HubSettings.cs
settings-use-the-plugin-hub = Use the plugin hub
settings-use-the-plugin-hub-description = Turn this off to use only the plugins in your plugins folder. Takes effect when Runesmith starts again.
settings-allowed-plugins = Allowed plugins
settings-allowed-plugins-description = Which plugins may be installed. Unverified plugins always ask you to confirm before they install.
settings-update-automatically = Update automatically
settings-update-automatically-description = Official and verified plugins update by themselves within their version line. Unverified plugins never do.
settings-check-for-changes-every = Check for changes every
settings-check-for-changes-every-description = How many hours apart Runesmith checks the plugin hub, from 1 to 24.
settings-open-runesmith-links = Open runesmith:// links
settings-open-runesmith-links-description = Whether Runesmith registers itself on Windows to open runesmith:// links from the hub's website. Takes effect when Runesmith starts again.
settings-pre-releases = Pre-releases
settings-pre-releases-description = The ids of the plugins you get pre-releases of, separated by semicolons.
settings-plugins-not-to-suggest = Plugins not to suggest
settings-plugins-not-to-suggest-description = The ids of the official plugins Runesmith no longer suggests for the files and folders you open, separated by semicolons.

# Settings of RunSettings.cs
settings-run-in-the-terminal = Run in the terminal
settings-run-in-the-terminal-description = Run a configuration's program in a tab of the Terminal panel, where it can use colors, the cursor and full-screen input, instead of the Run panel's console. Debugging still uses the Run panel.

# Settings of ShellSettings.cs
settings-turned-off-plugins = Turned off plugins
settings-turned-off-plugins-description = The ids of the plugins Runesmith does not load, separated by semicolons.
settings-menus = Menus
settings-menus-description = Where the menus are: behind the menu button at the left of the toolbar, or in a menu bar of their own under it.
settings-title-bar = Title bar
settings-title-bar-description = Whether the toolbar is the window's title bar, with its own window buttons, or the system's title bar stays above it. Takes effect when Runesmith starts again. macOS always keeps its own.
settings-interface-font-size = Interface font size
settings-interface-font-size-description = The size of the text of menus, panels and the status bar, in pixels.
settings-breadcrumbs = Breadcrumbs
settings-breadcrumbs-description = Show the file's path under the tabs. When this is off, the status bar shows it.

# Settings of TestSettings.cs
settings-run-tests-on-save = Run tests on save
settings-run-tests-on-save-description = Run a file's tests when you save it. Saving a file without tests runs the last run's tests again.

# Settings of TerminalSettings.cs
settings-shell = Shell
settings-shell-description = The program new terminals start, such as /bin/zsh or pwsh.exe. Leave it empty for your default shell: $SHELL on Linux and macOS, PowerShell or cmd on Windows.
settings-font-family = Font family
settings-font-family-description = The terminal's font. Leave it empty to use the editor's font.
settings-font-size = Font size
settings-font-size-description = The terminal's font size in pixels, or 0 to use the editor's.
settings-scrollback = Scrollback
settings-scrollback-description = How many lines a terminal keeps after they scroll off its top.
settings-shell-integration = Shell integration
settings-shell-integration-description = Let bash and zsh tell Runesmith their folder, so links to files open from it and new terminals start in it. Takes effect in new terminals.
settings-blinking-cursor = Blinking cursor
settings-blinking-cursor-description = Make the terminal's cursor blink.

# Choices of the settings above.
settings-choice-all-with-warnings = All, with warnings
settings-choice-official-and-verified = Official and verified
settings-choice-official-only = Official only
settings-choice-toolbar = Toolbar
settings-choice-separate = Separate
settings-choice-custom = Custom
settings-choice-system = System

## The settings page's categories

settings-category-appearance = Appearance
settings-category-editor = Editor
settings-category-files = Files
settings-category-window = Window
settings-category-languages = Languages
settings-category-plugins = Plugins
settings-category-sdks = SDKs
settings-category-terminal = Terminal
settings-category-run = Run
settings-category-testing = Testing

## The welcome screen

settings-welcome-search = Search projects
settings-welcome-projects = Projects
settings-welcome-customize = Customize
settings-welcome-plugins = Plugins
settings-welcome-back = Back to the workbench
# When a project was last opened.
settings-ago-now = Just now
# Variables:
#   $count (Number) - how many minutes ago
settings-ago-minutes =
    { $count ->
        [one] 1 minute ago
       *[other] { $count } minutes ago
    }
# Variables:
#   $count (Number) - how many hours ago
settings-ago-hours =
    { $count ->
        [one] 1 hour ago
       *[other] { $count } hours ago
    }
settings-ago-yesterday = Yesterday
# Variables:
#   $count (Number) - how many days ago
settings-ago-days =
    { $count ->
        [one] 1 day ago
       *[other] { $count } days ago
    }
# Variables:
#   $date (Date) - when
settings-ago-date = { DATETIME($date, dateStyle: "medium") }
settings-welcome-new-project = New Project
settings-welcome-open = Open
settings-welcome-clone = Clone
settings-welcome-no-recent = No recent projects
settings-welcome-no-match = No project matches
settings-welcome-no-recent-hint = Open a folder or create a project; it shows here next time.
settings-welcome-no-match-hint = Try another part of the name or path.
# Variables:
#   $path - the project's folder
settings-welcome-open-path = Open { $path }
settings-welcome-remove-recent = Remove from recent projects
settings-keymap-default = Default
settings-keymap-tip = Other keymaps come in a later version; the keyboard shortcuts page changes single bindings.
settings-customize-theme = Color theme
settings-customize-theme-hint = The colors of the window and the editor. Plugins can add more.
settings-customize-accent = Accent color
settings-customize-accent-hint = Selections, focus and highlights.
settings-customize-syntax = Syntax colors
settings-customize-syntax-hint = The colors of code in the editor.
settings-customize-icons = File icons
settings-customize-icons-hint = The icons of files and folders.
settings-customize-ui-font = Interface font size
settings-customize-ui-font-hint = The text of menus, panels and the status bar.
settings-customize-editor-font = Editor font size
settings-customize-editor-font-hint = The text of the editor.
settings-customize-keymap = Keymap
settings-customize-keymap-hint = The set of keyboard shortcuts.

## The settings page

settings-title = Settings
settings-search = Search settings
# The scopes of settings.
settings-scope-user = User
settings-scope-folder = Folder
settings-scope-tip = User settings apply everywhere; folder settings apply to the open folder only and override the user's.
settings-open-file = Open settings file
settings-no-match = No settings match
settings-no-match-hint = Try other words, or clear the search.
settings-section-failed = A plugin's settings section could not be added
# Variables:
#   $error - what went wrong
settings-section-not-shown = This section could not be shown: { $error }
# Added to a setting's description.
settings-folder-wins = The open folder sets its own value, which wins.
settings-user-only = Switch to User to change it.
settings-remove-folder-value = Remove the folder's value
settings-reset = Reset to the default
# Variables:
#   $setting - the setting's title
settings-change-failed = { $setting } could not be changed
settings-manage-plugins = Manage plugins
settings-open-plugins-folder = Open the plugins folder

## The keyboard shortcuts page

settings-shortcuts-title = Keyboard Shortcuts
settings-shortcuts-search = Search commands or keys
# Variables:
#   $path - the file of the user's key bindings
settings-shortcuts-hint = Click a binding to change it. Your bindings are saved in { $path }.
settings-shortcuts-add = Add a binding
settings-shortcuts-change = Change the binding
# Variables:
#   $command - the command's title
settings-shortcuts-dialog-title = Key binding of { $command }
settings-shortcuts-dialog-hint = Type keys such as Ctrl+Shift+K; separate several with | . Leave it empty to remove the binding.
settings-shortcuts-unknown = Some keys are not known. Write modifiers first, then one key, such as Ctrl+Alt+Down or F5.

## About Runesmith

settings-about-version = Version
settings-about-api = Plugin API
settings-about-system = System
settings-about-plugins = Plugins
# Variables:
#   $count (Number) - how many plugins are loaded
settings-about-plugins-loaded =
    { $count ->
        [one] 1 loaded
       *[other] { $count } loaded
    }
settings-about-settings = Settings
settings-about-logs = Logs
settings-about-tagline = A fast, extensible code editor
settings-close = Close

## New projects

settings-new-project = New Project
settings-new-project-search = Search templates
settings-new-project-create = Create
settings-cancel = Cancel
settings-new-project-loading = Loading templates...
settings-new-project-no-templates = No templates are installed. Install a template plugin, such as C# templates or Java templates, from Tools > Plugins, or check that yours are on there.
settings-new-project-no-match = No templates match. Try other words or clear the filters.
# Variables:
#   $folder - the folder the project goes into
settings-new-project-target = The project will be created in { $folder }
# Variables:
#   $name - the project's name
settings-new-project-creating = Creating { $name }...
settings-new-project-creating-git = Creating the Git repository...
settings-template-about = About this template
settings-template-description = Description
settings-template-author = Author
settings-template-tags = Tags
settings-template-source = Source
settings-template-id = Id
settings-drag-to-resize = Drag to resize
settings-project-name = Name
settings-project-name-placeholder = MyProject
settings-project-location = Location
settings-project-git = Create Git repository
settings-project-location-required = Type a full path, or choose a folder.
settings-project-name-required = Type a name.

## SDKs

settings-sdks-none = No plugin supplies SDKs.
# Variables:
#   $name - the SDK, such as .NET SDK 10.0.100
settings-sdks-installing = Installing { $name }
# Variables:
#   $name - the SDK
settings-sdks-installed = { $name } is installed
# Variables:
#   $name - the SDK
settings-sdks-install-failed = Could not install { $name }
settings-sdks-download = Download...
# Variables:
#   $kind - the kind of SDK, such as .NET SDK
settings-sdks-refresh-tip = Look again for installed copies of { $kind }.
# Variables:
#   $kind - the kind of SDK, such as .NET SDK
settings-sdks-none-found = No { $kind } was found.
# Variables:
#   $error - what went wrong
settings-sdks-updates-failed = Could not look for updates: { $error }
settings-sdks-default = Default
# Variables:
#   $kind - the kind of SDK, such as .NET SDK
settings-sdks-default-tip = Builds and new projects use this { $kind }.
settings-remove = Remove
# Variables:
#   $version - the newer version
settings-sdks-update-to = Update to { $version }
settings-sdks-update-tip = Installs the newer version next to this one; remove the older one when nothing needs it.
# Variables:
#   $name - the SDK
settings-sdks-remove-title = Remove { $name }?
# Variables:
#   $path - its folder
settings-sdks-remove-question = Runesmith deletes it from { $path }.
# Variables:
#   $name - the SDK
settings-sdks-removed = { $name } is removed
# Variables:
#   $name - the SDK
settings-sdks-remove-failed = Could not remove { $name }
settings-install = Install
# Variables:
#   $kind - the kind of SDK, such as .NET SDK
settings-sdks-download-title = Download { $kind }
settings-sdks-dotnet-source = Releases from Microsoft. Runesmith installs .NET SDKs side by side in one folder, and global.json picks between them.
settings-sdks-java-source = Builds of OpenJDK from their vendors: Temurin from Adoptium, the others as the foojay Discovery API lists them.
settings-sdks-loading = Loading the releases...
# Variables:
#   $error - what went wrong
settings-sdks-load-failed = Could not load the releases: { $error }
# Variables:
#   $kind - the kind of SDK, such as .NET SDK
settings-sdks-nothing = There is no { $kind } to download for this computer.
settings-sdks-out-of-support = out of support
settings-sdks-maintenance = maintenance
settings-sdks-starting = Starting
settings-sdks-cancelled = The install was cancelled.
# Variables:
#   $error - what went wrong
settings-sdks-failed = Could not install: { $error }

## Forms of options, such as a run configuration's

settings-form-advanced = Advanced
settings-form-choose = Choose...
settings-form-choose-folder = Choose a folder
settings-form-choose-file = Choose a file
settings-form-add-variable = Add variable
settings-form-add = Add
settings-form-name = Name
settings-form-value = Value
settings-form-looking-for-sdks = Looking for SDKs...
settings-form-no-sdk = No SDK found
settings-form-choose-sdk = Choose an SDK
# The badge of the SDK builds use by default.
settings-form-default = default
# The size of a download that is not known.
settings-sdks-size-unknown = Unknown
