using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;

namespace Runesmith.Sdk.Localization;

/// <summary>Localized text for code without <see cref="ILocalizationService"/>: the generated <c>Strings</c> classes, XAML, and code that only
/// has a message id.</summary>
/// <remarks>Added in plugin API 0.1.2. Views built in code show text that follows the language with <see cref="Bind"/>:
/// <c>Localized.Bind(title, TextBlock.TextProperty, () =&gt; Strings.SettingsTitle)</c>.</remarks>
public static class Localized
{
    private static readonly LanguageSubscriptions Subscriptions = new();
    private static volatile ILocalizationHost? host;

    /// <summary>Raised on the UI thread after the language changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Gets the localizer of the plugin, or the part of Runesmith, that an assembly belongs to.</summary>
    public static ILocalizer For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return host?.For(assembly) ?? ReadableIds.Instance;
    }

    /// <summary>Gets the localizer of the plugin, or the part of Runesmith, that an assembly belongs to, or <paramref name="fallback"/> while
    /// Runesmith's localization is not there, such as in unit tests.</summary>
    /// <remarks>The generated <c>Strings</c> class passes its English messages as the fallback.</remarks>
    public static ILocalizer For(Assembly assembly, ILocalizer fallback)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(fallback);
        return host?.For(assembly) ?? fallback;
    }

    /// <summary>Gets a message of the plugin the calling code belongs to, in the current language.</summary>
    /// <inheritdoc cref="ILocalizer.Get" path="/param"/>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Get(string id, LocalizationArgs? args = null) => For(Assembly.GetCallingAssembly()).Get(id, args);

    /// <summary>Gets text that follows the language: what <paramref name="text"/> returns now, and again after each change of language.</summary>
    /// <remarks>A subscription lasts while something holds it, such as the binding of a control, so a control that is gone is not kept
    /// alive.</remarks>
    public static IObservable<string> Observe(Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new LanguageText(text);
    }

    /// <summary>Binds a property, such as <c>TextBlock.TextProperty</c> or <c>ContentControl.ContentProperty</c>, to text that follows the
    /// language.</summary>
    /// <returns>The binding; dispose it to stop following the language.</returns>
    public static IDisposable Bind(AvaloniaObject target, AvaloniaProperty property, Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        return target.Bind(property, Observe(text).ToBinding());
    }

    /// <summary>Lets Runesmith's localization answer <see cref="For(Assembly)"/>; null detaches it.</summary>
    internal static void Attach(ILocalizationHost? value) => host = value;

    /// <summary>Tells every subscriber that the language changed; call it on the UI thread.</summary>
    internal static void RaiseChanged()
    {
        Subscriptions.Refresh();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Gets a readable form of a message id for when no language has the message, such as "Settings title" for
    /// <c>settings-title</c>.</summary>
    internal static string Readable(string id)
    {
        var words = new System.Text.StringBuilder(id.Length);
        foreach (var character in id)
        {
            var separator = character is '-' or '_' or '.';
            if (separator && (words.Length == 0 || words[^1] == ' '))
                continue;

            words.Append(separator ? ' ' : words.Length == 0 ? char.ToUpperInvariant(character) : character);
        }

        return words.ToString().TrimEnd() is { Length: > 0 } text ? text : "?";
    }

    private sealed class ReadableIds : ILocalizer
    {
        public static readonly ReadableIds Instance = new();

        public string Get(string id, LocalizationArgs? args = null) => Readable(id);

        public bool TryGet(string id, LocalizationArgs? args, [NotNullWhen(true)] out string? text)
        {
            text = null;
            return false;
        }
    }

    private sealed class LanguageText(Func<string> text) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            var subscription = new LanguageSubscription(text, observer);
            Subscriptions.Add(subscription);
            subscription.Refresh();
            return subscription;
        }
    }

    private sealed class LanguageSubscription(Func<string> text, IObserver<string> observer) : IDisposable
    {
        private volatile bool disposed;

        public bool IsDisposed => disposed;

        public void Refresh()
        {
            if (disposed)
                return;

            string value;
            try
            {
                value = text();
            }
            catch (Exception exception)
            {
                observer.OnError(exception);
                return;
            }

            observer.OnNext(value);
        }

        public void Dispose() => disposed = true;
    }

    // Weak, so a binding that is never disposed does not keep its control alive.
    private sealed class LanguageSubscriptions
    {
        private readonly Lock gate = new();
        private readonly List<WeakReference<LanguageSubscription>> items = [];

        public void Add(LanguageSubscription subscription)
        {
            lock (gate)
            {
                if (items.Count > 0 && items.Count % 64 == 0)
                    items.RemoveAll(item => !item.TryGetTarget(out var alive) || alive.IsDisposed);
                items.Add(new WeakReference<LanguageSubscription>(subscription));
            }
        }

        public void Refresh()
        {
            List<LanguageSubscription> alive = [];
            lock (gate)
            {
                items.RemoveAll(item => !item.TryGetTarget(out var subscription) || subscription.IsDisposed);
                foreach (var item in items)
                {
                    if (item.TryGetTarget(out var subscription))
                        alive.Add(subscription);
                }
            }

            foreach (var subscription in alive)
                subscription.Refresh();
        }
    }
}

/// <summary>Runesmith's localization, which answers <see cref="Localized.For(Assembly)"/>.</summary>
internal interface ILocalizationHost
{
    ILocalizer For(Assembly assembly);
}
