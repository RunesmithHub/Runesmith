using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Runesmith.Shell.Running;

/// <summary>The lines a console view shows, kept in step with a <see cref="ConsoleBuffer"/> through its deltas, adding many lines in one
/// change so a burst of output costs one layout.</summary>
internal sealed class ConsoleLineList : Collection<ConsoleLine>, INotifyCollectionChanged
{
    private long firstIndex;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Applies what changed in the buffer; returns whether anything did.</summary>
    public bool Apply(ConsoleDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var list = (List<ConsoleLine>)Items;
        var dropped = (int)Math.Clamp(delta.FirstIndex - firstIndex, 0, list.Count);
        var keep = (int)Math.Clamp(delta.From - Math.Max(firstIndex, delta.FirstIndex), 0, list.Count - dropped);
        var replaced = list.Count - dropped - keep;
        firstIndex = delta.FirstIndex;
        if (dropped == 0 && replaced == 0)
        {
            if (delta.Lines.Count == 0)
                return false;

            var start = list.Count;
            list.AddRange(delta.Lines);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)delta.Lines.ToList(), start));
            return true;
        }

        if (dropped == 0 && replaced == 1 && delta.Lines.Count >= 1)
        {
            var at = list.Count - 1;
            var old = list[at];
            list[at] = delta.Lines[0];
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, delta.Lines[0], old, at));
            if (delta.Lines.Count > 1)
            {
                var rest = delta.Lines.Skip(1).ToList();
                list.AddRange(rest);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)rest, at + 1));
            }

            return true;
        }

        list.RemoveRange(list.Count - replaced, replaced);
        list.RemoveRange(0, dropped);
        list.AddRange(delta.Lines);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }
}
