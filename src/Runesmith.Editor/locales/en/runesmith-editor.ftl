### Messages of the text editor: its menus, the find bar, completion, hover and its notices.
### Their ids start with editor-, the area this file is named after.

## The find bar

editor-find-placeholder = Find
editor-replace-placeholder = Replace
# The tooltip of the button that shows and hides the replace field.
editor-find-toggle-replace = Toggle replace
# Shown instead of the number of matches when the regular expression does not parse.
editor-find-invalid-pattern = Invalid pattern
# Shown instead of the number of matches when the search took too long and stopped.
editor-find-too-slow = Too slow
editor-find-no-results = No results
# The match the caret is on, among all matches.
# Variables:
#   $position (Number) - the match's number, counted from 1
#   $total (Number) - how many matches there are
editor-find-position = { $position } of { $total }
# As editor-find-position, when there are more matches than the find bar counts.
# Variables:
#   $position (Number) - the match's number, counted from 1
#   $total (Number) - how many matches the find bar counted
editor-find-position-more = { $position } of { $total }+
# How many matches there are, when the caret is on none of them.
# Variables:
#   $total (Number) - how many matches there are
editor-find-found = { $total } found
# As editor-find-found, when there are more matches than the find bar counts.
# Variables:
#   $total (Number) - how many matches the find bar counted
editor-find-found-more = { $total }+ found
# After Replace All.
# Variables:
#   $count (Number) - how many matches were replaced
editor-find-replaced = Replaced { $count }

## Code actions, rename and popups

# The tooltip of a code action in the light bulb menu.
# Variables:
#   $source (String) - what offers the action, such as a language server's name
editor-code-action-source = From { $source }
# Under the rename box.
editor-rename-hint = Enter to rename, Escape to cancel
editor-rename-busy = Renaming...
# The tooltip of the button that undoes a change in the gutter's change popup, for added lines.
editor-change-delete-added = Delete the added lines
# The tooltip of the button that undoes a change in the gutter's change popup, for changed or deleted lines.
editor-change-restore = Put the lines back as they were
# In signature help, when a function has several overloads.
# Variables:
#   $position (Number) - the overload shown, counted from 1
#   $count (Number) - how many overloads there are
editor-signature-counter = { $position } of { $count }  (Up and Down to switch)

# The kind of a symbol, as the outline, Go to Symbol and the breadcrumbs name it.
# Variables:
#   $kind - the kind's id
editor-symbol-kind =
    { $kind ->
        [file] file
        [module] module
        [namespace] namespace
        [package] package
        [class] class
        [method] method
        [property] property
        [field] field
        [constructor] constructor
        [enum] enum
        [interface] interface
        [function] function
        [variable] variable
        [constant] constant
        [string] string
        [number] number
        [boolean] boolean
        [array] array
        [object] object
        [key] key
        [null] null
        [enummember] enum member
        [struct] struct
        [event] event
        [operator] operator
        [typeparameter] type parameter
       *[other] symbol
    }

## Find bar buttons, change popup and messages over the editor

editor-find-match-case = Match case (Alt+C)
editor-find-whole-word = Match whole word (Alt+W)
editor-find-regex = Use regular expression (Alt+R)
editor-find-previous = Previous match (Shift+Enter)
editor-find-next = Next match (Enter)
editor-find-close = Close (Escape)
editor-find-replace = Replace
editor-find-replace-tip = Replace this match (Enter)
editor-find-replace-all = Replace all
editor-find-replace-all-tip = Replace every match (Ctrl+Alt+Enter)
editor-change-rollback = Rollback
editor-change-previous = Previous change
editor-change-next = Next change
editor-change-show-diff = Show Diff
editor-change-show-diff-tip = Show the file and its base side by side
editor-nothing-to-rename = Nothing to rename here.
editor-no-code-actions = No quick fixes or refactorings here.
# Variables:
#   $language - the language's name
editor-no-formatter = No formatter for { $language } files is installed.
