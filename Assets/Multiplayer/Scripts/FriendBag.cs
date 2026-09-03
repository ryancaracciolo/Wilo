using System;

/// <summary>One human bag the host uses to score a friend event.</summary>
[Serializable]
public struct FriendBag
{
    public long ClientId;
    public string PlayerId;
    public string Name;
    public float Pounds;
    public int Fish;
    public float LunkerLargemouth;
    public float LunkerSmallmouth;
    public bool Forfeited;
}

[Serializable]
public class FriendResultBatch
{
    public FriendResultEntry[] Entries = Array.Empty<FriendResultEntry>();
}

[Serializable]
public class FriendResultEntry
{
    public long ClientId;
    public string PlayerId;
    public string ResultJson;
}
