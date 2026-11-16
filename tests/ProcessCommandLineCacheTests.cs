using System;
using NUnit.Framework;
using TileKeeper.Class;

namespace TileKeeper.Tests
{
    /// <summary>
    /// Behaviour of the per-process command-line memo. Command lines are immutable for
    /// the lifetime of a process, but a PID is reused once it exits — so the cache is
    /// keyed on (pid, process start time) and a recycled PID must re-fetch.
    /// </summary>
    [TestFixture]
    public class ProcessCommandLineCacheTests
    {
        private const int  Pid   = 4321;
        private const long Start = 638900000000000000L; // arbitrary DateTime.UtcNow.Ticks

        [Test]
        public void ReturnsCachedValueWithoutRefetching()
        {
            var cache = new ProcessCommandLineCache();
            int fetches = 0;

            string first  = cache.GetOrFetch(Pid, Start, () => { fetches++; return "app.exe -arg"; });
            string second = cache.GetOrFetch(Pid, Start, () => { fetches++; return "changed"; });

            Assert.That(first,  Is.EqualTo("app.exe -arg"));
            Assert.That(second, Is.EqualTo("app.exe -arg"));
            Assert.That(fetches, Is.EqualTo(1));
        }

        [Test]
        public void RefetchesWhenPidIsReusedByNewProcess()
        {
            var cache = new ProcessCommandLineCache();
            int fetches = 0;

            cache.GetOrFetch(Pid, Start, () => { fetches++; return "old"; });
            string second = cache.GetOrFetch(Pid, Start + 1, () => { fetches++; return "new"; });

            Assert.That(second, Is.EqualTo("new"));
            Assert.That(fetches, Is.EqualTo(2));
        }

        [Test]
        public void DoesNotCacheNullResult()
        {
            var cache = new ProcessCommandLineCache();
            int fetches = 0;

            cache.GetOrFetch(Pid, Start, () => { fetches++; return null; });
            string second = cache.GetOrFetch(Pid, Start, () => { fetches++; return null; });

            Assert.That(second, Is.Null);
            Assert.That(fetches, Is.EqualTo(2));
        }

        [Test]
        public void KeepsEntriesSeparatePerProcess()
        {
            var cache = new ProcessCommandLineCache();

            cache.GetOrFetch(1, Start, () => "one");
            cache.GetOrFetch(2, Start, () => "two");

            Assert.That(cache.GetOrFetch(1, Start, () => "nope"), Is.EqualTo("one"));
            Assert.That(cache.GetOrFetch(2, Start, () => "nope"), Is.EqualTo("two"));
        }

        [Test]
        public void StaysBoundedUnderManyDistinctKeys()
        {
            var cache = new ProcessCommandLineCache(maxEntries: 8);

            for (int i = 0; i < 100; i++)
                cache.GetOrFetch(1000 + i, Start, () => "x");

            Assert.That(cache.Count, Is.InRange(1, 8));
        }
    }
}
