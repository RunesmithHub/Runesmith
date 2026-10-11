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
