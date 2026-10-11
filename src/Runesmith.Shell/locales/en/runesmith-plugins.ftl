### Messages of the plugin manager, the plugin hub's notices and dialogs, plugin pages, reports and safe mode.
### Their ids start with plugins-, the area this file is named after.

## Commands

# The category of the plugin commands in the command palette.
plugins-category = Plugins
# The category of the help commands in the command palette.
plugins-category-help = Help
plugins-show = Plugins
plugins-show-description = Show the plugin manager: installed plugins, updates and the plugin hub.
plugins-show-updates = Plugin Updates
plugins-check-hub = Check the Plugin Hub
plugins-check-hub-description = Check the plugin hub for updates and changes to your plugins now.
plugins-restart-safe-mode = Restart in Safe Mode
plugins-restart-safe-mode-description = Start Runesmith again without plugins, to find a plugin that causes problems.

## Lists of plugin names, such as "Git, GitHub and Gitea"

# Variables:
#   $list - the names before the last, joined with plugins-list-separator
#   $last - the last name
plugins-list-and = { $list } and { $last }
plugins-list-separator = ,{" "}

## Notifications and messages of the plugin hub

# The title of a warning when a runesmith:// link was refused; the reason follows.
plugins-link-refused = Runesmith refused a link
# Variables:
#   $name - the plugin's name
plugins-turned-on = { $name } is turned on from the next start.
# Variables:
#   $name - the plugin's name
plugins-turned-off = { $name } is turned off from the next start.
# Variables:
#   $name - the plugin's name
plugins-install-failed = { $name } can't be installed
# Variables:
#   $name - the plugin's name
plugins-update-failed = { $name } can't be updated
plugins-update-all-failed = The plugins can't be updated
# Variables:
#   $name - the plugin's name
plugins-remove-failed = { $name } can't be removed
# Variables:
#   $name - the plugin's name
plugins-reinstall-failed = { $name } can't be reinstalled
plugins-reinstall-unavailable = The hub no longer offers its version.
plugins-restart-title = Restart Runesmith
plugins-restart-manually = Close Runesmith and start it again to apply the changes.
plugins-restart-to-apply = Restart to apply
plugins-restart-now = Restart now
plugins-ok = OK
# The title of a message when installing, updating or removing plugins failed; the reason follows.
# Variables:
#   $action - what failed, such as "Install Git"
plugins-apply-failed = { $action } failed
# After installing or updating plugins.
# Variables:
#   $names - the plugins' names, such as "Git and GitHub"
#   $count (Number) - how many plugins
plugins-take-effect =
    { $count ->
        [one] { $names } takes effect after a restart.
       *[other] { $names } take effect after a restart.
    }
# After removing plugins.
# Variables:
#   $names - the plugins' names, such as "Git and GitHub"
#   $count (Number) - how many plugins
plugins-removed-at-restart =
    { $count ->
        [one] { $names } is removed when Runesmith restarts.
       *[other] { $names } are removed when Runesmith restarts.
    }
plugins-safety-unconfirmed = Plugin safety can't be confirmed
plugins-show-plugins = Show plugins
plugins-show-updates-button = Show updates
plugins-auto-updated = Plugins updated
# Variables:
#   $name - the plugin's name
plugins-update-available = An update for { $name } is available
# Variables:
#   $count (Number) - how many updates
plugins-updates-available = { $count } plugin updates are available
plugins-updates-waiting = Unverified plugins and updates that change what a plugin may do wait for you.
plugins-quarantined = A plugin was disabled because Runesmith found it harmful
plugins-files-changed = A plugin's files changed
plugins-see-manager = The plugin manager says which, and what to do.
plugins-hub-on = Plugins from the hub load from the next start.
plugins-hub-off = Plugins from the hub stop loading from the next start.
plugins-local-overrides-on = Local copies of plugins run in place of the installed ones from the next start.
plugins-local-overrides-off = Local copies of installed plugins stop loading from the next start.

## Install, update and remove dialogs

plugins-cancel = Cancel
plugins-close = Close
plugins-later = Later
# The heading above the capabilities of the plugins a plan installs.
plugins-capabilities = Capabilities
# Under plugins-capabilities, when the plugins a plan installs declare none.
# Variables:
#   $count (Number) - how many plugins the plan installs or updates
plugins-no-capabilities =
    { $count ->
        [one] None declared: this plugin works only inside the editor, through what Runesmith offers plugins.
       *[other] None declared: these plugins work only inside the editor, through what Runesmith offers plugins.
    }
plugins-step-downloading = Downloading the packages, within the sizes their records state
plugins-step-verifying = Checking each package against the hub's signed records
plugins-step-unpacking = Unpacking safely into a staging folder and checking each manifest
plugins-step-committing = Switching the installed plugins in one step
# Variables:
#   $name - the plugin's name
plugins-downloading = Downloading { $name }
# Variables:
#   $name - the plugin's name
plugins-unpacking = Unpacking { $name }
plugins-all-or-nothing = If anything fails, nothing changes.
plugins-restart-to-finish = Restart to finish
# The toggle that shows the steps that led to a failed install plan.
plugins-derivation = How Runesmith worked this out
# Added to the notice of a blocked plugin that other plugins need.
# Variables:
#   $dependents - the names of the plugins that need it
#   $count (Number) - how many plugins need it
plugins-blocked-dependents =
    { $count ->
        [one] { $dependents } needs it and stops with it.
       *[other] { $dependents } need it and stop with it.
    }
# Variables:
#   $name - the plugin's name
plugins-blocked-one = { $name } was turned off
plugins-blocked-many = Plugins were turned off
plugins-blocked-description = Runesmith restarts to stop it. Plugins that depend on it stop with it.
# Variables:
#   $name - the plugin's name
plugins-unverified = { $name } is unverified
plugins-no-capabilities-short = Capabilities: none declared.
plugins-source-repository = Source repository
plugins-build-analysis = Build and analysis

## Install plans

# The warning in an install plan about a plugin that is not verified.
# Variables:
#   $name - the plugin's name
#   $publisher - who publishes it
plugins-unverified-warning = { $name } hasn't been reviewed by Runesmith. Its code was checked automatically, but no person has read it. Plugins can access everything you can: your files, your projects and your accounts. Install it only if you trust { $publisher }.
# The check box the user must tick to install an unverified plugin.
# Variables:
#   $name - the plugin's name
plugins-unverified-acknowledgement = I understand that { $name } is unverified and I trust its publisher.
# A version change in a plan row.
# Variables:
#   $from - the installed version
#   $to - the new version
plugins-version-change = { $from } to { $to }
# The title of a plan, and the text of its button.
# Variables:
#   $name - the plugin's name
plugins-plan-remove = Remove { $name }
# Variables:
#   $name - the plugin's name
plugins-plan-update = Update { $name }
# Variables:
#   $name - the plugin's name
#   $version - the version it changes to
plugins-plan-downgrade = Change { $name } to { $version }
# Variables:
#   $name - the plugin's name
plugins-plan-install = Install { $name }
plugins-plan-update-many = Update plugins
plugins-plan-change-many = Change plugins
# Variables:
#   $count (Number) - how many plugins the plan changes
plugins-plan-summary =
    { $count ->
        [one] This changes 1 plugin.
       *[other] This changes { $count } plugins.
    }
# Variables:
#   $date (Date) - when the plugin was reviewed
plugins-reviewed-on = Reviewed by Runesmith on { DATETIME($date, dateStyle: "long") }.
plugins-reviewed = Reviewed by Runesmith.
# A download's size.
# Variables:
#   $size (Number) - the size in bytes
plugins-size-bytes = { $size } B
# Variables:
#   $size (Number) - the size in kilobytes
plugins-size-kilobytes = { NUMBER($size, maximumFractionDigits: 0) } KB
# Variables:
#   $size (Number) - the size in megabytes
plugins-size-megabytes = { NUMBER($size, minimumFractionDigits: 1, maximumFractionDigits: 1) } MB
# What a plan does with one plugin.
plugins-what-install = Install
# Variables:
#   $name - the plugin that needs it
plugins-what-install-needed = Install, needed by { $name }
plugins-what-update = Update
# Variables:
#   $name - the plugin that needs it
#   $range - the versions it needs, such as ^1.2.0
plugins-what-update-needed = Update, { $name } needs { $range }
plugins-what-downgrade = Downgrade
# Variables:
#   $name - the plugin that needs it
#   $range - the versions it needs, such as ^1.2.0
plugins-what-downgrade-needed = Downgrade, { $name } needs { $range }
plugins-what-remove = Remove
plugins-what-remove-unneeded = Remove, nothing needs it anymore
plugins-what-remove-unavailable = Remove, no longer available
plugins-what-keep = Keep
# The button that carries out a plan.
plugins-action-remove = Remove
plugins-action-update = Update
plugins-action-install = Install

## Notes under installed plugins

plugins-note-safe-mode = Off in safe mode, which loads no plugins.
plugins-note-installed = Installed. Restart Runesmith to start it.
plugins-note-quarantined = Moved to quarantine. It can't be turned back on.
# Variables:
#   $error - why it could not load
plugins-note-could-not-load = Could not load: { $error }
# On a plugin's page.
# Variables:
#   $error - why it could not load
plugins-problem-could-not-load = It could not load: { $error }
plugins-problem-turned-off = It is turned off.
plugins-note-turned-off = Turned off.
# Variables:
#   $version - the version from the hub
plugins-note-local-copy-of-version = Local copy, running in place of version { $version } from the hub. It has its own secrets and storage, so sign in to it again.
plugins-note-local-copy = Local copy, running in place of the installed plugin. It has its own secrets and storage, so sign in to it again.
plugins-note-restart-to-finish = Restart Runesmith to finish the change.
# Variables:
#   $version - the version installed
plugins-note-version-installed = Version { $version } is installed. Restart Runesmith to start it.
plugins-note-removed = Removed. Restart Runesmith to finish.
plugins-note-from-hub-needed = From the hub, installed because another plugin needs it.
plugins-note-from-hub = From the hub.
plugins-note-from-folder = From your plugins folder. Never updated or reported to the hub.

## Notices about local copies of plugins, at start

# Variables:
#   $name - the plugin's name
plugins-local-copy-running = A local copy of { $name } is running
# Variables:
#   $count (Number) - how many local copies
plugins-local-copies-running = { $count } local copies of plugins are running
# Variables:
#   $names - the plugins' names
#   $count (Number) - how many plugins
#   $setting - the setting's key
plugins-local-copies-running-detail =
    { $count ->
        [one] { $names } from your plugins folder runs in place of the installed plugin, with its own secrets and storage. Turn off { $setting } in Settings to stop this.
       *[other] { $names } from your plugins folder run in place of the installed plugins, with their own secrets and storage. Turn off { $setting } in Settings to stop this.
    }
# Variables:
#   $name - the plugin's name
plugins-local-copy-refused = A local copy of { $name } was not loaded
# Variables:
#   $count (Number) - how many local copies
plugins-local-copies-refused = { $count } local copies of plugins were not loaded
# Variables:
#   $names - the plugins' names
plugins-local-copies-refused-detail = Your plugins folder has a copy of { $names }, which the hub installed. The plugin manager says how to run it.

## The plugin manager

plugins-title = Plugins
plugins-tab-installed = Installed
plugins-tab-updates = Updates
plugins-tab-browse = Browse
# The Updates tab with how many updates there are, in the narrow Plugins panel.
# Variables:
#   $count (Number) - how many updates
plugins-tab-updates-compact = Updates { $count }
# The Updates tab with how many updates there are, on the Plugins page.
# Variables:
#   $count (Number) - how many updates
plugins-tab-updates-count = Updates ({ $count })
plugins-search = Search plugins
# A filter of the Browse tab.
plugins-filter-compatible = Runs on this Runesmith
plugins-filter-all-tiers = All allowed tiers
plugins-filter-all-categories = All categories
plugins-refresh = Check the plugin hub now
plugins-safe-mode-title = Runesmith is in safe mode.
plugins-safe-mode-detail = No plugins load. Turn off a plugin you suspect, then restart normally.
plugins-restart-normally = Restart normally
plugins-none-installed = No plugins installed
plugins-none-installed-hint = Browse the plugin hub for languages, tools and themes, or open a folder and Runesmith suggests the plugins it needs.
plugins-browse = Browse plugins
plugins-open-folder = Open the plugins folder
plugins-empty-quarantine = Empty the quarantine
plugins-settings = Plugin settings
# Variables:
#   $version - the version that fixes the problem
plugins-update-to = Update to { $version }
# Variables:
#   $name - the plugin's name
plugins-remove-one = Remove { $name }
# Variables:
#   $name - the plugin's name
#   $dependents - the plugins that need it
plugins-remove-with-dependents = Remove { $name } and { $dependents }
plugins-see-replacement = See the replacement
# Added to a notice about a plugin that other plugins need.
# Variables:
#   $dependents - the plugins that need it
#   $count (Number) - how many plugins need it
#   $name - the plugin's name
plugins-dependents-turned-off =
    { $count ->
        [one] { $dependents } was turned off with it, because it needs { $name }.
       *[other] { $dependents } were turned off with it, because they need { $name }.
    }
plugins-toggle = Turn the plugin on or off from the next start
plugins-toggle-locked = A plugin Runesmith turned off for your safety can't be turned on here
# Variables:
#   $name - the plugin's name
plugins-open-page = Open { $name }'s page
plugins-up-to-date = Everything is up to date
plugins-no-updates = No updates to show
plugins-up-to-date-hint = Official and verified plugins update by themselves; Runesmith tells you about the others.
plugins-update-all = Update all
# Variables:
#   $count (Number) - how many updates
plugins-updates-summary =
    { $count ->
        [one] 1 update. Official and verified plugins update by themselves within their line.
       *[other] { $count } updates. Official and verified plugins update by themselves within their line.
    }
plugins-review-update = Review update
plugins-update = Update
plugins-release-notes = Release notes
plugins-catalog-unavailable = The catalog is not available
# Variables:
#   $count (Number) - how many plugins the setting hides
plugins-hidden-by-tiers =
    { $count ->
        [one] 1 plugin is hidden by your setting Allowed plugins.
       *[other] { $count } plugins are hidden by your setting Allowed plugins.
    }
plugins-change = Change
plugins-no-match = No plugin matches
plugins-no-match-hint = Try other words, or clear the filters.
# After a plugin's publisher in the Browse tab.
# Variables:
#   $version - the installed version
plugins-installed-version = { $version } installed
plugins-settings-button = Settings
# A link to a security advisory about a plugin.
plugins-advisory = Advisory
plugins-reinstall = Reinstall

## Plugin pages

# A plugin tier as a name, such as in the tier filter.
plugins-tier-name-official = Official
plugins-tier-name-verified = Verified
plugins-tier-name-unverified = Unverified
# The tiers the setting Allowed plugins allows, in a sentence.
plugins-allowed-official = official
plugins-allowed-official-verified = official and verified
# Variables:
#   $id - the plugin id that was asked for
plugins-page-not-found = No plugin with the id { $id } is on the hub.
plugins-page-not-found-hint = Check the id, or search for the plugin in Browse.
# Variables:
#   $name - the plugin's name
#   $version - the version that was asked for
plugins-page-version-missing = { $name } has no version { $version } to install, so the newest one is selected.
# Variables:
#   $name - the plugin's name
#   $version - the installed version
plugins-page-quarantined = Your { $name } { $version } was disabled because Runesmith found it harmful.
# Variables:
#   $reason - why, such as Malware
plugins-page-reason = Reason: { $reason }.
# Variables:
#   $name - the plugin's name
plugins-page-blocked = { $name } was blocked because Runesmith found it harmful.
# Variables:
#   $name - the plugin's name
plugins-page-frozen = New installs of { $name } are paused while Runesmith looks into a report.
plugins-page-frozen-running = You can keep using it.
# Variables:
#   $name - the plugin's name
plugins-page-deleted = { $name } is no longer available on the hub.
# Variables:
#   $name - the plugin's name
#   $version - the version
plugins-page-yanked = { $name } { $version } was withdrawn by its developer.
# Variables:
#   $name - the plugin's name
plugins-page-deprecated = { $name } is no longer maintained.
# Variables:
#   $replacement - the plugin its developer suggests
plugins-page-deprecated-replacement = Its developer suggests { $replacement } instead.
# Variables:
#   $name - the plugin's name
#   $tier - official, verified or unverified
plugins-page-tier-not-allowed =
    { $tier ->
        [official] { $name } is official.
        [verified] { $name } is verified.
       *[unverified] { $name } is unverified.
    }
# Variables:
#   $tiers - the tiers allowed, plugins-allowed-official or plugins-allowed-official-verified
plugins-page-tiers-allowed = Your settings allow only { $tiers } plugins.
# Variables:
#   $name - the plugin's name
#   $version - the version
plugins-page-incompatible = { $name } { $version } doesn't run on this version of Runesmith.
# Variables:
#   $needed - the API versions it needs, such as ^0.1.2
#   $provided - the API version of this Runesmith
plugins-page-incompatible-detail = It needs Runesmith API { $needed }; this Runesmith provides { $provided }.
plugins-page-unverified = Unverified.
plugins-page-unverified-detail = Checked automatically. No person at Runesmith has read this code.
plugins-page-checked = Checked automatically, not reviewed by a person.
# The button that goes back from a plugin's page.
# Variables:
#   $tab - the tab it goes back to, such as Browse
plugins-page-back = Back to { $tab }
plugins-page-link-note = Runesmith shows what the hub's signed records say, not what the website said.
# Variables:
#   $count (Number) - how many other links were ignored
plugins-page-links-ignored =
    { $count ->
        [one] 1 other link that came within a minute was ignored.
       *[other] { $count } other links that came within a minute were ignored.
    }
# Variables:
#   $link - the runesmith:// link
plugins-page-from-link = Opened from a link: { $link }
# Variables:
#   $version - the version
plugins-page-version = Version { $version }
plugins-page-pending = Installed, restart to enable
# Variables:
#   $version - the version
plugins-page-install-version = Install { $version }
plugins-install = Install
# Variables:
#   $version - the installed version
plugins-page-installed-from-hub = Version { $version } is installed from the hub.
# Variables:
#   $version - the installed version
plugins-page-installed-locally = Version { $version } is in your plugins folder.
plugins-disable = Disable
plugins-enable = Enable
plugins-uninstall = Uninstall
plugins-auto-update = Update automatically
plugins-auto-update-tip = Official and verified plugins update by themselves within their line.
# Card titles on a plugin's page.
plugins-card-review = Review
plugins-card-capabilities = Capabilities
plugins-card-dependencies = Dependencies
plugins-card-versions = Versions
# Rows of the Review card.
plugins-fact-source = Source
plugins-fact-built-from = Built from
plugins-fact-published = Published
plugins-fact-api = Runesmith API
plugins-fact-categories = Categories
plugins-page-no-capabilities = Declares no capabilities: it works only inside the editor, through what Runesmith offers plugins.
# In a list of the hosts a plugin connects to, for *.
plugins-hosts-chosen = hosts you choose
# Variables:
#   $hosts - the hosts, joined with plugins-list-separator
plugins-connects-to = Connects to { $hosts }.
plugins-no-dependencies = Needs no other plugin.
plugins-installed = Installed
# Variables:
#   $version - the version
plugins-select-version = Select { $version }
plugins-no-versions = No version is published yet.
# A date on a plugin's page.
# Variables:
#   $date (Date) - the date
plugins-date-long = { DATETIME($date, dateStyle: "long") }
# Variables:
#   $date (Date) - the date
plugins-date-short = { DATETIME($date, dateStyle: "medium") }

## Badges

plugins-tier-official-tip = Published by Runesmith. Every version is reviewed.
plugins-tier-verified-tip = A verified publisher. Every version is reviewed by Runesmith before you get it.
plugins-tier-unverified-tip = Checked automatically. No person at Runesmith has read this code.
# The badge of a plugin from the plugins folder.
plugins-tier-local = Local
plugins-tier-local-tip = From your plugins folder, not from the hub.
plugins-local-replaces = Replaces the installed plugin
plugins-local-not-loaded = Not loaded
plugins-local-replaces-tip = This copy from your plugins folder runs in place of the plugin from the hub, with its own secrets and storage.
plugins-local-not-loaded-tip = A plugin from the hub has the same id, so this copy does not load.

## Suggestions to install official plugins

# Variables:
#   $name - the plugin's name, such as Git
#   $brings - what it adds, one of the plugins-suggest-brings messages
plugins-suggestion = Install the { $name } plugin for { $brings }.
plugins-suggest-brings-language = completion, problems and builds
plugins-suggest-brings-git = commits, branches, diffs and the log
plugins-suggest-brings-github = pull requests, reviews and checks
plugins-suggestion-install = Install
plugins-suggestion-decline = Don't suggest again

## Reports

plugins-report = Report
# Variables:
#   $name - the plugin's name
plugins-report-tip = Report { $name }
plugins-report-security = It's malicious or a security risk
plugins-report-security-hint = Private, to Runesmith's administrators
plugins-report-broken = It's broken or doesn't work
plugins-report-broken-hint = An issue in the plugin's repository
plugins-report-policy = It breaks the rules
plugins-report-policy-hint = Impersonation, spam or license: a policy issue
plugins-report-copied = The plugin's details are on the clipboard
# Variables:
#   $email - the address for security reports
plugins-report-copied-detail = Paste them into GitHub's private report form. Without a GitHub account, write to { $email } instead.
plugins-report-include-log = Include these recent log lines
plugins-report-no-log = No log lines mention this plugin.
plugins-report-open = Open in browser
# Variables:
#   $name - the plugin's name
plugins-report-broken-title = Report that { $name } is broken
plugins-report-broken-description = Runesmith opens a new issue in the plugin's repository with these details. You can edit everything before you submit it.
plugins-report-not-github = The plugin's repository is not on GitHub
plugins-report-not-github-detail = Report the problem where its developer asks for reports.

## Safe mode

# After the reason Runesmith did not start properly.
# Variables:
#   $count (Number) - how many plugins were loading
plugins-safe-mode-loading =
    { $count ->
        [one] This plugin was loading when it happened:
       *[other] These plugins were loading when it happened:
    }
plugins-safe-mode-explanation = Runesmith started in safe mode, which loads no plugins. Turn off the suspect plugin in the plugin manager, then restart normally.
plugins-start-normally = Start normally
plugins-stay-safe-mode = Stay in safe mode
plugins-safe-mode-header = Runesmith didn't start properly

## Status lines and badges

# Variables:
#   $ago - when, such as 3 minutes ago
plugins-status-verified = Hub verified { $ago }
plugins-status-checking = Checking the plugin hub
plugins-status-offline = Offline: the saved catalog
plugins-status-unverifiable = The hub can't be verified
plugins-status-off = The plugin hub is off
plugins-status-not-set-up = The plugin hub is not set up
plugins-update-automatic = Updates by itself: the same line and no new capability.
# After the reasons an update waits for the user.
plugins-update-needs-approval = It needs your approval.
plugins-update-unverified = Unverified: never updates by itself.
plugins-update-off = Automatic updates are off.
plugins-badge-restart = Restart to apply
plugins-badge-blocked = Blocked
plugins-badge-files-changed = Files changed
plugins-badge-paused = Installs paused
plugins-badge-withdrawn = Withdrawn
plugins-badge-deprecated = Deprecated
plugins-badge-removed = Removed from the hub
plugins-badge-yanked = Yanked
plugins-badge-deleted = Deleted
plugins-badge-new = New for you
