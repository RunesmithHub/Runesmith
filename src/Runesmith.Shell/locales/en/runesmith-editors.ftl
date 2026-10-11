### Messages of the editors: opening, saving and reloading files, Open With, and edits across files.
### Their ids start with editors-, the area this file is named after.

# The category of file commands in the command palette.
editors-category-file = File
editors-open-with = Open With...
editors-open-with-description = Open the file in another editor, such as the text editor or an image viewer.
editors-set-default = Set Default Editor...
editors-set-default-description = Choose the editor files of this kind open in.
editors-revert = Revert File
editors-revert-description = Read the file from disk again, dropping unsaved changes.

## Notifications; $file is a file's name, such as Program.cs, and the error follows as the message

# Variables:
#   $file - the file's name
editors-open-failed = { $file } could not be opened
# Variables:
#   $file - the file's name
editors-save-failed = { $file } could not be saved
# Variables:
#   $file - the file's name
editors-reload-failed = { $file } could not be read again
# The message of editors-open-failed when a plugin's editor failed.
# Variables:
#   $editor - the editor's name
#   $error - what went wrong
editors-editor-failed = { $editor } failed: { $error }
# Variables:
#   $file - the file's name
editors-changed-on-disk = { $file } changed on disk
# Variables:
#   $file - the file's name
editors-deleted-on-disk = { $file } was deleted
editors-changed-on-disk-detail = Your unsaved changes are kept. Reload to take the version on disk instead.
# For a file open in a plugin's editor that was deleted on disk.
editors-deleted-editor-detail = Its editor stays open; save it to create the file again.
# For a file open in the text editor that was deleted on disk.
editors-deleted-text-detail = Its text stays open; save it to create the file again.
editors-no-definition = No definition found
editors-no-definition-detail = The language does not know where this is defined.
editors-redo-partial = Part of the edit was not redone
# Variables:
#   $file - the file's name
editors-redo-partial-detail = { $file } changed since the edit was undone.

## Workspace edits, pickers and diffs

editors-edit-not-undone = The edit was not undone
editors-edit-not-redone = The edit was not redone
editors-edit-not-applied = The edit was not applied
# Variables:
#   $file - the file's name
editors-changed-since-edit = { $file } changed since the edit.
editors-apply = Apply
editors-undo = Undo
# Lines of the summary of a workspace edit, before it is applied.
# Variables:
#   $path - the file
editors-summary-create = Create { $path }
# Variables:
#   $path - the folder
editors-summary-create-folder = Create the folder { $path }
# Variables:
#   $from - the old path
#   $to - the new path
editors-summary-rename = Rename { $from } to { $to }
# Variables:
#   $path - the file or folder
editors-summary-delete = Delete { $path }
# Variables:
#   $path - the file
#   $count (Number) - in how many places
editors-summary-change =
    { $count ->
        [one] Change { $path } in 1 place
       *[other] Change { $path } in { $count } places
    }
# Variables:
#   $count (Number) - how many more lines
editors-summary-more = and { $count } more.
editors-unknown-operation = The edit has an operation Runesmith does not know.
editors-save-as = Save As
editors-pick-definition = Pick a definition
editors-reload = Reload
# The two sides of a diff of a file and its base.
editors-diff-base = Base
editors-diff-working-copy = Working copy
editors-open-file = Open File
editors-open-folder = Open Folder
# In the Open With list.
editors-current = Current
editors-open = Open
# Variables:
#   $pattern - the kind of file, such as *.png
editors-default-for = Default for { $pattern }
# Variables:
#   $pattern - the kind of file, such as *.png
editors-set-default-for = Set Default Editor for { $pattern }...
# Variables:
#   $pattern - the kind of file, such as *.png
editors-default-editor-for = Default editor for { $pattern }
editors-text-editor = Text Editor
editors-image-viewer = Image Viewer
editors-zoom-out = Zoom out (-)
editors-zoom-in = Zoom in (+)
editors-fit = Fit
editors-fit-tip = Fit in the window (0)
editors-actual-size-tip = Actual size (1)

## Why a file operation or an edit across files can't be made, undone or redone

# Variables:
#   $name - the file's or folder's name
editors-problem-missing = { $name } does not exist.
# Variables:
#   $name - the file's or folder's name
editors-problem-deleted-by-edit = { $name } is deleted by the same edit.
# Variables:
#   $name - the file's or folder's name
editors-problem-replaced-by-edit = { $name } is replaced by the same edit.
# Variables:
#   $name - the file's or folder's name
editors-problem-exists = { $name } already exists.
# Variables:
#   $name - the folder's name
editors-problem-into-itself = { $name } cannot move into itself.
# Variables:
#   $name - the folder's name
editors-problem-folder-not-empty = The folder { $name } is not empty.
# Variables:
#   $name - the file's or folder's name
editors-problem-gone = { $name } no longer exists.
# Variables:
#   $name - the file's or folder's name
editors-problem-exists-again = { $name } exists again.
# Variables:
#   $name - the folder's name
editors-problem-no-longer-empty = { $name } is no longer empty.
# Variables:
#   $name - the file's or folder's name
editors-problem-not-in-trash = { $name } is no longer in the trash.
# Variables:
#   $name - the file's name
editors-problem-unsaved = { $name } has unsaved changes.
# Variables:
#   $name - the file's name
editors-problem-not-opened = { $name } could not be opened.
# What an edit across files is called in the undo notification.
# Variables:
#   $name - the file's or folder's name
editors-label-create = Create { $name }
# Variables:
#   $from - the old name
#   $to - the new name
editors-label-rename = Rename { $from } to { $to }
# Variables:
#   $name - the file's or folder's name
#   $folder - the folder it moves to
editors-label-move = Move { $name } to { $folder }
# Variables:
#   $name - the file's or folder's name
editors-label-delete = Delete { $name }
# Variables:
#   $count (Number) - how many file operations
editors-label-operations = { $count } file operations
# Variables:
#   $count (Number) - how many files
editors-label-edit-files =
    { $count ->
        [one] Edit 1 file
       *[other] Edit { $count } files
    }
# Variables:
#   $name - the file's name
editors-choose-language = Choose the language of { $name }
# Variables:
#   $name - the name that is taken
editors-name-exists = { $name } exists already.
# Variables:
#   $name - the file's name
editors-problem-is-folder = { $name } is a folder.
