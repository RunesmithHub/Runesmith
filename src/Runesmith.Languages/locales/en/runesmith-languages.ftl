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
languages-show-output = Show output

## The status of a language server

# Variables:
#   $server - the language server's name
languages-server-not-installed = { $server } is not installed
# Variables:
#   $server - the language server's name
languages-server-stopped-title = { $server } stopped
# Variables:
#   $command - the command Runesmith ran
#   $hint - how to install it
languages-server-could-not-start-hint = Runesmith could not start { $command }. { $hint }
# Variables:
#   $server - the language server's name
languages-server-install-hint = Install it, or set its path in the { $server } command setting.
# Variables:
#   $server - the language server's name
languages-server-failed-to-start = { $server } failed to start. The output has the details.
# Variables:
#   $server - the language server's name
languages-server-kept-stopping = { $server } kept stopping unexpectedly. Run "Restart Language Servers" to try again.
# Status bar texts.
# Variables:
#   $server - the language server's name
languages-status-starting = { $server }: starting
# Variables:
#   $server - the language server's name
#   $folder - the folder it works in
languages-status-starting-tip = Starting { $server } in { $folder }
# Variables:
#   $server - the language server's name
#   $work - what it does, such as Indexing (40%)
languages-status-working = { $server }: { $work }
# Variables:
#   $server - the language server's name
#   $folder - the folder it works in
languages-status-working-tip = { $server } is working in { $folder }
# Variables:
#   $server - the language server's name
#   $folder - the folder it works in
languages-status-running-tip = { $server } is running in { $folder }. Click for its output.
# Variables:
#   $server - the language server's name
languages-status-missing = { $server }: not installed
# Variables:
#   $command - the command Runesmith ran
languages-status-missing-tip = Runesmith could not start { $command }.
# Variables:
#   $server - the language server's name
languages-status-stopped = { $server }: stopped
# Variables:
#   $server - the language server's name
languages-status-stopped-tip = { $server } stopped. Click for its output.
# Variables:
#   $server - the language server's name
languages-status-idle-tip = { $server } is stopped.
languages-working = working
