using System.Text;
using Runesmith.Sdk;

namespace Runesmith.Shell.Terminal;

/// <summary>Finds the user's shell and prepares the environment terminals start with, with the optional shell integration that tells
/// Runesmith the shell's folder and each command's exit code.</summary>
internal static class TerminalShells
{
    /// <summary>The variable shell integration scripts read to find the user's own zsh startup files.</summary>
    private const string UserZdotdir = "RUNESMITH_USER_ZDOTDIR";

    private const string BashScript = """
        # Runesmith's shell integration for bash: reports the folder (OSC 7) and the exit code of each command (OSC 133).
        if [ -n "$RUNESMITH_LOGIN_SHELL" ]; then
            unset RUNESMITH_LOGIN_SHELL
            [ -r /etc/profile ] && . /etc/profile
            for __runesmith_file in ~/.bash_profile ~/.bash_login ~/.profile; do
                if [ -r "$__runesmith_file" ]; then . "$__runesmith_file"; break; fi
            done
            unset __runesmith_file
        elif [ -r ~/.bashrc ]; then
            . ~/.bashrc
        fi
        __runesmith_prompt() {
            local code=$?
            printf '\e]133;D;%s\a\e]7;file://%s%s\a' "$code" "${HOSTNAME:-localhost}" "$PWD"
            return $code
        }
        if [[ "$(declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
            PROMPT_COMMAND=(__runesmith_prompt "${PROMPT_COMMAND[@]}")
        else
            PROMPT_COMMAND="__runesmith_prompt${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
        fi
        """;

    private const string ZshEnv = """
        # Runesmith's shell integration for zsh: reads the user's own startup files from their folder.
        __runesmith_zdotdir="$ZDOTDIR"
        ZDOTDIR="${RUNESMITH_USER_ZDOTDIR:-$HOME}"
        [ -r "$ZDOTDIR/.zshenv" ] && . "$ZDOTDIR/.zshenv"
        ZDOTDIR="$__runesmith_zdotdir"
        """;

    private const string ZshProfile = """
        ZDOTDIR="${RUNESMITH_USER_ZDOTDIR:-$HOME}"
        [ -r "$ZDOTDIR/.zprofile" ] && . "$ZDOTDIR/.zprofile"
        ZDOTDIR="$__runesmith_zdotdir"
        """;

    private const string ZshRc = """
        ZDOTDIR="${RUNESMITH_USER_ZDOTDIR:-$HOME}"
        [ -r "$ZDOTDIR/.zshrc" ] && . "$ZDOTDIR/.zshrc"
        __runesmith_precmd() {
            local code=$?
            printf '\e]133;D;%s\a\e]7;file://%s%s\a' "$code" "${HOST:-localhost}" "$PWD"
        }
        autoload -Uz add-zsh-hook 2>/dev/null && add-zsh-hook precmd __runesmith_precmd
        unset __runesmith_zdotdir
        """;

    /// <summary>Gets the user's shell: the <paramref name="configured"/> one when set, otherwise <c>$SHELL</c> on Linux and macOS, and
    /// PowerShell or cmd on Windows.</summary>
    public static string DefaultShell(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        var path = Environment.GetEnvironmentVariable("PATH");
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[] { "pwsh.exe", "powershell.exe" })
            {
                var found = PtyProcess.FindProgram(candidate, path);
                if (Path.IsPathRooted(found))
                    return found;
            }

            return Environment.GetEnvironmentVariable("COMSPEC") is { Length: > 0 } comspec ? comspec : "cmd.exe";
        }

        if (Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell && File.Exists(shell))
            return shell;

        return File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
    }

    /// <summary>Gets the arguments and variables a shell starts with: a login shell on macOS, as the system's own terminal starts it, and the
    /// shell integration for bash and zsh when it is on.</summary>
    public static (IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string?> Environment) Prepare(string shell, IReadOnlyList<string> arguments,
        bool integrate)
    {
        var environment = new Dictionary<string, string?>();
        var name = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();
        var login = OperatingSystem.IsMacOS() && arguments.Count == 0;
        if (arguments.Count > 0 || !integrate || OperatingSystem.IsWindows())
            return (login ? ["-l"] : arguments, environment);

        try
        {
            var folder = Path.Combine(RunesmithPaths.Cache, "shell-integration");
            if (name == "bash")
            {
                var script = Write(folder, "runesmith.bash", BashScript);
                if (login)
                    environment["RUNESMITH_LOGIN_SHELL"] = "1";
                return (["--rcfile", script], environment);
            }

            if (name == "zsh")
            {
                var zsh = Path.Combine(folder, "zsh");
                Write(zsh, ".zshenv", ZshEnv);
                Write(zsh, ".zprofile", ZshProfile);
                Write(zsh, ".zshrc", ZshRc);
                environment[UserZdotdir] = Environment.GetEnvironmentVariable("ZDOTDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                environment["ZDOTDIR"] = zsh;
                return (login ? ["-l"] : [], environment);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return (login ? ["-l"] : [], environment);
    }

    /// <summary>Gets the variables every terminal sets: the terminal's type and colors, and a UTF-8 locale when none is set.</summary>
    public static Dictionary<string, string?> BaseEnvironment()
    {
        var environment = new Dictionary<string, string?>
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
            ["TERM_PROGRAM"] = "Runesmith",
        };
        if (!OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LANG"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LC_ALL")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LC_CTYPE")))
            environment["LANG"] = OperatingSystem.IsMacOS() ? "en_US.UTF-8" : "C.UTF-8";
        return environment;
    }

    private static string Write(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        var content = text.ReplaceLineEndings("\n") + "\n";
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
