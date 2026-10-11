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
