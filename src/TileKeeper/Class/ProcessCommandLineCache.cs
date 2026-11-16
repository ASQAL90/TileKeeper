using System;
using System.Collections.Generic;

namespace TileKeeper.Class
{
    /// <summary>
    /// Memoises process command lines so the expensive WMI query (Win32ApiImpl,
    /// GetProcessCommandLine) runs once per process instead of once per monitor tick.
    /// Keyed on (pid, process start time) because Windows recycles PIDs: a new process on
    /// the same PID must not inherit the previous command line.
    /// </summary>
    public sealed class ProcessCommandLineCache
    {
        private const int DefaultMaxEntries = 512;

        private readonly Dictionary<Key, string> _entries = new Dictionary<Key, string>();
        private readonly object _gate = new object();
        private readonly int _maxEntries;

        public ProcessCommandLineCache() : this(DefaultMaxEntries) { }

        public ProcessCommandLineCache(int maxEntries)
        {
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException("maxEntries");
            _maxEntries = maxEntries;
        }

        /// <summary>Number of live entries; exposed for tests and diagnostics.</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>
        /// Returns the cached command line for (pid, start time), calling
        /// <paramref name="fetch"/> once when absent. A null result is never cached, so a
        /// transient WMI failure is retried on the next call.
        /// </summary>
        public string GetOrFetch(int pid, long startTimeUtcTicks, Func<string> fetch)
        {
            var key = new Key(pid, startTimeUtcTicks);

            lock (_gate)
            {
                string cached;
                if (_entries.TryGetValue(key, out cached))
                    return cached;
            }

            // Fetch outside the lock: a WMI query can take ~100ms and must not stall other callers.
            string value = fetch();
            if (value == null)
                return null;

            lock (_gate)
            {
                if (_entries.Count >= _maxEntries)
                    _entries.Clear(); // ponytail: crude flush, swap for LRU if a machine ever runs >512 processes
                _entries[key] = value;
            }
            return value;
        }

        /// <summary>Immutable (pid, start-time) pair; avoids boxing a ValueTuple as a dictionary key.</summary>
        private struct Key : IEquatable<Key>
        {
            private readonly int _pid;
            private readonly long _startTicks;

            public Key(int pid, long startTicks)
            {
                _pid = pid;
                _startTicks = startTicks;
            }

            public bool Equals(Key other) => _pid == other._pid && _startTicks == other._startTicks;

            public override bool Equals(object obj) => obj is Key && Equals((Key)obj);

            public override int GetHashCode() => (_pid * 397) ^ _startTicks.GetHashCode();
        }
    }
}
