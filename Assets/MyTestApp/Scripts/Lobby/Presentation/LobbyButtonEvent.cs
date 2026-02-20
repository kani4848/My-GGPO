using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

public static class LobbyButtonEvent
{
    public static event Action QuickMatchEvent;
    public static void RaiseQuickMatch() => QuickMatchEvent?.Invoke();

    public static event Action<string> CreateLobbyEvent;
    public static void RaiseCreateLobby(string path) => CreateLobbyEvent?.Invoke(path);

    public static event Action<string> SearchLobbyEvent;
    public static void RaiseSearchLobby(string path) => SearchLobbyEvent?.Invoke(path);

    public static event Action GoTitleEvent;
    public static void RaiseGoTitle() => GoTitleEvent?.Invoke();
}
