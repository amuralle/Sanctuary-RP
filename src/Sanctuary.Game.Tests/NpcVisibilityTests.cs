using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class NpcVisibilityTests
{
    [TestMethod]
    public void OnAddVisiblePlayers_ReplacesPreviousSessionWithSameGuid()
    {
        var npc = new Npc(null!);
        var previousSession = new Player(null!, null!, null!, null!) { Guid = 17 };
        var currentSession = new Player(null!, null!, null!, null!) { Guid = 17 };

        npc.OnAddVisiblePlayers(previousSession);
        npc.OnAddVisiblePlayers(currentSession);

        Assert.AreSame(currentSession, npc.VisiblePlayers[17]);
    }

    [TestMethod]
    public void OnRemoveVisiblePlayers_DoesNotRemoveReplacementSessionWithSameGuid()
    {
        var npc = new Npc(null!);
        var previousSession = new Player(null!, null!, null!, null!) { Guid = 17 };
        var currentSession = new Player(null!, null!, null!, null!) { Guid = 17 };

        npc.OnAddVisiblePlayers(previousSession);
        npc.OnAddVisiblePlayers(currentSession);
        npc.OnRemoveVisiblePlayers(previousSession);

        Assert.AreSame(currentSession, npc.VisiblePlayers[17]);
    }
}
