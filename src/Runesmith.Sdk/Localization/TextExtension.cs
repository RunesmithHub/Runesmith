using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

[assembly: XmlnsDefinition(Runesmith.Sdk.Localization.TextExtension.XmlNamespace, "Runesmith.Sdk.Localization")]
[assembly: XmlnsPrefix(Runesmith.Sdk.Localization.TextExtension.XmlNamespace, "l")]

namespace Runesmith.Sdk.Localization;

/// <summary>A message of the plugin, in XAML, that follows the language: <c>{l:Text settings-title}</c>, with
/// <c>xmlns:l="https://github.com/runesmithhub/runesmith/localization"</c>.</summary>
/// <remarks>Added in plugin API 0.1.2. The message comes from the plugin the XAML file belongs to. For a message with variables, set
/// <see cref="Args"/> to a <see cref="LocalizationArgs"/> or a binding that gives one: <c>{l:Text files-count, Args={Binding CountArgs}}</c>.</remarks>
public sealed class TextExtension : MarkupExtension
{
    /// <summary>The XML namespace of the localization markup.</summary>
    public const string XmlNamespace = "https://github.com/runesmithhub/runesmith/localization";

    public TextExtension()
    {
    }

    public TextExtension(string id) => Id = id;

    /// <summary>Gets or sets the message id, or <c>message.attribute</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the values of the message's variables: a <see cref="LocalizationArgs"/>, or a binding that gives one.</summary>
    public object? Args { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var localizer = Localized.For(AssemblyOf(serviceProvider));
        var id = Id;
        if (Args is BindingBase args)
        {
            return new MultiBinding
            {
                Bindings = { Localized.Observe(static () => "").ToBinding(), args },
                Converter = new MessageConverter(localizer, id),
            };
        }

        var values = Args as LocalizationArgs;
        return Localized.Observe(() => localizer.Get(id, values)).ToBinding();
    }

    private static Assembly AssemblyOf(IServiceProvider services)
    {
        var root = (services.GetService(typeof(IRootObjectProvider)) as IRootObjectProvider)?.RootObject?.GetType().Assembly;
        if (services.GetService(typeof(IUriContext)) is IUriContext { BaseUri: { Scheme: "avares" } uri } && uri.Host.Length > 0)
        {
            if (root is not null && string.Equals(root.GetName().Name, uri.Host, StringComparison.OrdinalIgnoreCase))
                return root;
            if (AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, uri.Host, StringComparison.OrdinalIgnoreCase)) is { } named)
                return named;
        }

        return root ?? typeof(TextExtension).Assembly;
    }

    private sealed class MessageConverter(ILocalizer localizer, string id) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            localizer.Get(id, values.Count > 1 ? values[1] as LocalizationArgs : null);
    }
}
