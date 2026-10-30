using System.Windows;

namespace HS2Tools.ViewModels;

/// <summary>封送回调到应用 UI 线程；无应用或普通测试宿主中直接执行</summary>
internal static class UiDispatch
{
    public static void Run(Action action)
    {
        var dispatcher = (Application.Current as App)?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }
}
