using System;

public static class MainGameEvent
{
    public static event Action SignalEvent;
    public static void RaiseSignal() => SignalEvent?.Invoke();

    public static event Action QuickMatchCancelEvent;
    public static void RaiseQuickMatchCancel() => QuickMatchCancelEvent?.Invoke();

}
