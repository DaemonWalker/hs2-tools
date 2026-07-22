using System.Windows;

namespace HS2Tools.ViewModels;

/// <summary>封送回调到 UI 线程；无 Application 的测试环境直接执行</summary>
internal static class UiDispatch
{
    public static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }
}
