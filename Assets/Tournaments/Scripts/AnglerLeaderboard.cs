using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

public enum LeaderboardKind
{
    Wins,
    Reputation,
    LunkerLargemouth,
    LunkerSmallmouth
}

public readonly struct LeaderboardLine
{
    public readonly int Place;
    public readonly string Name;
    public readonly string ScoreLabel;
    public readonly bool IsLocal;

    public LeaderboardLine(int place, string name, string scoreLabel, bool isLocal)
    {
        Place = place;
        Name = name;
        ScoreLabel = scoreLabel;
        IsLocal = isLocal;
    }
}

public readonly struct LeaderboardPage
{
    public readonly IReadOnlyList<LeaderboardLine> Rows;
    public readonly string Footer;
    public readonly string Error;

    public LeaderboardPage(IReadOnlyList<LeaderboardLine> rows, string footer, string error)
    {
        Rows = rows ?? Array.Empty<LeaderboardLine>();
        Footer = footer ?? "";
        Error = error ?? "";
    }

    public bool Ok => Error.Length == 0;
}

/// <summary>
/// Career ranks on Unity Leaderboards: wins, reputation, and heaviest bass
/// of each species. Boards are hosted; this only submits and reads top 10.
/// </summary>
public static class AnglerLeaderboard
{
    public const int TopCount = 10;

    public const string WinsId = "wilo-wins";
    public const string ReputationId = "wilo-reputation";
    public const string LunkerLmId = "wilo-lunker-largemouth";
    public const string LunkerSmId = "wilo-lunker-smallmouth";

    static int lastWins = -1;
    static int lastReputation = -1;
    static float lastLargemouth = -1f;
    static float lastSmallmouth = -1f;
    static string lastPublishedName = "";
    static string lastPlayerId = "";

    [Serializable]
    class ScoreMeta
    {
        public string name;
        public string id;
    }

    struct Draft
    {
        public string Name;
        public string SaveId;
        public double Score;
        public bool Local;
    }

    public static string BoardId(LeaderboardKind kind)
    {
        switch (kind)
        {
            case LeaderboardKind.Wins: return WinsId;
            case LeaderboardKind.Reputation: return ReputationId;
            case LeaderboardKind.LunkerLargemouth: return LunkerLmId;
            default: return LunkerSmId;
        }
    }

    public static async void PublishLater(PlayerProgress progress, IReadOnlyList<TournamentResult> history)
    {
        try
        {
            await PublishAsync(progress, history);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard publish: {e.Message}");
        }
    }

    public static async Task PublishAsync(PlayerProgress progress, IReadOnlyList<TournamentResult> history)
    {
        await EnsureAsync();
        RememberPlayer();

        int wins = CountWins(history);
        int reputation = progress != null ? progress.Reputation : 0;
        float largemouth = BestOf(progress, TournamentBag.IsLargemouth);
        float smallmouth = BestOf(progress, TournamentBag.IsSmallmouth);
        if (wins > 0 || reputation > 0 || largemouth > 0.01f || smallmouth > 0.01f)
            await SyncName(progress);

        string publishedName = progress != null && progress.HasName ? progress.DisplayName : "";
        if (publishedName != lastPublishedName)
        {
            lastWins = -1;
            lastReputation = -1;
            lastLargemouth = -1f;
            lastSmallmouth = -1f;
            lastPublishedName = publishedName;
        }

        object meta = NameMeta(progress);

        if (wins > 0 && wins != lastWins)
        {
            await AddScore(WinsId, wins, meta);
            lastWins = wins;
        }

        if (reputation > 0 && reputation != lastReputation)
        {
            await AddScore(ReputationId, reputation, meta);
            lastReputation = reputation;
        }

        if (largemouth > 0.01f && !Mathf.Approximately(largemouth, lastLargemouth))
        {
            await AddScore(LunkerLmId, largemouth, meta);
            lastLargemouth = largemouth;
        }

        if (smallmouth > 0.01f && !Mathf.Approximately(smallmouth, lastSmallmouth))
        {
            await AddScore(LunkerSmId, smallmouth, meta);
            lastSmallmouth = smallmouth;
        }
    }

    public static double ScoreOf(LeaderboardKind kind, PlayerProgress progress, IReadOnlyList<TournamentResult> history)
    {
        switch (kind)
        {
            case LeaderboardKind.Wins: return CountWins(history);
            case LeaderboardKind.Reputation: return progress != null ? progress.Reputation : 0;
            case LeaderboardKind.LunkerLargemouth: return BestOf(progress, TournamentBag.IsLargemouth);
            default: return BestOf(progress, TournamentBag.IsSmallmouth);
        }
    }

    public static async Task<LeaderboardPage> FetchAsync(
        LeaderboardKind kind,
        string localName = "",
        double localScore = 0)
    {
        try
        {
            await EnsureAsync();
            RememberPlayer();
            string id = BoardId(kind);
            LeaderboardScoresPage page = await LeaderboardsService.Instance.GetScoresAsync(
                id,
                new GetScoresOptions { Limit = 25, IncludeMetadata = true });

            string localId = AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : "";
            string saveId = SavePlayerId();
            bool hasLocalScore = HasScore(kind, localScore);
            var drafts = new List<Draft>();
            List<LeaderboardEntry> results = page != null ? page.Results : null;
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    LeaderboardEntry entry = results[i];
                    if (entry == null)
                        continue;

                    string entrySave = ReadJsonField(entry.Metadata, "id");
                    bool mine = (localId.Length > 0 && entry.PlayerId == localId)
                        || (saveId.Length > 0 && entrySave == saveId);
                    bool local = mine && hasLocalScore;
                    string name = ReadName(entry, local ? localName : "");
                    if (!hasLocalScore && (mine || SameName(name, localName)))
                        continue;

                    drafts.Add(new Draft
                    {
                        Name = name,
                        SaveId = entrySave,
                        Score = entry.Score,
                        Local = local
                    });
                }
            }

            List<LeaderboardLine> rows = Compact(drafts, kind);
            bool listed = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsLocal)
                    listed = true;
            }

            string footer = "";
            if (!listed && hasLocalScore && localId.Length > 0)
            {
                LeaderboardEntry mine = await TryPlayerScore(id);
                if (mine != null)
                {
                    int place = mine.Rank > 0 ? mine.Rank : 0;
                    footer = place > 0
                        ? $"You're {TournamentResult.Ordinal(place)}  ·  {FormatScore(kind, mine.Score)}"
                        : FormatScore(kind, mine.Score);
                }
            }

            return new LeaderboardPage(rows, footer, "");
        }
        catch (LeaderboardsException e)
        {
            return new LeaderboardPage(null, "", FriendlyError(e));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard fetch: {e.Message}");
            return new LeaderboardPage(null, "", "Leaderboard isn't available just now.");
        }
    }

    static void RememberPlayer()
    {
        string id = AuthenticationService.Instance.IsSignedIn
            ? AuthenticationService.Instance.PlayerId
            : "";
        if (id == lastPlayerId)
            return;

        lastPlayerId = id;
        lastWins = -1;
        lastReputation = -1;
        lastLargemouth = -1f;
        lastSmallmouth = -1f;
        lastPublishedName = "";
    }

    static async Task EnsureAsync()
    {
        await WiloAccount.SignInAsync();
    }

    static async Task SyncName(PlayerProgress progress)
    {
        if (progress == null || !progress.HasName)
            return;

        string want = CloudName(progress.DisplayName);
        string have = NameStem(AuthenticationService.Instance.PlayerName);
        if (string.Equals(have, want, StringComparison.Ordinal))
            return;

        try
        {
            await AuthenticationService.Instance.UpdatePlayerNameAsync(want);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard name: {e.Message}");
        }
    }

    static async Task AddScore(string id, double score, object meta)
    {
        await LeaderboardsService.Instance.AddPlayerScoreAsync(
            id,
            score,
            new AddPlayerScoreOptions { Metadata = meta });
    }

    static async Task<LeaderboardEntry> TryPlayerScore(string id)
    {
        try
        {
            return await LeaderboardsService.Instance.GetPlayerScoreAsync(
                id,
                new GetPlayerScoreOptions { IncludeMetadata = true });
        }
        catch (LeaderboardsException)
        {
            return null;
        }
    }

    static object NameMeta(PlayerProgress progress)
    {
        string name = progress != null && progress.HasName ? progress.DisplayName : "";
        return new ScoreMeta { name = name, id = SavePlayerId() };
    }

    static string SavePlayerId()
    {
        SaveService save = SaveService.Instance;
        if (save == null || save.Player == null)
            return "";
        return save.Player.playerId ?? "";
    }

    static List<LeaderboardLine> Compact(List<Draft> drafts, LeaderboardKind kind)
    {
        var best = new List<Draft>();
        for (int i = 0; i < drafts.Count; i++)
        {
            Draft row = drafts[i];
            int found = -1;
            for (int j = 0; j < best.Count; j++)
            {
                if (SameAngler(best[j], row))
                {
                    found = j;
                    break;
                }
            }

            if (found < 0)
            {
                best.Add(row);
                continue;
            }

            if (Better(row, best[found]))
                best[found] = row;
        }

        best.Sort((a, b) => b.Score.CompareTo(a.Score));
        var rows = new List<LeaderboardLine>();
        int take = Mathf.Min(TopCount, best.Count);
        for (int i = 0; i < take; i++)
            rows.Add(new LeaderboardLine(i + 1, best[i].Name, FormatScore(kind, best[i].Score), best[i].Local));
        return rows;
    }

    static bool SameAngler(Draft a, Draft b)
    {
        if (a.SaveId.Length > 0 && a.SaveId == b.SaveId)
            return true;
        return SameName(a.Name, b.Name);
    }

    static bool Better(Draft a, Draft b)
    {
        if (a.Local != b.Local)
            return a.Local;
        return a.Score > b.Score;
    }

    static string ReadName(LeaderboardEntry entry, string localName)
    {
        if (!string.IsNullOrWhiteSpace(localName))
            return localName.Trim();

        string fromMeta = MetaName(entry != null ? entry.Metadata : null);
        if (IsRealName(fromMeta))
            return fromMeta;
        string fromPlayer = NameStem(entry != null ? entry.PlayerName : "");
        if (IsRealName(fromPlayer))
            return fromPlayer;
        return "Angler";
    }

    static bool HasScore(LeaderboardKind kind, double score)
    {
        if (kind == LeaderboardKind.Wins || kind == LeaderboardKind.Reputation)
            return score >= 1;
        return score > 0.01;
    }

    static bool SameName(string a, string b)
    {
        return !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static bool IsRealName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "Angler", StringComparison.OrdinalIgnoreCase);
    }

    static string MetaName(object metadata)
    {
        if (metadata == null)
            return "";

        if (metadata is ScoreMeta typed && !string.IsNullOrEmpty(typed.name))
            return typed.name.Trim();

        string json = metadata as string;
        if (string.IsNullOrEmpty(json) && metadata != null)
            json = metadata.ToString();
        return ReadJsonField(json, "name");
    }

    static string ReadJsonField(object metadata, string field)
    {
        if (metadata == null)
            return "";
        if (metadata is ScoreMeta typed)
        {
            if (field == "name")
                return typed.name ?? "";
            if (field == "id")
                return typed.id ?? "";
        }

        string json = metadata as string;
        if (string.IsNullOrEmpty(json))
            json = metadata.ToString();
        return ReadJsonField(json, field);
    }

    static string ReadJsonField(string json, string field)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
            return "";

        string key = "\"" + field + "\"";
        int at = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return "";

        int colon = json.IndexOf(':', at + key.Length);
        if (colon < 0)
            return "";

        int i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
            i++;
        if (i >= json.Length || json[i] != '"')
            return "";

        i++;
        int start = i;
        while (i < json.Length && json[i] != '"')
        {
            if (json[i] == '\\' && i + 1 < json.Length)
                i += 2;
            else
                i++;
        }

        return i > start ? json.Substring(start, i - start).Trim() : "";
    }

    static string FormatScore(LeaderboardKind kind, double score)
    {
        switch (kind)
        {
            case LeaderboardKind.Wins:
                int wins = Mathf.Max(0, Mathf.RoundToInt((float)score));
                return wins == 1 ? "1 win" : $"{wins} wins";
            case LeaderboardKind.Reputation:
                return $"{Mathf.Max(0, Mathf.RoundToInt((float)score))} Rep";
            default:
                return $"{score:0.00} lb";
        }
    }

    static string FriendlyError(LeaderboardsException error)
    {
        string message = error != null ? error.Message : "";
        if (!string.IsNullOrEmpty(message) &&
            (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
             || message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0))
            return "Leaderboard isn't set up yet.";
        return "Leaderboard isn't available just now.";
    }

    static int CountWins(IReadOnlyList<TournamentResult> history)
    {
        if (history == null)
            return 0;

        int wins = 0;
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i] != null && history[i].Won)
                wins++;
        }

        return wins;
    }

    static float BestOf(PlayerProgress progress, Func<CatchRecord, bool> match)
    {
        if (progress == null)
            return 0f;

        IReadOnlyList<CatchRecord> catches = progress.Catches;
        float best = 0f;
        for (int i = 0; i < catches.Count; i++)
        {
            CatchRecord record = catches[i];
            if (record == null || !match(record))
                continue;
            if (record.Pounds > best)
                best = record.Pounds;
        }

        return best;
    }

    static string CloudName(string display)
    {
        if (string.IsNullOrEmpty(display))
            return "Angler";

        var chars = new char[Mathf.Min(display.Length, 50)];
        int n = 0;
        for (int i = 0; i < display.Length && n < chars.Length; i++)
        {
            char c = display[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '@')
                chars[n++] = c;
            else if (char.IsWhiteSpace(c) && n > 0 && chars[n - 1] != '_')
                chars[n++] = '_';
        }

        return n == 0 ? "Angler" : new string(chars, 0, n);
    }

    static string NameStem(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
            return "";

        int hash = playerName.LastIndexOf('#');
        return hash > 0 ? playerName.Substring(0, hash) : playerName;
    }
}
