namespace UrquhartsShadow.Core
{
    /// <summary>High-level flow of a whole match.</summary>
    public enum GamePhase
    {
        None = 0,
        Title,
        Lobby,
        Loading,
        NightSearch,   // the core loop: team searches, Nessie hunts
        Dawn,          // resupply / breather between nights
        Victory,       // 10 evidence saved
        Finale,        // night 5 failed: hull cracks, boat sinks
        Defeat         // fade to black after finale (or early sink)
    }

    public enum WeatherType { Clear = 0, Overcast = 1, Rain = 2, Storm = 3 }

    public enum EvidenceType
    {
        SonarContact = 0,
        HydrophoneCall = 1,
        TelephotoPhoto = 2,
        PhoneVideo = 3,
        EdnaSample = 4,
        RovFootage = 5
    }
}
