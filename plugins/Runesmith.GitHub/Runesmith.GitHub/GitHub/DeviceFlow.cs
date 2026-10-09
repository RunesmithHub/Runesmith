using System.Net.Http.Json;
using System.Text.Json;

namespace Runesmith.GitHub.GitHub;

/// <summary>A device code waiting for the user to approve it on GitHub.</summary>
/// <param name="Code">The code Runesmith polls with; never shown.</param>
/// <param name="UserCode">The code the user types on GitHub, such as <c>WDJB-MJHT</c>.</param>
/// <param name="VerificationUri">Where the user types it.</param>
/// <param name="Interval">How long to wait between polls.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
internal sealed record DeviceCode(string Code, string UserCode, Uri VerificationUri, TimeSpan Interval, DateTimeOffset ExpiresAt);

/// <summary>A user access token and, for an app with expiring tokens, its refresh token.</summary>
internal sealed record GitHubTokens(string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken, DateTimeOffset? RefreshExpiresAt);

/// <summary>Why signing in with the device flow, or refreshing its token, did not work.</summary>
internal enum DeviceFlowFailure
{
    /// <summary>The code expired before the user approved it.</summary>
    Expired,

    /// <summary>The user cancelled on GitHub.</summary>
    Denied,

    /// <summary>The app does not have the device flow turned on.</summary>
    Disabled,

    /// <summary>GitHub does not know the client id.</summary>
    UnknownClient,

    /// <summary>The refresh token was refused, so the user has to sign in again.</summary>
    RefreshRefused,

    /// <summary>GitHub could not be reached.</summary>
    Network,

    /// <summary>GitHub answered in a way the flow does not expect.</summary>
    Other,
}

/// <summary>A failed step of the device flow, with a message for people; it never carries a code or token.</summary>
internal sealed class DeviceFlowException(DeviceFlowFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public DeviceFlowFailure Failure { get; } = failure;
}

/// <summary>Signs in to a GitHub App with GitHub's device flow, and refreshes the tokens it gives. Neither needs the app's client secret.</summary>
internal sealed class DeviceFlow(HttpClient http, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>The page where the user enters the code; GitHub always names this one.</summary>
    public static readonly Uri DefaultVerificationUri = new("https://github.com/login/device");

    private static readonly Uri CodeUri = new("https://github.com/login/device/code");
    private static readonly Uri TokenUri = new("https://github.com/login/oauth/access_token");
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    /// <summary>Asks GitHub for a device code and the code the user types.</summary>
    public async Task<DeviceCode> StartAsync(string clientId, CancellationToken cancellationToken)
    {
        var response = await PostAsync(CodeUri, [new("client_id", clientId)], GitHubJson.Default.DeviceCodeResponse, cancellationToken).ConfigureAwait(false);
        if (response is null || string.IsNullOrEmpty(response.DeviceCode) || string.IsNullOrEmpty(response.UserCode))
            throw new DeviceFlowException(DeviceFlowFailure.UnknownClient,
                "GitHub did not start the sign-in. Check that the GitHub App's client ID is right and the app has the device flow turned on.");

        var verification = Uri.TryCreate(response.VerificationUri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? uri
                : DefaultVerificationUri;
        return new DeviceCode(response.DeviceCode, response.UserCode, verification, TimeSpan.FromSeconds(Math.Max(response.Interval, 1)),
            time.GetUtcNow().AddSeconds(response.ExpiresIn > 0 ? response.ExpiresIn : 900));
    }

    /// <summary>Polls GitHub until the user approves or refuses the code, or it expires, waiting as long between polls as GitHub asks.</summary>
    public async Task<GitHubTokens> WaitForTokenAsync(string clientId, DeviceCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var interval = code.Interval;
        while (true)
        {
            await delay(interval, cancellationToken).ConfigureAwait(false);
            if (time.GetUtcNow() >= code.ExpiresAt)
                throw Expired();

            var response = await PostAsync(TokenUri,
            [
                new("client_id", clientId),
                new("device_code", code.Code),
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ], GitHubJson.Default.AccessTokenResponse, cancellationToken).ConfigureAwait(false);

            switch (response?.Error)
            {
                case null when response?.AccessToken is { Length: > 0 }:
                    return ToTokens(response);
                case "authorization_pending":
                    continue;
                case "slow_down":
                    var asked = response.Interval is > 0 ? TimeSpan.FromSeconds(response.Interval.Value) : TimeSpan.Zero;
                    interval = asked > interval ? asked : interval + SlowDownStep;
                    continue;
                case "expired_token":
                    throw Expired();
                case "access_denied":
                    throw new DeviceFlowException(DeviceFlowFailure.Denied, "The sign-in was cancelled on GitHub.");
                case "device_flow_disabled":
                    throw new DeviceFlowException(DeviceFlowFailure.Disabled, "The GitHub App does not have the device flow turned on. Turn on Enable Device Flow in the app's settings.");
                case "incorrect_client_credentials":
                    throw new DeviceFlowException(DeviceFlowFailure.UnknownClient, "GitHub does not know the GitHub App's client ID. Check Client ID in Settings, GitHub.");
                default:
                    throw new DeviceFlowException(DeviceFlowFailure.Other, response?.ErrorDescription is { Length: > 0 } description
                        ? $"GitHub did not finish the sign-in: {description}"
                        : "GitHub did not finish the sign-in. Try again.");
            }
        }
    }

    /// <summary>Exchanges a refresh token for new tokens; GitHub replaces the refresh token too.</summary>
    public async Task<GitHubTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        var response = await PostAsync(TokenUri,
        [
            new("client_id", clientId),
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ], GitHubJson.Default.AccessTokenResponse, cancellationToken).ConfigureAwait(false);

        return response is { Error: null, AccessToken.Length: > 0 }
            ? ToTokens(response)
            : throw new DeviceFlowException(DeviceFlowFailure.RefreshRefused, "The GitHub sign-in expired. Sign in to GitHub again.");
    }

    private static DeviceFlowException Expired() =>
        new(DeviceFlowFailure.Expired, "The code expired before it was entered on GitHub. Start the sign-in again for a new code.");

    private GitHubTokens ToTokens(AccessTokenResponse response)
    {
        var now = time.GetUtcNow();
        return new GitHubTokens(response.AccessToken!,
            response.ExpiresIn is > 0 ? now.AddSeconds(response.ExpiresIn.Value) : null,
            response.RefreshToken is { Length: > 0 } ? response.RefreshToken : null,
            response.RefreshTokenExpiresIn is > 0 ? now.AddSeconds(response.RefreshTokenExpiresIn.Value) : null);
    }

    private async Task<T?> PostAsync<T>(Uri uri, IEnumerable<KeyValuePair<string, string>> form, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(form) };
        GitHubClient.AddCommonHeaders(request, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
                throw new DeviceFlowException(DeviceFlowFailure.Network, "GitHub could not be reached. Check the connection and try again.");

            // GitHub answers the flow's errors with 200 and an error field, and some with 4xx and the same body.
            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new DeviceFlowException(DeviceFlowFailure.Network, "GitHub could not be reached. Check the connection and try again.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceFlowException(DeviceFlowFailure.Network, "GitHub took too long to answer. Try again in a moment.", exception);
        }
        catch (JsonException exception)
        {
            throw new DeviceFlowException(DeviceFlowFailure.Other, "GitHub sent an answer Runesmith does not understand.", exception);
        }
    }
}
