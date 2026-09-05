using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// One Unity Auth profile per lake slot, so two cabins on this device are
/// two leaderboard players. Editor also keys the process so two Play windows
/// can still share a friend lobby.
/// </summary>
public static class WiloAccount
{
    static string boundSlot = "";

    public static string SlotId()
    {
        SaveService save = SaveService.Instance;
        if (save == null)
            return "";
        if (!string.IsNullOrEmpty(save.CurrentSlotId))
            return save.CurrentSlotId;
        return save.Player != null ? save.Player.playerId : "";
    }

    public static string ProfileName() => ProfileNameFor(SlotId());

    public static string ProfileNameFor(string slotId)
    {
        string key = SlotKey(slotId);
        if (Application.isEditor)
        {
            string pid = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            if (pid.Length > 6)
                pid = pid.Substring(pid.Length - 6);
            return Clip(string.IsNullOrEmpty(key) ? "wilo" + pid : "s" + key + pid);
        }

        return string.IsNullOrEmpty(key) ? "wilo" : Clip("s" + key);
    }

    /// <summary>
    /// Drops this lake's anonymous Unity account if this device still has its
    /// session. Leaderboard rows need an admin purge; the player SDK cannot.
    /// </summary>
    public static async void ForgetSlotLater(string slotId)
    {
        try
        {
            await ForgetSlotAsync(slotId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not forget Unity account. {e.Message}");
        }
    }

    public static async Task ForgetSlotAsync(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return;

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        string profile = ProfileNameFor(slotId);
        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn)
            auth.SignOut();
        if (auth.Profile != profile)
            auth.SwitchProfile(profile);
        if (!auth.SessionTokenExists)
        {
            boundSlot = "";
            return;
        }

        await auth.SignInAnonymouslyAsync();
        await auth.DeleteAccountAsync();
        if (auth.IsSignedIn)
            auth.SignOut(true);
        boundSlot = "";
    }

    public static async Task SignInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        string slot = SlotId();
        string profile = ProfileName();
        var auth = AuthenticationService.Instance;
        bool wrongSlot = slot.Length > 0 && boundSlot.Length > 0 && boundSlot != slot;
        bool wrongProfile = auth.IsSignedIn && auth.Profile != profile;
        if (auth.IsSignedIn && (wrongSlot || wrongProfile))
            auth.SignOut();

        if (!auth.IsSignedIn)
        {
            if (auth.Profile != profile)
                auth.SwitchProfile(profile);
            await auth.SignInAnonymouslyAsync();
        }

        boundSlot = slot;
    }

    static string SlotKey(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return "";
        return slotId.Length <= 16 ? slotId : slotId.Substring(0, 16);
    }

    static string Clip(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "wilo";
        return value.Length <= 30 ? value : value.Substring(0, 30);
    }
}
