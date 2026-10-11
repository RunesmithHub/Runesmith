### Messages of folders, files, documents, search and the settings Runesmith has without a window.
### Their ids start with workspace-, the area this file is named after.

# Settings of CoreSettings.cs
workspace-color-theme = Color theme
workspace-color-theme-description = The colors of the window and the editor: dark, light, system to follow the system between the two, or the id of a plugin's theme.
workspace-accent-color = Accent color
workspace-accent-color-description = The color of selections, focus and highlights, as a hex color such as #7073F6. Leave it empty for the theme's own accent.
workspace-syntax-colors = Syntax colors
workspace-syntax-colors-description = The colors of code in the editor, by the id of a color scheme. Leave it empty for the color theme's own.
workspace-file-icons = File icons
workspace-file-icons-description = The icons of files and folders, by the id of a file icon theme.
workspace-font = Font
workspace-font-description = The editor's font; a list separated by commas tries each in turn.
workspace-font-size = Font size
workspace-font-size-description = The editor's font size, in pixels.
workspace-font-ligatures = Font ligatures
workspace-font-ligatures-description = Draw character pairs such as => and != as one symbol when the font has ligatures.
workspace-tab-size = Tab size
workspace-tab-size-description = How many spaces a tab is wide, and how many spaces the Tab key inserts.
workspace-insert-spaces = Insert spaces
workspace-insert-spaces-description = Insert spaces when Tab is pressed, rather than a tab character.
workspace-line-numbers = Line numbers
workspace-line-numbers-description = Show line numbers beside the text.
workspace-indent-guides = Indent guides
workspace-indent-guides-description = Draw thin lines that show the levels of indentation.
workspace-highlight-the-current-line = Highlight the current line
workspace-highlight-the-current-line-description = Shade the line the caret is on.
workspace-close-brackets-and-quotes = Close brackets and quotes
workspace-close-brackets-and-quotes-description = Type the closing bracket or quote when you type an opening one.
workspace-show-whitespace = Show whitespace
workspace-show-whitespace-description = Draw spaces and tabs as faint dots and arrows.
workspace-suggest-while-typing = Suggest while typing
workspace-suggest-while-typing-description = Show completions as you type, not only when you press Ctrl+Space.
workspace-format-on-save = Format on save
workspace-format-on-save-description = Format a file with the formatter of its language each time you save it.
workspace-inlay-hints = Inlay hints
workspace-inlay-hints-description = Show hints inside lines, such as parameter names and inferred types, where the language offers them.
workspace-code-lens = Code lens
workspace-code-lens-description = Show lines of information and actions above code, such as reference counts, where the language offers them.
workspace-folding = Folding
workspace-folding-description = Show fold markers in the gutter, so blocks of code can fold. Turning it off unfolds everything.
workspace-highlight-occurrences = Highlight occurrences
workspace-highlight-occurrences-description = Highlight the other uses of the symbol under the caret a moment after the caret stops, where the language offers them.
workspace-semantic-highlighting = Semantic highlighting
workspace-semantic-highlighting-description = Color names by what they mean, such as parameters and types, where the language offers it. When it is off, only the grammar colors the text.
workspace-plugins-without-keyboard-hooks = Plugins without keyboard hooks
workspace-plugins-without-keyboard-hooks-description = The ids of plugins, separated by commas, whose keyboard hooks are turned off, so they no longer see the keys you press in the editor.
workspace-auto-save = Auto save
workspace-auto-save-description = Save changed files by themselves: never, a second after you stop typing, or when the editor loses focus.
workspace-trim-trailing-whitespace = Trim trailing whitespace
workspace-trim-trailing-whitespace-description = Remove spaces and tabs at the ends of lines when saving.
workspace-insert-a-final-newline = Insert a final newline
workspace-insert-a-final-newline-description = End the file with a line break when saving.
workspace-line-ending-of-new-files = Line ending of new files
workspace-line-ending-of-new-files-description = The line ending of new files, and of files without line breaks: the system's own, LF or CRLF.
workspace-excluded-files = Excluded files
workspace-excluded-files-description = Globs, separated by semicolons, of files and folders that the Explorer, Quick Open and search leave out.
workspace-restore-the-session = Restore the session
workspace-restore-the-session-description = Open the last folder and its files again when Runesmith starts.
workspace-local-copies-replace-plugins = Local copies replace plugins
workspace-local-copies-replace-plugins-description = Let a plugin in your plugins folder run in place of the plugin with the same id from the hub, such as a build of it you work on. The local copy gets its own secrets and storage. Only your user settings can turn this on. Takes effect when Runesmith starts again.

# Choices of the settings above.
workspace-choice-off = Off
workspace-choice-after-delay = After delay
workspace-choice-on-focus-change = On focus change
workspace-choice-auto = Auto
workspace-choice-lf = LF
workspace-choice-crlf = CRLF
