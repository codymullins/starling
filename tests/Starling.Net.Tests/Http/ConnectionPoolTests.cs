using FluentAssertions;
using Starling.Net.Http;
namespace Starling.Net.Tests.Http;

[TestClass]
public class ConnectionPoolTests
{
    private static OriginKey Origin(string host = "example.test", int port = 443)
        => OriginKey.Create("https", host, port);

    [TestMethod]
    public async Task TryAcquire_returns_the_same_transport_after_release()
    {
        var pool = new ConnectionPool();
        var origin = Origin();
        var fake = new FakeTransport(origin);

        await pool.ReleaseAsync(fake);
        var acquired = pool.TryAcquire(origin);

        acquired.Should().BeSameAs(fake);
        pool.IdleCount.Should().Be(0);
        fake.Disposed.Should().BeFalse("the transport was acquired by the caller, not discarded");
    }

    [TestMethod]
    public async Task TryAcquire_returns_null_when_pool_is_empty()
    {
        var pool = new ConnectionPool();
        pool.TryAcquire(Origin()).Should().BeNull();
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task TryAcquire_keys_on_origin_distinguishes_scheme_host_port()
    {
        var pool = new ConnectionPool();
        var http = OriginKey.Create("http", "example.test", 80);
        var https = OriginKey.Create("https", "example.test", 443);
        var altPort = OriginKey.Create("https", "example.test", 8443);
        var altHost = OriginKey.Create("https", "other.test", 443);

        var t1 = new FakeTransport(http);
        var t2 = new FakeTransport(https);
        var t3 = new FakeTransport(altPort);
        var t4 = new FakeTransport(altHost);
        await pool.ReleaseAsync(t1);
        await pool.ReleaseAsync(t2);
        await pool.ReleaseAsync(t3);
        await pool.ReleaseAsync(t4);

        pool.TryAcquire(http).Should().BeSameAs(t1);
        pool.TryAcquire(https).Should().BeSameAs(t2);
        pool.TryAcquire(altPort).Should().BeSameAs(t3);
        pool.TryAcquire(altHost).Should().BeSameAs(t4);

        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task Disposed_transport_is_not_returned_from_acquire()
    {
        var pool = new ConnectionPool();
        var origin = Origin();
        var fake = new FakeTransport(origin);
        await pool.ReleaseAsync(fake);

        fake.SimulatePeerClose();
        pool.TryAcquire(origin).Should().BeNull("a stale entry must be dropped silently");
        pool.IdleCount.Should().Be(0);
        fake.Disposed.Should().BeTrue("stale entries are discarded when encountered");
    }

    [TestMethod]
    public async Task Releasing_a_closed_transport_does_not_pool_it()
    {
        var pool = new ConnectionPool();
        var origin = Origin();
        var fake = new FakeTransport(origin);
        fake.SimulatePeerClose();

        await pool.ReleaseAsync(fake);

        pool.IdleCount.Should().Be(0);
        fake.Disposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task DrainExpired_disposes_entries_older_than_idle_timeout()
    {
        var pool = new ConnectionPool(maxPerOrigin: 6, idleTimeout: TimeSpan.FromMilliseconds(50));
        var origin = Origin();
        var stale = new FakeTransport(origin);
        var fresh = new FakeTransport(origin);

        await pool.ReleaseAsync(stale);
        // Make sure the second release is later than the first.
        await Task.Delay(100, CancellationToken.None);
        await pool.ReleaseAsync(fresh);

        // Pretend "now" is 75ms after the stale entry but before fresh's expiry.
        var now = DateTimeOffset.UtcNow;
        var drained = await pool.DrainExpiredAsync(now);

        drained.Should().Be(1);
        stale.Disposed.Should().BeTrue("stale entry expired and was disposed");
        fresh.Disposed.Should().BeFalse("fresh entry is still within the idle window");
        pool.IdleCount.Should().Be(1);
    }

    [TestMethod]
    public async Task DrainExpired_sync_overload_disposes_old_entries()
    {
        var pool = new ConnectionPool(maxPerOrigin: 4, idleTimeout: TimeSpan.FromHours(1));
        var origin = Origin();
        var entry = new FakeTransport(origin);
        await pool.ReleaseAsync(entry);

        // Pass a zero-ish timeout to force eviction of everything.
        var drained = pool.DrainExpired(TimeSpan.FromTicks(1));

        drained.Should().Be(1);
        entry.Disposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Pool_capacity_is_bounded_and_oldest_is_evicted()
    {
        var pool = new ConnectionPool(maxPerOrigin: 2, idleTimeout: TimeSpan.FromMinutes(5));
        var origin = Origin();
        var t1 = new FakeTransport(origin) { Tag = "first" };
        var t2 = new FakeTransport(origin) { Tag = "second" };
        var t3 = new FakeTransport(origin) { Tag = "third" };

        await pool.ReleaseAsync(t1);
        await pool.ReleaseAsync(t2);
        await pool.ReleaseAsync(t3); // should evict t1 (oldest)

        pool.IdleCount.Should().Be(2);
        t1.Disposed.Should().BeTrue("oldest entry must be LRU-evicted to make room");
        t2.Disposed.Should().BeFalse();
        t3.Disposed.Should().BeFalse();

        // MRU acquisition order: t3, then t2.
        pool.TryAcquire(origin).Should().BeSameAs(t3);
        pool.TryAcquire(origin).Should().BeSameAs(t2);
        pool.TryAcquire(origin).Should().BeNull();
    }

    [TestMethod]
    public async Task DisposeAll_disposes_every_entry_across_origins()
    {
        var pool = new ConnectionPool();
        var a = new FakeTransport(Origin("a.test"));
        var b = new FakeTransport(Origin("b.test"));
        var c = new FakeTransport(Origin("c.test"));
        await pool.ReleaseAsync(a);
        await pool.ReleaseAsync(b);
        await pool.ReleaseAsync(c);

        await pool.DisposeAllAsync();

        pool.IdleCount.Should().Be(0);
        a.Disposed.Should().BeTrue();
        b.Disposed.Should().BeTrue();
        c.Disposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task Release_on_disposed_pool_closes_the_transport()
    {
        var pool = new ConnectionPool();
        await pool.DisposeAsync();
        var fake = new FakeTransport(Origin());

        await pool.ReleaseAsync(fake);

        pool.IdleCount.Should().Be(0);
        fake.Disposed.Should().BeTrue("disposed pools refuse new releases and clean up the transport");
    }

    [TestMethod]
    public void Constructor_rejects_zero_capacity_or_non_positive_timeout()
    {
        Action zero = () => new ConnectionPool(0, TimeSpan.FromSeconds(1));
        Action negative = () => new ConnectionPool(2, TimeSpan.Zero);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void OriginKey_normalises_scheme_and_host_to_lowercase()
    {
        var a = OriginKey.Create("HTTPS", "Example.COM", 443);
        var b = OriginKey.Create("https", "example.com", 443);
        a.Should().Be(b);
    }

    /// <summary>
    /// In-memory <see cref="IHttpTransport"/> stand-in used by the pool tests.
    /// Exposes a flag so assertions can verify the pool disposed (or didn't
    /// dispose) the entry, plus a way to simulate the peer closing the
    /// connection while the transport was idle.
    /// </summary>
    private sealed class FakeTransport : IHttpTransport
    {
        public OriginKey Origin { get; }
        public Stream Stream { get; } = new MemoryStream();
        public bool Disposed { get; private set; }
        public bool Open { get; private set; } = true;
        public string Tag { get; init; } = "";

        public FakeTransport(OriginKey origin) => Origin = origin;

        public bool IsOpen => Open && !Disposed;

        public void SimulatePeerClose() => Open = false;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
