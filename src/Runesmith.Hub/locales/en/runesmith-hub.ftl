### Messages of the plugin hub client: its status, install plans and notices about installed plugins.
### Their ids start with hub-, the area this file is named after.

## The status of the plugin hub

hub-status-not-set-up = The plugin hub is not set up in this build of Runesmith. Plugins in your plugins folder work as usual.
hub-status-turned-off = The plugin hub is turned off in Settings. Only the plugins in your plugins folder load.
hub-status-checking = Checking the plugin hub.
hub-status-verified = The plugin hub's data is verified and current.
hub-status-offline = Runesmith can't reach the plugin hub, so this is the saved catalog. Installs and updates are unavailable.
# Variables:
#   $date (Date) - the last time the safety status was confirmed
hub-status-unverifiable = Runesmith can't confirm the safety status of your plugins since { DATETIME($date, dateStyle: "long") }. Installs and updates are unavailable. If your computer's date is wrong, correct it.
hub-status-unverifiable-never = Runesmith can't confirm the safety status of your plugins since the last check. Installs and updates are unavailable. If your computer's date is wrong, correct it.
hub-status-verification-failed = The plugin hub's data could not be verified, so Runesmith kept what it had. Installs and updates are unavailable; try again later.
hub-status-runesmith-too-old = The plugin hub needs a newer version of Runesmith. Update Runesmith to install and update plugins.

## Why the hub turned a plugin off

hub-reason-malicious = Malicious
hub-reason-compromised-account = Compromised account
hub-reason-vulnerability = Vulnerability
hub-reason-broken = Broken
hub-reason-policy = Breaks the hub's rules
hub-reason-legal = Legal
hub-reason-developer-request = At its developer's request

## Notices about installed plugins

# Variables:
#   $name - the plugin's name
hub-notice-harmful = { $name } was disabled because Runesmith found it harmful.
# Variables:
#   $reason - why, one of the hub-reason messages
hub-notice-reason = Reason: { $reason }.
# Variables:
#   $fix - what the hub says to do
hub-notice-remediation = What to do: { $fix }
# Variables:
#   $name - the plugin's name
hub-notice-files-changed = { $name } is off because its files changed since it was installed.
# After the first three changed files.
# Variables:
#   $count (Number) - how many more files changed
hub-notice-more-files = And { $count } more.
hub-notice-restart-to-finish = Restart Runesmith to finish turning it off.
# Variables:
#   $name - the plugin's name
hub-notice-frozen = New installs of { $name } are paused while Runesmith looks into a report. You can keep using it.
# Variables:
#   $name - the plugin's name
#   $version - the installed version
hub-notice-yanked = { $name } { $version } was withdrawn by its developer.
# Variables:
#   $name - the plugin's name
hub-notice-deleted = { $name } is no longer available on the hub. It keeps working, but won't get updates.
# Variables:
#   $name - the plugin's name
hub-notice-deprecated = { $name } is no longer maintained.
# Variables:
#   $replacement - the plugin its developer suggests
hub-notice-replacement = Its developer suggests { $replacement } instead.

## Why an update waits for the user

hub-update-adds-capability = It adds a capability.
hub-update-adds-capabilities = It adds capabilities.
# Variables:
#   $line - the new version line, such as 2.x
hub-update-new-line = It moves to a new line, { $line }.
# Variables:
#   $name - the plugin it installs
hub-update-adds-unverified = It adds { $name }, which is unverified.
# Variables:
#   $name - the plugin it removes
hub-update-removes = It removes { $name }.
# Variables:
#   $name - the plugin it downgrades
hub-update-downgrades = It downgrades { $name }.
# Variables:
#   $name - the plugin
hub-update-new-capabilities = { $name } gets new capabilities with it.

## Why a runesmith:// link was refused

hub-link-empty = The link is empty.
# Variables:
#   $max (Number) - the longest a link may be
hub-link-too-long = The link is longer than { $max } characters.
hub-link-characters = The link has characters links cannot have.
hub-link-scheme = Only runesmith:// links open Runesmith.
hub-link-fragment = A link cannot carry a fragment.
# Variables:
#   $host - the part after runesmith://
hub-link-host = Runesmith has no "{ $host }" links; links open the plugin hub's pages.
hub-link-updates-parameters = The updates link takes no parameters.
hub-link-no-target = A link opens a plugin's page or the updates list, and this one names neither.
# Variables:
#   $id - what the link gave as the plugin id
hub-link-bad-id = "{ $id }" is not a plugin id; ids are lowercase, like publisher.name.
# Variables:
#   $names - the parameters it refuses
hub-link-parameters = Runesmith refuses { $names }: a link carries only a plugin id and a version.
hub-link-versions = A link carries one version at most.
# Variables:
#   $value - what the link gave as the version
hub-link-bad-version = "{ $value }" is not a plugin version, such as 1.2.0.

# Variables:
#   $name - the plugin's name
#   $version - the version
hub-download-mismatch = The download of { $name } { $version } did not match what the hub published, so nothing was installed. Try again later.
