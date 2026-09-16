using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// Aggregate statistics for the end-of-game overlay. Server-authoritative; pushed to clients at the end.
    /// </summary>
    [Serializable]
    public class MatchStats
    {
        public int NightsSurvived;
        public int EvidenceSaved;
        public int EvidenceLost;          // captured but never made it to the locker
        public int SonarContacts;
        public int HydrophoneCalls;
        public int Photos;
        public int PhoneClips;
        public int EdnaSamples;
        public int RovClips;
        public int BeaconsLost;
        public int RovsLost;
        public int PlayersOverboard;
        public int PlayersLost;
        public int HullRepairs;
        public float TotalHullDamage;
        public int NessieBreaches;
        public int FundingSpent;
        public float TotalSeconds;
        public string Difficulty = "Wary";
        public bool Won;

        public Dictionary<ulong, PlayerStats> PerPlayer = new Dictionary<ulong, PlayerStats>();

        public PlayerStats For(ulong clientId)
        {
            if (!PerPlayer.TryGetValue(clientId, out var s))
            {
                s = new PlayerStats { ClientId = clientId };
                PerPlayer[clientId] = s;
            }
            return s;
        }

        public void CountEvidence(EvidenceType type)
        {
            EvidenceSaved++;
            switch (type)
            {
                case EvidenceType.SonarContact: SonarContacts++; break;
                case EvidenceType.HydrophoneCall: HydrophoneCalls++; break;
                case EvidenceType.TelephotoPhoto: Photos++; break;
                case EvidenceType.PhoneVideo: PhoneClips++; break;
                case EvidenceType.EdnaSample: EdnaSamples++; break;
                case EvidenceType.RovFootage: RovClips++; break;
            }
        }
    }

    [Serializable]
    public class PlayerStats
    {
        public ulong ClientId;
        public string Name = "Researcher";
        public int EvidenceCaptured;
        public int EvidenceSaved;
        public int TimesOverboard;
        public float SecondsInWater;
        public int Rations;
        public int Batteries;
        public bool Died;
    }

    /// <summary>Compact, serialisable summary sent to every client for the ending screen.</summary>
    public struct MatchSummary : INetworkSerializable
    {
        public bool Won;
        public int NightsSurvived;
        public int EvidenceSaved;
        public int EvidenceLost;
        public int Breaches;
        public int Overboard;
        public int PlayersLost;
        public int BeaconsLost;
        public float TotalHullDamage;
        public float TotalSeconds;
        public int DifficultyLevel;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Won);
            s.SerializeValue(ref NightsSurvived);
            s.SerializeValue(ref EvidenceSaved);
            s.SerializeValue(ref EvidenceLost);
            s.SerializeValue(ref Breaches);
            s.SerializeValue(ref Overboard);
            s.SerializeValue(ref PlayersLost);
            s.SerializeValue(ref BeaconsLost);
            s.SerializeValue(ref TotalHullDamage);
            s.SerializeValue(ref TotalSeconds);
            s.SerializeValue(ref DifficultyLevel);
        }

        public static MatchSummary From(MatchStats st) => new MatchSummary
        {
            Won = st.Won,
            NightsSurvived = st.NightsSurvived,
            EvidenceSaved = st.EvidenceSaved,
            EvidenceLost = st.EvidenceLost,
            Breaches = st.NessieBreaches,
            Overboard = st.PlayersOverboard,
            PlayersLost = st.PlayersLost,
            BeaconsLost = st.BeaconsLost,
            TotalHullDamage = st.TotalHullDamage,
            TotalSeconds = st.TotalSeconds,
        };
    }
}
