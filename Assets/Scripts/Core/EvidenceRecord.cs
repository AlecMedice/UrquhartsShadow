using System;
using Unity.Collections;
using Unity.Netcode;

namespace UrquhartsShadow.Core
{
    /// <summary>
    /// One saved piece of evidence. Network-serialisable so the whole team sees the locker contents.
    /// </summary>
    public struct EvidenceRecord : INetworkSerializable, IEquatable<EvidenceRecord>
    {
        public int Id;
        public EvidenceType Type;
        public float Quality;          // 0..1
        public int Night;              // 1..5
        public float NightTimeSeconds; // seconds into the night when captured
        public ulong CapturedByClientId;
        public FixedString64Bytes Label;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            int type = (int)Type;
            serializer.SerializeValue(ref type);
            Type = (EvidenceType)type;
            serializer.SerializeValue(ref Quality);
            serializer.SerializeValue(ref Night);
            serializer.SerializeValue(ref NightTimeSeconds);
            serializer.SerializeValue(ref CapturedByClientId);
            serializer.SerializeValue(ref Label);
        }

        public bool Equals(EvidenceRecord other) => Id == other.Id;
        public override bool Equals(object obj) => obj is EvidenceRecord o && Equals(o);
        public override int GetHashCode() => Id;
        public override string ToString() => $"#{Id} {Type} q={Quality:0.00} n{Night}";
    }

    /// <summary>
    /// Evidence a player has captured but not yet deposited in the locker on the main deck.
    /// Plain class because it only lives on the owning client until saved.
    /// </summary>
    [Serializable]
    public class PendingEvidence
    {
        public EvidenceType Type;
        public float Quality;
        public string Label;
        public float CapturedAtNightTime;

        public PendingEvidence(EvidenceType type, float quality, string label, float nightTime)
        {
            Type = type; Quality = quality; Label = label; CapturedAtNightTime = nightTime;
        }
    }
}
