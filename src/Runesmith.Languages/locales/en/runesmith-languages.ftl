### Messages of the language registry, syntax highlighting and language servers.
### Their ids start with languages-, the area this file is named after.

# The category of the language server commands in the command palette.
languages-category = Languages
languages-show-server-output = Show Language Server Output
languages-restart-servers = Restart Language Servers
languages-restart-servers-description = Stops every language server and starts them again, including ones that were not installed or kept stopping.
languages-run-server-command = Run Language Server Command
# The title of a warning when a language server asked for an edit that could not be made.
languages-server-edit-failed = The language server's edit was not applied

# Settings of each language server.
# Variables:
#   $server - the language server's name
languages-setting-run = Run { $server }
# Variables:
#   $server - the language server's name
#   $languages - the language ids it serves, such as csharp, java
languages-setting-run-description = Start { $server } for { $languages } files, for completion, hover, go to definition and problems.
# Variables:
#   $server - the language server's name
languages-setting-command = { $server } command
# Variables:
#   $command - the executable it runs by default
languages-setting-command-description = The executable to run instead of { $command }, such as a full path. Leave empty to use { $command } from the PATH.
languages-setting-trace = Trace language servers
languages-setting-trace-description = Write every message to and from language servers to their output channels, for finding out why a server misbehaves.
