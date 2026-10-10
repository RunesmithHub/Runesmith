using System.Text.Json;

namespace Runesmith.Shell.Web;

/// <summary>The message channel between a plugin and its page: JSON text both ways, never objects.</summary>
internal static class WebMessages
{
    /// <summary>What the bridge script sends once the page has loaded; pages cannot send it, since their messages are JSON.</summary>
    public const string Ready = "\u0001ready";

    /// <summary>The script that asks the page's bridge to say it is ready again, after a load whose first message was not accepted.</summary>
    public const string ReadyScript = "globalThis.__runesmithReady && globalThis.__runesmithReady();";

    /// <summary>The script that delivers a message to the page; the JSON goes in as a string literal the page parses, so it cannot run as
    /// code.</summary>
    public static string DeliveryScript(string json) =>
        $"globalThis.__runesmithDeliver && globalThis.__runesmithDeliver({JsonSerializer.Serialize(json, WebMessagesJson.Default.String)});";

    /// <summary>Whether a text is one JSON value.</summary>
    public static bool IsJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The script pages load from <see cref="BundledFiles.BridgePath"/>: <c>runesmith.postMessage(value)</c> and
    /// <c>runesmith.onMessage(listener)</c>.</summary>
    public const string BridgeScript = """
        (() => {
          'use strict';
          const listeners = new Set();
          const waiting = [];
          const send = (text) => {
            const invoke = globalThis.invokeCSharpAction ?? globalThis.external?.invokeCSharpAction;
            if (typeof invoke === 'function') invoke(text);
          };
          const deliver = (text) => {
            const value = JSON.parse(text);
            if (listeners.size === 0) { waiting.push(value); return; }
            for (const listener of listeners) {
              try { listener(value); } catch (error) { console.error(error); }
            }
          };
          const api = Object.freeze({
            postMessage(value) { send(JSON.stringify(value === undefined ? null : value)); },
            onMessage(listener) {
              listeners.add(listener);
              for (const value of waiting.splice(0)) {
                try { listener(value); } catch (error) { console.error(error); }
              }
              return () => listeners.delete(listener);
            },
          });
          Object.defineProperty(globalThis, 'runesmith', { value: api });
          Object.defineProperty(globalThis, '__runesmithDeliver', { value: deliver });
          const ready = () => send('\u0001ready');
          Object.defineProperty(globalThis, '__runesmithReady', { value: ready });
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, { once: true });
          else ready();
        })();
        """;
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class WebMessagesJson : System.Text.Json.Serialization.JsonSerializerContext;
