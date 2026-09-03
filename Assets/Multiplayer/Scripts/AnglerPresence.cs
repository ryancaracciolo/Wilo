using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One networked angler. The owner writes where they are; everyone else
/// sees a silent copy of the player and boat at that spot.
/// Idle vs driving is a pose byte so the remote boat can sit still or go.
/// </summary>
public class AnglerPresence : NetworkBehaviour
{
    public enum Pose : byte
    {
        OnFoot = 0,
        BoatIdle = 1,
        BoatDriving = 2
    }

    readonly NetworkVariable<Vector3> position = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> yaw = new NetworkVariable<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<byte> pose = new NetworkVariable<byte>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> ready = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> submitted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> forfeited = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> bagPounds = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<int> bagFish = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> lunkerLm = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> lunkerSm = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Transform player;
    PlayerBoatInteractor boats;
    GameObject bodyVisual;
    GameObject boatVisual;
    float nextBagWrite;
    bool resultApplied;

    public bool Ready => ready.Value;
    public bool Submitted => submitted.Value;
    public bool Forfeited => forfeited.Value;
    public string AnglerName { get; private set; } = "";
    public string PlayerId { get; private set; } = "";
    public Pose CurrentPose => (Pose)pose.Value;

    public FriendBag Bag => new FriendBag
    {
        ClientId = (long)OwnerClientId,
        PlayerId = PlayerId,
        Name = string.IsNullOrEmpty(AnglerName) ? "Angler" : AnglerName,
        Pounds = forfeited.Value ? 0f : bagPounds.Value,
        Fish = forfeited.Value ? 0 : bagFish.Value,
        LunkerLargemouth = forfeited.Value ? 0f : lunkerLm.Value,
        LunkerSmallmouth = forfeited.Value ? 0f : lunkerSm.Value,
        Forfeited = forfeited.Value
    };

    public override void OnNetworkSpawn()
    {
        TournamentLobby.Instance?.Register(this);
        if (IsOwner)
        {
            if (TournamentLobby.Instance != null)
                PlayerId = TournamentLobby.Instance.LocalPlayerId;
            BindOwner();
        }
        else
            BuildRemoteVisual();
    }

    public override void OnNetworkDespawn()
    {
        TournamentLobby.Instance?.Unregister(this);
        if (bodyVisual != null)
            Destroy(bodyVisual);
        if (boatVisual != null)
            Destroy(boatVisual);
    }

    public void SetReady(bool value)
    {
        if (IsOwner)
            ready.Value = value;
    }

    public void Submit(bool giveUp)
    {
        if (!IsOwner)
            return;

        WriteBag();
        forfeited.Value = giveUp;
        submitted.Value = true;
    }

    [ClientRpc]
    public void StartEventClientRpc()
    {
        TournamentLobby.Instance?.BeginLocalEvent();
    }

    [ClientRpc]
    public void FinishEventClientRpc(string batchJson)
    {
        if (resultApplied)
            return;

        resultApplied = true;
        TournamentLobby.Instance?.ApplyFriendBatch(batchJson);
    }

    void Update()
    {
        if (IsOwner)
        {
            WritePose();
            if (Time.time >= nextBagWrite)
            {
                nextBagWrite = Time.time + 0.5f;
                WriteBag();
            }
        }
        else
        {
            ApplyRemote();
        }

        if (IsServer && IsOwner)
            TournamentLobby.Instance?.HostTick();
    }

    void BindOwner()
    {
        GameObject go = GameObject.FindGameObjectWithTag("Player");
        if (go == null)
            return;

        player = go.transform;
        boats = go.GetComponent<PlayerBoatInteractor>();

        string name = "Angler";
        var progress = go.GetComponent<PlayerProgress>();
        if (progress != null && progress.HasName)
            name = progress.DisplayName;

        string id = TournamentLobby.Instance != null ? TournamentLobby.Instance.LocalPlayerId : "";
        if (string.IsNullOrEmpty(id) && SaveService.Instance != null)
            id = SaveService.Instance.Player.playerId;
        SubmitIdentityServerRpc(name, id ?? "");
        WritePose();
    }

    [ServerRpc]
    void SubmitIdentityServerRpc(string name, string id)
    {
        SetIdentityClientRpc(name, id);
    }

    [ClientRpc]
    void SetIdentityClientRpc(string name, string id)
    {
        AnglerName = name ?? "";
        PlayerId = id ?? "";
        TournamentLobby.Instance?.NotifyChanged();
    }

    void WritePose()
    {
        if (player == null)
            BindOwner();
        if (player == null)
            return;

        BoatMotor boat = boats != null ? boats.OccupiedBoat : null;
        if (boat != null)
        {
            position.Value = boat.transform.position;
            yaw.Value = boat.BowYaw;
            pose.Value = (byte)(boat.HasDriveInput || boat.Speed > 0.4f ? Pose.BoatDriving : Pose.BoatIdle);
            return;
        }

        position.Value = player.position;
        yaw.Value = player.eulerAngles.y;
        pose.Value = (byte)Pose.OnFoot;
    }

    void WriteBag()
    {
        TournamentDirector director = TournamentLobby.Director;
        if (director == null || !director.IsFriendEvent)
            return;

        bagPounds.Value = director.BagPounds;
        bagFish.Value = director.BagFish;
        WriteLunkers(director);
    }

    void WriteLunkers(TournamentDirector director)
    {
        float lm = 0f;
        float sm = 0f;
        IReadOnlyList<CatchRecord> kept = director.Bag;
        for (int i = 0; i < kept.Count; i++)
        {
            CatchRecord fish = kept[i];
            if (TournamentBag.IsLargemouth(fish))
                lm = Mathf.Max(lm, fish.Pounds);
            else if (TournamentBag.IsSmallmouth(fish))
                sm = Mathf.Max(sm, fish.Pounds);
        }

        lunkerLm.Value = lm;
        lunkerSm.Value = sm;
    }

    void BuildRemoteVisual()
    {
        GameObject local = GameObject.FindGameObjectWithTag("Player");
        if (local != null)
        {
            bodyVisual = VisualCopy.Clone(local, "RemoteAngler");
            bodyVisual.transform.SetParent(transform, false);
        }

        BoatMotor boat = FindBoardableBoat();
        if (boat != null)
        {
            boatVisual = VisualCopy.Clone(boat.gameObject, "RemoteBoat");
            boatVisual.transform.SetParent(transform, false);
        }

        ApplyRemote();
    }

    void ApplyRemote()
    {
        transform.position = position.Value;
        bool inBoat = pose.Value != (byte)Pose.OnFoot;
        if (bodyVisual != null)
        {
            bodyVisual.SetActive(!inBoat);
            if (!inBoat)
                bodyVisual.transform.rotation = Quaternion.Euler(0f, yaw.Value, 0f);
        }

        if (boatVisual != null)
        {
            boatVisual.SetActive(inBoat);
            if (inBoat)
                boatVisual.transform.rotation = Quaternion.Euler(0f, yaw.Value - 180f, 0f);
        }
    }

    static BoatMotor FindBoardableBoat()
    {
        BoatMotor[] boats = FindObjectsByType<BoatMotor>(FindObjectsSortMode.None);
        for (int i = 0; i < boats.Length; i++)
        {
            if (boats[i] != null && boats[i].Boardable && !boats[i].IsAiControlled)
                return boats[i];
        }

        return boats.Length > 0 ? boats[0] : null;
    }
}
