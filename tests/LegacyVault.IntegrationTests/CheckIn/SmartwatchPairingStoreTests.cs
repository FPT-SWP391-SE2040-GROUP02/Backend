using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.CheckIn;

public sealed class SmartwatchPairingStoreTests
{
    [Fact]
    public void CreateCode_GeneratesSixDigitCodeAndStoresSession()
    {
        var store = new SmartwatchPairingStore();
        var userId = Guid.NewGuid();
        var vaultId = Guid.NewGuid();

        string code = store.CreateCode(userId, vaultId, TimeSpan.FromMinutes(5));

        Assert.Equal(6, code.Length);
        Assert.True(int.TryParse(code, out _));

        bool found = store.TryGetStatus(code, userId, out var session);
        Assert.True(found);
        Assert.Equal(userId, session.UserId);
        Assert.Equal(vaultId, session.VaultId);
        Assert.False(session.IsPaired);
        Assert.Null(session.DeviceName);
        Assert.Null(session.PairedAt);
    }

    [Fact]
    public void TryPair_MarksSessionAsPaired_AndCannotPairTwice()
    {
        var store = new SmartwatchPairingStore();
        var userId = Guid.NewGuid();
        var vaultId = Guid.NewGuid();

        string code = store.CreateCode(userId, vaultId, TimeSpan.FromMinutes(5));

        bool paired = store.TryPair(code, "Samsung Galaxy Watch 6", out var session);
        Assert.True(paired);
        Assert.True(session.IsPaired);
        Assert.Equal("Samsung Galaxy Watch 6", session.DeviceName);
        Assert.NotNull(session.PairedAt);

        // Cannot pair a second time with same code
        bool secondPair = store.TryPair(code, "Another Watch", out _);
        Assert.False(secondPair);
    }

    [Fact]
    public void TryGetStatus_ReflectsPairingState_AndEnforcesUserOwnership()
    {
        var store = new SmartwatchPairingStore();
        var userId = Guid.NewGuid();
        var vaultId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        string code = store.CreateCode(userId, vaultId, TimeSpan.FromMinutes(5));

        // Different user cannot check status
        Assert.False(store.TryGetStatus(code, otherUser, out _));

        // Owner checks status before pair
        Assert.True(store.TryGetStatus(code, userId, out var beforeSession));
        Assert.False(beforeSession.IsPaired);

        // Pair device
        store.TryPair(code, "Galaxy Watch Ultra", out _);

        // Owner checks status after pair
        Assert.True(store.TryGetStatus(code, userId, out var afterSession));
        Assert.True(afterSession.IsPaired);
        Assert.Equal("Galaxy Watch Ultra", afterSession.DeviceName);
    }

    [Fact]
    public void ExpiredSession_CannotBePairedOrRetrieved()
    {
        var store = new SmartwatchPairingStore();
        var userId = Guid.NewGuid();
        var vaultId = Guid.NewGuid();

        // Expired immediately
        string code = store.CreateCode(userId, vaultId, TimeSpan.FromMilliseconds(-1));

        Assert.False(store.TryPair(code, "Watch", out _));
        Assert.False(store.TryGetStatus(code, userId, out _));
    }
}
