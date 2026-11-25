using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SidebarDiagnostics.Utilities;

namespace SidebarDiagnostics
{
    // Coalesces off-UI-thread PropertyChanged (and similar UI notification) dispatches
    // and drains them at DispatcherPriority.Background so sensor polling never starves
    // WPF Input (priority 5) such as tray right-clicks and Settings interactions.
    public static class UiNotify
    {
        private readonly struct NotifyKey : IEquatable<NotifyKey>
        {
            public readonly object Target;
            public readonly string PropertyName;

            public NotifyKey(object target, string propertyName)
            {
                Target = target;
                PropertyName = propertyName ?? string.Empty;
            }

            public bool Equals(NotifyKey other)
            {
                return ReferenceEquals(Target, other.Target) &&
                    string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is NotifyKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Target) * 397) ^ StringComparer.Ordinal.GetHashCode(PropertyName);
                }
            }
        }

        private readonly struct NotifyItem
        {
            public readonly NotifyKey Key;
            public readonly Action Raise;

            public NotifyItem(NotifyKey key, Action raise)
            {
                Key = key;
                Raise = raise;
            }
        }

        private static readonly ConcurrentQueue<NotifyItem> _queue = new ConcurrentQueue<NotifyItem>();
        private static readonly ConcurrentDictionary<NotifyKey, byte> _pending = new ConcurrentDictionary<NotifyKey, byte>();
        private static readonly ConcurrentDictionary<Type, byte> _loggedExceptionTypes = new ConcurrentDictionary<Type, byte>();
        private static readonly long _drainSliceTicks = (long)(Stopwatch.Frequency * 0.004);
        private static int _scheduled;

        public static void Post(object target, string propertyName, Action raise)
        {
            if (raise == null)
            {
                return;
            }

            Application app = Application.Current;
            if (app == null)
            {
                return;
            }

            Dispatcher dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                raise();
                return;
            }

            NotifyKey key = new NotifyKey(target, propertyName);
            if (!_pending.TryAdd(key, 0))
            {
                return;
            }

            _queue.Enqueue(new NotifyItem(key, raise));
            ScheduleDrain(dispatcher);
        }

        private static void ScheduleDrain(Dispatcher dispatcher)
        {
            if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Drain(dispatcher)));
            }
            catch
            {
                Interlocked.Exchange(ref _scheduled, 0);
            }
        }

        private static void Drain(Dispatcher dispatcher)
        {
            long startTicks = Stopwatch.GetTimestamp();
            int processed = 0;

            try
            {
                while (_queue.TryDequeue(out NotifyItem item))
                {
                    _pending.TryRemove(item.Key, out _);

                    try
                    {
                        item.Raise();
                    }
                    catch (Exception ex)
                    {
                        if (_loggedExceptionTypes.TryAdd(ex.GetType(), 0))
                        {
                            ErrorLog.Write(ex);
                        }
                    }

                    processed++;
                    if ((processed & 15) == 0 && (Stopwatch.GetTimestamp() - startTicks) >= _drainSliceTicks)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _scheduled, 0);

                if (!_queue.IsEmpty && Application.Current != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    ScheduleDrain(dispatcher);
                }
            }
        }
    }
}
