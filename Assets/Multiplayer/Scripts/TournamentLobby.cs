using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// A short-lived friend tournament: join code, see each other, start when
/// everyone is ready, weigh in together, then the lobby dissolves and each
/// player is back on their own lake.
/// </summary>
[DefaultExecutionOrder(-180)]
public class TournamentLobby : MonoBehaviour
{
    public const int MaxAnglers = 4;

    static TournamentLobby instance;

    readonly List<AnglerPresence> anglers = new List<AnglerPresence>();
    const string EventProperty = "event";

    ISession session;
    GameObject networkRoot;
    GameObject presencePrefab;
    readonly List<EntrantRecord> entrants = new List<EntrantRecord>();
    TournamentDefinition invited;
    bool entryPaid;
    bool eventStarted;
    bool busy;
    bool starting;
    bool finishing;
    bool leaving;
    bool cancelRequested;
    float nextHostTick;
    string error = "";
    string joinCode = "";

    public static TournamentLobby Instance => instance;

    public static TournamentDirector Director =>
        FindFirstObjectByType<TournamentDirector>();

    public bool Busy => busy;
    public bool IsActive => session != null;
    public bool IsHost => session != null && session.IsHost;
    public string JoinCode => joinCode;
    public string Error => error;
    public TournamentDefinition Invited => invited;
    public string LocalPlayerId { get; private set; } = "";
    public IReadOnlyList<AnglerPresence> Anglers => anglers;

    public Task EnsureSignedInAsync() => EnsureServices();

    public event Action Changed;
    public event Action<string> Notice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null)
            return;

        var go = new GameObject("TournamentLobby");
        DontDestroyOnLoad(go);
        go.AddComponent<TournamentLobby>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    public string StatusLine
    {
        get
        {
            if (session == null)
                return "";

            TournamentDirector director = Director;
            if (director != null && director.IsFriendEvent && director.Phase == TournamentPhase.Running)
                return director.StatusLine;

            string eventName = invited != null ? invited.DisplayName : "Tournament";
            if (string.IsNullOrEmpty(joinCode))
                return $"{eventName}  ·  connecting";

            int ready = ReadyCount();
            return $"{eventName}  ·  {joinCode}  ·  {ready}/{anglers.Count} ready";
        }
    }

    public bool CanWeighIn
    {
        get
        {
            TournamentDirector director = Director;
            if (director == null || !director.IsFriendEvent || director.Phase != TournamentPhase.Running)
                return false;
            AnglerPresence mine = LocalPresence();
            return mine != null && !mine.Submitted;
        }
    }

    public bool WaitingOnOthers
    {
        get
        {
            AnglerPresence mine = LocalPresence();
            return mine != null && mine.Submitted && !finishing;
        }
    }

    public bool CanCallScales => IsHost && eventStarted && !finishing;

    public async void Host()
    {
        Host(Director != null ? Director.FindDefinition(null) : null);
    }

    public async void Host(TournamentDefinition definition)
    {
        if (busy || session != null)
            return;

        TournamentDirector director = Director;
        if (definition == null)
            definition = director != null ? FirstInvitable(director) : null;
        if (definition == null || director == null || !director.CanInvite(definition))
        {
            error = director != null && definition != null && director.ScheduledToday(definition)
                ? $"{definition.DisplayName} is today. Invite a different field, or wait and fish it in the morning."
                : "Pick a tournament to host.";
            Notice?.Invoke(error);
            Changed?.Invoke();
            return;
        }

        await Run("Couldn't open a lobby.", async () =>
        {
            await EnsureServices();
            EnsureNetwork();
            var options = new SessionOptions
            {
                MaxPlayers = MaxAnglers,
                Name = definition.DisplayName,
                IsPrivate = true,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { EventProperty, new SessionProperty(definition.Id) }
                }
            }.WithRelayNetwork();
            if (MultiplayerService.Instance == null)
                throw new InvalidOperationException("Multiplayer services did not start.");
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            joinCode = session.Code ?? "";
            BindEvent(definition);
            PayEntry(director, definition);
            HookSession();
            Notice?.Invoke(string.IsNullOrEmpty(joinCode)
                ? $"Lobby open for {definition.DisplayName}."
                : $"{definition.DisplayName} lobby. Code {joinCode}.");
        });
    }

    public async void Join(string code)
    {
        if (busy || session != null)
            return;

        string clean = SanitizeCode(code);
        if (clean.Length < 3)
        {
            error = "That code is too short.";
            Changed?.Invoke();
            return;
        }

        await Run("Couldn't join that lobby.", async () =>
        {
            await EnsureServices();
            EnsureNetwork();
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(clean);
            joinCode = session.Code ?? clean;
            TournamentDirector director = Director;
            TournamentDefinition definition = EventFromSession(session, director);
            if (definition == null)
                throw new InvalidOperationException("That lobby has no tournament.");
            if (director == null || !director.CanJoinFriend(definition))
                throw new InvalidOperationException(director != null && !director.MeetsReputation(definition)
                    ? $"Need {definition.ReputationRequired} Reputation for {definition.DisplayName}."
                    : $"Can't enter {definition.DisplayName} right now.");
            BindEvent(definition);
            PayEntry(director, definition);
            HookSession();
            Notice?.Invoke($"Joined {definition.DisplayName}. Ready up when you are.");
        });
    }

    public void SetReady(bool value)
    {
        LocalPresence()?.SetReady(value);
        Changed?.Invoke();
    }

    public void WeighIn()
    {
        LocalPresence()?.Submit(false);
        TournamentDirector director = Director;
        if (director != null && director.IsFriendEvent)
            director.NoticeFriend("Bag is in. Waiting on the others.");
        Changed?.Invoke();
    }

    public void ForfeitAndLeave() => Leave();

    public void CallScales()
    {
        if (!IsHost || finishing || !eventStarted)
            return;

        Notice?.Invoke("Calling scales.");
        FinishNow(forfeitHoldouts: true);
    }

    public async void Leave()
    {
        if (leaving)
            return;

        if (busy && session == null)
        {
            cancelRequested = true;
            return;
        }

        if (eventStarted && !finishing && session != null)
        {
            RequestExitAfterEvent();
            return;
        }

        await CloseLobby();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void Register(AnglerPresence presence)
    {
        if (presence == null || anglers.Contains(presence))
            return;
        anglers.Add(presence);
        Remember(presence);
        Changed?.Invoke();
    }

    public void Unregister(AnglerPresence presence)
    {
        if (presence != null && eventStarted && !presence.Submitted)
            RememberForfeit(presence);
        anglers.Remove(presence);
        Changed?.Invoke();
    }

    public void BeginLocalEvent()
    {
        TournamentDirector director = Director;
        if (director == null)
            return;
        if (!director.StartFriendEvent(invited))
            return;
        eventStarted = true;
        Notice?.Invoke("Lines in. Fish, then weigh in together.");
        Changed?.Invoke();
    }

    public void ApplyFriendBatch(string batchJson)
    {
        TournamentDirector director = Director;
        if (director == null)
            return;

        finishing = true;
        bool applied = false;
        FriendResultBatch batch = JsonUtility.FromJson<FriendResultBatch>(batchJson);
        if (batch?.Entries != null)
        {
            long mine = LocalClientId();
            string mineId = LocalPlayerId;
            for (int i = 0; i < batch.Entries.Length; i++)
            {
                FriendResultEntry entry = batch.Entries[i];
                if (entry == null || !Mine(entry, mine, mineId))
                    continue;

                TournamentResult result = JsonUtility.FromJson<TournamentResult>(entry.ResultJson);
                if (result == null)
                    continue;
                director.ApplyFriendResult(result);
                applied = true;
                break;
            }
        }

        if (!applied)
            ApplyAbandonedResult();

        StartCoroutine(LeaveAfterResults());
        Changed?.Invoke();
    }

    public void HostTick()
    {
        if (!IsHost || Time.time < nextHostTick)
            return;

        nextHostTick = Time.time + 0.25f;
        PruneAnglers();
        SyncEntrants();
        ForfeitMissingAnglers();

        TournamentDirector director = Director;
        if (director != null && director.IsFriendEvent)
        {
            TryFinish();
            return;
        }

        TryStart();
    }

    void TryStart()
    {
        if (starting || anglers.Count < 2)
            return;

        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] == null || !anglers[i].Ready)
                return;
        }

        starting = true;
        HostPresence()?.StartEventClientRpc();
    }

    void TryFinish()
    {
        if (finishing)
            return;

        SyncEntrants();
        if (!AllBagsIn())
            return;

        FinishNow(forfeitHoldouts: false);
    }

    void FinishNow(bool forfeitHoldouts)
    {
        if (finishing)
            return;

        SyncEntrants();
        if (forfeitHoldouts)
        {
            for (int i = 0; i < entrants.Count; i++)
            {
                if (!entrants[i].Submitted)
                    entrants[i].Forfeit();
            }
        }

        List<FriendBag> bags = CollectedBags();
        if (bags.Count == 0)
            return;

        TournamentDirector director = Director;
        if (director == null)
            return;

        var batch = new FriendResultBatch { Entries = new FriendResultEntry[bags.Count] };
        for (int i = 0; i < bags.Count; i++)
        {
            TournamentResult result = director.BuildFriendResult(bags, bags[i].ClientId, bags[i].PlayerId);
            batch.Entries[i] = new FriendResultEntry
            {
                ClientId = bags[i].ClientId,
                PlayerId = bags[i].PlayerId,
                ResultJson = JsonUtility.ToJson(result)
            };
        }

        finishing = true;
        HostPresence()?.FinishEventClientRpc(JsonUtility.ToJson(batch));
    }

    AnglerPresence HostPresence()
    {
        ulong hostId = NetworkManager.Singleton != null ? NetworkManager.ServerClientId : 0;
        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] != null && anglers[i].OwnerClientId == hostId)
                return anglers[i];
        }

        return LocalPresence() ?? (anglers.Count > 0 ? anglers[0] : null);
    }

    AnglerPresence LocalPresence()
    {
        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] != null && anglers[i].IsOwner)
                return anglers[i];
        }

        return null;
    }

    int ReadyCount()
    {
        int n = 0;
        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] != null && anglers[i].Ready)
                n++;
        }

        return n;
    }

    void PruneAnglers()
    {
        for (int i = anglers.Count - 1; i >= 0; i--)
        {
            if (anglers[i] == null)
                anglers.RemoveAt(i);
        }
    }

    IEnumerator LeaveAfterResults()
    {
        yield return new WaitForSeconds(1.2f);
        StartCoroutine(CloseLobbyRoutine());
    }

    IEnumerator CloseLobbyRoutine()
    {
        var close = CloseLobby();
        while (!close.IsCompleted)
            yield return null;
    }

    void RequestExitAfterEvent()
    {
        if (IsHost)
        {
            CallScales();
            return;
        }

        LocalPresence()?.Submit(true);
        Notice?.Invoke("Bag is in. Waiting on the others.");
        Changed?.Invoke();
    }

    async Task CloseLobby()
    {
        if (leaving)
            return;

        bool afterResults = finishing;
        leaving = true;
        starting = false;
        if (!afterResults)
            Director?.CancelFriendEvent();
        await ShutdownSession();
        leaving = false;
        if (!afterResults)
            Notice?.Invoke("Back on your own lake.");
        Changed?.Invoke();
    }

    async Task Run(string failNotice, Func<Task> work)
    {
        busy = true;
        cancelRequested = false;
        error = "";
        Changed?.Invoke();
        try
        {
            await work();
            if (cancelRequested)
                await ShutdownSession();
        }
        catch (Exception e)
        {
            error = string.IsNullOrEmpty(e.Message) ? failNotice : $"{failNotice} {e.Message}";
            Debug.LogWarning($"Tournament lobby: {e}");
            Notice?.Invoke(error);
            await ShutdownSession();
        }

        busy = false;
        Changed?.Invoke();
    }

    async Task EnsureServices()
    {
        if (!IsActive)
            await WiloAccount.SignInAsync();
        else if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        if (AuthenticationService.Instance.IsSignedIn)
            LocalPlayerId = AuthenticationService.Instance.PlayerId;
    }

    void EnsureNetwork()
    {
        if (NetworkManager.Singleton != null)
            return;

        presencePrefab = Resources.Load<GameObject>("AnglerPresence");
        if (presencePrefab == null)
            throw new InvalidOperationException("Missing AnglerPresence prefab.");

        networkRoot = new GameObject("NetworkManager");
        DontDestroyOnLoad(networkRoot);
        var transport = networkRoot.AddComponent<UnityTransport>();
        var network = networkRoot.AddComponent<NetworkManager>();
        if (network.NetworkConfig == null)
            network.NetworkConfig = new NetworkConfig();
        network.NetworkConfig.NetworkTransport = transport;
        network.NetworkConfig.PlayerPrefab = presencePrefab;
        network.NetworkConfig.EnableSceneManagement = false;
        network.OnClientStopped += OnClientStopped;
        network.OnServerStopped += OnServerStopped;
    }

    void HookSession()
    {
        if (session == null)
            return;
        session.Deleted += OnSessionEnded;
        session.RemovedFromSession += OnSessionEnded;
    }

    void UnhookSession(ISession closing)
    {
        if (closing == null)
            return;
        closing.Deleted -= OnSessionEnded;
        closing.RemovedFromSession -= OnSessionEnded;
    }

    void OnSessionEnded()
    {
        if (leaving || finishing)
            return;
        if (eventStarted)
            ApplyAbandonedResult();
        Notice?.Invoke("The lobby closed.");
        StartCoroutine(CloseLobbyRoutine());
    }

    void OnClientStopped(bool _) => OnDropped();

    void OnServerStopped(bool _) => OnDropped();

    void OnDropped()
    {
        if (leaving || finishing)
            return;
        if (session == null)
            return;
        if (eventStarted)
            ApplyAbandonedResult();
        Notice?.Invoke("Lost the lobby.");
        StartCoroutine(CloseLobbyRoutine());
    }

    async Task ShutdownSession()
    {
        if (entryPaid && !eventStarted)
            Director?.RefundEntry(invited);
        entryPaid = false;
        eventStarted = false;
        invited = null;
        anglers.Clear();
        entrants.Clear();
        joinCode = "";
        starting = false;
        finishing = false;
        cancelRequested = false;

        ISession closing = session;
        session = null;
        UnhookSession(closing);
        if (closing != null)
        {
            try
            {
                await closing.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Tournament lobby: {e.Message}");
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        if (networkRoot != null)
        {
            Destroy(networkRoot);
            networkRoot = null;
        }
    }

    void BindEvent(TournamentDefinition definition)
    {
        invited = definition;
    }

    void PayEntry(TournamentDirector director, TournamentDefinition definition)
    {
        if (entryPaid || director == null || definition == null)
            return;
        if (director.HasUpcomingEntry(definition))
            return;
        if (!director.TryPayEntry(definition))
            throw new InvalidOperationException($"Need ${definition.EntryFee} to enter {definition.DisplayName}.");
        entryPaid = true;
        if (definition.EntryFee > 0)
            SaveService.Instance?.Save();
    }

    void SyncEntrants()
    {
        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] != null)
                Remember(anglers[i]);
        }
    }

    void Remember(AnglerPresence presence)
    {
        if (presence == null)
            return;

        EntrantRecord row = FindEntrant((long)presence.OwnerClientId);
        if (row == null)
        {
            row = new EntrantRecord();
            entrants.Add(row);
        }

        row.CopyFrom(presence);
    }

    void RememberForfeit(AnglerPresence presence)
    {
        Remember(presence);
        EntrantRecord row = FindEntrant((long)presence.OwnerClientId);
        row?.Forfeit();
    }

    EntrantRecord FindEntrant(long clientId)
    {
        for (int i = 0; i < entrants.Count; i++)
        {
            if (entrants[i].ClientId == clientId)
                return entrants[i];
        }

        return null;
    }

    bool AllBagsIn()
    {
        if (entrants.Count == 0)
            return false;
        for (int i = 0; i < entrants.Count; i++)
        {
            if (!entrants[i].Submitted)
                return false;
        }

        return true;
    }

    List<FriendBag> CollectedBags()
    {
        var bags = new List<FriendBag>();
        for (int i = 0; i < entrants.Count; i++)
            bags.Add(entrants[i].Bag);
        return bags;
    }

    void ApplyAbandonedResult()
    {
        TournamentDirector director = Director;
        if (director == null || !director.IsFriendEvent)
            return;

        finishing = true;
        var bags = new List<FriendBag> { LocalAbandonBag() };
        TournamentResult result = director.BuildFriendResult(bags, LocalClientId(), LocalPlayerId);
        if (result != null)
            director.ApplyFriendResult(result);
    }

    FriendBag LocalAbandonBag()
    {
        AnglerPresence mine = LocalPresence();
        if (mine != null)
        {
            FriendBag bag = mine.Bag;
            if (mine.Submitted)
                return bag;

            bag.Forfeited = true;
            bag.Pounds = 0f;
            bag.Fish = 0;
            bag.LunkerLargemouth = 0f;
            bag.LunkerSmallmouth = 0f;
            return bag;
        }

        return new FriendBag
        {
            ClientId = LocalClientId(),
            PlayerId = LocalPlayerId,
            Name = "You",
            Forfeited = true
        };
    }

    static bool Mine(FriendResultEntry entry, long clientId, string playerId)
    {
        if (entry.ClientId == clientId)
            return true;
        return !string.IsNullOrEmpty(playerId) && entry.PlayerId == playerId;
    }

    static long LocalClientId()
    {
        if (NetworkManager.Singleton == null)
            return 0;
        return (long)NetworkManager.Singleton.LocalClientId;
    }

    static TournamentDefinition EventFromSession(ISession joined, TournamentDirector director)
    {
        if (joined?.Properties == null || director == null)
            return null;
        if (!joined.Properties.TryGetValue(EventProperty, out SessionProperty property) || property == null)
            return null;
        return director.FindDefinition(property.Value);
    }

    static TournamentDefinition FirstInvitable(TournamentDirector director)
    {
        IReadOnlyList<TournamentDefinition> list = director.Definitions;
        for (int i = 0; i < list.Count; i++)
        {
            if (director.CanInvite(list[i]))
                return list[i];
        }

        return null;
    }

    void ForfeitMissingAnglers()
    {
        for (int i = 0; i < entrants.Count; i++)
        {
            if (entrants[i].Submitted)
                continue;
            if (!AnglerStillHere(entrants[i].ClientId))
                entrants[i].Forfeit();
        }
    }

    bool AnglerStillHere(long clientId)
    {
        for (int i = 0; i < anglers.Count; i++)
        {
            if (anglers[i] != null && (long)anglers[i].OwnerClientId == clientId)
                return true;
        }

        return false;
    }

    static string SanitizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        return code.Trim().Replace(" ", "").ToUpperInvariant();
    }
}

class EntrantRecord
{
    public long ClientId;
    public bool Submitted;
    public FriendBag Bag;

    public void CopyFrom(AnglerPresence presence)
    {
        ClientId = (long)presence.OwnerClientId;
        Bag = presence.Bag;
        if (presence.Submitted || presence.Forfeited)
            Submitted = true;
    }

    public void Forfeit()
    {
        Submitted = true;
        Bag.ClientId = ClientId;
        Bag.Forfeited = true;
        Bag.Pounds = 0f;
        Bag.Fish = 0;
        Bag.LunkerLargemouth = 0f;
        Bag.LunkerSmallmouth = 0f;
    }
}
